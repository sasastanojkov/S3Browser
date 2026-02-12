using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace S3Browser.Models
{
    /// <summary>
    /// User preferences for S3Browser application.
    /// Stores user-configurable settings that persist across application sessions.
    /// </summary>
    public class UserPreferences : INotifyPropertyChanged
    {
        private int _maxTableSizeMB = 500;

        /// <summary>
        /// Maximum size in MB for loading parquet files into DuckDB table mode.
        /// Default: 500 MB. Valid range: 1-10000 MB.
        /// </summary>
        public int MaxTableSizeMB
        {
            get => _maxTableSizeMB;
            set
            {
                if (_maxTableSizeMB != value)
                {
                    _maxTableSizeMB = Math.Clamp(value, 1, 10000);
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// List of saved S3 location bookmarks.
        /// </summary>
        public List<Bookmark> Bookmarks { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
