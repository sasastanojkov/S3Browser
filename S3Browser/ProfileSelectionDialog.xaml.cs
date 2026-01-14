using System.Windows;
using Amazon.Runtime.CredentialManagement;

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
        /// Loads available AWS profiles and pre-selects the default profile from configuration.
        /// </summary>
        public ProfileSelectionDialog()
        {
            InitializeComponent();

            LoadAvailableProfiles();

            ProfileComboBox.Focus();
        }

        /// <summary>
        /// Loads available AWS profiles from the credential store into the ComboBox.
        /// Pre-selects the default profile from configuration if available.
        /// </summary>
        private void LoadAvailableProfiles()
        {
            try
            {
                var credentialChain = new CredentialProfileStoreChain();
                var profiles = credentialChain.ListProfiles();

                foreach (var profile in profiles.OrderBy(p => p.Name))
                {
                    ProfileComboBox.Items.Add(profile.Name);
                }

                // Set default profile from configuration if it exists, otherwise select first available
                string defaultProfile = AppConfiguration.Instance.DefaultAwsProfile;
                if (!string.IsNullOrEmpty(defaultProfile) && ProfileComboBox.Items.Contains(defaultProfile))
                {
                    ProfileComboBox.Text = defaultProfile;
                }
                else if (ProfileComboBox.Items.Count > 0)
                {
                    ProfileComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                // If we can't load profiles, the ComboBox will be empty but still editable
                MessageBox.Show($"Could not load AWS profiles: {ex.Message}\n\nYou can still enter a profile name manually.",
                    "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ProfileComboBox.Text))
            {
                MessageBox.Show("Please select or enter a profile name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedProfile = ProfileComboBox.Text.Trim();
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
