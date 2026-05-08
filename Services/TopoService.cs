using System.Globalization;
using System.Text.Json;
using MaghrebButusAPI.Models;

namespace MaghrebButusAPI.Services
{
    public class TopoService
    {
        private readonly HttpClient _http;
        private readonly string _maptilerKey;
        private readonly ProjectionService _proj;

        public TopoService(IHttpClientFactory httpFactory, IConfiguration config, ProjectionService proj)
        {
            _http = httpFactory.CreateClient("Maptiler");
            _http.Timeout = TimeSpan.FromSeconds(60);
            _maptilerKey = config["Maptiler:ApiKey"] ?? "xXeDRtXpeuPb7DggheQA";
            _proj = proj;
        }

        // ── Conversion de coordonnées (ProjNet — local, gratuit, sans réseau) ──
        public async Task<CoordConvertResponse> ConvertCoordinatesAsync(CoordConvertRequest req)
        {
            var results = new List<ProjectedPoint>(req.Points.Count);

            try
            {
                foreach (var pt in req.Points)
                {
                    // ProjNet: pour WGS84 (géographique) on passe (lon, lat)
                    // Pour les systèmes projetés on passe (x, y) = (Lat, Lon) dans notre modèle
                    double inputX = req.SourceEpsg == 4326 ? pt.Lon : pt.Lat;
                    double inputY = req.SourceEpsg == 4326 ? pt.Lat : pt.Lon;

                    var (outX, outY) = _proj.Transform(inputX, inputY, req.SourceEpsg, req.TargetEpsg);

                    results.Add(new ProjectedPoint { X = outX, Y = outY });
                }
            }
            catch (Exception ex)
            {
                return new CoordConvertResponse { Success = false, Message = $"Erreur projection: {ex.Message}" };
            }

            return await Task.FromResult(new CoordConvertResponse { Success = true, Points = results });
        }

        // ── Élévations ────────────────────────────────────────────────────────
        public async Task<ElevationResponse> GetElevationsAsync(ElevationRequest req)
        {
            // Try Open-Meteo first, fallback to AWS Terrarium on 429
            try
            {
                return await GetElevationsOpenMeteoAsync(req);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429"))
            {
                // Fallback: use AWS Terrarium tiles
                return await GetElevationsFromTerrainAsync(req);
            }
            catch (Exception ex) when (ex.Message.Contains("429"))
            {
                return await GetElevationsFromTerrainAsync(req);
            }
        }

        private async Task<ElevationResponse> GetElevationsOpenMeteoAsync(ElevationRequest req)
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
                if ((int)response.StatusCode == 429)
                    throw new HttpRequestException("429 Too Many Requests");

                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                foreach (var el in doc.RootElement.GetProperty("elevation").EnumerateArray())
                    allElevations.Add(el.GetDouble());

                if (end < req.Points.Count)
                    await Task.Delay(500);
            }

            return new ElevationResponse { Success = true, Elevations = allElevations };
        }

        /// <summary>
        /// Fallback: get elevation from AWS Terrarium tiles (free, no rate limit)
        /// </summary>
        private async Task<ElevationResponse> GetElevationsFromTerrainAsync(ElevationRequest req)
        {
            var elevations = new List<double>();
            int zoom = 12; // ~30m resolution

            foreach (var pt in req.Points)
            {
                int tileX = (int)Math.Floor((pt.Lon + 180.0) / 360.0 * (1 << zoom));
                double latRad = pt.Lat * Math.PI / 180.0;
                int tileY = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * (1 << zoom));

                string url = $"https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{zoom}/{tileX}/{tileY}.png";
                var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    elevations.Add(0);
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync();
                using var ms = new MemoryStream(bytes);
                using var bmp = new System.Drawing.Bitmap(ms);

                // Calculate pixel position within tile
                double n = 1 << zoom;
                double tileLonLeft = tileX / n * 360.0 - 180.0;
                double tileLonRight = (tileX + 1) / n * 360.0 - 180.0;
                double tileLatTop = Math.Atan(Math.Sinh(Math.PI * (1 - 2.0 * tileY / n))) * 180.0 / Math.PI;
                double tileLatBottom = Math.Atan(Math.Sinh(Math.PI * (1 - 2.0 * (tileY + 1) / n))) * 180.0 / Math.PI;

                int px = (int)((pt.Lon - tileLonLeft) / (tileLonRight - tileLonLeft) * 256);
                int py = (int)((tileLatTop - pt.Lat) / (tileLatTop - tileLatBottom) * 256);
                px = Math.Clamp(px, 0, 255);
                py = Math.Clamp(py, 0, 255);

                var pixel = bmp.GetPixel(px, py);
                // Terrarium encoding: altitude = (R×256 + G + B/256) - 32768
                double alt = (pixel.R * 256.0 + pixel.G + pixel.B / 256.0) - 32768.0;
                elevations.Add(Math.Round(alt, 1));
            }

            return new ElevationResponse { Success = true, Elevations = elevations };
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

            // Convert left and right columns to projected coords (in parallel)
            double[] leftX = new double[tilesY + 1];
            double[] leftY = new double[tilesY + 1];
            double[] rightX = new double[tilesY + 1];
            double[] rightY = new double[tilesY + 1];

            var tasks = new List<Task>();
            for (int gy = 0; gy <= tilesY; gy++)
            {
                int gyLocal = gy;
                tasks.Add(Task.Run(async () =>
                {
                    var (lx, ly) = await ConvertFromWGS84(lats[gyLocal], lons[0], req.Epsg);
                    leftX[gyLocal] = lx; leftY[gyLocal] = ly;
                }));
                tasks.Add(Task.Run(async () =>
                {
                    var (rx, ry) = await ConvertFromWGS84(lats[gyLocal], lons[tilesX], req.Epsg);
                    rightX[gyLocal] = rx; rightY[gyLocal] = ry;
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

            // Download terrain tiles (AWS Terrarium - gratuit, sans clé)
            for (int ty = tileMinY; ty <= tileMaxY; ty++)
            {
                for (int tx = tileMinX; tx <= tileMaxX; tx++)
                {
                    string url = $"https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{zoom}/{tx}/{ty}.png";
                    var response = await _http.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    var bytes = await response.Content.ReadAsByteArrayAsync();

                    int offsetX = (tx - tileMinX) * tileSize;
                    int offsetY = (ty - tileMinY) * tileSize;

                    // Decode Terrarium PNG: altitude = (R×256 + G + B/256) - 32768
                    using var ms = new MemoryStream(bytes);
                    using var bmp = new System.Drawing.Bitmap(ms);

                    for (int py = 0; py < Math.Min(bmp.Height, tileSize); py++)
                    {
                        for (int px = 0; px < Math.Min(bmp.Width, tileSize); px++)
                        {
                            var pixel = bmp.GetPixel(px, py);
                            double alt = (pixel.R * 256.0 + pixel.G + pixel.B / 256.0) - 32768.0;
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
        private Task<(double x, double y)> ConvertFromWGS84(double lat, double lon, int epsg)
        {
            // ProjNet local — pas d'appel réseau
            var (x, y) = _proj.Transform(lon, lat, 4326, epsg);
            return Task.FromResult((x, y));
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
