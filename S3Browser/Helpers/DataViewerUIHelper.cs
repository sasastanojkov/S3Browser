using System.Data;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace S3Browser.Helpers
{
    /// <summary>
    /// Helper class for common UI operations in data viewer windows.
    /// Provides reusable methods for expanding data views, copying cell content, and path management.
    /// </summary>
    public static class DataViewerUIHelper
    {
        /// <summary>
        /// Shows all data from a DataGrid in a comprehensive detailed view window.
        /// </summary>
        /// <param name="dataGrid">The DataGrid containing the data to display.</param>
        /// <param name="owner">The owner window for the dialog.</param>
        /// <param name="fileName">The file name to display in the title.</param>
        public static void ShowExpandAllDataWindow(DataGrid dataGrid, Window owner, string fileName)
        {
            if (dataGrid.ItemsSource == null)
            {
                MessageBox.Show("No data loaded to display.", "No Data",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (dataGrid.ItemsSource is not DataView dataView || dataView.Count == 0)
            {
                MessageBox.Show("No data loaded to display.", "No Data",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Create comprehensive view window
            var dialog = new Window
            {
                Title = $"All Data - {fileName}",
                Width = 900,
                Height = 650,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner
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
                    Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243))
                };
                stackPanel.Children.Add(rowHeader);

                // Row separator
                var separator = new Separator
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
                    string cellText;

                    if (cellValue == DBNull.Value || cellValue == null)
                    {
                        cellText = "(null)";
                    }
                    else if (cellValue is string strValue)
                    {
                        // It's already a string (could be JSON from complex type processing)
                        // Try to format as JSON if it looks like JSON
                        cellText = FormatJsonIfValid(strValue);
                    }
                    else
                    {
                        // For non-string types, try to format them properly
                        cellText = FormatCellValue(cellValue);
                    }

                    var valueBlock = new TextBlock
                    {
                        Text = cellText,
                        TextWrapping = TextWrapping.Wrap,
                        FontFamily = new FontFamily("Consolas"),
                        Margin = new Thickness(0, 0, 0, 5),
                        Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
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
            var statusBar = new StatusBar();
            var statusText = new TextBlock
            {
                Text = $"Showing {dataTable.Rows.Count:N0} rows with {dataTable.Columns.Count} columns"
            };
            var statusItem = new StatusBarItem { Content = statusText };
            statusBar.Items.Add(statusItem);
            Grid.SetRow(statusBar, 1);
            grid.Children.Add(statusBar);

            dialog.Content = grid;
            dialog.Show();
        }

        /// <summary>
        /// Handles Ctrl+C keyboard shortcut to copy full cell content from a DataGrid.
        /// </summary>
        /// <param name="dataGrid">The DataGrid to copy from.</param>
        /// <param name="e">The keyboard event args.</param>
        public static void HandleCellCopyShortcut(DataGrid dataGrid, KeyEventArgs e)
        {
            // Handle Ctrl+C to copy full cell content
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var currentCell = dataGrid.CurrentCell;
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

        /// <summary>
        /// Copies a path to clipboard with retry logic and visual feedback.
        /// </summary>
        /// <param name="path">The path to copy.</param>
        /// <param name="statusTextBlock">TextBlock to update with status message.</param>
        /// <param name="border">Optional Border to flash for visual feedback.</param>
        /// <returns>True if copy succeeded; false otherwise.</returns>
        public static bool CopyPathToClipboard(string path, TextBlock statusTextBlock, Border? border = null)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                // Retry clipboard operation if it fails (common issue when clipboard is busy)
                bool clipboardSet = false;
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        Clipboard.SetDataObject(path, true);
                        clipboardSet = true;
                        break;
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        if (i < 2)
                        {
                            System.Threading.Thread.Sleep(100);
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

                if (clipboardSet)
                {
                    statusTextBlock.Text = $"Copied to clipboard: {path}";

                    // Flash the background to indicate copy
                    if (border != null)
                    {
                        var originalBrush = border.Background;
                        border.Background = new SolidColorBrush(Color.FromRgb(200, 230, 201)); // Light green

                        // Schedule the background to be restored after 200ms
                        var dispatcher = border.Dispatcher;
                        Task.Delay(200).ContinueWith(_ =>
                        {
                            dispatcher.Invoke(() => border.Background = originalBrush);
                        });
                    }

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy path: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Formats a cell value for display, handling complex types that might have been missed.
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <returns>Formatted string representation of the value.</returns>
        private static string FormatCellValue(object value)
        {
            if (value == null)
                return string.Empty;

            var type = value.GetType();

            // Check if it's a simple type that ToString works well for
            if (type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) ||
                type == typeof(decimal) || type == typeof(Guid) || type.IsEnum)
            {
                return value.ToString() ?? string.Empty;
            }

            // Check if it's a complex type (Dictionary, List, etc.) that wasn't processed
            if (type.IsGenericType || value is System.Collections.IDictionary ||
                (value is System.Collections.IEnumerable && !(value is string)))
            {
                try
                {
                    // Try to serialize to JSON for better display
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    return JsonSerializer.Serialize(value, options);
                }
                catch
                {
                    // If JSON serialization fails, fall back to ToString
                    return value.ToString() ?? string.Empty;
                }
            }

            // Default: use ToString
            return value.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Formats a string as indented JSON if it's valid JSON, otherwise returns the original string.
        /// Used for displaying expanded content in dialogs and detail views.
        /// </summary>
        /// <param name="text">The text to format.</param>
        /// <returns>Formatted JSON with indentation if valid JSON; otherwise the original text.</returns>
        public static string FormatJsonIfValid(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Check if the text starts with { or [ (likely JSON)
            text = text.Trim();
            if (!text.StartsWith("{") && !text.StartsWith("["))
                return text;

            try
            {
                // Try to parse and reformat as indented JSON
                using var document = JsonDocument.Parse(text);
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                return JsonSerializer.Serialize(document.RootElement, options);
            }
            catch
            {
                // If it's not valid JSON, return as-is
                return text;
            }
        }
    }
}
