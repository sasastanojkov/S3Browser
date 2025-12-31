namespace S3Browser.Helpers
{
    /// <summary>
    /// Helper class for file-related utility methods.
    /// </summary>
    public static class FileHelper
    {
        /// <summary>
        /// Formats a file size in bytes to a human-readable string.
        /// </summary>
        /// <param name="bytes">The size in bytes.</param>
        /// <returns>A formatted string with appropriate unit (B, KB, MB, GB, or TB).</returns>
        public static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
