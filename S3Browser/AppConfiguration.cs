using Microsoft.Extensions.Configuration;

namespace S3Browser
{
    /// <summary>
    /// Singleton configuration manager for application settings loaded from appsettings.json.
    /// Thread-safe implementation using Lazy&lt;T&gt; pattern.
    /// </summary>
    public sealed class AppConfiguration
    {
        private static readonly Lazy<AppConfiguration> _lazyInstance = new(() => new AppConfiguration());
        private readonly IConfiguration _configuration;

        private AppConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            _configuration = builder.Build();
        }

        /// <summary>
        /// Gets the singleton instance of the AppConfiguration.
        /// </summary>
        public static AppConfiguration Instance => _lazyInstance.Value;

        /// <summary>
        /// Gets the default AWS profile name from configuration.
        /// Returns "default" if not specified in appsettings.json.
        /// </summary>
        public string DefaultAwsProfile => _configuration["AppSettings:DefaultAwsProfile"] ?? "default";
    }
}
