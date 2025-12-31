using System.IO;
using S3Browser.Constants;

namespace S3Browser.Services
{
    /// <summary>
    /// Service for file type detection and classification based on file extensions.
    /// Provides methods to identify Parquet, CSV, TSV, text files, and WKT geometry data.
    /// </summary>
    public sealed class FileTypeService
    {
        /// <summary>
        /// Checks if the file is a Parquet file based on its extension (.parquet).
        /// </summary>
        /// <param name="fileName">The file name to check.</param>
        /// <returns>True if the file has a .parquet extension; otherwise, false.</returns>
        public bool IsParquetFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension == AppConstants.FileExtensions.Parquet;
        }

        /// <summary>
        /// Checks if the file is a CSV file based on its extension (.csv).
        /// </summary>
        /// <param name="fileName">The file name to check.</param>
        /// <returns>True if the file has a .csv extension; otherwise, false.</returns>
        public bool IsCsvFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension == AppConstants.FileExtensions.Csv;
        }

        /// <summary>
        /// Checks if the file is a TSV (Tab-Separated Values) file based on its extension (.tsv).
        /// </summary>
        /// <param name="fileName">The file name to check.</param>
        /// <returns>True if the file has a .tsv extension; otherwise, false.</returns>
        public bool IsTsvFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension == AppConstants.FileExtensions.Tsv;
        }

        /// <summary>
        /// Checks if the file is a text file based on its extension.
        /// Supports extensions: .txt, .json, .xml, .log, .md, .yaml, .yml, .config, .ini,
        /// .properties, .html, .htm, .css, .js, .ts, .sql, .sh, .bat, .ps1
        /// </summary>
        /// <param name="fileName">The file name to check.</param>
        /// <returns>True if the file has a recognized text file extension; otherwise, false.</returns>
        public bool IsTextFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            return AppConstants.FileExtensions.TextFiles.Contains(extension);
        }

        /// <summary>
        /// Checks if the text appears to be WKT (Well-Known Text) geometry data.
        /// Identifies common geometry types: POINT, LINESTRING, POLYGON, MULTIPOINT, MULTILINESTRING, MULTIPOLYGON.
        /// </summary>
        /// <param name="text">The text to analyze.</param>
        /// <returns>True if the text starts with a recognized WKT geometry prefix; otherwise, false.</returns>
        public bool IsGeometryWkt(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return AppConstants.FileExtensions.GeometryWktPrefixes.Any(prefix =>
                text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets a human-readable file type description based on the file extension.
        /// </summary>
        /// <param name="fileName">The file name to analyze.</param>
        /// <returns>
        /// A descriptive string: "Parquet File", "CSV File", "TSV File", "Text File", or "Unknown File Type".
        /// </returns>
        public string GetFileTypeDescription(string fileName)
        {
            if (IsParquetFile(fileName))
            {
                return "Parquet File";
            }

            if (IsCsvFile(fileName))
            {
                return "CSV File";
            }

            if (IsTsvFile(fileName))
            {
                return "TSV File";
            }

            if (IsTextFile(fileName))
            {
                return "Text File";
            }

            return "Unknown File Type";
        }

        /// <summary>
        /// Determines the appropriate file viewer type for a given file based on its extension.
        /// </summary>
        /// <param name="fileName">The file name to analyze.</param>
        /// <returns>A FileViewerType enum value indicating which viewer should be used to open the file.</returns>
        public FileViewerType GetViewerType(string fileName)
        {
            if (IsParquetFile(fileName))
            {
                return FileViewerType.Parquet;
            }

            if (IsCsvFile(fileName))
            {
                return FileViewerType.Csv;
            }

            if (IsTsvFile(fileName))
            {
                return FileViewerType.Tsv;
            }

            if (IsTextFile(fileName))
            {
                return FileViewerType.Text;
            }

            return FileViewerType.None;
        }
    }

    /// <summary>
    /// Enum representing different file viewer types available in the application.
    /// </summary>
    public enum FileViewerType
    {
        /// <summary>
        /// No specific viewer type (unsupported file).
        /// </summary>
        None,

        /// <summary>
        /// Parquet file viewer for .parquet files.
        /// </summary>
        Parquet,

        /// <summary>
        /// CSV file viewer for comma-separated value files.
        /// </summary>
        Csv,

        /// <summary>
        /// TSV file viewer for tab-separated value files.
        /// </summary>
        Tsv,

        /// <summary>
        /// Text file viewer for various text-based file formats.
        /// </summary>
        Text
    }
}
