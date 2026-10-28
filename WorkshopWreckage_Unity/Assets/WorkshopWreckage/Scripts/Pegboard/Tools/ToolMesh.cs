using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage.Pegboard
{
    /// <summary>Mesh builder for the pegboard's hand tools (same conventions as SalvageMeshes, so a tool is an
    /// ordinary item mesh): Unity space, 1 unit = the tool's length, the tool lying in the XY plane with its
    /// length along Y and its front (the side that faces the room on the board) towards -Z; submesh slots
    /// 0 = the item's own material (coloured per definition, rusts or chips in the item shader), 2 = accents
    /// coloured by vertex colour. Outlines may be concave (ear clipping). Hook(): where the board's peg holds
    /// the tool, recorded in the same space and centred with the mesh.</summary>
    public class ToolMesh
    {
        public int seg = 16;
        /// <summary>Edge chamfer as a share of a plate's thickness (0 = hard 90 degree edges): the bevels catch the
        /// light and carry the skin's edge wear, so a cut-out reads as forged or pressed steel.</summary>
        public float bevel = 0.28f;
        readonly List<Vector3> _v = new(), _n = new();
        readonly List<Color> _c = new();
        readonly List<Vector2> _uv = new();
        readonly SortedDictionary<int, List<int>> _tris = new();
        readonly List<(Vector3 at, float kind)> _hooks = new();
        readonly Stack<Matrix4x4> _stack = new();
        Matrix4x4 _m = Matrix4x4.identity;

        public const int Own = 0, Accent = 2;

        // --- transform stack: sub-parts (a plier half, a tilted blade) are built in their own frame
        public void Push(Vector3 at, float angleDeg = 0) { _stack.Push(_m); _m = _m * Matrix4x4.TRS(at, Quaternion.Euler(0, 0, angleDeg), Vector3.one); }
        public void Push(Matrix4x4 m) { _stack.Push(_m); _m = _m * m; }
        public void Pop() => _m = _stack.Pop();

        int V(Vector3 p, Vector3 n, Color c)
        {
            _v.Add(_m.MultiplyPoint3x4(p)); _n.Add(_m.MultiplyVector(n).normalized); _c.Add(c); _uv.Add(Vector2.zero);
            return _v.Count - 1;
        }

        void Tri(int slot, int a, int b, int c, Vector3 nWorld)
        {
            if (!_tris.TryGetValue(slot, out var t)) _tris[slot] = t = new List<int>();
            if (Vector3.Dot(Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]), nWorld) < 0) (b, c) = (c, b);
            t.Add(a); t.Add(b); t.Add(c);
        }

        void Quad(int slot, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n, Color c)
        {
            int a = V(p0, n, c), b = V(p1, n, c), cc = V(p2, n, c), d = V(p3, n, c);
            var nw = _m.MultiplyVector(n);
            Tri(slot, a, b, cc, nw); Tri(slot, a, cc, d, nw);
        }

        /// <summary>A peg hook point (tool space; z is ignored, the peg comes out of the board there).
        /// kind 0 = a straight peg, 1 = a J hook (the tool hangs in its curl), 2 = a pair of rest pins.</summary>
        public void Hook(Vector2 at, float kind = 0) => _hooks.Add((_m.MultiplyPoint3x4(new Vector3(at.x, at.y, 0)), kind));

        public void Box(Vector3 c, Vector3 s, int slot, Color col, float angleDeg = 0)
        {
            if (angleDeg != 0) Push(c, angleDeg); else Push(c);
            var h = s * 0.5f;
            float cut = bevel * 0.5f * Mathf.Min(s.x, Mathf.Min(s.y, s.z));
            if (cut > 0.0012f)   // a chamfered block: the corners cut in the outline, the face edges by the plate's bevel
            {
                Plate(new[] { new Vector2(-h.x + cut, -h.y), new Vector2(h.x - cut, -h.y), new Vector2(h.x, -h.y + cut), new Vector2(h.x, h.y - cut),
                    new Vector2(h.x - cut, h.y), new Vector2(-h.x + cut, h.y), new Vector2(-h.x, h.y - cut), new Vector2(-h.x, -h.y + cut) }, -h.z, h.z, slot, col, cut);
                Pop();
                return;
            }
            foreach (var ax in new[] { Vector3.right, Vector3.up, Vector3.forward })
                foreach (float sg in new[] { -1f, 1f })
                {
                    var n = ax * sg;
                    var u = ax == Vector3.up ? Vector3.right : Vector3.up;
                    var w = Vector3.Cross(n, u);
                    Vector3 F(float a, float b) => Vector3.Scale(n, h) + Vector3.Scale(u * a + w * b, h);
                    Quad(slot, F(-1, -1), F(1, -1), F(1, 1), F(-1, 1), n, col);
                }
            Pop();
        }

        /// <summary>A frustum from a (radius ra) to b (radius rb): smooth sides, flat caps.</summary>
        public void Cone(Vector3 a, Vector3 b, float ra, float rb, int slot, Color col, int segs = 0, bool caps = true) => Cone(a, b, ra, rb, slot, col, segs, caps, caps);

        void Cone(Vector3 a, Vector3 b, float ra, float rb, int slot, Color col, int segs, bool capA, bool capB)
        {
            int n = segs > 0 ? segs : seg;
            var ax = (b - a).normalized;
            var u = Vector3.Cross(ax, Mathf.Abs(ax.z) < 0.9f ? Vector3.forward : Vector3.right).normalized;
            var w = Vector3.Cross(ax, u);
            float slope = (ra - rb) / Mathf.Max(1e-5f, (b - a).magnitude);
            for (int i = 0; i < n; i++)
            {
                float t0 = i * Mathf.PI * 2 / n, t1 = (i + 1) * Mathf.PI * 2 / n;
                var d0 = u * Mathf.Cos(t0) + w * Mathf.Sin(t0);
                var d1 = u * Mathf.Cos(t1) + w * Mathf.Sin(t1);
                var n0 = (d0 + ax * slope).normalized; var n1 = (d1 + ax * slope).normalized;
                int p0 = V(a + d0 * ra, n0, col), p1 = V(a + d1 * ra, n1, col), p2 = V(b + d1 * rb, n1, col), p3 = V(b + d0 * rb, n0, col);
                var mid = _m.MultiplyVector((d0 + d1).normalized);
                Tri(slot, p0, p1, p2, mid); Tri(slot, p0, p2, p3, mid);
                var axw = _m.MultiplyVector(ax);
                if (capA && ra > 0) { int ca = V(a, -ax, col), c0 = V(a + d0 * ra, -ax, col), c1 = V(a + d1 * ra, -ax, col); Tri(slot, ca, c0, c1, -axw); }
                if (capB && rb > 0) { int cb = V(b, ax, col), e0 = V(b + d0 * rb, ax, col), e1 = V(b + d1 * rb, ax, col); Tri(slot, cb, e0, e1, axw); }
            }
        }

        /// <summary>A cylinder with chamfered rims (a plain one when it is too small or too short to chamfer).</summary>
        public void Cyl(Vector3 a, Vector3 b, float r, int slot, Color col, int segs = 0)
        {
            float len = (b - a).magnitude, c = Mathf.Min(r * bevel * 0.6f, len * 0.2f);
            if (c < 0.0008f) { Cone(a, b, r, r, slot, col, segs); return; }
            var ax = (b - a) / len;
            Cone(a, a + ax * c, r - c, r, slot, col, segs, true, false);
            Cone(a + ax * c, b - ax * c, r, r, slot, col, segs, false, false);
            Cone(b - ax * c, b, r, r - c, slot, col, segs, false, true);
        }

        /// <summary>A turned part along +Y from `at`: profile points (radius, height), bottom to top, closed at both ends.</summary>
        public void Lathe(Vector3 at, Vector2[] profile, int slot, Color col, int segs = 0)
        {
            for (int k = 0; k + 1 < profile.Length; k++)
            {
                var p0 = profile[k]; var p1 = profile[k + 1];
                if (Mathf.Abs(p1.y - p0.y) < 1e-6f) continue;
                Cone(at + Vector3.up * p0.y, at + Vector3.up * p1.y, p0.x, p1.x, slot, col, segs, k == 0 || k == profile.Length - 2);
            }
        }

        /// <summary>A flat plate: any simple outline in the XY plane (either winding), from z0 to z1. The edges are
        /// chamfered front and back (`chamfer` in mesh units, default `bevel` x thickness); an outline too thin to take
        /// the chamfer gets a smaller one, or square edges. Side walls and chamfers shade smoothly round curves
        /// (wrench heads, jaws) and keep a crisp line at corners.</summary>
        public void Plate(IList<Vector2> outline, float z0, float z1, int slot, Color col, float chamfer = -1)
        {
            var poly = new List<Vector2>();
            foreach (var q in outline) if (poly.Count == 0 || (q - poly[^1]).sqrMagnitude > 1e-10f) poly.Add(q);
            if (poly.Count > 2 && (poly[0] - poly[^1]).sqrMagnitude <= 1e-10f) poly.RemoveAt(poly.Count - 1);
            if (poly.Count < 3) return;
            if (Area(poly) < 0) poly.Reverse();   // counter-clockwise
            var tris = Triangulate(poly);
            int n = poly.Count;

            // in-plane outward normals: per edge, and per vertex smoothed where the outline turns gently
            var en = new Vector2[n];
            for (int i = 0; i < n; i++) { var e = (poly[(i + 1) % n] - poly[i]).normalized; en[i] = new Vector2(e.y, -e.x); }
            var vn = new Vector2[n];
            var smooth = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var np = en[(i + n - 1) % n]; var nx = en[i];
                smooth[i] = Vector2.Dot(np, nx) > 0.8f;   // under ~37 degrees: a curve, not a corner
                vn[i] = (np + nx).normalized;
            }

            float thick = z1 - z0;
            float c = chamfer >= 0 ? chamfer : bevel * thick;
            c = Mathf.Min(c, thick * 0.45f);
            List<Vector2> inset = null;
            for (int tries = 0; tries < 3 && c > 0.0004f; tries++, c *= 0.5f)
                if ((inset = Inset(poly, en, c)) != null) break;
            if (inset == null) c = 0;

            var back = new Vector3(0, 0, 1);
            var bw = _m.MultiplyVector(back);
            var face = c > 0 ? inset : poly;
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector2 a = face[tris[i]], b = face[tris[i + 1]], cc = face[tris[i + 2]];
                Tri(slot, V(new Vector3(a.x, a.y, z0), -back, col), V(new Vector3(b.x, b.y, z0), -back, col), V(new Vector3(cc.x, cc.y, z0), -back, col), -bw);
                Tri(slot, V(new Vector3(a.x, a.y, z1), back, col), V(new Vector3(b.x, b.y, z1), back, col), V(new Vector3(cc.x, cc.y, z1), back, col), bw);
            }
            float w0 = z0 + c, w1 = z1 - c;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector2 q0 = poly[i], q1 = poly[j];
                Vector3 N(int k) { var v2 = smooth[k] ? vn[k] : en[i]; return new Vector3(v2.x, v2.y, 0); }
                var n0 = N(i); var n1 = N(j);
                var nf = new Vector3(en[i].x, en[i].y, 0);
                Strip4(slot, new Vector3(q0.x, q0.y, w0), new Vector3(q1.x, q1.y, w0), new Vector3(q1.x, q1.y, w1), new Vector3(q0.x, q0.y, w1), n0, n1, n1, n0, nf, col);
                if (c <= 0) continue;
                Vector2 i0 = inset[i], i1 = inset[j];
                var f0 = (n0 - back).normalized; var f1 = (n1 - back).normalized;   // front chamfer (-Z faces the room)
                Strip4(slot, new Vector3(i0.x, i0.y, z0), new Vector3(i1.x, i1.y, z0), new Vector3(q1.x, q1.y, w0), new Vector3(q0.x, q0.y, w0), f0, f1, f1, f0, (nf - back).normalized, col);
                var k0 = (n0 + back).normalized; var k1 = (n1 + back).normalized;
                Strip4(slot, new Vector3(q0.x, q0.y, w1), new Vector3(q1.x, q1.y, w1), new Vector3(i1.x, i1.y, z1), new Vector3(i0.x, i0.y, z1), k0, k1, k1, k0, (nf + back).normalized, col);
            }
        }

        /// <summary>A quad with its own normal per corner (smooth shading round a curve); `nFace` orders the winding.</summary>
        void Strip4(int slot, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n0, Vector3 n1, Vector3 n2, Vector3 n3, Vector3 nFace, Color col)
        {
            int a = V(p0, n0, col), b = V(p1, n1, col), cc = V(p2, n2, col), d = V(p3, n3, col);
            var nw = _m.MultiplyVector(nFace);
            Tri(slot, a, b, cc, nw); Tri(slot, a, cc, d, nw);
        }

        /// <summary>The outline moved in by d (mitred corners, clamped at sharp ones), or null when that would fold it:
        /// an edge flipping over or the area collapsing means a part of the outline is thinner than 2d.</summary>
        static List<Vector2> Inset(List<Vector2> poly, Vector2[] en, float d)
        {
            int n = poly.Count;
            var r = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                var np = en[(i + n - 1) % n]; var nx = en[i];
                var bis = (np + nx);
                if (bis.sqrMagnitude < 1e-8f) bis = new Vector2(-np.y, np.x);
                bis.Normalize();
                float m = d / Mathf.Max(0.4f, Vector2.Dot(bis, nx));
                r.Add(poly[i] - bis * m);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var eo = poly[j] - poly[i]; var ei = r[j] - r[i];
                if (Vector2.Dot(eo, ei) < 0) return null;
            }
            float a0 = Area(poly), a1 = Area(r);
            return a1 > a0 * 0.35f ? r : null;
        }

        /// <summary>A flat ring (washer shape) in the XY plane: a wrench's box end, a clamp's eye. Both rims are
        /// chamfered front and back (bevel x thickness, at most a third of the ring's width).</summary>
        public void Ring(Vector2 c, float rOut, float rIn, float z0, float z1, int slot, Color col, int segs = 0)
        {
            int n = segs > 0 ? segs : seg;
            float ch = Mathf.Min(bevel * (z1 - z0), (rOut - rIn) * 0.3f);
            if (ch < 0.0004f) ch = 0;
            var cc = new Vector3(c.x, c.y, 0);
            var fw = Vector3.forward;
            for (int i = 0; i < n; i++)
            {
                float t0 = i * Mathf.PI * 2 / n, t1 = (i + 1) * Mathf.PI * 2 / n;
                var d0 = new Vector3(Mathf.Cos(t0), Mathf.Sin(t0), 0); var d1 = new Vector3(Mathf.Cos(t1), Mathf.Sin(t1), 0);
                var m = (d0 + d1).normalized;
                Vector3 P(Vector3 d, float r, float z) => cc + d * r + fw * z;
                foreach (var (z, nz) in new[] { (z0, -1f), (z1, 1f) })
                    Quad(slot, P(d0, rOut - ch, z), P(d1, rOut - ch, z), P(d1, rIn + ch, z), P(d0, rIn + ch, z), new Vector3(0, 0, nz), col);
                float w0 = z0 + ch, w1 = z1 - ch;
                Strip4(slot, P(d0, rOut, w0), P(d1, rOut, w0), P(d1, rOut, w1), P(d0, rOut, w1), d0, d1, d1, d0, m, col);
                Strip4(slot, P(d0, rIn, w0), P(d1, rIn, w0), P(d1, rIn, w1), P(d0, rIn, w1), -d0, -d1, -d1, -d0, -m, col);
                if (ch <= 0) continue;
                foreach (var (zf, zw, s) in new[] { (z0, w0, -1f), (z1, w1, 1f) })
                {
                    var z = fw * s;
                    Strip4(slot, P(d0, rOut - ch, zf), P(d1, rOut - ch, zf), P(d1, rOut, zw), P(d0, rOut, zw), (d0 + z).normalized, (d1 + z).normalized, (d1 + z).normalized, (d0 + z).normalized, (m + z).normalized, col);
                    Strip4(slot, P(d0, rIn + ch, zf), P(d1, rIn + ch, zf), P(d1, rIn, zw), P(d0, rIn, zw), (z - d0).normalized, (z - d1).normalized, (z - d1).normalized, (z - d0).normalized, (z - m).normalized, col);
                }
            }
        }

        /// <summary>A plate with a straight centre line from a to b, width wa at a and wb at b (shanks, blades, handles).</summary>
        public void Strip(Vector2 a, Vector2 b, float wa, float wb, float z0, float z1, int slot, Color col)
        {
            var d = (b - a).normalized; var s = new Vector2(-d.y, d.x);
            Plate(new[] { a - s * wa / 2, b - s * wb / 2, b + s * wb / 2, a + s * wa / 2 }, z0, z1, slot, col);
        }

        /// <summary>Points on an arc in the XY plane (radians, counter-clockwise from +X).</summary>
        public static IEnumerable<Vector2> Arc(Vector2 c, float r, float a0, float a1, int n)
        {
            for (int i = 0; i <= n; i++) { float t = Mathf.Lerp(a0, a1, i / (float)n); yield return c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r; }
        }

        static float Area(List<Vector2> p)
        {
            float a = 0;
            for (int i = 0; i < p.Count; i++) { var q = p[i]; var r = p[(i + 1) % p.Count]; a += q.x * r.y - r.x * q.y; }
            return a * 0.5f;
        }

        /// <summary>Ear clipping of a counter-clockwise simple polygon; indices into it.</summary>
        static List<int> Triangulate(List<Vector2> p)
        {
            var idx = new List<int>();
            for (int i = 0; i < p.Count; i++) idx.Add(i);
            var res = new List<int>();
            int guard = p.Count * p.Count + 10;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector2 a = p[ia], b = p[ib], c = p[ic];
                    if ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) <= 1e-9f) continue;   // reflex or flat
                    bool inside = false;
                    foreach (int j in idx)
                    {
                        if (j == ia || j == ib || j == ic) continue;
                        if (InTri(p[j], a, b, c)) { inside = true; break; }
                    }
                    if (inside) continue;
                    res.Add(ia); res.Add(ib); res.Add(ic);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;   // degenerate outline: fan the rest
            }
            for (int i = 1; i + 1 < idx.Count; i++) { res.Add(idx[0]); res.Add(idx[i]); res.Add(idx[i + 1]); }
            return res;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        /// <summary>The finished tool: an item mesh entry, centred like every item mesh, and its hooks.</summary>
        public (MeshLibrary.Entry entry, List<(Vector3 at, float kind)> hooks) Finish(string name)
        {
            var mesh = new Mesh { name = name };
            if (_v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_v);
            mesh.RecalculateBounds();
            var off = -mesh.bounds.center;
            for (int i = 0; i < _v.Count; i++) _v[i] += off;
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetUVs(0, _uv);
            mesh.SetColors(_c);
            mesh.subMeshCount = _tris.Count;
            var slots = new int[_tris.Count];
            int sub = 0;
            foreach (var kv in _tris)
            {
                mesh.SetTriangles(kv.Value, sub, false);
                slots[sub++] = kv.Key;
            }
            mesh.RecalculateBounds();
            SkinUV.Apply(mesh, slots);
            mesh.UploadMeshData(false);
            var hooks = new List<(Vector3, float)>();
            foreach (var (at, kind) in _hooks) hooks.Add((at + off, kind));
            return (new MeshLibrary.Entry { mesh = mesh, slots = slots, bounds = mesh.bounds }, hooks);
        }
    }
}
