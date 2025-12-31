using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Amazon;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.S3.Model;
using S3Browser.Helpers;

namespace S3Browser
{
    /// <summary>
    /// Main application window for browsing AWS S3 buckets, folders, and files.
    /// Provides navigation, file preview, and support for various file types including Parquet, CSV, TSV, and text files.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Gets or sets the collection of S3 items (buckets, folders, files) displayed in the main grid.
        /// </summary>
        public ObservableCollection<S3Item> Items { get; set; }
        private IAmazonS3? _s3Client;
        private string? _awsProfile;
        private string? _currentBucket;
        private string _currentPrefix = string.Empty;
        private Stack<string> _navigationStack = new Stack<string>();

        /// <summary>
        /// Initializes a new instance of the MainWindow.
        /// Prompts for AWS profile selection and loads S3 buckets on successful authentication.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            Items = new ObservableCollection<S3Item>();
            FilesDataGrid.ItemsSource = Items;

            var dialog = new ProfileSelectionDialog();
            if (dialog.ShowDialog() == true)
            {
                _awsProfile = dialog.SelectedProfile;
                LoadBucketsAsync();
            }
            else
            {
                Application.Current.Shutdown();
            }
        }

        private async void LoadBucketsAsync()
        {
            try
            {
                var chain = new CredentialProfileStoreChain();
                if (!chain.TryGetProfile(_awsProfile!, out var profile))
                {
                    MessageBox.Show($"Could not load AWS profile '{_awsProfile}'.\n\nMake sure the profile exists in your AWS configuration.",
                        "AWS Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!chain.TryGetAWSCredentials(_awsProfile!, out var credentials))
                {
                    MessageBox.Show($"Could not load AWS credentials for profile '{_awsProfile}'.\n\nMake sure you have run 'aws sso login --profile {_awsProfile}' before starting this application.",
                        "AWS Authentication Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                RegionEndpoint? region = profile.Region ?? RegionEndpoint.USEast1;
                _s3Client = new AmazonS3Client(credentials, region);

                var response = await _s3Client.ListBucketsAsync();

                _currentBucket = null;
                _currentPrefix = string.Empty;
                _navigationStack.Clear();

                Items.Clear();
                foreach (var bucket in response.Buckets)
                {
                    Items.Add(new S3Item
                    {
                        Type = "Bucket",
                        Name = bucket.BucketName,
                        Size = "--",
                        LastModified = bucket.CreationDate?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Unknown"
                    });
                }

                UpdateBreadcrumb();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading S3 buckets: {ex.Message}\n\nMake sure you have run 'aws sso login --profile {_awsProfile}' before starting this application.",
                    "AWS Authentication Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LoadBucketContentsAsync(string bucketName, string prefix = "")
        {
            try
            {
                if (_s3Client == null) return;

                var request = new ListObjectsV2Request
                {
                    BucketName = bucketName,
                    Prefix = prefix,
                    Delimiter = "/"
                };

                var response = await _s3Client.ListObjectsV2Async(request);

                Items.Clear();

                // Always add ".." entry when inside a bucket (even at root level)
                Items.Add(new S3Item
                {
                    Type = "Folder",
                    Name = "..",
                    Size = "--",
                    LastModified = "--"
                });

                if (response.CommonPrefixes != null)
                {
                    foreach (var folder in response.CommonPrefixes)
                    {
                        if (string.IsNullOrEmpty(folder)) continue;

                        var folderName = folder.TrimEnd('/');
                        if (!string.IsNullOrEmpty(prefix))
                        {
                            folderName = folderName.Substring(prefix.Length);
                        }

                        Items.Add(new S3Item
                        {
                            Type = "Folder",
                            Name = folderName,
                            Size = "--",
                            LastModified = "--",
                            FullKey = folder
                        });
                    }
                }

                if (response.S3Objects != null)
                {
                    foreach (var s3Object in response.S3Objects)
                    {
                        if (s3Object == null || string.IsNullOrEmpty(s3Object.Key)) continue;
                        if (s3Object.Key.EndsWith("/")) continue;

                        var fileName = s3Object.Key;
                        if (!string.IsNullOrEmpty(prefix))
                        {
                            fileName = fileName.Substring(prefix.Length);
                        }

                        Items.Add(new S3Item
                        {
                            Type = "File",
                            Name = fileName,
                            Size = FileHelper.FormatFileSize(s3Object.Size ?? 0),
                            LastModified = s3Object.LastModified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Unknown",
                            FullKey = s3Object.Key
                        });
                    }
                }

                // Check if folder contains only parquet files
                CheckAndShowReadAllParquetButton();

                UpdateBreadcrumb();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading bucket contents: {ex.Message}",
                    "AWS Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateBreadcrumb()
        {
            if (_currentBucket == null)
            {
                TitleTextBlock.Text = "AWS S3 Buckets";
                S3PathTextBox.Text = "";
            }
            else
            {
                // Show bucket and current folder name in title
                if (string.IsNullOrEmpty(_currentPrefix))
                {
                    TitleTextBlock.Text = $"Bucket: {_currentBucket}";
                    S3PathTextBox.Text = $"s3://{_currentBucket}";
                }
                else
                {
                    // Extract current folder name from prefix
                    string folderName = _currentPrefix.TrimEnd('/');
                    int lastSlash = folderName.LastIndexOf('/');
                    if (lastSlash >= 0)
                    {
                        folderName = folderName.Substring(lastSlash + 1);
                    }
                    TitleTextBlock.Text = $"Bucket: {_currentBucket} / Folder: {folderName}";
                    S3PathTextBox.Text = $"s3://{_currentBucket}/{_currentPrefix.TrimEnd('/')}";
                }
            }
        }

        private void FilesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            HandleFileSelection();
        }

        private void FilesDataGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                HandleFileSelection();
                e.Handled = true;
            }
        }

        private void HandleFileSelection()
        {
            if (FilesDataGrid.SelectedItem is S3Item selectedItem)
            {
                if (selectedItem.Type == "Bucket")
                {
                    _currentBucket = selectedItem.Name;
                    _currentPrefix = string.Empty;
                    _navigationStack.Clear();
                    LoadBucketContentsAsync(_currentBucket);
                }
                else if (selectedItem.Type == "Folder")
                {
                    if (selectedItem.Name == "..")
                    {
                        if (_navigationStack.Count > 0)
                        {
                            _currentPrefix = _navigationStack.Pop();
                        }
                        else
                        {
                            LoadBucketsAsync();
                            return;
                        }
                    }
                    else
                    {
                        _navigationStack.Push(_currentPrefix);
                        _currentPrefix = selectedItem.FullKey ?? string.Empty;
                    }

                    if (_currentBucket != null)
                    {
                        LoadBucketContentsAsync(_currentBucket, _currentPrefix);
                    }
                }
                else if (selectedItem.Type == "File")
                {
                    if (IsParquetFile(selectedItem.Name))
                    {
                        OpenParquetFileViewer(selectedItem);
                    }
                    else if (IsCsvFile(selectedItem.Name))
                    {
                        OpenTabularFileViewer(selectedItem, "csv");
                    }
                    else if (IsTsvFile(selectedItem.Name))
                    {
                        OpenTabularFileViewer(selectedItem, "tsv");
                    }
                    else if (IsTextFile(selectedItem.Name))
                    {
                        OpenTextFileViewer(selectedItem);
                    }
                    else
                    {
                        MessageBox.Show($"File: {selectedItem.Name}\nSize: {selectedItem.Size}\nLast Modified: {selectedItem.LastModified}",
                            "File Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        private bool IsTextFile(string fileName)
        {
            string[] textExtensions = { ".txt", ".json", ".xml", ".log", ".md", ".yaml", ".yml", ".config", ".ini", ".properties", ".html", ".htm", ".css", ".js", ".ts", ".sql", ".sh", ".bat", ".ps1" };
            string extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
            return textExtensions.Contains(extension);
        }

        private bool IsParquetFile(string fileName)
        {
            string extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
            return extension == ".parquet";
        }

        private bool IsCsvFile(string fileName)
        {
            string extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
            return extension == ".csv";
        }

        private bool IsTsvFile(string fileName)
        {
            string extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
            return extension == ".tsv";
        }

        private async void OpenTextFileViewer(S3Item fileItem)
        {
            try
            {
                if (_s3Client == null || _currentBucket == null || string.IsNullOrEmpty(fileItem.FullKey))
                    return;

                var headRequest = new GetObjectMetadataRequest
                {
                    BucketName = _currentBucket,
                    Key = fileItem.FullKey
                };

                var metadata = await _s3Client.GetObjectMetadataAsync(headRequest);
                long fileSize = metadata.ContentLength;

                var viewer = new FileViewerWindow(_s3Client, _currentBucket, fileItem.FullKey, fileItem.Name, fileSize);
                viewer.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file viewer: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenParquetFileViewer(S3Item fileItem)
        {
            try
            {
                if (_s3Client == null || _currentBucket == null || string.IsNullOrEmpty(fileItem.FullKey))
                    return;

                var viewer = new ParquetViewerWindow(_s3Client, _currentBucket, fileItem.FullKey, fileItem.Name, false, _awsProfile);
                viewer.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening parquet viewer: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenTabularFileViewer(S3Item fileItem, string fileType)
        {
            try
            {
                if (_s3Client == null || _currentBucket == null || string.IsNullOrEmpty(fileItem.FullKey))
                    return;

                var viewer = new TabularFileViewerWindow(_s3Client, _currentBucket, fileItem.FullKey, fileItem.Name, fileType);
                viewer.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening tabular file viewer: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GoButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToS3Path();
        }

        private void S3PathTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                NavigateToS3Path();
            }
        }

        private async void NavigateToS3Path()
        {
            try
            {
                if (_s3Client == null)
                {
                    MessageBox.Show("S3 client is not initialized.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string path = S3PathTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                // Parse S3 path (s3:// or s3a://)
                var parsedPath = ParseS3Path(path);
                if (parsedPath == null)
                {
                    MessageBox.Show("Invalid S3 path format. Please use:\ns3://bucket-name/path/to/object\nor\ns3a://bucket-name/path/to/object",
                        "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var (bucketName, key) = parsedPath.Value;

                // Check if it's a file or folder
                if (!string.IsNullOrEmpty(key))
                {
                    // Try to get object metadata to determine if it's a file
                    try
                    {
                        var headRequest = new GetObjectMetadataRequest
                        {
                            BucketName = bucketName,
                            Key = key
                        };

                        var metadata = await _s3Client.GetObjectMetadataAsync(headRequest);

                        // It's a file, open the appropriate viewer
                        string fileName = System.IO.Path.GetFileName(key);
                        var fileItem = new S3Item
                        {
                            Type = "File",
                            Name = fileName,
                            Size = FileHelper.FormatFileSize(metadata.ContentLength),
                            LastModified = metadata.LastModified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Unknown",
                            FullKey = key
                        };

                        _currentBucket = bucketName;
                        _navigationStack.Clear();

                        if (IsParquetFile(fileName))
                        {
                            OpenParquetFileViewer(fileItem);
                        }
                        else if (IsCsvFile(fileName))
                        {
                            OpenTabularFileViewer(fileItem, "csv");
                        }
                        else if (IsTsvFile(fileName))
                        {
                            OpenTabularFileViewer(fileItem, "tsv");
                        }
                        else if (IsTextFile(fileName))
                        {
                            OpenTextFileViewer(fileItem);
                        }
                        else
                        {
                            MessageBox.Show($"File: {fileName}\nSize: {fileItem.Size}\nLast Modified: {fileItem.LastModified}",
                                "File Information", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        return;
                    }
                    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // Not a file, treat as a folder/prefix
                    }
                }

                // Navigate to bucket/folder
                _currentBucket = bucketName;
                _currentPrefix = string.IsNullOrEmpty(key) ? string.Empty : (key.EndsWith("/") ? key : key + "/");

                // Build navigation stack from the path
                BuildNavigationStack(_currentPrefix);

                LoadBucketContentsAsync(_currentBucket, _currentPrefix);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error navigating to S3 path: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private (string bucketName, string key)? ParseS3Path(string path)
        {
            // Remove s3:// or s3a:// prefix
            if (path.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(5);
            }
            else if (path.StartsWith("s3a://", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(6);
            }
            else
            {
                return null;
            }

            // Split bucket and key
            int firstSlash = path.IndexOf('/');
            if (firstSlash == -1)
            {
                // Just bucket name
                return (path, string.Empty);
            }

            string bucketName = path.Substring(0, firstSlash);
            string key = path.Substring(firstSlash + 1);

            return (bucketName, key);
        }

        private void BuildNavigationStack(string prefix)
        {
            _navigationStack.Clear();

            if (string.IsNullOrEmpty(prefix))
            {
                return;
            }

            // Split the prefix into parts and build the stack
            var parts = prefix.TrimEnd('/').Split('/');
            string currentPath = string.Empty;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                _navigationStack.Push(currentPath);
                currentPath = string.IsNullOrEmpty(currentPath)
                    ? parts[i] + "/"
                    : currentPath + parts[i] + "/";
            }

            // Reverse the stack so we can pop in the correct order
            var tempList = _navigationStack.ToList();
            _navigationStack.Clear();
            for (int i = tempList.Count - 1; i >= 0; i--)
            {
                _navigationStack.Push(tempList[i]);
            }
        }

        private void CheckAndShowReadAllParquetButton()
        {
            // Get all file items (excluding folders and "..")
            var fileItems = Items.Where(item => item.Type == "File").ToList();

            if (fileItems.Count == 0)
            {
                ReadAllParquetButton.Visibility = Visibility.Collapsed;
                return;
            }

            // Check if all files are either .parquet or _SUCCESS
            bool allParquetOrSuccess = fileItems.All(item =>
                IsParquetFile(item.Name) ||
                item.Name.Equals("_SUCCESS", StringComparison.OrdinalIgnoreCase));

            // Check if there's at least one parquet file
            bool hasParquetFiles = fileItems.Any(item => IsParquetFile(item.Name));

            if (allParquetOrSuccess && hasParquetFiles)
            {
                ReadAllParquetButton.Visibility = Visibility.Visible;
            }
            else
            {
                ReadAllParquetButton.Visibility = Visibility.Collapsed;
            }
        }

        private void ReadAllParquetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_s3Client == null || _currentBucket == null)
                    return;

                // Create a wildcard pattern for all parquet files in the current prefix
                string wildcardPattern = string.IsNullOrEmpty(_currentPrefix)
                    ? "*.parquet"
                    : $"{_currentPrefix.TrimEnd('/')}/*.parquet";

                // Extract folder name for window title
                string folderName = string.IsNullOrEmpty(_currentPrefix)
                    ? _currentBucket
                    : _currentPrefix.TrimEnd('/').Split('/').Last();

                // Open the parquet viewer with wildcard mode
                var viewer = new ParquetViewerWindow(_s3Client, _currentBucket, wildcardPattern, folderName, isWildcard: true, awsProfile: _awsProfile);
                viewer.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening parquet viewer: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Represents an item in S3 (bucket, folder, or file) for display in the UI.
    /// </summary>
    public class S3Item
    {
        /// <summary>
        /// Gets or sets the type of the item ("Bucket", "Folder", or "File").
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of the item.
        /// For files, this is the file name without path. For folders, this is the folder name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the formatted size of the item.
        /// For files, shows size in B, KB, MB, GB, or TB. For folders and buckets, shows "--".
        /// </summary>
        public string Size { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the last modified date/time formatted as "yyyy-MM-dd HH:mm" in local time.
        /// Shows "--" for folders or "Unknown" if unavailable.
        /// </summary>
        public string LastModified { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the full S3 key (path) for the item.
        /// Null for buckets, contains full path with prefix for files and folders.
        /// </summary>
        public string? FullKey { get; set; }
    }
}