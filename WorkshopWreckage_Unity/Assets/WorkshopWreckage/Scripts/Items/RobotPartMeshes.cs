using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage
{
    /// <summary>Playtest 2026-10-29 ("the robot parts we find look very different in size and scale from the parts on
    /// the robots; the robot design is what we adhere to"): a loose robot part is the part cut out of its robot.
    /// Geometry key robot:part:&lt;unit&gt;:&lt;part key, spaces as _&gt; builds the unit's model with only that part,
    /// at rest, and merges what it draws into one mesh in model units (centred), one submesh per robot material. The
    /// item renderers draw it with those very materials (MeshLibrary.Entry.materials), so paint, wear and skins are
    /// the robot's own. The definition's mesh_scale_mm is the robot's model unit in mm (robots.json height /
    /// ModelHeight × 1000): the loose part is exactly as big as it is on the robot. The robot models are not changed.
    /// The Part Radar's parts likewise (unit part_radar, part keys as PartRadarModel's, "battery" = its cell), cut
    /// from the device at true size (mesh_scale_mm 1000); a part on a pivot of its own (the splayed antennas) is
    /// baked upright in that pivot's frame.</summary>
    public static class RobotPartMeshes
    {
        public const string Prefix = "robot:part:";

        public static bool Has(string key) => key.StartsWith(Prefix);

        public static string KeyFor(string unit, string part) => Prefix + unit + ":" + part.Replace(' ', '_');

        static readonly Dictionary<string, MeshLibrary.Entry> _baked = new();

        /// <summary>One bake per key: the robot models have one level of detail, so both LODs share it.</summary>
        public static MeshLibrary.Entry Build(string key, int lod)
        {
            if (_baked.TryGetValue(key, out var e)) return e;
            var rest = key.Substring(Prefix.Length);
            int c = rest.IndexOf(':');
            string unit = rest.Substring(0, c), part = rest.Substring(c + 1);
            if (unit != "part_radar") part = part.Replace('_', ' ');   // the radar's part keys are snake_case
            string type = unit.Length > 2 && unit[unit.Length - 2] == '_' && char.IsDigit(unit[unit.Length - 1]) ? unit.Substring(0, unit.Length - 2) : unit;
            return _baked[key] = Bake(key, unit, type, part);
        }

        static RobotModel NewModel(GameObject go, string type) => type switch
        {
            "unibot" => go.AddComponent<UnibotModel>(),
            "spiderbot" => go.AddComponent<SpiderbotModel>(),
            "caterpillarbot" => go.AddComponent<CaterpillarbotModel>(),
            "hoverbot" => go.AddComponent<HoverbotModel>(),
            "sorting_arm" => go.AddComponent<SortingArmModel>(),
            "part_radar" => go.AddComponent<PartRadarModel>(),
            _ => go.AddComponent<RetrobotModel>(),
        };

        class Piece { public Mesh mesh; public Matrix4x4 m; public Material mat; public Transform pivot; }

        static MeshLibrary.Entry Bake(string key, string unit, string type, string part)
        {
            var root = new GameObject("RobotPartBake_" + key);
            root.SetActive(false);   // never drawn: only read back
            var model = NewModel(root, type);
            if (model is HoverbotModel hm) hm.PaintFactory(unit);   // the unit's own factory skin, as Robot.Init
            if (model is PartRadarModel rm && part == "battery") rm.battery = true;   // the cell is a model of its own
            model.Init();
            model.Build(new HashSet<string> { part });
            var toRoot = root.transform.worldToLocalMatrix;
            var pieces = new List<Piece>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled || mf.sharedMesh == null || !ShownUnder(mf.transform, root.transform)) continue;   // only what the robot shows
                var mat = mr.sharedMaterial;
                // the stain decals (grease, oil, rust runs) are transparent overlays without instancing: left out
                if (mat == null || mat.shader == null || mat.shader.name.Contains("Stain") || mat.shader.name.Contains("Ghost")) continue;
                var pivot = mf.transform.parent;
                while (pivot != null && pivot.name.StartsWith("G_")) pivot = pivot.parent;
                pieces.Add(new Piece { mesh = mf.sharedMesh, m = toRoot * mf.transform.localToWorldMatrix, mat = mat, pivot = pivot });
            }
            // a radar part alone on a turned pivot (the antennas splay into a V): upright in that pivot's frame
            var pivots = pieces.Select(p => p.pivot).Distinct().ToList();
            if (type == "part_radar" && pivots.Count == 1 && pivots[0] != null && pivots[0].name.StartsWith("Antenna"))
            {
                var upright = pivots[0].worldToLocalMatrix * root.transform.localToWorldMatrix;
                foreach (var p in pieces) p.m = upright * p.m;
            }
            pieces = Cut(type, part, pieces);
            var extra = Joins(type, part, pieces);
            var cores = Cores(type, part, pieces);
            Backings(type, part, pieces, cores);
            var entry = Merge(key, pieces, extra, cores);
            if (Application.isPlaying) Object.Destroy(root); else Object.DestroyImmediate(root);
            if (entry.mesh.vertexCount == 0) Debug.LogError($"[RobotPartMeshes] {key}: the {type} model draws nothing for '{part}'");
            return entry;
        }

        static bool ShownUnder(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent) if (!t.gameObject.activeSelf) return false;
            return true;
        }

        /// <summary>Parts the robot carries in several places are one loose piece: one of a pair of sprockets, one
        /// side's road wheels, the harness bundle along the upper arm.</summary>
        static List<Piece> Cut(string type, string part, List<Piece> pieces)
        {
            Vector3 Centre(Piece p) => p.m.MultiplyPoint3x4(p.mesh.bounds.center);
            if (type == "caterpillarbot" && part.EndsWith("drive sprocket"))
            {
                var first = pieces.Select(p => p.pivot).Where(t => t != null).OrderBy(t => t.name).FirstOrDefault(t => t.name.EndsWith("F"));
                return pieces.Where(p => p.pivot == first).ToList();
            }
            if (type == "caterpillarbot" && part.StartsWith("road wheel set"))
                return pieces.Where(p => Centre(p).x < 0).ToList();
            if (type == "unibot" && part == "gyro stabilizer")   // the drums; the suspension springs drawn under its name stay with the body
            {
                var drums = pieces.GroupBy(p => p.pivot).OrderByDescending(g => BoundsOf(g.ToList()).size.z).First().Key;   // the drums are the widest
                return pieces.Where(p => p.pivot == drums).ToList();
            }
            if (type == "sorting_arm" && part == "cable harness")
            {
                var most = pieces.GroupBy(p => p.pivot).OrderByDescending(g => g.Sum(p => p.mesh.vertexCount)).First().Key;
                return pieces.Where(p => p.pivot == most).ToList();
            }
            return pieces;
        }

        /// <summary>What holds a loose set together where the robot's own frame did: a bar through one side's road
        /// wheels, the hip bar across the Retrobot's two legs. Boxes (model units) in the robot's dark steel.</summary>
        static List<(Vector3 c, Vector3 size)> Joins(string type, string part, List<Piece> pieces)
        {
            var list = new List<(Vector3, Vector3)>();
            if (pieces.Count == 0) return list;
            var b = BoundsOf(pieces);
            if (type == "caterpillarbot" && part.StartsWith("road wheel set"))
                list.Add((new Vector3(b.max.x + 0.006f, b.center.y, b.center.z), new Vector3(0.012f, 0.022f, b.size.z - 0.04f)));
            if (type == "retrobot" && part == "leg assembly")
                list.Add((new Vector3(b.center.x, b.max.y - 0.02f, b.center.z), new Vector3(b.size.x * 0.8f, 0.04f, 0.06f)));
            return list;
        }

        /// <summary>What fills a loose loop where the robot's own wheels did: the Caterpillarbot track is an open rubber
        /// band round its sprockets and road wheels, so loose it gets a solid dark track frame inside the loop (a block
        /// between the runs and a drum in each rounded end, 4 mm in from the rubber's edges). Drums along model X.</summary>
        static List<(Vector3 c, float r, float len, Vector3 axis)> Cores(string type, string part, List<Piece> pieces)
        {
            var list = new List<(Vector3, float, float, Vector3)>();
            if (pieces.Count == 0) return list;
            if (type == "hoverbot" && part == "upper saucer shell")
            {
                // the hull's open underside (the lower shell and the body closed it): a dark deck plate across it
                var sb = BoundsOf(pieces);
                float th = sb.size.y * 0.06f;
                list.Add((new Vector3(sb.center.x, sb.min.y + th * 0.5f + sb.size.y * 0.02f, sb.center.z), Mathf.Min(sb.extents.x, sb.extents.z) * 0.93f, th, Vector3.up));
                return list;
            }
            if (type == "unibot" && (part == "gyro stabilizer" || part == "wheel hub"))
            {
                // an axle through the pair (the torso held the drums, the tyre the hubs): along X through the centre of
                // the widest group (the drums, the hub discs), as long as the whole part
                var all = BoundsOf(pieces);
                var disc = pieces.GroupBy(p => p.pivot).Select(g => BoundsOf(g.ToList())).OrderByDescending(bb => bb.size.z).First();
                float rr = part == "wheel hub" ? 0.018f : 0.012f;
                list.Add((new Vector3(all.center.x, disc.center.y, disc.center.z), rr, all.size.x * 0.96f, Vector3.right));
                return list;
            }
            if (type != "caterpillarbot" || !part.EndsWith(" track")) return list;
            // CaterpillarbotModel: TrackW 0.17, EndZ 0.47, EndR 0.145, EndY 0.145, runs 0.025 thick, cleats 0.008 below
            var b = BoundsOf(pieces);
            float floor = b.min.y + 0.008f, w = 0.17f - 0.008f, inner = 0.145f - 0.025f + 0.002f;
            var c = new Vector3(b.center.x, floor + 0.145f, b.center.z);
            list.Add((c + Vector3.forward * 0.47f, inner, w, Vector3.right));
            list.Add((c - Vector3.forward * 0.47f, inner, w, Vector3.right));
            list.Add((c, -1, w, Vector3.right));   // the block between the drums (r < 0: a box 2·inner high, 2·EndZ long)
            return list;
        }

        /// <summary>Lamps that sat open-backed against the robot's shell: loose, each open rim gets a dark backing
        /// plate (a drum a little wider than the rim, behind it, away from the dome).</summary>
        static readonly HashSet<string> Backed = new() { "hoverbot:siren light", "hoverbot:rim light 1", "hoverbot:rim light 2", "hoverbot:rim light 3", "hoverbot:rim light 4" };

        static void Backings(string type, string part, List<Piece> pieces, List<(Vector3 c, float r, float len, Vector3 axis)> list)
        {
            if (!Backed.Contains(type + ":" + part) || pieces.Count == 0) return;
            if (part.StartsWith("rim light") && pieces.Select(p => p.mat).Distinct().Count() > 1)
            {
                // HoverbotModel: a brass ring with the glow dome in front of it, both facing out of the rim; the ring's
                // hole showed the dome's inside. A plate across the ring's back, as wide as the ring
                var ring = pieces.OrderByDescending(p => p.mesh.bounds.size.magnitude).First();
                Vector3 Mean(IEnumerable<Piece> ps) { var vs = ps.SelectMany(p => p.mesh.vertices.Select(x => p.m.MultiplyPoint3x4(x))).ToList(); return vs.Aggregate(Vector3.zero, (u, v) => u + v) / vs.Count; }
                var rc = Mean(new[] { ring });
                var outward = (Mean(pieces.Where(p => p.mat != ring.mat)) - rc).normalized;
                var verts = pieces.SelectMany(p => p.mesh.vertices.Select(x => p.m.MultiplyPoint3x4(x))).ToList();
                float back = verts.Min(x => Vector3.Dot(x - rc, outward));
                float rr = verts.Max(x => Vector3.ProjectOnPlane(x - rc, outward).magnitude);
                float t = rr * 0.25f;
                list.Add((rc + outward * (back + t * 0.5f - rr * 0.02f), rr, t, outward));
                return;
            }
            // welded boundary edges of all the pieces: an edge only one triangle uses lies on an open rim
            var pts = new List<Vector3>(); var ids = new Dictionary<Vector3Int, int>();
            float size = BoundsOf(pieces).size.magnitude, q = size * 1e-3f;
            var count = new Dictionary<(int, int), int>();
            foreach (var p in pieces)
            {
                var pv = p.mesh.vertices; var w = new int[pv.Length];
                for (int i = 0; i < pv.Length; i++)
                {
                    var x = p.m.MultiplyPoint3x4(pv[i]); var k = Vector3Int.RoundToInt(x / q);
                    if (!ids.TryGetValue(k, out w[i])) { w[i] = ids[k] = pts.Count; pts.Add(x); }
                }
                for (int s = 0; s < p.mesh.subMeshCount; s++)
                {
                    var tr = p.mesh.GetTriangles(s);
                    for (int i = 0; i < tr.Length; i += 3)
                        for (int e = 0; e < 3; e++)
                        {
                            int a = w[tr[i + e]], b = w[tr[i + (e + 1) % 3]];
                            if (a == b) continue;
                            var key = (Mathf.Min(a, b), Mathf.Max(a, b));
                            count[key] = count.TryGetValue(key, out var c) ? c + 1 : 1;
                        }
                }
            }
            var rim = count.Where(kv => kv.Value == 1).SelectMany(kv => new[] { kv.Key.Item1, kv.Key.Item2 }).Distinct().Select(i => pts[i]).ToList();
            if (rim.Count < 3) return;
            var centre = rim.Aggregate(Vector3.zero, (a, b) => a + b) / rim.Count;
            var nrm = Vector3.zero;   // the rim's plane: crosses of spread point pairs, kept to one sign
            for (int i = 0; i < rim.Count; i++) nrm += AbsCross(rim[i] - centre, rim[(i + rim.Count / 3) % rim.Count] - centre, nrm);
            nrm.Normalize();
            float radius = rim.Max(x => Vector3.ProjectOnPlane(x - centre, nrm).magnitude);
            var all = pieces.SelectMany(p => p.mesh.vertices.Select(x => p.m.MultiplyPoint3x4(x)));
            if (Vector3.Dot(all.Aggregate(Vector3.zero, (a, b) => a + b) / all.Count() - centre, nrm) > 0) nrm = -nrm;   // away from the dome
            float th = radius * 0.22f;
            list.Add((centre + nrm * th * 0.5f, radius * 1.1f, th, nrm));
        }

        /// <summary>Accumulates a plane normal from rim point pairs, keeping one sign.</summary>
        static Vector3 AbsCross(Vector3 a, Vector3 b, Vector3 sofar)
        {
            var c = Vector3.Cross(a, b);
            return Vector3.Dot(c, sofar) < 0 ? -c : c;
        }

        static Bounds BoundsOf(List<Piece> pieces)
        {
            Bounds? b = null;
            foreach (var p in pieces)
                foreach (var v in p.mesh.vertices)
                {
                    var w = p.m.MultiplyPoint3x4(v);
                    if (b is Bounds bb) { bb.Encapsulate(w); b = bb; } else b = new Bounds(w, Vector3.zero);
                }
            return b ?? new Bounds();
        }

        static Material _joinMat;

        static MeshLibrary.Entry Merge(string key, List<Piece> pieces, List<(Vector3 c, Vector3 size)> joins, List<(Vector3 c, float r, float len, Vector3 axis)> cores)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
            var tris = new Dictionary<Material, List<int>>();
            List<int> TrisOf(Material m) { if (!tris.TryGetValue(m, out var t)) tris[m] = t = new List<int>(); return t; }
            foreach (var p in pieces)
            {
                var mesh = p.mesh;
                var pv = mesh.vertices; var pn = mesh.normals; var puv = mesh.uv;
                var nm = p.m.inverse.transpose;
                int b0 = v.Count;
                for (int i = 0; i < pv.Length; i++)
                {
                    v.Add(p.m.MultiplyPoint3x4(pv[i]));
                    n.Add(pn.Length == pv.Length ? nm.MultiplyVector(pn[i]).normalized : Vector3.up);
                    uv.Add(puv.Length == pv.Length ? puv[i] : Vector2.zero);
                }
                bool flip = p.m.determinant < 0;   // a mirrored piece keeps its faces outward
                var t = TrisOf(p.mat);
                // the robot's shells are open surfaces (its other parts hide their insides and rims); loose, every
                // surface also gets a back: the same vertices again with turned normals, wound the other way
                int back = v.Count;
                for (int i = 0; i < pv.Length; i++) { v.Add(v[b0 + i]); n.Add(-n[b0 + i]); uv.Add(uv[b0 + i]); }
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var st = mesh.GetTriangles(s);
                    for (int i = 0; i < st.Length; i += 3)
                    {
                        t.Add(b0 + st[i]);
                        t.Add(b0 + (flip ? st[i + 2] : st[i + 1]));
                        t.Add(b0 + (flip ? st[i + 1] : st[i + 2]));
                        t.Add(back + st[i]);
                        t.Add(back + (flip ? st[i + 1] : st[i + 2]));
                        t.Add(back + (flip ? st[i + 2] : st[i + 1]));
                    }
                }
            }
            if (joins.Count > 0)
            {
                _joinMat ??= EnvMaterials.Plain(new Color(0.17f, 0.16f, 0.15f), 0.85f, 0.5f);   // RobotModel.Dark
                var t = TrisOf(_joinMat);
                foreach (var (c, size) in joins) AddBox(v, n, uv, t, c, size);
            }
            if (cores.Count > 0)
            {
                _joinMat ??= EnvMaterials.Plain(new Color(0.17f, 0.16f, 0.15f), 0.85f, 0.5f);
                var t = TrisOf(_joinMat);
                float inner = cores.Max(k => k.r);
                foreach (var (c, r, len, axis) in cores)
                    if (r < 0) AddBox(v, n, uv, t, c, new Vector3(len, inner * 2, 0.94f));
                    else AddDrum(v, n, uv, t, c, r, len, axis, 24);
            }
            // centred like every item mesh
            var bounds = new Bounds(v.Count > 0 ? v[0] : Vector3.zero, Vector3.zero);
            foreach (var p in v) bounds.Encapsulate(p);
            for (int i = 0; i < v.Count; i++) v[i] -= bounds.center;
            var outMesh = new Mesh { name = key, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            outMesh.SetVertices(v);
            outMesh.SetNormals(n);
            outMesh.SetUVs(0, uv);
            var mats = tris.Keys.ToArray();
            outMesh.subMeshCount = mats.Length;
            for (int s = 0; s < mats.Length; s++) outMesh.SetTriangles(tris[mats[s]], s, false);
            outMesh.RecalculateBounds();
            outMesh.UploadMeshData(false);
            foreach (var m in mats) m.enableInstancing = true;   // drawn with RenderMeshInstanced in the piles
            return new MeshLibrary.Entry { mesh = outMesh, slots = new int[mats.Length], bounds = outMesh.bounds, materials = mats };
        }

        /// <summary>A closed cylinder along `axis` (side faces and both caps), outward faces.</summary>
        static void AddDrum(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 c, float r, float len, Vector3 axis, int seg)
        {
            float hx = len * 0.5f;
            var turn = Quaternion.FromToRotation(Vector3.right, axis.normalized);
            Vector3 Ring(int i) { float a = i * Mathf.PI * 2 / seg; return turn * new Vector3(0, Mathf.Cos(a), Mathf.Sin(a)); }
            var Left = turn * Vector3.left; var Right = turn * Vector3.right;
            void Tri(int a, int b, int d, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[d] - v[a]), outward) > 0) { t.Add(a); t.Add(b); t.Add(d); }   // as AddBox: clockwise from outside
                else { t.Add(a); t.Add(d); t.Add(b); }
            }
            for (int i = 0; i < seg; i++)
            {
                Vector3 r0 = Ring(i), r1 = Ring(i + 1);
                int b = v.Count;
                foreach (var (p, nn) in new[] { (r0 * r + Left * hx, r0), (r1 * r + Left * hx, r1), (r1 * r + Right * hx, r1), (r0 * r + Right * hx, r0) })
                { v.Add(c + p); n.Add(nn); uv.Add(Vector2.zero); }
                var mid = (r0 + r1).normalized;
                Tri(b, b + 1, b + 2, mid); Tri(b, b + 2, b + 3, mid);
                foreach (float sg in new[] { -1f, 1f })
                {
                    int k = v.Count;
                    var nn = Right * sg;
                    v.Add(c + nn * hx); v.Add(c + nn * hx + r0 * r); v.Add(c + nn * hx + r1 * r);
                    n.Add(nn); n.Add(nn); n.Add(nn); uv.Add(Vector2.zero); uv.Add(Vector2.zero); uv.Add(Vector2.zero);
                    Tri(k, k + 1, k + 2, nn);
                }
            }
        }

        static void AddBox(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 c, Vector3 size)
        {
            var h = size * 0.5f;
            foreach (var ax in new[] { Vector3.right, Vector3.up, Vector3.forward })
                foreach (float sg in new[] { -1f, 1f })
                {
                    var nn = ax * sg;
                    var u = Vector3.Cross(nn, Mathf.Abs(nn.y) > 0.5f ? Vector3.right : Vector3.up).normalized;
                    var w = Vector3.Cross(nn, u);
                    var fc = c + Vector3.Scale(nn, h);
                    var hu = Vector3.Scale(u, h).magnitude * u; var hw = Vector3.Scale(w, h).magnitude * w;
                    int b = v.Count;
                    foreach (var (a, d) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }) { v.Add(fc + hu * a + hw * d); n.Add(nn); uv.Add(Vector2.zero); }
                    // Unity's front face is clockwise seen from outside
                    if (Vector3.Dot(Vector3.Cross(v[b + 1] - v[b], v[b + 2] - v[b]), nn) > 0) { t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3); }
                    else { t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2); }
                }
        }
    }
}
