using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage.Pegboard
{
    /// <summary>Draws the board's tools while they lie in the room (the item renderer leaves them out: 80 unique
    /// meshes there cost ~500 draw calls a frame). Merged by ToolBatch; rebuilt at once when a tool is picked up,
    /// put down or hidden (flying), and at most every RebuildEvery seconds while tools only move (settling).</summary>
    public class ScatteredTools : MonoBehaviour
    {
        const float RebuildEvery = 0.25f;
        ItemWorld _world;
        ItemRenderer _renderer;
        int[] _ids;
        bool[] _shown;
        bool _moved;
        float _last = -1;
        Transform _root;

        public static ScatteredTools Attach(ItemWorld world, ItemRenderer renderer)
        {
            var s = renderer.gameObject.AddComponent<ScatteredTools>();
            s._world = world;
            s._renderer = renderer;
            var ids = new List<int>();
            foreach (var i in world.instances) if (world.DefOf(i.id).IsPegboardTool) ids.Add(i.id);
            s._ids = ids.ToArray();
            s._shown = new bool[ids.Count];
            s._root = new GameObject("ScatteredTools").transform;
            s._root.SetParent(renderer.transform, false);
            var set = new HashSet<int>(ids);
            world.TransformChanged += inst => { if (set.Contains(inst.id)) s._moved = true; };
            s.Rebuild();
            return s;
        }

        bool Shows(int id) => _world.instances[id].state == ItemState.InWorld && !_renderer.IsHidden(id);

        void LateUpdate()
        {
            if (Game.Booting) return;   // the game is still being built (loading splash)
            if (_ids == null) return;
            bool changed = false;
            for (int k = 0; k < _ids.Length; k++) if (Shows(_ids[k]) != _shown[k]) { changed = true; break; }
            if (changed || _moved && Time.unscaledTime - _last >= RebuildEvery) Rebuild();
        }

        void Rebuild()
        {
            _moved = false;
            _last = Time.unscaledTime;
            ToolBatch.Clear(_root);
            var list = new List<(ItemDefinition, Matrix4x4)>();
            for (int k = 0; k < _ids.Length; k++)
            {
                int id = _ids[k];
                _shown[k] = Shows(id);
                if (_shown[k]) list.Add((_world.DefOf(id), _world.instances[id].transform));
            }
            ToolBatch.Build(_root, list, 0, "room_tools");
        }
    }
}
