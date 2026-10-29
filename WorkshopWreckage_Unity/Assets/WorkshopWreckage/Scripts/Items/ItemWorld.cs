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

    }
}
