using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage
{
    /// <summary>Skin coordinates for the industrial component skins (docs/SKIN_FIRST_COMPONENT_AUDIT.md).
    /// Computed once per mesh at load from the existing vertices: positions, triangles and
    /// normals are untouched, only two extra vertex channels are written.
    /// Triangles are grouped into surfaces (connected, same slot, smooth across shared edges):
    /// flat faces, cylinder sides and discs / rings. Per vertex:
    ///   UV1 = (u, v) on the surface: 0..1 across a flat face, (0.5, along the axis) on a
    ///         cylinder side, (radial 0 inner .. 1 rim, has-inner-edge) on a disc or ring
    ///   UV2 = (size u, size v in mesh units, kind 0 flat / 1 cylinder / 2 disc, label slot)
    /// The shader turns that into edge distance (wear at edges and seams) and label placement.
    ///   UV3 = (concave, convex) from CavityMask (art pass: wash, grime, edge wear, highlights)
    /// Label slots: 1 / 2 = the two largest visible flat faces of the part body (slot 0),
    /// 3 = the face of an existing type-plate accent (the warning-yellow plates).</summary>
    public static class SkinUV
    {
        static readonly Color PlateYellow = new(0.92f, 0.66f, 0.1f);

        class Surface { public List<int> tris = new(); public int slot; public int kind; public Vector3 n; public float area, d; public float su, sv; }

        public static void Apply(Mesh mesh, int[] slots)
        {
            var pos = mesh.vertices;
            var nrm = mesh.normals;
            var col = mesh.colors;
            int nv = pos.Length;
            var tri = new List<int>();
            var triSlot = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                tri.AddRange(t);
                for (int k = 0; k < t.Length / 3; k++) triSlot.Add(slots[s]);
            }
            int nt = tri.Count / 3;

            // weld positions so surfaces join across split vertices
            var weld = new int[nv];
            var map = new Dictionary<(int, int, int), int>();
            for (int i = 0; i < nv; i++)
            {
                var q = (Mathf.RoundToInt(pos[i].x * 20000), Mathf.RoundToInt(pos[i].y * 20000), Mathf.RoundToInt(pos[i].z * 20000));
                if (!map.TryGetValue(q, out var w)) map[q] = w = i;
                weld[i] = w;
            }

            // union triangles across shared edges where the shading is smooth (same normals)
            var parent = new int[nt];
            for (int i = 0; i < nt; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            var edges = new Dictionary<(int, int), (int t, int a, int b)>();
            for (int t = 0; t < nt; t++)
                for (int e = 0; e < 3; e++)
                {
                    int a = tri[t * 3 + e], b = tri[t * 3 + (e + 1) % 3];
                    var key = weld[a] < weld[b] ? (weld[a], weld[b]) : (weld[b], weld[a]);
                    if (!edges.TryGetValue(key, out var o)) { edges[key] = (t, a, b); continue; }
                    if (triSlot[o.t] != triSlot[t]) continue;
                    int oa = weld[o.a] == weld[a] ? o.a : o.b, ob = oa == o.a ? o.b : o.a;
                    if (Vector3.Dot(nrm[oa], nrm[a]) > 0.98f && Vector3.Dot(nrm[ob], nrm[b]) > 0.98f)
                        parent[Find(o.t)] = Find(t);
                }

            var groups = new Dictionary<int, Surface>();
            var faceN = new Vector3[nt];
            for (int t = 0; t < nt; t++)
            {
                Vector3 p0 = pos[tri[t * 3]], p1 = pos[tri[t * 3 + 1]], p2 = pos[tri[t * 3 + 2]];
                var c = Vector3.Cross(p1 - p0, p2 - p0);
                var vn = nrm[tri[t * 3]] + nrm[tri[t * 3 + 1]] + nrm[tri[t * 3 + 2]];
                if (Vector3.Dot(c, vn) < 0) c = -c;
                faceN[t] = c.sqrMagnitude > 1e-14f ? c.normalized : vn.normalized;
                int r = Find(t);
                if (!groups.TryGetValue(r, out var g)) groups[r] = g = new Surface { slot = triSlot[t] };
                g.tris.Add(t);
                g.area += c.magnitude * 0.5f;
            }

            var uv1 = new Vector2[nv];
            var uv2 = new Vector4[nv];
            for (int i = 0; i < nv; i++) { uv1[i] = new Vector2(0.5f, 0.5f); uv2[i] = new Vector4(0, 1000, 1, 0); }   // unassigned: no edges
            var verts = new List<int>();
            var seen = new HashSet<int>();
            foreach (var g in groups.Values)
            {
                verts.Clear(); seen.Clear();
                foreach (int t in g.tris) for (int k = 0; k < 3; k++) if (seen.Add(tri[t * 3 + k])) verts.Add(tri[t * 3 + k]);
                var n0 = faceN[g.tris[0]];
                bool planar = true;
                Vector3 nsum = Vector3.zero, nfar = n0;
                float farDot = 1;
                foreach (int t in g.tris)
                {
                    float d = Vector3.Dot(faceN[t], n0);
                    if (d < 0.995f) planar = false;
                    if (Mathf.Abs(d) < farDot) { farDot = Mathf.Abs(d); nfar = faceN[t]; }
                    nsum += faceN[t];
                }
                if (planar)
                {
                    var n = nsum.normalized;
                    var e1 = Mathf.Abs(n.y) > 0.7f ? Vector3.ProjectOnPlane(Vector3.right, n).normalized : Vector3.Cross(n, Vector3.up).normalized;
                    var e2 = Vector3.Cross(e1, n);
                    float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
                    foreach (int i in verts)
                    {
                        float u = Vector3.Dot(pos[i], e1), v = Vector3.Dot(pos[i], e2);
                        u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u); v0 = Mathf.Min(v0, v); v1 = Mathf.Max(v1, v);
                    }
                    float su = Mathf.Max(1e-5f, u1 - u0), sv = Mathf.Max(1e-5f, v1 - v0);
                    g.n = n; g.su = su; g.sv = sv; g.d = Vector3.Dot(pos[verts[0]], n);
                    // a disc or ring: every vertex sits at the centre, on the rim or on one inner radius
                    var c2 = new Vector2((u0 + u1) * 0.5f, (v0 + v1) * 0.5f);
                    float R = Mathf.Max(su, sv) * 0.5f, ri = -1;
                    bool round = verts.Count >= 9 && Mathf.Abs(su - sv) < 0.04f * R * 2;
                    int rim = 0;
                    if (round)
                        foreach (int i in verts)
                        {
                            float r = (new Vector2(Vector3.Dot(pos[i], e1), Vector3.Dot(pos[i], e2)) - c2).magnitude;
                            if (r > R * 0.97f) { rim++; continue; }
                            if (r < R * 0.03f) continue;
                            if (ri < 0) ri = r;
                            else if (Mathf.Abs(r - ri) > R * 0.04f) { round = false; break; }
                        }
                    if (round && rim >= 6)
                    {
                        g.kind = 2;
                        float inner = Mathf.Max(0, ri);
                        foreach (int i in verts)
                        {
                            float r = (new Vector2(Vector3.Dot(pos[i], e1), Vector3.Dot(pos[i], e2)) - c2).magnitude;
                            uv1[i] = new Vector2(Mathf.Clamp01((r - inner) / Mathf.Max(1e-5f, R - inner)), inner > 0 ? 1 : 0);
                            uv2[i] = new Vector4(R - inner, R * 2, 2, 0);
                        }
                    }
                    else
                    {
                        g.kind = 0;
                        foreach (int i in verts)
                        {
                            uv1[i] = new Vector2((Vector3.Dot(pos[i], e1) - u0) / su, (Vector3.Dot(pos[i], e2) - v0) / sv);
                            uv2[i] = new Vector4(su, sv, 0, 0);
                        }
                    }
                }
                else
                {
                    // a cylinder side (or any curved surface): edges only at the two ends of its axis
                    g.kind = 1;
                    var axis = Vector3.Cross(n0, nfar);
                    axis = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.up;
                    float v0 = float.MaxValue, v1 = float.MinValue;
                    foreach (int i in verts) { float v = Vector3.Dot(pos[i], axis); v0 = Mathf.Min(v0, v); v1 = Mathf.Max(v1, v); }
                    float sv = Mathf.Max(1e-5f, v1 - v0);
                    foreach (int i in verts)
                    {
                        uv1[i] = new Vector2(0.5f, (Vector3.Dot(pos[i], axis) - v0) / sv);
                        uv2[i] = new Vector4(0, sv, 1, 0);
                    }
                }
            }

            // label slots
            Surface best = null, second = null;
            float bestScore = 0, secondScore = 0, plateMax = 0;
            foreach (var g in groups.Values)
            {
                if (g.kind != 0) continue;
                if (g.slot == 2 && IsPlate(col, tri, g)) plateMax = Mathf.Max(plateMax, g.area);
                if (g.slot != 0 || g.n.y < -0.3f) continue;
                float aspect = Mathf.Min(g.su, g.sv) / Mathf.Max(g.su, g.sv);
                float score = g.area * (Mathf.Abs(g.n.y) > 0.7f ? 0.6f : 1f) * Mathf.Clamp01(aspect * 3f);
                // a flange under a lid: a parallel face of similar size further out hides all but its rim
                foreach (var h in groups.Values)
                    if (h != g && h.kind == 0 && h.slot == 0 && Vector3.Dot(h.n, g.n) > 0.99f && h.d > g.d + 1e-4f && h.area >= 0.6f * g.area) { score *= 0.1f; break; }
                if (score > bestScore) { second = best; secondScore = bestScore; best = g; bestScore = score; }
                else if (score > secondScore && (best == null || Mathf.Abs(Vector3.Dot(g.n, best.n)) < 0.5f)) { second = g; secondScore = score; }
            }
            if (second != null && Mathf.Abs(Vector3.Dot(second.n, best.n)) > 0.5f) second = null;   // never the opposite face: one label per view
            // a box has a twin face opposite each label face: label it too, so one always faces the camera
            Surface Twin(Surface f)
            {
                if (f == null) return null;
                foreach (var g in groups.Values)
                    if (g != f && g.kind == 0 && g.slot == 0 && Vector3.Dot(g.n, f.n) < -0.95f && Mathf.Abs(g.area - f.area) < 0.05f * f.area) return g;
                return null;
            }
            Surface bestTwin = Twin(best), secondTwin = Twin(second);
            foreach (var g in groups.Values)
            {
                int label = g == best || g == bestTwin ? 1 : g == second || g == secondTwin ? 2 : g.kind == 0 && g.slot == 2 && g.area >= plateMax * 0.3f && IsPlate(col, tri, g) ? 3 : 0;
                if (label == 0) continue;
                foreach (int t in g.tris) for (int k = 0; k < 3; k++) uv2[tri[t * 3 + k]].w = label;
            }

            // the skin channels go in a second vertex stream at half precision: the depth and shadow
            // passes never fetch them, and the forward pass reads 16 extra bytes per vertex, not 32
            var nrmArr = nrm;
            var cav = CavityMask.Compute(pos, nrm, tri, weld, mesh.bounds);   // art pass: wash / wear mask (UV3)
            var uv0 = new List<Vector2>();
            mesh.GetUVs(0, uv0);
            var layout = new List<VertexAttributeDescriptor>
            {
                new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                new(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
            };
            if (col != null && col.Length == nv) layout.Add(new(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, 0));
            if (uv0.Count == nv) layout.Add(new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0));
            layout.Add(new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float16, 2, 1));
            layout.Add(new(VertexAttribute.TexCoord2, VertexAttributeFormat.Float16, 4, 1));
            layout.Add(new(VertexAttribute.TexCoord3, VertexAttributeFormat.Float16, 2, 1));
            mesh.SetVertexBufferParams(nv, layout.ToArray());
            mesh.SetVertices(pos);
            mesh.SetNormals(nrmArr);
            if (col != null && col.Length == nv) mesh.SetColors(col);
            if (uv0.Count == nv) mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetUVs(3, cav);
        }

        static bool IsPlate(Color[] col, List<int> tri, Surface g)
        {
            if (col == null || col.Length == 0) return false;
            var c = col[tri[g.tris[0] * 3]];
            return Mathf.Abs(c.r - PlateYellow.r) + Mathf.Abs(c.g - PlateYellow.g) + Mathf.Abs(c.b - PlateYellow.b) < 0.06f;
        }
    }
}
