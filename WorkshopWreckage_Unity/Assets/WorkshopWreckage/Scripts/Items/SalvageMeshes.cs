using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage
{
    /// <summary>Robot-themed salvage meshes built in code (Robot Workshop redesign,
    /// docs/ROBOT_WORKSHOP_ASSET_AUDIT.md). Same conventions as the exported item meshes:
    /// unit space (about 1 unit = the part's nominal size), centred, long parts along Y;
    /// submesh slots 0 = part material, 2 = accents coloured by vertex colour (alpha 0 = no
    /// thread shading). LOD 0 for inspection, LOD 1 (fewer segments) in the world.</summary>
    public static class SalvageMeshes
    {
        static readonly Dictionary<string, Action<B>> Builders = new()
        {
            ["mach:hydraulic_actuator"] = HydraulicActuator,
            ["mach:servo_joint"] = ServoJoint,
            ["mach:drive_motor"] = DriveMotor,
            ["mach:limb_housing"] = LimbHousing,
            ["mach:chassis_bracket"] = ChassisBracket,
            // Sorting Arm unique parts (robot_parts.json, yellow enamel with red joints)
            ["robot:sa_coupler"] = b => { b.Box(new Vector3(0, -0.3f, 0), new Vector3(1f, 0.16f, 0.7f), 0, Part); b.Box(new Vector3(0, -0.14f, -0.3f), new Vector3(0.7f, 0.2f, 0.12f), 2, DarkSteel); b.Cyl(new Vector3(0, -0.22f, 0.05f), new Vector3(0, 0.2f, 0.05f), 0.24f, 2, ArmRed); foreach (float x in new[] { -0.4f, 0.4f }) b.Cyl(new Vector3(x, -0.38f, 0.2f), new Vector3(x, -0.2f, 0.2f), 0.05f, 2, Hole); b.Box(new Vector3(0.3f, -0.21f, 0.25f), new Vector3(0.1f, 0.02f, 0.06f), 2, Lens); },
            ["robot:sa_shoulder"] = b => { b.Cyl(new Vector3(0, -0.4f, 0), new Vector3(0, 0.05f, 0), 0.45f, 2, ArmRed); b.Cyl(new Vector3(0, 0.05f, 0), new Vector3(0, 0.15f, 0), 0.38f, 2, DarkSteel); b.Box(new Vector3(0, 0.3f, 0), new Vector3(0.7f, 0.3f, 0.5f), 0, Part); b.Cyl(new Vector3(-0.38f, 0.32f, 0), new Vector3(0.38f, 0.32f, 0), 0.16f, 2, Chrome); },
            ["robot:sa_upper"] = b => ArmBeam(b, 1.7f, 0.28f),
            ["robot:sa_forearm"] = b => ArmBeam(b, 1.6f, 0.22f),
            ["robot:sa_elbow"] = b => { b.Cyl(new Vector3(-0.42f, 0, 0), new Vector3(0.42f, 0, 0), 0.4f, 2, ArmRed); b.Cyl(new Vector3(-0.5f, 0, 0), new Vector3(-0.42f, 0, 0), 0.22f, 2, DarkSteel); b.Box(new Vector3(0, -0.35f, 0), new Vector3(0.5f, 0.3f, 0.4f), 0, Part); b.Box(new Vector3(0.2f, 0.25f, 0.33f), new Vector3(0.18f, 0.08f, 0.01f), 2, Warn); },
            ["robot:sa_wrist"] = b => { b.Cyl(new Vector3(0, -0.45f, 0), new Vector3(0, 0.1f, 0), 0.32f, 0, Part); b.Cyl(new Vector3(0, 0.1f, 0), new Vector3(0, 0.25f, 0), 0.4f, 2, ArmRed); b.Cyl(new Vector3(0, 0.25f, 0), new Vector3(0, 0.45f, 0), 0.25f, 2, DarkSteel); },
            ["robot:sa_finger"] = b => { b.Box(new Vector3(0, 0, 0), new Vector3(0.22f, 1.3f, 0.3f), 0, Part); b.Box(new Vector3(0.1f, 0.55f, 0), new Vector3(0.22f, 0.25f, 0.28f), 2, Rubber); b.Cyl(new Vector3(0, -0.6f, -0.17f), new Vector3(0, -0.6f, 0.17f), 0.14f, 2, ArmRed); },
            ["robot:sa_motor"] = b => { b.Cyl(new Vector3(0, -0.7f, 0), new Vector3(0, 0.5f, 0), 0.3f, 2, DarkSteel); for (int i = 0; i < 5; i++) b.Cyl(new Vector3(0, -0.5f + i * 0.2f, 0), new Vector3(0, -0.45f + i * 0.2f, 0), 0.34f, 0, Part); b.Cyl(new Vector3(0, 0.5f, 0), new Vector3(0, 0.75f, 0), 0.07f, 2, Chrome); b.Box(new Vector3(0.3f, -0.2f, 0), new Vector3(0.16f, 0.3f, 0.2f), 2, Rubber); },
            ["robot:sa_control"] = b => { b.Box(Vector3.zero, new Vector3(1f, 0.6f, 0.7f), 0, Part); b.Box(new Vector3(0, 0.05f, 0.355f), new Vector3(0.7f, 0.3f, 0.01f), 2, DarkSteel); for (int i = 0; i < 3; i++) b.Box(new Vector3(-0.2f + i * 0.2f, 0.05f, 0.362f), new Vector3(0.08f, 0.08f, 0.01f), 2, i == 1 ? Lens : ArmRed); for (int i = 0; i < 5; i++) b.Box(new Vector3(-0.4f + i * 0.2f, 0.31f, 0), new Vector3(0.06f, 0.02f, 0.6f), 2, DarkSteel); },
            ["robot:sa_sensor"] = b => { b.Box(new Vector3(0, 0, -0.1f), new Vector3(0.9f, 0.55f, 0.6f), 0, Part); foreach (float x in new[] { -0.22f, 0.22f }) { b.Cyl(new Vector3(x, 0, 0.2f), new Vector3(x, 0, 0.35f), 0.17f, 2, DarkSteel); b.Cyl(new Vector3(x, 0, 0.35f), new Vector3(x, 0, 0.38f), 0.12f, 2, Lens); } b.Box(new Vector3(0, 0.3f, -0.1f), new Vector3(0.2f, 0.08f, 0.2f), 2, ArmRed); },
            ["robot:sa_harness"] = b => { for (int i = 0; i < 9; i++) { float a0 = i * 0.7f, a1 = (i + 1) * 0.7f; var p0 = new Vector3(Mathf.Cos(a0) * 0.35f, -0.4f + i * 0.09f, Mathf.Sin(a0) * 0.35f); var p1 = new Vector3(Mathf.Cos(a1) * 0.35f, -0.4f + (i + 1) * 0.09f, Mathf.Sin(a1) * 0.35f); b.Cyl(p0, p1, 0.06f, 2, i % 3 == 0 ? ArmRed : Rubber, 8); } b.Box(new Vector3(0.35f, -0.45f, 0), new Vector3(0.2f, 0.14f, 0.14f), 0, Part); b.Box(new Vector3(0.3f, 0.45f, 0.2f), new Vector3(0.2f, 0.14f, 0.14f), 0, Part); },
        };

        static readonly Color ArmRed = new(0.7f, 0.12f, 0.08f, 0);

        /// <summary>A tapered box-section arm segment with joint bosses at both ends.</summary>
        static void ArmBeam(B b, float len, float w)
        {
            b.Box(Vector3.zero, new Vector3(w * 1.6f, len * 0.78f, w * 1.2f), 0, Part);
            foreach (float y in new[] { -len * 0.42f, len * 0.42f })
                b.Cyl(new Vector3(-w * 1.1f, y, 0), new Vector3(w * 1.1f, y, 0), w * 0.95f, 2, ArmRed);
            b.Box(new Vector3(0, 0, w * 0.61f), new Vector3(w * 0.7f, len * 0.5f, 0.01f), 2, DarkSteel);   // inspection plate
            b.Cyl(new Vector3(w * 0.9f, -len * 0.3f, 0), new Vector3(w * 0.9f, len * 0.3f, 0), 0.035f, 2, Rubber);   // cable
        }

        public static bool Has(string key) => Builders.ContainsKey(key);
        public static IEnumerable<string> Keys => Builders.Keys;

        public static MeshLibrary.Entry Build(string key, int lod)
        {
            var b = new B { seg = lod == MeshLibrary.LodInspect ? 24 : 12 };
            Builders[key](b);
            return b.Finish(key + "_lod" + lod);
        }

        // accent colours
        static readonly Color Chrome = new(0.82f, 0.83f, 0.85f, 0), Rubber = new(0.07f, 0.07f, 0.08f, 0), DarkSteel = new(0.25f, 0.26f, 0.28f, 0),
            Hole = new(0.04f, 0.04f, 0.05f, 0), Warn = new(0.92f, 0.66f, 0.1f, 0), WireRed = new(0.7f, 0.1f, 0.08f, 0), Lens = new(0.15f, 0.55f, 0.75f, 0);
        static readonly Color Part = new(1, 1, 1, 0);

        // ------------------------------------------------------------------ parts

        /// <summary>Cylinder barrel, chrome piston rod, clevis eyes at both ends, hose port.</summary>
        static void HydraulicActuator(B b)
        {
            b.Cyl(new Vector3(0, -0.62f, 0), new Vector3(0, 0.22f, 0), 0.17f, 0, Part);
            b.Cyl(new Vector3(0, -0.66f, 0), new Vector3(0, -0.56f, 0), 0.2f, 0, Part);          // end cap
            b.Cyl(new Vector3(0, 0.16f, 0), new Vector3(0, 0.26f, 0), 0.2f, 0, Part);            // gland
            b.Cyl(new Vector3(0, 0.26f, 0), new Vector3(0, 0.72f, 0), 0.065f, 2, Chrome);        // piston rod
            b.Box(new Vector3(0, -0.78f, 0), new Vector3(0.16f, 0.2f, 0.14f), 0, Part);          // rear clevis
            b.Cyl(new Vector3(-0.09f, -0.82f, 0), new Vector3(0.09f, -0.82f, 0), 0.045f, 2, Hole);
            b.Box(new Vector3(0, 0.8f, 0), new Vector3(0.12f, 0.16f, 0.12f), 2, Chrome);         // rod eye
            b.Cyl(new Vector3(-0.07f, 0.83f, 0), new Vector3(0.07f, 0.83f, 0), 0.04f, 2, Hole);
            b.Cyl(new Vector3(0, -0.45f, 0.15f), new Vector3(0, -0.45f, 0.27f), 0.05f, 2, DarkSteel);   // hose ports
            b.Cyl(new Vector3(0, 0.08f, 0.15f), new Vector3(0, 0.08f, 0.27f), 0.05f, 2, DarkSteel);
            b.Cyl(new Vector3(0, -0.45f, 0.25f), new Vector3(0, 0.08f, 0.25f), 0.03f, 2, Rubber); // hose
            for (int i = 0; i < 3; i++) b.Cyl(new Vector3(0, -0.4f + i * 0.2f, 0), new Vector3(0, -0.38f + i * 0.2f, 0), 0.178f, 2, Warn);   // stripes
        }

        /// <summary>Round servo housing with mounting ears, white output horn, cable stub.</summary>
        static void ServoJoint(B b)
        {
            b.Cyl(new Vector3(0, 0, -0.22f), new Vector3(0, 0, 0.18f), 0.36f, 0, Part);
            b.Box(new Vector3(0, -0.02f, -0.02f), new Vector3(0.98f, 0.12f, 0.3f), 0, Part);     // ears
            foreach (float x in new[] { -0.42f, 0.42f })
                b.Cyl(new Vector3(x, -0.09f, -0.02f), new Vector3(x, 0.09f, -0.02f), 0.035f, 2, Hole);
            b.Cyl(new Vector3(0, 0, 0.18f), new Vector3(0, 0, 0.24f), 0.13f, 2, DarkSteel);      // output spline
            b.Box(new Vector3(0, 0, 0.27f), new Vector3(0.62f, 0.12f, 0.05f), 2, new Color(0.9f, 0.9f, 0.86f, 0));   // horn
            b.Box(new Vector3(0, 0, 0.27f), new Vector3(0.12f, 0.5f, 0.05f), 2, new Color(0.9f, 0.9f, 0.86f, 0));
            b.Cyl(new Vector3(0, 0.3f, -0.1f), new Vector3(0, 0.46f, -0.1f), 0.06f, 2, Rubber);  // cable stub
            for (int i = 0; i < 3; i++)
                b.Cyl(new Vector3(-0.03f + i * 0.03f, 0.46f, -0.1f), new Vector3(-0.05f + i * 0.05f, 0.56f, -0.12f), 0.014f, 2, i == 0 ? WireRed : i == 1 ? Rubber : Warn);
            b.Box(new Vector3(0.2f, 0.2f, 0.185f), new Vector3(0.1f, 0.06f, 0.01f), 2, Warn);   // label
        }

        /// <summary>Finned motor can on a foot plate, dark end bells, output shaft, terminal box.</summary>
        static void DriveMotor(B b)
        {
            b.Cyl(new Vector3(-0.32f, 0, 0), new Vector3(0.3f, 0, 0), 0.32f, 0, Part);
            for (int i = 0; i < 6; i++)
            {
                float x = -0.26f + i * 0.1f;
                b.Cyl(new Vector3(x, 0, 0), new Vector3(x + 0.035f, 0, 0), 0.36f, 0, Part);   // cooling fins
            }
            b.Cyl(new Vector3(-0.42f, 0, 0), new Vector3(-0.32f, 0, 0), 0.28f, 2, DarkSteel);   // rear bell
            b.Cyl(new Vector3(0.3f, 0, 0), new Vector3(0.38f, 0, 0), 0.26f, 2, DarkSteel);     // front bell
            b.Cyl(new Vector3(0.38f, 0, 0), new Vector3(0.56f, 0, 0), 0.055f, 2, Chrome);      // shaft
            b.Box(new Vector3(0.53f, 0.05f, 0), new Vector3(0.1f, 0.02f, 0.02f), 2, DarkSteel);   // key
            b.Box(new Vector3(0.02f, -0.36f, 0), new Vector3(0.62f, 0.06f, 0.56f), 0, Part);   // foot
            b.Box(new Vector3(0.02f, 0.38f, 0), new Vector3(0.22f, 0.14f, 0.2f), 0, Part);     // terminal box
            b.Cyl(new Vector3(0.02f, 0.38f, 0.1f), new Vector3(0.02f, 0.38f, 0.2f), 0.04f, 2, Rubber);   // gland
            b.Box(new Vector3(-0.1f, 0.0f, 0.34f), new Vector3(0.16f, 0.1f, 0.01f), 2, Warn);  // rating plate
        }

        /// <summary>Pressed-steel limb shell: flat body between two joint bosses, lightening holes, bearings.</summary>
        static void LimbHousing(B b)
        {
            b.Box(new Vector3(0, 0, 0), new Vector3(0.3f, 1.3f, 0.16f), 0, Part);
            b.Box(new Vector3(0, 0, -0.06f), new Vector3(0.36f, 1.2f, 0.04f), 0, Part);        // flange
            foreach (float y in new[] { -0.7f, 0.7f })
            {
                b.Cyl(new Vector3(0, y, -0.1f), new Vector3(0, y, 0.1f), 0.2f, 0, Part);     // joint boss
                b.Cyl(new Vector3(0, y, 0.1f), new Vector3(0, y, 0.125f), 0.12f, 2, Chrome);   // bearing
                b.Cyl(new Vector3(0, y, 0.125f), new Vector3(0, y, 0.13f), 0.05f, 2, Hole);
            }
            for (int i = -1; i <= 1; i++)
                b.Cyl(new Vector3(0, i * 0.3f, 0.08f), new Vector3(0, i * 0.3f, 0.085f), 0.08f, 2, Hole);   // lightening holes
            b.Box(new Vector3(0.155f, 0, 0.04f), new Vector3(0.02f, 0.9f, 0.04f), 2, Rubber);  // cable channel
            for (int i = 0; i < 5; i++)
                foreach (float x in new[] { -0.167f, 0.167f })
                {
                    var p = new Vector3(x, -0.4f + i * 0.2f, -0.04f);
                    b.Cyl(p, p + Vector3.forward * 0.03f, 0.022f, 2, DarkSteel, 6);              // flange bolts
                }
            b.Box(new Vector3(-0.06f, 0.15f, 0.081f), new Vector3(0.12f, 0.07f, 0.004f), 2, Warn);   // service tag
        }

        /// <summary>L-shaped chassis bracket with gussets, slotted holes and bolts.</summary>
        static void ChassisBracket(B b)
        {
            b.Box(new Vector3(0, -0.39f, 0), new Vector3(1f, 0.08f, 0.6f), 0, Part);           // base plate
            b.Box(new Vector3(0, 0, -0.26f), new Vector3(1f, 0.86f, 0.08f), 0, Part);          // upright
            foreach (float x in new[] { -0.36f, 0.36f })
                b.Prism(new[] { new Vector2(-0.22f, -0.35f), new Vector2(0.28f, -0.35f), new Vector2(-0.22f, 0.3f) }, x - 0.03f, x + 0.03f, 0, Part);   // gussets (profile in ZY)
            foreach (float x in new[] { -0.15f, 0.15f })
            {
                b.Box(new Vector3(x, -0.346f, 0.1f), new Vector3(0.06f, 0.01f, 0.2f), 2, Hole);   // slots in the base
                b.Box(new Vector3(x, 0.1f, -0.216f), new Vector3(0.06f, 0.3f, 0.01f), 2, Hole);   // slots in the upright
            }
            foreach (var p in new[] { new Vector3(-0.15f, -0.35f, 0.22f), new Vector3(0.15f, -0.35f, -0.02f) })
                b.Cyl(p, p + Vector3.up * 0.05f, 0.04f, 2, DarkSteel, 6);                      // hex bolts
            b.Box(new Vector3(0.38f, 0.3f, -0.217f), new Vector3(0.14f, 0.07f, 0.01f), 2, Warn);   // stencil tag
        }

        // ------------------------------------------------------------------ builder

        public class B
        {
            public int seg = 16;
            readonly List<Vector3> _v = new(), _n = new();
            readonly List<Color> _c = new();
            readonly List<Vector2> _uv = new();
            readonly SortedDictionary<int, List<int>> _tris = new();

            int V(Vector3 p, Vector3 n, Color c)
            {
                _v.Add(p); _n.Add(n.normalized); _c.Add(c); _uv.Add(Vector2.zero);
                return _v.Count - 1;
            }

            /// <summary>Triangle facing along n (winding fixed so Unity's front face points out).</summary>
            void Tri(int slot, int a, int b, int c, Vector3 n)
            {
                if (!_tris.TryGetValue(slot, out var t)) _tris[slot] = t = new List<int>();
                if (Vector3.Dot(Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]), n) < 0) (b, c) = (c, b);
                t.Add(a); t.Add(b); t.Add(c);
            }

            void Quad(int slot, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n, Color c)
            {
                int a = V(p0, n, c), b1 = V(p1, n, c), c1 = V(p2, n, c), d = V(p3, n, c);
                Tri(slot, a, b1, c1, n); Tri(slot, a, c1, d, n);
            }

            public void Box(Vector3 c, Vector3 s, int slot, Color col)
            {
                var h = s * 0.5f;
                foreach (var ax in new[] { Vector3.right, Vector3.up, Vector3.forward })
                    foreach (float sg in new[] { -1f, 1f })
                    {
                        var n = ax * sg;
                        var u = ax == Vector3.up ? Vector3.right : Vector3.up;
                        var w = Vector3.Cross(n, u);
                        Vector3 F(float a, float b2) => c + Vector3.Scale(n, h) + Vector3.Scale(u * a + w * b2, h);
                        Quad(slot, F(-1, -1), F(1, -1), F(1, 1), F(-1, 1), n, col);
                    }
            }

            /// <summary>Capped cylinder from a to b (smooth sides, flat caps).</summary>
            public void Cyl(Vector3 a, Vector3 b, float r, int slot, Color col, int segOverride = 0)
            {
                int n = segOverride > 0 ? segOverride : seg;
                var ax = (b - a).normalized;
                var u = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                var w = Vector3.Cross(ax, u);
                for (int i = 0; i < n; i++)
                {
                    float t0 = i * Mathf.PI * 2 / n, t1 = (i + 1) * Mathf.PI * 2 / n;
                    var d0 = u * Mathf.Cos(t0) + w * Mathf.Sin(t0);
                    var d1 = u * Mathf.Cos(t1) + w * Mathf.Sin(t1);
                    int p0 = V(a + d0 * r, d0, col), p1 = V(a + d1 * r, d1, col), p2 = V(b + d1 * r, d1, col), p3 = V(b + d0 * r, d0, col);
                    var mid = (d0 + d1).normalized;
                    Tri(slot, p0, p1, p2, mid); Tri(slot, p0, p2, p3, mid);
                    int ca = V(a, -ax, col), c0 = V(a + d0 * r, -ax, col), c1 = V(a + d1 * r, -ax, col);
                    Tri(slot, ca, c0, c1, -ax);
                    int cb = V(b, ax, col), e0 = V(b + d0 * r, ax, col), e1 = V(b + d1 * r, ax, col);
                    Tri(slot, cb, e0, e1, ax);
                }
            }

            /// <summary>Convex polygon in the (z, y) plane extruded along X from x0 to x1.</summary>
            public void Prism(Vector2[] zy, float x0, float x1, int slot, Color col)
            {
                Vector3 P(Vector2 q, float x) => new(x, q.y, q.x);
                for (int i = 1; i + 1 < zy.Length; i++)
                {
                    Tri(slot, V(P(zy[0], x0), Vector3.left, col), V(P(zy[i], x0), Vector3.left, col), V(P(zy[i + 1], x0), Vector3.left, col), Vector3.left);
                    Tri(slot, V(P(zy[0], x1), Vector3.right, col), V(P(zy[i], x1), Vector3.right, col), V(P(zy[i + 1], x1), Vector3.right, col), Vector3.right);
                }
                var centre = Vector2.zero;
                foreach (var q in zy) centre += q / zy.Length;
                for (int i = 0; i < zy.Length; i++)
                {
                    var q0 = zy[i]; var q1 = zy[(i + 1) % zy.Length];
                    var e = q1 - q0;
                    var nn = new Vector2(e.y, -e.x).normalized;
                    if (Vector2.Dot(nn, (q0 + q1) * 0.5f - centre) < 0) nn = -nn;
                    var n3 = new Vector3(0, nn.y, nn.x);
                    Quad(slot, P(q0, x0), P(q1, x0), P(q1, x1), P(q0, x1), n3, col);
                }
            }

            public MeshLibrary.Entry Finish(string name)
            {
                var mesh = new Mesh { name = name };
                if (_v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
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
                // centre like the exported meshes
                var off = -mesh.bounds.center;
                if (off.sqrMagnitude > 1e-8f)
                {
                    for (int i = 0; i < _v.Count; i++) _v[i] += off;
                    mesh.SetVertices(_v);
                    mesh.RecalculateBounds();
                }
                mesh.UploadMeshData(false);
                return new MeshLibrary.Entry { mesh = mesh, slots = slots, bounds = mesh.bounds };
            }
        }
    }
}
