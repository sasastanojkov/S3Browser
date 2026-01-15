using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DuckDB.NET.Data;
using Microsoft.Win32;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using S3Browser.Services;

namespace S3Browser
{
    /// <summary>
    /// Converter for truncating text in Parquet viewer cells.
    /// </summary>
    public class SmartTruncateTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || value == DBNull.Value)
                return "";

            string text = value.ToString() ?? "";

            // If text has multiple lines, show only first line
            if (text.Contains('\n'))
            {
                var firstLine = text.Split('\n')[0];

                // If first line is longer than 50 chars, truncate it
                if (firstLine.Length > 50)
                    return firstLine.Substring(0, 50);
                return firstLine;
            }

            // If text is longer than 50 characters, truncate
            if (text.Length > 50)
                return text.Substring(0, 50);

            // Otherwise show full text
            return text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter that determines if text needs expansion in Parquet viewer.
    /// </summary>
    public class NeedsExpansionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || value == DBNull.Value)
                return Visibility.Collapsed;

            string text = value.ToString() ?? "";

            // Show button if text is longer than 50 chars OR has multiple lines
            if (text.Length > 50 || text.Contains('\n'))
                return Visibility.Visible;

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Window for viewing Parquet files from S3 using DuckDB engine.
    /// Supports single file and wildcard (folder) modes with geometry visualization.
    /// </summary>
    public partial class ParquetViewerWindow : Window
    {
        private readonly string _bucketName;
        private readonly string _key;
        private readonly string _fileName;
        private readonly bool _isWildcard;
        private string? _customQuery;
        private string? _lastExecutedQuery;
        private readonly bool _loadAsTable;
        private readonly string _tableName = "parquet_data";
        private DuckDBConnection? _duckDbConnection;
        private CancellationTokenSource? _cancellationTokenSource;
        private Dictionary<int, List<GeometryMapWindow.GeometryInfo>> _rowGeometries = new(); // Map row index to geometries with column info
        private GeometryMapWindow? _currentMapWindow;
        private int _lastSelectedRowIndex = -1; // Track the last selected row for toggle behavior

        /// <summary>
        /// Initializes a new instance of the <see cref="ParquetViewerWindow"/> class.
        /// </summary>
        /// <param name="bucketName">Name of the S3 bucket containing the file(s).</param>
        /// <param name="key">S3 key (path) to the file or wildcard pattern.</param>
        /// <param name="fileName">Display name for the file or folder.</param>
        /// <param name="isWildcard">True if key is a wildcard pattern (e.g., "*.parquet"); false for single file.</param>
        /// <param name="customQuery">Optional custom SQL query to execute instead of default query.</param>
        /// <param name="loadAsTable">True to load all parquet files into a DuckDB table for faster querying.</param>
        public ParquetViewerWindow(string bucketName, string key, string fileName, bool isWildcard = false, string? customQuery = null, bool loadAsTable = false)
        {
            InitializeComponent();

            _bucketName = bucketName;
            _key = key;
            _fileName = fileName;
            _isWildcard = isWildcard;
            _customQuery = customQuery;
            _loadAsTable = loadAsTable;

            // Subscribe to row selection changes
            ResultsDataGrid.SelectionChanged += ResultsDataGrid_SelectionChanged;

            // Subscribe to keyboard events for copy functionality
            ResultsDataGrid.PreviewKeyDown += ResultsDataGrid_PreviewKeyDown;

            // Create a dedicated DuckDB connection for this window with S3 access
            InitializeDuckDbConnectionAsync();

            if (!string.IsNullOrEmpty(_customQuery))
            {
                FileNameTextBlock.Text = $"Custom Query: {fileName}";
                Title = $"Query: {fileName}";
            }
            else if (_isWildcard)
            {
                if (_loadAsTable)
                {
                    FileNameTextBlock.Text = $"Parquet Table: {fileName}";
                    Title = $"{fileName}/* (Table Mode)";
                }
                else
                {
                    FileNameTextBlock.Text = $"Parquet Files in: {fileName}";
                    Title = $"{fileName}/*";
                }
            }
            else
            {
                FileNameTextBlock.Text = $"Parquet File: {fileName}";
                Title = fileName;
            }
        }

        private async void InitializeDuckDbConnectionAsync()
        {
            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                StatusTextBlock.Text = "Initializing database connection...";
                LoadingMessageTextBlock.Text = "Configuring S3 access...";

                // Get S3Client from S3Manager
                var s3Client = await S3Manager.Instance.GetS3ClientForBucketAsync(_bucketName);
                if (s3Client == null)
                {
                    throw new InvalidOperationException($"Unable to access bucket '{_bucketName}'.");
                }

                var region = s3Client.Config.RegionEndpoint?.SystemName ?? "us-east-1";

                // Check if this is a public bucket using S3Manager
                bool isPublicBucket = S3Manager.Instance.IsPublicBucket(_bucketName);

                if (isPublicBucket)
                {
                    // Create connection for anonymous/public S3 access (no credentials)
                    _duckDbConnection = await Task.Run(() =>
                        DuckDbManager.Instance.CreateConnectionWithAnonymousS3Access(region));
                }
                else
                {
                    // Get AWS credentials using S3Manager's profile
                    var chain = new Amazon.Runtime.CredentialManagement.CredentialProfileStoreChain();
                    Amazon.Runtime.AWSCredentials? awsCredentials = null;

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
                    _duckDbConnection = await Task.Run(() =>
                        DuckDbManager.Instance.CreateConnectionWithS3Access(immutableCredentials, region));
                }

                // Start loading data once connection is ready
                LoadParquetDataAsync();
            }
            catch (Exception ex)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Error initializing database connection: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "Failed to initialize";
            }
        }

        private async void LoadParquetDataAsync()
        {
            // Wait for connection to be initialized
            if (_duckDbConnection == null)
            {
                return;
            }

            // Cancel any existing operation
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;

            // Disable controls during loading
            RowLimitComboBox.IsEnabled = false;
            ResultsDataGrid.ItemsSource = null;
            LoadingOverlay.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "Preparing query...";

            try
            {
                var selectedItem = RowLimitComboBox.SelectedItem as ComboBoxItem;
                if (selectedItem == null) return;

                int rowLimit = Convert.ToInt32(selectedItem.Tag);

                LoadingMessageTextBlock.Text = "Preparing S3 query...";
                StatusTextBlock.Text = "Querying S3...";

                string queryToExecute;

                if (!string.IsNullOrEmpty(_customQuery))
                {
                    // Use custom query provided by user
                    LoadingMessageTextBlock.Text = "Executing custom query...";
                    StatusTextBlock.Text = "Running custom query...";

                    // Apply row limit to custom query if specified
                    queryToExecute = _customQuery.Trim();
                    if (rowLimit != -1)
                    {
                        // Check if query already has LIMIT clause
                        if (!queryToExecute.Contains("LIMIT", StringComparison.OrdinalIgnoreCase))
                        {
                            queryToExecute += $" LIMIT {rowLimit}";
                        }
                    }
                }
                else
                {
                    string s3Path;

                    if (_isWildcard)
                    {
                        // Use S3 wildcard pattern directly
                        var prefix = _key.Replace("*.parquet", "");
                        s3Path = $"s3://{_bucketName}/{prefix}*.parquet";

                        LoadingMessageTextBlock.Text = "Querying parquet files from S3...";
                    }
                    else
                    {
                        // Use direct S3 path
                        s3Path = $"s3://{_bucketName}/{_key}";
                        LoadingMessageTextBlock.Text = "Querying parquet file from S3...";
                    }

                    LoadingMessageTextBlock.Text = "Executing query...";
                    StatusTextBlock.Text = "Querying parquet file(s)...";

                    // If loadAsTable is true, create a table first
                    if (_loadAsTable && _isWildcard)
                    {
                        LoadingMessageTextBlock.Text = "Creating table from parquet files...";
                        StatusTextBlock.Text = "Loading data into table...";

                        // Create table on background thread
                        await Task.Run(() =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (_duckDbConnection == null)
                            {
                                throw new InvalidOperationException("DuckDB connection is not initialized.");
                            }

                            using (var command = _duckDbConnection.CreateCommand())
                            {
                                // Create table from all parquet files
                                command.CommandText = $"CREATE TABLE {_tableName} AS SELECT * FROM read_parquet('{s3Path}')";
                                command.ExecuteNonQuery();
                            }
                        }, cancellationToken);

                        LoadingMessageTextBlock.Text = "Querying table...";
                        StatusTextBlock.Text = "Reading from table...";

                        // Query the table instead of reading from S3 again
                        queryToExecute = rowLimit == -1
                            ? $"SELECT * FROM {_tableName}"
                            : $"SELECT * FROM {_tableName} LIMIT {rowLimit}";
                    }
                    else
                    {
                        queryToExecute = rowLimit == -1
                            ? $"SELECT * FROM read_parquet('{s3Path}')"
                            : $"SELECT * FROM read_parquet('{s3Path}') LIMIT {rowLimit}";
                    }
                }

                // Store the query that will be executed
                _lastExecutedQuery = queryToExecute;

                // Execute query on background thread to keep UI responsive
                var result = await Task.Run(() => ExecuteQuery(queryToExecute, cancellationToken), cancellationToken);

                // Update UI on UI thread
                if (result != null)
                {
                    // Process complex types and convert to JSON
                    ProcessComplexColumns(result);

                    // Clear auto-generated columns and create custom columns
                    ResultsDataGrid.AutoGenerateColumns = false;
                    ResultsDataGrid.Columns.Clear();
                    CreateCustomColumns(result);

                    ResultsDataGrid.ItemsSource = result.DefaultView;

                    int rowCount = result.Rows.Count;
                    if (!string.IsNullOrEmpty(_customQuery))
                    {
                        StatusTextBlock.Text = $"Query executed successfully: {rowCount:N0} rows returned";
                    }
                    else if (_loadAsTable && _isWildcard)
                    {
                        if (rowLimit == -1)
                        {
                            StatusTextBlock.Text = $"Table loaded: {rowCount:N0} rows (table: {_tableName})";
                        }
                        else
                        {
                            StatusTextBlock.Text = $"Table loaded: {rowCount:N0} rows shown (limited to {rowLimit:N0}, table: {_tableName})";
                        }
                    }
                    else if (rowLimit == -1)
                    {
                        StatusTextBlock.Text = $"Loaded {rowCount:N0} rows";
                    }
                    else
                    {
                        StatusTextBlock.Text = $"Loaded {rowCount:N0} rows (limited to {rowLimit:N0})";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Operation cancelled";
            }
            catch (Exception ex)
            {
                string errorMessage = $"Error executing query: {ex.Message}";
                MessageBox.Show(errorMessage, "Query Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "Query execution failed";
            }
            finally
            {
                // Re-enable controls and hide loading overlay
                LoadingOverlay.Visibility = Visibility.Collapsed;
                RowLimitComboBox.IsEnabled = true;
            }
        }

        private DataTable? ExecuteQuery(string query, CancellationToken cancellationToken)
        {
            // This runs on a background thread
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Query the parquet file(s) using DuckDB with this window's dedicated connection
                if (_duckDbConnection == null)
                {
                    throw new InvalidOperationException("DuckDB connection is not initialized.");
                }

                using (var command = _duckDbConnection.CreateCommand())
                {
                    command.CommandText = query;

                    cancellationToken.ThrowIfCancellationRequested();

                    using (var reader = command.ExecuteReader())
                    {
                        var dataTable = new DataTable();
                        var streamColumns = new HashSet<int>();

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
                                    if (typeof(Stream).IsAssignableFrom(fieldType))
                                    {
                                        streamColumns.Add(i);

                                        // Store as object to hold byte arrays
                                        dataTable.Columns.Add(reader.GetName(i), typeof(byte[]));
                                    }
                                    else
                                    {
                                        dataTable.Columns.Add(reader.GetName(i), fieldType);
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
                                    if (value is UnmanagedMemoryStream stream)
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
                                    row[i] = reader.GetValue(i);
                                }
                            }
                            dataTable.Rows.Add(row);
                        }

                        return dataTable;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw; // Re-throw to be handled in the async method
            }
            catch (Exception ex)
            {
                // Rethrow to be caught in the async method
                throw new InvalidOperationException($"Query execution failed: {ex.Message}", ex);
            }
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            LoadParquetDataAsync();
        }

        private void EditQueryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get the last executed query or generate a default one
                string? queryToEdit = _lastExecutedQuery;

                if (string.IsNullOrEmpty(queryToEdit))
                {
                    // Generate default query if none was executed yet
                    if (!string.IsNullOrEmpty(_customQuery))
                    {
                        queryToEdit = _customQuery;
                    }
                    else if (_loadAsTable && _isWildcard)
                    {
                        // If in table mode, default query uses the table
                        queryToEdit = $"SELECT * FROM {_tableName}";
                    }
                    else if (_isWildcard)
                    {
                        var prefix = _key.Replace("*.parquet", "");
                        string s3Path = $"s3://{_bucketName}/{prefix}*.parquet";
                        queryToEdit = $"SELECT * FROM read_parquet('{s3Path}')";
                    }
                    else
                    {
                        string s3Path = $"s3://{_bucketName}/{_key}";
                        queryToEdit = $"SELECT * FROM read_parquet('{s3Path}')";
                    }
                }

                // Open query editor dialog with the current query
                var queryDialog = new QueryEditorDialog(
                    _bucketName,
                    queryToEdit,
                    _fileName,
                    this); // Pass reference to this window for re-execution

                queryDialog.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening query editor: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Executes a new custom query in this window, replacing the current results.
        /// Called from QueryEditorDialog when user modifies and re-executes a query.
        /// </summary>
        /// <param name="newQuery">The SQL query to execute.</param>
        public void ExecuteNewQuery(string newQuery)
        {
            // Update the custom query field
            _customQuery = newQuery;

            // Reload data with the new query
            LoadParquetDataAsync();

            // Bring window to front
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ResultsDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Get selected row index
            var selectedIndex = ResultsDataGrid.SelectedIndex;

            // Check if user clicked on the same row again (toggle behavior)
            if (selectedIndex == _lastSelectedRowIndex && selectedIndex >= 0)
            {
                // Deselect the row
                ResultsDataGrid.SelectedIndex = -1;
                _lastSelectedRowIndex = -1;

                // Close map window
                if (_currentMapWindow != null)
                {
                    _currentMapWindow.Close();
                    _currentMapWindow = null;
                }
                return;
            }

            // Update last selected row
            _lastSelectedRowIndex = selectedIndex;

            if (selectedIndex < 0)
            {
                // No row selected - close map window if open
                if (_currentMapWindow != null)
                {
                    _currentMapWindow.Close();
                    _currentMapWindow = null;
                }
                return;
            }

            // Check if this row has geometries
            if (_rowGeometries.TryGetValue(selectedIndex, out var geometries) && geometries.Count > 0)
            {
                try
                {
                    if (_currentMapWindow == null)
                    {
                        // Create new map window
                        _currentMapWindow = new GeometryMapWindow
                        {
                            Owner = this
                        };

                        _currentMapWindow.Show();

                        // Subscribe to closed event to clear reference
                        _currentMapWindow.Closed += (s, args) =>
                        {
                            if (_currentMapWindow == s)
                            {
                                _currentMapWindow = null;
                                _lastSelectedRowIndex = -1; // Reset selection tracking when map closes
                                ResultsDataGrid.SelectedIndex = -1; // Deselect row when map closes
                            }
                        };
                    }

                    // Load geometries (will replace existing ones)
                    _currentMapWindow.LoadGeometriesWithInfo(geometries);

                    // Bring window to front if it was minimized or behind other windows
                    if (_currentMapWindow.WindowState == WindowState.Minimized)
                    {
                        _currentMapWindow.WindowState = WindowState.Normal;
                    }
                    _currentMapWindow.Activate();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening map window: {ex.Message}", "Map Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                // Row has no geometries - close map window if open
                if (_currentMapWindow != null)
                {
                    _currentMapWindow.Close();
                    _currentMapWindow = null;
                }
            }
        }

        private void ProcessComplexColumns(DataTable dataTable)
        {
            var complexColumns = new List<DataColumn>();
            var geometryColumns = new Dictionary<DataColumn, bool>();
            _rowGeometries.Clear(); // Reset row geometries map

            // Identify complex columns and geometry columns
            foreach (DataColumn column in dataTable.Columns)
            {
                // Check for byte arrays (converted from streams) OR Stream types - assume all are geometries
                if (column.DataType == typeof(byte[]) || column.DataType == typeof(Stream) || column.DataType == typeof(UnmanagedMemoryStream))
                {
                    // Assume byte array or stream columns are geometries

                    // We'll try to convert them and fall back to hex string if not
                    geometryColumns[column] = true;
                    complexColumns.Add(column);
                }
                else if (IsComplexType(column.DataType))
                {
                    geometryColumns[column] = false;
                    complexColumns.Add(column);
                }
            }

            // If no complex columns, nothing to process
            if (complexColumns.Count == 0)
                return;

            // Create a new DataTable with modified structure
            var newDataTable = new DataTable();

            // Add columns to new table
            foreach (DataColumn column in dataTable.Columns)
            {
                if (complexColumns.Contains(column))
                {
                    // Replace complex column with string column
                    newDataTable.Columns.Add(column.ColumnName, typeof(string));
                }
                else
                {
                    // Keep original column type
                    newDataTable.Columns.Add(column.ColumnName, column.DataType);
                }
            }

            // Copy and transform data
            int rowIndex = 0;
            foreach (DataRow row in dataTable.Rows)
            {
                var newRow = newDataTable.NewRow();
                var rowGeometryList = new List<GeometryMapWindow.GeometryInfo>();

                foreach (DataColumn column in dataTable.Columns)
                {
                    if (complexColumns.Contains(column))
                    {
                        // Convert complex value to JSON or WKT
                        if (row[column] != DBNull.Value && row[column] != null)
                        {
                            try
                            {
                                // Check if it's a geometry column
                                if (geometryColumns.TryGetValue(column, out bool isGeometry) && isGeometry)
                                {
                                    var wkt = ConvertGeometryToWkt(row[column]);
                                    newRow[column.ColumnName] = wkt;

                                    // Store WKT for map display
                                    if (!string.IsNullOrWhiteSpace(wkt) && !wkt.StartsWith("0x") && !wkt.StartsWith("[Error"))
                                    {
                                        rowGeometryList.Add(new GeometryMapWindow.GeometryInfo
                                        {
                                            Wkt = wkt,
                                            ColumnName = column.ColumnName
                                        });
                                    }
                                }
                                else
                                {
                                    newRow[column.ColumnName] = ConvertToJson(row[column]);
                                }
                            }
                            catch
                            {
                                newRow[column.ColumnName] = row[column].ToString();
                            }
                        }
                        else
                        {
                            newRow[column.ColumnName] = DBNull.Value;
                        }
                    }
                    else
                    {
                        newRow[column.ColumnName] = row[column];
                    }
                }
                newDataTable.Rows.Add(newRow);

                // Store geometries for this row if any
                if (rowGeometryList.Count > 0)
                {
                    _rowGeometries[rowIndex] = rowGeometryList;
                }
                rowIndex++;
            }

            // Replace original DataTable content
            dataTable.Clear();
            dataTable.Columns.Clear();

            foreach (DataColumn column in newDataTable.Columns)
            {
                dataTable.Columns.Add(column.ColumnName, column.DataType);
            }

            foreach (DataRow row in newDataTable.Rows)
            {
                dataTable.ImportRow(row);
            }
        }

        private bool IsComplexType(Type type)
        {
            // Exclude byte arrays from being treated as complex types
            if (type == typeof(byte[]))
                return false;

            // Check if the type is a complex type (array, dictionary, or custom object)
            return type != typeof(string) &&
                   (type.IsArray ||
                    type.IsGenericType ||
                    (!type.IsPrimitive && !type.IsEnum && type != typeof(DateTime) && type != typeof(decimal)));
        }

        private string ConvertToJson(object value)
        {
            if (value == null) return "null";

            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                return JsonSerializer.Serialize(value, options);
            }
            catch
            {
                return value.ToString() ?? "";
            }
        }

        private bool TryParseGeometry(byte[] wkb, out NetTopologySuite.Geometries.Geometry? geometry)
        {
            geometry = null;

            if (wkb == null || wkb.Length < 5)
                return false;

            try
            {
                // Try both byte orders (little-endian and big-endian)
                var reader = new WKBReader();
                geometry = reader.Read(wkb);
                return geometry is not null;
            }
            catch
            {
                // If standard WKB fails, might be a different format
                return false;
            }
        }

        private string ConvertGeometryToWkt(object value)
        {
            if (value == null) return "null";

            try
            {
                byte[]? wkb = null;

                // Handle both byte arrays and streams
                if (value is byte[] byteArray)
                {
                    wkb = byteArray;
                }
                else if (value is UnmanagedMemoryStream stream)
                {
                    wkb = ReadStreamToBytes(stream);
                }
                else if (value is Stream streamBase)
                {
                    using (var ms = new MemoryStream())
                    {
                        streamBase.CopyTo(ms);
                        wkb = ms.ToArray();
                    }
                }

                if (wkb != null && wkb.Length > 0)
                {
                    if (TryParseGeometry(wkb, out var geometry) && geometry is not null)
                    {
                        var writer = new WKTWriter();
                        return writer.Write(geometry);
                    }
                    else
                    {
                        // Not a valid geometry, convert to hex string
                        return "0x" + BitConverter.ToString(wkb).Replace("-", "");
                    }
                }

                return value.ToString() ?? "";
            }
            catch (Exception ex)
            {
                // If conversion fails, show as hex if possible
                if (value is byte[] bytes)
                {
                    return "0x" + BitConverter.ToString(bytes).Replace("-", "");
                }
                return $"[Error: {ex.Message}]";
            }
        }

        private byte[] ReadStreamToBytes(UnmanagedMemoryStream stream)
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

        private void CreateCustomColumns(DataTable dataTable)
        {
            foreach (DataColumn column in dataTable.Columns)
            {
                // Check if this is a geometry column
                bool isGeometryColumn = false;
                foreach (DataRow row in dataTable.Rows)
                {
                    if (row[column] != DBNull.Value && row[column] != null)
                    {
                        string value = row[column].ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(value) &&
                            (value.StartsWith("POINT") || value.StartsWith("LINESTRING") ||
                             value.StartsWith("POLYGON") || value.StartsWith("MULTIPOINT") ||
                             value.StartsWith("MULTILINESTRING") || value.StartsWith("MULTIPOLYGON")))
                        {
                            isGeometryColumn = true;
                            break;
                        }
                    }
                }

                // Create collapsible column for all data types with expand button
                var templateColumn = new DataGridTemplateColumn
                {
                    Header = column.ColumnName,
                    Width = DataGridLength.Auto, // Auto-size based on content
                    MinWidth = 100,
                    MaxWidth = 400, // Prevent extremely wide columns
                    CanUserResize = true, // Allow manual resizing
                    SortMemberPath = column.ColumnName, // Enable sorting by this column
                    CellTemplate = isGeometryColumn
                        ? CreateGeometryCellTemplate(column.ColumnName)
                        : CreateExpandableCellTemplate(column.ColumnName)
                };
                ResultsDataGrid.Columns.Add(templateColumn);
            }
        }

        private DataTemplate CreateExpandableCellTemplate(string columnName)
        {
            var template = new DataTemplate();

            // Create a Grid to hold truncated text and expand button
            var gridFactory = new FrameworkElementFactory(typeof(Grid));
            gridFactory.SetValue(Grid.MarginProperty, new Thickness(2));
            gridFactory.SetValue(Grid.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);

            // Column definitions
            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col1.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto); // Auto-size to content
            gridFactory.AppendChild(col1);

            var col2 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col2.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            gridFactory.AppendChild(col2);

            // TextBlock for content (truncated or full)
            var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
            var textBinding = new Binding($"[{columnName}]");
            textBinding.Converter = new SmartTruncateTextConverter();
            textBlockFactory.SetBinding(TextBlock.TextProperty, textBinding);
            textBlockFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.None);
            textBlockFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
            textBlockFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            textBlockFactory.SetValue(TextBlock.PaddingProperty, new Thickness(4, 2, 4, 2));
            textBlockFactory.SetValue(Grid.ColumnProperty, 0);
            gridFactory.AppendChild(textBlockFactory);

            // Button to expand (visibility bound to whether content is truncated)
            var buttonFactory = new FrameworkElementFactory(typeof(Button));
            buttonFactory.SetValue(Button.ContentProperty, "...");
            buttonFactory.SetValue(Button.PaddingProperty, new Thickness(8, 2, 8, 2));
            buttonFactory.SetValue(Button.MarginProperty, new Thickness(5, 0, 0, 0));
            buttonFactory.SetValue(Button.CursorProperty, Cursors.Hand);
            buttonFactory.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center);
            buttonFactory.SetValue(Grid.ColumnProperty, 1);
            buttonFactory.SetValue(Button.ToolTipProperty, "Click to view full content");
            buttonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler(ExpandButton_Click));

            // Bind button Tag to full text and Visibility to whether text needs expansion
            var fullTextBinding = new Binding($"[{columnName}]");
            buttonFactory.SetValue(Button.TagProperty, fullTextBinding);

            var visibilityBinding = new Binding($"[{columnName}]");
            visibilityBinding.Converter = new NeedsExpansionConverter();
            buttonFactory.SetBinding(Button.VisibilityProperty, visibilityBinding);

            gridFactory.AppendChild(buttonFactory);

            template.VisualTree = gridFactory;
            return template;
        }

        private DataTemplate CreateGeometryCellTemplate(string columnName)
        {
            var template = new DataTemplate();

            // Create a Grid to hold geometry text (styled the same as other columns)
            var gridFactory = new FrameworkElementFactory(typeof(Grid));
            gridFactory.SetValue(Grid.MarginProperty, new Thickness(2));
            gridFactory.SetValue(Grid.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);

            // Column definitions
            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col1.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            gridFactory.AppendChild(col1);

            var col2 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col2.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            gridFactory.AppendChild(col2);

            // TextBlock for geometry content
            var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
            var textBinding = new Binding($"[{columnName}]");
            textBinding.Converter = new SmartTruncateTextConverter();
            textBlockFactory.SetBinding(TextBlock.TextProperty, textBinding);
            textBlockFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.None);
            textBlockFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
            textBlockFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            textBlockFactory.SetValue(TextBlock.PaddingProperty, new Thickness(4, 2, 4, 2));
            textBlockFactory.SetValue(Grid.ColumnProperty, 0);
            gridFactory.AppendChild(textBlockFactory);

            // Button to expand full content
            var expandButtonFactory = new FrameworkElementFactory(typeof(Button));
            expandButtonFactory.SetValue(Button.ContentProperty, "...");
            expandButtonFactory.SetValue(Button.PaddingProperty, new Thickness(8, 2, 8, 2));
            expandButtonFactory.SetValue(Button.MarginProperty, new Thickness(5, 0, 0, 0));
            expandButtonFactory.SetValue(Button.CursorProperty, Cursors.Hand);
            expandButtonFactory.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center);
            expandButtonFactory.SetValue(Grid.ColumnProperty, 1);
            expandButtonFactory.SetValue(Button.ToolTipProperty, "Click to view full content");
            expandButtonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler(ExpandButton_Click));

            var fullTextBinding = new Binding($"[{columnName}]");
            expandButtonFactory.SetValue(Button.TagProperty, fullTextBinding);

            var visibilityBinding = new Binding($"[{columnName}]");
            visibilityBinding.Converter = new NeedsExpansionConverter();
            expandButtonFactory.SetBinding(Button.VisibilityProperty, visibilityBinding);

            gridFactory.AppendChild(expandButtonFactory);

            template.VisualTree = gridFactory;
            return template;
        }

        private void ExpandButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string fullText)
            {
                // Show full content in a dialog
                var dialog = new Window
                {
                    Title = "Full Content",
                    Width = 600,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this
                };

                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                var textBox = new TextBox
                {
                    Text = fullText ?? "",
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Padding = new Thickness(10),
                    BorderThickness = new Thickness(0)
                };

                scrollViewer.Content = textBox;
                dialog.Content = scrollViewer;
                dialog.Show();
            }
        }

        private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsDataGrid.ItemsSource == null)
            {
                MessageBox.Show("No data loaded to display.", "No Data",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (ResultsDataGrid.ItemsSource is not DataView dataView || dataView.Count == 0)
            {
                MessageBox.Show("No data loaded to display.", "No Data",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Create comprehensive view window
            var dialog = new Window
            {
                Title = $"All Data - {_fileName}",
                Width = 900,
                Height = 650,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(10)
            };

            var stackPanel = new StackPanel();

            // Build the content
            var dataTable = dataView.Table ?? new DataTable();
            int rowNumber = 1;

            foreach (DataRow row in dataTable.Rows)
            {
                // Row header
                var rowHeader = new TextBlock
                {
                    Text = $"Row {rowNumber}",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, rowNumber == 1 ? 0 : 20, 0, 10),
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243))
                };
                stackPanel.Children.Add(rowHeader);

                // Row separator
                var separator = new System.Windows.Controls.Separator
                {
                    Margin = new Thickness(0, 0, 0, 10)
                };
                stackPanel.Children.Add(separator);

                // Create a grid for each row's data
                var rowGrid = new Grid
                {
                    Margin = new Thickness(10, 0, 0, 0)
                };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                int cellRow = 0;
                foreach (DataColumn column in dataTable.Columns)
                {
                    rowGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    // Column name
                    var columnNameBlock = new TextBlock
                    {
                        Text = column.ColumnName + ":",
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 10, 5),
                        VerticalAlignment = VerticalAlignment.Top
                    };
                    Grid.SetRow(columnNameBlock, cellRow);
                    Grid.SetColumn(columnNameBlock, 0);
                    rowGrid.Children.Add(columnNameBlock);

                    // Cell value
                    var cellValue = row[column];
                    var cellText = cellValue == DBNull.Value || cellValue == null
                        ? "(null)"
                        : cellValue.ToString() ?? "";

                    var valueBlock = new TextBlock
                    {
                        Text = cellText,
                        TextWrapping = TextWrapping.Wrap,
                        FontFamily = new FontFamily("Consolas"),
                        Margin = new Thickness(0, 0, 0, 5),
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 245, 245)),
                        Padding = new Thickness(5)
                    };
                    Grid.SetRow(valueBlock, cellRow);
                    Grid.SetColumn(valueBlock, 1);
                    rowGrid.Children.Add(valueBlock);

                    cellRow++;
                }

                stackPanel.Children.Add(rowGrid);
                rowNumber++;
            }

            scrollViewer.Content = stackPanel;
            Grid.SetRow(scrollViewer, 0);
            grid.Children.Add(scrollViewer);

            // Status bar
            var statusBar = new System.Windows.Controls.Primitives.StatusBar();
            var statusText = new TextBlock
            {
                Text = $"Showing {dataTable.Rows.Count:N0} rows with {dataTable.Columns.Count} columns"
            };
            var statusItem = new System.Windows.Controls.Primitives.StatusBarItem { Content = statusText };
            statusBar.Items.Add(statusItem);
            Grid.SetRow(statusBar, 1);
            grid.Children.Add(statusBar);

            dialog.Content = grid;
            dialog.Show();
        }

        private void ResultsDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Handle Ctrl+C to copy full cell content
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var currentCell = ResultsDataGrid.CurrentCell;
                if (currentCell.IsValid && currentCell.Item != null)
                {
                    try
                    {
                        // Get the column name from the current cell
                        var column = currentCell.Column;
                        if (column != null)
                        {
                            // Get the row data
                            var rowView = currentCell.Item as DataRowView;
                            var dataTable = rowView?.Row?.Table;
                            if (dataTable != null)
                            {
                                // Get the full cell content (not truncated)
                                var columnName = column.Header?.ToString() ?? string.Empty;
                                if (!string.IsNullOrEmpty(columnName) && dataTable.Columns.Contains(columnName))
                                {
#pragma warning disable CS8602 // Dereference of a possibly null reference - rowView is guaranteed non-null when dataTable is not null
                                    var cellValue = rowView![columnName];
#pragma warning restore CS8602
                                    var fullText = cellValue == DBNull.Value || cellValue == null
                                        ? string.Empty
                                        : cellValue.ToString() ?? string.Empty;

                                    // Copy the full content to clipboard
                                    if (!string.IsNullOrEmpty(fullText))
                                    {
                                        Clipboard.SetText(fullText);
                                        e.Handled = true; // Prevent default copy behavior
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log error but don't show message box to avoid interrupting user
                        System.Diagnostics.Debug.WriteLine($"Error copying cell content: {ex.Message}");
                    }
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Close map window if open
            if (_currentMapWindow != null)
            {
                _currentMapWindow.Close();
                _currentMapWindow = null;
            }

            // Cancel any ongoing operations
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            // Dispose DuckDB connection (this will automatically drop the table and free memory)
            if (_duckDbConnection != null)
            {
                // Note: No need to explicitly drop the table - it will be destroyed when connection is disposed
                // The in-memory table and all its data will be freed when the connection closes
                DuckDbManager.Instance.ReleaseConnection(_duckDbConnection);
                _duckDbConnection = null;
            }
        }
    }
}
