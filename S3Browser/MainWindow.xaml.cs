using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using S3Browser.Helpers;
using S3Browser.Services;

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
        private string? _currentBucket;
        private string _currentPrefix = string.Empty;
        private Stack<string> _navigationStack = new Stack<string>();

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// Prompts for AWS profile selection or anonymous access and loads S3 buckets accordingly.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            Items = new ObservableCollection<S3Item>();
            FilesDataGrid.ItemsSource = Items;

            var dialog = new ProfileSelectionDialog();
            if (dialog.ShowDialog() == true)
            {
                InitializeS3ManagerAsync(dialog.SelectedProfile, dialog.IsAnonymousMode);
            }
            else
            {
                Application.Current.Shutdown();
            }
        }

        private async void InitializeS3ManagerAsync(string? awsProfile, bool isAnonymousMode)
        {
            try
            {
                StatusTextBlock.Text = "Initializing...";
                StatusProgressBar.Visibility = Visibility.Visible;

                S3Manager.Instance.Initialize(awsProfile, isAnonymousMode);

                if (isAnonymousMode)
                {
                    ShowAnonymousWelcomeMessage();
                }
                else
                {
                    await LoadBucketsAsync();
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Initialization failed";
                StatusProgressBar.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Failed to initialize: {ex.Message}\n\nMake sure you have run 'aws sso login --profile {awsProfile}' if using authenticated access.",
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadBucketsAsync()
        {
            try
            {
                StatusTextBlock.Text = "Loading S3 buckets...";
                StatusProgressBar.Visibility = Visibility.Visible;
                ItemCountTextBlock.Text = "";

                var buckets = await S3Manager.Instance.ListBucketsAsync();

                _currentBucket = null;
                _currentPrefix = string.Empty;
                _navigationStack.Clear();

                Items.Clear();
                foreach (var bucket in buckets)
                {
                    bool isPublic = S3Manager.Instance.IsPublicBucket(bucket.BucketName);
                    Items.Add(new S3Item
                    {
                        Type = "Bucket",
                        Name = isPublic ? $"{bucket.BucketName} (Public)" : bucket.BucketName,
                        Size = "--",
                        LastModified = bucket.CreationDate?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Unknown"
                    });
                }

                UpdateBreadcrumb();

                int bucketCount = Items.Count;
                StatusTextBlock.Text = "Ready";
                StatusProgressBar.Visibility = Visibility.Collapsed;
                ItemCountTextBlock.Text = $"{bucketCount} bucket{(bucketCount != 1 ? "s" : "")}";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error loading buckets";
                StatusProgressBar.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Error loading S3 buckets: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Shows a welcome message for anonymous mode with instructions.
        /// </summary>
        private void ShowAnonymousWelcomeMessage()
        {
            _currentBucket = null;
            _currentPrefix = string.Empty;
            _navigationStack.Clear();
            Items.Clear();

            TitleTextBlock.Text = "Anonymous Access Mode";
            StatusTextBlock.Text = "Ready - Anonymous Mode";
            StatusProgressBar.Visibility = Visibility.Collapsed;
            ItemCountTextBlock.Text = "Enter S3 path above to browse public buckets";
        }

        private CancellationTokenSource? _loadingCancellationTokenSource;

        private async Task LoadBucketContentsAsync(string bucketName, string prefix = "")
        {
            // Cancel any existing load operation
            _loadingCancellationTokenSource?.Cancel();
            _loadingCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _loadingCancellationTokenSource.Token;

            try
            {
                StatusTextBlock.Text = string.IsNullOrEmpty(prefix)
                    ? $"Loading bucket '{bucketName}'..."
                    : $"Loading folder contents...";
                StatusProgressBar.Visibility = Visibility.Visible;
                ItemCountTextBlock.Text = "";

                var result = await S3Manager.Instance.ListObjectsAsync(bucketName, prefix);

                // Check if operation was cancelled
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                Items.Clear();

                // Add ".." entry when inside a bucket
                Items.Add(new S3Item
                {
                    Type = "Folder",
                    Name = "..",
                    Size = "--",
                    LastModified = "--"
                });

                // Add folders
                foreach (var folder in result.Folders)
                {
                    Items.Add(new S3Item
                    {
                        Type = "Folder",
                        Name = folder.Name,
                        Size = "--",
                        LastModified = "--",
                        FullKey = folder.FullKey
                    });
                }

                // Add files
                foreach (var file in result.Files)
                {
                    Items.Add(new S3Item
                    {
                        Type = "File",
                        Name = file.Name,
                        Size = FileHelper.FormatFileSize(file.Size),
                        LastModified = file.LastModified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Unknown",
                        FullKey = file.FullKey
                    });
                }

                CheckAndShowReadAllParquetButton();
                UpdateBreadcrumb();

                // Update status
                int folderCount = result.Folders.Count;
                int fileCount = result.Files.Count;

                StatusTextBlock.Text = "Ready";
                StatusProgressBar.Visibility = Visibility.Collapsed;

                if (fileCount > 0 && folderCount > 0)
                {
                    ItemCountTextBlock.Text = $"{folderCount} folder{(folderCount != 1 ? "s" : "")}, {fileCount} file{(fileCount != 1 ? "s" : "")}";
                }
                else if (fileCount > 0)
                {
                    ItemCountTextBlock.Text = $"{fileCount} file{(fileCount != 1 ? "s" : "")}";
                }
                else if (folderCount > 0)
                {
                    ItemCountTextBlock.Text = $"{folderCount} folder{(folderCount != 1 ? "s" : "")}";
                }
                else
                {
                    ItemCountTextBlock.Text = "Empty";
                }
            }
            catch (OperationCanceledException)
            {
                // Operation was cancelled, do nothing
                StatusTextBlock.Text = "Loading cancelled";
                StatusProgressBar.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error loading contents";
                StatusProgressBar.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Error loading bucket contents: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateBreadcrumb()
        {
            if (_currentBucket == null)
            {
                TitleTextBlock.Text = S3Manager.Instance.IsAnonymousMode() ? "Anonymous Access Mode" : "AWS S3 Buckets";
                S3PathTextBox.Text = "";
                HomeButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                HomeButton.Visibility = Visibility.Visible;
                if (string.IsNullOrEmpty(_currentPrefix))
                {
                    TitleTextBlock.Text = $"Bucket: {_currentBucket}";
                    S3PathTextBox.Text = $"s3://{_currentBucket}";
                }
                else
                {
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

        private async void HandleFileSelection()
        {
            if (FilesDataGrid.SelectedItem is S3Item selectedItem)
            {
                if (selectedItem.Type == "Bucket")
                {
                    // Remove " (Public)" suffix if present
                    string bucketName = selectedItem.Name;
                    if (bucketName.EndsWith(" (Public)"))
                    {
                        bucketName = bucketName.Substring(0, bucketName.Length - 9);
                    }
                    _currentBucket = bucketName;
                    _currentPrefix = string.Empty;
                    _navigationStack.Clear();
                    await LoadBucketContentsAsync(_currentBucket);
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
                            await LoadBucketsAsync();
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
                        await LoadBucketContentsAsync(_currentBucket, _currentPrefix);
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
                if (_currentBucket == null || string.IsNullOrEmpty(fileItem.FullKey))
                    return;

                var metadata = await S3Manager.Instance.GetObjectMetadataAsync(_currentBucket, fileItem.FullKey);
                long fileSize = metadata.ContentLength;

                var viewer = new FileViewerWindow(_currentBucket, fileItem.FullKey, fileItem.Name, fileSize);
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
                if (_currentBucket == null || string.IsNullOrEmpty(fileItem.FullKey))
                    return;

                var viewer = new ParquetViewerWindow(_currentBucket, fileItem.FullKey, fileItem.Name, isWildcard: false);
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
                if (_currentBucket == null || string.IsNullOrEmpty(fileItem.FullKey))
                    return;

                var viewer = new TabularFileViewerWindow(_currentBucket, fileItem.FullKey, fileItem.Name, fileType);
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
                string path = S3PathTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                // Show parsing status
                StatusTextBlock.Text = "Parsing S3 path...";
                StatusProgressBar.Visibility = Visibility.Visible;
                ItemCountTextBlock.Text = "";

                // Parse S3 path (s3:// or s3a://)
                var parsedPath = ParseS3Path(path);
                if (parsedPath == null)
                {
                    StatusTextBlock.Text = "Error: Invalid path format";
                    StatusProgressBar.Visibility = Visibility.Collapsed;
                    MessageBox.Show("Invalid S3 path format. Please use:\ns3://bucket-name/path/to/object\nor\ns3a://bucket-name/path/to/object",
                        "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var (bucketName, key) = parsedPath.Value;

                if (!string.IsNullOrEmpty(key))
                {
                    try
                    {
                        StatusTextBlock.Text = "Checking object metadata...";
                        var metadata = await S3Manager.Instance.GetObjectMetadataAsync(bucketName, key);

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

                        StatusTextBlock.Text = "Opening file...";
                        StatusProgressBar.Visibility = Visibility.Collapsed;

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

                        StatusTextBlock.Text = "Ready";
                        return;
                    }
                    catch (Exception ex) when (ex.Message.Contains("NotFound") || ex.Message.Contains("404"))
                    {
                        // Not a file, treat as a folder/prefix
                        StatusTextBlock.Text = "Loading folder...";
                    }
                }

                // Navigate to bucket/folder
                _currentBucket = bucketName;
                _currentPrefix = string.IsNullOrEmpty(key) ? string.Empty : (key.EndsWith("/") ? key : key + "/");

                // Build navigation stack from the path
                BuildNavigationStack(_currentPrefix);

                // LoadBucketContentsAsync will update the status bar
                await LoadBucketContentsAsync(_currentBucket, _currentPrefix);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error during navigation";
                StatusProgressBar.Visibility = Visibility.Collapsed;
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
                WriteQueryButton.Visibility = Visibility.Collapsed;
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
                WriteQueryButton.Visibility = Visibility.Visible;
            }
            else
            {
                ReadAllParquetButton.Visibility = Visibility.Collapsed;
                WriteQueryButton.Visibility = Visibility.Collapsed;
            }
        }

        private void ReadAllParquetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentBucket == null)
                    return;

                string wildcardPattern = string.IsNullOrEmpty(_currentPrefix)
                    ? "*.parquet"
                    : $"{_currentPrefix.TrimEnd('/')}/*.parquet";

                string folderName = string.IsNullOrEmpty(_currentPrefix)
                    ? _currentBucket
                    : _currentPrefix.TrimEnd('/').Split('/').Last();

                var viewer = new ParquetViewerWindow(_currentBucket, wildcardPattern, folderName, isWildcard: true);
                viewer.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening parquet viewer: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void WriteQueryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentBucket == null)
                    return;

                string wildcardPattern = string.IsNullOrEmpty(_currentPrefix)
                    ? "*.parquet"
                    : $"{_currentPrefix.TrimEnd('/')}/*.parquet";

                string s3Path = $"s3://{_currentBucket}/{wildcardPattern.Replace("*.parquet", "")}*.parquet";
                string initialQuery = $"SELECT * FROM read_parquet('{s3Path}')";

                string folderName = string.IsNullOrEmpty(_currentPrefix)
                    ? _currentBucket
                    : _currentPrefix.TrimEnd('/').Split('/').Last();

                var queryDialog = new QueryEditorDialog(_currentBucket, initialQuery, folderName);
                queryDialog.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening query editor: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (S3Manager.Instance.IsAnonymousMode())
                {
                    ShowAnonymousWelcomeMessage();
                }
                else
                {
                    await LoadBucketsAsync();
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error loading buckets";
                StatusProgressBar.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Error navigating home: {ex.Message}", "Error",
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
