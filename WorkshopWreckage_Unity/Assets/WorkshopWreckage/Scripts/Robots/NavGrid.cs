using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Floor walkability grid for small robots, built from the procedural
    /// Layout (storage units, furniture, benches, repair bay), inflated by the robot
    /// radius. A* with 8 neighbours plus line-of-sight smoothing. Unity XZ space.
    /// Loose parts are not obstacles — robots wade through scatter like the player.</summary>
    public class NavGrid
    {
        public const float Cell = 0.2f;
        readonly Vector2 _origin;
        public readonly int w, h;
        readonly bool[] _blocked, _static;
        readonly float _radius;

        /// <param name="robotHeight">robots lower than a bench top walk underneath benches</param>
        public NavGrid(WorkshopLayout layout, float radius, float robotHeight = 1f)
        {
            _radius = radius;
            _origin = new Vector2(layout.roomMin.x, -layout.roomMax.z);   // Layout z max = Unity z min
            w = Mathf.CeilToInt((layout.roomMax.x - layout.roomMin.x) / Cell);
            h = Mathf.CeilToInt((layout.roomMax.z - layout.roomMin.z) / Cell);
            _blocked = new bool[w * h];
            var rects = new List<Rect>(layout.floorBlockers);
            foreach (var f in layout.furniture)
            {
                // benches: robots are too tall to walk underneath
                if (Json.Str(f, "type") != "bench" || robotHeight < 0.8f) continue;
                var p = WorkshopLayout.V(f["pos"]); var s = WorkshopLayout.V(f["size"]);
                rects.Add(new Rect(p.x - s.x / 2, p.z - s.z / 2, s.x, s.z));
            }
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    var c = CellCenter(x, z);
                    var g = new Vector2(c.x, -c.z);   // back to layout XZ for the layout rects
                    bool b = g.x < layout.roomMin.x + radius || g.x > layout.roomMax.x - radius || g.y < layout.roomMin.z + radius || g.y > layout.roomMax.z - radius;
                    for (int i = 0; i < rects.Count && !b; i++)
                    {
                        var r = rects[i];
                        b = g.x > r.xMin - radius && g.x < r.xMax + radius && g.y > r.yMin - radius && g.y < r.yMax + radius;
                    }
                    _blocked[z * w + x] = b;
                }
            _static = (bool[])_blocked.Clone();
        }

        /// <summary>Marks heaps taller than the robot can step over as obstacles (call
        /// now and then — heaps shrink as parts are collected).</summary>
        public void RefreshItems(ItemWorld world, float stepHeight)
        {
            System.Array.Copy(_static, _blocked, _blocked.Length);
            int r = Mathf.CeilToInt(_radius / Cell);
            foreach (var inst in world.instances)
            {
                if (inst.state != ItemState.InWorld || inst.Position.y < stepHeight) continue;
                var c = CellOf(inst.Position);
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = c.x + dx, z = c.y + dz;
                        if (x >= 0 && z >= 0 && x < w && z < h) _blocked[z * w + x] = true;
                    }
            }
        }

        public Vector3 CellCenter(int x, int z) => new(_origin.x + (x + 0.5f) * Cell, 0, _origin.y + (z + 0.5f) * Cell);
        public Vector2Int CellOf(Vector3 p) => new(Mathf.Clamp(Mathf.FloorToInt((p.x - _origin.x) / Cell), 0, w - 1), Mathf.Clamp(Mathf.FloorToInt((p.z - _origin.y) / Cell), 0, h - 1));
        public bool Blocked(Vector2Int c) => c.x < 0 || c.y < 0 || c.x >= w || c.y >= h || _blocked[c.y * w + c.x];
        public bool Walkable(Vector3 p) => !Blocked(CellOf(p));
        /// <summary>Blocked by parts lying there (piles), not just by furniture and its safety margin.</summary>
        public bool BlockedByItems(Vector3 p) { var c = CellOf(p); int i = c.y * w + c.x; return _blocked[i] && !_static[i]; }

        /// <summary>Nearest walkable point within maxDist of p (null if none).</summary>
        public Vector3? NearestFree(Vector3 p, float maxDist)
        {
            var c0 = CellOf(p);
            int r = Mathf.CeilToInt(maxDist / Cell);
            Vector3? best = null;
            float bd = float.MaxValue;
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var c = new Vector2Int(c0.x + dx, c0.y + dz);
                    if (Blocked(c)) continue;
                    var q = CellCenter(c.x, c.y);
                    float d = new Vector2(q.x - p.x, q.z - p.z).magnitude;
                    if (d <= maxDist && d < bd) { bd = d; best = q; }
                }
            return best;
        }

        /// <summary>Smoothed path of floor points from a to b, or null if unreachable.</summary>
        public List<Vector3> Path(Vector3 a, Vector3 b)
        {
            var s = CellOf(a);
            if (Blocked(s)) { var f = NearestFree(a, 1.0f); if (f == null) return null; s = CellOf(f.Value); }
            var t = CellOf(b);
            if (Blocked(t)) return null;
            int n = w * h, si = s.y * w + s.x, ti = t.y * w + t.x;
            // search state reused between searches: a cell is fresh unless stamped with this search's number
            // (performance pass 2026-11-01: the arrays were allocated and the open set kept sorted per search)
            if (_seen == null) { _seen = new int[n]; _g = new float[n]; _from = new int[n]; _closed = new int[n]; }
            if (++_search == int.MaxValue) { System.Array.Clear(_seen, 0, n); System.Array.Clear(_closed, 0, n); _search = 1; }
            int stamp = _search;
            var g = _g; var from = _from;
            float G(int i) => _seen[i] == stamp ? g[i] : float.MaxValue;
            g[si] = 0; from[si] = -1; _seen[si] = stamp;
            // open list: a binary heap ordered by (f, cell) like the sorted set it replaces, so the cells are
            // expanded in the same order; an entry made stale by a cheaper one is skipped when it comes up
            _open.Clear();
            Push((H(si, ti), si));
            // an unreachable target would otherwise flood the whole floor (seconds on Massive):
            // give up after a bounded search, the robot then treats the part as unreachable
            int expanded = 0, maxExpand = Mathf.Min(n, 6000);
            while (_open.Count > 0)
            {
                var cur = Pop();
                int ci = cur.i;
                if (ci == ti) break;
                if (_closed[ci] == stamp) continue;
                if (cur.f > G(ci) + H(ci, ti) + 1e-4f) continue;   // stale: a cheaper way here was found later
                _closed[ci] = stamp;
                if (++expanded > maxExpand) return null;
                int cx = ci % w, cz = ci / w;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                        int ni = nz * w + nx;
                        if (_blocked[ni] || _closed[ni] == stamp) continue;
                        if (dx != 0 && dz != 0 && (_blocked[cz * w + nx] || _blocked[nz * w + cx])) continue;   // no corner cutting
                        float ng = g[ci] + (dx != 0 && dz != 0 ? 1.4142f : 1f);
                        if (ng >= G(ni)) continue;
                        g[ni] = ng;
                        from[ni] = ci;
                        _seen[ni] = stamp;
                        Push((ng + H(ni, ti), ni));
                    }
            }
            if (si != ti && (_seen[ti] != stamp || from[ti] < 0)) return null;
            var cells = new List<int>();
            for (int i = ti; i >= 0; i = i == si ? -1 : from[i]) cells.Add(i);
            cells.Reverse();
            // string pulling: keep only the turning points that are needed
            var pts = new List<Vector3>();
            int anchor = 0;
            pts.Add(CellCenter(cells[0] % w, cells[0] / w));
            for (int k = 2; k < cells.Count; k++)
                if (!LineClear(cells[anchor], cells[k])) { anchor = k - 1; pts.Add(CellCenter(cells[anchor] % w, cells[anchor] / w)); }
            pts.Add(b);
            return pts;
        }

        int[] _seen, _closed, _from;
        float[] _g;
        int _search;
        readonly List<(float f, int i)> _open = new();

        static bool Before((float f, int i) p, (float f, int i) q) => p.f != q.f ? p.f < q.f : p.i < q.i;

        void Push((float f, int i) e)
        {
            _open.Add(e);
            int k = _open.Count - 1;
            while (k > 0)
            {
                int up = (k - 1) / 2;
                if (!Before(_open[k], _open[up])) break;
                (_open[k], _open[up]) = (_open[up], _open[k]);
                k = up;
            }
        }

        (float f, int i) Pop()
        {
            var top = _open[0];
            int last = _open.Count - 1;
            _open[0] = _open[last];
            _open.RemoveAt(last);
            int k = 0, n = _open.Count;
            while (true)
            {
                int l = 2 * k + 1, r = l + 1, m = k;
                if (l < n && Before(_open[l], _open[m])) m = l;
                if (r < n && Before(_open[r], _open[m])) m = r;
                if (m == k) break;
                (_open[k], _open[m]) = (_open[m], _open[k]);
                k = m;
            }
            return top;
        }

        float H(int a, int b)
        {
            int dx = Mathf.Abs(a % w - b % w), dz = Mathf.Abs(a / w - b / w);
            return Mathf.Max(dx, dz) + 0.4142f * Mathf.Min(dx, dz);
        }

        bool LineClear(int a, int b)
        {
            var p = CellCenter(a % w, a / w); var q = CellCenter(b % w, b / w);
            int steps = Mathf.CeilToInt(Vector3.Distance(p, q) / (Cell * 0.4f));
            for (int i = 1; i < steps; i++)
            {
                var m = Vector3.Lerp(p, q, i / (float)steps);
                // check a small cross so the robot body does not clip corners
                if (!Walkable(m) || !Walkable(m + new Vector3(Cell * 0.45f, 0, 0)) || !Walkable(m - new Vector3(Cell * 0.45f, 0, 0))
                    || !Walkable(m + new Vector3(0, 0, Cell * 0.45f)) || !Walkable(m - new Vector3(0, 0, Cell * 0.45f))) return false;
            }
            return true;
        }
    }
}
