using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Art pass (docs/ART_DIRECTION_PASS.md): a per-vertex cavity mask for the shared item meshes,
    /// computed once per mesh at load (like SkinUV) and stored in UV3 = (concave, convex), 0..1.
    /// A vertex is concave where the geometry around it rises above its own surface (inside corners,
    /// gear roots, seams, the foot of a boss) and convex where it falls away (outer edges, tooth tips,
    /// rims, bolt heads). Two sources: the faces that share the vertex's position (welded, so hard
    /// edges count) and nearby vertices of separate pieces (a bolt head sitting on a housing).
    /// The item shader turns it into the wash, grime, edge wear and edge highlight layers.</summary>
    public static class CavityMask
    {
        public static double BakeMs;   // total bake time, logged at boot
        public static int Meshes;

        public static Vector2[] Compute(Vector3[] pos, Vector3[] nrm, List<int> tri, int[] weld, Bounds bounds)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return Bake(pos, nrm, tri, weld, bounds); }
            finally { BakeMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; Meshes++; }
        }

        static Vector2[] Bake(Vector3[] pos, Vector3[] nrm, List<int> tri, int[] weld, Bounds bounds)
        {
            int nv = pos.Length, nt = tri.Count / 3;
            var cav = new Vector2[nv];
            if (nv == 0) return cav;

            // triangles per welded position
            var byWeld = new Dictionary<int, List<int>>();
            for (int t = 0; t < nt; t++)
                for (int k = 0; k < 3; k++)
                {
                    int w = weld[tri[t * 3 + k]];
                    if (!byWeld.TryGetValue(w, out var l)) byWeld[w] = l = new List<int>(8);
                    if (l.Count == 0 || l[l.Count - 1] != t) l.Add(t);
                }

            // nearby vertices: a hash grid with cells of the search radius
            float r = Mathf.Max(bounds.size.magnitude * 0.06f, 1e-4f);
            float inv = 1f / r;
            var grid = new Dictionary<(int, int, int), List<int>>();
            (int, int, int) Cell(Vector3 p) => (Mathf.FloorToInt(p.x * inv), Mathf.FloorToInt(p.y * inv), Mathf.FloorToInt(p.z * inv));
            for (int i = 0; i < nv; i++)
                if (weld[i] == i)
                {
                    var c = Cell(pos[i]);
                    if (!grid.TryGetValue(c, out var l)) grid[c] = l = new List<int>();
                    l.Add(i);
                }

            // vertices are independent and the lookups above are read-only: big meshes bake on all cores
            void Vertex(int i, HashSet<int> linked)
            {
                var n = nrm[i];
                var p = pos[i];
                float conc = 0, conv = 0;

                // faces meeting at this position: does each rise above this vertex's surface (inside
                // corner) or fall away (outside edge)? Kept apart and taken as the strongest of each,
                // so a gear root is both (inside between the flanks, outside towards the face)
                linked.Clear();
                if (byWeld.TryGetValue(weld[i], out var faces))
                    foreach (int t in faces)
                    {
                        var cen = Vector3.zero;
                        for (int k = 0; k < 3; k++) { int v = tri[t * 3 + k]; linked.Add(weld[v]); cen += pos[v]; }
                        var d = cen / 3 - p;
                        float len = d.magnitude;
                        if (len < 1e-7f) continue;
                        float s = Vector3.Dot(n, d / len);
                        conc = Mathf.Max(conc, s); conv = Mathf.Max(conv, -s);
                    }

                // separate pieces close by (contact): weighted by distance
                var c0 = Cell(p);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (!grid.TryGetValue((c0.Item1 + dx, c0.Item2 + dy, c0.Item3 + dz), out var near)) continue;
                            foreach (int j in near)
                            {
                                if (linked.Contains(j)) continue;
                                var d = pos[j] - p;
                                float len = d.magnitude;
                                if (len < 1e-7f || len > r) continue;
                                float s = Vector3.Dot(n, d / len);
                                if (s <= 0.05f) continue;   // only what rises above the surface: contact shadow, not the far side
                                float wgt = 1 - len / r;
                                conc = Mathf.Max(conc, s * wgt);
                            }
                        }

                // a dead zone keeps smooth cylinders and faint facets neutral: only real corners, roots and contacts
                cav[i] = new Vector2(Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.12f, 0.5f, conc)),
                                     Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.15f, 0.55f, conv)));
            }
            if (nv < 128) { var linked = new HashSet<int>(); for (int i = 0; i < nv; i++) Vertex(i, linked); }
            else System.Threading.Tasks.Parallel.For(0, nv, () => new HashSet<int>(), (i, _, linked) => { Vertex(i, linked); return linked; }, _ => { });
            return cav;
        }
    }
}
