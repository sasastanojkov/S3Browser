using System.Data;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using DuckDB.NET.Data;
using S3Browser.Services;

namespace S3Browser
{
    /// <summary>
    /// Singleton manager for DuckDB connections with S3 support.
    /// Manages connection lifecycle, S3 credential configuration, and query execution.
    /// All DuckDB interactions should go through this manager.
    /// </summary>
    public sealed class DuckDbManager
    {
        private static readonly Lazy<DuckDbManager> _lazyInstance = new(() => new DuckDbManager());
        private readonly object _lock = new();
        private readonly List<DuckDBConnection> _connections = new();

        private DuckDbManager()
        {
        }

        /// <summary>
        /// Gets the singleton instance of the DuckDbManager.
        /// </summary>
        public static DuckDbManager Instance => _lazyInstance.Value;

        /// <summary>
        /// Creates a new DuckDB connection for concurrent operations.
        /// Each viewer window should create its own connection to allow parallel queries.
        /// The connection should be disposed when the window is closed.
        /// </summary>
        /// <returns>A new, opened DuckDB connection.</returns>
        public DuckDBConnection CreateConnection()
        {
            lock (_lock)
            {
                var connection = new DuckDBConnection("Data Source=:memory:");
                connection.Open();
                _connections.Add(connection);
                return connection;
            }
        }

        /// <summary>
        /// Creates a new DuckDB connection with S3 access configured using AWS credentials.
        /// Installs and loads the httpfs extension and configures S3 credentials.
        /// </summary>
        /// <param name="credentials">AWS immutable credentials containing access key, secret key, and optional session token. Pass null for anonymous access to public buckets.</param>
        /// <param name="region">AWS region for S3 access (e.g., "us-east-1").</param>
        /// <returns>A new, configured DuckDB connection with S3 access enabled.</returns>
        /// <exception cref="InvalidOperationException">Thrown if connection configuration fails.</exception>
        public DuckDBConnection CreateConnectionWithS3Access(ImmutableCredentials? credentials, string region)
        {
            lock (_lock)
            {
                var connection = new DuckDBConnection("Data Source=:memory:");
                connection.Open();

                try
                {
                    // Install and load httpfs extension for S3 access
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "INSTALL httpfs; LOAD httpfs;";
                        cmd.ExecuteNonQuery();
                    }

                    // Configure S3 region and credentials (if provided)
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = $"SET s3_region='{region}';";

                        // Only configure credentials if provided (authenticated access)
                        if (credentials != null)
                        {
                            cmd.CommandText += $@"
                            SET s3_access_key_id='{credentials.AccessKey}';
                            SET s3_secret_access_key='{credentials.SecretKey}';";

                            // Add session token if present (for temporary credentials like SSO)
                            if (!string.IsNullOrEmpty(credentials.Token))
                            {
                                cmd.CommandText += $"SET s3_session_token='{credentials.Token}';";
                            }
                        }

                        cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    connection.Close();
                    connection.Dispose();
                    string accessType = credentials != null ? "authenticated" : "anonymous";
                    throw new InvalidOperationException($"Failed to configure DuckDB for {accessType} S3 access: {ex.Message}", ex);
                }

                _connections.Add(connection);
                return connection;
            }
        }

        /// <summary>
        /// Creates a new DuckDB connection with anonymous S3 access for public buckets.
        /// This is a convenience method that calls <see cref="CreateConnectionWithS3Access"/> with null credentials.
        /// </summary>
        /// <param name="region">AWS region for S3 access (e.g., "us-west-2").</param>
        /// <returns>A new, configured DuckDB connection with anonymous S3 access enabled.</returns>
        /// <exception cref="InvalidOperationException">Thrown if connection configuration fails.</exception>
        public DuckDBConnection CreateConnectionWithAnonymousS3Access(string region)
        {
            return CreateConnectionWithS3Access(null, region);
        }

        /// <summary>
        /// Creates a DuckDB connection with S3 access configured based on bucket accessibility.
        /// Automatically determines if the bucket is public and configures credentials accordingly.
        /// </summary>
        /// <param name="bucketName">Name of the S3 bucket to access.</param>
        /// <returns>A configured DuckDB connection with appropriate S3 access.</returns>
        /// <exception cref="InvalidOperationException">Thrown if connection setup fails.</exception>
        public async Task<DuckDBConnection> CreateConnectionForBucketAsync(string bucketName)
        {
            // Get S3Client from S3Manager
            var s3Client = await S3Manager.Instance.GetS3ClientForBucketAsync(bucketName);
            if (s3Client == null)
            {
                throw new InvalidOperationException($"Unable to access bucket '{bucketName}'.");
            }

            var region = s3Client.Config.RegionEndpoint?.SystemName ?? "us-east-1";

            // Check if this is a public bucket using S3Manager
            bool isPublicBucket = S3Manager.Instance.IsPublicBucket(bucketName);

            if (isPublicBucket)
            {
                // Create connection for anonymous/public S3 access (no credentials)
                return await Task.Run(() => CreateConnectionWithAnonymousS3Access(region));
            }
            else
            {
                // Get AWS credentials using S3Manager's profile
                var chain = new CredentialProfileStoreChain();
                AWSCredentials? awsCredentials = null;

                var awsProfile = S3Manager.Instance.GetAwsProfile();
                if (!string.IsNullOrEmpty(awsProfile))
                {
                    if (!chain.TryGetAWSCredentials(awsProfile, out awsCredentials))
                    {
                        throw new InvalidOperationException($"Unable to retrieve AWS credentials for profile '{awsProfile}'.");
                    }
                }
                else
                {
                    // Fallback: try to get credentials from default profile
                    if (!chain.TryGetAWSCredentials(null, out awsCredentials))
                    {
                        throw new InvalidOperationException("Unable to retrieve AWS credentials from default sources.");
                    }
                }

                if (awsCredentials == null)
                {
                    throw new InvalidOperationException("Unable to retrieve AWS credentials.");
                }

                var immutableCredentials = await awsCredentials.GetCredentialsAsync();

                // Create connection with S3 credentials for authenticated access
                return await Task.Run(() => CreateConnectionWithS3Access(immutableCredentials, region));
            }
        }

        /// <summary>
        /// Executes a SQL query and returns results as a DataTable.
        /// </summary>
        /// <param name="connection">The DuckDB connection to use for the query.</param>
        /// <param name="query">The SQL query to execute.</param>
        /// <param name="cancellationToken">Cancellation token for query execution.</param>
        /// <returns>A DataTable containing the query results.</returns>
        /// <exception cref="InvalidOperationException">Thrown if query execution fails.</exception>
        public DataTable ExecuteQuery(DuckDBConnection connection, string query, CancellationToken cancellationToken)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            var dataTable = new DataTable();
            var streamColumns = new HashSet<int>();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = query;

                    cancellationToken.ThrowIfCancellationRequested();

                    using (var reader = command.ExecuteReader())
                    {
                        // Check for cancellation periodically while loading data
                        while (reader.Read())
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (dataTable.Columns.Count == 0)
                            {
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    var fieldType = reader.GetFieldType(i);

                                    // Track which columns are UnmanagedMemoryStream
                                    if (typeof(System.IO.Stream).IsAssignableFrom(fieldType))
                                    {
                                        streamColumns.Add(i);
                                        // Store as object to hold byte arrays
                                        dataTable.Columns.Add(reader.GetName(i), typeof(byte[]));
                                    }
                                    else
                                    {
                                        // Store as object to preserve complex types (List, Dictionary, etc.)
                                        dataTable.Columns.Add(reader.GetName(i), typeof(object));
                                    }
                                }
                            }

                            var row = dataTable.NewRow();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                if (streamColumns.Contains(i))
                                {
                                    // Convert stream to byte array immediately for stream columns
                                    var value = reader.GetValue(i);
                                    if (value is System.IO.UnmanagedMemoryStream stream)
                                    {
                                        row[i] = ReadStreamToBytes(stream);
                                    }
                                    else if (value == DBNull.Value || value == null)
                                    {
                                        row[i] = DBNull.Value;
                                    }
                                    else
                                    {
                                        // Unexpected type in stream column, convert to string
                                        row[i] = System.Text.Encoding.UTF8.GetBytes(value.ToString() ?? "");
                                    }
                                }
                                else
                                {
                                    // Get the raw value from DuckDB without converting to string
                                    // This preserves complex types like List<>, Dictionary<>, etc.
                                    var value = reader.GetValue(i);
                                    row[i] = value == DBNull.Value ? DBNull.Value : value;
                                }
                            }
                            dataTable.Rows.Add(row);
                        }
                    }
                }

                return dataTable;
            }
            catch (OperationCanceledException)
            {
                throw; // Re-throw to be handled by caller
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Query execution failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Executes a non-query command (e.g., CREATE TABLE).
        /// </summary>
        /// <param name="connection">The DuckDB connection to use.</param>
        /// <param name="commandText">The SQL command to execute.</param>
        /// <param name="cancellationToken">Cancellation token for command execution.</param>
        /// <exception cref="InvalidOperationException">Thrown if command execution fails.</exception>
        public void ExecuteNonQuery(DuckDBConnection connection, string commandText, CancellationToken cancellationToken)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = commandText;
                    command.ExecuteNonQuery();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Command execution failed: {ex.Message}", ex);
            }
        }

        private byte[] ReadStreamToBytes(System.IO.UnmanagedMemoryStream stream)
        {
            // Reset stream position to beginning
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            byte[] buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);

            return buffer;
        }

        /// <summary>
        /// Releases a connection and removes it from tracking.
        /// Closes and disposes the connection safely, ignoring any disposal errors.
        /// </summary>
        /// <param name="connection">The connection to release. Can be null.</param>
        public void ReleaseConnection(DuckDBConnection? connection)
        {
            lock (_lock)
            {
                if (connection is not null)
                {
                    _connections.Remove(connection);
                    try
                    {
                        connection.Close();
                        connection.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                }
            }
        }

        /// <summary>
        /// Disposes all tracked connections.
        /// Should be called when shutting down the application.
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var connection in _connections.ToList())
                {
                    try
                    {
                        connection.Close();
                        connection.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                }
                _connections.Clear();
            }
        }
    }
}
