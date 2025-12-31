using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace S3Browser.Converters
{
    /// <summary>
    /// Converter that truncates text to a maximum display length and shows only the first line for multi-line text.
    /// Used for displaying large text values in DataGrid cells without overwhelming the UI.
    /// </summary>
    public class SmartTruncateTextConverter : IValueConverter
    {
        /// <summary>
        /// Maximum number of characters to display before truncation (default: 50).
        /// </summary>
        public const int MaxDisplayLength = 50;

        /// <summary>
        /// Converts a value to a truncated string representation for display.
        /// Multi-line text shows only the first line. Text longer than MaxDisplayLength is truncated.
        /// </summary>
        /// <param name="value">The value to convert. Can be null or DBNull.</param>
        /// <param name="targetType">The type of the binding target property (not used).</param>
        /// <param name="parameter">Optional parameter (not used).</param>
        /// <param name="culture">The culture to use in the converter (not used).</param>
        /// <returns>
        /// Empty string if value is null or DBNull.
        /// First line (truncated to MaxDisplayLength) if multi-line text.
        /// Truncated text if longer than MaxDisplayLength.
        /// Original text otherwise.
        /// </returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || value == DBNull.Value)
                return string.Empty;

            string text = value.ToString() ?? string.Empty;

            // If text has multiple lines, show only first line
            if (text.Contains('\n'))
            {
                var firstLine = text.Split('\n')[0];
                if (firstLine.Length > MaxDisplayLength)
                    return firstLine.Substring(0, MaxDisplayLength);
                return firstLine;
            }

            // If text is longer than max characters, truncate
            if (text.Length > MaxDisplayLength)
                return text.Substring(0, MaxDisplayLength);

            return text;
        }

        /// <summary>
        /// ConvertBack is not supported for this one-way converter.
        /// </summary>
        /// <exception cref="NotImplementedException">Always thrown as this is a one-way converter.</exception>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("SmartTruncateTextConverter is a one-way converter.");
        }
    }

    /// <summary>
    /// Converter that determines if text needs expansion based on length or multi-line content.
    /// Returns Visibility.Visible if content exceeds MaxDisplayLength or contains multiple lines; Visibility.Collapsed otherwise.
    /// Used to show/hide expand buttons in DataGrid cells.
    /// </summary>
    public class NeedsExpansionConverter : IValueConverter
    {
        /// <summary>
        /// Converts a value to a Visibility enum indicating whether expansion UI should be shown.
        /// </summary>
        /// <param name="value">The value to analyze. Can be null or DBNull.</param>
        /// <param name="targetType">The type of the binding target property (not used).</param>
        /// <param name="parameter">Optional parameter (not used).</param>
        /// <param name="culture">The culture to use in the converter (not used).</param>
        /// <returns>
        /// Visibility.Collapsed if value is null, DBNull, or text is short (?50 chars) and single-line.
        /// Visibility.Visible if text is longer than 50 characters or contains multiple lines.
        /// </returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || value == DBNull.Value)
                return Visibility.Collapsed;

            string text = value.ToString() ?? string.Empty;

            // Show button if text is longer than max chars OR has multiple lines
            if (text.Length > SmartTruncateTextConverter.MaxDisplayLength || text.Contains('\n'))
                return Visibility.Visible;

            return Visibility.Collapsed;
        }

        /// <summary>
        /// ConvertBack is not supported for this one-way converter.
        /// </summary>
        /// <exception cref="NotImplementedException">Always thrown as this is a one-way converter.</exception>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("NeedsExpansionConverter is a one-way converter.");
        }
    }
}
