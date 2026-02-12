using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace S3Browser.Models
{
    /// <summary>
    /// Represents a saved S3 location bookmark.
    /// Stores the S3 path and access mode for quick navigation.
    /// </summary>
    public class Bookmark : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _s3Path = string.Empty;
        private bool _isAnonymous;
        private DateTime _createdDate = DateTime.Now;

        /// <summary>
        /// Gets or sets the user-friendly name for this bookmark.
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the S3 path (e.g., s3://bucket-name/folder/path).
        /// </summary>
        public string S3Path
        {
            get => _s3Path;
            set
            {
                if (_s3Path != value)
                {
                    _s3Path = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this bookmark uses anonymous access.
        /// True for public buckets, false for authenticated access.
        /// </summary>
        public bool IsAnonymous
        {
            get => _isAnonymous;
            set
            {
                if (_isAnonymous != value)
                {
                    _isAnonymous = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsPublic));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether this bookmark is for a public bucket.
        /// Inverse of IsAnonymous for display purposes.
        /// </summary>
        public bool IsPublic => _isAnonymous;

        /// <summary>
        /// Gets or sets the date and time when this bookmark was created.
        /// </summary>
        public DateTime CreatedDate
        {
            get => _createdDate;
            set
            {
                if (_createdDate != value)
                {
                    _createdDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
