using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using S3Browser.Models;
using S3Browser.Services;

namespace S3Browser
{
    /// <summary>
    /// Window for managing S3 location bookmarks.
    /// </summary>
    public partial class BookmarksWindow : Window
    {
        private readonly MainWindow _mainWindow;
        public ObservableCollection<Bookmark> Bookmarks { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BookmarksWindow"/> class.
        /// </summary>
        /// <param name="mainWindow">Reference to the main window for navigation.</param>
        public BookmarksWindow(MainWindow mainWindow)
        {
            InitializeComponent();

            _mainWindow = mainWindow;
            Bookmarks = new ObservableCollection<Bookmark>();

            LoadBookmarks();

            BookmarksDataGrid.ItemsSource = Bookmarks;
        }

        private void LoadBookmarks()
        {
            Bookmarks.Clear();

            var bookmarks = BookmarksManager.GetAllBookmarks();
            foreach (var bookmark in bookmarks)
            {
                Bookmarks.Add(bookmark);
            }

            UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            bool hasBookmarks = Bookmarks.Count > 0;
            EmptyStatePanel.Visibility = hasBookmarks ? Visibility.Collapsed : Visibility.Visible;
            BookmarksDataGrid.Visibility = hasBookmarks ? Visibility.Visible : Visibility.Collapsed;
            ClearAllButton.IsEnabled = hasBookmarks;
        }

        private void BookmarksDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (BookmarksDataGrid.SelectedItem is Bookmark bookmark)
            {
                NavigateToBookmark(bookmark);
            }
        }

        private void GoToBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is Bookmark bookmark)
            {
                NavigateToBookmark(bookmark);
            }
        }

        private void NavigateToBookmark(Bookmark bookmark)
        {
            try
            {
                // Navigate to the bookmark
                _mainWindow.NavigateToBookmark(bookmark);

                // Close the bookmarks window
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error navigating to bookmark: {ex.Message}", "Navigation Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is Bookmark bookmark)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete the bookmark '{bookmark.Name}'?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    BookmarksManager.RemoveBookmark(bookmark);
                    Bookmarks.Remove(bookmark);
                    UpdateEmptyState();
                }
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (Bookmarks.Count == 0)
                return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete all {Bookmarks.Count} bookmarks?\n\nThis action cannot be undone.",
                "Confirm Clear All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                BookmarksManager.ClearAllBookmarks();
                Bookmarks.Clear();
                UpdateEmptyState();

                MessageBox.Show("All bookmarks have been deleted.", "Bookmarks Cleared",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
