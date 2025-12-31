using System.Windows;
using System.Windows.Input;
using Amazon.S3;

namespace S3Browser
{
    /// <summary>
    /// Dialog window for editing and executing custom SQL queries against Parquet files in S3.
    /// Provides syntax highlighting, error messages, and direct integration with DuckDB.
    /// </summary>
    public partial class QueryEditorDialog : Window
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;
        private readonly string? _awsProfile;
        private readonly string _folderName;
        private readonly ParquetViewerWindow? _existingViewerWindow;

        /// <summary>
        /// Initializes a new instance of the QueryEditorDialog.
        /// </summary>
        /// <param name="s3Client">AWS S3 client for accessing S3 resources.</param>
        /// <param name="bucketName">Name of the S3 bucket containing the Parquet files.</param>
        /// <param name="initialQuery">Initial SQL query to populate the editor.</param>
        /// <param name="folderName">Display name for the folder being queried.</param>
        /// <param name="awsProfile">AWS profile name for credential resolution.</param>
        /// <param name="existingViewerWindow">Optional existing ParquetViewerWindow to re-use instead of creating a new one.</param>
        public QueryEditorDialog(IAmazonS3 s3Client, string bucketName, string initialQuery, string folderName, string? awsProfile, ParquetViewerWindow? existingViewerWindow = null)
        {
            InitializeComponent();

            _s3Client = s3Client;
            _bucketName = bucketName;
            _awsProfile = awsProfile;
            _folderName = folderName;
            _existingViewerWindow = existingViewerWindow;

            // Set initial query
            QueryTextBox.Text = initialQuery;

            // Update title
            SubtitleTextBlock.Text = $"Querying: {folderName}";

            // Add keyboard shortcut for execution (Ctrl+Enter)
            QueryTextBox.PreviewKeyDown += QueryTextBox_PreviewKeyDown;

            // Focus on query text box
            QueryTextBox.Focus();
            QueryTextBox.SelectAll();
        }

        private void QueryTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Execute query on Ctrl+Enter
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                ExecuteQuery();
            }
        }

        private void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteQuery();
        }

        private void ExecuteQuery()
        {
            try
            {
                // Hide any previous error message
                MessageBorder.Visibility = Visibility.Collapsed;

                string query = QueryTextBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(query))
                {
                    ShowMessage("Please enter a SQL query.", isError: true);
                    return;
                }

                // Validate query (basic check)
                if (!query.Contains("read_parquet", StringComparison.OrdinalIgnoreCase) &&
                    !query.Contains("FROM", StringComparison.OrdinalIgnoreCase))
                {
                    ShowMessage("Query should contain 'read_parquet' function or 'FROM' clause.", isError: true);
                    return;
                }

                // Check if we should re-use an existing window or create a new one
                if (_existingViewerWindow != null)
                {
                    // Re-execute the query in the existing window
                    _existingViewerWindow.ExecuteNewQuery(query);
                }
                else
                {
                    // Open ParquetViewerWindow with custom query (new window)
                    var viewer = new ParquetViewerWindow(
                        _s3Client,
                        _bucketName,
                        query, // Pass the custom query as the key parameter
                        _folderName,
                        isWildcard: false, // Mark as not wildcard since we're using custom query
                        awsProfile: _awsProfile,
                        customQuery: query); // Pass custom query

                    viewer.Show();
                }

                // Close this dialog
                Close();
            }
            catch (Exception ex)
            {
                ShowMessage($"Error executing query: {ex.Message}", isError: true);
            }
        }

        private void ShowMessage(string message, bool isError)
        {
            MessageTextBox.Text = message;

            if (isError)
            {
                MessageBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 82, 82));
                MessageBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 235, 238));
            }
            else
            {
                MessageBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
                MessageBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 245, 233));
            }

            MessageBorder.Visibility = Visibility.Visible;
        }

        private void CloseMessageButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBorder.Visibility = Visibility.Collapsed;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
