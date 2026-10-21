using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Geometry batching: primitives added in Godot-local coordinates are
    /// mirrored into Unity space and merged into one mesh per material.</summary>
    public class Batch
    {
        readonly Dictionary<Material, (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)> _parts = new();
        public readonly List<(Vector3 center, Vector3 size, Quaternion rot)> colliders = new();

        /// <summary>Godot local point -> Unity local point.</summary>
        public static Vector3 G(Vector3 p) => new Vector3(p.x, p.y, -p.z);
        public static Vector3 G(float x, float y, float z) => new Vector3(x, y, -z);
        /// <summary>Godot rotation (axis, radians) -> Unity rotation.</summary>
        public static Quaternion R(Vector3 axis, float rad) => Quaternion.AngleAxis(-rad * Mathf.Rad2Deg, new Vector3(axis.x, axis.y, -axis.z));

        (List<Vector3>, List<Vector3>, List<Vector2>, List<int>) Part(Material m)
        {
            if (!_parts.TryGetValue(m, out var p))
            {
                p = (new List<Vector3>(), new List<Vector3>(), new List<Vector2>(), new List<int>());
                _parts[m] = p;
            }
            return p;
        }

        /// <summary>Box centred at a Godot-local point, optional Godot-space rotation.</summary>
        public void Box(Vector3 centerG, Vector3 size, Material m, bool collide = false, Quaternion? rotU = null)
        {
            var rot = rotU ?? Quaternion.identity;
            var c = G(centerG);
            var (v, n, uv, t) = Part(m);
            var h = size * 0.5f;
            for (int axis = 0; axis < 3; axis++)
            {
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    var nn = Vector3.zero; nn[axis] = sgn;
                    var a1 = Vector3.zero; a1[(axis + 1) % 3] = h[(axis + 1) % 3];
                    var a2 = Vector3.zero; a2[(axis + 2) % 3] = h[(axis + 2) % 3];
                    var fc = nn * h[axis];
                    int b = v.Count;
                    Vector3[] corners = { fc - a1 - a2, fc + a1 - a2, fc + a1 + a2, fc - a1 + a2 };
                    foreach (var k in corners) { v.Add(c + rot * k); n.Add(rot * nn); uv.Add(Vector2.zero); }
                    // Unity front faces: Cross(b - a, c - a) points along the outward normal
                    var cross = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
                    if (Vector3.Dot(cross, nn) > 0) t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                    else t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                }
            }
            if (collide) colliders.Add((c, size, rot));
        }

        /// <summary>Cylinder along local Y, centred at a Godot-local point.</summary>
        public void Cylinder(Vector3 centerG, float r, float h, Material m, Quaternion? rotU = null, int segs = 16)
        {
            var rot = rotU ?? Quaternion.identity;
            var c = G(centerG);
            var (v, n, uv, t) = Part(m);
            for (int s = 0; s < segs; s++)
            {
                float a0 = Mathf.PI * 2 * s / segs, a1 = Mathf.PI * 2 * (s + 1) / segs;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                int b = v.Count;
                v.Add(c + rot * (d0 * r + Vector3.down * h / 2)); n.Add(rot * d0);
                v.Add(c + rot * (d1 * r + Vector3.down * h / 2)); n.Add(rot * d1);
                v.Add(c + rot * (d1 * r + Vector3.up * h / 2)); n.Add(rot * d1);
                v.Add(c + rot * (d0 * r + Vector3.up * h / 2)); n.Add(rot * d0);
                for (int k = 0; k < 4; k++) uv.Add(Vector2.zero);
                t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                // caps
                foreach (int cap in new[] { -1, 1 })
                {
                    int cb = v.Count;
                    var up = rot * (Vector3.up * cap);
                    v.Add(c + rot * (Vector3.up * cap * h / 2)); n.Add(up);
                    v.Add(c + rot * (d0 * r + Vector3.up * cap * h / 2)); n.Add(up);
                    v.Add(c + rot * (d1 * r + Vector3.up * cap * h / 2)); n.Add(up);
                    uv.Add(Vector2.zero); uv.Add(Vector2.zero); uv.Add(Vector2.zero);
                    if (cap > 0) t.AddRange(new[] { cb, cb + 2, cb + 1 }); else t.AddRange(new[] { cb, cb + 1, cb + 2 });
                }
            }
        }

        /// <summary>Quad facing local -Z after mirroring (i.e. Godot +Z), with UVs.</summary>
        public void Quad(Vector3 centerG, Vector2 size, Material m, Rect uvRect, Quaternion? rotU = null)
        {
            var rot = rotU ?? Quaternion.identity;
            var c = G(centerG);
            var (v, n, uv, t) = Part(m);
            int b = v.Count;
            var nrm = rot * Vector3.back;
            // the viewer stands on the -Z side looking +Z, so +X is to their right
            Vector3[] k = { new(-size.x / 2, -size.y / 2, 0), new(size.x / 2, -size.y / 2, 0), new(size.x / 2, size.y / 2, 0), new(-size.x / 2, size.y / 2, 0) };
            Vector2[] u = { new(uvRect.xMin, uvRect.yMin), new(uvRect.xMax, uvRect.yMin), new(uvRect.xMax, uvRect.yMax), new(uvRect.xMin, uvRect.yMax) };
            for (int i = 0; i < 4; i++) { v.Add(c + rot * k[i]); n.Add(nrm); uv.Add(u[i]); }
            t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }

        public void Build(Transform parent, string name, bool shadows = true, int layer = 0)
        {
            foreach (var kv in _parts)
            {
                var (v, n, uv, t) = kv.Value;
                if (t.Count == 0) continue;
                var mesh = new Mesh { name = name, indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                mesh.SetVertices(v);
                mesh.SetNormals(n);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(t, 0);
                mesh.RecalculateBounds();
                var go = new GameObject(name + "_" + kv.Key.name) { layer = layer };
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = kv.Key;
                mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (var (center, size, rot) in colliders)
            {
                var go = new GameObject("col") { layer = layer };
                go.transform.SetParent(parent, false);
                go.transform.localPosition = center;
                go.transform.localRotation = rot;
                go.AddComponent<BoxCollider>().size = size;
            }
            _parts.Clear();
            colliders.Clear();
        }
    }
}
