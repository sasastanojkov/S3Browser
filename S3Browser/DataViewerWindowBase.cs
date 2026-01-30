using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DuckDB.NET.Data;
using S3Browser.Converters;
using S3Browser.Helpers;
using S3Browser.Interfaces;

namespace S3Browser
{
    /// <summary>
    /// Abstract base class for data viewer windows (Parquet, CSV, TSV).
    /// Provides common functionality for displaying tabular data from S3 with geometry visualization support.
    /// </summary>
    public abstract class DataViewerWindowBase : Window, IQueryExecutor
    {
        // Common fields for all data viewers
        protected DuckDBConnection? _duckDbConnection;
        protected CancellationTokenSource? _cancellationTokenSource;
        protected Dictionary<int, List<GeometryMapWindow.GeometryInfo>> _rowGeometries = new();
        protected GeometryMapWindow? _currentMapWindow;
        protected int _lastSelectedRowIndex = -1;
        protected string _displayPath = string.Empty;
        protected bool _ignoreSelectionChange = false;
        protected string? _customQuery;
        protected string? _lastExecutedQuery;

        // Abstract properties - derived classes must provide access to their XAML controls
        // Named differently to avoid conflicts with XAML-generated properties
        protected abstract DataGrid DataGrid { get; }
        protected abstract TextBlock StatusText { get; }
        protected abstract string FileName { get; }

        /// <summary>
        /// Creates an expandable cell template for displaying truncated data with an expand button.
        /// </summary>
        /// <param name="columnName">The name of the column.</param>
        /// <returns>A DataTemplate for the cell.</returns>
        protected DataTemplate CreateExpandableCellTemplate(string columnName)
        {
            var template = new DataTemplate();

            // Create a Grid to hold truncated text and expand button
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

            // TextBlock for content (truncated)
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

            // Button to expand
            var buttonFactory = new FrameworkElementFactory(typeof(Button));
            buttonFactory.SetValue(Button.ContentProperty, "...");
            buttonFactory.SetValue(Button.PaddingProperty, new Thickness(8, 2, 8, 2));
            buttonFactory.SetValue(Button.MarginProperty, new Thickness(5, 0, 0, 0));
            buttonFactory.SetValue(Button.CursorProperty, Cursors.Hand);
            buttonFactory.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center);
            buttonFactory.SetValue(Grid.ColumnProperty, 1);
            buttonFactory.SetValue(Button.ToolTipProperty, "Click to view full content");
            buttonFactory.AddHandler(Button.PreviewMouseDownEvent, new MouseButtonEventHandler(ExpandButton_PreviewMouseDown), true);
            buttonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler(ExpandButton_Click));

            // Bind button Tag to full text and Visibility
            var fullTextBinding = new Binding($"[{columnName}]");
            buttonFactory.SetValue(Button.TagProperty, fullTextBinding);

            var visibilityBinding = new Binding($"[{columnName}]");
            visibilityBinding.Converter = new NeedsExpansionConverter();
            buttonFactory.SetBinding(Button.VisibilityProperty, visibilityBinding);

            gridFactory.AppendChild(buttonFactory);

            template.VisualTree = gridFactory;
            return template;
        }

        /// <summary>
        /// Handles the PreviewMouseDown event on expand buttons to prevent row selection.
        /// </summary>
        protected void ExpandButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Set flag immediately when mouse is pressed on expand button
            // Do NOT set e.Handled = true here, as it would prevent the Click event from firing
            _ignoreSelectionChange = true;
        }

        /// <summary>
        /// Handles the Click event on expand buttons to show full cell content.
        /// </summary>
        protected void ExpandButton_Click(object sender, RoutedEventArgs e)
        {
            // Prevent event bubbling
            e.Handled = true;

            try
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

                    // Format JSON with indentation if the content is JSON
                    string displayText = DataViewerUIHelper.FormatJsonIfValid(fullText ?? "");

                    var textBox = new TextBox
                    {
                        Text = displayText,
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
            finally
            {
                // Reset flag after a short delay to allow any pending selection events to be ignored
                Dispatcher.InvokeAsync(() =>
                {
                    _ignoreSelectionChange = false;
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Detects geometry columns in the DataTable and stores geometry info for each row.
        /// </summary>
        /// <param name="dataTable">The DataTable containing the loaded data.</param>
        protected void DetectAndStoreGeometries(DataTable dataTable)
        {
            _rowGeometries.Clear();

            // Detect which columns contain WKT geometry data
            var geometryColumns = GeometryHelper.DetectGeometryColumns(dataTable);

            if (geometryColumns.Count == 0)
                return;

            // Extract geometries from each row
            for (int rowIndex = 0; rowIndex < dataTable.Rows.Count; rowIndex++)
            {
                var geometries = GeometryHelper.ExtractGeometriesFromRow(dataTable.Rows[rowIndex], dataTable);
                if (geometries.Count > 0)
                {
                    _rowGeometries[rowIndex] = geometries;
                }
            }
        }

        /// <summary>
        /// Handles row selection changes to display geometries on map.
        /// </summary>
        protected void HandleResultsDataGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Ignore selection changes triggered by expand button clicks
            if (_ignoreSelectionChange)
                return;

            var selectedIndex = DataGrid.SelectedIndex;

            // Check if user clicked on the same row again (toggle behavior)
            if (selectedIndex == _lastSelectedRowIndex && selectedIndex >= 0)
            {
                // Deselect the row
                DataGrid.SelectedIndex = -1;
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
                                _lastSelectedRowIndex = -1;
                                DataGrid.SelectedIndex = -1;
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

        /// <summary>
        /// Handles keyboard shortcuts for copying cell content.
        /// </summary>
        protected void HandleResultsDataGridPreviewKeyDown(object sender, KeyEventArgs e)
        {
            DataViewerUIHelper.HandleCellCopyShortcut(DataGrid, e);
        }

        /// <summary>
        /// Shows all data in an expanded detailed view.
        /// </summary>
        protected void HandleExpandAllButtonClick(object sender, RoutedEventArgs e)
        {
            DataViewerUIHelper.ShowExpandAllDataWindow(DataGrid, this, FileName);
        }

        /// <summary>
        /// Executes a query using DuckDB.
        /// </summary>
        protected DataTable ExecuteQuery(string query, CancellationToken cancellationToken)
        {
            if (_duckDbConnection == null)
                throw new InvalidOperationException("DuckDB connection is not available.");

            return DuckDbManager.Instance.ExecuteQuery(_duckDbConnection, query, cancellationToken);
        }

        /// <summary>
        /// Cleans up resources including DuckDB connection and map window.
        /// </summary>
        protected void CleanupResources()
        {
            // Close map window if open
            if (_currentMapWindow != null)
            {
                _currentMapWindow.Close();
                _currentMapWindow = null;
            }

            // Cancel any ongoing operations
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            // Clean up DuckDB connection
            if (_duckDbConnection != null)
            {
                DuckDbManager.Instance.ReleaseConnection(_duckDbConnection);
                _duckDbConnection = null;
            }
        }

        /// <summary>
        /// Abstract method for executing new queries. Must be implemented by derived classes.
        /// </summary>
        public abstract void ExecuteNewQuery(string newQuery);
    }
}
