using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage
{
    /// <summary>Four large "motivational" enamel signs (playtest 2026-11-02): 2 m tall, aspect from the image,
    /// hung dead straight and flush on the wall (unlike the taped blueprints). Two between the big west
    /// windows, one above the machine on the south wall beside the sorting tray, one high on the south wall
    /// above the shelving (in view when the player turns from the start or heads for the tray; the east wall
    /// is hidden behind the aisle name boards from most of the room). Spots() is also what the blueprints
    /// and the high wall dressing keep clear of.</summary>
    public static class EnamelSigns
    {
        public const float Height = 2f;
        const float WindowBottom = 1.7f;   // between the windows: centred on them (1.0–4.4 m)
        const float HighBottom = 3.15f;    // above the storage name boards (the dressing band), under the beams (5.26 m)

        /// <summary>Image (Resources/EnamelSigns) and its width / height.</summary>
        static readonly (string image, float aspect)[] Images =
        {
            ("no_i_in_hard_work", 1086f / 1448f),
            ("think_work", 1024f / 1536f),
            ("time_and_place", 1086f / 1448f),
            ("work_harder", 1122f / 1402f),
        };

        public struct Spot
        {
            public string wall;   // north / east / south / west
            public float t;       // along the wall: x on north and south, z on east and west (Godot)
            public float bottom, w;
            public string image;
        }

        /// <summary>Where the signs hang in this layout (Godot coordinates).</summary>
        public static List<Spot> Spots(WorkshopLayout L)
        {
            var spots = new List<Spot>();
            Spot S(string wall, float t, float bottom, int img) => new Spot { wall = wall, t = t, bottom = bottom, w = Height * Images[img].aspect, image = Images[img].image };
            // west: the first and the last gap between the windows
            var zs = L.windows.Select(w => (float)Json.Num(w, "z")).OrderBy(z => z).ToList();
            if (zs.Count >= 2)
            {
                spots.Add(S("west", (zs[0] + zs[1]) / 2, WindowBottom, 3));
                spots.Add(S("west", (zs[zs.Count - 2] + zs[zs.Count - 1]) / 2, WindowBottom, 1));
            }
            // south: above the machine beside the sorting tray ("now put it there")
            var machine = L.furniture.FirstOrDefault(f => Json.Str(f, "type") == "machine");
            if (machine != null)
            {
                var p = WorkshopLayout.V(machine["pos"]);
                var s = WorkshopLayout.V(machine["size"]);
                spots.Add(S("south", p.x, Mathf.Max(WindowBottom, s.y + 0.4f), 2));
            }
            // south, high above the shelving run east of the machine
            spots.Add(S("south", L.roomMax.x - (L.roomMax.x - L.roomMin.x) * 0.28f, HighBottom, 0));
            return spots;
        }

        /// <summary>Does something w wide at t on `wall` (bottom..top, m) overlap a sign, with a margin?</summary>
        public static bool Blocks(WorkshopLayout L, string wall, float t, float w, float bottom = 0, float top = 99, float margin = 0.25f)
        {
            foreach (var s in Spots(L))
                if (s.wall == wall && Mathf.Abs(s.t - t) < (s.w + w) / 2 + margin && bottom < s.bottom + Height + margin && top > s.bottom - margin) return true;
            return false;
        }

        public static void Build(WorkshopLayout L, Transform root)
        {
            var parent = new GameObject("EnamelSigns").transform;
            parent.SetParent(root, false);
            var b = new Batch();
            var steel = EnvMaterials.Plain(new Color(0.2f, 0.17f, 0.14f), 0.35f, 0.4f);   // the plate's edge
            const float plate = 0.012f;
            foreach (var s in Spots(L))
            {
                var tex = Resources.Load<Texture2D>("EnamelSigns/" + s.image);
                if (tex == null) { Debug.LogWarning($"[enamel] missing image {s.image}"); continue; }
                tex.anisoLevel = 8;   // seen at a slant along the walls
                var (c, n) = s.wall switch
                {
                    "north" => (new Vector2(s.t, L.roomMin.z), Vector2.up),
                    "south" => (new Vector2(s.t, L.roomMax.z), Vector2.down),
                    "east" => (new Vector2(L.roomMax.x, s.t), Vector2.left),
                    _ => (new Vector2(L.roomMin.x, s.t), Vector2.right),
                };
                float y = s.bottom + Height / 2;
                var mat = EnvMaterials.Atlas(tex);
                mat.name = "enamel_" + s.image;
                mat.SetFloat("_Smoothness", 0.45f);   // glossy enamel
                // the face, straight and flush: no tilt, no curl, just in front of its plate
                var face = new Vector3(c.x + n.x * (plate + 0.002f), y, c.y + n.y * (plate + 0.002f));
                BlueprintPosters.Sheet(parent, tex, Shared.Pos(face.x, face.y, face.z), Shared.YRot(Mathf.Atan2(n.x, n.y)), s.w, Height, 0, "Enamel_" + s.image);
                parent.GetChild(parent.childCount - 1).GetComponent<MeshRenderer>().sharedMaterial = mat;
                var size = Mathf.Abs(n.x) > 0.5f ? new Vector3(plate, Height, s.w) : new Vector3(s.w, Height, plate);
                b.Box(new Vector3(c.x + n.x * (plate / 2 + 0.001f), y, c.y + n.y * (plate / 2 + 0.001f)), size, steel);
                Debug.Log($"[enamel] {s.image} on the {s.wall} wall at {s.t:0.00}, {s.w:0.00} × {Height:0.00} m, bottom {s.bottom:0.00} m");
            }
            b.Build(parent, "EnamelPlates", shadows: false);
        }
    }
}
