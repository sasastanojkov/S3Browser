# S3Browser Deployment Options

This document explains the different deployment configurations and their trade-offs.

## Current Configuration (Recommended)

**Setting**: `IncludeNativeLibrariesForSelfExtract=false`

### What You Get:
```
📦 S3Browser-v1.0.0-win-x64.zip
├── S3Browser.exe          (~80-100 MB)
├── duckdb.dll            (~15-20 MB)
├── libSkiaSharp.dll      (~5-10 MB)
├── appsettings.json      (~1 KB)
└── README.txt            (~2 KB)
```

### Pros:
✅ **Fast startup** - 2-5 seconds
✅ **Instant subsequent runs** - No extraction needed
✅ **Still self-contained** - No .NET installation required
✅ **Smaller main .exe** - Easier to scan/verify

### Cons:
❌ **Multiple files** - Not truly "single file"
❌ **Users must extract all files** - Can't just copy the .exe
❌ **~10-15 files total** - Looks less clean

### Best For:
- Most users who value fast startup
- Professional use where startup time matters
- Users who will run the app frequently

---

## Alternative: True Single File

**Setting**: `IncludeNativeLibrariesForSelfExtract=true`

### What You Get:
```
📦 S3Browser-v1.0.0-win-x64.zip
├── S3Browser.exe          (~150-200 MB)
├── appsettings.json      (~1 KB)
└── README.txt            (~2 KB)
```

### Pros:
✅ **True single .exe** - Only need to copy one file (plus config)
✅ **Portable** - Easy to move around
✅ **Cleaner** - Looks more professional with just one .exe

### Cons:
❌ **Slow first startup** - 30-60 seconds on first run
❌ **Extracts to temp** - Creates files in `%TEMP%\.net\S3Browser\`
❌ **Larger file size** - ~150-200MB .exe
❌ **May trigger antivirus** - Extracting DLLs from .exe looks suspicious
❌ **Slower subsequent runs** - 5-10 seconds (re-extracts on some updates)

### Best For:
- Distribution where portability is critical
- USB stick deployment
- Users who rarely run the app
- Preference for "clean" deployment (one .exe)

---

## Alternative: Framework-Dependent

**Setting**: `SelfContained=false`

### What You Get:
```
📦 S3Browser-v1.0.0-win-x64.zip
├── S3Browser.exe          (~2-5 MB)
├── S3Browser.dll          (~1-2 MB)
├── duckdb.dll            (~15-20 MB)
├── Other DLLs            (~20-30 MB total)
├── appsettings.json      (~1 KB)
└── README.txt            (~2 KB)
```

### Pros:
✅ **Fastest startup** - Instant (< 1 second)
✅ **Smallest total size** - ~40-50 MB
✅ **Shares .NET runtime** - Uses system .NET installation

### Cons:
❌ **Requires .NET 8** - Users must install .NET 8 Runtime
❌ **Most files** - ~30-40 DLLs
❌ **Dependency** - Won't work if .NET 8 not installed

### Best For:
- Internal/corporate deployment where .NET is already installed
- Development/testing
- Users comfortable installing prerequisites

---

## Comparison Table

| Feature | Current (Native DLLs Separate) | True Single File | Framework-Dependent |
|---------|-------------------------------|------------------|---------------------|
| **Startup Time** | 2-5 seconds ⚡ | 30-60 seconds 🐌 | < 1 second ⚡⚡ |
| **File Count** | ~10-15 files | 2-3 files | ~30-40 files |
| **Total Size** | ~100-120 MB | ~150-200 MB | ~40-50 MB |
| **.NET Required** | ❌ No | ❌ No | ✅ Yes |
| **Portability** | Good | Excellent | Poor |
| **User Experience** | Best | Slowest start | Best (if .NET installed) |

---

## Recommendation

**For public GitHub releases**: Use **Current Configuration** (native DLLs separate)

**Why**:
- Fast startup is critical for user experience
- Most users will extract the full ZIP anyway
- Antivirus is less likely to flag it
- Much better first impression (app opens quickly)

**Trade-off**: Users must extract all files, but this is standard for most Windows apps.

---

## How to Switch Configurations

Edit `S3Browser/S3Browser.csproj`:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <PublishSingleFile>true</PublishSingleFile>
  
  <!-- Self-contained (no .NET required) vs Framework-dependent -->
  <SelfContained>true</SelfContained>  <!-- Change to false for framework-dependent -->
  
  <!-- Native libraries bundled vs separate -->
  <IncludeNativeLibrariesForSelfExtract>false</IncludeNativeLibrariesForSelfExtract>
  <!-- Change to true for single file (slower startup) -->
  
  <PublishTrimmed>false</PublishTrimmed>
  <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
</PropertyGroup>
```

Don't forget to update `.github/workflows/release.yml` with the same settings!

---

## Current Configuration Details

With `IncludeNativeLibrariesForSelfExtract=false`, the published output includes:

**Main executable**:
- S3Browser.exe (~80-100 MB) - Managed code + .NET runtime

**Native libraries** (kept separate for performance):
- duckdb.dll - DuckDB database engine
- libSkiaSharp.dll - Graphics rendering
- Other native dependencies

**Configuration**:
- appsettings.json - Application settings

**Documentation**:
- README.txt - User instructions

**Total**: ~100-120 MB when extracted, starts in 2-5 seconds ⚡
