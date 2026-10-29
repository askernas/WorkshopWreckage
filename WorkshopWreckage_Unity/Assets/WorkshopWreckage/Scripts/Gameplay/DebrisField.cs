using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Height of loose parts lying on the floor, so the player and robots
    /// walk OVER them instead of through them. A fine grid (Unity XZ) stores the top
    /// of the parts above each cell (their world AABB tops, footprint shrunk a little
    /// so thin parts don't make wide steps). It is kept current incrementally: when a
    /// part is picked up, dropped or moved only the cells it covered are recomputed.
    /// Parts lying over furniture footprints (shelving, cabinets, benches) don't
    /// count: they rest on the furniture, not underfoot.</summary>
    public class DebrisField
    {
        public const float Cell = 0.06f;
        const float Bucket = 0.5f, Shrink = 0.7f;
        readonly ItemWorld _world;
        readonly Vector2 _origin;
        readonly int _w, _h;
        readonly float[] _top;
        readonly Dictionary<int, Rect> _rect = new();          // footprint per counted item
        readonly Dictionary<int, float> _height = new();
        readonly Dictionary<long, List<int>> _buckets = new();

        readonly List<Rect> _furniture;   // Unity XZ footprints of units/benches: parts there rest on furniture, not underfoot

        public DebrisField(ItemWorld world, ItemSpatialIndex index, Vector3 roomMinU, Vector3 roomMaxU, List<Rect> furniture)
        {
            _furniture = furniture;
            _world = world;
            _origin = new Vector2(roomMinU.x, roomMinU.z);
            _w = Mathf.CeilToInt((roomMaxU.x - roomMinU.x) / Cell) + 1;
            _h = Mathf.CeilToInt((roomMaxU.z - roomMinU.z) / Cell) + 1;
            _top = new float[_w * _h];
            _index = index;
            foreach (var inst in world.instances) if (inst.state == ItemState.InWorld) Add(inst.id, false);
            world.StateChanged += (inst, _) => Changed(inst.id);
            world.TransformChanged += inst => Changed(inst.id);
        }

        readonly ItemSpatialIndex _index;

        static long Key(int bx, int bz) => ((long)bx << 32) ^ (uint)bz;

        void Add(int id, bool restamp)
        {
            var b = _index.WorldBounds(id);
            foreach (var f in _furniture) if (f.Contains(new Vector2(b.center.x, b.center.z))) return;                 // on a shelf / bench: not underfoot
            var c = new Vector2(b.center.x, b.center.z);
            var s = new Vector2(b.size.x, b.size.z) * Shrink;
            var r = new Rect(c - s * 0.5f, s);
            _rect[id] = r;
            _height[id] = b.max.y;
            int bx0 = Mathf.FloorToInt(r.xMin / Bucket), bx1 = Mathf.FloorToInt(r.xMax / Bucket);
            int bz0 = Mathf.FloorToInt(r.yMin / Bucket), bz1 = Mathf.FloorToInt(r.yMax / Bucket);
            for (int bx = bx0; bx <= bx1; bx++)
                for (int bz = bz0; bz <= bz1; bz++)
                {
                    if (!_buckets.TryGetValue(Key(bx, bz), out var l)) _buckets[Key(bx, bz)] = l = new List<int>();
                    l.Add(id);
                }
            Stamp(r, b.max.y);
        }

        void Remove(int id)
        {
            if (!_rect.TryGetValue(id, out var r)) return;
            _rect.Remove(id);
            _height.Remove(id);
            int bx0 = Mathf.FloorToInt(r.xMin / Bucket), bx1 = Mathf.FloorToInt(r.xMax / Bucket);
            int bz0 = Mathf.FloorToInt(r.yMin / Bucket), bz1 = Mathf.FloorToInt(r.yMax / Bucket);
            for (int bx = bx0; bx <= bx1; bx++)
                for (int bz = bz0; bz <= bz1; bz++)
                    if (_buckets.TryGetValue(Key(bx, bz), out var l)) l.Remove(id);
            Recompute(r);
        }

        void Changed(int id)
        {
            Remove(id);
            if (_world.instances[id].state == ItemState.InWorld) Add(id, true);
        }

        void CellRange(Rect r, out int x0, out int x1, out int z0, out int z1)
        {
            x0 = Mathf.Clamp(Mathf.FloorToInt((r.xMin - _origin.x) / Cell), 0, _w - 1);
            x1 = Mathf.Clamp(Mathf.FloorToInt((r.xMax - _origin.x) / Cell), 0, _w - 1);
            z0 = Mathf.Clamp(Mathf.FloorToInt((r.yMin - _origin.y) / Cell), 0, _h - 1);
            z1 = Mathf.Clamp(Mathf.FloorToInt((r.yMax - _origin.y) / Cell), 0, _h - 1);
        }

        void Stamp(Rect r, float top)
        {
            CellRange(r, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (top > _top[z * _w + x]) _top[z * _w + x] = top;
        }

        void Recompute(Rect r)
        {
            CellRange(r, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++) _top[z * _w + x] = 0;
            var seen = new HashSet<int>();
            int bx0 = Mathf.FloorToInt(r.xMin / Bucket) - 1, bx1 = Mathf.FloorToInt(r.xMax / Bucket) + 1;
            int bz0 = Mathf.FloorToInt(r.yMin / Bucket) - 1, bz1 = Mathf.FloorToInt(r.yMax / Bucket) + 1;
            for (int bx = bx0; bx <= bx1; bx++)
                for (int bz = bz0; bz <= bz1; bz++)
                    if (_buckets.TryGetValue(Key(bx, bz), out var l))
                        foreach (var id in l)
                        {
                            if (!seen.Add(id)) continue;
                            var o = _rect[id];
                            if (!o.Overlaps(r)) continue;
                            var clip = Rect.MinMaxRect(Mathf.Max(o.xMin, r.xMin), Mathf.Max(o.yMin, r.yMin), Mathf.Min(o.xMax, r.xMax), Mathf.Min(o.yMax, r.yMax));
                            Stamp(clip, _height[id]);
                        }
        }

        public float At(Vector3 p)
        {
            int x = Mathf.FloorToInt((p.x - _origin.x) / Cell), z = Mathf.FloorToInt((p.z - _origin.y) / Cell);
            if (x < 0 || z < 0 || x >= _w || z >= _h) return 0;
            return _top[z * _w + x];
        }

        /// <summary>Highest debris under a foot disc of radius r (centre + 8 points).</summary>
        public float Under(Vector3 p, float r)
        {
            float m = At(p);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4;
                m = Mathf.Max(m, At(p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r));
            }
            return m;
        }
    }
}
