using S3Browser.Models;

namespace S3Browser.Services
{
    /// <summary>
    /// Manages bookmark operations including adding, removing, and retrieving bookmarks.
    /// </summary>
    public static class BookmarksManager
    {
        /// <summary>
        /// Adds a new bookmark or updates an existing one with the same path.
        /// </summary>
        /// <param name="name">User-friendly name for the bookmark.</param>
        /// <param name="s3Path">S3 path (e.g., s3://bucket-name/folder).</param>
        /// <param name="isAnonymous">True if this location uses anonymous access.</param>
        public static void AddBookmark(string name, string s3Path, bool isAnonymous)
        {
            var preferences = PreferencesManager.Current;

            // Check if bookmark with this path already exists
            var existing = preferences.Bookmarks.FirstOrDefault(b =>
                b.S3Path.Equals(s3Path, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                // Update existing bookmark
                existing.Name = name;
                existing.IsAnonymous = isAnonymous;
                existing.CreatedDate = DateTime.Now;
            }
            else
            {
                // Add new bookmark
                preferences.Bookmarks.Add(new Bookmark
                {
                    Name = name,
                    S3Path = s3Path,
                    IsAnonymous = isAnonymous,
                    CreatedDate = DateTime.Now
                });
            }

            // Save preferences
            PreferencesManager.Save();
        }

        /// <summary>
        /// Removes a bookmark by its S3 path.
        /// </summary>
        /// <param name="s3Path">The S3 path of the bookmark to remove.</param>
        /// <returns>True if the bookmark was found and removed, false otherwise.</returns>
        public static bool RemoveBookmark(string s3Path)
        {
            var preferences = PreferencesManager.Current;
            var bookmark = preferences.Bookmarks.FirstOrDefault(b =>
                b.S3Path.Equals(s3Path, StringComparison.OrdinalIgnoreCase));

            if (bookmark != null)
            {
                preferences.Bookmarks.Remove(bookmark);
                PreferencesManager.Save();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Removes a bookmark by reference.
        /// </summary>
        /// <param name="bookmark">The bookmark to remove.</param>
        /// <returns>True if the bookmark was found and removed, false otherwise.</returns>
        public static bool RemoveBookmark(Bookmark bookmark)
        {
            var preferences = PreferencesManager.Current;
            if (preferences.Bookmarks.Remove(bookmark))
            {
                PreferencesManager.Save();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets all bookmarks sorted by creation date (newest first).
        /// </summary>
        /// <returns>List of bookmarks.</returns>
        public static List<Bookmark> GetAllBookmarks()
        {
            return PreferencesManager.Current.Bookmarks
                .OrderByDescending(b => b.CreatedDate)
                .ToList();
        }

        /// <summary>
        /// Checks if a bookmark exists for the given S3 path.
        /// </summary>
        /// <param name="s3Path">The S3 path to check.</param>
        /// <returns>True if a bookmark exists, false otherwise.</returns>
        public static bool BookmarkExists(string s3Path)
        {
            return PreferencesManager.Current.Bookmarks.Any(b =>
                b.S3Path.Equals(s3Path, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets a bookmark by its S3 path.
        /// </summary>
        /// <param name="s3Path">The S3 path to find.</param>
        /// <returns>The bookmark if found, null otherwise.</returns>
        public static Bookmark? GetBookmark(string s3Path)
        {
            return PreferencesManager.Current.Bookmarks.FirstOrDefault(b =>
                b.S3Path.Equals(s3Path, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Clears all bookmarks.
        /// </summary>
        public static void ClearAllBookmarks()
        {
            PreferencesManager.Current.Bookmarks.Clear();
            PreferencesManager.Save();
        }
    }
}
