# Local Release Build Script for S3Browser
# This script simulates what GitHub Actions does when creating a release

param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64"
)

Write-Host "Building S3Browser v$Version for $Runtime..." -ForegroundColor Cyan

# Clean previous builds
if (Test-Path "publish") {
    Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
    Remove-Item -Path "publish" -Recurse -Force
}

# Restore dependencies
Write-Host "Restoring dependencies..." -ForegroundColor Yellow
dotnet restore S3Browser/S3Browser.csproj

# Build and publish
Write-Host "Publishing application..." -ForegroundColor Yellow
dotnet publish S3Browser/S3Browser.csproj `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:Version=$Version `
    -p:AssemblyVersion=$Version `
    -p:FileVersion=$Version `
    -o "publish/$Runtime"

if ($LASTEXITCODE -eq 0) {
    # Create README for the release
    Write-Host "Creating release README..." -ForegroundColor Yellow
    
    $readmeContent = @"
# S3Browser v$Version

## Overview
S3Browser is a Windows desktop application for browsing and querying AWS S3 buckets.

## Features
- Browse S3 buckets, folders, and files
- View Parquet files with DuckDB SQL engine
- View CSV/TSV files
- View text files
- Visualize geospatial data from WKB/WKT geometry columns
- Write custom SQL queries against Parquet files
- Download files and folders
- Support for both authenticated and anonymous (public bucket) access

## System Requirements
- Windows 10 or later ($Runtime)
- No .NET installation required (self-contained)

## Installation
1. Extract all files to a folder of your choice
2. Run **S3Browser.exe**

## AWS Configuration
For authenticated access to your AWS S3 buckets:
1. Install AWS CLI: https://aws.amazon.com/cli/
2. Configure your AWS profile: ``aws configure sso`` or ``aws configure``
3. For SSO profiles, login before using: ``aws sso login --profile your-profile``

For public/anonymous bucket access, no AWS configuration is needed.

## Usage
- **Browse buckets**: Select your AWS profile or use anonymous mode
- **Navigate**: Double-click folders/files or press Enter
- **Copy S3 paths**: Select an item and press Ctrl+C
- **View Parquet files**: Double-click .parquet files or use wildcard patterns
- **Custom queries**: Use the "Write Custom Query" button for SQL queries

## Architecture
This build is for **$Runtime** architecture.

## Documentation
For full documentation, visit: https://github.com/sasastanojkov/S3Browser

## License
See LICENSE file in the repository.

## Source Code
https://github.com/sasastanojkov/S3Browser
"@

    $readmeContent | Out-File -FilePath "publish/$Runtime/README.txt" -Encoding UTF8

    # Create ZIP file
    Write-Host "Creating ZIP archive..." -ForegroundColor Yellow
    $zipName = "S3Browser-v$Version-$Runtime.zip"
    
    if (Test-Path $zipName) {
        Remove-Item $zipName -Force
    }
    
    Compress-Archive -Path "publish/$Runtime/*" -DestinationPath $zipName
    
    Write-Host "`nBuild completed successfully!" -ForegroundColor Green
    Write-Host "Output: $zipName" -ForegroundColor Green
    Write-Host "Published files in: publish/$Runtime/" -ForegroundColor Green
    
    # Show file size
    $zipSize = (Get-Item $zipName).Length / 1MB
    Write-Host "ZIP size: $([math]::Round($zipSize, 2)) MB" -ForegroundColor Cyan
    
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}
