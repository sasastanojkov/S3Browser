using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DuckDB.NET.Data;
using S3Browser.Interfaces;

namespace S3Browser
{
    /// <summary>
    /// Defines the query execution mode for the SQL query dialog.
    /// </summary>
    public enum QueryExecutionMode
    {
        /// <summary>
        /// Executes DDL/DML queries that don't return data (CREATE, DROP, INSERT, UPDATE, DELETE).
        /// Uses ExecuteNonQuery() and shows success/error messages.
        /// </summary>
        Ddl,

        /// <summary>
        /// Executes SELECT queries that return data and opens a viewer window.
        /// </summary>
        DataQuery
    }

    /// <summary>
    /// Unified dialog window for executing SQL queries against DuckDB.
    /// Supports two modes:
    /// 1. DDL mode: Execute DDL/DML queries without returning data (CREATE TABLE, CREATE VIEW, etc.)
    /// 2. DataQuery mode: Execute SELECT queries and display results in a viewer window.
    /// </summary>
    public partial class SqlQueryDialog : Window
    {
        private readonly QueryExecutionMode _mode;
        private readonly DuckDBConnection? _duckDbConnection;
        private readonly string _bucketName;
        private readonly string _contextInfo;
        private readonly IQueryExecutor? _existingViewerWindow;
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>
        /// Initializes a new instance for DDL mode.
        /// </summary>
        /// <param name="duckDbConnection">Active DuckDB connection to execute queries against.</param>
        /// <param name="bucketName">Name of the S3 bucket for context.</param>
        /// <param name="contextInfo">Additional context information to display (e.g., current file or folder name).</param>
        /// <param name="initialQuery">Optional initial query to populate the editor.</param>
        public SqlQueryDialog(DuckDBConnection duckDbConnection, string bucketName, string contextInfo, string? initialQuery = null)
        {
            InitializeComponent();

            _mode = QueryExecutionMode.Ddl;
            _duckDbConnection = duckDbConnection ?? throw new ArgumentNullException(nameof(duckDbConnection));
            _bucketName = bucketName;
            _contextInfo = contextInfo;

            ConfigureForDdlMode(initialQuery);
        }

        /// <summary>
        /// Initializes a new instance for DataQuery mode.
        /// </summary>
        /// <param name="bucketName">Name of the S3 bucket containing the files.</param>
        /// <param name="initialQuery">Initial SQL query to populate the editor.</param>
        /// <param name="contextInfo">Display name for the context being queried.</param>
        /// <param name="existingViewerWindow">Optional existing viewer window implementing IQueryExecutor to re-use instead of creating a new one.</param>
        public SqlQueryDialog(string bucketName, string initialQuery, string contextInfo, IQueryExecutor? existingViewerWindow = null)
        {
            InitializeComponent();

            _mode = QueryExecutionMode.DataQuery;
            _bucketName = bucketName;
            _contextInfo = contextInfo;
            _existingViewerWindow = existingViewerWindow;

            ConfigureForDataQueryMode(initialQuery);
        }

        private void ConfigureForDdlMode(string? initialQuery)
        {
            TitleTextBlock.Text = "DDL Query Editor";
            SubtitleTextBlock.Text = $"Context: {_contextInfo}";

            // Show info section with examples
            InfoBorder.Visibility = Visibility.Visible;
            InfoTextBlock.Inlines.Clear();
            InfoTextBlock.Inlines.Add(new System.Windows.Documents.Run("Create database objects (tables, views) for use in query editor.") { FontWeight = FontWeights.SemiBold });
            InfoTextBlock.Inlines.Add(new System.Windows.Documents.LineBreak());
            InfoTextBlock.Inlines.Add(new System.Windows.Documents.Run("Examples:") { Foreground = Brushes.Gray });
            InfoTextBlock.Inlines.Add(new System.Windows.Documents.LineBreak());
            InfoTextBlock.Inlines.Add(new System.Windows.Documents.Run("CREATE TABLE my_data AS SELECT * FROM read_parquet('s3://bucket/path/*.parquet')")
            {
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(25, 118, 210))
            });
            InfoTextBlock.Inlines.Add(new System.Windows.Documents.LineBreak());
            InfoTextBlock.Inlines.Add(new System.Windows.Documents.Run("CREATE VIEW my_view AS SELECT col1, col2 FROM my_data WHERE condition")
            {
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(25, 118, 210))
            });

            // Set initial query if provided
            if (!string.IsNullOrEmpty(initialQuery))
            {
                QueryTextBox.Text = initialQuery;
            }

            PlaceholderTextBlock.Text = "Enter your DDL query here (Ctrl+Enter to execute)...";

            // Focus on query text box
            QueryTextBox.PreviewKeyDown += QueryTextBox_PreviewKeyDown;
            QueryTextBox.Focus();
        }

        private void ConfigureForDataQueryMode(string initialQuery)
        {
            TitleTextBlock.Text = "SQL Query Editor";
            SubtitleTextBlock.Text = $"Querying: {_contextInfo}";

            // Hide info section for data query mode
            InfoBorder.Visibility = Visibility.Collapsed;

            // Set initial query
            QueryTextBox.Text = initialQuery;

            PlaceholderTextBlock.Text = "Enter your SQL query here (Ctrl+Enter to execute)...";

            // Focus on query text box and select all
            QueryTextBox.PreviewKeyDown += QueryTextBox_PreviewKeyDown;
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
            if (_mode == QueryExecutionMode.Ddl)
            {
                ExecuteDdlQuery();
            }
            else
            {
                ExecuteDataQuery();
            }
        }

        private async void ExecuteDdlQuery()
        {
            try
            {
                // Hide any previous message
                MessageBorder.Visibility = Visibility.Collapsed;

                string query = QueryTextBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(query))
                {
                    ShowMessage("Please enter a SQL query.", isError: true);
                    return;
                }

                // Validate that this is a DDL query (CREATE, DROP, ALTER, etc.)
                var upperQuery = query.ToUpperInvariant();
                bool isDdlQuery = upperQuery.StartsWith("CREATE ") ||
                                      upperQuery.StartsWith("DROP ") ||
                                      upperQuery.StartsWith("ALTER ") ||
                                      upperQuery.StartsWith("INSERT ") ||
                                      upperQuery.StartsWith("UPDATE ") ||
                                      upperQuery.StartsWith("DELETE ");

                if (!isDdlQuery)
                {
                    var confirmResult = MessageBox.Show(
                        "This query doesn't appear to be a DDL query (CREATE, DROP, ALTER, INSERT, UPDATE, DELETE).\n\n" +
                        "This dialog is for creating database objects without returning data.\n\n" +
                        "Do you want to execute it anyway?",
                        "Confirm Execution",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirmResult != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                // Cancel any existing operation
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();
                var cancellationToken = _cancellationTokenSource.Token;

                // Show loading indicator
                LoadingOverlay.Visibility = Visibility.Visible;
                LoadingMessageTextBlock.Text = "Executing query...";

                // Disable Execute button, enable Cancel button
                QueryTextBox.IsEnabled = false;
                ExecuteButton.IsEnabled = false;
                CancelButton.Content = "Cancel";

                // Execute query on background thread using DuckDbManager
                await Task.Run(() =>
                {
                    DuckDbManager.Instance.ExecuteNonQuery(_duckDbConnection!, query, cancellationToken);
                }, cancellationToken);

                // Show success message
                ShowMessage("Query executed successfully.", isError: false);
            }
            catch (OperationCanceledException)
            {
                ShowMessage("Query execution cancelled.", isError: false);
            }
            catch (Exception ex)
            {
                ShowMessage($"Error executing query: {ex.Message}", isError: true);
            }
            finally
            {
                // Hide loading indicator and re-enable controls
                LoadingOverlay.Visibility = Visibility.Collapsed;
                QueryTextBox.IsEnabled = true;
                ExecuteButton.IsEnabled = true;
                CancelButton.Content = "Close";
            }
        }

        private void ExecuteDataQuery()
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
                    !query.Contains("read_csv", StringComparison.OrdinalIgnoreCase) &&
                    !query.Contains("FROM", StringComparison.OrdinalIgnoreCase))
                {
                    ShowMessage("Query should contain 'read_parquet', 'read_csv' function or 'FROM' clause.", isError: true);
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
                    // Determine file type from query and open appropriate viewer
                    if (query.Contains("read_parquet", StringComparison.OrdinalIgnoreCase))
                    {
                        var viewer = new ParquetViewerWindow(
                            _bucketName,
                            string.Empty, // Don't pass query as key - it will be used via customQuery parameter
                            _contextInfo,
                            isWildcard: false,
                            customQuery: query);
                        viewer.Show();
                    }
                    else if (query.Contains("read_csv", StringComparison.OrdinalIgnoreCase))
                    {
                        var viewer = new TabularFileViewerWindow(
                            _bucketName,
                            string.Empty, // Don't pass query as key - it will be used via customQuery parameter
                            _contextInfo,
                            customQuery: query);
                        viewer.Show();
                    }
                    else
                    {
                        ShowMessage("Unable to determine file type. Query should contain 'read_parquet' or 'read_csv'.", isError: true);
                        return;
                    }
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
                MessageBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 82, 82));
                MessageBorder.Background = new SolidColorBrush(Color.FromRgb(255, 235, 238));
            }
            else
            {
                MessageBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                MessageBorder.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233));
            }

            MessageBorder.Visibility = Visibility.Visible;
        }

        private void CloseMessageButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBorder.Visibility = Visibility.Collapsed;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Cancel any running operations
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // If a query is running, cancel it
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
            }

            // Close the window
            Close();
        }
    }
}
