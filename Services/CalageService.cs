using MaghrebButusAPI.Models;

namespace MaghrebButusAPI.Services
{
    public class CalageService
    {
        public CalageResponse CalculerCalage(CalageRequest req)
        {
            double penteMin = req.PenteMin;
            double penteMax = req.PenteMax;
            double recMin = req.RecouvrementMin;

            // Construire les objets internes
            var collecteurs = req.Collecteurs.Select(c => new CollecteurInterne
            {
                Nom = c.Nom,
                Regards = c.Regards,
                Diametres = c.Diametres,
                CollecteurAvalNom = c.CollecteurAvalNom,
                PointConnexion = c.PointConnexion
            }).ToList();

            // Résoudre les références aval
            foreach (var col in collecteurs)
                if (!string.IsNullOrEmpty(col.CollecteurAvalNom))
                    col.CollecteurAval = collecteurs.FirstOrDefault(c => c.Nom == col.CollecteurAvalNom);

            // ÉTAPE 1 : calage initial
            foreach (var col in collecteurs)
                CalcerCollecteur(col, recMin, penteMin, penteMax);

            // ÉTAPE 2 : ajustement des raccordements (itératif)
            bool changed = true;
            int iter = 0;
            while (changed && iter++ < 20)
            {
                changed = false;
                foreach (var col in collecteurs)
                {
                    if (col.CollecteurAval == null || col.Regards.Count < 2) continue;

                    int last = col.Regards.Count - 1;
                    double feAffluent = col.AltitudesFilEau[last];
                    double chuteLast = col.ChutesAval.Count > last ? col.ChutesAval[last] : 0;
                    double feApresChute = feAffluent - chuteLast;

                    double feAval = TrouverAltitudeAuPoint(col.CollecteurAval, col.PointConnexion);

                    if (feApresChute < feAval)
                    {
                        double deficit = feAval - feApresChute + 0.05;
                        // Remonter l'affluent
                        for (int i = 0; i < col.AltitudesFilEau.Count; i++)
                            col.AltitudesFilEau[i] += deficit;
                        changed = true;
                    }
                }
            }

            // ÉTAPE 3 : vérification finale des pentes
            foreach (var col in collecteurs)
            {
                bool modif = true;
                int pass = 0;
                while (modif && pass++ < 10)
                {
                    modif = false;
                    for (int i = 0; i < col.Regards.Count - 1; i++)
                    {
                        double dist = Distance(col.Regards[i], col.Regards[i + 1]);
                        double dz = col.AltitudesFilEau[i] - col.AltitudesFilEau[i + 1];
                        double pente = dz / dist;
                        if (pente > penteMax + 0.0001)
                        {
                            double chuteNec = dz - dist * penteMax;
                            col.AltitudesFilEau[i + 1] = col.AltitudesFilEau[i] - dist * penteMax;
                            while (col.ChutesAval.Count <= i + 1) col.ChutesAval.Add(0);
                            col.ChutesAval[i + 1] += chuteNec;
                            modif = true;
                        }
                    }
                }
            }

            return new CalageResponse
            {
                Success = true,
                Message = $"{collecteurs.Count} collecteur(s) calés",
                Resultats = collecteurs.Select(c => new CollecteurCalageResult
                {
                    Nom = c.Nom,
                    AltitudesFilEau = c.AltitudesFilEau,
                    ChutesAval = c.ChutesAval
                }).ToList()
            };
        }

        private static void CalcerCollecteur(CollecteurInterne col, double recMin, double penteMin, double penteMax)
        {
            col.AltitudesFilEau.Clear();
            col.ChutesAval.Clear();

            if (col.Regards.Count < 2) return;

            double diam0 = col.Diametres.Count > 0 ? col.Diametres[0] / 1000.0 : 0.3;
            double feDepart = col.Regards[0].Z - recMin - diam0;
            col.AltitudesFilEau.Add(feDepart);
            col.ChutesAval.Add(0);

            for (int i = 1; i < col.Regards.Count; i++)
            {
                double dist = Distance(col.Regards[i - 1], col.Regards[i]);
                double diami = i < col.Diametres.Count ? col.Diametres[i] / 1000.0 : 0.3;

                double feAvecPenteMin = col.AltitudesFilEau[i - 1] - dist * penteMin;
                double feAvecPenteMax = col.AltitudesFilEau[i - 1] - dist * penteMax;
                double recAvecPenteMin = col.Regards[i].Z - feAvecPenteMin - diami;

                double feAval;
                double chute = 0;

                if (recAvecPenteMin >= recMin)
                {
                    feAval = feAvecPenteMin;
                }
                else
                {
                    double fePourRec = col.Regards[i].Z - recMin - diami;
                    if (fePourRec >= feAvecPenteMax)
                        feAval = fePourRec;
                    else
                    {
                        feAval = feAvecPenteMax;
                        chute = feAvecPenteMax - fePourRec;
                    }
                }

                col.AltitudesFilEau.Add(feAval);
                col.ChutesAval.Add(chute);
            }
        }

        private static double TrouverAltitudeAuPoint(CollecteurInterne col, PointXYZ? pt)
        {
            if (col.AltitudesFilEau.Count == 0 || pt == null) return 0;
            double distMin = double.MaxValue;
            int idx = 0;
            for (int i = 0; i < col.Regards.Count; i++)
            {
                double d = Distance(col.Regards[i], pt);
                if (d < distMin) { distMin = d; idx = i; }
            }
            return col.AltitudesFilEau[idx];
        }

        private static double Distance(PointXYZ a, PointXYZ b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private class CollecteurInterne
        {
            public string Nom { get; set; } = "";
            public List<PointXYZ> Regards { get; set; } = new();
            public List<double> Diametres { get; set; } = new();
            public string CollecteurAvalNom { get; set; } = "";
            public PointXYZ? PointConnexion { get; set; }
            public CollecteurInterne? CollecteurAval { get; set; }
            public List<double> AltitudesFilEau { get; set; } = new();
            public List<double> ChutesAval { get; set; } = new();
        }
    }
}
