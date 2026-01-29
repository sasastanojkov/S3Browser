using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using DuckDB.NET.Data;
using S3Browser.Services;

namespace S3Browser
{
    /// <summary>
    /// Converter for truncating text in tabular file viewer cells.
    /// </summary>
    public class TabularSmartTruncateTextConverter : IValueConverter
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
    /// Converter that determines if text needs expansion in tabular file viewer.
    /// </summary>
    public class TabularNeedsExpansionConverter : IValueConverter
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
    /// Window for viewing tabular files (CSV and TSV) from S3.
    /// Uses DuckDB engine to process files directly from S3 without downloading.
    /// Supports CSV and TSV file formats with automatic delimiter detection.
    /// </summary>
    public partial class TabularFileViewerWindow : Window
    {
        private readonly string _bucketName;
        private readonly string _key;
        private readonly string _fileName;
        private readonly string _fileType; // "csv" or "tsv"
        private DuckDBConnection? _duckDbConnection;
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="TabularFileViewerWindow"/> class.
        /// </summary>
        /// <param name="bucketName">Name of the S3 bucket containing the file.</param>
        /// <param name="key">S3 key (path) to the file.</param>
        /// <param name="fileName">Display name for the file.</param>
        /// <param name="fileType">Type of file: "csv" or "tsv".</param>
        public TabularFileViewerWindow(string bucketName, string key, string fileName, string fileType)
        {
            InitializeComponent();

            _bucketName = bucketName;
            _key = key;
            _fileName = fileName;
            _fileType = fileType.ToLowerInvariant();

            FileNameTextBlock.Text = $"{fileType.ToUpperInvariant()} File: {fileName}";
            Title = $"{fileType.ToUpperInvariant()} Viewer - {fileName}";

            InitializeDuckDbConnectionAsync();
        }

        private async void InitializeDuckDbConnectionAsync()
        {
            try
            {
                StatusTextBlock.Text = "Initializing connection...";
                LoadingOverlay.Visibility = Visibility.Visible;

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

                // Build S3 path
                string s3Path = $"s3://{_bucketName}/{_key}";

                LoadingMessageTextBlock.Text = "Executing query...";
                StatusTextBlock.Text = "Reading data...";

                // Build DuckDB query for CSV/TSV
                string query = BuildDuckDbQuery(s3Path, _fileType, hasHeader, rowLimit);

                // Execute query on background thread
                var dataTable = await Task.Run(() => ExecuteQuery(query, cancellationToken), cancellationToken);

                // Create custom columns with expandable cells
                CreateCustomColumns(dataTable);

                ResultsDataGrid.ItemsSource = dataTable.DefaultView;

                int rowCount = dataTable.Rows.Count;
                if (rowLimit == -1)
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
            if (_duckDbConnection == null)
                throw new InvalidOperationException("DuckDB connection is not available.");

            var dataTable = new DataTable();

            using (var command = _duckDbConnection.CreateCommand())
            {
                command.CommandText = query;

                using (var reader = command.ExecuteReader())
                {
                    // Create columns from reader schema
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        string columnName = reader.GetName(i);
                        dataTable.Columns.Add(columnName, typeof(string)); // Use string for all columns to handle mixed types
                    }

                    // Read data rows
                    while (reader.Read())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var row = dataTable.NewRow();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[i] = reader.IsDBNull(i) ? string.Empty : reader.GetValue(i)?.ToString() ?? string.Empty;
                        }
                        dataTable.Rows.Add(row);
                    }
                }
            }

            return dataTable;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Cancel any running operations
            _cancellationTokenSource?.Cancel();

            // Clean up DuckDB connection safely
            if (_duckDbConnection != null)
            {
                try
                {
                    if (_duckDbConnection.State == System.Data.ConnectionState.Open)
                    {
                        _duckDbConnection.Close();
                    }
                    _duckDbConnection.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
                finally
                {
                    _duckDbConnection = null;
                }
            }

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
                    CellTemplate = CreateExpandableCellTemplate(column.ColumnName)
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
            textBinding.Converter = new TabularSmartTruncateTextConverter();
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
            buttonFactory.SetValue(Button.CursorProperty, System.Windows.Input.Cursors.Hand);
            buttonFactory.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center);
            buttonFactory.SetValue(Grid.ColumnProperty, 1);
            buttonFactory.SetValue(Button.ToolTipProperty, "Click to view full content");
            buttonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler(ExpandButton_Click));

            // Bind button Tag to full text and Visibility to whether text needs expansion
            var fullTextBinding = new Binding($"[{columnName}]");
            buttonFactory.SetValue(Button.TagProperty, fullTextBinding);

            var visibilityBinding = new Binding($"[{columnName}]");
            visibilityBinding.Converter = new TabularNeedsExpansionConverter();
            buttonFactory.SetBinding(Button.VisibilityProperty, visibilityBinding);

            gridFactory.AppendChild(buttonFactory);

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

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            LoadTabularDataAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Cancel any ongoing operations
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            // Connection should already be cleaned up in OnClosing
            // Just ensure cancellation token is disposed
        }
    }
}
