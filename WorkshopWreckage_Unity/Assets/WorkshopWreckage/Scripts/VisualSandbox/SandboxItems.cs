using System.Collections.Generic;
using UnityEngine;
using WorkshopWreckage.Pegboard;

namespace WorkshopWreckage.Sandbox
{
    /// <summary>A set of items drawn exactly as the game draws them: an ItemWorld of their own, the game's
    /// ItemRenderer (instanced, skins, the item buffer) and, for the pegboard's tools, ScatteredTools (ToolBatch).
    /// Rebuilding reuses the renderer, its materials and the tool batch's root; the old meshes are destroyed.</summary>
    public class SandboxItems : MonoBehaviour
    {
        public ItemWorld world;
        public ItemRenderer itemRenderer;
        ScatteredTools _tools;

        public class Entry
        {
            public int id;
            public ItemDefinition def;
            public Vector3 centre;
            public float radius;
            public string group;
        }

        public readonly List<Entry> entries = new();

        public static SandboxItems Create(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<SandboxItems>();
            s.itemRenderer = go.AddComponent<ItemRenderer>();
            return s;
        }

        /// <summary>Draw `w` from now on (its instances' transforms already final).</summary>
        public void Show(ItemWorld w)
        {
            ClearTools();
            world = w;
            itemRenderer.Build(w);
            _tools = ScatteredTools.Attach(w, itemRenderer);
            entries.Clear();
            foreach (var inst in w.instances)
            {
                var def = w.DefOf(inst.id);
                var e = MeshLibrary.Get(def.geometryKey, MeshLibrary.LodWorld);
                float s = inst.transform.GetColumn(0).magnitude;
                entries.Add(new Entry { id = inst.id, def = def, centre = inst.transform.MultiplyPoint3x4(e.bounds.center), radius = e.bounds.extents.magnitude * s });
            }
        }

        /// <summary>The same items again (same world, same transforms): picks up a new geometry quality.</summary>
        public void Redraw() { if (world != null) Show(world); }

        void ClearTools()
        {
            if (_tools == null) return;
            var root = itemRenderer.transform.Find("ScatteredTools");
            if (root != null) { ToolBatch.Clear(root); Destroy(root.gameObject); }
            Destroy(_tools);
            _tools = null;
        }

        /// <summary>Total triangles of the items as drawn (the mesh each one's group uses), for the stats readout.</summary>
        public long Triangles()
        {
            long n = 0;
            foreach (var e in entries)
            {
                var m = itemRenderer.WorldMeshFor(e.id);
                for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3;
            }
            return n;
        }

        /// <summary>The entry whose centre lies nearest the screen point (within its projected size), or null.</summary>
        public Entry Pick(Camera cam, Vector2 screen)
        {
            Entry best = null;
            float bestD = float.MaxValue;
            foreach (var e in entries)
            {
                var sp = cam.WorldToScreenPoint(e.centre);
                if (sp.z <= 0) continue;
                float px = e.radius / (sp.z * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * Screen.height * 0.5f;
                float d = Vector2.Distance(screen, sp);
                if (d < Mathf.Max(px, 10) && sp.z < bestD) { best = e; bestD = sp.z; }
            }
            return best;
        }
    }
}
