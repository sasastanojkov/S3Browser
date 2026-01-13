using System.Windows;

namespace S3Browser
{
    /// <summary>
    /// Dialog window for selecting an AWS profile for authentication.
    /// Pre-populates with the default profile from configuration.
    /// Supports anonymous access mode for public buckets.
    /// </summary>
    public partial class ProfileSelectionDialog : Window
    {
        /// <summary>
        /// Gets the AWS profile name selected by the user.
        /// Null if dialog was cancelled, no profile was selected, or anonymous mode was chosen.
        /// </summary>
        public string? SelectedProfile { get; private set; }

        /// <summary>
        /// Gets a value indicating whether gets whether the user chose to use anonymous access mode.
        /// When true, no AWS credentials will be used - only public buckets are accessible.
        /// </summary>
        public bool IsAnonymousMode { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProfileSelectionDialog"/> class.
        /// Loads the default AWS profile from configuration and pre-fills the text box.
        /// </summary>
        public ProfileSelectionDialog()
        {
            InitializeComponent();

            // Load default profile from configuration
            ProfileTextBox.Text = AppConfiguration.Instance.DefaultAwsProfile;

            ProfileTextBox.Focus();
            ProfileTextBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ProfileTextBox.Text))
            {
                MessageBox.Show("Please enter a profile name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedProfile = ProfileTextBox.Text.Trim();
            IsAnonymousMode = false;
            DialogResult = true;
            Close();
        }

        private void AnonymousButton_Click(object sender, RoutedEventArgs e)
        {
            // User chose anonymous access - no profile needed
            SelectedProfile = null;
            IsAnonymousMode = true;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
