using System.Text.Json;
using System.Windows;
using S3Browser.Models;
using S3Browser.Services;

namespace S3Browser
{
    /// <summary>
    /// Settings window for configuring user preferences.
    /// Allows users to modify application settings that persist across sessions.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly UserPreferences _preferences;
        private readonly UserPreferences _originalPreferences;

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
        /// </summary>
        public SettingsWindow()
        {
            InitializeComponent();

            // Load current preferences
            _originalPreferences = PreferencesManager.Current;

            // Create a copy for editing (allows Cancel to work)
            _preferences = ClonePreferences(_originalPreferences);

            // Set data context for binding
            DataContext = _preferences;
        }

        /// <summary>
        /// Creates a deep copy of preferences using JSON serialization.
        /// This allows us to edit a copy without modifying the original until OK is clicked.
        /// </summary>
        private static UserPreferences ClonePreferences(UserPreferences original)
        {
            var json = JsonSerializer.Serialize(original);
            return JsonSerializer.Deserialize<UserPreferences>(json) ?? new UserPreferences();
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate settings
            if (_preferences.MaxTableSizeMB < 100)
            {
                MessageBox.Show("Maximum table size must be at least 100 MB.", "Invalid Setting",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_preferences.MaxTableSizeMB > 2000)
            {
                MessageBox.Show("Maximum table size cannot exceed 2000 MB (2 GB).", "Invalid Setting",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Save preferences
            PreferencesManager.Save(_preferences);

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to reset all settings to their default values?\n\n" +
                "This action cannot be undone.",
                "Confirm Reset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                PreferencesManager.Reset();
                DialogResult = true;
                Close();
            }
        }
    }
}
