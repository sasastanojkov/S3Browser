using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using DuckDB.NET.Data;
using S3Browser.Converters;
using S3Browser.Helpers;
using S3Browser.Interfaces;

namespace S3Browser
{
    /// <summary>
    /// Window for viewing tabular files (CSV and TSV) from S3.
    /// Uses DuckDB engine to process files directly from S3 without downloading.
    /// Supports CSV and TSV file formats with automatic delimiter detection, custom SQL queries, and geometry visualization.
    /// </summary>
    public partial class TabularFileViewerWindow : DataViewerWindowBase
    {
        private readonly string _bucketName;
        private readonly string _key;
        private readonly string _fileName;
        private readonly string _fileType; // "csv" or "tsv"

        // Override abstract properties to provide access to XAML controls
        protected override DataGrid DataGrid => ResultsDataGrid;
        protected override TextBlock StatusText => StatusTextBlock;
        protected override string FileName => _fileName;

        /// <summary>
        /// Initializes a new instance of the <see cref="TabularFileViewerWindow"/> class.
        /// </summary>
        /// <param name="bucketName">Name of the S3 bucket containing the file.</param>
        /// <param name="key">S3 key (path) to the file.</param>
        /// <param name="fileName">Display name for the file.</param>
        /// <param name="fileType">Type of file: "csv" or "tsv". Optional if customQuery is provided.</param>
        /// <param name="customQuery">Optional custom SQL query to execute instead of default query.</param>
        public TabularFileViewerWindow(string bucketName, string key, string fileName, string? fileType = null, string? customQuery = null)
        {
            InitializeComponent();

            _bucketName = bucketName;
            _key = key;
            _fileName = fileName;
            _fileType = fileType?.ToLowerInvariant() ?? "csv";
            _customQuery = customQuery;

            // Build S3 path for display
            _displayPath = $"s3://{_bucketName}/{_key}";

            FileNameTextBlock.Text = $"{_fileType.ToUpperInvariant()} File: {fileName}";
            FilePathTextBlock.Text = _displayPath;
            FilePathTextBlock.Visibility = Visibility.Visible;
            Title = $"{_fileType.ToUpperInvariant()} Viewer - {fileName}";

            // Subscribe to row selection changes for geometry display
            ResultsDataGrid.SelectionChanged += HandleResultsDataGridSelectionChanged;

            // Subscribe to keyboard events for copy functionality
            ResultsDataGrid.PreviewKeyDown += HandleResultsDataGridPreviewKeyDown;

            InitializeDuckDbConnectionAsync();
        }

        private async void InitializeDuckDbConnectionAsync()
        {
            try
            {
                StatusTextBlock.Text = "Initializing connection...";
                LoadingOverlay.Visibility = Visibility.Visible;

                // Use DuckDbManager to create connection with proper S3 access
                _duckDbConnection = await DuckDbManager.Instance.CreateConnectionForBucketAsync(_bucketName);

                LoadTabularDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing DuckDB connection: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "Error initializing connection";
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void LoadTabularDataAsync()
        {
            // Cancel any existing operation
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;

            // Disable controls during loading
            HasHeaderCheckBox.IsEnabled = false;
            RowLimitComboBox.IsEnabled = false;
            ResultsDataGrid.ItemsSource = null;
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingMessageTextBlock.Text = "Reading file...";
            StatusTextBlock.Text = "Querying file...";

            try
            {
                if (_duckDbConnection == null)
                {
                    throw new InvalidOperationException("DuckDB connection is not initialized.");
                }

                var selectedItem = RowLimitComboBox.SelectedItem as ComboBoxItem;
                if (selectedItem == null) return;

                int rowLimit = Convert.ToInt32(selectedItem.Tag);
                bool hasHeader = HasHeaderCheckBox.IsChecked ?? false;

                LoadingMessageTextBlock.Text = "Preparing query...";
                StatusTextBlock.Text = "Reading data...";

                string query;

                if (!string.IsNullOrEmpty(_customQuery))
                {
                    // Use custom query provided by user
                    LoadingMessageTextBlock.Text = "Executing custom query...";
                    StatusTextBlock.Text = "Running custom query...";

                    // Apply row limit to custom query if specified
                    query = _customQuery.Trim();
                    if (rowLimit != -1)
                    {
                        // Check if query already has LIMIT clause
                        if (!query.Contains("LIMIT", StringComparison.OrdinalIgnoreCase))
                        {
                            query += $" LIMIT {rowLimit}";
                        }
                    }
                }
                else
                {
                    // Build S3 path
                    string s3Path = $"s3://{_bucketName}/{_key}";

                    LoadingMessageTextBlock.Text = "Executing query...";
                    StatusTextBlock.Text = "Reading data...";

                    // Build DuckDB query for CSV/TSV
                    query = BuildDuckDbQuery(s3Path, _fileType, hasHeader, rowLimit);
                }

                // Store the query that will be executed
                _lastExecutedQuery = query;

                // Execute query on background thread
                var dataTable = await Task.Run(() => ExecuteQuery(query, cancellationToken), cancellationToken);

                // Detect and store geometries from data
                DetectAndStoreGeometries(dataTable);

                // Create custom columns with expandable cells
                CreateCustomColumns(dataTable);

                ResultsDataGrid.ItemsSource = dataTable.DefaultView;

                int rowCount = dataTable.Rows.Count;
                if (!string.IsNullOrEmpty(_customQuery))
                {
                    StatusTextBlock.Text = $"Query executed successfully: {rowCount:N0} rows returned";
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
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Operation cancelled";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "Error loading file";
            }
            finally
            {
                // Re-enable controls and hide loading overlay
                LoadingOverlay.Visibility = Visibility.Collapsed;
                HasHeaderCheckBox.IsEnabled = true;
                RowLimitComboBox.IsEnabled = true;
            }
        }

        private string BuildDuckDbQuery(string s3Path, string fileType, bool hasHeader, int rowLimit)
        {
            string delimiter = fileType == "tsv" ? "E'\\t'" : "','";  // Tab for TSV, comma for CSV
            string headerParam = hasHeader ? "true" : "false";

            string query = $"SELECT * FROM read_csv('{s3Path}', delim={delimiter}, header={headerParam}, auto_detect=true)";

            if (rowLimit != -1)
            {
                query += $" LIMIT {rowLimit}";
            }

            return query;
        }

        private DataTable ExecuteQuery(string query, CancellationToken cancellationToken)
        {
            // Use base class implementation
            return base.ExecuteQuery(query, cancellationToken);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Use base class cleanup
            CleanupResources();
            base.OnClosing(e);
        }

        private void CreateCustomColumns(DataTable dataTable)
        {
            ResultsDataGrid.AutoGenerateColumns = false;
            ResultsDataGrid.Columns.Clear();

            foreach (DataColumn column in dataTable.Columns)
            {
                var templateColumn = new DataGridTemplateColumn
                {
                    Header = column.ColumnName,
                    Width = DataGridLength.Auto, // Auto-size based on content
                    MinWidth = 100,
                    MaxWidth = 400, // Prevent extremely wide columns
                    CanUserResize = true, // Allow manual resizing
                    CellTemplate = CreateExpandableCellTemplate(column.ColumnName) // Use base class method
                };
                ResultsDataGrid.Columns.Add(templateColumn);
            }
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            LoadTabularDataAsync();
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
                    else
                    {
                        string s3Path = $"s3://{_bucketName}/{_key}";
                        bool hasHeader = HasHeaderCheckBox.IsChecked ?? false;
                        string delimiter = _fileType == "tsv" ? "E'\\t'" : "','";
                        string headerParam = hasHeader ? "true" : "false";
                        queryToEdit = $"SELECT * FROM read_csv('{s3Path}', delim={delimiter}, header={headerParam}, auto_detect=true)";
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
            LoadTabularDataAsync();

            // Bring window to front
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
        }

        private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
        {
            // Use base class method
            HandleExpandAllButtonClick(sender, e);
        }

        private void FilePathBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DataViewerUIHelper.CopyPathToClipboard(_displayPath, StatusTextBlock, FilePathBorder);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }
    }
}
