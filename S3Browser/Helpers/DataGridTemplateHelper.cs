using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using S3Browser.Converters;

namespace S3Browser.Helpers
{
    /// <summary>
    /// Helper class for creating consistent DataGrid cell templates with expandable content.
    /// </summary>
    public static class DataGridTemplateHelper
    {
        /// <summary>
        /// Creates a cell template with truncated text and an expand button.
        /// The template shows truncated text (max 50 characters or first line) with an optional expand button
        /// that appears only when content is truncated or multi-line.
        /// </summary>
        /// <param name="columnName">The name of the column to bind to in the data source.</param>
        /// <param name="expandClickHandler">The event handler to invoke when the expand button is clicked.</param>
        /// <returns>A DataTemplate configured for expandable content display.</returns>
        public static DataTemplate CreateExpandableCellTemplate(string columnName, RoutedEventHandler expandClickHandler)
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
            var textBinding = new Binding($"[{columnName}]")
            {
                Converter = new SmartTruncateTextConverter()
            };
            textBlockFactory.SetBinding(TextBlock.TextProperty, textBinding);
            textBlockFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.None);
            textBlockFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
            textBlockFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            textBlockFactory.SetValue(TextBlock.PaddingProperty, new Thickness(4, 2, 4, 2));
            textBlockFactory.SetValue(Grid.ColumnProperty, 0);
            gridFactory.AppendChild(textBlockFactory);

            // Expand button
            var buttonFactory = new FrameworkElementFactory(typeof(Button));
            buttonFactory.SetValue(Button.ContentProperty, "...");
            buttonFactory.SetValue(Button.PaddingProperty, new Thickness(8, 2, 8, 2));
            buttonFactory.SetValue(Button.MarginProperty, new Thickness(5, 0, 0, 0));
            buttonFactory.SetValue(Button.CursorProperty, Cursors.Hand);
            buttonFactory.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center);
            buttonFactory.SetValue(Grid.ColumnProperty, 1);
            buttonFactory.SetValue(Button.ToolTipProperty, "Click to view full content");
            buttonFactory.AddHandler(Button.ClickEvent, expandClickHandler);

            // Bind button Tag to full text and Visibility
            var fullTextBinding = new Binding($"[{columnName}]");
            buttonFactory.SetValue(Button.TagProperty, fullTextBinding);

            var visibilityBinding = new Binding($"[{columnName}]")
            {
                Converter = new NeedsExpansionConverter()
            };
            buttonFactory.SetBinding(Button.VisibilityProperty, visibilityBinding);

            gridFactory.AppendChild(buttonFactory);

            template.VisualTree = gridFactory;
            return template;
        }

        /// <summary>
        /// Shows the full content of a cell in a modal dialog window.
        /// Displays content in a read-only text box with scroll bars, using Consolas font for better readability.
        /// </summary>
        /// <param name="fullText">The full text content to display. Can be null.</param>
        /// <param name="owner">The owner window for the dialog, used for centering and modal behavior.</param>
        public static void ShowFullContentDialog(string? fullText, Window owner)
        {
            var dialog = new Window
            {
                Title = "Full Content",
                Width = 600,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var textBox = new TextBox
            {
                Text = fullText ?? string.Empty,
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
}
