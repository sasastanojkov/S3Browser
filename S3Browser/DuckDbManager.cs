using Amazon.Runtime;
using DuckDB.NET.Data;

namespace S3Browser
{
    /// <summary>
    /// Singleton manager for DuckDB connections with S3 support.
    /// Manages connection lifecycle and S3 credential configuration.
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
        /// <param name="credentials">AWS immutable credentials containing access key, secret key, and optional session token.</param>
        /// <param name="region">AWS region for S3 access (e.g., "us-east-1").</param>
        /// <returns>A new, configured DuckDB connection with S3 access enabled.</returns>
        /// <exception cref="InvalidOperationException">Thrown if connection configuration fails.</exception>
        public DuckDBConnection CreateConnectionWithS3Access(ImmutableCredentials credentials, string region)
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

                    // Configure S3 credentials
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = $@"
                            SET s3_region='{region}';
                            SET s3_access_key_id='{credentials.AccessKey}';
                            SET s3_secret_access_key='{credentials.SecretKey}';
                        ";

                        // Add session token if present (for temporary credentials like SSO)
                        if (!string.IsNullOrEmpty(credentials.Token))
                        {
                            cmd.CommandText += $"SET s3_session_token='{credentials.Token}';";
                        }

                        cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    connection.Close();
                    connection.Dispose();
                    throw new InvalidOperationException($"Failed to configure DuckDB for S3 access: {ex.Message}", ex);
                }

                _connections.Add(connection);
                return connection;
            }
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
