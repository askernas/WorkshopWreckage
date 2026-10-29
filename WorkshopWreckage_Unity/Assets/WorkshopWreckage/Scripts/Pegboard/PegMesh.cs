using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage.Pegboard
{
    /// <summary>Static geometry of the board, one mesh per material, in the board's own frame (Unity space: x right,
    /// y up, -z into the room). Boxes, cylinders and raw quads with UVs (the panel); Build() makes one renderer per
    /// material under a parent.</summary>
    public class PegMesh
    {
        class Part { public readonly List<Vector3> v = new(), n = new(); public readonly List<Vector2> uv = new(); public readonly List<int> t = new(); }
        readonly Dictionary<Material, Part> _parts = new();

        Part P(Material m) { if (!_parts.TryGetValue(m, out var p)) _parts[m] = p = new Part(); return p; }

        public int Vertices { get { int c = 0; foreach (var p in _parts.Values) c += p.v.Count; return c; } }

        /// <summary>A quad a-b-c-d (any winding; it is turned to face along n).</summary>
        public void Quad(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2? ua = null, Vector2? ub = null, Vector2? uc = null, Vector2? ud = null)
        {
            var p = P(m);
            int i = p.v.Count;
            p.v.Add(a); p.v.Add(b); p.v.Add(c); p.v.Add(d);
            for (int k = 0; k < 4; k++) p.n.Add(n);
            p.uv.Add(ua ?? Vector2.zero); p.uv.Add(ub ?? Vector2.zero); p.uv.Add(uc ?? Vector2.zero); p.uv.Add(ud ?? Vector2.zero);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0) { p.t.Add(i); p.t.Add(i + 1); p.t.Add(i + 2); p.t.Add(i); p.t.Add(i + 2); p.t.Add(i + 3); }   // Unity: Cross(b-a, c-a) is the front normal
            else { p.t.Add(i); p.t.Add(i + 2); p.t.Add(i + 1); p.t.Add(i); p.t.Add(i + 3); p.t.Add(i + 2); }
        }

        /// <summary>A box (centre, size), optionally turned about z (degrees).</summary>
        public void Box(Material m, Vector3 c, Vector3 s, float angleZ = 0, bool skipBack = true)
        {
            var r = Quaternion.Euler(0, 0, angleZ);
            var h = s * 0.5f;
            foreach (var ax in new[] { Vector3.right, Vector3.up, Vector3.forward })
                foreach (float sg in new[] { -1f, 1f })
                {
                    if (skipBack && ax == Vector3.forward && sg > 0) continue;   // the side towards the wall is never seen
                    var n = ax * sg;
                    var u = ax == Vector3.up ? Vector3.right : Vector3.up;
                    var w = Vector3.Cross(n, u);
                    Vector3 F(float a, float b) => c + r * (Vector3.Scale(n, h) + Vector3.Scale(u * a + w * b, h));
                    Quad(m, F(-1, -1), F(1, -1), F(1, 1), F(-1, 1), r * n);
                }
        }

        /// <summary>A cylinder from a to b (radius r), with caps.</summary>
        public void Cyl(Material m, Vector3 a, Vector3 b, float r, int segs = 8, bool caps = true)
        {
            var ax = (b - a).normalized;
            var u = Vector3.Cross(ax, Mathf.Abs(ax.z) < 0.9f ? Vector3.forward : Vector3.right).normalized;
            var w = Vector3.Cross(ax, u);
            for (int i = 0; i < segs; i++)
            {
                float t0 = i * Mathf.PI * 2 / segs, t1 = (i + 1) * Mathf.PI * 2 / segs;
                var d0 = u * Mathf.Cos(t0) + w * Mathf.Sin(t0); var d1 = u * Mathf.Cos(t1) + w * Mathf.Sin(t1);
                var p = P(m);
                int k = p.v.Count;
                p.v.Add(a + d0 * r); p.v.Add(a + d1 * r); p.v.Add(b + d1 * r); p.v.Add(b + d0 * r);
                p.n.Add(d0); p.n.Add(d1); p.n.Add(d1); p.n.Add(d0);
                for (int q = 0; q < 4; q++) p.uv.Add(Vector2.zero);
                var mid = (d0 + d1).normalized;
                if (Vector3.Dot(Vector3.Cross(p.v[k + 1] - p.v[k], p.v[k + 2] - p.v[k]), mid) >= 0) { p.t.Add(k); p.t.Add(k + 1); p.t.Add(k + 2); p.t.Add(k); p.t.Add(k + 2); p.t.Add(k + 3); }
                else { p.t.Add(k); p.t.Add(k + 2); p.t.Add(k + 1); p.t.Add(k); p.t.Add(k + 3); p.t.Add(k + 2); }
                if (!caps) continue;
                Tri(m, b, b + d0 * r, b + d1 * r, ax);
                Tri(m, a, a + d1 * r, a + d0 * r, -ax);
            }
        }

        public void Tri(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 n, Vector2? ua = null, Vector2? ub = null, Vector2? uc = null)
        {
            var p = P(m);
            int i = p.v.Count;
            p.v.Add(a); p.v.Add(b); p.v.Add(c);
            p.n.Add(n); p.n.Add(n); p.n.Add(n);
            p.uv.Add(ua ?? Vector2.zero); p.uv.Add(ub ?? Vector2.zero); p.uv.Add(uc ?? Vector2.zero);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0) { p.t.Add(i); p.t.Add(i + 1); p.t.Add(i + 2); }
            else { p.t.Add(i); p.t.Add(i + 2); p.t.Add(i + 1); }
        }

        /// <summary>A round-headed bolt on a face whose normal is -z (heads towards the room), with a washer.</summary>
        public void Bolt(Material m, Vector3 at, float r)
        {
            Cyl(m, at, at + new Vector3(0, 0, -r * 0.25f), r * 1.35f, 10);
            Cyl(m, at + new Vector3(0, 0, -r * 0.25f), at + new Vector3(0, 0, -r * 0.8f), r, 6);   // hex head
        }

        public List<GameObject> Build(Transform parent, string name, bool shadows, int layer)
        {
            var made = new List<GameObject>();
            foreach (var kv in _parts)
            {
                var p = kv.Value;
                if (p.t.Count == 0) continue;
                var mesh = new Mesh { name = name + "_" + kv.Key.name, indexFormat = p.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(p.v);
                mesh.SetNormals(p.n);
                mesh.SetUVs(0, p.uv);
                mesh.SetTriangles(p.t, 0);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                var go = new GameObject(name + "_" + kv.Key.name) { layer = layer };
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = kv.Key;
                mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                made.Add(go);
            }
            _parts.Clear();
            return made;
        }
    }
}
