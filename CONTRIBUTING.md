# Contributing to S3Browser

Thank you for your interest in contributing to S3Browser! This document provides guidelines and instructions for contributing.

## Development Setup

### Prerequisites
- Visual Studio 2022 (Community edition or higher) or Visual Studio Code
- .NET 8 SDK
- Git
- AWS CLI (for testing S3 functionality)

### Getting Started

1. Fork the repository on GitHub
2. Clone your fork locally:
   ```bash
   git clone https://github.com/YOUR-USERNAME/S3Browser.git
   cd S3Browser
   ```
3. Create a branch for your feature:
   ```bash
   git checkout -b feature/my-awesome-feature
   ```
4. Open `S3Browser.sln` in Visual Studio or the folder in VS Code

### Building and Running

**Using Visual Studio:**
- Open `S3Browser.sln`
- Press F5 to build and run

**Using Command Line:**
```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Run
dotnet run --project S3Browser/S3Browser.csproj
```

### Testing Your Changes

1. Test with different AWS profiles
2. Test with anonymous/public bucket access
3. Test with various file types (Parquet, CSV, TSV, text)
4. Test with different S3 path patterns
5. Test with geometry data if modifying map features
6. Verify UI responsiveness with large datasets

## Code Style

This project uses StyleCop for code analysis. Please follow these guidelines:

- Use C# 12 features where appropriate
- Enable nullable reference types
- Add XML documentation for public members
- Follow existing naming conventions
- Keep methods focused and reasonably sized

## Making Changes

### Code Organization
- **MainWindow.xaml/.cs** - Main browser window and S3 navigation
- **ParquetViewerWindow.xaml/.cs** - Parquet file viewer with DuckDB
- **TabularFileViewerWindow.xaml/.cs** - CSV/TSV file viewer
- **FileViewerWindow.xaml/.cs** - Text file viewer
- **GeometryMapWindow.xaml/.cs** - Map visualization for geometries
- **QueryEditorDialog.xaml/.cs** - SQL query editor
- **Services/** - Backend services (S3Manager, DuckDbManager)
- **Helpers/** - Utility classes

### Adding New Features

1. Discuss significant changes by opening an issue first
2. Write code following existing patterns
3. Test thoroughly with different scenarios
4. Update README.md if adding user-facing features
5. Add comments for complex logic

### Submitting Changes

1. Commit your changes with clear, descriptive messages:
   ```bash
   git commit -m "Add feature: ability to export query results to CSV"
   ```

2. Push to your fork:
   ```bash
   git push origin feature/my-awesome-feature
   ```

3. Create a Pull Request on GitHub with:
   - Clear description of what changed and why
   - Screenshots for UI changes
   - Testing steps you followed
   - Related issue numbers (if applicable)

## Pull Request Guidelines

- **Title**: Use a clear, descriptive title
- **Description**: Explain what changed and why
- **Testing**: Describe how you tested the changes
- **Screenshots**: Include screenshots for UI changes
- **Breaking Changes**: Clearly mark any breaking changes
- **Documentation**: Update README.md for user-facing changes

## Reporting Issues

When reporting issues, please include:

- S3Browser version
- Windows version
- Steps to reproduce
- Expected vs actual behavior
- Error messages or screenshots
- Sample S3 paths (if applicable, remove sensitive info)

## Release Process

For maintainers only:

1. Update version numbers if needed
2. Update CHANGELOG.md
3. Commit changes
4. Create and push a tag:
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```
5. GitHub Actions automatically builds and creates the release

## Questions?

Feel free to open an issue for questions about contributing!

## Code of Conduct

Be respectful and professional in all interactions. We're all here to build something useful together.
