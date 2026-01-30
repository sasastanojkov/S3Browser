using System.Data;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using S3Browser.Converters;
using S3Browser.Helpers;

namespace S3Browser
{
    /// <summary>
    /// Window for viewing Parquet files from S3 using DuckDB engine.
    /// Supports single file and wildcard (folder) modes with geometry visualization.
    /// </summary>
    public partial class ParquetViewerWindow : DataViewerWindowBase
    {
        private readonly string _bucketName;
        private readonly string _key;
        private readonly string _fileName;
        private readonly bool _isWildcard;
        private readonly bool _loadAsTable;
        private readonly string _tableName = "parquet_data";

        // Override abstract properties to provide access to XAML controls
        protected override DataGrid DataGrid => ResultsDataGrid;
        protected override TextBlock StatusText => StatusTextBlock;
        protected override string FileName => _fileName;

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
            ResultsDataGrid.SelectionChanged += HandleResultsDataGridSelectionChanged;

            // Subscribe to keyboard events for copy functionality
            ResultsDataGrid.PreviewKeyDown += HandleResultsDataGridPreviewKeyDown;

            // Build the S3 path that will be used
            if (_isWildcard)
            {
                var prefix = _key.Replace("*.parquet", "");
                _displayPath = $"s3://{_bucketName}/{prefix}*.parquet";
            }
            else
            {
                _displayPath = $"s3://{_bucketName}/{_key}";
            }

            // Set display text and title
            FileNameTextBlock.Text = _displayPath;
            Title = fileName;

            // Create a dedicated DuckDB connection for this window with S3 access
            InitializeDuckDbConnectionAsync();
        }

        private async void InitializeDuckDbConnectionAsync()
        {
            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                StatusTextBlock.Text = "Initializing database connection...";
                LoadingMessageTextBlock.Text = "Configuring S3 access...";

                // Use DuckDbManager to create connection with proper S3 access
                _duckDbConnection = await DuckDbManager.Instance.CreateConnectionForBucketAsync(_bucketName);

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

                        // Create table on background thread using DuckDbManager
                        await Task.Run(() =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (_duckDbConnection == null)
                            {
                                throw new InvalidOperationException("DuckDB connection is not initialized.");
                            }

                            // Create table from all parquet files
                            string createTableCommand = $"CREATE TABLE {_tableName} AS SELECT * FROM read_parquet('{s3Path}')";
                            DuckDbManager.Instance.ExecuteNonQuery(_duckDbConnection, createTableCommand, cancellationToken);
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

        private new DataTable? ExecuteQuery(string query, CancellationToken cancellationToken)
        {
            // Use base class implementation
            return base.ExecuteQuery(query, cancellationToken);
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
        public override void ExecuteNewQuery(string newQuery)
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
                                    var wkt = GeometryHelper.ConvertToWkt(row[column]);
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
                            catch (Exception ex)
                            {
                                // If JSON conversion fails, try one more time with the value
                                try
                                {
                                    newRow[column.ColumnName] = ConvertToJson(row[column]);
                                }
                                catch
                                {
                                    // Last resort: use ToString but log the error
                                    System.Diagnostics.Debug.WriteLine($"Failed to convert column {column.ColumnName}: {ex.Message}");
                                    newRow[column.ColumnName] = $"[Error converting: {row[column]?.GetType().Name ?? "null"}]";
                                }
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
                // First, try to extract the actual value from DuckDB types
                var extractedValue = ExtractValueFromDuckDbType(value);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = false,  // Compact JSON without indentation
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                return JsonSerializer.Serialize(extractedValue, options);
            }
            catch (Exception ex)
            {
                // If JSON serialization fails, try to get a readable string representation
                System.Diagnostics.Debug.WriteLine($"Failed to serialize to JSON: {ex.Message}");
                return value.ToString() ?? "";
            }
        }

        private object ExtractValueFromDuckDbType(object value)
        {
            if (value == null) return "null";

            var type = value.GetType();
            var typeName = type.FullName ?? type.Name;

            // Check if this is a generic Dictionary type (like Dictionary`2)
            if (type.IsGenericType)
            {
                var genericTypeDef = type.GetGenericTypeDefinition();

                // Handle Dictionary<TKey, TValue>
                if (genericTypeDef == typeof(Dictionary<,>) ||
                    typeName.Contains("Dictionary") ||
                    type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IDictionary<,>)))
                {
                    var dict = new Dictionary<string, object>();

                    // Try to use IDictionary interface
                    if (value is System.Collections.IDictionary idict)
                    {
                        foreach (System.Collections.DictionaryEntry entry in idict)
                        {
                            var key = entry.Key?.ToString() ?? "null";
                            var val = entry.Value != null ? ExtractValueFromDuckDbType(entry.Value) : "null";
                            dict[key] = val;
                        }
                        return dict;
                    }

                    // Fallback: use reflection to get Keys and Values properties
                    try
                    {
                        var keysProperty = type.GetProperty("Keys");
                        var indexer = type.GetProperty("Item");

                        if (keysProperty != null && indexer != null)
                        {
                            var keys = keysProperty.GetValue(value) as System.Collections.IEnumerable;
                            if (keys != null)
                            {
                                foreach (var key in keys)
                                {
                                    var keyStr = key?.ToString() ?? "null";
                                    var val = indexer.GetValue(value, new[] { key });
                                    dict[keyStr] = val != null ? ExtractValueFromDuckDbType(val) : "null";
                                }
                                return dict;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to extract dictionary via reflection: {ex.Message}");
                    }
                }

                // Handle List<T> or other generic collections
                if (genericTypeDef == typeof(List<>) ||
                    type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IList<>)))
                {
                    var list = new List<object>();
                    if (value is System.Collections.IEnumerable enumerable)
                    {
                        foreach (var item in enumerable)
                        {
                            list.Add(item != null ? ExtractValueFromDuckDbType(item) : "null");
                        }
                    }
                    return list;
                }
            }

            // Handle non-generic dictionaries
            if (value is System.Collections.IDictionary dictionary)
            {
                var dict = new Dictionary<string, object>();
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                {
                    var key = entry.Key?.ToString() ?? "null";
                    var val = entry.Value != null ? ExtractValueFromDuckDbType(entry.Value) : "null";
                    dict[key] = val;
                }
                return dict;
            }

            // Handle arrays and general enumerables (but not strings)
            if (value is System.Collections.IEnumerable enumerable2 && !(value is string))
            {
                var list = new List<object>();
                foreach (var item in enumerable2)
                {
                    list.Add(item != null ? ExtractValueFromDuckDbType(item) : "null");
                }
                return list;
            }

            // Handle structs and complex objects with properties (both value types and reference types)
            if (!type.IsPrimitive && type != typeof(string) && type != typeof(DateTime) &&
                type != typeof(decimal) && !type.IsEnum)
            {
                // Try to extract properties using reflection
                try
                {
                    var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (properties.Length > 0)
                    {
                        var dict = new Dictionary<string, object>();
                        foreach (var prop in properties)
                        {
                            try
                            {
                                // Skip indexer properties
                                if (prop.GetIndexParameters().Length > 0)
                                    continue;

                                var propValue = prop.GetValue(value);
                                dict[prop.Name] = propValue != null ? ExtractValueFromDuckDbType(propValue) : "null";
                            }
                            catch
                            {
                                // Skip properties that can't be read
                            }
                        }
                        if (dict.Count > 0)
                        {
                            return dict;
                        }
                    }
                }
                catch
                {
                    // Fall through to return original value
                }
            }

            // Return primitive types as-is
            return value;
        }

        private void CreateCustomColumns(DataTable dataTable)
        {
            foreach (DataColumn column in dataTable.Columns)
            {
                // Check if this is a geometry column using GeometryHelper
                bool isGeometryColumn = false;
                foreach (DataRow row in dataTable.Rows)
                {
                    if (row[column] != DBNull.Value && row[column] != null)
                    {
                        string value = row[column].ToString() ?? "";
                        if (GeometryHelper.IsWktGeometry(value))
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
                        : CreateExpandableCellTemplate(column.ColumnName) // Use base class method
                };
                ResultsDataGrid.Columns.Add(templateColumn);
            }
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
            expandButtonFactory.AddHandler(Button.PreviewMouseDownEvent, new System.Windows.Input.MouseButtonEventHandler(ExpandButton_PreviewMouseDown), true);
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

        private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
        {
            // Use base class method
            HandleExpandAllButtonClick(sender, e);
        }

        private void FilePathBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DataViewerUIHelper.CopyPathToClipboard(_displayPath, StatusTextBlock, sender as Border);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Use base class cleanup
            CleanupResources();

            // Note: No need to explicitly drop the table - it will be destroyed when connection is disposed
            // The in-memory table and all its data will be freed when the connection closes
        }
    }
}
