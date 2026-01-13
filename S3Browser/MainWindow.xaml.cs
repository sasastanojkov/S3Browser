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
        private bool _isAnonymousMode;
        private string? _currentBucket;
        private string _currentPrefix = string.Empty;
        private Stack<string> _navigationStack = new Stack<string>();
        private HashSet<string> _publicBuckets = new HashSet<string>();
        private Dictionary<string, RegionEndpoint> _bucketRegions = new Dictionary<string, RegionEndpoint>();

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
                _awsProfile = dialog.SelectedProfile;
                _isAnonymousMode = dialog.IsAnonymousMode;

                if (_isAnonymousMode)
                {
                    // In anonymous mode, show empty list with instructions
                    ShowAnonymousWelcomeMessage();
                }
                else
                {
                    // Load buckets with credentials
                    LoadBucketsAsync();
                }
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
                // In anonymous mode, we don't list buckets - user must provide bucket name
                if (_isAnonymousMode)
                {
                    ShowAnonymousWelcomeMessage();
                    return;
                }

                // Show loading status
                StatusTextBlock.Text = "Loading AWS credentials...";
                StatusProgressBar.Visibility = Visibility.Visible;
                ItemCountTextBlock.Text = "";

                var chain = new CredentialProfileStoreChain();
                if (!chain.TryGetProfile(_awsProfile!, out var profile))
                {
                    StatusTextBlock.Text = "Error: Profile not found";
                    StatusProgressBar.Visibility = Visibility.Collapsed;
                    MessageBox.Show($"Could not load AWS profile '{_awsProfile}'.\n\nMake sure the profile exists in your AWS configuration.",
                        "AWS Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!chain.TryGetAWSCredentials(_awsProfile!, out var credentials))
                {
                    StatusTextBlock.Text = "Error: Credentials not available";
                    StatusProgressBar.Visibility = Visibility.Collapsed;
                    MessageBox.Show($"Could not load AWS credentials for profile '{_awsProfile}'.\n\nMake sure you have run 'aws sso login --profile {_awsProfile}' before starting this application.",
                        "AWS Authentication Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusTextBlock.Text = "Loading S3 buckets...";

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

                // Add public buckets to the list
                foreach (var publicBucket in _publicBuckets)
                {
                    Items.Add(new S3Item
                    {
                        Type = "Bucket",
                        Name = publicBucket + " (Public)",
                        Size = "--",
                        LastModified = "--"
                    });
                }

                UpdateBreadcrumb();

                // Update status
                int bucketCount = Items.Count;
                StatusTextBlock.Text = "Ready";
                StatusProgressBar.Visibility = Visibility.Collapsed;
                ItemCountTextBlock.Text = $"{bucketCount} bucket{(bucketCount != 1 ? "s" : "")}";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error loading buckets";
                StatusProgressBar.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Error loading S3 buckets: {ex.Message}\n\nMake sure you have run 'aws sso login --profile {_awsProfile}' before starting this application.",
                    "AWS Authentication Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private async void LoadBucketContentsAsync(string bucketName, string prefix = "")
        {
            try
            {
                // Show loading status
                StatusTextBlock.Text = string.IsNullOrEmpty(prefix)
                    ? $"Loading bucket '{bucketName}'..."
                    : $"Loading folder contents...";
                StatusProgressBar.Visibility = Visibility.Visible;
                ItemCountTextBlock.Text = "";

                // Determine which S3 client to use
                IAmazonS3? s3Client = null;

                if (_publicBuckets.Contains(bucketName))
                {
                    // Get the region for this public bucket
                    RegionEndpoint region = _bucketRegions.ContainsKey(bucketName)
                        ? _bucketRegions[bucketName]
                        : RegionEndpoint.USEast1;
                    s3Client = new AmazonS3Client(new Amazon.Runtime.AnonymousAWSCredentials(), region);
                }
                else
                {
                    // For private buckets, try to get the bucket's region and create a region-specific client
                    // This ensures we're using the correct endpoint for the bucket

                    // In anonymous mode, treat all non-cached buckets as public
                    if (_isAnonymousMode)
                    {
                        // Try to access as public bucket
                        StatusTextBlock.Text = "Detecting region for public bucket...";
                        try
                        {
                            var bucketRegion = await DetectBucketRegionAsync(bucketName);
                            _bucketRegions[bucketName] = bucketRegion;
                            s3Client = new AmazonS3Client(new Amazon.Runtime.AnonymousAWSCredentials(), bucketRegion);
                        }
                        catch
                        {
                            // Use default region
                            s3Client = new AmazonS3Client(new Amazon.Runtime.AnonymousAWSCredentials(), RegionEndpoint.USEast1);
                        }

                        StatusTextBlock.Text = string.IsNullOrEmpty(prefix)
                            ? $"Loading bucket '{bucketName}'..."
                            : $"Loading folder contents...";
                    }
                    else if (_bucketRegions.ContainsKey(bucketName))
                    {
                        // We already know the region for this bucket, create a client with that region
                        var chain = new CredentialProfileStoreChain();
                        if (chain.TryGetAWSCredentials(_awsProfile!, out var credentials))
                        {
                            s3Client = new AmazonS3Client(credentials, _bucketRegions[bucketName]);
                        }
                        else
                        {
                            s3Client = _s3Client;
                        }
                    }
                    else
                    {
                        // Try to detect the bucket's region
                        StatusTextBlock.Text = "Detecting bucket region...";
                        try
                        {
                            var bucketRegion = await DetectPrivateBucketRegionAsync(bucketName);
                            if (bucketRegion != null)
                            {
                                // Cache the region
                                _bucketRegions[bucketName] = bucketRegion;

                                // Create a client with the correct region
                                var chain = new CredentialProfileStoreChain();
                                if (chain.TryGetAWSCredentials(_awsProfile!, out var credentials))
                                {
                                    s3Client = new AmazonS3Client(credentials, bucketRegion);
                                }
                                else
                                {
                                    s3Client = _s3Client;
                                }
                            }
                            else
                            {
                                // Fall back to the default client
                                s3Client = _s3Client;
                            }
                        }
                        catch
                        {
                            // If region detection fails, use the default client
                            s3Client = _s3Client;
                        }

                        StatusTextBlock.Text = string.IsNullOrEmpty(prefix)
                            ? $"Loading bucket '{bucketName}'..."
                            : $"Loading folder contents...";
                    }
                }

                if (s3Client == null)
                {
                    MessageBox.Show("S3 client is not initialized. Please restart the application.",
                        "Client Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var request = new ListObjectsV2Request
                {
                    BucketName = bucketName,
                    Prefix = prefix,
                    Delimiter = "/"
                };

                var response = await s3Client.ListObjectsV2Async(request);

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

                // Update status
                int totalItems = Items.Count - 1; // Subtract the ".." entry
                int folderCount = Items.Count(i => i.Type == "Folder" && i.Name != "..");
                int fileCount = Items.Count(i => i.Type == "File");

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
            catch (AmazonS3Exception ex)
            {
                StatusTextBlock.Text = "Error loading contents";
                StatusProgressBar.Visibility = Visibility.Collapsed;
                MessageBox.Show($"AWS S3 Error: {ex.Message}\n\nError Code: {ex.ErrorCode}\nStatus Code: {ex.StatusCode}",
                    "S3 Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error loading contents";
                StatusProgressBar.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Error loading bucket contents: {ex.Message}\n\n{ex.GetType().Name}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Detects the region of a private S3 bucket using authenticated credentials.
        /// </summary>
        private async Task<RegionEndpoint?> DetectPrivateBucketRegionAsync(string bucketName)
        {
            try
            {
                // Use the authenticated client to get bucket location
                if (_s3Client != null)
                {
                    var locationRequest = new GetBucketLocationRequest
                    {
                        BucketName = bucketName
                    };
                    var locationResponse = await _s3Client.GetBucketLocationAsync(locationRequest);

                    // Convert S3 location to RegionEndpoint
                    if (string.IsNullOrEmpty(locationResponse.Location.Value) || locationResponse.Location.Value == "us-east-1" || locationResponse.Location.Value == "")
                    {
                        return RegionEndpoint.USEast1;
                    }

                    return RegionEndpoint.GetBySystemName(locationResponse.Location.Value);
                }
            }
            catch
            {
                // If detection fails, return null
            }

            return null;
        }

        /// <summary>
        /// Gets the appropriate S3 client for the current bucket with the correct region.
        /// </summary>
        private async Task<IAmazonS3?> GetS3ClientForCurrentBucketAsync()
        {
            if (_currentBucket == null)
                return null;

            // Check if it's a public bucket
            if (_publicBuckets.Contains(_currentBucket))
            {
                RegionEndpoint region = _bucketRegions.ContainsKey(_currentBucket)
                    ? _bucketRegions[_currentBucket]
                    : RegionEndpoint.USEast1;
                return new AmazonS3Client(new Amazon.Runtime.AnonymousAWSCredentials(), region);
            }

            // In anonymous mode, treat all buckets as public
            if (_isAnonymousMode)
            {
                RegionEndpoint region;
                if (_bucketRegions.ContainsKey(_currentBucket))
                {
                    region = _bucketRegions[_currentBucket];
                }
                else
                {
                    // Detect region
                    region = await DetectBucketRegionAsync(_currentBucket);
                    _bucketRegions[_currentBucket] = region;
                }
                return new AmazonS3Client(new Amazon.Runtime.AnonymousAWSCredentials(), region);
            }

            // For private buckets, check if we already have the region cached
            if (_bucketRegions.ContainsKey(_currentBucket))
            {
                var chain = new CredentialProfileStoreChain();
                if (chain.TryGetAWSCredentials(_awsProfile!, out var credentials))
                {
                    return new AmazonS3Client(credentials, _bucketRegions[_currentBucket]);
                }
            }
            else
            {
                // Try to detect the region
                try
                {
                    var bucketRegion = await DetectPrivateBucketRegionAsync(_currentBucket);
                    if (bucketRegion != null)
                    {
                        _bucketRegions[_currentBucket] = bucketRegion;

                        var chain = new CredentialProfileStoreChain();
                        if (chain.TryGetAWSCredentials(_awsProfile!, out var credentials))
                        {
                            return new AmazonS3Client(credentials, bucketRegion);
                        }
                    }
                }
                catch
                {
                    // Region detection failed, fall through to default client
                }
            }

            // Fall back to default client
            return _s3Client;
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
                    // Remove " (Public)" suffix if present
                    string bucketName = selectedItem.Name;
                    if (bucketName.EndsWith(" (Public)"))
                    {
                        bucketName = bucketName.Substring(0, bucketName.Length - 9);
                    }
                    _currentBucket = bucketName;
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
                if (_currentBucket == null || string.IsNullOrEmpty(fileItem.FullKey))
                    return;

                // Get the appropriate S3 client with correct region
                var s3Client = await GetS3ClientForCurrentBucketAsync();

                if (s3Client == null)
                    return;

                var headRequest = new GetObjectMetadataRequest
                {
                    BucketName = _currentBucket,
                    Key = fileItem.FullKey
                };

                var metadata = await s3Client.GetObjectMetadataAsync(headRequest);
                long fileSize = metadata.ContentLength;

                var viewer = new FileViewerWindow(s3Client, _currentBucket, fileItem.FullKey, fileItem.Name, fileSize);
                viewer.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file viewer: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OpenParquetFileViewer(S3Item fileItem)
        {
            try
            {
                if (_currentBucket == null || string.IsNullOrEmpty(fileItem.FullKey))
                    return;

                // Check if this is a public bucket
                bool isPublicBucket = _publicBuckets.Contains(_currentBucket);

                // Get the appropriate S3 client with correct region
                var s3Client = await GetS3ClientForCurrentBucketAsync();

                if (s3Client == null)
                    return;

                var viewer = new ParquetViewerWindow(s3Client, _currentBucket, fileItem.FullKey, fileItem.Name, false, _awsProfile, null, isPublicBucket);
                viewer.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening parquet viewer: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OpenTabularFileViewer(S3Item fileItem, string fileType)
        {
            try
            {
                if (_currentBucket == null || string.IsNullOrEmpty(fileItem.FullKey))
                    return;

                // Get the appropriate S3 client with correct region
                var s3Client = await GetS3ClientForCurrentBucketAsync();

                if (s3Client == null)
                    return;

                var viewer = new TabularFileViewerWindow(s3Client, _currentBucket, fileItem.FullKey, fileItem.Name, fileType);
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

                // Show bucket access status
                StatusTextBlock.Text = $"Accessing bucket '{bucketName}'...";

                // Try to determine the appropriate S3 client and bucket accessibility
                IAmazonS3? s3Client = await GetS3ClientForBucketAsync(bucketName);

                if (s3Client == null)
                {
                    StatusTextBlock.Text = "Error: Bucket not accessible";
                    StatusProgressBar.Visibility = Visibility.Collapsed;
                    MessageBox.Show($"Unable to access bucket '{bucketName}'. The bucket may not exist, may not be publicly accessible, or you may not have permission to access it.",
                        "Access Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Check if it's a file or folder
                if (!string.IsNullOrEmpty(key))
                {
                    // Try to get object metadata to determine if it's a file
                    try
                    {
                        StatusTextBlock.Text = "Checking object metadata...";

                        var headRequest = new GetObjectMetadataRequest
                        {
                            BucketName = bucketName,
                            Key = key
                        };

                        var metadata = await s3Client.GetObjectMetadataAsync(headRequest);

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
                    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
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
                LoadBucketContentsAsync(_currentBucket, _currentPrefix);
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

        private async void ReadAllParquetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentBucket == null)
                    return;

                // Check if this is a public bucket
                bool isPublicBucket = _publicBuckets.Contains(_currentBucket);

                // Get the appropriate S3 client with correct region
                var s3Client = await GetS3ClientForCurrentBucketAsync();

                if (s3Client == null)
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
                var viewer = new ParquetViewerWindow(s3Client, _currentBucket, wildcardPattern, folderName, isWildcard: true, awsProfile: _awsProfile, customQuery: null, isPublicBucket: isPublicBucket);
                viewer.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening parquet viewer: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void WriteQueryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentBucket == null)
                    return;

                // Check if this is a public bucket
                bool isPublicBucket = _publicBuckets.Contains(_currentBucket);

                // Get the appropriate S3 client with correct region
                var s3Client = await GetS3ClientForCurrentBucketAsync();

                if (s3Client == null)
                    return;

                // Create a wildcard pattern for all parquet files in the current prefix
                string wildcardPattern = string.IsNullOrEmpty(_currentPrefix)
                    ? "*.parquet"
                    : $"{_currentPrefix.TrimEnd('/')}/*.parquet";

                string s3Path = $"s3://{_currentBucket}/{wildcardPattern.Replace("*.parquet", "")}*.parquet";

                // Generate initial query
                string initialQuery = $"SELECT * FROM read_parquet('{s3Path}')";

                // Extract folder name for window title
                string folderName = string.IsNullOrEmpty(_currentPrefix)
                    ? _currentBucket
                    : _currentPrefix.TrimEnd('/').Split('/').Last();

                // Open query editor dialog
                var queryDialog = new QueryEditorDialog(s3Client, _currentBucket, initialQuery, folderName, _awsProfile, null, isPublicBucket);
                queryDialog.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening query editor: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Gets the appropriate S3 client for a bucket, detecting if it's accessible with credentials or publicly.
        /// Caches the result for subsequent access.
        /// </summary>
        private async Task<IAmazonS3?> GetS3ClientForBucketAsync(string bucketName)
        {
            // Check if we already know this is a public bucket
            if (_publicBuckets.Contains(bucketName))
            {
                RegionEndpoint region = _bucketRegions.ContainsKey(bucketName)
                    ? _bucketRegions[bucketName]
                    : RegionEndpoint.USEast1;
                return new AmazonS3Client(new Amazon.Runtime.AnonymousAWSCredentials(), region);
            }

            // In anonymous mode, skip authenticated access and go straight to public
            if (!_isAnonymousMode && _s3Client != null)
            {
                // First, try with the authenticated client
                try
                {
                    StatusTextBlock.Text = $"Testing authenticated access to '{bucketName}'...";

                    var testRequest = new ListObjectsV2Request
                    {
                        BucketName = bucketName,
                        MaxKeys = 1
                    };
                    await _s3Client.ListObjectsV2Async(testRequest);
                    return _s3Client;
                }
                catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                                                     ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // Access denied with credentials, try public access
                    StatusTextBlock.Text = $"Trying public access to '{bucketName}'...";
                }
                catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Bucket doesn't exist
                    return null;
                }
                catch
                {
                    // Other error, continue to try public access
                    StatusTextBlock.Text = $"Trying public access to '{bucketName}'...";
                }
            }
            else if (_isAnonymousMode)
            {
                StatusTextBlock.Text = $"Accessing public bucket '{bucketName}'...";
            }

            // Try to access as a public bucket
            try
            {
                StatusTextBlock.Text = $"Detecting region for public bucket '{bucketName}'...";
                RegionEndpoint region = await DetectBucketRegionAsync(bucketName);

                StatusTextBlock.Text = $"Testing public access to '{bucketName}'...";
                var anonymousClient = new AmazonS3Client(new Amazon.Runtime.AnonymousAWSCredentials(), region);

                var testRequest = new ListObjectsV2Request
                {
                    BucketName = bucketName,
                    MaxKeys = 1
                };
                await anonymousClient.ListObjectsV2Async(testRequest);

                // Success! Cache this as a public bucket
                _publicBuckets.Add(bucketName);
                _bucketRegions[bucketName] = region;

                // Add to the displayed list if we're at the bucket list view
                if (_currentBucket == null && !_isAnonymousMode)
                {
                    Items.Add(new S3Item
                    {
                        Type = "Bucket",
                        Name = bucketName + " (Public)",
                        Size = "--",
                        LastModified = "--"
                    });
                }

                return anonymousClient;
            }
            catch
            {
                // Unable to access bucket
                return null;
            }
        }

        /// <summary>
        /// Detects the region of an S3 bucket by trying GetBucketLocation or common regions.
        /// </summary>
        private async Task<RegionEndpoint> DetectBucketRegionAsync(string bucketName)
        {
            // First, try to use GetBucketLocation API with us-east-1 client
            try
            {
                var usEast1Client = new AmazonS3Client(new Amazon.Runtime.AnonymousAWSCredentials(), RegionEndpoint.USEast1);
                var locationRequest = new GetBucketLocationRequest
                {
                    BucketName = bucketName
                };
                var locationResponse = await usEast1Client.GetBucketLocationAsync(locationRequest);

                // Convert S3 location to RegionEndpoint
                if (string.IsNullOrEmpty(locationResponse.Location.Value) || locationResponse.Location.Value == "us-east-1")
                {
                    return RegionEndpoint.USEast1;
                }

                return RegionEndpoint.GetBySystemName(locationResponse.Location.Value);
            }
            catch
            {
                // GetBucketLocation failed, try brute force approach
            }

            // Try common regions by attempting to list objects
            var regionsToTry = new[]
            {
                RegionEndpoint.USEast1,
                RegionEndpoint.USWest2,
                RegionEndpoint.USWest1,
                RegionEndpoint.USEast2,
                RegionEndpoint.EUWest1,
                RegionEndpoint.EUCentral1,
                RegionEndpoint.APSoutheast1,
                RegionEndpoint.APNortheast1
            };

            foreach (var region in regionsToTry)
            {
                try
                {
                    var client = new AmazonS3Client(new Amazon.Runtime.AnonymousAWSCredentials(), region);
                    var request = new ListObjectsV2Request
                    {
                        BucketName = bucketName,
                        MaxKeys = 1
                    };
                    await client.ListObjectsV2Async(request);
                    return region;
                }
                catch
                {
                    continue;
                }
            }

            // Default to us-east-1 if detection fails
            return RegionEndpoint.USEast1;
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
