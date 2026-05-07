using System.Globalization;
using System.Text.Json;
using MaghrebButusAPI.Models;

namespace MaghrebButusAPI.Services
{
    public class TopoService
    {
        private readonly HttpClient _http;
        private readonly string _maptilerKey;

        public TopoService(IHttpClientFactory httpFactory, IConfiguration config)
        {
            _http = httpFactory.CreateClient("Maptiler");
            _http.Timeout = TimeSpan.FromSeconds(120);
            _maptilerKey = config["Maptiler:ApiKey"] ?? "xXeDRtXpeuPb7DggheQA";
        }

        // ── Conversion de coordonnées ─────────────────────────────────────────
        public async Task<CoordConvertResponse> ConvertCoordinatesAsync(CoordConvertRequest req)
        {
            var results = new List<ProjectedPoint>();

            for (int i = 0; i < req.Points.Count; i++)
            {
                var pt = req.Points[i];
                string coordStr = req.SourceEpsg == 4326
                    ? $"{pt.Lon.ToString(CultureInfo.InvariantCulture)},{pt.Lat.ToString(CultureInfo.InvariantCulture)}"
                    : $"{pt.Lat.ToString(CultureInfo.InvariantCulture)},{pt.Lon.ToString(CultureInfo.InvariantCulture)}";

                string url = $"https://api.maptiler.com/coordinates/transform/{coordStr}.json?s_srs={req.SourceEpsg}&t_srs={req.TargetEpsg}&key={_maptilerKey}";

                await _maptilerSemaphore.WaitAsync();
                try
                {
                    var response = await _http.GetAsync(url);
                    var json = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                        throw new Exception($"MapTiler error {(int)response.StatusCode} for point {i}: {json}");

                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("results", out var resArray) || resArray.GetArrayLength() == 0)
                        throw new Exception($"MapTiler missing 'results' for point {i}: {json.Substring(0, Math.Min(200, json.Length))}");

                    var coord = resArray[0];
                    results.Add(new ProjectedPoint
                    {
                        X = coord.GetProperty("x").GetDouble(),
                        Y = coord.GetProperty("y").GetDouble()
                    });
                }
                finally
                {
                    _maptilerSemaphore.Release();
                }

                if (i > 0 && i % 50 == 0)
                    await Task.Delay(100);
            }

            return new CoordConvertResponse { Success = true, Points = results };
        }

        // ── Élévations ────────────────────────────────────────────────────────
        public async Task<ElevationResponse> GetElevationsAsync(ElevationRequest req)
        {
            var allElevations = new List<double>();
            int batchSize = 100;

            for (int start = 0; start < req.Points.Count; start += batchSize)
            {
                int end = Math.Min(start + batchSize, req.Points.Count);
                var batch = req.Points.GetRange(start, end - start);

                string lats = string.Join(",", batch.Select(p => p.Lat.ToString(CultureInfo.InvariantCulture)));
                string lons = string.Join(",", batch.Select(p => p.Lon.ToString(CultureInfo.InvariantCulture)));

                string url = $"https://api.open-meteo.com/v1/elevation?latitude={lats}&longitude={lons}";
                var response = await _http.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                foreach (var el in doc.RootElement.GetProperty("elevation").EnumerateArray())
                    allElevations.Add(el.GetDouble());

                if (end < req.Points.Count)
                    await Task.Delay(200);
            }

            return new ElevationResponse { Success = true, Elevations = allElevations };
        }

        // ── Grille de tuiles satellite ────────────────────────────────────────
        public async Task<TileGridResponse> ComputeTileGridAsync(TileGridRequest req)
        {
            int tileMinX = LonToTileX(req.MinLon, req.Zoom);
            int tileMaxX = LonToTileX(req.MaxLon, req.Zoom);
            int tileMinY = LatToTileY(req.MaxLat, req.Zoom);
            int tileMaxY = LatToTileY(req.MinLat, req.Zoom);

            int tilesX = tileMaxX - tileMinX + 1;
            int tilesY = tileMaxY - tileMinY + 1;

            // Compute lat/lon for grid edges
            double[] lons = new double[tilesX + 1];
            double[] lats = new double[tilesY + 1];
            for (int gx = 0; gx <= tilesX; gx++)
                lons[gx] = TileXToLon(tileMinX + gx, req.Zoom);
            for (int gy = 0; gy <= tilesY; gy++)
                lats[gy] = TileYToLat(tileMinY + gy, req.Zoom);

            // Convert left and right columns to projected coords IN PARALLEL
            double[] leftX = new double[tilesY + 1];
            double[] leftY = new double[tilesY + 1];
            double[] rightX = new double[tilesY + 1];
            double[] rightY = new double[tilesY + 1];

            var tasks = new List<Task>();
            for (int gy = 0; gy <= tilesY; gy++)
            {
                int idx = gy;
                tasks.Add(Task.Run(async () =>
                {
                    var (lx, ly) = await ConvertFromWGS84(lats[idx], lons[0], req.Epsg);
                    leftX[idx] = lx; leftY[idx] = ly;
                    var (rx, ry) = await ConvertFromWGS84(lats[idx], lons[tilesX], req.Epsg);
                    rightX[idx] = rx; rightY[idx] = ry;
                }));
            }
            await Task.WhenAll(tasks);

            // Fill grid by linear interpolation
            double[,] gridX = new double[tilesY + 1, tilesX + 1];
            double[,] gridY = new double[tilesY + 1, tilesX + 1];
            for (int gy = 0; gy <= tilesY; gy++)
            {
                for (int gx = 0; gx <= tilesX; gx++)
                {
                    double t = (double)gx / tilesX;
                    gridX[gy, gx] = leftX[gy] + t * (rightX[gy] - leftX[gy]);
                    gridY[gy, gx] = leftY[gy] + t * (rightY[gy] - leftY[gy]);
                }
            }

            var tiles = new List<TileInfo>();
            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    int tileXCoord = tileMinX + tx;
                    int tileYCoord = tileMinY + ty;

                    double blX = gridX[ty + 1, tx], blY = gridY[ty + 1, tx];
                    double brX = gridX[ty + 1, tx + 1], brY = gridY[ty + 1, tx + 1];
                    double tlX = gridX[ty, tx], tlY = gridY[ty, tx];

                    tiles.Add(new TileInfo
                    {
                        TileUrl = $"https://api.maptiler.com/tiles/satellite-v2/{req.Zoom}/{tileXCoord}/{tileYCoord}.jpg?key={_maptilerKey}",
                        OriginX = blX, OriginY = blY,
                        UX = brX - blX, UY = brY - blY,
                        VX = tlX - blX, VY = tlY - blY
                    });
                }
            }

            // Compute bounding box
            double minX = Math.Min(gridX[tilesY, 0], gridX[0, 0]);
            double minY = Math.Min(gridY[tilesY, 0], gridY[tilesY, tilesX]);
            double maxX = Math.Max(gridX[0, tilesX], gridX[tilesY, tilesX]);
            double maxY = Math.Max(gridY[0, 0], gridY[0, tilesX]);

            return new TileGridResponse
            {
                Success = true,
                Tiles = tiles,
                MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY,
                Message = $"{tiles.Count} tuiles calculées"
            };
        }

        // ── Contours (Marching Squares) ───────────────────────────────────────
        public async Task<ContourResponse> GenerateContoursAsync(ContourRequest req)
        {
            int zoom = 12;
            int tileMinX = LonToTileX(req.MinLon, zoom);
            int tileMaxX = LonToTileX(req.MaxLon, zoom);
            int tileMinY = LatToTileY(req.MaxLat, zoom);
            int tileMaxY = LatToTileY(req.MinLat, zoom);

            int tilesX = tileMaxX - tileMinX + 1;
            int tilesY = tileMaxY - tileMinY + 1;
            int tileSize = 256;
            int totalW = tilesX * tileSize;
            int totalH = tilesY * tileSize;
            double[,] elevation = new double[totalH, totalW];

            // Download terrain tiles
            for (int ty = tileMinY; ty <= tileMaxY; ty++)
            {
                for (int tx = tileMinX; tx <= tileMaxX; tx++)
                {
                    string url = $"https://api.maptiler.com/tiles/terrain-rgb-v2/{zoom}/{tx}/{ty}.png?key={_maptilerKey}";
                    var response = await _http.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    var bytes = await response.Content.ReadAsByteArrayAsync();

                    int offsetX = (tx - tileMinX) * tileSize;
                    int offsetY = (ty - tileMinY) * tileSize;

                    // Decode terrain-RGB PNG
                    using var ms = new MemoryStream(bytes);
                    using var bmp = new System.Drawing.Bitmap(ms);

                    for (int py = 0; py < Math.Min(bmp.Height, tileSize); py++)
                    {
                        for (int px = 0; px < Math.Min(bmp.Width, tileSize); px++)
                        {
                            var pixel = bmp.GetPixel(px, py);
                            double alt = -10000.0 + (pixel.R * 256.0 * 256.0 + pixel.G * 256.0 + pixel.B) * 0.1;
                            int gx = offsetX + px;
                            int gy = offsetY + py;
                            if (gx < totalW && gy < totalH)
                                elevation[gy, gx] = alt;
                        }
                    }
                }
            }

            // Grid geographic bounds
            double gridLonLeft = TileXToLon(tileMinX, zoom);
            double gridLonRight = TileXToLon(tileMaxX + 1, zoom);
            double gridLatTop = TileYToLat(tileMinY, zoom);
            double gridLatBottom = TileYToLat(tileMaxY + 1, zoom);

            // Convert corners to projected
            var (projMinX, projMinY) = await ConvertFromWGS84(gridLatBottom, gridLonLeft, req.Epsg);
            var (projMaxX, projMaxY) = await ConvertFromWGS84(gridLatTop, gridLonRight, req.Epsg);
            double projWidth = projMaxX - projMinX;
            double projHeight = projMaxY - projMinY;

            // Find elevation range
            double minElev = double.MaxValue, maxElev = double.MinValue;
            for (int y = 0; y < totalH; y++)
                for (int x = 0; x < totalW; x++)
                {
                    if (elevation[y, x] < minElev) minElev = elevation[y, x];
                    if (elevation[y, x] > maxElev) maxElev = elevation[y, x];
                }

            int startLevel = (int)(Math.Ceiling(minElev / req.Equidistance) * req.Equidistance);
            int endLevel = (int)(Math.Floor(maxElev / req.Equidistance) * req.Equidistance);

            var contours = new List<ContourLine>();

            for (int level = startLevel; level <= endLevel; level += req.Equidistance)
            {
                var segments = MarchingSquares(elevation, totalW, totalH, level);
                bool isMajor = (level % (req.Equidistance * 5)) == 0;

                foreach (var seg in segments)
                {
                    var projPoints = new List<ProjectedPoint>();
                    foreach (var pt in seg)
                    {
                        double px = projMinX + (pt.X / totalW) * projWidth;
                        double py = projMaxY - (pt.Y / totalH) * projHeight;
                        projPoints.Add(new ProjectedPoint { X = px, Y = py });
                    }
                    if (projPoints.Count >= 2)
                    {
                        contours.Add(new ContourLine
                        {
                            Elevation = level,
                            Level = isMajor ? 1 : 0,
                            Points = projPoints
                        });
                    }
                }
            }

            return new ContourResponse
            {
                Success = true,
                Contours = contours,
                Message = $"{contours.Count} courbes générées ({minElev:F0}m → {maxElev:F0}m)"
            };
        }

        // ── Marching Squares ──────────────────────────────────────────────────
        private static List<List<System.Drawing.PointF>> MarchingSquares(double[,] grid, int width, int height, double level)
        {
            var segments = new List<List<System.Drawing.PointF>>();

            for (int y = 0; y < height - 1; y++)
            {
                for (int x = 0; x < width - 1; x++)
                {
                    double tl = grid[y, x], tr = grid[y, x + 1];
                    double bl = grid[y + 1, x], br = grid[y + 1, x + 1];

                    int code = 0;
                    if (tl >= level) code |= 8;
                    if (tr >= level) code |= 4;
                    if (br >= level) code |= 2;
                    if (bl >= level) code |= 1;

                    if (code == 0 || code == 15) continue;

                    float top = Lerp(x, x + 1, tl, tr, level);
                    float bottom = Lerp(x, x + 1, bl, br, level);
                    float left = Lerp(y, y + 1, tl, bl, level);
                    float right = Lerp(y, y + 1, tr, br, level);

                    var pts = new List<System.Drawing.PointF>();
                    switch (code)
                    {
                        case 1: case 14: pts.Add(new(x, left)); pts.Add(new(bottom, y + 1)); break;
                        case 2: case 13: pts.Add(new(bottom, y + 1)); pts.Add(new(x + 1, right)); break;
                        case 3: case 12: pts.Add(new(x, left)); pts.Add(new(x + 1, right)); break;
                        case 4: case 11: pts.Add(new(top, y)); pts.Add(new(x + 1, right)); break;
                        case 5: pts.Add(new(x, left)); pts.Add(new(top, y)); pts.Add(new(bottom, y + 1)); pts.Add(new(x + 1, right)); break;
                        case 6: case 9: pts.Add(new(top, y)); pts.Add(new(bottom, y + 1)); break;
                        case 7: case 8: pts.Add(new(x, left)); pts.Add(new(top, y)); break;
                        case 10: pts.Add(new(top, y)); pts.Add(new(x + 1, right)); pts.Add(new(x, left)); pts.Add(new(bottom, y + 1)); break;
                    }
                    if (pts.Count >= 2) segments.Add(pts);
                }
            }
            return segments;
        }

        private static float Lerp(float a, float b, double va, double vb, double level)
        {
            if (Math.Abs(vb - va) < 0.0001) return (a + b) / 2f;
            return (float)(a + (level - va) / (vb - va) * (b - a));
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static readonly SemaphoreSlim _maptilerSemaphore = new(5, 5); // Max 5 concurrent calls

        private async Task<(double x, double y)> ConvertFromWGS84(double lat, double lon, int epsg)
        {
            await _maptilerSemaphore.WaitAsync();
            try
            {
                string url = $"https://api.maptiler.com/coordinates/transform/{lon.ToString(CultureInfo.InvariantCulture)},{lat.ToString(CultureInfo.InvariantCulture)}.json?s_srs=4326&t_srs={epsg}&key={_maptilerKey}";
                var response = await _http.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"MapTiler error {(int)response.StatusCode}: {json}");

                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                    throw new Exception($"MapTiler response missing 'results': {json.Substring(0, Math.Min(200, json.Length))}");

                var coord = results[0];
                return (coord.GetProperty("x").GetDouble(), coord.GetProperty("y").GetDouble());
            }
            finally
            {
                _maptilerSemaphore.Release();
            }
        }

        private static int LonToTileX(double lon, int zoom) =>
            (int)Math.Floor((lon + 180.0) / 360.0 * (1 << zoom));

        private static int LatToTileY(double lat, int zoom)
        {
            double latRad = lat * Math.PI / 180.0;
            return (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * (1 << zoom));
        }

        private static double TileXToLon(int x, int zoom) =>
            x / (double)(1 << zoom) * 360.0 - 180.0;

        private static double TileYToLat(int y, int zoom)
        {
            double n = Math.PI - 2.0 * Math.PI * y / (1 << zoom);
            return 180.0 / Math.PI * Math.Atan(Math.Sinh(n));
        }
    }
}
