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

                List<string> validProfileNames = new List<string>();

                try
                {
                    // Try to get all profiles
                    var profiles = credentialChain.ListProfiles();

                    // Validate each profile and only add ones that can be accessed
                    foreach (var profile in profiles.OrderBy(p => p.Name))
                    {
                        try
                        {
                            // Try to validate the profile by attempting to get credentials info
                            // This will skip profiles with SSO configuration errors
                            if (credentialChain.TryGetProfile(profile.Name, out _))
                            {
                                validProfileNames.Add(profile.Name);
                            }
                        }
                        catch
                        {
                            // Skip profiles that have configuration errors
                            System.Diagnostics.Debug.WriteLine($"Skipping invalid profile: {profile.Name}");
                        }
                    }
                }
                catch (Amazon.Runtime.AmazonClientException ex) when (ex.Message.Contains("sso_session"))
                {
                    // Handle SSO session configuration errors
                    System.Diagnostics.Debug.WriteLine($"SSO configuration error: {ex.Message}");

                    // Try to load profiles from the credentials file directly (skip config file with SSO errors)
                    try
                    {
                        var credentialsFile = new SharedCredentialsFile();
                        var credProfiles = credentialsFile.ListProfiles();

                        foreach (var profile in credProfiles.OrderBy(p => p.Name))
                        {
                            validProfileNames.Add(profile.Name);
                        }
                    }
                    catch
                    {
                        // If even credentials file fails, we'll show a message
                    }
                }

                // Add valid profiles to ComboBox
                foreach (var profileName in validProfileNames)
                {
                    ProfileComboBox.Items.Add(profileName);
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

                // Show helpful message if no valid profiles were found
                if (ProfileComboBox.Items.Count == 0)
                {
                    MessageBox.Show(
                        "No valid AWS profiles were found.\n\n" +
                        "Common causes:\n" +
                        " - SSO session configuration errors in ~/.aws/config\n" +
                        " - Missing or invalid credentials\n\n" +
                        "You can:\n" +
                        " - Enter a profile name manually in the text box\n" +
                        " - Use Anonymous access for public buckets\n" +
                        " - Fix your AWS configuration files",
                        "No Profiles Available",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                // If we can't load profiles, the ComboBox will be empty but still editable
                MessageBox.Show(
                    $"Could not load AWS profiles: {ex.Message}\n\n" +
                    "You can still:\n" +
                    " - Enter a profile name manually\n" +
                    " - Use Anonymous access for public buckets",
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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
