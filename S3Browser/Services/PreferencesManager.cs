using System.IO;
using System.Text.Json;
using S3Browser.Models;

namespace S3Browser.Services
{
    /// <summary>
    /// Manages loading and saving user preferences.
    /// Preferences are stored as JSON in the user's AppData folder.
    /// </summary>
    public static class PreferencesManager
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "S3Browser",
            "preferences.json");

        private static UserPreferences? _current;
        private static readonly object _lock = new();

        /// <summary>
        /// Gets the current user preferences singleton instance.
        /// Loads from disk on first access.
        /// </summary>
        public static UserPreferences Current
        {
            get
            {
                lock (_lock)
                {
                    if (_current == null)
                    {
                        _current = Load();
                    }
                    return _current;
                }
            }
        }

        /// <summary>
        /// Loads user preferences from disk.
        /// Returns default preferences if file doesn't exist or can't be loaded.
        /// </summary>
        /// <returns>User preferences object.</returns>
        public static UserPreferences Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    return new UserPreferences();
                }

                var json = File.ReadAllText(ConfigPath);
                var preferences = JsonSerializer.Deserialize<UserPreferences>(json);
                return preferences ?? new UserPreferences();
            }
            catch (Exception ex)
            {
                // Log error but don't crash - return defaults
                System.Diagnostics.Debug.WriteLine($"Failed to load preferences: {ex.Message}");
                return new UserPreferences();
            }
        }

        /// <summary>
        /// Saves user preferences to disk.
        /// Creates the directory if it doesn't exist.
        /// </summary>
        /// <param name="preferences">The preferences to save. If null, saves the current instance.</param>
        public static void Save(UserPreferences? preferences = null)
        {
            lock (_lock)
            {
                try
                {
                    var prefs = preferences ?? _current ?? new UserPreferences();

                    // Update current instance if saving a different object
                    if (preferences != null && preferences != _current)
                    {
                        _current = preferences;
                    }

                    // Create directory if it doesn't exist
                    var directory = Path.GetDirectoryName(ConfigPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Serialize with indentation for readability
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true
                    };

                    var json = JsonSerializer.Serialize(prefs, options);
                    File.WriteAllText(ConfigPath, json);
                }
                catch (Exception ex)
                {
                    // Log error but don't crash
                    System.Diagnostics.Debug.WriteLine($"Failed to save preferences: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Resets preferences to default values and saves to disk.
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _current = new UserPreferences();
                Save();
            }
        }

        /// <summary>
        /// Gets the full path to the preferences file.
        /// Useful for debugging or backup purposes.
        /// </summary>
        public static string GetPreferencesFilePath() => ConfigPath;
    }
}
