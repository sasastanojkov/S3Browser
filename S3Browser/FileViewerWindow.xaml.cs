using System.IO;
using System.Windows;
using System.Windows.Controls;
using Amazon.S3;
using Amazon.S3.Model;
using S3Browser.Helpers;

namespace S3Browser
{
    /// <summary>
    /// Window for viewing text-based files from S3.
    /// Displays content in a read-only text editor with configurable read limits.
    /// </summary>
    public partial class FileViewerWindow : Window
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;
        private readonly string _key;
        private readonly long _fileSize;
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>
        /// Initializes a new instance of the FileViewerWindow.
        /// </summary>
        /// <param name="s3Client">AWS S3 client for accessing S3 resources.</param>
        /// <param name="bucketName">Name of the S3 bucket containing the file.</param>
        /// <param name="key">S3 key (path) to the file.</param>
        /// <param name="fileName">Display name for the file.</param>
        /// <param name="fileSize">Size of the file in bytes.</param>
        public FileViewerWindow(IAmazonS3 s3Client, string bucketName, string key, string fileName, long fileSize)
        {
            InitializeComponent();

            _s3Client = s3Client;
            _bucketName = bucketName;
            _key = key;
            _fileSize = fileSize;

            FileNameTextBlock.Text = $"File: {fileName}";
            Title = $"File Viewer - {fileName}";

            LoadFileContentAsync();
        }

        private async void LoadFileContentAsync()
        {
            // Cancel any existing operation
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;

            try
            {
                var selectedItem = ReadLimitComboBox.SelectedItem as ComboBoxItem;
                if (selectedItem == null) return;

                long readLimitBytes = Convert.ToInt64(selectedItem.Tag);

                StatusTextBlock.Text = "Loading file...";
                ContentTextBox.Text = "";

                var request = new GetObjectRequest
                {
                    BucketName = _bucketName,
                    Key = _key
                };

                using (var response = await _s3Client.GetObjectAsync(request, cancellationToken))
                {
                    if (readLimitBytes == -1)
                    {
                        // Read entire file
                        using (var reader = new StreamReader(response.ResponseStream))
                        {
                            ContentTextBox.Text = await reader.ReadToEndAsync(cancellationToken);
                            StatusTextBlock.Text = $"Loaded entire file ({FileHelper.FormatFileSize(_fileSize)})";
                        }
                    }
                    else
                    {
                        // Read limited number of bytes
                        long bytesToRead = Math.Min(readLimitBytes, _fileSize);
                        byte[] buffer = new byte[bytesToRead];

                        int totalBytesRead = 0;
                        int bytesRead;
                        while (totalBytesRead < bytesToRead &&
                               (bytesRead = await response.ResponseStream.ReadAsync(buffer, totalBytesRead, (int)(bytesToRead - totalBytesRead), cancellationToken)) > 0)
                        {
                            totalBytesRead += bytesRead;
                        }

                        // Convert bytes to string using UTF-8 encoding
                        ContentTextBox.Text = System.Text.Encoding.UTF8.GetString(buffer, 0, totalBytesRead);

                        if (_fileSize > bytesToRead)
                        {
                            StatusTextBlock.Text = $"Loaded {FileHelper.FormatFileSize(totalBytesRead)} of {FileHelper.FormatFileSize(_fileSize)} (file truncated)";
                        }
                        else
                        {
                            StatusTextBlock.Text = $"Loaded entire file ({FileHelper.FormatFileSize(_fileSize)})";
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Operation cancelled";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "Error loading file";
            }
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            LoadFileContentAsync();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Cancel any ongoing operations
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
        }
    }
}
