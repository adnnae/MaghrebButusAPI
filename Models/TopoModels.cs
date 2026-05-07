namespace MaghrebButusAPI.Models
{
    // ── Conversion de coordonnées ─────────────────────────────────────────────
    public class CoordConvertRequest
    {
        public string MachineId { get; set; } = "";
        public List<LatLonPoint> Points { get; set; } = new();
        public int SourceEpsg { get; set; } = 4326;
        public int TargetEpsg { get; set; }
    }

    public class LatLonPoint
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
    }

    public class ProjectedPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class CoordConvertResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<ProjectedPoint> Points { get; set; } = new();
    }

    // ── Élévations ────────────────────────────────────────────────────────────
    public class ElevationRequest
    {
        public string MachineId { get; set; } = "";
        public List<LatLonPoint> Points { get; set; } = new();
    }

    public class ElevationResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<double> Elevations { get; set; } = new();
    }

    // ── Courbes de niveau (Contours) ──────────────────────────────────────────
    public class ContourRequest
    {
        public string MachineId { get; set; } = "";
        public double MinLat { get; set; }
        public double MinLon { get; set; }
        public double MaxLat { get; set; }
        public double MaxLon { get; set; }
        public int Epsg { get; set; }
        public int Equidistance { get; set; } = 10;
    }

    public class ContourLine
    {
        public double Elevation { get; set; }
        public int Level { get; set; } // 0=minor, 1=major
        public List<ProjectedPoint> Points { get; set; } = new();
    }

    public class ContourResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<ContourLine> Contours { get; set; } = new();
    }

    // ── Projection satellite (grille de tuiles) ───────────────────────────────
    public class TileGridRequest
    {
        public string MachineId { get; set; } = "";
        public double MinLat { get; set; }
        public double MinLon { get; set; }
        public double MaxLat { get; set; }
        public double MaxLon { get; set; }
        public int Epsg { get; set; }
        public int Zoom { get; set; } = 16;
    }

    public class TileInfo
    {
        public string TileUrl { get; set; } = "";
        public double OriginX { get; set; }
        public double OriginY { get; set; }
        public double UX { get; set; }
        public double UY { get; set; }
        public double VX { get; set; }
        public double VY { get; set; }
    }

    public class TileGridResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<TileInfo> Tiles { get; set; } = new();
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
    }
}
