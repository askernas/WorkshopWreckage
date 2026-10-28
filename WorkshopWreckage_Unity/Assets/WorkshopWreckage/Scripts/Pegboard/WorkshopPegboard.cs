using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage.Pegboard
{
    /// <summary>The interactive tool pegboard (docs/PEGBOARD.md): one root transform (on the wall surface, bottom
    /// centre of the frame; move, turn or scale it freely) with the steel frame, the perforated panel with real
    /// holes and the painted tool silhouettes, three light bars, hooks and racks, and the tools that hang on it.
    /// Everything comes from PegboardConfig. Hung tools are drawn here, merged per material and rebuilt only when
    /// one is hung or taken (no per-tool objects or update loops); the game decides what is hung (Install/Remove).</summary>
    public class WorkshopPegboard : MonoBehaviour
    {
        public PegboardConfig cfg;
        public PegboardMaterials mats;
        public int layer;
        readonly Dictionary<string, Matrix4x4> _slotLocal = new();       // board frame, snapped to the hole grid
        readonly Dictionary<string, ItemDefinition> _installed = new();  // slot -> the tool hung there
        readonly Dictionary<string, Bounds> _slotBounds = new();          // board frame, for aiming
        Transform _toolsRoot, _highlightRoot;
        bool _dirty;
        string _highlightSlot;
        Material _highlightMat;
        MeshRenderer _highlightRenderer;
        public readonly List<Light> lights = new();

        public static WorkshopPegboard Create(Transform parent, Vector3 localPos, Quaternion localRot, PegboardConfig cfg, int layer = 0)
        {
            var go = new GameObject("WorkshopPegboard") { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var pb = go.AddComponent<WorkshopPegboard>();
            pb.cfg = cfg;
            pb.layer = layer;
            pb.Build();
            return pb;
        }

        // ------------------------------------------------------------------ queries

        public int Total => cfg.slots.Count;
        public int Hung => _installed.Count;
        public int Missing => Total - Hung;
        public float Percent => Total == 0 ? 0 : Hung * 100f / Total;
        public bool IsOccupied(string slot) => _installed.ContainsKey(slot);
        public IEnumerable<string> Occupied => _installed.Keys;

        /// <summary>World matrix of a tool hung in a slot (scale included).</summary>
        public Matrix4x4 SlotWorld(string slot) => transform.localToWorldMatrix * _slotLocal[slot];
        public Vector3 SlotCentre(string slot) => SlotWorld(slot).GetColumn(3);

        /// <summary>The slot whose painted outline (grown by aim_radius) the ray passes through, nearest first; or null.
        /// `prefer` (the slot of the tool in hand) wins whenever it is within reach of the crosshair.</summary>
        public string SlotUnderRay(Ray worldRay, float maxDist, out float dist, string prefer = null)
        {
            dist = 0;
            var lr = new Ray(transform.InverseTransformPoint(worldRay.origin), transform.InverseTransformDirection(worldRay.direction).normalized);
            float pz = cfg.PanelFront;
            if (Mathf.Abs(lr.direction.z) < 1e-4f) return null;
            float t = (pz - lr.origin.z) / lr.direction.z;
            if (t < 0 || t > maxDist / Mathf.Max(1e-4f, transform.lossyScale.x)) return null;
            var hit = lr.GetPoint(t);
            float grow = cfg.F("interaction", "aim_radius", 0.16f);
            string best = null; float bestD = float.MaxValue;
            foreach (var kv in _slotBounds)
            {
                var b = kv.Value;
                float dx = Mathf.Max(0, Mathf.Abs(hit.x - b.center.x) - b.extents.x), dy = Mathf.Max(0, Mathf.Abs(hit.y - b.center.y) - b.extents.y);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > grow) continue;
                // inside the outline beats close to it; among those the nearest centre wins
                float score = kv.Key == prefer ? -1 : d * 10 + Vector2.Distance(hit, b.center);
                if (score < bestD) { bestD = score; best = kv.Key; }
            }
            dist = t * transform.lossyScale.x;
            return best;
        }

        // ------------------------------------------------------------------ state

        public void Install(string slot, ItemDefinition def)
        {
            if (!_slotLocal.ContainsKey(slot)) return;
            _installed[slot] = def;
            _dirty = true;
        }

        public void Remove(string slot)
        {
            if (_installed.Remove(slot)) _dirty = true;
        }

        public void Clear() { _installed.Clear(); _dirty = true; }

        /// <summary>QA / preview: hang a deterministic fraction of the tools (0..1), the rest left empty.</summary>
        public void PreviewFill(float frac)
        {
            Clear();
            var errors = new List<string>();
            var order = new List<PegboardConfig.Slot>(cfg.slots);
            order.Sort((a, b) => Hash(a.id).CompareTo(Hash(b.id)));
            int n = Mathf.RoundToInt(Mathf.Clamp01(frac) * order.Count);
            for (int i = 0; i < n; i++)
            {
                var t = cfg.toolById[order[i].tool];
                var def = ItemDefinition.FromDict(cfg.ItemDict(t), 1, errors);
                if (def != null) Install(order[i].id, def);
            }
            foreach (var e in errors) Debug.LogWarning("[pegboard] " + e);
        }

        static uint Hash(string s) { uint h = 2166136261; foreach (char c in s) h = (h ^ c) * 16777619; return h; }

        /// <summary>The highlighted slot (the player carries its tool and aims near it), or null.</summary>
        public void Highlight(string slot)
        {
            if (slot == _highlightSlot) return;
            _highlightSlot = slot;
            if (_highlightRenderer != null) Destroy(_highlightRenderer.gameObject);
            _highlightRenderer = null;
            if (slot == null || !_slotLocal.ContainsKey(slot)) return;
            var mesh = FlatMesh(slot, 0.006f);
            var go = new GameObject("Highlight_" + slot) { layer = layer };
            go.transform.SetParent(_highlightRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            _highlightRenderer = go.AddComponent<MeshRenderer>();
            _highlightRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _highlightMat ??= HighlightMaterial();
            _highlightRenderer.sharedMaterial = _highlightMat;
        }

        Material HighlightMaterial()
        {
            var m = PegboardMaterials.Make("peg_highlight", null, 1, 0, 0.2f, cfg.C("highlight", "color", new Color(1f, 0.82f, 0.48f)));
            m.SetFloat("_Emission", 0);
            return m;
        }

        void Update()
        {
            if (_highlightRenderer != null && _highlightMat != null)
            {
                float hz = cfg.F("highlight", "pulse_hz", 1.6f), k = cfg.F("highlight", "intensity", 0.55f);
                _highlightMat.SetFloat("_Emission", k * (0.6f + 0.4f * Mathf.Sin(Time.time * hz * Mathf.PI * 2)));
            }
        }

        void LateUpdate()
        {
            if (!_dirty) return;
            _dirty = false;
            RebuildTools();
        }

        // ------------------------------------------------------------------ building

        public void Build()
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            lights.Clear();
            _slotLocal.Clear();
            _slotBounds.Clear();
            PlaceSlots();
            var sil = new List<(Vector2[] tri, float shade)>();
            foreach (var s in cfg.slots) Silhouette(s, sil);
            mats = new PegboardMaterials(cfg, sil);
            var geo = new PegMesh();
            Panel(geo);
            geo.Build(transform, "PegboardPanel", false, layer);   // flat on the wall: its shadow can't be seen, and it is most of the triangles
            Frame(geo);
            LightBars(geo);
            Hooks(geo);
            Racks(geo);
            geo.Build(transform, "Pegboard", true, layer);
            _toolsRoot = new GameObject("HungTools") { layer = layer }.transform;
            _toolsRoot.SetParent(transform, false);
            _highlightRoot = new GameObject("Highlight") { layer = layer }.transform;
            _highlightRoot.SetParent(transform, false);
            _dirty = true;
        }

        /// <summary>Each slot's tool transform, moved so its first hook sits on a hole of the grid.</summary>
        void PlaceSlots()
        {
            float pitch = cfg.F("panel", "hole_pitch", 0.052f);
            var (ox, oy) = PegboardMaterials.HoleOrigin(cfg);
            var pc = cfg.PanelCentre;
            float left = pc.x - cfg.PanelW / 2, bottom = pc.y - cfg.PanelH / 2;
            foreach (var s in cfg.slots)
            {
                var t = cfg.toolById[s.tool];
                var m = cfg.SlotMatrix(s, t.size);
                var hooks = ToolFactory.Hooks(t.GeometryKey);
                if (hooks.Count > 0)
                {
                    var h = m.MultiplyPoint3x4(hooks[0].at);
                    float hx = left + ox + Mathf.Round((h.x - left - ox) / pitch) * pitch;
                    float hy = bottom + oy + Mathf.Round((h.y - bottom - oy) / pitch) * pitch;
                    m = Matrix4x4.Translate(new Vector3(hx - h.x, hy - h.y, 0)) * m;
                }
                _slotLocal[s.id] = m;
                var ub = MeshLibrary.UnitBounds(t.GeometryKey);
                var b = new Bounds(m.MultiplyPoint3x4(ub.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                    b.Encapsulate(m.MultiplyPoint3x4(ub.center + Vector3.Scale(ub.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1))));
                _slotBounds[s.id] = b;
            }
        }

        /// <summary>The tool's outline on the panel: its front-facing triangles flattened, in panel UV space.</summary>
        void Silhouette(PegboardConfig.Slot s, List<(Vector2[] tri, float shade)> into)
        {
            var key = cfg.toolById[s.tool].GeometryKey;
            var e = MeshLibrary.Get(key, MeshLibrary.LodWorld);
            var m = _slotLocal[s.id];
            var v = e.mesh.vertices;
            var pc = cfg.PanelCentre;
            float left = pc.x - cfg.PanelW / 2, bottom = pc.y - cfg.PanelH / 2;
            Vector2 UV(Vector3 p) { var q = m.MultiplyPoint3x4(p); return new Vector2((q.x - left) / cfg.PanelW, (q.y - bottom) / cfg.PanelH); }
            for (int sm = 0; sm < e.mesh.subMeshCount; sm++)
            {
                var tris = e.mesh.GetTriangles(sm);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var a = UV(v[tris[i]]); var b = UV(v[tris[i + 1]]); var c = UV(v[tris[i + 2]]);
                    into.Add((new[] { a, b, c }, 1));
                }
            }
        }

        /// <summary>A flat copy of the tool's outline just in front of the panel (the highlight).</summary>
        Mesh FlatMesh(string slot, float lift)
        {
            var key = cfg.toolById[cfg.slotById[slot].tool].GeometryKey;
            var e = MeshLibrary.Get(key, MeshLibrary.LodWorld);
            var m = _slotLocal[slot];
            var src = e.mesh.vertices;
            var verts = new List<Vector3>(src.Length);
            float z = cfg.PanelFront - lift;
            foreach (var p in src) { var q = m.MultiplyPoint3x4(p); verts.Add(new Vector3(q.x, q.y, z)); }
            var tris = new List<int>();
            for (int sm = 0; sm < e.mesh.subMeshCount; sm++)
            {
                var t = e.mesh.GetTriangles(sm);
                for (int i = 0; i < t.Length; i += 3)
                {
                    var a = verts[t[i]]; var b = verts[t[i + 1]]; var c = verts[t[i + 2]];
                    float cz = Vector3.Cross(b - a, c - a).z;
                    if (Mathf.Abs(cz) < 1e-9f) continue;
                    if (cz < 0) { tris.Add(t[i]); tris.Add(t[i + 1]); tris.Add(t[i + 2]); }   // front normal is -z
                    else { tris.Add(t[i]); tris.Add(t[i + 2]); tris.Add(t[i + 1]); }
                }
            }
            var mesh = new Mesh { name = "peg_flat_" + slot, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            var n = new Vector3[verts.Count];
            for (int i = 0; i < n.Length; i++) n[i] = Vector3.back;
            mesh.SetNormals(n);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The perforated sheet: every grid cell is a square with a round hole (real geometry, hole walls
        /// included), the margins are plain strips; a dark backing sheet on the wall shows through.</summary>
        void Panel(PegMesh g)
        {
            float pw = cfg.PanelW, ph = cfg.PanelH;
            var pc = cfg.PanelCentre;
            float left = pc.x - pw / 2, bottom = pc.y - ph / 2;
            float zf = cfg.PanelFront, zb = cfg.PanelBack;
            float pitch = cfg.F("panel", "hole_pitch", 0.052f), r = cfg.F("panel", "hole_radius", 0.0085f);
            int segs = Mathf.Max(6, (int)cfg.F("panel", "hole_segments", 8));
            var (ox, oy) = PegboardMaterials.HoleOrigin(cfg);
            int nx = Mathf.RoundToInt((pw - 2 * ox) / pitch), ny = Mathf.RoundToInt((ph - 2 * oy) / pitch);
            Vector2 UV(float x, float y) => new((x - left) / pw, (y - bottom) / ph);
            Vector3 P(float x, float y, float z) => new(x, y, z);
            var front = Vector3.back;
            // margins (outside the cells)
            float gx0 = left + ox - pitch / 2, gx1 = left + ox + nx * pitch + pitch / 2, gy0 = bottom + oy - pitch / 2, gy1 = bottom + oy + ny * pitch + pitch / 2;
            void Rect(float x0, float y0, float x1, float y1)
            {
                if (x1 - x0 < 1e-5f || y1 - y0 < 1e-5f) return;
                g.Quad(mats.panel, P(x0, y0, zf), P(x1, y0, zf), P(x1, y1, zf), P(x0, y1, zf), front, UV(x0, y0), UV(x1, y0), UV(x1, y1), UV(x0, y1));
            }
            Rect(left, bottom, left + pw, gy0);
            Rect(left, gy1, left + pw, bottom + ph);
            Rect(left, gy0, gx0, gy1);
            Rect(gx1, gy0, left + pw, gy1);
            // cells: the square boundary point at each of the hole's segment angles, ring to boundary
            var dirs = new Vector2[segs + 1];
            var edge = new Vector2[segs + 1];
            for (int k = 0; k <= segs; k++)
            {
                float a = k * Mathf.PI * 2 / segs + Mathf.PI / segs;
                dirs[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float s = 0.5f / Mathf.Max(Mathf.Abs(dirs[k].x), Mathf.Abs(dirs[k].y));
                edge[k] = dirs[k] * s * pitch;
            }
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float cx = left + ox + i * pitch, cy = bottom + oy + j * pitch;
                    // the boundary between two segment points runs along the square: add the corner when one lies between
                    for (int k = 0; k < segs; k++)
                    {
                        Vector2 a0 = dirs[k] * r, a1 = dirs[k + 1] * r, e0 = edge[k], e1 = edge[k + 1];
                        // corner of the square between e0 and e1?
                        var corner = new Vector2(Mathf.Sign(e0.x + e1.x) * pitch / 2, Mathf.Sign(e0.y + e1.y) * pitch / 2);
                        bool hasCorner = Mathf.Abs(e0.x - e1.x) > 1e-6f && Mathf.Abs(e0.y - e1.y) > 1e-6f;
                        Vector3 W(Vector2 q) => P(cx + q.x, cy + q.y, zf);
                        Vector2 U(Vector2 q) => UV(cx + q.x, cy + q.y);
                        if (hasCorner)
                        {
                            g.Quad(mats.panel, W(a0), W(e0), W(corner), W(a1), front, U(a0), U(e0), U(corner), U(a1));
                            g.Tri(mats.panel, W(a1), W(corner), W(e1), front, U(a1), U(corner), U(e1));
                        }
                        else g.Quad(mats.panel, W(a0), W(e0), W(e1), W(a1), front, U(a0), U(e0), U(e1), U(a1));
                        // the hole wall
                        var nIn = -(Vector3)((dirs[k] + dirs[k + 1]).normalized);
                        g.Quad(mats.holeWall, P(cx + a0.x, cy + a0.y, zf), P(cx + a1.x, cy + a1.y, zf), P(cx + a1.x, cy + a1.y, zb), P(cx + a0.x, cy + a0.y, zb), nIn);
                    }
                }
            // the panel's edge and the dark backing on the wall
            float bz = -cfg.wallOffset;
            g.Quad(mats.backing, P(left - 0.02f, bottom - 0.02f, bz), P(left + pw + 0.02f, bottom - 0.02f, bz), P(left + pw + 0.02f, bottom + ph + 0.02f, bz), P(left - 0.02f, bottom + ph + 0.02f, bz), front);
        }

        /// <summary>The heavy steel frame: angle-iron stiles and rails standing proud of the panel, corner gussets,
        /// joint plates, bolts, welds, the header with its hinge brackets on top.</summary>
        void Frame(PegMesh g)
        {
            float W = cfg.width, H = cfg.height, d = cfg.depth;
            float rail = cfg.F("frame", "rail", 0.09f), fl = cfg.F("frame", "flange", 0.012f);
            float inset = cfg.panelInset, top = cfg.topBand;
            float br = cfg.F("frame", "bolt_radius", 0.016f), spacing = cfg.F("frame", "bolt_spacing", 0.62f), cp = cfg.F("frame", "corner_plate", 0.17f);
            var f = mats.frame; var fd = mats.frameDark; var bolt = mats.bolt;
            float zFace = -d, zMid = cfg.PanelFront - 0.004f;
            // face plates (the visible front of the frame) and their return to the wall on the outside
            void Member(float x0, float y0, float x1, float y1, bool outerX, bool outerY)
            {
                g.Box(f, new Vector3((x0 + x1) / 2, (y0 + y1) / 2, zFace + fl / 2), new Vector3(x1 - x0, y1 - y0, fl), 0, false);
            }
            // stiles: full height
            Member(-W / 2, 0, -W / 2 + rail, H, true, false);
            Member(W / 2 - rail, 0, W / 2, H, true, false);
            // bottom rail, header (top rail), the light band's lower lip
            Member(-W / 2 + rail, 0, W / 2 - rail, inset, false, true);
            Member(-W / 2 + rail, H - 0.085f, W / 2 - rail, H, false, true);
            // returns to the wall (the frame's depth, seen from the side and from below/above)
            g.Box(fd, new Vector3(-W / 2 + fl / 2, H / 2, zFace / 2), new Vector3(fl, H, -zFace), 0, false);
            g.Box(fd, new Vector3(W / 2 - fl / 2, H / 2, zFace / 2), new Vector3(fl, H, -zFace), 0, false);
            g.Box(fd, new Vector3(0, fl / 2, zFace / 2), new Vector3(W, fl, -zFace), 0, false);
            g.Box(fd, new Vector3(0, H - fl / 2, zFace / 2), new Vector3(W, fl, -zFace), 0, false);
            // inner returns: from the face plate back to the panel (the panel sits recessed)
            float px0 = -cfg.PanelW / 2, px1 = cfg.PanelW / 2, py0 = inset, py1 = inset + cfg.PanelH;
            float rd = zMid - zFace;
            g.Box(fd, new Vector3(px0 - fl / 2, (py0 + py1) / 2, (zFace + zMid) / 2), new Vector3(fl, py1 - py0 + 2 * fl, rd), 0, false);
            g.Box(fd, new Vector3(px1 + fl / 2, (py0 + py1) / 2, (zFace + zMid) / 2), new Vector3(fl, py1 - py0 + 2 * fl, rd), 0, false);
            g.Box(fd, new Vector3(0, py0 - fl / 2, (zFace + zMid) / 2), new Vector3(px1 - px0 + 2 * fl, fl, rd), 0, false);
            // the light band: a recessed back plate between the panel's top and the header
            float lb0 = py1, lb1 = H - 0.085f;
            g.Box(f, new Vector3(0, (lb0 + lb1) / 2, zMid + 0.002f), new Vector3(px1 - px0 + 2 * fl, lb1 - lb0, 0.004f), 0, false);
            g.Box(f, new Vector3(0, lb0 + 0.008f, (zFace + zMid) / 2 - 0.01f), new Vector3(px1 - px0, 0.016f, rd + 0.02f), 0, false);   // ledge over the panel
            g.Box(fd, new Vector3(0, lb1 + fl / 2, (zFace + zMid) / 2), new Vector3(px1 - px0, fl, rd), 0, false);
            // a raised strip down the middle of each member (rolled-edge look)
            float lip = 0.012f;
            g.Box(f, new Vector3(-W / 2 + rail / 2, H / 2, zFace - lip / 2), new Vector3(rail * 0.4f, H - 0.02f, lip), 0, false);
            g.Box(f, new Vector3(W / 2 - rail / 2, H / 2, zFace - lip / 2), new Vector3(rail * 0.4f, H - 0.02f, lip), 0, false);
            g.Box(f, new Vector3(0, inset / 2, zFace - lip / 2), new Vector3(W - 2 * rail, inset * 0.42f, lip), 0, false);
            g.Box(f, new Vector3(0, H - 0.0425f, zFace - lip / 2), new Vector3(W - 2 * rail, 0.036f, lip), 0, false);
            // bolts along the members
            foreach (float y in Steps(cp, H - cp, spacing))
            {
                g.Bolt(bolt, new Vector3(-W / 2 + rail / 2, y, zFace - lip), br);
                g.Bolt(bolt, new Vector3(W / 2 - rail / 2, y, zFace - lip), br);
            }
            foreach (float x in Steps(-W / 2 + cp, W / 2 - cp, spacing * 1.6f))
            {
                g.Bolt(bolt, new Vector3(x, inset / 2, zFace - lip), br);
                g.Bolt(bolt, new Vector3(x, H - 0.0425f, zFace - lip), br);
            }
            // corner gussets: thick plates over each corner with four bolts
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sy in new[] { 0f, 1f })
                {
                    float cx = sx * (W / 2 - cp / 2 + 0.012f), cy = sy * H + (sy > 0 ? -1 : 1) * (cp / 2 - 0.012f);
                    g.Box(f, new Vector3(cx, cy, zFace - lip - 0.006f), new Vector3(cp, cp, 0.012f), 0, false);
                    g.Box(fd, new Vector3(cx, cy, zFace - lip - 0.0125f), new Vector3(cp - 0.03f, cp - 0.03f, 0.002f), 0, false);
                    foreach (float bx in new[] { -1f, 1f })
                        foreach (float by in new[] { -1f, 1f })
                            g.Bolt(bolt, new Vector3(cx + bx * cp * 0.32f, cy + by * cp * 0.32f, zFace - lip - 0.012f), br * 0.85f);
                }
            // joint plates on the bottom rail and the header (thirds, like the reference)
            foreach (float x in new[] { -W / 6f * 1.05f, W / 6f * 1.05f })
                foreach (float y in new[] { inset / 2, H - 0.0425f })
                {
                    g.Box(f, new Vector3(x, y, zFace - lip - 0.005f), new Vector3(0.1f, (y < H / 2 ? inset : 0.085f) + 0.012f, 0.01f), 0, false);
                    g.Bolt(bolt, new Vector3(x - 0.028f, y, zFace - lip - 0.01f), br * 0.8f);
                    g.Bolt(bolt, new Vector3(x + 0.028f, y, zFace - lip - 0.01f), br * 0.8f);
                }
            // weld beads where the stiles meet the rails (inner corners)
            var weld = mats.frameDark;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float y in new[] { inset, lb0, lb1 })
                    for (int k = 0; k < 6; k++)
                        g.Cyl(weld, new Vector3(sx * (W / 2 - rail) - sx * k * 0.012f, y, zFace), new Vector3(sx * (W / 2 - rail) - sx * (k * 0.012f + 0.011f), y, zFace), 0.006f, 5, false);
            // hinge brackets on top of the header
            var tops = Json.Arr(cfg.Section("frame").GetValueOrDefault("top_brackets"));
            foreach (var o in tops)
            {
                float x = Json.F(o);
                g.Box(f, new Vector3(x, H + 0.035f, zFace / 2 - 0.01f), new Vector3(0.36f, 0.07f, -zFace + 0.02f), 0, false);
                g.Box(fd, new Vector3(x, H - 0.03f, zFace - lip - 0.01f), new Vector3(0.4f, 0.11f, 0.02f), 0, false);
                g.Cyl(bolt, new Vector3(x - 0.2f, H + 0.005f, zFace - lip - 0.03f), new Vector3(x + 0.2f, H + 0.005f, zFace - lip - 0.03f), 0.02f, 10);
                g.Bolt(bolt, new Vector3(x - 0.13f, H - 0.03f, zFace - lip - 0.02f), br);
                g.Bolt(bolt, new Vector3(x + 0.13f, H - 0.03f, zFace - lip - 0.02f), br);
            }
            // wall brackets at the outer top corners
            foreach (float sx in new[] { -1f, 1f })
                g.Box(f, new Vector3(sx * (W / 2 - 0.2f), H + 0.03f, zFace / 2), new Vector3(0.2f, 0.06f, -zFace), 0, false);
        }

        static IEnumerable<float> Steps(float a, float b, float step)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt((b - a) / step));
            for (int i = 0; i <= n; i++) yield return Mathf.Lerp(a, b, i / (float)n);
        }

        /// <summary>Three warm light bars under the header: a steel hood with end brackets, a glowing tube, and one
        /// spot light each (no shadows) washing the panel from above.</summary>
        void LightBars(PegMesh g)
        {
            int count = Mathf.Max(0, (int)cfg.F("lights", "count", 3));
            float len = cfg.F("lights", "length", 1.7f), H = cfg.height;
            float y = cfg.panelInset + cfg.PanelH + (H - 0.085f - cfg.panelInset - cfg.PanelH) * 0.45f;
            var lc = cfg.C("lights", "light_color", new Color(1f, 0.76f, 0.48f));
            for (int i = 0; i < count; i++)
            {
                float x = count == 1 ? 0 : Mathf.Lerp(-cfg.PanelW / 2 + len / 2 + 0.12f, cfg.PanelW / 2 - len / 2 - 0.12f, i / (float)(count - 1));
                float tr = cfg.F("lights", "tube_radius", 0.022f), tz = cfg.PanelFront - tr - 0.03f;
                g.Box(mats.housing, new Vector3(x, y + tr + 0.012f, tz + 0.01f), new Vector3(len + 0.06f, 0.02f, 0.1f), 0, false);   // hood over the tube
                g.Box(mats.frameDark, new Vector3(x, y + 0.004f, cfg.PanelFront - 0.004f), new Vector3(len + 0.06f, 2 * tr + 0.03f, 0.006f), 0, false);   // reflector behind it
                g.Cyl(mats.tube, new Vector3(x - len / 2, y, tz), new Vector3(x + len / 2, y, tz), tr, 12, false);
                foreach (float sx in new[] { -1f, 1f })
                {
                    g.Box(mats.housing, new Vector3(x + sx * (len / 2 + 0.025f), y + 0.005f, tz + 0.005f), new Vector3(0.05f, 2 * tr + 0.04f, 0.07f), 0, false);   // end caps
                    g.Bolt(mats.bolt, new Vector3(x + sx * (len / 2 + 0.025f), y + 0.005f, tz - 0.03f), 0.008f);
                }
                if (cfg.F("lights", "light_intensity", 2.2f) <= 0) continue;
                // the light sits out in front of the tube and aims down at the panel: an even wash, brightest at the top
                float throwD = cfg.F("lights", "throw", 0.6f), aimY = cfg.panelInset + cfg.PanelH * cfg.F("lights", "aim", 0.4f);
                var lgo = new GameObject("PegLight" + i) { layer = layer };
                lgo.transform.SetParent(transform, false);
                var lp = new Vector3(x, H + cfg.F("lights", "lift", 0.3f), cfg.PanelFront - throwD);
                lgo.transform.localPosition = lp;
                lgo.transform.localRotation = Quaternion.LookRotation(new Vector3(x, aimY, cfg.PanelFront) - lp, Vector3.up);
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = cfg.F("lights", "spot_angle", 120);
                l.innerSpotAngle = l.spotAngle * 0.55f;
                l.range = cfg.F("lights", "light_range", 2.4f);
                l.intensity = cfg.F("lights", "light_intensity", 2.2f);
                l.color = lc;
                l.shadows = Json.Bool(cfg.Section("lights"), "shadows") ? LightShadows.Soft : LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
                lights.Add(l);
            }
        }

        /// <summary>Pegs for every slot (they stay when the tool is gone): out of a hole, through or under the tool,
        /// tipped up at the end; J hooks curl up in front.</summary>
        void Hooks(PegMesh g)
        {
            float r = cfg.F("hooks", "radius", 0.0045f);
            var inRack = new HashSet<string>();
            foreach (var o in Json.Arr(cfg.raw.GetValueOrDefault("racks"))) foreach (var s in Json.Arr(Json.Obj(o).GetValueOrDefault("slots"))) inRack.Add(s as string);
            foreach (var s in cfg.slots)
            {
                if (inRack.Contains(s.id)) continue;
                var t = cfg.toolById[s.tool];
                var m = _slotLocal[s.id];
                float toolFront = _slotBounds[s.id].min.z;
                foreach (var (at, kind) in ToolFactory.Hooks(t.GeometryKey))
                {
                    var p = m.MultiplyPoint3x4(at);
                    var a = new Vector3(p.x, p.y, cfg.PanelBack);
                    float zEnd = toolFront - (kind == 2 ? 0.006f : 0.012f);
                    // pegs hold from below: drop them to the underside of the opening for straight pegs and pins
                    var b = new Vector3(p.x, p.y, zEnd);
                    g.Cyl(mats.hook, a, b, r, 6);
                    // the pressed collar where the peg leaves its hole: reads as a fitted hook, not a rod stuck in the board
                    g.Cyl(mats.hook, new Vector3(p.x, p.y, cfg.PanelFront + 0.001f), new Vector3(p.x, p.y, cfg.PanelFront - 0.004f), r * 1.9f, 8);
                    if (kind == 1) g.Cyl(mats.hook, b, b + new Vector3(0, 0.03f, -0.004f), r, 6);
                    else g.Cyl(mats.hook, b, b + new Vector3(0, 0.008f, -0.006f), r, 6);
                }
            }
        }

        /// <summary>Racks: a steel shelf with a back plate along the hook points of its slots (the tools stand in it),
        /// on two brackets.</summary>
        void Racks(PegMesh g)
        {
            foreach (var o in Json.Arr(cfg.raw.GetValueOrDefault("racks")))
            {
                var rk = Json.Obj(o);
                float x0 = float.MaxValue, x1 = float.MinValue, ySum = 0, zMin = 0; int n = 0;
                foreach (var so in Json.Arr(rk.GetValueOrDefault("slots")))
                {
                    if (!(so is string sid) || !_slotLocal.ContainsKey(sid)) continue;
                    var t = cfg.toolById[cfg.slotById[sid].tool];
                    var hooks = ToolFactory.Hooks(t.GeometryKey);
                    var p = hooks.Count > 0 ? _slotLocal[sid].MultiplyPoint3x4(hooks[0].at) : (Vector3)_slotLocal[sid].GetColumn(3);
                    x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); ySum += p.y; n++;
                    zMin = Mathf.Min(zMin, _slotBounds[sid].min.z);
                }
                if (n == 0) continue;
                float y = ySum / n, zb = cfg.PanelFront, depth = Mathf.Max(0.05f, zb - zMin + 0.015f);
                float xa = x0 - 0.05f, xb = x1 + 0.05f;
                g.Box(mats.rack, new Vector3((xa + xb) / 2, y - 0.004f, zb - depth / 2), new Vector3(xb - xa, 0.008f, depth), 0, false);          // shelf
                g.Box(mats.rack, new Vector3((xa + xb) / 2, y - 0.03f, zb - 0.004f), new Vector3(xb - xa, 0.06f, 0.008f), 0, false);              // back plate
                g.Box(mats.rack, new Vector3((xa + xb) / 2, y - 0.012f, zb - depth + 0.004f), new Vector3(xb - xa, 0.024f, 0.008f), 0, false);    // front lip
                // a drilled hole per tool: a dark bore with a raised steel ferrule, so the shanks pass through the shelf
                foreach (var so in Json.Arr(rk.GetValueOrDefault("slots")))
                {
                    if (!(so is string sid) || !_slotLocal.ContainsKey(sid)) continue;
                    var sl = _slotLocal[sid];
                    var at = sl.MultiplyPoint3x4(Vector3.zero);
                    float zt = (_slotBounds[sid].min.z + _slotBounds[sid].max.z) / 2;
                    float rr = Mathf.Clamp(_slotBounds[sid].size.z * 0.3f, 0.006f, 0.012f);
                    g.Cyl(mats.rack, new Vector3(at.x, y - 0.001f, zt), new Vector3(at.x, y + 0.003f, zt), rr * 1.45f, 10);
                    g.Cyl(mats.holeWall, new Vector3(at.x, y + 0.0031f, zt), new Vector3(at.x, y + 0.0033f, zt), rr, 10);
                }
                foreach (float x in new[] { xa + 0.02f, xb - 0.02f })
                {
                    g.Box(mats.rack, new Vector3(x, y - 0.03f, zb - depth / 2), new Vector3(0.008f, 0.05f, depth), 0, false);   // brackets
                    g.Bolt(mats.bolt, new Vector3(x, y - 0.04f, zb - 0.008f), 0.006f);
                }
            }
        }

        // ------------------------------------------------------------------ hung tools

        /// <summary>The hung tools, merged into a few meshes by ToolBatch (rebuilt when the set changes).</summary>
        void RebuildTools()
        {
            ToolBatch.Clear(_toolsRoot);
            var hung = new List<(ItemDefinition, Matrix4x4)>();
            foreach (var kv in _installed)
            {
                var def = kv.Value;
                var tool = cfg.toolById.TryGetValue(cfg.slotById[kv.Key].tool, out var tt) ? tt : null;
                var m = _slotLocal[kv.Key];
                // the item's own size if it differs from the slot's (catalogue scale), so the hung tool is the item
                if (tool != null && def.worldScale > 0 && Mathf.Abs(def.worldScale - tool.size) > 1e-4f)
                    m = m * Matrix4x4.Scale(Vector3.one * (def.worldScale / tool.size));
                hung.Add((def, m));
            }
            ToolBatch.Build(_toolsRoot, hung, layer, "peg_tools");
        }
    }
}
