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
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DuckDB.NET.Data;
using Microsoft.Win32;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

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
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;
        private readonly string _key;
        private readonly string _fileName;
        private readonly bool _isWildcard;
        private readonly string? _awsProfile;
        private DuckDBConnection? _duckDbConnection;
        private CancellationTokenSource? _cancellationTokenSource;
        private List<string> _geometryWktList = new();
        private Dictionary<int, List<GeometryMapWindow.GeometryInfo>> _rowGeometries = new(); // Map row index to geometries with column info
        private GeometryMapWindow? _currentMapWindow;

        /// <summary>
        /// Initializes a new instance of the ParquetViewerWindow.
        /// </summary>
        /// <param name="s3Client">AWS S3 client for accessing S3 resources.</param>
        /// <param name="bucketName">Name of the S3 bucket containing the file(s).</param>
        /// <param name="key">S3 key (path) to the file or wildcard pattern.</param>
        /// <param name="fileName">Display name for the file or folder.</param>
        /// <param name="isWildcard">True if key is a wildcard pattern (e.g., "*.parquet"); false for single file.</param>
        /// <param name="awsProfile">AWS profile name for credential resolution. Can be null.</param>
        public ParquetViewerWindow(IAmazonS3 s3Client, string bucketName, string key, string fileName, bool isWildcard = false, string? awsProfile = null)
        {
            InitializeComponent();

            _s3Client = s3Client;
            _bucketName = bucketName;
            _key = key;
            _fileName = fileName;
            _isWildcard = isWildcard;
            _awsProfile = awsProfile;

            // Subscribe to row selection changes
            ResultsDataGrid.SelectionChanged += ResultsDataGrid_SelectionChanged;

            // Create a dedicated DuckDB connection for this window with S3 access
            InitializeDuckDbConnectionAsync();

            if (_isWildcard)
            {
                FileNameTextBlock.Text = $"Parquet Files in: {fileName}";
                Title = $"{fileName}/*";
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

                // Get AWS credentials using the profile chain
                var chain = new Amazon.Runtime.CredentialManagement.CredentialProfileStoreChain();
                Amazon.Runtime.AWSCredentials? awsCredentials = null;

                if (!string.IsNullOrEmpty(_awsProfile))
                {
                    if (!chain.TryGetAWSCredentials(_awsProfile, out awsCredentials))
                    {
                        throw new InvalidOperationException($"Unable to retrieve AWS credentials for profile '{_awsProfile}'.");
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
                var region = _s3Client.Config.RegionEndpoint?.SystemName ?? "us-east-1";

                // Create connection with S3 access on background thread
                _duckDbConnection = await Task.Run(() =>
                    DuckDbManager.Instance.CreateConnectionWithS3Access(immutableCredentials, region));

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

                // Execute query on background thread to keep UI responsive
                var result = await Task.Run(() => ExecuteQuery(s3Path, rowLimit, cancellationToken), cancellationToken);

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
                    if (rowLimit == -1)
                    {
                        StatusTextBlock.Text = $"Loaded {rowCount:N0} rows";
                    }
                    else
                    {
                        StatusTextBlock.Text = $"Loaded {rowCount:N0} rows (limited to {rowLimit:N0})";
                    }

                    // Enable export button if there are geometries
                    ExportGeoJsonButton.IsEnabled = _geometryWktList.Count > 0;
                }
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Operation cancelled";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading parquet file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "Error loading file";
            }
            finally
            {
                // Re-enable controls and hide loading overlay
                LoadingOverlay.Visibility = Visibility.Collapsed;
                RowLimitComboBox.IsEnabled = true;
            }
        }

        private DataTable? ExecuteQuery(string s3Path, int rowLimit, CancellationToken cancellationToken)
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

                string query = rowLimit == -1
                    ? $"SELECT * FROM read_parquet('{s3Path}')"
                    : $"SELECT * FROM read_parquet('{s3Path}') LIMIT {rowLimit}";

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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ResultsDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Get selected row index
            var selectedIndex = ResultsDataGrid.SelectedIndex;

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

        private void ExportGeoJsonButton_Click(object sender, RoutedEventArgs e)
        {
            if (_geometryWktList.Count == 0)
            {
                MessageBox.Show("No valid geometries found to export.", "No Geometries",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "GeoJSON files (*.geojson)|*.geojson|All files (*.*)|*.*",
                FileName = $"{_fileName}_geometries.geojson",
                DefaultExt = ".geojson"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    ExportToGeoJson(saveDialog.FileName);
                    MessageBox.Show($"Successfully exported {_geometryWktList.Count} geometries to:\n{saveDialog.FileName}\n\nYou can open this file in QGIS, ArcGIS, or any GIS application.",
                        "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting geometries: {ex.Message}", "Export Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportToGeoJson(string filePath)
        {
            var wktReader = new WKTReader();
            var geoJsonWriter = new GeoJsonWriter();

            var features = new List<object>();
            int index = 0;

            foreach (var wkt in _geometryWktList)
            {
                try
                {
                    var geometry = wktReader.Read(wkt);
                    if (geometry != null && !geometry.IsEmpty)
                    {
                        var feature = new
                        {
                            type = "Feature",
                            id = index++,
                            geometry = System.Text.Json.JsonSerializer.Deserialize<object>(geoJsonWriter.Write(geometry)),
                            properties = new { wkt = wkt }
                        };
                        features.Add(feature);
                    }
                }
                catch
                {
                    // Skip invalid geometries
                }
            }

            var featureCollection = new
            {
                type = "FeatureCollection",
                features = features
            };

            var json = System.Text.Json.JsonSerializer.Serialize(featureCollection, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(filePath, json, Encoding.UTF8);
        }

        private void ProcessComplexColumns(DataTable dataTable)
        {
            var complexColumns = new List<DataColumn>();
            var geometryColumns = new Dictionary<DataColumn, bool>();
            _geometryWktList.Clear(); // Reset geometry list
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

                                    // Store WKT for export (skip hex values and errors)
                                    if (!string.IsNullOrWhiteSpace(wkt) && !wkt.StartsWith("0x") && !wkt.StartsWith("[Error"))
                                    {
                                        _geometryWktList.Add(wkt);
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
                dialog.ShowDialog();
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

            // Dispose DuckDB connection
            if (_duckDbConnection != null)
            {
                DuckDbManager.Instance.ReleaseConnection(_duckDbConnection);
                _duckDbConnection = null;
            }
        }
    }
}
