namespace S3Browser.Constants
{
    /// <summary>
    /// Application-wide constants for consistent behavior across the S3Browser application.
    /// Centralized location for magic numbers, strings, and configuration values.
    /// </summary>
    public static class AppConstants
    {
        /// <summary>
        /// Constants related to text display and truncation in the UI.
        /// </summary>
        public static class TextDisplay
        {
            /// <summary>
            /// Maximum length for truncated text display in cells (50 characters).
            /// </summary>
            public const int MaxTruncatedLength = 50;

            /// <summary>
            /// String indicator shown when text is truncated ("...").
            /// </summary>
            public const string TruncationIndicator = "...";
        }

        /// <summary>
        /// File extension constants for file type detection.
        /// </summary>
        public static class FileExtensions
        {
            /// <summary>
            /// Parquet file extension (".parquet").
            /// </summary>
            public const string Parquet = ".parquet";

            /// <summary>
            /// CSV file extension (".csv").
            /// </summary>
            public const string Csv = ".csv";

            /// <summary>
            /// TSV file extension (".tsv").
            /// </summary>
            public const string Tsv = ".tsv";

            /// <summary>
            /// Array of recognized text file extensions.
            /// Includes: .txt, .json, .xml, .log, .md, .yaml, .yml, .config, .ini, .properties,
            /// .html, .htm, .css, .js, .ts, .sql, .sh, .bat, .ps1
            /// </summary>
            public static readonly string[] TextFiles =
            {
                ".txt", ".json", ".xml", ".log", ".md", ".yaml", ".yml",
                ".config", ".ini", ".properties", ".html", ".htm", ".css",
                ".js", ".ts", ".sql", ".sh", ".bat", ".ps1"
            };

            /// <summary>
            /// Array of WKT geometry type prefixes for geometry detection.
            /// Includes: POINT, LINESTRING, POLYGON, MULTIPOINT, MULTILINESTRING, MULTIPOLYGON
            /// </summary>
            public static readonly string[] GeometryWktPrefixes =
            {
                "POINT", "LINESTRING", "POLYGON",
                "MULTIPOINT", "MULTILINESTRING", "MULTIPOLYGON"
            };
        }

        /// <summary>
        /// Constants for S3 path prefixes and formats.
        /// </summary>
        public static class S3Paths
        {
            /// <summary>
            /// Standard S3 path prefix ("s3://").
            /// </summary>
            public const string StandardPrefix = "s3://";

            /// <summary>
            /// Alternate S3 path prefix used by some tools ("s3a://").
            /// </summary>
            public const string AlternatePrefix = "s3a://";
        }

        /// <summary>
        /// Default dialog window dimensions.
        /// </summary>
        public static class DialogSizes
        {
            /// <summary>
            /// Default width for content viewer dialogs (600 pixels).
            /// </summary>
            public const int ContentViewerWidth = 600;

            /// <summary>
            /// Default height for content viewer dialogs (400 pixels).
            /// </summary>
            public const int ContentViewerHeight = 400;
        }

        /// <summary>
        /// DuckDB-specific configuration constants.
        /// </summary>
        public static class DuckDb
        {
            /// <summary>
            /// Connection string for in-memory DuckDB database ("Data Source=:memory:").
            /// </summary>
            public const string InMemoryConnectionString = "Data Source=:memory:";

            /// <summary>
            /// Name of the HTTP filesystem extension for S3 access ("httpfs").
            /// </summary>
            public const string HttpFsExtension = "httpfs";

            /// <summary>
            /// Default AWS region for S3 access ("us-east-1").
            /// </summary>
            public const string DefaultRegion = "us-east-1";
        }

        /// <summary>
        /// DataGrid display configuration constants.
        /// </summary>
        public static class DataGrid
        {
            /// <summary>
            /// Minimum column width in pixels (100).
            /// </summary>
            public const int MinColumnWidth = 100;

            /// <summary>
            /// Maximum column width in pixels to prevent extremely wide columns (400).
            /// </summary>
            public const int MaxColumnWidth = 400;
        }

        /// <summary>
        /// Performance tuning constants for UI rendering.
        /// </summary>
        public static class Performance
        {
            /// <summary>
            /// Minimum interval between render calls in milliseconds (16ms ? 60 FPS).
            /// </summary>
            public const int MinRenderIntervalMs = 16; // ~60 FPS
        }
    }
}
