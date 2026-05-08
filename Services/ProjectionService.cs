using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using System.Collections.Concurrent;

namespace MaghrebButusAPI.Services
{
    /// <summary>
    /// Service de conversion de coordonnées local (ProjNet) — remplace MapTiler Coordinates API
    /// Gratuit, sans clé, sans réseau, ultra rapide
    /// </summary>
    public class ProjectionService
    {
        private readonly CoordinateSystemFactory _csFactory = new CoordinateSystemFactory();
        private readonly CoordinateTransformationFactory _ctFactory = new CoordinateTransformationFactory();
        private readonly ConcurrentDictionary<int, CoordinateSystem> _csCache = new();

        // Définitions WKT des systèmes de coordonnées utilisés au Maghreb
        private static readonly Dictionary<int, string> WktDefinitions = new()
        {
            [4326] = "GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]]",

            [26191] = "PROJCS[\"Merchich / Nord Maroc\",GEOGCS[\"Merchich\",DATUM[\"Merchich\",SPHEROID[\"Clarke 1880 (IGN)\",6378249.2,293.4660212936265]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Lambert_Conformal_Conic_1SP\"],PARAMETER[\"latitude_of_origin\",33.3],PARAMETER[\"central_meridian\",-5.4],PARAMETER[\"scale_factor\",0.999625769],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",300000],UNIT[\"metre\",1]]",

            [26192] = "PROJCS[\"Merchich / Sud Maroc\",GEOGCS[\"Merchich\",DATUM[\"Merchich\",SPHEROID[\"Clarke 1880 (IGN)\",6378249.2,293.4660212936265]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Lambert_Conformal_Conic_1SP\"],PARAMETER[\"latitude_of_origin\",29.7],PARAMETER[\"central_meridian\",-5.4],PARAMETER[\"scale_factor\",0.9996155960000001],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",300000],UNIT[\"metre\",1]]",

            [26194] = "PROJCS[\"Merchich / Sahara\",GEOGCS[\"Merchich\",DATUM[\"Merchich\",SPHEROID[\"Clarke 1880 (IGN)\",6378249.2,293.4660212936265]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Lambert_Conformal_Conic_1SP\"],PARAMETER[\"latitude_of_origin\",26.1],PARAMETER[\"central_meridian\",-5.4],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",1200000],PARAMETER[\"false_northing\",400000],UNIT[\"metre\",1]]",

            [32628] = "PROJCS[\"WGS 84 / UTM zone 28N\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",-15],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1]]",

            [32629] = "PROJCS[\"WGS 84 / UTM zone 29N\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",-9],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1]]",

            [32630] = "PROJCS[\"WGS 84 / UTM zone 30N\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",-3],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1]]",

            [32631] = "PROJCS[\"WGS 84 / UTM zone 31N\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",3],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1]]",

            [32632] = "PROJCS[\"WGS 84 / UTM zone 32N\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",9],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1]]",

            [32633] = "PROJCS[\"WGS 84 / UTM zone 33N\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",15],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1]]",

            [30791] = "PROJCS[\"Nord Sahara 1959 / Nord Algerie\",GEOGCS[\"Nord Sahara 1959\",DATUM[\"Nord_Sahara_1959\",SPHEROID[\"Clarke 1880 (RGS)\",6378249.145,293.465]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Lambert_Conformal_Conic_1SP\"],PARAMETER[\"latitude_of_origin\",36],PARAMETER[\"central_meridian\",2.7],PARAMETER[\"scale_factor\",0.999625544],PARAMETER[\"false_easting\",500135],PARAMETER[\"false_northing\",300090],UNIT[\"metre\",1]]",

            [30792] = "PROJCS[\"Nord Sahara 1959 / Sud Algerie\",GEOGCS[\"Nord Sahara 1959\",DATUM[\"Nord_Sahara_1959\",SPHEROID[\"Clarke 1880 (RGS)\",6378249.145,293.465]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Lambert_Conformal_Conic_1SP\"],PARAMETER[\"latitude_of_origin\",33.3],PARAMETER[\"central_meridian\",2.7],PARAMETER[\"scale_factor\",0.999625769],PARAMETER[\"false_easting\",500135],PARAMETER[\"false_northing\",300090],UNIT[\"metre\",1]]",

            [22391] = "PROJCS[\"Carthage / Nord Tunisie\",GEOGCS[\"Carthage\",DATUM[\"Carthage\",SPHEROID[\"Clarke 1880 (IGN)\",6378249.2,293.4660212936265]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Lambert_Conformal_Conic_1SP\"],PARAMETER[\"latitude_of_origin\",36],PARAMETER[\"central_meridian\",9.9],PARAMETER[\"scale_factor\",0.999625544],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",300000],UNIT[\"metre\",1]]",

            [22392] = "PROJCS[\"Carthage / Sud Tunisie\",GEOGCS[\"Carthage\",DATUM[\"Carthage\",SPHEROID[\"Clarke 1880 (IGN)\",6378249.2,293.4660212936265]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Lambert_Conformal_Conic_1SP\"],PARAMETER[\"latitude_of_origin\",33.3],PARAMETER[\"central_meridian\",9.9],PARAMETER[\"scale_factor\",0.999625769],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",300000],UNIT[\"metre\",1]]",
        };

        public CoordinateSystem GetCoordinateSystem(int epsg)
        {
            return _csCache.GetOrAdd(epsg, id =>
            {
                if (!WktDefinitions.TryGetValue(id, out var wkt))
                    throw new ArgumentException($"EPSG:{id} non supporté");
                return _csFactory.CreateFromWkt(wkt);
            });
        }

        /// <summary>
        /// Convertit un point d'un système source vers un système cible
        /// </summary>
        public (double x, double y) Transform(double x, double y, int sourceEpsg, int targetEpsg)
        {
            var source = GetCoordinateSystem(sourceEpsg);
            var target = GetCoordinateSystem(targetEpsg);
            var transform = _ctFactory.CreateFromCoordinateSystems(source, target);

            // ProjNet attend (lon, lat) pour les systèmes géographiques, (x, y) pour les projetés
            double[] point = new double[] { x, y };
            double[] result = transform.MathTransform.Transform(point);
            return (result[0], result[1]);
        }

        /// <summary>
        /// Convertit un batch de points
        /// </summary>
        public List<(double x, double y)> TransformBatch(List<(double x, double y)> points, int sourceEpsg, int targetEpsg)
        {
            var source = GetCoordinateSystem(sourceEpsg);
            var target = GetCoordinateSystem(targetEpsg);
            var transform = _ctFactory.CreateFromCoordinateSystems(source, target);

            var results = new List<(double x, double y)>(points.Count);
            foreach (var pt in points)
            {
                double[] result = transform.MathTransform.Transform(new double[] { pt.x, pt.y });
                results.Add((result[0], result[1]));
            }
            return results;
        }
    }
}
