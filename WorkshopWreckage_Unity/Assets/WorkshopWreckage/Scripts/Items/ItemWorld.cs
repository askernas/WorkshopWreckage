using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Authoritative logical state of every item instance.</summary>
    public class ItemWorld
    {
        public readonly ItemDatabase db;
        public readonly List<ItemInstance> instances = new();
        readonly Dictionary<int, List<int>> _byDef = new();
        public int seed;
        public long layoutHash;

        public event Action<ItemInstance, ItemState> StateChanged;
        public event Action<ItemInstance> TransformChanged;

        public ItemWorld(ItemDatabase db) { this.db = db; }

        public int Count => instances.Count;
        public ItemDefinition DefOf(int id) => db.Get(instances[id].defId);
        public int TotalOf(int defId) => _byDef.TryGetValue(defId, out var l) ? l.Count : 0;
        public List<int> IdsOf(int defId) => _byDef.TryGetValue(defId, out var l) ? l : new List<int>();

        public ItemInstance Add(int defId, Matrix4x4 m)
        {
            var inst = new ItemInstance { id = instances.Count, defId = defId, transform = m };
            instances.Add(inst);
            if (!_byDef.TryGetValue(defId, out var l)) _byDef[defId] = l = new List<int>();
            l.Add(inst.id);
            return inst;
        }

        public int CountInState(ItemState s)
        {
            int n = 0;
            foreach (var i in instances) if (i.state == s) n++;
            return n;
        }

        public void SetState(int id, ItemState state, string owner)
        {
            var inst = instances[id];
            var old = inst.state;
            inst.state = state;
            inst.owner = owner;
            StateChanged?.Invoke(inst, old);
        }

        public void SetTransform(int id, Matrix4x4 m)
        {
            var inst = instances[id];
            inst.transform = m;
            inst.moved = true;
            TransformChanged?.Invoke(inst);
        }

        /// <summary>Loads the reference layout exported by the Godot generator
        /// (shared/generated/world_SEED_N.json): identical piles in both engines.</summary>
        public static ItemWorld LoadReference(ItemDatabase db, int seed, int count, WorkshopLayout layout, List<string> errors)
        {
            var path = Shared.GeneratedPath($"world_{seed}_{count}.json");
            if (!System.IO.File.Exists(path))
            {
                errors.Add($"No reference world for seed {seed} with {count} items ({path}). Run tools/export_shared.gd in the Godot project.");
                return null;
            }
            var d = Shared.ReadObject(path);
            var w = new ItemWorld(db) { seed = seed, layoutHash = (long)Json.Num(d, "layout_hash") };
            foreach (var row in Json.Arr(d["instances"]))
            {
                var a = (List<object>)row;
                int id = (int)(double)a[0];
                int defId = (int)(double)a[1];
                if (db.Get(defId) == null) { errors.Add($"Instance {id}: unknown definition {defId}"); continue; }
                var o = new Vector3(Json.F(a[2]), Json.F(a[3]), Json.F(a[4]));
                var bx = new Vector3(Json.F(a[5]), Json.F(a[6]), Json.F(a[7]));
                var by = new Vector3(Json.F(a[8]), Json.F(a[9]), Json.F(a[10]));
                var bz = new Vector3(Json.F(a[11]), Json.F(a[12]), Json.F(a[13]));
                var inst = w.Add(defId, Shared.Matrix(bx, by, bz, o));
                if (inst.id != id) errors.Add($"Instance id mismatch {id} vs {inst.id}");
                inst.zone = layout.ZoneAt(inst.Position);
            }
            if (count == Json.Int(db.config, "total_items", 4096))
            {
                foreach (var defId in db.order)
                    if (w.TotalOf(defId) != db.Get(defId).spawnCount)
                        errors.Add($"Reference world has {w.TotalOf(defId)} copies of {defId}, catalog says {db.Get(defId).spawnCount}");
            }
            return w;
        }
    }
}
