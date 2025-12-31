using System.Windows;

namespace S3Browser
{
    /// <summary>
    /// Dialog window for selecting an AWS profile for authentication.
    /// Pre-populates with the default profile from configuration.
    /// </summary>
    public partial class ProfileSelectionDialog : Window
    {
        /// <summary>
        /// Gets the AWS profile name selected by the user.
        /// Null if dialog was cancelled or no profile was selected.
        /// </summary>
        public string? SelectedProfile { get; private set; }

        /// <summary>
        /// Initializes a new instance of the ProfileSelectionDialog.
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
