using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
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
        private string? _currentProfile;
        private bool _isAnonymousMode;

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
                _ = InitializeS3ManagerAsync(dialog.SelectedProfile, dialog.IsAnonymousMode);
            }
            else
            {
                Application.Current.Shutdown();
            }
        }

        private async Task InitializeS3ManagerAsync(string? awsProfile, bool isAnonymousMode)
        {
            try
            {
                StatusTextBlock.Text = "Initializing...";
                StatusProgressBar.Visibility = Visibility.Visible;

                _currentProfile = awsProfile;
                _isAnonymousMode = isAnonymousMode;
                UpdateWindowTitle();

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
                _currentBucket = null;
                _currentPrefix = string.Empty;
                _navigationStack.Clear();
                Items.Clear();
                UpdateBreadcrumb();

                StatusTextBlock.Text = "Initialization failed";
                StatusProgressBar.Visibility = Visibility.Collapsed;
                ItemCountTextBlock.Text = "";

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
        private CancellationTokenSource? _downloadCancellationTokenSource;

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
                    LastModified = "--"
                });

                // Add folders
                foreach (var folder in result.Folders)
                {
                    Items.Add(new S3Item
                    {
                        Type = "Folder",
                        Name = folder.Name,
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
                        SizeInBytes = file.Size,
                        LastModified = file.LastModified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Unknown",
                        FullKey = file.FullKey
                    });
                }

                CheckAndShowReadAllParquetButton();
                CheckAndShowDownloadAllButton();
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
            else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                CopyS3PathToClipboard();
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

                // Check if the key contains wildcard characters (*, ?)
                if (!string.IsNullOrEmpty(key) && (key.Contains('*') || key.Contains('?')))
                {
                    StatusTextBlock.Text = "Opening parquet viewer with wildcard pattern...";
                    StatusProgressBar.Visibility = Visibility.Collapsed;

                    // Extract folder name from the pattern for display
                    string folderName;

                    // Try to find the last directory name before the wildcard
                    var pathParts = key.Split('/');
                    var nonWildcardParts = pathParts.TakeWhile(part => !part.Contains('*') && !part.Contains('?')).ToList();

                    if (nonWildcardParts.Count > 0)
                    {
                        folderName = nonWildcardParts.Last();
                    }
                    else
                    {
                        folderName = bucketName;
                    }

                    if (string.IsNullOrEmpty(folderName))
                    {
                        folderName = bucketName;
                    }

                    // Open ParquetViewerWindow with wildcard pattern
                    var viewer = new ParquetViewerWindow(bucketName, key, folderName, isWildcard: true);
                    viewer.Show();

                    StatusTextBlock.Text = "Ready";
                    return;
                }

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
                            SizeInBytes = metadata.ContentLength,
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
                ReadAllParquetAsTableButton.Visibility = Visibility.Collapsed;
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
                ReadAllParquetAsTableButton.Visibility = Visibility.Visible;
                WriteQueryButton.Visibility = Visibility.Visible;
            }
            else
            {
                ReadAllParquetButton.Visibility = Visibility.Collapsed;
                ReadAllParquetAsTableButton.Visibility = Visibility.Collapsed;
                WriteQueryButton.Visibility = Visibility.Collapsed;
            }
        }

        private void CheckAndShowDownloadAllButton()
        {
            // Show Download All button only when browsing folders (not at bucket level)
            if (_currentBucket != null)
            {
                DownloadAllButton.Visibility = Visibility.Visible;
            }
            else
            {
                DownloadAllButton.Visibility = Visibility.Collapsed;
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

        private void ReadAllParquetAsTableButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentBucket == null)
                    return;

                // Calculate total size of parquet files in current folder (using already loaded size data)
                var parquetFiles = Items.Where(item => item.Type == "File" && IsParquetFile(item.Name)).ToList();
                long totalSizeBytes = parquetFiles.Sum(file => file.SizeInBytes ?? 0);

                // Check if total size exceeds 500MB (524,288,000 bytes)
                const long maxSizeBytes = 500L * 1024L * 1024L;
                if (totalSizeBytes > maxSizeBytes)
                {
                    string totalSizeFormatted = FileHelper.FormatFileSize(totalSizeBytes);
                    StatusTextBlock.Text = "Folder too large for table mode";
                    MessageBox.Show($"The total size of parquet files in this folder is {totalSizeFormatted}, which exceeds the 500 MB limit for table mode.\n\nPlease use 'Read All Parquet Files' (streaming mode) or 'Write Custom Query' instead.",
                        "Folder Too Large", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusTextBlock.Text = "Ready";

                string wildcardPattern = string.IsNullOrEmpty(_currentPrefix)
                    ? "*.parquet"
                    : $"{_currentPrefix.TrimEnd('/')}/*.parquet";

                string folderName = string.IsNullOrEmpty(_currentPrefix)
                    ? _currentBucket
                    : _currentPrefix.TrimEnd('/').Split('/').Last();

                var viewer = new ParquetViewerWindow(_currentBucket, wildcardPattern, folderName, isWildcard: true, customQuery: null, loadAsTable: true);
                viewer.Show();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error";
                StatusProgressBar.Visibility = Visibility.Collapsed;
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

        private async void ChangeProfileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ProfileSelectionDialog();
                if (dialog.ShowDialog() == true)
                {
                    _currentBucket = null;
                    _currentPrefix = string.Empty;
                    _navigationStack.Clear();
                    Items.Clear();
                    UpdateBreadcrumb();

                    await InitializeS3ManagerAsync(dialog.SelectedProfile, dialog.IsAnonymousMode);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error changing profile";
                StatusProgressBar.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Error changing profile: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateWindowTitle()
        {
            if (_isAnonymousMode)
            {
                Title = "S3 Browser";
            }
            else if (!string.IsNullOrEmpty(_currentProfile))
            {
                Title = $"S3 Browser - {_currentProfile}";
            }
            else
            {
                Title = "S3 Browser";
            }
        }

        private void CopyS3PathToClipboard()
        {
            if (FilesDataGrid.SelectedItem is not S3Item selectedItem)
                return;

            string s3Path = string.Empty;

            if (selectedItem.Type == "Bucket")
            {
                string bucketName = selectedItem.Name;
                if (bucketName.EndsWith(" (Public)"))
                {
                    bucketName = bucketName.Substring(0, bucketName.Length - 9);
                }
                s3Path = $"s3://{bucketName}";
            }
            else if (selectedItem.Type == "Folder")
            {
                if (selectedItem.Name == ".." || _currentBucket == null)
                    return;

                s3Path = $"s3://{_currentBucket}/{selectedItem.FullKey}";
            }
            else if (selectedItem.Type == "File")
            {
                if (_currentBucket == null || string.IsNullOrEmpty(selectedItem.FullKey))
                    return;

                s3Path = $"s3://{_currentBucket}/{selectedItem.FullKey}";
            }

            if (!string.IsNullOrEmpty(s3Path))
            {
                try
                {
                    Clipboard.SetText(s3Path);
                    StatusTextBlock.Text = $"Copied to clipboard: {s3Path}";
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = "Failed to copy to clipboard";
                    MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private async void DownloadFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not S3Item fileItem)
                return;

            if (_currentBucket == null || string.IsNullOrEmpty(fileItem.FullKey))
                return;

            // Check if already downloading
            if (fileItem.IsDownloading)
            {
                // Cancel the download
                fileItem.DownloadCancellationTokenSource?.Cancel();
                return;
            }

            // Ask user where to save the file
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = fileItem.Name,
                Title = "Save File As"
            };

            if (saveDialog.ShowDialog() != true)
                return;

            string localFilePath = saveDialog.FileName;

            // Start download
            await DownloadSingleFileAsync(fileItem, localFilePath);
        }

        private async Task DownloadSingleFileAsync(S3Item fileItem, string localFilePath)
        {
            if (_currentBucket == null || string.IsNullOrEmpty(fileItem.FullKey))
                return;

            // Create cancellation token source
            fileItem.DownloadCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = fileItem.DownloadCancellationTokenSource.Token;

            try
            {
                fileItem.IsDownloading = true;
                fileItem.DownloadProgress = 0;

                StatusTextBlock.Text = $"Downloading {fileItem.Name}...";
                StatusProgressBar.Visibility = Visibility.Visible;

                // Get the object
                using (var response = await S3Manager.Instance.GetObjectAsync(_currentBucket, fileItem.FullKey))
                {
                    long totalBytes = response.ContentLength;
                    long downloadedBytes = 0;

                    // Create directory if needed
                    string? directory = Path.GetDirectoryName(localFilePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Download with progress tracking
                    using (var responseStream = response.ResponseStream)
                    using (var fileStream = File.Create(localFilePath))
                    {
                        byte[] buffer = new byte[81920]; // 80 KB buffer
                        int bytesRead;

                        while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                            downloadedBytes += bytesRead;

                            // Update progress
                            if (totalBytes > 0)
                            {
                                double progress = (double)downloadedBytes / totalBytes * 100;
                                fileItem.DownloadProgress = progress;
                                StatusTextBlock.Text = $"Downloading {fileItem.Name}: {progress:F1}% ({FileHelper.FormatFileSize(downloadedBytes)} / {FileHelper.FormatFileSize(totalBytes)})";
                            }
                        }
                    }
                }

                StatusTextBlock.Text = $"Downloaded {fileItem.Name} successfully";
                StatusProgressBar.Visibility = Visibility.Collapsed;

                MessageBox.Show($"File downloaded successfully to:\n{localFilePath}", "Download Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Download cancelled";
                StatusProgressBar.Visibility = Visibility.Collapsed;

                // Delete partial file
                try
                {
                    if (File.Exists(localFilePath))
                    {
                        File.Delete(localFilePath);
                    }
                }
                catch
                {
                    // Ignore errors during cleanup
                }

                MessageBox.Show($"Download of {fileItem.Name} was cancelled.", "Download Cancelled",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Download failed";
                StatusProgressBar.Visibility = Visibility.Collapsed;

                MessageBox.Show($"Error downloading file: {ex.Message}", "Download Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                fileItem.IsDownloading = false;
                fileItem.DownloadProgress = 0;
                fileItem.DownloadCancellationTokenSource?.Dispose();
                fileItem.DownloadCancellationTokenSource = null;
            }
        }

        private async void DownloadAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBucket == null)
                return;

            // Ask user to select download location
            var dialog = new OpenFolderDialog
            {
                Title = "Select folder to save downloaded files",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            string downloadPath = dialog.FolderName;

            // Create a folder name based on current location
            string folderName;
            if (string.IsNullOrEmpty(_currentPrefix))
            {
                folderName = _currentBucket;
            }
            else
            {
                folderName = _currentPrefix.TrimEnd('/').Split('/').Last();
            }

            string targetPath = Path.Combine(downloadPath, folderName);

            // Confirm with user
            var result = MessageBox.Show(
                $"Download all files from:\ns3://{_currentBucket}/{_currentPrefix}\n\nTo:\n{targetPath}\n\nThis will download all files recursively and preserve folder structure.\n\nContinue?",
                "Confirm Download",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            // Start download
            await DownloadAllFilesAsync(targetPath);
        }

        private async Task DownloadAllFilesAsync(string targetPath)
        {
            // Cancel any existing download
            _downloadCancellationTokenSource?.Cancel();
            _downloadCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _downloadCancellationTokenSource.Token;

            try
            {
                // Create target directory
                Directory.CreateDirectory(targetPath);

                // Disable UI elements during download
                DownloadAllButton.IsEnabled = false;
                StatusTextBlock.Text = "Discovering files...";
                StatusProgressBar.Visibility = Visibility.Visible;

                // Discover all files recursively
                var allFiles = await DiscoverAllFilesAsync(_currentBucket!, _currentPrefix, cancellationToken);

                if (allFiles.Count == 0)
                {
                    MessageBox.Show("No files found to download.", "Download Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Calculate total size
                long totalBytes = allFiles.Sum(f => f.Size);
                string totalSizeFormatted = FileHelper.FormatFileSize(totalBytes);

                StatusTextBlock.Text = $"Downloading {allFiles.Count} files ({totalSizeFormatted})...";

                // Download all files
                int downloadedCount = 0;
                long downloadedBytes = 0;

                foreach (var fileInfo in allFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Calculate relative path
                    string relativePath = fileInfo.FullKey;
                    if (!string.IsNullOrEmpty(_currentPrefix))
                    {
                        relativePath = fileInfo.FullKey.Substring(_currentPrefix.Length);
                    }

                    // Create full local path
                    string localFilePath = Path.Combine(targetPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

                    // Create directory if needed
                    string? directory = Path.GetDirectoryName(localFilePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Download file
                    try
                    {
                        using (var response = await S3Manager.Instance.GetObjectAsync(_currentBucket!, fileInfo.FullKey))
                        {
                            await response.WriteResponseStreamToFileAsync(localFilePath, false, cancellationToken);
                        }

                        downloadedCount++;
                        downloadedBytes += fileInfo.Size;

                        // Update status
                        double progress = (double)downloadedCount / allFiles.Count * 100;
                        string downloadedSize = FileHelper.FormatFileSize(downloadedBytes);
                        StatusTextBlock.Text = $"Downloading: {downloadedCount}/{allFiles.Count} files ({downloadedSize}/{totalSizeFormatted}) - {progress:F1}%";
                        ItemCountTextBlock.Text = $"Current: {Path.GetFileName(localFilePath)}";
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to download {fileInfo.FullKey}: {ex.Message}");
                        // Continue with next file
                    }
                }

                StatusTextBlock.Text = $"Download complete: {downloadedCount} files ({FileHelper.FormatFileSize(downloadedBytes)})";
                ItemCountTextBlock.Text = "";
                StatusProgressBar.Visibility = Visibility.Collapsed;

                MessageBox.Show(
                    $"Successfully downloaded {downloadedCount} of {allFiles.Count} files to:\n{targetPath}",
                    "Download Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Download cancelled";
                StatusProgressBar.Visibility = Visibility.Collapsed;
                ItemCountTextBlock.Text = "";
                MessageBox.Show("Download was cancelled.", "Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Download failed";
                StatusProgressBar.Visibility = Visibility.Collapsed;
                ItemCountTextBlock.Text = "";
                MessageBox.Show($"Error during download: {ex.Message}", "Download Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                DownloadAllButton.IsEnabled = true;
            }
        }

        private async Task<List<S3FileInfo>> DiscoverAllFilesAsync(string bucketName, string prefix, CancellationToken cancellationToken)
        {
            var allFiles = new List<S3FileInfo>();
            var foldersToProcess = new Queue<string>();
            foldersToProcess.Enqueue(prefix);

            while (foldersToProcess.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string currentPrefix = foldersToProcess.Dequeue();
                var result = await S3Manager.Instance.ListObjectsAsync(bucketName, currentPrefix);

                // Add files from current folder
                allFiles.AddRange(result.Files);

                // Add subfolders to process
                foreach (var folder in result.Folders)
                {
                    foldersToProcess.Enqueue(folder.FullKey);
                }
            }

            return allFiles;
        }
    }

    /// <summary>
    /// Represents an item in S3 (bucket, folder, or file) for display in the UI.
    /// </summary>
    public class S3Item : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isDownloading;
        private double _downloadProgress;
        private CancellationTokenSource? _downloadCancellationTokenSource;

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
        /// Gets or sets the raw size in bytes.
        /// For files, contains the actual byte count. For folders and buckets, is null.
        /// </summary>
        public long? SizeInBytes { get; set; }

        /// <summary>
        /// Gets the formatted size of the item.
        /// For files, shows size in B, KB, MB, GB, or TB. For folders and buckets, shows "--".
        /// Automatically computed from SizeInBytes.
        /// </summary>
        public string Size => SizeInBytes.HasValue ? FileHelper.FormatFileSize(SizeInBytes.Value) : "--";

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

        /// <summary>
        /// Gets or sets a value indicating whether gets or sets whether this file is currently being downloaded.
        /// </summary>
        public bool IsDownloading
        {
            get => _isDownloading;
            set
            {
                if (_isDownloading != value)
                {
                    _isDownloading = value;
                    OnPropertyChanged(nameof(IsDownloading));
                }
            }
        }

        /// <summary>
        /// Gets or sets the download progress (0-100).
        /// </summary>
        public double DownloadProgress
        {
            get => _downloadProgress;
            set
            {
                if (Math.Abs(_downloadProgress - value) > 0.01)
                {
                    _downloadProgress = value;
                    OnPropertyChanged(nameof(DownloadProgress));
                }
            }
        }

        /// <summary>
        /// Gets or sets the cancellation token source for the download operation.
        /// </summary>
        public CancellationTokenSource? DownloadCancellationTokenSource
        {
            get => _downloadCancellationTokenSource;
            set => _downloadCancellationTokenSource = value;
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
