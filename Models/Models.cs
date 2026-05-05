namespace MaghrebButusAPI.Models
{
    public class PointXYZ
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public class MntRequest
    {
        public List<PointXYZ> Points { get; set; } = new();
        public string MachineId { get; set; } = "";
    }

    public class Triangle
    {
        public PointXYZ P1 { get; set; } = new();
        public PointXYZ P2 { get; set; } = new();
        public PointXYZ P3 { get; set; } = new();
    }

    public class MntResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<Triangle> Triangles { get; set; } = new();
        public int PointCount { get; set; }
        public int TriangleCount { get; set; }
    }

    public class LicenceInfo
    {
        public bool Actif { get; set; }
        public string Expiration { get; set; } = "";
        public string Utilisateur { get; set; } = "";
        public string Email { get; set; } = "";
        public string MachineId { get; set; } = "";       // Machine autorisée (monoposte)
        public string MachineName { get; set; } = "";     // Nom lisible de la machine
        public List<string> Fonctions { get; set; } = new();
    }

    public class LicenceCheckRequest
    {
        public string IdToken { get; set; } = "";         // JWT Firebase du plugin
        public string MachineId { get; set; } = "";       // Hash unique de la machine
        public string MachineName { get; set; } = "";
    }

    public class LicenceCheckResponse
    {
        public bool Autorise { get; set; }
        public string Message { get; set; } = "";
        public string Utilisateur { get; set; } = "";
        public string Expiration { get; set; } = "";
        public List<string> Fonctions { get; set; } = new();
        public string SessionToken { get; set; } = "";    // Token de session signé par notre API
    }

    // ── Calage Automatique ────────────────────────────────────────────────────

    public class CollecteurCalageRequest
    {
        public string Nom { get; set; } = "";
        public List<PointXYZ> Regards { get; set; } = new();
        public List<double> Diametres { get; set; } = new();
        public string CollecteurAvalNom { get; set; } = "";
        public PointXYZ? PointConnexion { get; set; }  // nullable
    }

    public class CalageRequest
    {
        public string MachineId { get; set; } = "";
        public double PenteMin { get; set; } = 0.005;          // 0.5%
        public double PenteMax { get; set; } = 0.05;           // 5%
        public double RecouvrementMin { get; set; } = 1.0;     // 1m
        public List<CollecteurCalageRequest> Collecteurs { get; set; } = new();
    }

    public class CollecteurCalageResult
    {
        public string Nom { get; set; } = "";
        public List<double> AltitudesFilEau { get; set; } = new();
        public List<double> ChutesAval { get; set; } = new();
    }

    public class CalageResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<CollecteurCalageResult> Resultats { get; set; } = new();
    }
}
