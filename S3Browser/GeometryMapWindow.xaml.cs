using System.Windows;
using System.Windows.Input;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Rendering.Skia;
using Mapsui.Styles;
using Mapsui.Tiling;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using Color = Mapsui.Styles.Color;
using MPoint = Mapsui.MPoint;
using NtsPoint = NetTopologySuite.Geometries.Point;

namespace S3Browser
{
    /// <summary>
    /// Window for displaying WKT geometries from Parquet files on an interactive map.
    /// Uses Mapsui for map rendering with OpenStreetMap tiles and geometry overlay.
    /// </summary>
    public partial class GeometryMapWindow : Window
    {
        private readonly Map _map = new();
        private readonly MapRenderer _renderer = new();
        private WritableLayer? _geometryLayer;
        private MPoint? _previousMousePosition;
        private bool _isPanning;
        private List<GeometryInfo>? _pendingGeometries;
        private readonly Dictionary<IFeature, string> _featureColumnNames = new();
        private readonly Dictionary<IFeature, (IStyle normalStyle, IStyle highlightedStyle)> _featureStyles = new();
        private IFeature? _currentHighlightedFeature = null;
        private DateTime _lastRenderTime = DateTime.MinValue;
        private const int MinRenderIntervalMs = 16; // ~60 FPS max
        private bool _renderPending = false;
        private double _lastRenderedResolution = 0;
        private MPoint? _lastRenderedCenter = null;

        /// <summary>
        /// Information about a geometry including WKT and source column name.
        /// </summary>
        public class GeometryInfo
        {
            /// <summary>
            /// Gets or sets the WKT (Well-Known Text) representation of the geometry.
            /// </summary>
            public string Wkt { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the name of the column from which this geometry was extracted.
            /// </summary>
            public string ColumnName { get; set; } = string.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the GeometryMapWindow.
        /// Map is initialized when the window is loaded.
        /// </summary>
        public GeometryMapWindow()
        {
            InitializeComponent();
            Loaded += GeometryMapWindow_Loaded;
        }

        private void GeometryMapWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Initializing map...";
                InitializeMap();
                StatusText.Text = "Map ready";

                // Load any pending geometries
                if (_pendingGeometries != null)
                {
                    LoadGeometriesInternal(_pendingGeometries, null);
                    _pendingGeometries = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing map: {ex.Message}", "Map Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void InitializeMap()
        {
            // Add OpenStreetMap tile layer with optimizations (Mapsui 5.0)
            try
            {
                var tileLayer = OpenStreetMap.CreateTileLayer();
                _map.Layers.Add(tileLayer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load tiles: {ex.Message}");

                // Fallback to simple background
                _map.BackColor = Color.FromArgb(255, 240, 240, 245);
            }

            // Create geometry layer with optimizations
            _geometryLayer = new WritableLayer
            {
                Name = "Geometries",
                Style = null,
                IsMapInfoLayer = false // Disable map info for better performance
            };
            _map.Layers.Add(_geometryLayer);

            // Performance settings
            _map.Navigator.OverridePanBounds = null; // Allow free panning
            _map.Navigator.RotationLock = true;

            // Set initial view (will be adjusted when geometries are loaded)
            _map.Navigator.CenterOn(0, 0);
            _map.Navigator.ZoomTo(1);
        }

        /// <summary>
        /// Loads a list of WKT geometries onto the map with auto-generated names.
        /// </summary>
        /// <param name="wktGeometries">List of WKT strings to visualize.</param>
        public void LoadGeometries(List<string> wktGeometries)
        {
            var geometryInfos = wktGeometries.Select((wkt, index) => new GeometryInfo
            {
                Wkt = wkt,
                ColumnName = $"Geometry {index + 1}"
            }).ToList();

            LoadGeometriesWithInfo(geometryInfos);
        }

        /// <summary>
        /// Loads geometries with associated column name information onto the map.
        /// Creates selection buttons for each geometry.
        /// </summary>
        /// <param name="geometryInfos">List of geometry information objects.</param>
        public void LoadGeometriesWithInfo(List<GeometryInfo> geometryInfos)
        {
            _pendingGeometries = geometryInfos;

            // Only load if map is initialized, otherwise will load on window Loaded event
            if (_geometryLayer is not null)
            {
                LoadGeometriesInternal(geometryInfos, null);
            }
        }

        /// <summary>
        /// Loads geometries onto the map with one geometry highlighted.
        /// </summary>
        /// <param name="geometryInfos">List of geometry information objects.</param>
        /// <param name="highlightedColumnName">Column name of the geometry to highlight.</param>
        public void LoadGeometriesWithHighlight(List<GeometryInfo> geometryInfos, string highlightedColumnName)
        {
            _pendingGeometries = geometryInfos;

            // Only load if map is initialized, otherwise will load on window Loaded event
            if (_geometryLayer is not null)
            {
                LoadGeometriesInternal(geometryInfos, highlightedColumnName);
            }
        }

        /// <summary>
        /// Highlights a specific geometry on the map by its column name.
        /// Returns previously highlighted geometries to normal display.
        /// </summary>
        /// <param name="columnName">The column name of the geometry to highlight.</param>
        public void HighlightGeometryByColumnName(string columnName)
        {
            // Find the feature with the matching column name
            var featureToHighlight = _featureColumnNames.FirstOrDefault(kvp => kvp.Value == columnName).Key;

            if (featureToHighlight == null)
                return;

            // If there's a currently highlighted feature, return it to normal
            if (_currentHighlightedFeature != null && _currentHighlightedFeature != featureToHighlight)
            {
                if (_featureStyles.TryGetValue(_currentHighlightedFeature, out var previousStyles))
                {
                    var stylesList = (List<IStyle>)_currentHighlightedFeature.Styles;
                    stylesList.Clear();
                    stylesList.Add(previousStyles.normalStyle);
                }
            }

            // Highlight the new feature
            if (_featureStyles.TryGetValue(featureToHighlight, out var styles))
            {
                var stylesList = (List<IStyle>)featureToHighlight.Styles;
                stylesList.Clear();
                stylesList.Add(styles.highlightedStyle);
                _currentHighlightedFeature = featureToHighlight;
            }

            MapCanvas.InvalidateVisual();
        }

        private void LoadGeometriesInternal(List<GeometryInfo> geometryInfos, string? highlightedColumnName = null)
        {
            if (_geometryLayer == null)
            {
                StatusText.Text = "Map not initialized";
                return;
            }

            var wktReader = new WKTReader();
            int validCount = 0;
            int errorCount = 0;
            int totalVertices = 0;

            StatusText.Text = "Loading geometries...";

            // Clear existing features and metadata
            _geometryLayer.Clear();
            _featureColumnNames.Clear();
            _featureStyles.Clear();
            _currentHighlightedFeature = null;

            // Clear existing buttons (except the header)
            ClearGeometryButtons();

            // Color palette
            var colors = new[]
            {
                (Color.FromArgb(200, 255, 0, 0), Color.FromArgb(100, 255, 102, 102)),     // Red
                (Color.FromArgb(200, 0, 0, 255), Color.FromArgb(100, 102, 102, 255)),     // Blue
                (Color.FromArgb(200, 0, 170, 0), Color.FromArgb(100, 102, 221, 102)),     // Green
                (Color.FromArgb(200, 255, 136, 0), Color.FromArgb(100, 255, 170, 102)),   // Orange
                (Color.FromArgb(200, 170, 0, 255), Color.FromArgb(100, 221, 102, 255)),   // Purple
                (Color.FromArgb(200, 0, 170, 170), Color.FromArgb(100, 102, 221, 221)),   // Cyan
                (Color.FromArgb(200, 255, 170, 0), Color.FromArgb(100, 255, 204, 102)),   // Gold
                (Color.FromArgb(200, 255, 0, 136), Color.FromArgb(100, 255, 102, 170)),   // Pink
            };

            var features = new List<IFeature>();

            for (int i = 0; i < geometryInfos.Count; i++)
            {
                var geometryInfo = geometryInfos[i];
                try
                {
                    var wkt = geometryInfo.Wkt;
                    if (string.IsNullOrWhiteSpace(wkt) || wkt.StartsWith("0x") || wkt.StartsWith("[Error"))
                        continue;

                    var ntsGeometry = wktReader.Read(wkt);
                    if (ntsGeometry == null || ntsGeometry.IsEmpty)
                        continue;

                    totalVertices += ntsGeometry.NumPoints;

                    // Transform from WGS84 to Web Mercator
                    var transformedGeometry = TransformToWebMercator(ntsGeometry);

                    // Convert NTS geometry to Mapsui feature manually
                    var feature = new GeometryFeature { Geometry = transformedGeometry };
                    if (feature == null)
                        continue;

                    // Get color for this geometry
                    var colorIndex = i % colors.Length;
                    var (strokeColor, fillColor) = colors[colorIndex];

                    // Check if this geometry should be highlighted
                    bool isHighlighted = highlightedColumnName != null &&
                                        geometryInfo.ColumnName == highlightedColumnName;

                    // Create normal and highlighted styles based on geometry type
                    IStyle normalStyle;
                    IStyle highlightedStyle;

                    if (ntsGeometry is NtsPoint || ntsGeometry is MultiPoint)
                    {
                        normalStyle = new SymbolStyle
                        {
                            SymbolScale = 0.8,
                            SymbolType = SymbolType.Ellipse,
                            Fill = new Brush(strokeColor),
                            Outline = new Pen(Color.White, 2)
                        };
                        highlightedStyle = new SymbolStyle
                        {
                            SymbolScale = 1.5,
                            SymbolType = SymbolType.Ellipse,
                            Fill = new Brush(Color.Yellow),
                            Outline = new Pen(Color.Red, 4)
                        };
                    }
                    else if (ntsGeometry is LineString || ntsGeometry is MultiLineString)
                    {
                        normalStyle = new VectorStyle
                        {
                            Line = new Pen(strokeColor, 3)
                        };
                        highlightedStyle = new VectorStyle
                        {
                            Line = new Pen(Color.Yellow, 6)
                        };
                    }
                    else
                    {
                        normalStyle = new VectorStyle
                        {
                            Fill = new Brush(fillColor),
                            Outline = new Pen(strokeColor, 2)
                        };
                        highlightedStyle = new VectorStyle
                        {
                            Fill = new Brush(Color.FromArgb(200, 255, 255, 0)),
                            Outline = new Pen(Color.Red, 4)
                        };
                    }

                    // Set the current style based on whether this should be highlighted
                    feature.Styles = new List<IStyle> { isHighlighted ? highlightedStyle : normalStyle };

                    // Store both styles for later toggling
                    _featureStyles[feature] = (normalStyle, highlightedStyle);

                    // Track the highlighted feature
                    if (isHighlighted)
                    {
                        _currentHighlightedFeature = feature;
                    }

                    // Store column name
                    _featureColumnNames[feature] = geometryInfo.ColumnName;

                    features.Add(feature);
                    validCount++;
                }
                catch (Exception ex)
                {
                    errorCount++;
                    System.Diagnostics.Debug.WriteLine($"Error parsing geometry: {ex.Message}");
                }
            }

            // Add all features to layer
            _geometryLayer.AddRange(features);

            // Create buttons for each geometry
            CreateGeometryButtons(geometryInfos.Take(validCount).ToList(), highlightedColumnName);

            GeometryCountText.Text = $"Geometries: {validCount} ({totalVertices:N0} vertices)";

            if (errorCount > 0)
            {
                StatusText.Text = $"Loaded {validCount} geometries ({errorCount} errors)";
            }
            else
            {
                StatusText.Text = $"Loaded {validCount} geometries";
            }

            // Zoom to extent of all geometries
            if (validCount > 0)
            {
                ZoomToExtent();
            }

            MapCanvas.InvalidateVisual();
        }

        private Geometry TransformToWebMercator(Geometry geometry)
        {
            try
            {
                var envelope = geometry.EnvelopeInternal;

                // Check if coordinates look like WGS84 (lat/lon)
                if (envelope.MinX >= -180 && envelope.MaxX <= 180 &&
                    envelope.MinY >= -90 && envelope.MaxY <= 90)
                {
                    var transformed = geometry.Copy();
                    transformed.Apply(new WebMercatorTransformer());
                    return transformed;
                }
            }
            catch
            {
                // If transformation fails, return original
            }

            return geometry;
        }

        private class WebMercatorTransformer : ICoordinateSequenceFilter
        {
            public void Filter(CoordinateSequence seq, int i)
            {
                var coord = seq.GetCoordinate(i);
                var (x, y) = SphericalMercator.FromLonLat(coord.X, coord.Y);
                seq.SetOrdinate(i, 0, x);
                seq.SetOrdinate(i, 1, y);
            }

            public bool Done => false;
            public bool GeometryChanged => true;
        }

        private void ZoomToExtent()
        {
            if (_geometryLayer == null || !_geometryLayer.GetFeatures().Any())
                return;

            try
            {
                var extent = _geometryLayer.Extent;
                if (extent != null)
                {
                    // Check if extent is very small (e.g., single point or very close points)
                    var extentWidth = extent.Width;
                    var extentHeight = extent.Height;

                    // If extent is too small (less than 1000 meters), create a minimum extent
                    if (extentWidth < 1000 || extentHeight < 1000)
                    {
                        // Create a buffer of ~500 meters around the center
                        var centerX = (extent.Left + extent.Right) / 2;
                        var centerY = (extent.Top + extent.Bottom) / 2;
                        var buffer = 500; // meters in Web Mercator

                        extent = new MRect(
                            centerX - buffer,
                            centerY - buffer,
                            centerX + buffer,
                            centerY + buffer);
                    }

                    // Add 10% padding
                    var paddedExtent = extent.Grow(extent.Width * 0.1, extent.Height * 0.1);
                    _map.Navigator.ZoomToBox(paddedExtent);
                }
            }
            catch
            {
                // If zoom fails, stay at current position
            }
        }

        private void MapCanvas_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SkiaSharp.SKColors.White);

            _map.Navigator.SetSize(e.Info.Width, e.Info.Height);

            // Check if we need to update tiles (only if viewport changed significantly)
            var currentResolution = _map.Navigator.Viewport.Resolution;
            var currentCenter = new MPoint(_map.Navigator.Viewport.CenterX, _map.Navigator.Viewport.CenterY);

            bool viewportChanged = _lastRenderedResolution == 0 ||
                                   Math.Abs(currentResolution - _lastRenderedResolution) > currentResolution * 0.01 ||
                                   (_lastRenderedCenter != null &&
                                    (Math.Abs(currentCenter.X - _lastRenderedCenter.X) > currentResolution * 100 ||
                                     Math.Abs(currentCenter.Y - _lastRenderedCenter.Y) > currentResolution * 100));

            if (viewportChanged)
            {
                _lastRenderedResolution = currentResolution;
                _lastRenderedCenter = currentCenter;
            }

            // Render with performance optimization
            try
            {
                _renderer.Render(canvas, _map.Navigator.Viewport, _map.Layers, _map.Widgets, _map.BackColor);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Render error: {ex.Message}");
            }

            _renderPending = false;
        }

        private void MapCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                // Start panning
                _isPanning = true;
                _previousMousePosition = GetMapsuiPosition(e);
                MapCanvas.CaptureMouse();
            }
        }

        private void MapCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isPanning = false;
            _previousMousePosition = null;
            MapCanvas.ReleaseMouseCapture();
        }

        private void MapCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning && _previousMousePosition != null)
            {
                var currentPosition = GetMapsuiPosition(e);

                // Calculate delta in screen coordinates
                var deltaX = currentPosition.X - _previousMousePosition.X;
                var deltaY = currentPosition.Y - _previousMousePosition.Y;

                // Convert screen delta to world delta using the viewport resolution
                var resolution = _map.Navigator.Viewport.Resolution;
                var worldDeltaX = deltaX * resolution;
                var worldDeltaY = deltaY * resolution;

                // Update the map center by the world delta
                _map.Navigator.CenterOn(
                    _map.Navigator.Viewport.CenterX - worldDeltaX,
                    _map.Navigator.Viewport.CenterY + worldDeltaY); // Y is inverted in screen coordinates

                _previousMousePosition = currentPosition;

                // Throttle redraws during panning for better performance
                RequestRender();
            }
        }

        private void RequestRender()
        {
            // Throttle rendering to avoid overwhelming the GPU
            var now = DateTime.UtcNow;
            var timeSinceLastRender = (now - _lastRenderTime).TotalMilliseconds;

            if (timeSinceLastRender >= MinRenderIntervalMs)
            {
                _lastRenderTime = now;
                MapCanvas.InvalidateVisual();
            }
            else if (!_renderPending)
            {
                // Schedule a render after the minimum interval
                _renderPending = true;
                var delay = (int)(MinRenderIntervalMs - timeSinceLastRender);
                Task.Delay(delay).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_renderPending)
                        {
                            _lastRenderTime = DateTime.UtcNow;
                            MapCanvas.InvalidateVisual();
                        }
                    });
                });
            }
        }

        private void MapCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var mousePosition = e.GetPosition(MapCanvas);
            var worldPosition = _map.Navigator.Viewport.ScreenToWorld(mousePosition.X, mousePosition.Y);

            if (e.Delta > 0)
            {
                _map.Navigator.ZoomIn();
            }
            else
            {
                _map.Navigator.ZoomOut();
            }

            MapCanvas.InvalidateVisual();
            e.Handled = true;
        }

        private MPoint GetMapsuiPosition(MouseEventArgs e)
        {
            var position = e.GetPosition(MapCanvas);
            return new MPoint(position.X, position.Y);
        }

        private void ClearGeometryButtons()
        {
            // Keep only the header TextBlock (first child)
            while (GeometryButtonsPanel.Children.Count > 1)
            {
                GeometryButtonsPanel.Children.RemoveAt(1);
            }
        }

        private void CreateGeometryButtons(List<GeometryInfo> geometryInfos, string? highlightedColumnName)
        {
            // Create a button for each geometry
            foreach (var geometryInfo in geometryInfos)
            {
                var button = new System.Windows.Controls.Button
                {
                    Content = geometryInfo.ColumnName,
                    Margin = new Thickness(0, 0, 0, 5),
                    Padding = new Thickness(10, 5, 10, 5),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Tag = geometryInfo.ColumnName,
                    Cursor = Cursors.Hand
                };

                // Highlight the button if it matches the highlighted column
                if (geometryInfo.ColumnName == highlightedColumnName)
                {
                    button.Background = System.Windows.Media.Brushes.LightYellow;
                    button.BorderBrush = System.Windows.Media.Brushes.Orange;
                    button.BorderThickness = new Thickness(2);
                    button.FontWeight = FontWeights.Bold;
                }

                button.Click += GeometryButton_Click;
                GeometryButtonsPanel.Children.Add(button);
            }
        }

        private void GeometryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is string columnName)
            {
                // Check if this button is already selected (highlighted)
                bool isAlreadySelected = button.Background == System.Windows.Media.Brushes.LightYellow;

                // Reset all buttons to normal
                foreach (var child in GeometryButtonsPanel.Children)
                {
                    if (child is System.Windows.Controls.Button btn)
                    {
                        btn.Background = System.Windows.Media.Brushes.White;
                        btn.BorderBrush = System.Windows.Media.Brushes.LightGray;
                        btn.BorderThickness = new Thickness(1);
                        btn.FontWeight = FontWeights.Normal;
                    }
                }

                // If button was already selected, deselect it (toggle off)
                if (isAlreadySelected)
                {
                    // Return all geometries to normal style
                    if (_currentHighlightedFeature != null)
                    {
                        if (_featureStyles.TryGetValue(_currentHighlightedFeature, out var styles))
                        {
                            var stylesList = (List<IStyle>)_currentHighlightedFeature.Styles;
                            stylesList.Clear();
                            stylesList.Add(styles.normalStyle);
                        }
                        _currentHighlightedFeature = null;
                    }
                }
                else
                {
                    // Button was not selected, so highlight it
                    button.Background = System.Windows.Media.Brushes.LightYellow;
                    button.BorderBrush = System.Windows.Media.Brushes.Orange;
                    button.BorderThickness = new Thickness(2);
                    button.FontWeight = FontWeights.Bold;

                    // Highlight the corresponding geometry
                    HighlightGeometryByColumnName(columnName);
                }

                MapCanvas.InvalidateVisual();
            }
        }
    }
}
