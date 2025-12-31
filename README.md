# S3Browser

A modern WPF desktop application for browsing and viewing AWS S3 bucket contents with advanced support for Parquet files, tabular data (CSV/TSV), text files, and geometry visualization.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=.net)
![WPF](https://img.shields.io/badge/WPF-Windows-0078D4?logo=windows)
![AWS S3](https://img.shields.io/badge/AWS-S3-FF9900?logo=amazon-aws)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

### ??? S3 Navigation
- Browse AWS S3 buckets, folders, and files with an intuitive interface
- Navigate using mouse double-click or keyboard (Enter key)
- Direct S3 path navigation (e.g., `s3://bucket-name/path/to/folder`)
- Breadcrumb navigation showing current location
- Back navigation using ".." folder entries

### ?? Parquet File Support
- View Parquet files directly from S3 using DuckDB engine
- Query single Parquet files or entire folders with wildcard patterns
- Configurable row limits (10, 100, 1K, 10K, 100K, or all rows)
- Automatic geometry detection and visualization
- Export geometries to GeoJSON format
- Interactive map display for spatial data

### ?? File Viewers
- **Text Files**: View `.txt`, `.json`, `.xml`, `.log`, `.md`, `.yaml`, `.sql`, and more
- **CSV/TSV Files**: Tabular data viewer with header detection
- **Parquet Files**: Advanced viewer with DuckDB query engine
- All viewers support configurable read limits for large files

### ??? Geometry Visualization
- Automatic detection of WKT (Well-Known Text) geometry columns
- Interactive map using OpenStreetMap tiles
- Support for Point, LineString, Polygon, MultiPoint, MultiLineString, MultiPolygon
- Color-coded geometry display with selection highlighting
- Pan and zoom controls
- Multiple geometry layer support per row

### ? Performance Features
- Lazy loading with configurable row limits
- Concurrent DuckDB connections for parallel queries
- Efficient S3 streaming for large files
- Optimized map rendering (~60 FPS)
- Geometry data caching

## Requirements

### System Requirements
- **Operating System**: Windows 10/11 (64-bit)
- **.NET Runtime**: .NET 8.0 Desktop Runtime
- **Memory**: 4GB RAM minimum (8GB recommended for large Parquet files)
- **Storage**: 100MB free disk space

### AWS Requirements
- AWS account with S3 access
- AWS CLI configured with SSO or IAM credentials
- Appropriate IAM permissions for S3 bucket access:
  - `s3:ListBucket`
  - `s3:GetObject`
  - `s3:GetObjectMetadata`

## Installation

### Prerequisites

1. **Install .NET 8.0 Desktop Runtime**
   ```bash
   # Download from Microsoft
   https://dotnet.microsoft.com/download/dotnet/8.0
   ```

2. **Install AWS CLI** (if using SSO)
   ```bash
   # Download from AWS
   https://aws.amazon.com/cli/
   ```

3. **Configure AWS Profile**
   ```bash
   # For SSO
   aws configure sso
   
   # For IAM credentials
   aws configure --profile your-profile-name
   ```

### Building from Source

1. **Clone the repository**
   ```bash
   git clone https://github.com/yourusername/S3Browser.git
   cd S3Browser
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore
   ```

3. **Build the application**
   ```bash
   dotnet build --configuration Release
   ```

4. **Run the application**
   ```bash
   dotnet run --project S3Browser
   ```

   Or navigate to the output directory:
   ```bash
   cd S3Browser\bin\Release\net8.0-windows
   S3Browser.exe
   ```

## Configuration

### Application Settings

Edit `appsettings.json` to configure default settings:

```json
{
  "AppSettings": {
    "DefaultAwsProfile": "your-profile-name"
  }
}
```

**Configuration Options:**
- `DefaultAwsProfile`: The AWS CLI profile name to use by default (pre-fills the profile selection dialog)

### AWS Profile Setup

#### For SSO Users:
```bash
# Configure SSO profile
aws configure sso

# Login before starting the application
aws sso login --profile your-profile-name
```

#### For IAM Users:
```bash
# Configure with access keys
aws configure --profile your-profile-name
# Enter: AWS Access Key ID
# Enter: AWS Secret Access Key
# Enter: Default region name
# Enter: Default output format
```

## Usage

### Starting the Application

1. **Ensure AWS credentials are active**
   ```bash
   # For SSO users
   aws sso login --profile your-profile-name
   ```

2. **Launch S3Browser.exe**

3. **Select AWS Profile**
   - Enter your AWS profile name in the dialog
   - Click OK to connect

### Navigation

#### Mouse Navigation:
- **Double-click** a bucket to open it
- **Double-click** a folder to navigate into it
- **Double-click** a file to open the appropriate viewer
- **Double-click** ".." to go back

#### Keyboard Navigation:
- Use **Arrow Keys** to select items
- Press **Enter** to open the selected item
- Type an S3 path in the path bar and press **Enter**

#### Path Navigation:
```
s3://my-bucket
s3://my-bucket/my-folder/
s3://my-bucket/path/to/file.parquet
s3a://my-bucket/path/  (alternate format)
```

### Viewing Files

#### Parquet Files
1. Double-click or press Enter on a `.parquet` file
2. Select row limit from dropdown (10, 100, 1K, 10K, 100K, All)
3. Click "Reload" to refresh with new limit
4. If geometries detected:
   - Select a row to view geometries on map
   - Click geometry buttons to highlight specific geometries
   - Use "Export Geometries to GeoJSON" to save spatial data

#### Parquet Folders (Wildcard Query)
1. Navigate to a folder containing only `.parquet` files
2. Click the **"?? Read All Parquet Files in Folder"** button
3. DuckDB will query all Parquet files using a wildcard pattern
4. Perfect for partitioned Parquet datasets

#### CSV/TSV Files
1. Double-click a `.csv` or `.tsv` file
2. Toggle "File has header" checkbox if needed
3. Select row limit
4. Click "Reload" to refresh

#### Text Files
1. Double-click any supported text file
2. Select read limit (1KB, 10KB, 100KB, 1MB, Full)
3. Click "Load" to refresh

### Working with Geometries

The application automatically detects WKT geometry columns in Parquet files:

**Supported Geometry Types:**
- POINT
- LINESTRING
- POLYGON
- MULTIPOINT
- MULTILINESTRING
- MULTIPOLYGON

**Map Controls:**
- **Mouse Drag**: Pan the map
- **Mouse Wheel**: Zoom in/out
- **Geometry Buttons**: Click to highlight specific geometries
- **Row Selection**: Select different rows to load new geometries

**Export Options:**
- Click "Export Geometries to GeoJSON"
- Choose save location
- Open in QGIS, ArcGIS, or any GIS application

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| **Enter** | Open selected item (bucket/folder/file) |
| **Arrow Keys** | Navigate through items |
| **Ctrl+V** | Paste S3 path (in path textbox) |
| **Escape** | Close dialog windows |

## Supported File Types

### Text Files
`.txt`, `.json`, `.xml`, `.log`, `.md`, `.yaml`, `.yml`, `.config`, `.ini`, `.properties`, `.html`, `.htm`, `.css`, `.js`, `.ts`, `.sql`, `.sh`, `.bat`, `.ps1`

### Tabular Files
- `.csv` - Comma-Separated Values
- `.tsv` - Tab-Separated Values

### Binary Files
- `.parquet` - Apache Parquet columnar format

## Architecture

### Technology Stack
- **UI Framework**: WPF (.NET 8)
- **S3 Access**: AWS SDK for .NET
- **Parquet Engine**: DuckDB with httpfs extension
- **Map Rendering**: Mapsui 5.0 with SkiaSharp
- **Geometry Processing**: NetTopologySuite
- **Configuration**: Microsoft.Extensions.Configuration

### Project Structure
```
S3Browser/
??? MainWindow.xaml.cs          # Main browser window
??? ParquetViewerWindow.xaml.cs # Parquet file viewer
??? TabularFileViewerWindow.xaml.cs # CSV/TSV viewer
??? FileViewerWindow.xaml.cs    # Text file viewer
??? GeometryMapWindow.xaml.cs   # Interactive map
??? ProfileSelectionDialog.xaml.cs # AWS profile selector
??? Services/
?   ??? AwsCredentialService.cs # AWS authentication
?   ??? FileTypeService.cs      # File type detection
??? Helpers/
?   ??? FileHelper.cs           # File utilities
?   ??? DataGridTemplateHelper.cs # UI templates
??? Converters/
?   ??? TextTruncateConverter.cs # Data binding converters
??? Constants/
?   ??? AppConstants.cs         # Application constants
??? appsettings.json            # Configuration file
```

## Troubleshooting

### AWS Authentication Errors

**Problem**: "Could not load AWS credentials"

**Solutions:**
1. Ensure AWS CLI is installed
2. Run `aws sso login --profile your-profile-name`
3. Verify profile exists: `aws configure list-profiles`
4. Check credentials: `aws sts get-caller-identity --profile your-profile-name`

### Parquet Query Errors

**Problem**: "Error loading parquet file"

**Solutions:**
1. Check file is valid Parquet format
2. Verify S3 permissions (s3:GetObject)
3. Try reducing row limit
4. Check DuckDB logs in debug output

### Map Display Issues

**Problem**: Geometries not visible or map blank

**Solutions:**
1. Verify geometry data is valid WKT format
2. Check geometry coordinates are in WGS84 (latitude/longitude)
3. Ensure internet connection for map tiles
4. Try zooming out or clicking "Zoom to Extent"

### Performance Issues

**Problem**: Application slow with large files

**Solutions:**
1. Use row limits instead of loading all rows
2. Close unused viewer windows
3. Increase system memory allocation
4. Use wildcard queries for partitioned Parquet datasets

## Dependencies

The application uses the following NuGet packages:

| Package | Version | Purpose |
|---------|---------|---------|
| AWSSDK.S3 | 4.0.16 | AWS S3 access |
| AWSSDK.SSO | 4.0.2.8 | AWS SSO support |
| AWSSDK.SSOOIDC | 4.0.3.9 | AWS SSO OIDC |
| DuckDB.NET.Data.Full | 1.1.3 | Parquet query engine |
| NetTopologySuite | 2.5.0 | Geometry processing |
| NetTopologySuite.IO.GeoJSON | 4.0.0 | GeoJSON export |
| Mapsui | 5.0.0-beta.3 | Map rendering |
| Mapsui.Nts | 5.0.0-beta.3 | NTS integration |
| Mapsui.Rendering.Skia | 5.0.0-beta.3 | Skia renderer |
| Mapsui.Tiling | 5.0.0-beta.3 | Tile layer support |
| SkiaSharp.Views.WPF | 2.88.8 | WPF rendering |
| Microsoft.Extensions.Configuration | 8.0.0 | Configuration |
| Microsoft.Extensions.Configuration.Json | 8.0.0 | JSON config |

## Contributing

Contributions are welcome! Please follow these guidelines:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Code Style
- Follow C# naming conventions (PascalCase for public members)
- Add XML documentation for public APIs
- Include unit tests for new features
- Ensure all builds pass before submitting PR

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgments

- **DuckDB** - Fast analytical query engine
- **Mapsui** - Cross-platform map component
- **NetTopologySuite** - Geometry processing library
- **AWS SDK** - Amazon Web Services integration
- **OpenStreetMap** - Map tile provider

## Support

For issues, questions, or feature requests:
- Open an issue on GitHub
- Check existing documentation
- Review troubleshooting section

## Version History

### v1.0.0 (Current)
- Initial release
- S3 bucket browsing
- Parquet file viewer with DuckDB
- CSV/TSV viewer
- Text file viewer
- Geometry map visualization
- GeoJSON export
- AWS SSO support

## Roadmap

### Planned Features
- [ ] File upload to S3
- [ ] Folder download
- [ ] S3 object metadata editor
- [ ] SQL query builder for Parquet files
- [ ] Multiple map projections
- [ ] Bookmark favorite S3 locations
- [ ] Search functionality
- [ ] File preview thumbnails
- [ ] Dark mode theme
- [ ] Export to multiple formats (CSV, Excel, Shapefile)

---

**Built with ?? using .NET 8 and WPF**
