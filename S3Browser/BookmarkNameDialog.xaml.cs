using System.Windows;
using System.Windows.Input;

namespace S3Browser
{
    /// <summary>
    /// Dialog for entering a bookmark name.
    /// </summary>
    public partial class BookmarkNameDialog : Window
    {
        public string BookmarkName { get; private set; } = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="BookmarkNameDialog"/> class.
        /// </summary>
        /// <param name="s3Path">The S3 path being bookmarked.</param>
        public BookmarkNameDialog(string s3Path)
        {
            InitializeComponent();

            S3PathTextBlock.Text = $"Location: {s3Path}";

            // Generate default bookmark name from path
            string defaultName = GenerateDefaultName(s3Path);
            BookmarkNameTextBox.Text = defaultName;
            BookmarkNameTextBox.SelectAll();
            BookmarkNameTextBox.Focus();
        }

        private string GenerateDefaultName(string s3Path)
        {
            try
            {
                // Remove s3:// prefix
                if (s3Path.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
                {
                    s3Path = s3Path.Substring(5);
                }

                // Split into bucket and path
                var parts = s3Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    return "My Bookmark";
                }

                if (parts.Length == 1)
                {
                    // Just bucket name
                    return parts[0];
                }

                // Return last folder name or bucket/folder
                string lastPart = parts[parts.Length - 1];
                return $"{parts[0]}/{lastPart}";
            }
            catch
            {
                return "My Bookmark";
            }
        }

        private void BookmarkNameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SaveBookmark();
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveBookmark();
        }

        private void SaveBookmark()
        {
            string name = BookmarkNameTextBox.Text.Trim();

            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Please enter a bookmark name.", "Name Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                BookmarkNameTextBox.Focus();
                return;
            }

            BookmarkName = name;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
