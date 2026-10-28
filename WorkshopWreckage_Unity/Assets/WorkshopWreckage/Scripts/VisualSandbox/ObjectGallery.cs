using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage.Sandbox
{
    /// <summary>One representative of every distinct object in the loaded preset's database (tools, components,
    /// robot parts, everything): the definitions grouped by geometry key, the first of each group shown, at its real
    /// size, resting as it would on the floor (long parts lying down). Rows per category, wrapped at RowWidth, never
    /// overlapping. A page shows every category, or one (the panel's Gallery page).</summary>
    public class ObjectGallery
    {
        public const float RowWidth = 8f, MinCell = 0.12f, Gap = 0.06f, CategoryGap = 0.35f;
        public readonly SandboxItems items;
        public readonly ItemDatabase db;
        public readonly Vector3 origin;   // the front left corner
        public readonly List<(string name, Vector3 at)> headers = new();
        public readonly List<int> categories = new();   // categories with anything in them (the pages)
        public int page = -1;                          // -1 every category, else an index into categories
        public Bounds bounds;
        public int distinct;

        public ObjectGallery(ItemDatabase db, SandboxItems items, Vector3 origin)
        {
            this.db = db;
            this.items = items;
            this.origin = origin;
            foreach (var c in Representatives().Select(d => d.category).Distinct().OrderBy(c => c)) categories.Add(c);
        }

        public string PageName => page < 0 ? "All categories" : ItemDefinition.CategoryNames[categories[page]];

        /// <summary>The first definition of every geometry key, in catalogue order.</summary>
        public List<ItemDefinition> Representatives() =>
            db.order.Select(db.Get).Where(d => d != null && !string.IsNullOrEmpty(d.geometryKey)).GroupBy(d => d.geometryKey).Select(g => g.First()).ToList();

        /// <summary>How the part lies on the floor: long parts (rods, shafts, tools) along X, the rest as modelled.</summary>
        static Quaternion Rest(ItemDefinition def) => def.restMode == RestMode.Long ? Quaternion.Euler(0, 0, 90) : Quaternion.identity;

        public void Build()
        {
            var reps = Representatives();
            distinct = reps.Count;
            var show = reps.Where(d => page < 0 || d.category == categories[page]).OrderBy(d => d.category).ThenBy(d => d.geometryKey).ToList();
            var w = new ItemWorld(db);
            headers.Clear();
            float z = origin.z;
            int cat = -1;
            float x = 0, rowDepth = 0;
            bounds = new Bounds(origin, Vector3.zero);
            var groups = new List<string>();
            foreach (var def in show)
            {
                var e = MeshLibrary.Get(def.geometryKey, MeshLibrary.LodWorld);
                var rot = Rest(def);
                float s = def.worldScale;
                // the rested part's world box
                var ext = e.bounds.extents * s;
                var size = Vector3.zero;
                for (int k = 0; k < 3; k++) size += Abs(rot * Axis(k)) * ext[k] * 2;
                if (def.category != cat)
                {
                    if (cat >= 0) z += rowDepth + CategoryGap;
                    cat = def.category;
                    x = 0; rowDepth = 0;
                    headers.Add((ItemDefinition.CategoryNames[cat], new Vector3(origin.x - 0.1f, 0.02f, z)));
                }
                float cw = Mathf.Max(MinCell, size.x + Gap);
                if (x > 0 && x + cw > RowWidth) { z += rowDepth; x = 0; rowDepth = 0; }
                float cd = Mathf.Max(MinCell, size.z + Gap);
                var centre = new Vector3(origin.x + x + cw / 2, size.y / 2, z + cd / 2);
                var pos = centre - rot * (e.bounds.center * s);
                var inst = w.Add(def.id, Matrix4x4.TRS(pos, rot, Vector3.one * s));
                inst.state = ItemState.InWorld;
                groups.Add(ItemDefinition.CategoryNames[cat]);
                bounds.Encapsulate(new Bounds(centre, size));
                x += cw;
                rowDepth = Mathf.Max(rowDepth, cd);
            }
            items.Show(w);
            for (int k = 0; k < items.entries.Count; k++) items.entries[k].group = groups[k];
            Debug.Log($"[sandbox] gallery: {show.Count} of {distinct} distinct objects ({PageName}), {bounds.size.x:0.0} x {bounds.size.z:0.0} m");
        }

        static Vector3 Axis(int k) => k == 0 ? Vector3.right : k == 1 ? Vector3.up : Vector3.forward;
        static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
