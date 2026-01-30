using System.Data;
using System.IO;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace S3Browser.Helpers
{
    /// <summary>
    /// Helper class for handling geometry data (WKT/WKB) in viewer windows.
    /// Provides methods to detect, parse, and convert geometry data for map visualization.
    /// </summary>
    public static class GeometryHelper
    {
        /// <summary>
        /// Checks if a string value contains Well-Known Text (WKT) geometry data.
        /// </summary>
        /// <param name="value">The string value to check.</param>
        /// <returns>True if the value starts with a known geometry type; false otherwise.</returns>
        public static bool IsWktGeometry(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.StartsWith("POINT", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("LINESTRING", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("POLYGON", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("MULTIPOINT", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("MULTILINESTRING", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("MULTIPOLYGON", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("GEOMETRYCOLLECTION", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Attempts to parse Well-Known Binary (WKB) data into a NetTopologySuite Geometry.
        /// </summary>
        /// <param name="wkb">The WKB byte array to parse.</param>
        /// <param name="geometry">The parsed geometry if successful; null otherwise.</param>
        /// <returns>True if parsing succeeded; false otherwise.</returns>
        public static bool TryParseWkbGeometry(byte[]? wkb, out Geometry? geometry)
        {
            geometry = null;

            if (wkb == null || wkb.Length < 5)
                return false;

            try
            {
                var reader = new WKBReader();
                geometry = reader.Read(wkb);
                return geometry is not null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Attempts to parse a Well-Known Text (WKT) string into a NetTopologySuite Geometry.
        /// </summary>
        /// <param name="wkt">The WKT string to parse.</param>
        /// <param name="geometry">The parsed geometry if successful; null otherwise.</param>
        /// <returns>True if parsing succeeded; false otherwise.</returns>
        public static bool TryParseWktGeometry(string? wkt, out Geometry? geometry)
        {
            geometry = null;

            if (string.IsNullOrWhiteSpace(wkt))
                return false;

            try
            {
                var reader = new WKTReader();
                geometry = reader.Read(wkt);
                return geometry is not null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Converts various geometry representations (WKB bytes, streams, WKT strings) to WKT format.
        /// </summary>
        /// <param name="value">The value to convert. Can be byte[], Stream, UnmanagedMemoryStream, or string.</param>
        /// <returns>WKT string if conversion successful, hex string for binary data that isn't valid geometry, error message on failure.</returns>
        public static string ConvertToWkt(object? value)
        {
            if (value == null || value == DBNull.Value)
                return "null";

            try
            {
                byte[]? wkb = null;

                // Try to extract WKB bytes from various sources
                if (value is byte[] byteArray)
                {
                    wkb = byteArray;
                }
                else if (value is UnmanagedMemoryStream unmanagedStream)
                {
                    wkb = ReadStreamToBytes(unmanagedStream);
                }
                else if (value is Stream streamBase)
                {
                    using (var ms = new MemoryStream())
                    {
                        streamBase.CopyTo(ms);
                        wkb = ms.ToArray();
                    }
                }
                else if (value is string stringValue)
                {
                    // If it's already a WKT string, validate and return it
                    if (IsWktGeometry(stringValue) && TryParseWktGeometry(stringValue, out var _))
                    {
                        return stringValue;
                    }
                    return value.ToString() ?? "";
                }

                // Try to parse WKB and convert to WKT
                if (wkb != null && wkb.Length > 0)
                {
                    if (TryParseWkbGeometry(wkb, out var geometry) && geometry is not null)
                    {
                        var writer = new WKTWriter();
                        return writer.Write(geometry);
                    }
                    else
                    {
                        // Not a valid geometry, convert to hex string for debugging
                        return "0x" + BitConverter.ToString(wkb).Replace("-", "");
                    }
                }

                return value.ToString() ?? "";
            }
            catch (Exception ex)
            {
                // If conversion fails, show error or hex if binary data
                if (value is byte[] bytes)
                {
                    return "0x" + BitConverter.ToString(bytes).Replace("-", "");
                }
                return $"[Error: {ex.Message}]";
            }
        }

        /// <summary>
        /// Scans a DataTable to find all columns containing WKT geometry data.
        /// </summary>
        /// <param name="dataTable">The DataTable to scan.</param>
        /// <returns>A dictionary mapping column names to a boolean indicating if they contain geometries.</returns>
        public static Dictionary<string, bool> DetectGeometryColumns(DataTable dataTable)
        {
            var geometryColumns = new Dictionary<string, bool>();

            foreach (DataColumn column in dataTable.Columns)
            {
                bool hasGeometry = false;

                // Check a sample of rows to see if this column contains WKT geometries
                int rowsToCheck = Math.Min(10, dataTable.Rows.Count);
                for (int i = 0; i < rowsToCheck; i++)
                {
                    var value = dataTable.Rows[i][column];
                    if (value != null && value != DBNull.Value)
                    {
                        string? strValue = value.ToString();
                        if (IsWktGeometry(strValue))
                        {
                            hasGeometry = true;
                            break;
                        }
                    }
                }

                if (hasGeometry)
                {
                    geometryColumns[column.ColumnName] = true;
                }
            }

            return geometryColumns;
        }

        /// <summary>
        /// Extracts all geometries from a DataRow, checking all columns for WKT data.
        /// </summary>
        /// <param name="row">The DataRow to extract geometries from.</param>
        /// <param name="dataTable">The DataTable the row belongs to (for column information).</param>
        /// <returns>A list of GeometryInfo objects containing WKT and column names.</returns>
        public static List<GeometryMapWindow.GeometryInfo> ExtractGeometriesFromRow(DataRow row, DataTable dataTable)
        {
            var geometries = new List<GeometryMapWindow.GeometryInfo>();

            foreach (DataColumn column in dataTable.Columns)
            {
                var value = row[column];
                if (value != null && value != DBNull.Value)
                {
                    string? strValue = value.ToString();
                    if (IsWktGeometry(strValue))
                    {
                        // Validate that it's actually valid WKT
                        if (TryParseWktGeometry(strValue, out var _))
                        {
                            geometries.Add(new GeometryMapWindow.GeometryInfo
                            {
                                Wkt = strValue!,
                                ColumnName = column.ColumnName
                            });
                        }
                    }
                }
            }

            return geometries;
        }

        /// <summary>
        /// Reads all bytes from an UnmanagedMemoryStream.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <returns>Byte array containing all data from the stream.</returns>
        private static byte[] ReadStreamToBytes(UnmanagedMemoryStream stream)
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            byte[] buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);

            return buffer;
        }
    }
}
