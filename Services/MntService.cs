using MaghrebButusAPI.Models;

namespace MaghrebButusAPI.Services
{
    // Représente un triangle par ses 3 indices dans la liste de points
    internal class Tri
    {
        public int A { get; set; }
        public int B { get; set; }
        public int C { get; set; }
        public Tri(int a, int b, int c) { A = a; B = b; C = c; }
    }

    public class MntService
    {
        public List<Triangle> Triangulate(List<PointXYZ> points)
        {
            if (points.Count < 3)
                throw new ArgumentException("Au moins 3 points requis");

            var indices = Delaunay(points);
            var triangles = new List<Triangle>(indices.Count);

            foreach (var t in indices)
            {
                triangles.Add(new Triangle
                {
                    P1 = points[t.A],
                    P2 = points[t.B],
                    P3 = points[t.C]
                });
            }

            return triangles;
        }

        // Bowyer-Watson Delaunay 2D
        private static List<Tri> Delaunay(List<PointXYZ> pts)
        {
            int n = pts.Count;

            double minX = pts[0].X, maxX = pts[0].X;
            double minY = pts[0].Y, maxY = pts[0].Y;
            foreach (var p in pts)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            double delta = Math.Max(maxX - minX, maxY - minY) * 10.0;

            // Ajouter les 3 sommets du super-triangle
            var all = new List<PointXYZ>(pts)
            {
                new PointXYZ { X = minX - delta,     Y = minY - delta,     Z = 0 },
                new PointXYZ { X = minX + 2 * delta, Y = minY - delta,     Z = 0 },
                new PointXYZ { X = minX,             Y = minY + 2 * delta, Z = 0 }
            };

            var tris = new List<Tri> { new Tri(n, n + 1, n + 2) };

            for (int pi = 0; pi < n; pi++)
            {
                var bad = new List<Tri>();
                foreach (var t in tris)
                    if (InCircumcircle(all[t.A], all[t.B], all[t.C], all[pi]))
                        bad.Add(t);

                // Trouver les arêtes frontières (non partagées)
                var boundary = new List<(int, int)>();
                foreach (var t in bad)
                {
                    AddEdgeIfUnique(boundary, t.A, t.B);
                    AddEdgeIfUnique(boundary, t.B, t.C);
                    AddEdgeIfUnique(boundary, t.C, t.A);
                }

                foreach (var t in bad) tris.Remove(t);
                foreach (var e in boundary) tris.Add(new Tri(e.Item1, e.Item2, pi));
            }

            // Garder uniquement les triangles sans sommet du super-triangle
            var result = new List<Tri>();
            foreach (var t in tris)
                if (t.A < n && t.B < n && t.C < n)
                    result.Add(t);

            return result;
        }

        private static void AddEdgeIfUnique(List<(int, int)> edges, int a, int b)
        {
            // Si l'arête inverse existe déjà, on supprime les deux (arête partagée)
            for (int i = 0; i < edges.Count; i++)
            {
                if ((edges[i].Item1 == b && edges[i].Item2 == a) ||
                    (edges[i].Item1 == a && edges[i].Item2 == b))
                {
                    edges.RemoveAt(i);
                    return;
                }
            }
            edges.Add((a, b));
        }

        private static bool InCircumcircle(PointXYZ pa, PointXYZ pb, PointXYZ pc, PointXYZ pp)
        {
            double ax = pa.X - pp.X, ay = pa.Y - pp.Y;
            double bx = pb.X - pp.X, by = pb.Y - pp.Y;
            double cx = pc.X - pp.X, cy = pc.Y - pp.Y;

            double det = ax * (by * (cx * cx + cy * cy) - cy * (bx * bx + by * by))
                       - ay * (bx * (cx * cx + cy * cy) - cx * (bx * bx + by * by))
                       + (ax * ax + ay * ay) * (bx * cy - by * cx);

            return det > 0;
        }
    }
}
