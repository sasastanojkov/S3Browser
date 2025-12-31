using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Amazon.S3;
using Amazon.S3.Model;

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
    /// Downloads files temporarily and parses them with custom delimiter handling.
    /// </summary>
    public partial class TabularFileViewerWindow : Window
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;
        private readonly string _key;
        private readonly string _fileName;
        private readonly string _fileType; // "csv" or "tsv"
        private string? _localFilePath;
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>
        /// Initializes a new instance of the TabularFileViewerWindow.
        /// </summary>
        /// <param name="s3Client">AWS S3 client for accessing S3 resources.</param>
        /// <param name="bucketName">Name of the S3 bucket containing the file.</param>
        /// <param name="key">S3 key (path) to the file.</param>
        /// <param name="fileName">Display name for the file.</param>
        /// <param name="fileType">Type of file: "csv" or "tsv".</param>
        public TabularFileViewerWindow(IAmazonS3 s3Client, string bucketName, string key, string fileName, string fileType)
        {
            InitializeComponent();

            _s3Client = s3Client;
            _bucketName = bucketName;
            _key = key;
            _fileName = fileName;
            _fileType = fileType.ToLowerInvariant();

            FileNameTextBlock.Text = $"{fileType.ToUpperInvariant()} File: {fileName}";
            Title = $"{fileType.ToUpperInvariant()} Viewer - {fileName}";

            LoadTabularDataAsync();
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
            LoadingMessageTextBlock.Text = "Downloading file...";
            StatusTextBlock.Text = "Downloading...";

            try
            {
                var selectedItem = RowLimitComboBox.SelectedItem as ComboBoxItem;
                if (selectedItem == null) return;

                int rowLimit = Convert.ToInt32(selectedItem.Tag);
                bool hasHeader = HasHeaderCheckBox.IsChecked ?? false;

                // Download the file to a temporary location if not already downloaded
                if (_localFilePath == null || !File.Exists(_localFilePath))
                {
                    _localFilePath = Path.Combine(Path.GetTempPath(), $"s3browser_{Guid.NewGuid()}_{_fileName}");

                    var request = new GetObjectRequest
                    {
                        BucketName = _bucketName,
                        Key = _key
                    };

                    using (var response = await _s3Client.GetObjectAsync(request, cancellationToken))
                    {
                        await response.WriteResponseStreamToFileAsync(_localFilePath, false, cancellationToken);
                    }
                }

                LoadingMessageTextBlock.Text = "Parsing file...";
                StatusTextBlock.Text = "Parsing file...";

                // Parse the CSV/TSV file on background thread
                var dataTable = await Task.Run(() => ParseDelimitedFile(_localFilePath, _fileType, hasHeader, rowLimit, cancellationToken), cancellationToken);

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

        private DataTable ParseDelimitedFile(string filePath, string fileType, bool hasHeader, int rowLimit, CancellationToken cancellationToken)
        {
            var dataTable = new DataTable();
            char delimiter = fileType == "tsv" ? '\t' : ',';

            using (var reader = new StreamReader(filePath))
            {
                bool isFirstRow = true;
                int columnCount = 0;
                int rowsRead = 0;

                while (!reader.EndOfStream && (rowLimit == -1 || rowsRead < rowLimit))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? line = reader.ReadLine();
                    if (string.IsNullOrEmpty(line)) continue;

                    var values = ParseDelimitedLine(line, delimiter);

                    if (isFirstRow)
                    {
                        columnCount = values.Length;

                        if (hasHeader)
                        {
                            // Use first row as column headers
                            for (int i = 0; i < values.Length; i++)
                            {
                                dataTable.Columns.Add(values[i]);
                            }
                            isFirstRow = false;
                            continue;
                        }
                        else
                        {
                            // Create default column headers
                            for (int i = 0; i < values.Length; i++)
                            {
                                dataTable.Columns.Add($"Column{i + 1}");
                            }
                        }
                        isFirstRow = false;
                    }

                    // Add row data
                    var row = dataTable.NewRow();
                    for (int i = 0; i < Math.Min(values.Length, columnCount); i++)
                    {
                        row[i] = values[i];
                    }
                    dataTable.Rows.Add(row);
                    rowsRead++;
                }
            }

            // Create custom columns with expandable cells
            CreateCustomColumns(dataTable);

            return dataTable;
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
                dialog.ShowDialog();
            }
        }

        private string[] ParseDelimitedLine(string line, char delimiter)
        {
            var values = new List<string>();
            bool inQuotes = false;
            var currentValue = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        // Escaped quote
                        currentValue.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    values.Add(currentValue.ToString());
                    currentValue.Clear();
                }
                else
                {
                    currentValue.Append(c);
                }
            }

            values.Add(currentValue.ToString());
            return values.ToArray();
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            LoadTabularDataAsync();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Cancel any ongoing operations
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            // Clean up temporary file
            if (_localFilePath != null && File.Exists(_localFilePath))
            {
                try
                {
                    File.Delete(_localFilePath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}
