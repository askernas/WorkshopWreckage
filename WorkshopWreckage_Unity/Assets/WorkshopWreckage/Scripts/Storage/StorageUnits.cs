using System.Collections.Generic;
using UnityEngine;
using static WorkshopWreckage.Batch;

namespace WorkshopWreckage
{
    /// <summary>Physics layers used by the game.</summary>
    public static class Layers
    {
        public const int Env = 0;          // walls, furniture, unit blockers
        public const int Target = 8;       // storage destinations / stations (aim only)
        public const int Player = 9;
        public const int AimMask = (1 << Env) | (1 << Target);
    }

    /// <summary>Collider tag: which destination (or station) this collider is.</summary>
    public class StorageTarget : MonoBehaviour
    {
        public StorageUnit unit;
        public string destId;
        public int slot = -1;
        public bool station;
    }

    /// <summary>Marks player-blocking volumes that aiming rays must pass through.</summary>
    public class AimPassThrough : MonoBehaviour { }

    public static class Mirror
    {
        static readonly Matrix4x4 S = Matrix4x4.Scale(new Vector3(1, 1, -1));
        /// <summary>Godot-space matrix -> Unity-space matrix.</summary>
        public static Matrix4x4 M(Matrix4x4 g) => S * g * S;
        public static Matrix4x4 GRot(Vector3 axis, float rad) => Matrix4x4.Rotate(Quaternion.AngleAxis(rad * Mathf.Rad2Deg, axis));
    }

    /// <summary>Neat, deterministic placement of stored items (port of StorageArrangement.gd).
    /// Works in Godot destination-local space (+Z = front), returns a Unity local matrix.</summary>
    public static class StorageArrangement
    {
        public static Matrix4x4 SlotLocal(ItemDefinition def, int index, Vector3 inner /* width, height, depth */)
        {
            var s = MeshLibrary.UnitBounds(def.geometryKey).size * def.worldScale;
            Matrix4x4 basis;
            Vector3 e;
            if (def.restMode == RestMode.Long)
            {
                e = new Vector3(s.x, s.z, s.y);
                basis = Mirror.GRot(Vector3.right, Mathf.PI / 2);
                if (e.z > inner.x * 3f || e.z > 0.9f)
                {
                    basis = Mirror.GRot(Vector3.forward, Mathf.PI / 2);
                    e = new Vector3(s.y, s.x, s.z);
                }
            }
            else { basis = Matrix4x4.identity; e = s; }
            // mesh bounds were measured in mirrored space; extents are symmetric, so unchanged
            const float pad = 1.06f;
            int cols = Mathf.Max(1, (int)(inner.x / (e.x * pad)));
            int rows = Mathf.Max(1, (int)(inner.z / (e.z * pad)));
            int perLayer = cols * rows;
            int layer = index / perLayer, k = index % perLayer;
            int row = k / cols, col = k % cols;
            float usedW = cols * e.x * pad;
            float x = -usedW * 0.5f + e.x * pad * (col + 0.5f);
            float z = -inner.z * 0.5f + e.z * pad * (row + 0.5f);
            float y = e.y * (layer + 0.5f);
            float j = Hash01(index * 92821 + def.id);
            var g = Matrix4x4.Translate(new Vector3(x, y, z)) * Mirror.GRot(Vector3.up, (j - 0.5f) * 0.12f) * basis * Matrix4x4.Scale(Vector3.one * def.worldScale);
            return Mirror.M(g);
        }

        static float Hash01(int v)
        {
            unchecked
            {
                uint h = (uint)v * 2654435761u;
                h ^= h >> 15;
                return (h & 0xffff) / 65535f;
            }
        }
    }

    /// <summary>A storage unit: drawer cabinet, warehouse shelving or pallet zone.</summary>
    public class StorageUnit : MonoBehaviour
    {
        public static readonly Dictionary<string, Color> CategoryColors = new()
        {
            ["Screws"] = new Color(0.23f, 0.37f, 0.55f), ["Nuts"] = new Color(0.6f, 0.18f, 0.13f), ["Washers"] = new Color(0.78f, 0.56f, 0.17f),
            ["Gears"] = new Color(0.25f, 0.42f, 0.23f), ["Springs"] = new Color(0.18f, 0.44f, 0.48f), ["Bearings"] = new Color(0.42f, 0.44f, 0.46f),
            ["Shafts & Pins"] = new Color(0.55f, 0.36f, 0.17f), ["Electronics"] = new Color(0.29f, 0.31f, 0.5f), ["Specialty Parts"] = new Color(0.48f, 0.18f, 0.29f),
        };
        // painted steel shelving in off-white and beige (playtest: no wooden shelves)
        static readonly Color Upright = new(0.84f, 0.82f, 0.75f), Beam = new(0.76f, 0.71f, 0.6f), Cardboard = new(0.62f, 0.45f, 0.28f);
        static readonly Color[] BinColors = { new(0.18f, 0.35f, 0.62f), new(0.55f, 0.12f, 0.1f), new(0.75f, 0.55f, 0.15f), new(0.32f, 0.36f, 0.38f) };

        public Dictionary<string, object> unit;
        public string id, type;
        public readonly List<Dictionary<string, object>> dests = new();
        public System.Func<string, (string code, string name, string count, bool known)> labelProvider;
        readonly Dictionary<string, Matrix4x4> _destLocal = new();   // Unity local, destination floor centre
        readonly Dictionary<string, int> _labelIndex = new();
        TextAtlas _atlas;
        Material _atlasMat;
        // drawer cabinets
        public int rows = 6, cols = 6;
        public Vector3 size;
        public Vector2 front;
        const float Plinth = 0.15f, RowH = 0.27f;
        public const float TrayDepth = 0.42f;
        readonly Dictionary<int, Material> _frontMat = new();   // per drawer slot (4 shades per cabinet)
        GameObject _fronts;                                       // all closed drawers of the cabinet, one batched mesh
        readonly Dictionary<int, (GameObject node, float timer, bool closing)> _open = new();

        public void Setup(Dictionary<string, object> u, List<Dictionary<string, object>> d)
        {
            unit = u;
            id = Json.Str(u, "id");
            type = Json.Str(u, "type");
            name = "Unit_" + id;
            dests.AddRange(d);
            size = new Vector3((float)Json.Num(u, "width"), (float)Json.Num(u, "height"), (float)Json.Num(u, "depth"));
            var b = new Batch();
            var blocker = new Batch();
            if (type == "drawer_cabinet") BuildCabinet(b);
            else if (type == "pallet_zone") BuildPallets(b);
            else BuildShelving(b, blocker);
            b.Build(transform, id);
            if (blocker.colliders.Count > 0)
            {
                var bl = new GameObject("Blocker");
                bl.transform.SetParent(transform, false);
                bl.AddComponent<AimPassThrough>();
                blocker.Build(bl.transform, "blocker");
                foreach (Transform c in bl.transform) c.gameObject.AddComponent<AimPassThrough>();
            }
            BuildLabels();
        }

        public Matrix4x4 DestWorld(string destId) => transform.localToWorldMatrix * _destLocal[destId];

        /// <summary>World point where a destination is filled: the drawer front for
        /// drawers, the destination centre otherwise.</summary>
        public Vector3 DestPoint(Dictionary<string, object> dest)
        {
            var id = Json.Str(dest, "id");
            if (_destLocal.ContainsKey(id)) return DestWorld(id).GetColumn(3);
            int slot = Json.Int(dest, "row") * cols + Json.Int(dest, "col");
            return transform.TransformPoint(Batch.G(FrontCenterG(slot)));
        }
        public static Vector3 Inner(Dictionary<string, object> d) => new((float)Json.Num(d, "width"), (float)Json.Num(d, "height"), (float)Json.Num(d, "depth"));

        // ------------------------------------------------------------ drawer cabinet

        public Vector3 FrontCenterG(int slot)
        {
            int row = slot / cols, col = slot % cols;
            float x = -size.x * 0.5f + 0.04f + (size.x - 0.08f) * (col + 0.5f) / cols;
            float y = Plinth + (rows - 1 - row) * RowH + RowH * 0.5f;
            return new Vector3(x, y, size.z * 0.5f + 0.004f);
        }

        public Matrix4x4 FrontWorld(int slot) => transform.localToWorldMatrix * Matrix4x4.Translate(G(FrontCenterG(slot)));

        void BuildCabinet(Batch b)
        {
            rows = Json.Int(unit, "rows", 6);
            cols = Json.Int(unit, "cols", 6);
            front = new Vector2((size.x - 0.08f) / cols - 0.012f, RowH - 0.02f);
            var cats = Json.Arr(unit.GetValueOrDefault("categories"));
            var color = cats.Count > 0 && CategoryColors.TryGetValue(cats[0].ToString(), out var cc) ? cc : new Color(0.4f, 0.4f, 0.4f);
            var carcass = EnvMaterials.Metal(new Color(0.2f, 0.23f, 0.2f), 0.3f);
            float w = size.x, d = size.z, top = Plinth + rows * RowH;
            b.Box(new Vector3(-w / 2 + 0.02f, top / 2, 0), new Vector3(0.04f, top, d), carcass);
            b.Box(new Vector3(w / 2 - 0.02f, top / 2, 0), new Vector3(0.04f, top, d), carcass);
            b.Box(new Vector3(0, top + 0.02f, 0), new Vector3(w, 0.04f, d + 0.02f), carcass);
            b.Box(new Vector3(0, top / 2, -d / 2 + 0.01f), new Vector3(w, top, 0.02f), carcass);
            b.Box(new Vector3(0, Plinth / 2, 0), new Vector3(w - 0.02f, Plinth, d - 0.04f), EnvMaterials.Metal(new Color(0.12f, 0.12f, 0.12f), 0.2f));
            var shelf = EnvMaterials.Metal(new Color(0.16f, 0.18f, 0.16f), 0.3f);
            for (int r = 0; r <= rows; r++) b.Box(new Vector3(0, Plinth + r * RowH, d / 2 - 0.01f), new Vector3(w - 0.06f, 0.012f, 0.03f), shelf);
            b.Box(new Vector3(0, top + 0.17f, d / 2 - 0.04f), new Vector3(w * 0.92f, 0.24f, 0.03f), EnvMaterials.Wood(new Color(0.2f, 0.13f, 0.08f)));
            b.colliders.Add((G(0, (top + 0.3f) / 2, 0), new Vector3(w, top + 0.3f, d), Quaternion.identity));
            // header text
            var header = TextAtlas.RenderSingle($"{id}  ·  {Json.Str(unit, "label")}", 1024, 160, new Color(0.2f, 0.13f, 0.08f), color * 1.6f + new Color(0.2f, 0.2f, 0.2f), 64, true);
            b.Quad(new Vector3(0, top + 0.17f, d / 2 - 0.024f), new Vector2(w * 0.9f, w * 0.9f * 160 / 1024f), EnvMaterials.Atlas(header), new Rect(0, 0, 1, 1));
            // drawer fronts and handles: one batched mesh each (per material)
            var rng = new System.Random(id.GetHashCode());
            dests.Sort((a, c) => (Json.Int(a, "row") * 100 + Json.Int(a, "col")).CompareTo(Json.Int(c, "row") * 100 + Json.Int(c, "col")));
            var shades = new[] { 0.88f, 0.96f, 1.04f, 1.12f };   // few materials -> few draw calls
            for (int slot = 0; slot < rows * cols; slot++)
            {
                float v = shades[rng.Next(shades.Length)];
                _frontMat[slot] = EnvMaterials.Plain(new Color(color.r * v, color.g * v, color.b * v), 0.25f, 0.45f);
                if (slot < dests.Count) AddTarget(Json.Str(dests[slot], "id"), FrontCenterG(slot), new Vector3(front.x, front.y, 0.04f), slot);
            }
            RebuildFronts();
        }

        static Material Handle => EnvMaterials.Plain(new Color(0.78f, 0.6f, 0.3f), 1, 0.68f);

        /// <summary>One drawer front with handle and label, at its place in the cabinet.</summary>
        void AddFront(Batch b, int slot)
        {
            var c = FrontCenterG(slot);
            b.Box(c, new Vector3(front.x, front.y, 0.025f), _frontMat[slot]);
            b.Box(c + new Vector3(0, -0.06f, 0.02f), new Vector3(0.08f, 0.018f, 0.02f), Handle);
            if (_atlas != null && slot < dests.Count) b.Quad(LabelPosG(dests[slot], slot), new Vector2(0.17f, 0.068f), _atlasMat, _atlas.CellUV(slot));
        }

        /// <summary>All closed drawers as one batched mesh (open drawers are separate objects).</summary>
        void RebuildFronts()
        {
            if (_fronts != null) Destroy(_fronts);
            _fronts = new GameObject("Fronts");
            _fronts.transform.SetParent(transform, false);
            var b = new Batch();
            for (int slot = 0; slot < rows * cols; slot++) if (!_open.ContainsKey(slot)) AddFront(b, slot);
            b.Build(_fronts.transform, "fronts");
        }

        // ------------------------------------------------------------ shelving

        void BuildShelving(Batch b, Batch blocker)
        {
            float w = size.x, depth = size.z, h = size.y;
            var levels = Json.Arr(unit["levels"]);
            var upright = EnvMaterials.Metal(Upright, 0.25f);
            var beam = EnvMaterials.Metal(Beam, 0.3f);
            var deck = EnvMaterials.Metal(new Color(0.87f, 0.85f, 0.79f), 0.15f);
            foreach (var sx in new[] { -1f, 1f })
            {
                foreach (var sz in new[] { -1f, 1f })
                    b.Box(new Vector3(sx * (w / 2 - 0.04f), h / 2, sz * (depth / 2 - 0.04f)), new Vector3(0.07f, h, 0.07f), upright);
                for (int k = 0; k < 4; k++)
                {
                    float y = 0.3f + k * (h - 0.4f) / 4f;
                    b.Box(new Vector3(sx * (w / 2 - 0.04f), y + 0.25f, 0), new Vector3(0.025f, 0.025f, depth - 0.1f), upright, false, R(Vector3.right, k % 2 == 0 ? 0.6f : -0.6f));
                }
            }
            foreach (var lo in levels)
            {
                float lv = Json.F(lo);
                foreach (var sz in new[] { -1f, 1f }) b.Box(new Vector3(0, lv - 0.05f, sz * (depth / 2 - 0.04f)), new Vector3(w - 0.08f, 0.1f, 0.05f), beam);
                b.Box(new Vector3(0, lv - 0.012f, 0), new Vector3(w - 0.12f, 0.025f, depth - 0.06f), deck);
            }
            blocker.colliders.Add((G(0, h / 2, 0), new Vector3(w, h, depth), Quaternion.identity));
            var header = TextAtlas.RenderSingle($"{id}  ·  {Json.Str(unit, "label")}", 1024, 120, new Color(0.08f, 0.06f, 0.05f), new Color(0.98f, 0.86f, 0.55f), 56, true);
            b.Quad(new Vector3(0, h + 0.1f, depth / 2), new Vector2(1.6f, 1.6f * 120 / 1024f), EnvMaterials.Atlas(header), new Rect(0, 0, 1, 1));
            var rng = new System.Random(id.GetHashCode());
            foreach (var d in dests)
            {
                float lv = Json.F(levels[Json.Int(d, "level")]);
                float x = -w / 2 + (float)Json.Num(d, "x");
                var inner = Inner(d);
                var o = new Vector3(x, lv, 0);
                string kind = Json.Str(d, "kind");
                switch (kind)
                {
                    case "cardboard_box": CardboardBox(b, o, inner, rng); o.y += 0.012f; break;
                    case "metal_bin": MetalBin(b, o, inner, Json.Str(d, "id")); o.y += 0.012f; break;
                    case "wooden_crate": Crate(b, o, inner); o.y += 0.025f; break;
                    default: SectionMarks(b, o, inner); break;
                }
                _destLocal[Json.Str(d, "id")] = Matrix4x4.Translate(G(o));
                AddTarget(Json.Str(d, "id"), o + new Vector3(0, Mathf.Min(inner.y, kind == "shelf_section" ? 0.3f : inner.y) / 2, 0),
                    new Vector3(inner.x + 0.02f, Mathf.Max(0.08f, kind == "shelf_section" ? Mathf.Min(inner.y, 0.3f) : inner.y), inner.z + 0.02f), -1);
            }
        }

        void CardboardBox(Batch b, Vector3 o, Vector3 inner, System.Random rng)
        {
            float shade = new[] { 0.86f, 0.94f, 1f, 1.08f }[rng.Next(4)];
            var tint = Cardboard * shade;
            var m = EnvMaterials.Cardboard(new Color(tint.r, tint.g, tint.b));
            float wall = 0.008f;
            b.Box(o + new Vector3(0, wall / 2, 0), new Vector3(inner.x + 2 * wall, wall, inner.z + 2 * wall), m);
            foreach (var sx in new[] { -1f, 1f }) b.Box(o + new Vector3(sx * (inner.x / 2 + wall / 2), inner.y / 2, 0), new Vector3(wall, inner.y, inner.z + 2 * wall), m);
            b.Box(o + new Vector3(0, inner.y / 2, -inner.z / 2 - wall / 2), new Vector3(inner.x, inner.y, wall), m);
            b.Box(o + new Vector3(0, inner.y / 2, inner.z / 2 + wall / 2), new Vector3(inner.x, inner.y, wall), m);
            var flap = EnvMaterials.Cardboard(new Color(tint.r * 0.9f, tint.g * 0.9f, tint.b * 0.9f));
            b.Box(o + new Vector3(0, inner.y + 0.04f, -inner.z / 2 - 0.03f), new Vector3(inner.x, 0.1f, 0.006f), flap, false, R(Vector3.right, -0.5f));
            b.Box(o + new Vector3(0, inner.y / 2, inner.z / 2 + wall + 0.001f), new Vector3(0.05f, inner.y, 0.002f), EnvMaterials.Plain(new Color(0.75f, 0.62f, 0.42f), 0, 0.6f));
        }

        void MetalBin(Batch b, Vector3 o, Vector3 inner, string destId)
        {
            var col = BinColors[Mathf.Abs(destId.GetHashCode()) % BinColors.Length];
            var m = EnvMaterials.Metal(col, 0.2f);
            float t = 0.01f;
            b.Box(o + new Vector3(0, t / 2, 0), new Vector3(inner.x + 2 * t, t, inner.z + 2 * t), m);
            b.Box(o + new Vector3(0, inner.y / 2, -inner.z / 2 - t / 2), new Vector3(inner.x + 2 * t, inner.y, t), m);
            b.Box(o + new Vector3(0, inner.y * 0.22f, inner.z / 2 + t / 2), new Vector3(inner.x + 2 * t, inner.y * 0.44f, t), m);
            foreach (var sx in new[] { -1f, 1f }) b.Box(o + new Vector3(sx * (inner.x / 2 + t / 2), inner.y / 2, 0), new Vector3(t, inner.y, inner.z), m);
        }

        void Crate(Batch b, Vector3 o, Vector3 inner)
        {
            var m = EnvMaterials.CrateWood(Color.white);
            b.Box(o + new Vector3(0, 0.012f, 0), new Vector3(inner.x + 0.04f, 0.025f, inner.z + 0.04f), m);
            for (int k = 0; k < 3; k++)
            {
                float sy = 0.04f + (inner.y - 0.05f) * (k + 0.5f) / 3f;
                foreach (var sz in new[] { -1f, 1f }) b.Box(o + new Vector3(0, sy, sz * (inner.z / 2 + 0.01f)), new Vector3(inner.x + 0.04f, inner.y / 3 * 0.7f, 0.02f), m);
                foreach (var sx in new[] { -1f, 1f }) b.Box(o + new Vector3(sx * (inner.x / 2 + 0.01f), sy, 0), new Vector3(0.02f, inner.y / 3 * 0.7f, inner.z), m);
            }
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    b.Box(o + new Vector3(sx * (inner.x / 2 + 0.01f), inner.y / 2, sz * (inner.z / 2 + 0.01f)), new Vector3(0.035f, inner.y, 0.035f), m);
        }

        void SectionMarks(Batch b, Vector3 o, Vector3 inner)
        {
            var paint = EnvMaterials.Plain(new Color(0.95f, 0.8f, 0.2f), 0, 0.4f);
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                {
                    var c = o + new Vector3(sx * (inner.x / 2 - 0.04f), 0.002f, sz * (inner.z / 2 - 0.04f));
                    b.Box(c, new Vector3(0.08f, 0.002f, 0.012f), paint);
                    b.Box(c, new Vector3(0.012f, 0.002f, 0.08f), paint);
                }
        }

        // ------------------------------------------------------------ pallets

        void BuildPallets(Batch b)
        {
            float w = size.x, depth = size.z;
            var tape = EnvMaterials.Plain(new Color(0.95f, 0.78f, 0.1f), 0, 0.4f);
            var dark = EnvMaterials.Plain(new Color(0.08f, 0.08f, 0.08f), 0, 0.4f);
            foreach (var sz in new[] { -1f, 1f })
                for (int k = 0; k < 12; k++)
                    b.Box(new Vector3(-w / 2 + w / 12f * (k + 0.5f), 0.002f, sz * depth / 2), new Vector3(w / 12f, 0.003f, 0.06f), k % 2 == 0 ? tape : dark);
            foreach (var sx in new[] { -1f, 1f }) b.Box(new Vector3(sx * w / 2, 0.002f, 0), new Vector3(0.06f, 0.003f, depth), tape);
            var wood = EnvMaterials.Wood(new Color(0.6f, 0.47f, 0.3f));
            float pitch = w / 4f;
            foreach (var d in dests)
            {
                float x = -w / 2 + (float)Json.Num(d, "x");
                float z = depth / 2 - pitch * (Json.Int(d, "row") + 0.5f);
                for (int k = 0; k < 5; k++) b.Box(new Vector3(x, 0.13f, z - pitch * 0.42f + k * pitch * 0.21f), new Vector3(pitch * 0.92f, 0.02f, pitch * 0.15f), wood);
                for (int k = 0; k < 3; k++) b.Box(new Vector3(x - pitch * 0.42f + k * pitch * 0.42f, 0.06f, z), new Vector3(pitch * 0.12f, 0.1f, pitch * 0.92f), wood);
                var o = new Vector3(x, 0.14f, z);
                _destLocal[Json.Str(d, "id")] = Matrix4x4.Translate(G(o));
                var inner = Inner(d);
                AddTarget(Json.Str(d, "id"), o + new Vector3(0, 0.15f, 0), new Vector3(inner.x + 0.02f, 0.3f, inner.z + 0.02f), -1);
            }
            var sign = TextAtlas.RenderSingle($"{id}  ·  HEAVY PARTS", 1024, 120, new Color(0.08f, 0.06f, 0.05f), new Color(0.98f, 0.86f, 0.3f), 56, true);
            b.Box(new Vector3(0, 0.55f, -depth / 2 + 0.04f), new Vector3(0.05f, 1.1f, 0.05f), dark);
            b.Quad(new Vector3(0, 1.1f, -depth / 2 + 0.07f), new Vector2(1.4f, 1.4f * 120 / 1024f), EnvMaterials.Atlas(sign), new Rect(0, 0, 1, 1));
        }

        // ------------------------------------------------------------ targets & labels

        void AddTarget(string destId, Vector3 centerG, Vector3 sz, int slot)
        {
            var go = new GameObject("Target_" + destId) { layer = Layers.Target };
            go.transform.SetParent(transform, false);
            go.transform.localPosition = G(centerG);
            var bc = go.AddComponent<BoxCollider>();
            bc.size = sz;
            var t = go.AddComponent<StorageTarget>();
            t.unit = this;
            t.destId = destId;
            t.slot = slot;
        }

        Vector3 LabelPosG(Dictionary<string, object> d, int slot)
        {
            if (type == "drawer_cabinet") return FrontCenterG(slot) + new Vector3(0, 0.06f, 0.0135f);
            var o = (Vector3)(_destLocal[Json.Str(d, "id")].GetColumn(3));
            var og = new Vector3(o.x, o.y, -o.z);
            var inner = Inner(d);
            switch (Json.Str(d, "kind"))
            {
                case "cardboard_box": return og + new Vector3(0, inner.y * 0.62f, inner.z / 2 + 0.012f);
                case "metal_bin": return og + new Vector3(0, inner.y * 0.22f, inner.z / 2 + 0.017f);
                case "wooden_crate": return og + new Vector3(0, inner.y * 0.55f, inner.z / 2 + 0.032f);
                case "floor_pallet": return og + new Vector3(0, 0.02f, inner.z / 2 + 0.08f);
            }
            return new Vector3(og.x, og.y - 0.05f, size.z / 2 - 0.01f);
        }

        void BuildLabels()
        {
            if (dests.Count == 0) return;
            var paper = type == "pallet_zone" ? new Color(0.98f, 0.85f, 0.2f) : new Color(0.95f, 0.92f, 0.84f);
            _atlas = new TextAtlas(dests.Count, new Vector2Int(200, 80), paper);
            _atlasMat = EnvMaterials.Atlas(_atlas.texture);
            var b = new Batch();
            for (int i = 0; i < dests.Count; i++)
            {
                _labelIndex[Json.Str(dests[i], "id")] = i;
                var rot = type == "pallet_zone" ? R(Vector3.right, -Mathf.PI / 2 + 0.35f) : Quaternion.identity;
                if (type == "drawer_cabinet") continue;   // drawer labels belong to the drawer fronts
                b.Quad(LabelPosG(dests[i], i), new Vector2(0.17f, 0.068f), _atlasMat, _atlas.CellUV(i), rot);
            }
            if (type == "drawer_cabinet") RebuildFronts();   // labels are part of the batched fronts
            else b.Build(transform, "labels", false);
            RefreshLabels();
        }

        public void RefreshLabels()
        {
            if (_atlas == null) return;
            for (int i = 0; i < dests.Count; i++)
            {
                var did = Json.Str(dests[i], "id");
                (string code, string name, string count, bool known) info = labelProvider != null ? labelProvider(did) : (Json.Str(dests[i], "slot"), "", "", false);
                _atlas.Set(i, info.code, info.name, info.count, info.known);
            }
            _atlas.Redraw();
        }

        // ------------------------------------------------------------ drawers

        public int SlotOf(string destId) => _labelIndex.TryGetValue(destId, out var i) ? i : -1;

        /// <summary>Opens a drawer with the given contents (mesh, materials, tray-local Unity matrix);
        /// `slide` is how far it comes out (a small robot standing in front opens it only part way).</summary>
        public void OpenDrawer(int slot, List<(Mesh mesh, Material[] mats, Matrix4x4 local)> contents, float hold = 1.4f, float slide = 0.26f)
        {
            if (_open.TryGetValue(slot, out var o))
            {
                _open[slot] = (o.node, hold, false);
                FillTray(o.node.transform.Find("Tray"), contents);
                return;
            }
            var node = new GameObject("OpenDrawer" + slot);
            node.transform.SetParent(transform, false);
            var fb = new Batch();
            AddFront(fb, slot);
            fb.Build(node.transform, "front");
            _open[slot] = (node, hold, false);
            RebuildFronts();
            var tray = new GameObject("Tray");
            tray.transform.SetParent(node.transform, false);
            var c = FrontCenterG(slot);
            tray.transform.localPosition = G(c + new Vector3(0, -front.y / 2 + 0.03f, -0.015f));
            var tb = new Batch();
            var wood = EnvMaterials.Wood(new Color(0.5f, 0.36f, 0.22f));
            tb.Box(new Vector3(0, -0.005f, -TrayDepth / 2), new Vector3(front.x - 0.02f, 0.01f, TrayDepth), wood);
            foreach (var sx in new[] { -1f, 1f }) tb.Box(new Vector3(sx * (front.x / 2 - 0.015f), 0.07f, -TrayDepth / 2), new Vector3(0.01f, 0.15f, TrayDepth), wood);
            tb.Box(new Vector3(0, 0.07f, -TrayDepth), new Vector3(front.x - 0.02f, 0.15f, 0.01f), wood);
            tb.Build(tray.transform, "tray");
            FillTray(tray.transform, contents);
            _open[slot] = (node, hold, false);
            StartCoroutine(Slide(node.transform, 0, slide, 0.16f));
            Sfx.Play("drawer_open", FrontWorld(slot).GetColumn(3), -6);
        }

        static void FillTray(Transform tray, List<(Mesh mesh, Material[] mats, Matrix4x4 local)> contents)
        {
            var old = tray.Find("Items");
            if (old != null) Destroy(old.gameObject);
            var items = new GameObject("Items").transform;
            items.SetParent(tray, false);
            foreach (var (mesh, mats, local) in contents)
            {
                var go = new GameObject("item");
                go.transform.SetParent(items, false);
                go.transform.localPosition = local.GetColumn(3);
                go.transform.localRotation = local.rotation;
                go.transform.localScale = local.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
        }

        System.Collections.IEnumerator Slide(Transform t, float from, float to, float dur)
        {
            float time = 0;
            var basePos = t.localPosition;
            while (time < dur)
            {
                time += Time.deltaTime;
                float e = Mathf.Clamp01(time / dur);
                e = to > from ? 1 - (1 - e) * (1 - e) : e * e;
                // Godot +Z (out of the cabinet) is Unity -Z
                t.localPosition = basePos + new Vector3(0, 0, -Mathf.Lerp(from, to, e));
                yield return null;
            }
        }

        void Update()
        {
            if (_open.Count == 0) return;
            foreach (var slot in new List<int>(_open.Keys))
            {
                var o = _open[slot];
                o.timer -= Time.deltaTime;
                if (o.timer <= 0 && !o.closing)
                {
                    o.closing = true;
                    StartCoroutine(Close(slot, o.node));
                }
                _open[slot] = o;
            }
        }

        System.Collections.IEnumerator Close(int slot, GameObject node)
        {
            float time = 0;
            var start = node.transform.localPosition;
            var end = Vector3.zero;
            while (time < 0.14f)
            {
                time += Time.deltaTime;
                float e = Mathf.Clamp01(time / 0.14f);
                node.transform.localPosition = Vector3.Lerp(start, end, e * e);
                yield return null;
            }
            Destroy(node);
            _open.Remove(slot);
            RebuildFronts();
            Sfx.Play("drawer_close", FrontWorld(slot).GetColumn(3), -8);
        }

        public Matrix4x4 TrayContentLocal(ItemDefinition def, int index, Dictionary<string, object> d)
        {
            // tray-local (Godot): destination frame centred on the tray floor
            var inner = Inner(d);
            return Mirror.M(Matrix4x4.Translate(new Vector3(0, 0, -TrayDepth / 2))) * StorageArrangement.SlotLocal(def, index, inner);
        }
    }
}
