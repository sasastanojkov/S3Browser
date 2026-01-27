# S3 Browser

A powerful Windows desktop application for browsing and analyzing data stored in Amazon S3 buckets. Built with .NET 8 and WPF, S3 Browser provides an intuitive interface for navigating S3 storage and viewing various file formats directly without downloading.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=.net)
![WPF](https://img.shields.io/badge/WPF-Windows-0078D4?logo=windows)
![AWS S3](https://img.shields.io/badge/AWS-S3-FF9900?logo=amazon-aws)
![License](https://img.shields.io/badge/license-MIT-green)

## 🌟 What is S3 Browser?

S3 Browser is a **read-only desktop tool** that makes working with Amazon S3 storage simple and efficient. Whether you're a data analyst, developer, or GIS professional, S3 Browser helps you quickly access, view, and analyze data stored in the cloud without downloading files to your local machine.

Think of it as a specialized file explorer for AWS S3 - browse buckets and folders, preview files, query data, and visualize geographic information, all from one intuitive application.

## 📥 Installation

### Download Pre-built Release (Recommended)

1. Go to the [Releases](https://github.com/sasastanojkov/S3Browser/releases) page
2. Download the appropriate ZIP file for your architecture:
   - **S3Browser-vX.X.X-win-x64.zip** for 64-bit Intel/AMD systems (most common)
   - **S3Browser-vX.X.X-win-arm64.zip** for ARM64 systems
3. Extract the **entire contents** of the ZIP file to a folder of your choice
4. Run **S3Browser.exe**

**Important**: 
- **Extract ALL files** from the ZIP, not just the .exe
- The .exe needs the accompanying DLL files (like duckdb.dll) to run
- Keep all extracted files in the same folder
- No .NET installation required - the application is fully self-contained!

**Why multiple files?**: Native libraries (DuckDB, SkiaSharp) are kept separate for **fast startup** (2-5 seconds instead of 30-60 seconds). See [DEPLOYMENT-OPTIONS.md](DEPLOYMENT-OPTIONS.md) for details.

**Note**: When running for the first time, Windows SmartScreen may show a warning. See the [Troubleshooting](#️-troubleshooting) section for details.

### Build from Source

#### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (optional, for development)

#### Build Steps

```bash
# Clone the repository
git clone https://github.com/sasastanojkov/S3Browser.git
cd S3Browser

# Restore dependencies
dotnet restore

# Build for your architecture (win-x64 or win-arm64)
dotnet publish S3Browser/S3Browser.csproj -c Release -r win-x64 --self-contained true

# Output will be in: S3Browser/bin/Release/net8.0-windows/win-x64/publish/
```

## ✨ Key Capabilities

### 📂 Browse S3 Like a File Explorer
- Navigate S3 storage with familiar double-click and keyboard controls
- Jump to any location by typing an S3 path (like `s3://my-bucket/data/2024/`)
- Switch between AWS accounts using different profiles
- Access public buckets without any credentials
- Copy S3 paths with Ctrl+C for easy sharing
- Download entire folders recursively** - preserves folder hierarchy

### 📊 Powerful Parquet File Viewer
Parquet files are commonly used for storing large datasets efficiently. S3 Browser gives you advanced tools to work with them:

- View Parquet data instantly without downloading the files
- Query multiple files at once - perfect for partitioned datasets (folders with many .parquet files)
- Write SQL queries to filter, sort, and analyze your data using DuckDB
- Choose your data mode:
  - **Streaming Mode**: For very large datasets (memory efficient)
  - **Table Mode**: For datasets under 500MB (faster repeated queries)
- Control data volume: Load 10, 100, 1,000, 10,000, 100,000 rows, or everything
- Edit and re-run queries with a built-in SQL editor

### 🗺️ Visualize Geographic Data
Perfect for GIS professionals and anyone working with spatial data:

- See your geometry data on a map - automatically detects WKB (Well-Known Binary) geometries in Parquet files
- Display multiple geometries from different columns at the same time
- Click to highlight specific geometries for focused inspection
- Interactive map controls - zoom, pan, and explore your spatial data
- Supports all common geometry types: Points, Lines, Polygons, and Multi-geometries
- Row-to-map sync: Click any data row to instantly see its geometries on the map

### 📄 View Text and Tabular Files
Beyond Parquet, S3 Browser handles many common file formats:

**Text Files** (.txt, .json, .xml, .yaml, .log, .md, .sql, .js, .html, .css, .sh, .ps1, and more)
- View configuration files
- Inspect log files
- Review code files
- Check JSON/XML data

**CSV and TSV Files**
- Automatic delimiter detection
- Toggle header row on/off
- Smart cell expansion for long text
- Row limiting options

### 🎯 Smart Data Display Features
- **Truncated cells with expand buttons** - long text is shortened with "..." buttons to view full content
- **Copy complete cell data** with Ctrl+C, not just what's visible
- **Expand All view** - see all your data in a comprehensive window
- **Sortable columns** - click headers to sort
- **Resizable columns** - adjust widths to fit your data
- **File information** - see sizes, dates, and metadata

### ⚡ Built for Performance
- **Background operations** - all S3 operations run without freezing the UI
- **Cancel anytime** - stop long-running operations with one click
- **Progress indicators** - always know what's happening
- **Smart cleanup** - temporary files are automatically deleted
- **Clear error messages** - helpful suggestions when something goes wrong

## 🎮 Getting Started

### What You Need
- **Windows 10 or 11** (x64 or ARM64)
- **AWS credentials** (for private buckets) OR just an **internet connection** (for public buckets)

**Note**: If using the pre-built release, no .NET installation is required. If building from source, you need .NET 8 SDK.

### First Time Setup

**Option 1: Using with your AWS account**
1. Install AWS CLI - [Download here](https://aws.amazon.com/cli/)
2. Configure your credentials:
   ```
   aws configure sso
   ```
   OR
   ```
   aws configure --profile my-profile
   ```
3. Login if using SSO:
   ```
   aws sso login --profile my-profile
   ```

**Option 2: Accessing public buckets**
- No setup needed! Just select "Anonymous" mode when the app starts

### Launching the Application

1. **Run S3Browser.exe**
2. **Choose your access method**:
   - Enter your AWS profile name, OR
   - Check "Anonymous" for public buckets
3. **Start browsing!**

### ⏱️ Startup Time

**First launch**: Takes **10-15 seconds** while the application initializes all components.

**Why?**: The application uses compression to keep file size smaller (~100-150 MB). Windows must decompress components before launch. This is normal for compressed .NET applications.

**Important**: .NET does not provide a way to show a progress bar during decompression. This happens at the runtime level before any application code runs. Your cursor will show as "busy" - just wait and the application will appear.

**Alternative**: The application can be configured for 2-5 second startup at the cost of a larger file size (~200-250 MB) by disabling compression in the build configuration.

## 💡 How to Use

### Navigating Around

**Opening items:**
- Double-click buckets, folders, or files
- Or select an item and press Enter

**Going back:**
- Click the ".." folder at the top
- Or click the Home button to return to bucket list

**Jump to a specific location:**
- Type an S3 path in the navigation bar (e.g., `s3://my-bucket/data/`)
- Press Enter to go there

**Copy S3 paths:**
- Select any bucket, folder, or file
- Press Ctrl+C to copy its S3 path

### Opening Files

**Parquet Files** - Double-click opens the Parquet viewer
- Choose how many rows to load (dropdown at top)
- Click Reload to refresh with different settings
- If the file has geometry columns, click any row to see them on a map

**Folders with Parquet Files** - Click the "Read All Parquet Files" button
- Opens all .parquet files in the folder as one dataset
- Great for partitioned data (like data organized by date)
- Choose between streaming mode (large data) or table mode (faster queries, max 500MB)

**CSV or TSV Files** - Double-click opens the tabular viewer
- Toggle "File has header" if needed
- Choose how many rows to load
- Click Reload to refresh

**Text Files** - Double-click to view
- Select read limit for large files
- View JSON, XML, logs, code, and more

### Working with Geographic Data

When viewing Parquet files that contain geometry columns:

1. **Open a Parquet file** with geometry data (WKB format)
2. **Click any row** in the data grid
3. **A map opens automatically** showing the geometries from that row
4. **Click geometry buttons** in the sidebar to highlight specific ones
5. **Click the same row again** to close the map

The map supports: Points, Lines, Polygons, MultiPoints, MultiLineStrings, and MultiPolygons

### Writing Custom SQL Queries

For Parquet files and folders:

1. Click the **"Write Custom Query"** button
2. A SQL editor opens with a starter query
3. Modify the query (use standard SQL syntax)
4. Click **"Execute Query"** to see results
5. Results open in a new viewer window

**Example queries:**
```sql
-- Filter data
SELECT * FROM read_parquet('s3://my-bucket/data/*.parquet')
WHERE date >= '2024-01-01'
LIMIT 1000

-- Aggregate data
SELECT category, COUNT(*) as count, AVG(value) as avg_value
FROM read_parquet('s3://my-bucket/data/*.parquet')
GROUP BY category
ORDER BY count DESC
```

## 🔍 Supported File Types

| File Type | Extensions | Viewer Features |
|-----------|-----------|-----------------|
| **Parquet** | .parquet | SQL queries, streaming mode, table mode, geometry visualization |
| **CSV** | .csv | Header detection, cell expansion, row limiting |
| **TSV** | .tsv | Header detection, cell expansion, row limiting |
| **Text** | .txt, .log, .md | Large file support with configurable limits |
| **JSON** | .json | Formatted display |
| **XML** | .xml | Formatted display |
| **YAML** | .yaml, .yml | Formatted display |
| **Code** | .js, .ts, .py, .sql, .html, .css | Syntax-aware viewing |
| **Scripts** | .sh, .bat, .ps1 | Script viewing |
| **Config** | .config, .ini, .properties | Configuration file viewing |

## 🎯 Common Use Cases

### For Data Analysts
- Preview large Parquet datasets before downloading
- Run quick SQL queries to check data quality
- Filter and aggregate data without copying it locally
- Verify data structure and content

### For GIS Professionals
- Preview spatial datasets stored in S3
- Visualize geometry data on interactive maps
- Check coordinate systems and data extent
- Validate geometry data from processing pipelines

### For Developers
- Inspect log files directly from S3
- Review JSON/XML configuration files
- Debug data pipeline outputs
- Check intermediate processing results

### For DevOps Engineers
- Browse and organize S3 storage
- Monitor file sizes and modification dates
- Work across multiple AWS accounts
- Quick access to stored artifacts and logs

## 💡 Tips & Best Practices

### Performance Tips
- **Start small**: Use 10 or 100 row limits for initial previews
- **Use Table Mode**: For datasets under 500MB when you need multiple queries
- **Close unused windows**: Free up memory by closing viewers you're not using
- **Filter early**: In custom queries, use WHERE clauses to limit data before loading

### Navigation Tips
- **Bookmark paths**: Save frequently used S3 paths in a text file
- **Use the path bar**: Type paths directly instead of clicking through folders
- **Ctrl+C everywhere**: Works on any selected item to copy its S3 path

### Query Writing Tips
- **Test with LIMIT**: Add `LIMIT 100` to queries while testing
- **Use wildcards wisely**: `s3://bucket/path/*.parquet` reads all files in a folder
- **Standard SQL works**: DuckDB supports most common SQL features
- **Filter before aggregating**: Use WHERE before GROUP BY for better performance

### Geometry Tips
- **Check coordinate system**: Geometries should be in WGS84 (EPSG:4326) for best results
- **Toggle selection**: Click a row once to open map, click again to close
- **Use highlight buttons**: Click geometry buttons to focus on specific columns
- **Zoom controls**: Mouse wheel zooms, click-and-drag pans the map

## ⚠️ Important Things to Know

- **Read-Only Application**: S3 Browser never modifies or deletes your S3 data
- **No Full Downloads**: Files are streamed directly (except CSV/TSV which download temporarily)
- **Temporary Files**: CSV/TSV files are downloaded temporarily and auto-deleted when you close the viewer
- **Memory Usage**: Table mode loads data into RAM - large datasets will use system memory
- **Internet Required**: Map tiles need internet connection to display

## 🛠️ Troubleshooting

### Application takes 10-15 seconds to start
**This is expected behavior** with the current configuration.

The application uses compression to reduce file size (~100-150 MB). The 10-15 seconds is Windows decompressing internal components at the .NET Runtime level. No progress bar is possible during this phase.

**This is normal and safe** - just wait and the application will appear.

### Windows SmartScreen / "Windows protected your PC"
**This is normal for unsigned applications.**

When running for the first time, Windows SmartScreen may show a warning:

**"Windows protected your PC" message:**
1. Click **"More info"** on the warning dialog
2. Click **"Run anyway"** button

**Why this happens:**
- Code signing certificates cost $100-300/year
- This is a free, open-source application
- The code is publicly available for review on GitHub
- Once enough people download it, Windows will stop showing the warning

**Is it safe?**
- ✅ Yes! The application is open source - you can review all code
- ✅ No telemetry, no data collection, no network calls except to AWS S3
- ✅ Read-only access to S3 - never modifies or deletes your data

### Application doesn't start / crashes immediately
**Solutions:**

1. **Check for error messages**: The app now shows detailed error dialogs. Read the error message carefully.

2. **Run from command line to see errors**:
   ```cmd
   # Navigate to the folder containing S3Browser.exe
   cd C:\path\to\S3Browser
   
   # Run from command line
   S3Browser.exe
   ```
   Any errors will be displayed in the console.

3. **Check Windows Event Viewer**:
   - Press Win+X, select "Event Viewer"
   - Navigate to: Windows Logs → Application
   - Look for recent errors from S3Browser

4. **Missing Visual C++ Redistributables**:
   - Download and install: [VC++ Redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe)

5. **Antivirus blocking**: Temporarily disable antivirus or add S3Browser.exe to exclusions

6. **Extract to a simple path**: Avoid paths with special characters or very long paths
   - ✅ Good: `C:\Apps\S3Browser\`
   - ❌ Bad: `C:\Users\YourName\Documents\My Downloads (Old)\S3 Browser v1.0\`

7. **Build from source**: If the published version doesn't work, try building from source:
   ```bash
   git clone https://github.com/sasastanojkov/S3Browser.git
   cd S3Browser
   dotnet run --project S3Browser/S3Browser.csproj
   ```

### "Could not load AWS credentials"
**Solution:**
1. Make sure AWS CLI is installed
2. Run: `aws configure` or `aws sso login --profile your-profile`
3. Verify with: `aws sts get-caller-identity --profile your-profile`

### "Error loading parquet file"
**Solutions:**
- Verify the file is valid Parquet format
- Check you have S3 read permissions (s3:GetObject)
- Try reducing the row limit
- Use streaming mode instead of table mode

### Map not showing anything
**Solutions:**
- Check geometry data is in WKT format
- Verify coordinates are in WGS84 (latitude/longitude range: -180 to 180, -90 to 90)
- Make sure you have internet connection for map tiles
- Try closing and reopening the map window

### Application is slow
**Solutions:**
- Use row limits instead of loading all data
- Close viewer windows you're not using
- Use streaming mode for very large datasets
- Check your internet connection speed

## 📞 Getting Help

- **Check this README** for answers to common questions
- **Review Troubleshooting** section above
- **Open an issue** on [GitHub](https://github.com/sasastanojkov/S3Browser/issues)
- **Check existing issues** - someone may have already solved your problem

## 🤝 Contributing

Want to help make S3 Browser better? Contributions are welcome!

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Make your changes
4. Commit with clear messages: `git commit -m 'Add awesome feature'`
5. Push to your fork: `git push origin feature/my-feature`
6. Open a Pull Request

## 🚀 Creating Releases

For maintainers: To create a new release:

1. **Update version** and commit your changes
2. **Create and push a version tag**:
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```
3. **GitHub Actions automatically**:
   - Builds the application for win-x64 and win-arm64
   - Creates single-file executables
   - Packages them as ZIP files with README
   - Creates a GitHub Release with downloadable assets

The workflow creates releases for both x64 and ARM64 architectures automatically.

## 📜 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 🙏 Built With

S3 Browser uses these excellent open-source libraries:

- **[DuckDB](https://duckdb.org/)** - Lightning-fast analytical query engine for Parquet files
- **[Mapsui](https://mapsui.com/)** - Cross-platform map rendering library
- **[NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite)** - Geometry processing and spatial operations
- **[AWS SDK for .NET](https://aws.amazon.com/sdk-for-net/)** - Official AWS integration
- **[OpenStreetMap](https://www.openstreetmap.org/)** - Community-driven map tile provider

## 📊 Technology Stack

- **.NET 8** - Modern, cross-platform framework
- **WPF** - Rich desktop UI framework
- **DuckDB** - Embedded analytical database
- **AWS SDK** - S3 integration
- **Mapsui + SkiaSharp** - Map rendering
- **NetTopologySuite** - Geometry operations

---

**Built with ❤️ for the data community**

For more information, visit the [GitHub repository](https://github.com/sasastanojkov/S3Browser)
