using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Destination contents and rules (port of StorageSystem.gd).</summary>
    public class StorageSystem
    {
        public enum Result { Ok, WrongBox, Full, UnknownBox, Blocked }
        /// <summary>Stray parts (unsorted, belonging elsewhere) lying in a destination: it
        /// can't be filled until they are taken out. Set by the game (needs the spatial index).</summary>
        public Func<string, int> straysIn;
        public readonly ItemDatabase db;
        public readonly ItemWorld world;
        public readonly Dictionary<string, List<int>> contents = new();
        public Func<string, int, int, Matrix4x4> placement;
        public event Action<string, int> Deposited;

        public StorageSystem(ItemDatabase db, ItemWorld world) { this.db = db; this.world = world; }

        public List<int> Stored(string box) => contents.TryGetValue(box, out var l) ? l : new List<int>();

        public Result Check(string box, int id)
        {
            if (!db.boxes.ContainsKey(box) && !db.stations.ContainsKey(box)) return Result.UnknownBox;
            if (world.DefOf(id).storageBox != box) return Result.WrongBox;
            if (Stored(box).Count >= db.BoxCapacity(box)) return Result.Full;
            if (straysIn != null && straysIn(box) > 0) return Result.Blocked;
            return Result.Ok;
        }

        /// <summary>Who made the deposit currently being reported ("player" or "robot:&lt;name&gt;").</summary>
        public string lastDepositor = "player";

        public Result Deposit(string box, int id, string by = "player")
        {
            var r = Check(box, id);
            if (r != Result.Ok) return r;
            lastDepositor = by;
            if (!contents.TryGetValue(box, out var l)) contents[box] = l = new List<int>();
            l.Add(id);
            if (placement != null) world.instances[id].transform = placement(box, id, l.Count - 1);
            world.SetState(id, ItemState.Stored, "box:" + box);
            Deposited?.Invoke(box, id);
            return r;
        }

        /// <summary>Takes a part out of a destination's contents (the caller sets its new state).</summary>
        public void Remove(string box, int id)
        {
            if (contents.TryGetValue(box, out var l)) l.Remove(id);
        }

        public string ResultText(Result r, string box, int id) => r switch
        {
            Result.WrongBox => "Wrong destination — this belongs in " + db.BoxLabel(world.DefOf(id).storageBox),
            Result.Full => $"That one is full ({db.BoxCapacity(box)})",
            Result.UnknownBox => "Not a storage destination",
            Result.Blocked => $"Blocked — {straysIn?.Invoke(box)} stray part(s) lie in here; take them out first",
            _ => "",
        };
    }

    /// <summary>What the player carries. Early on every part is its own slot, in the
    /// order it was picked up (you sort your pockets yourself); the Parts Organiser skill
    /// (stacking) groups identical parts into stacks ordered by category and name.</summary>
    public class Inventory
    {
        public readonly ItemWorld world;
        public int capacity = 10;
        public readonly List<int> items = new();
        public int heldDef = -1;
        /// <summary>Parts Organiser: identical parts stack, stacks sorted by type.</summary>
        public bool stacking;
        int _heldSlot = -1;   // without stacking: the held instance
        public event Action Changed;

        public Inventory(ItemWorld w) { world = w; }
        public int Count => items.Count;
        public bool IsFull => items.Count >= capacity;

        public bool Add(int id)
        {
            if (IsFull || items.Contains(id)) return false;
            items.Add(id);
            if (heldDef < 0) { heldDef = world.instances[id].defId; _heldSlot = id; }
            Changed?.Invoke();
            return true;
        }

        public void Remove(int id)
        {
            items.Remove(id);
            if (_heldSlot == id) _heldSlot = -1;
            if (heldDef >= 0 && (stacking ? IdsOf(heldDef).Count == 0 : _heldSlot < 0))
            {
                // the hand takes the next part (the same kind if there is one)
                var same = IdsOf(heldDef);
                if (!stacking && same.Count > 0) _heldSlot = same[^1];
                else if (items.Count > 0) { _heldSlot = items[^1]; heldDef = world.instances[_heldSlot].defId; }
                else heldDef = -1;
            }
            Changed?.Invoke();
        }

        public List<int> IdsOf(int def) => items.Where(i => world.instances[i].defId == def).ToList();

        /// <summary>Hotbar slots: one per part (pickup order) or, with stacking, one per definition.</summary>
        public List<(int defId, List<int> ids)> Stacks()
        {
            if (!stacking) return items.Select(i => (world.instances[i].defId, new List<int> { i })).ToList();
            var o = new List<(int, List<int>)>();
            var idx = new Dictionary<int, int>();
            foreach (var id in items)
            {
                int d = world.instances[id].defId;
                if (!idx.TryGetValue(d, out var k)) { idx[d] = k = o.Count; o.Add((d, new List<int>())); }
                o[k].Item2.Add(id);
            }
            return o.OrderBy(x => world.db.Get(x.Item1).category).ThenBy(x => world.db.Get(x.Item1).displayName).ToList();
        }

        public int HeldId()
        {
            if (heldDef < 0) return -1;
            if (!stacking) return items.Contains(_heldSlot) ? _heldSlot : -1;
            var l = IdsOf(heldDef);
            return l.Count > 0 ? l[^1] : -1;
        }

        /// <summary>Holds a part of this definition (the newest one picked up).</summary>
        public void Select(int def)
        {
            var l = IdsOf(def);
            if (l.Count == 0) return;
            heldDef = def;
            _heldSlot = l[^1];
            Changed?.Invoke();
        }

        /// <summary>Holds hotbar slot k.</summary>
        public void SelectSlot(int k)
        {
            var s = Stacks();
            if (k < 0 || k >= s.Count) return;
            heldDef = s[k].defId;
            _heldSlot = s[k].ids[^1];
            Changed?.Invoke();
        }

        int HeldSlotIndex(List<(int defId, List<int> ids)> s) => Math.Max(0, stacking ? s.FindIndex(x => x.defId == heldDef) : s.FindIndex(x => x.ids.Contains(_heldSlot)));

        public void Cycle(int dir)
        {
            var s = Stacks();
            if (s.Count == 0) return;
            SelectSlot(((HeldSlotIndex(s) + dir) % s.Count + s.Count) % s.Count);
        }

        public bool IsHeldSlot((int defId, List<int> ids) slot) => stacking ? slot.defId == heldDef : slot.ids.Contains(HeldId());
    }

    /// <summary>Discovery, counts, zones, milestones (port of ProgressTracker.gd).</summary>
    public class ProgressTracker
    {
        static readonly int[] SortMilestones = { 1, 10, 25, 50, 100, 250, 500, 1000, 2000, 3000, 4096 };
        readonly ItemWorld world;
        readonly List<string> zoneNames;
        public readonly HashSet<int> discovered = new(), revealed = new();
        public readonly HashSet<string> milestones = new();
        readonly Dictionary<int, int> _collected = new(), _sorted = new();
        int[] _zoneTotal, _zoneSorted;
        public int sortedTotal, robotPartsFound;
        public int CollectedTotal => _collected.Values.Sum();
        public event Action<string, string> Milestone;
        public event Action<int> Discovered, DefinitionCompleted, DefinitionHalf;

        public ProgressTracker(ItemWorld w, List<string> zones)
        {
            world = w;
            zoneNames = zones;
            Recount();
            w.StateChanged += OnState;
        }

        public void Recount()
        {
            _collected.Clear();
            _sorted.Clear();
            _zoneTotal = new int[zoneNames.Count];
            _zoneSorted = new int[zoneNames.Count];
            sortedTotal = robotPartsFound = 0;
            foreach (var i in world.instances)
            {
                if (i.zone >= 0) _zoneTotal[i.zone]++;
                if (i.state != ItemState.InWorld)
                {
                    _collected[i.defId] = _collected.GetValueOrDefault(i.defId) + 1;
                    if (world.DefOf(i.id).IsRobotPart) robotPartsFound++;
                }
                if (Accounted(i.state))
                {
                    _sorted[i.defId] = _sorted.GetValueOrDefault(i.defId) + 1;
                    sortedTotal++;
                    if (i.zone >= 0) _zoneSorted[i.zone]++;
                }
            }
        }

        void OnState(ItemInstance inst, ItemState old)
        {
            var def = world.DefOf(inst.id);
            if (old == ItemState.InWorld && inst.state != ItemState.InWorld)
            {
                _collected[inst.defId] = _collected.GetValueOrDefault(inst.defId) + 1;
                if (def.IsRobotPart) { robotPartsFound++; Mile("robot_part_" + def.id, "Robot component found: " + def.displayName); }
                Discover(inst.defId);
                Mile("first_pickup", "First part collected — every piece counts!");
            }
            else if (old != ItemState.InWorld && inst.state == ItemState.InWorld)
            {
                _collected[inst.defId] = _collected.GetValueOrDefault(inst.defId) - 1;
                if (def.IsRobotPart) robotPartsFound--;
            }
            if (Accounted(inst.state) && !Accounted(old))
            {
                _sorted[inst.defId] = _sorted.GetValueOrDefault(inst.defId) + 1;
                sortedTotal++;
                if (inst.zone >= 0)
                {
                    _zoneSorted[inst.zone]++;
                    float pct = ZonePercent(inst.zone);
                    foreach (var step in new[] { 25, 50, 75, 100 })
                        if (pct >= step) Mile($"zone_{inst.zone}_{step}", $"{zoneNames[inst.zone]} {step}% restored");
                }
                // unique robot parts follow the workbench's rules, not collection completion
                int total = world.TotalOf(inst.defId), have = SortedOf(inst.defId);
                if (!def.IsRobotPart && total >= 2 && have == (total + 1) / 2 && have < total) DefinitionHalf?.Invoke(inst.defId);
                if (!def.IsRobotPart && have == total) DefinitionCompleted?.Invoke(inst.defId);
                foreach (var m in SortMilestones)
                    if (sortedTotal == m) Mile("sorted_" + m, m == 1 ? "First part sorted!" : $"{m} parts sorted");
            }
            else if (!Accounted(inst.state) && Accounted(old))
            {
                _sorted[inst.defId] = _sorted.GetValueOrDefault(inst.defId) - 1;
                sortedTotal--;
                if (inst.zone >= 0) _zoneSorted[inst.zone]--;
            }
        }

        /// <summary>Accounted for in its collection: stored in its destination, or
        /// installed at the Robot Workbench (robot part, or consumed by a recipe).
        /// Parts in staging zones, in hands or carried by robots are not.</summary>
        public static bool Accounted(ItemState s) => s == ItemState.Stored || s == ItemState.Installed;

        /// <summary>Every copy of the definition accounted for (the collection is complete).</summary>
        public bool IsComplete(int defId) => world.TotalOf(defId) > 0 && SortedOf(defId) == world.TotalOf(defId);

        void Mile(string key, string text)
        {
            if (milestones.Add(key)) Milestone?.Invoke(key, text);
        }

        public void Discover(int defId)
        {
            if (discovered.Add(defId)) Discovered?.Invoke(defId);
        }

        public bool IsDiscovered(int defId) => discovered.Contains(defId);
        public int CollectedOf(int d) => _collected.GetValueOrDefault(d);
        public int SortedOf(int d) => _sorted.GetValueOrDefault(d);
        public string CountLabel(int d) => revealed.Contains(d) ? $"{CollectedOf(d)} / {world.TotalOf(d)} collected" : $"{CollectedOf(d)} collected";
        public float ZonePercent(int z) => 100f * _zoneSorted[z] / Mathf.Max(1, _zoneTotal[z]);
        public int ZoneSorted(int z) => _zoneSorted[z];
        public int ZoneRemaining(int z) => world.instances.Count(i => i.zone == z && !Accounted(i.state));
        public float OverallPercent => 100f * sortedTotal / Mathf.Max(1, world.Count);

        public Dictionary<string, object> ToDict() => new()
        {
            ["discovered"] = discovered.Select(x => (object)x).ToList(),
            ["revealed"] = revealed.Select(x => (object)x).ToList(),
            ["milestones"] = milestones.Select(x => (object)x).ToList(),
        };

        public void Load(Dictionary<string, object> d)
        {
            discovered.Clear(); revealed.Clear(); milestones.Clear();
            foreach (var x in Json.Arr(d.GetValueOrDefault("discovered"))) discovered.Add((int)(double)x);
            foreach (var x in Json.Arr(d.GetValueOrDefault("revealed"))) revealed.Add((int)(double)x);
            foreach (var x in Json.Arr(d.GetValueOrDefault("milestones"))) milestones.Add(x.ToString());
            Recount();
        }
    }

    /// <summary>Uniform hash grid + 3D-DDA ray walk + OBB tests (port of ItemSpatialIndex.gd).</summary>
    public class ItemSpatialIndex
    {
        const float Cell = 0.2f;
        readonly ItemWorld world;
        readonly Dictionary<Vector3Int, List<int>> _cells = new();
        readonly Dictionary<int, List<Vector3Int>> _cellsOf = new();
        public long lastPickTicks;
        public int lastPickCandidates;
        public double LastPickMicros => lastPickTicks * 1e6 / System.Diagnostics.Stopwatch.Frequency;

        public ItemSpatialIndex(ItemWorld w)
        {
            world = w;
            foreach (var i in w.instances) if (i.state == ItemState.InWorld) Insert(i.id);
        }

        Bounds UnitBounds(int id) => MeshLibrary.UnitBounds(world.DefOf(id).geometryKey);

        public Bounds WorldBounds(int id)
        {
            var m = world.instances[id].transform;
            var b = UnitBounds(id);
            var c = m.MultiplyPoint3x4(b.center);
            var e = b.extents;
            var ax = m.GetColumn(0) * e.x; var ay = m.GetColumn(1) * e.y; var az = m.GetColumn(2) * e.z;
            var ext = new Vector3(Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x), Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y), Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
            return new Bounds(c, ext * 2);
        }

        static Vector3Int CellOf(Vector3 p) => new(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell), Mathf.FloorToInt(p.z / Cell));

        public void Insert(int id)
        {
            Remove(id);
            var b = WorldBounds(id);
            var a = CellOf(b.min); var c = CellOf(b.max);
            var list = new List<Vector3Int>();
            for (int x = a.x; x <= c.x; x++)
                for (int y = a.y; y <= c.y; y++)
                    for (int z = a.z; z <= c.z; z++)
                    {
                        var k = new Vector3Int(x, y, z);
                        if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<int>();
                        l.Add(id);
                        list.Add(k);
                    }
            _cellsOf[id] = list;
        }

        public void Remove(int id)
        {
            if (!_cellsOf.TryGetValue(id, out var list)) return;
            foreach (var k in list)
            {
                var l = _cells[k];
                l.Remove(id);
                if (l.Count == 0) _cells.Remove(k);
            }
            _cellsOf.Remove(id);
        }

        public bool Contains(int id) => _cellsOf.ContainsKey(id);

        /// <summary>Nearest item hit along the ray: (id, distance) or (-1, inf).</summary>
        public (int id, float dist) Pick(Vector3 origin, Vector3 dir, float maxDist, HashSet<int> exclude = null)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            dir.Normalize();
            int best = -1;
            float bestT = maxDist;
            var tested = new HashSet<int>();
            var cell = CellOf(origin);
            var step = new Vector3Int(Math.Sign(dir.x), Math.Sign(dir.y), Math.Sign(dir.z));
            var tMax = Vector3.zero;
            var tDelta = Vector3.zero;
            for (int a = 0; a < 3; a++)
            {
                if (Mathf.Abs(dir[a]) < 1e-9f) { tMax[a] = float.PositiveInfinity; tDelta[a] = float.PositiveInfinity; }
                else
                {
                    float boundary = (cell[a] + (dir[a] > 0 ? 1 : 0)) * Cell;
                    tMax[a] = (boundary - origin[a]) / dir[a];
                    tDelta[a] = Cell / Mathf.Abs(dir[a]);
                }
            }
            float t = 0;
            while (t <= bestT + Cell)
            {
                if (_cells.TryGetValue(cell, out var l))
                {
                    foreach (var id in l)
                    {
                        if (!tested.Add(id) || (exclude != null && exclude.Contains(id))) continue;
                        float hit = RayObb(id, origin, dir);
                        if (hit >= 0 && hit < bestT) { bestT = hit; best = id; }
                    }
                }
                int ax = 0;
                if (tMax.y < tMax[ax]) ax = 1;
                if (tMax.z < tMax[ax]) ax = 2;
                t = tMax[ax];
                if (float.IsInfinity(t)) break;
                cell[ax] += step[ax];
                tMax[ax] += tDelta[ax];
            }
            lastPickCandidates = tested.Count;
            lastPickTicks = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            return (best, best >= 0 ? bestT : float.PositiveInfinity);
        }

        public float RayObb(int id, Vector3 origin, Vector3 dir)
        {
            var inv = world.instances[id].transform.inverse;
            var o = inv.MultiplyPoint3x4(origin);
            var d = inv.MultiplyVector(dir);
            var b = UnitBounds(id);
            float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;
            for (int a = 0; a < 3; a++)
            {
                float lo = b.min[a], hi = b.max[a];
                if (Mathf.Abs(d[a]) < 1e-12f)
                {
                    if (o[a] < lo || o[a] > hi) return -1;
                }
                else
                {
                    float t1 = (lo - o[a]) / d[a], t2 = (hi - o[a]) / d[a];
                    tmin = Mathf.Max(tmin, Mathf.Min(t1, t2));
                    tmax = Mathf.Min(tmax, Mathf.Max(t1, t2));
                }
            }
            if (tmax < Mathf.Max(tmin, 0)) return -1;
            return Mathf.Max(tmin, 0);
        }

        public List<int> QuerySphere(Vector3 c, float r)
        {
            var o = new List<int>();
            var a = CellOf(c - Vector3.one * r); var b = CellOf(c + Vector3.one * r);
            var seen = new HashSet<int>();
            for (int x = a.x; x <= b.x; x++)
                for (int y = a.y; y <= b.y; y++)
                    for (int z = a.z; z <= b.z; z++)
                        if (_cells.TryGetValue(new Vector3Int(x, y, z), out var l))
                            foreach (var id in l)
                            {
                                if (!seen.Add(id)) continue;
                                var wb = WorldBounds(id);
                                if (Vector3.Distance(wb.ClosestPoint(c), c) <= r) o.Add(id);
                            }
            return o;
        }
    }

    /// <summary>Versioned JSON delta saves (same structure as the Godot SaveSystem).</summary>
    public static class SaveSystem
    {
        /// <summary>2: every saved part also records its definition id (item_defs), so a save
        /// survives catalog changes (SaveMigration). Version 1 saves still load unchanged.</summary>
        public const int Version = 2;
        public static readonly string[] Slots = { "quick", "slot1", "slot2", "slot3" };
        public static string Dir => System.IO.Path.Combine(Application.persistentDataPath, "saves");
        public static string PathOf(string slot) => System.IO.Path.Combine(Dir, slot + ".json");

        public static Dictionary<string, object> Serialize(Game g)
        {
            var items = new List<object>();
            var itemDefs = new List<object>();
            foreach (var i in g.world.instances)
            {
                if (i.state == ItemState.InWorld && !i.moved) continue;
                itemDefs.Add((double)i.defId);
                var e = new List<object> { (double)i.id, (double)(int)i.state, i.owner };
                if (i.state == ItemState.InWorld)
                    for (int k = 0; k < 16; k++) e.Add((double)i.transform[k]);
                items.Add(e);
            }
            var storage = new Dictionary<string, object>();
            foreach (var kv in g.storage.contents) storage[kv.Key] = kv.Value.Select(x => (object)(double)x).ToList();
            var p = g.player.transform;
            return new Dictionary<string, object>
            {
                ["game"] = "Workshop Wreckage (Unity)", ["version"] = (double)Version, ["saved_at"] = DateTime.Now.ToString("s"),
                ["preset"] = g.presetId, ["catalog_version"] = (double)Json.Int(g.db.config, "version", 1),
                ["seed"] = (double)g.world.seed, ["item_count"] = (double)g.world.Count, ["catalog_hash"] = g.db.catalogHash.ToString(),
                ["play_time_s"] = g.playTime, ["progression"] = g.progression.ToDict(),
                ["robots"] = g.robots.ToDictionary(r => r.id, r => (object)r.ToDict()),
                ["objectives"] = g.objectives.ToList(),
                ["workbench"] = g.workbench.ToDict(),
                ["items"] = items, ["item_defs"] = itemDefs, ["storage"] = storage,
                ["inventory"] = new Dictionary<string, object> { ["items"] = g.inventory.items.Select(x => (object)(double)x).ToList(), ["held_def"] = (double)g.inventory.heldDef },
                ["progress"] = g.progress.ToDict(),
                ["game_stats"] = g.stats?.ToDict(),
                ["player"] = new Dictionary<string, object> { ["pos"] = new List<object> { (double)p.position.x, (double)p.position.y, (double)p.position.z }, ["yaw"] = (double)g.player.Yaw, ["pitch"] = (double)g.player.Pitch },
                ["stats"] = new Dictionary<string, object> { ["sorted"] = (double)g.progress.sortedTotal, ["percent"] = (double)g.progress.OverallPercent, ["level"] = (double)g.progression.level },
            };
        }

        /// <summary>True if the save describes the world currently being played
        /// (same preset, seed and catalog), i.e. saving over it loses nothing else.</summary>
        public static bool SameWorld(Dictionary<string, object> d, Game g)
            => Json.Str(d, "preset", "standard") == g.presetId && Json.Int(d, "seed") == g.world.seed && Json.Str(d, "catalog_hash") == g.db.catalogHash.ToString();

        public static string WorldLabel(Dictionary<string, object> d)
        {
            var p = Json.Str(d, "preset", "standard");
            return $"{char.ToUpper(p[0]) + p.Substring(1)}, seed {Json.Int(d, "seed")}";
        }

        public static bool Write(string slot, Dictionary<string, object> data)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Dir);
                var tmp = PathOf(slot) + ".tmp";
                System.IO.File.WriteAllText(tmp, Json.Write(data));
                if (System.IO.File.Exists(PathOf(slot))) System.IO.File.Delete(PathOf(slot));
                System.IO.File.Move(tmp, PathOf(slot));
                return true;
            }
            catch (Exception e) { Debug.LogError("Save failed: " + e.Message); return false; }
        }

        public static Dictionary<string, object> Read(string slot)
            => System.IO.File.Exists(PathOf(slot)) ? Json.Obj(Json.Parse(System.IO.File.ReadAllText(PathOf(slot)))) : null;

        public static string Check(Dictionary<string, object> d, ItemDatabase db)
        {
            if (d == null) return "No save in that slot.";
            if (Json.Int(d, "version") > Version) return "This save was made by a newer version of the game.";
            if (Json.Str(d, "preset", "standard") != db.presetId) return $"This save belongs to the {Json.Str(d, "preset")} world size.";
            bool sameCatalog = Json.Int(d, "catalog_version", 1) == Json.Int(db.config, "version", 1) && Json.Str(d, "catalog_hash") == db.catalogHash.ToString();
            if (!sameCatalog && !d.ContainsKey("item_defs")) return "This save was made with an older item catalog and can't be converted (saves from before 2026-10-22).";
            return "";
        }

        /// <summary>Does this save need SaveMigration for the current catalog?</summary>
        public static bool NeedsMigration(Dictionary<string, object> d, ItemDatabase db)
            => Json.Int(d, "catalog_version", 1) != Json.Int(db.config, "version", 1) || Json.Str(d, "catalog_hash") != db.catalogHash.ToString();

        public static void ApplyItems(Dictionary<string, object> d, ItemWorld w)
        {
            foreach (var row in Json.Arr(d["items"]))
            {
                var e = (List<object>)row;
                var inst = w.instances[(int)(double)e[0]];
                inst.state = (ItemState)(int)(double)e[1];
                inst.owner = e[2].ToString();
                if (e.Count >= 19)
                {
                    var m = new Matrix4x4();
                    for (int k = 0; k < 16; k++) m[k] = (float)(double)e[3 + k];
                    inst.transform = m;
                    inst.moved = true;
                }
            }
        }

        public static string Describe(string slot)
        {
            var d = Read(slot);
            if (d == null) return slot + " — empty";
            var st = Json.Obj(d["stats"]);
            return $"{slot} — {WorldLabel(d)}  ·  {Json.Str(d, "saved_at").Replace('T', ' ')}  ·  level {Json.Int(st, "level", 1)}  ·  {Json.Int(st, "sorted")} sorted ({Json.Num(st, "percent"):0.0}%)";
        }
    }

    /// <summary>Carries a save over to a changed item catalog (IMPLEMENTATION_AUDIT P1-4).
    /// The world is generated from the new catalog with the save's seed; every saved part is
    /// matched to an unused part of the same definition (definition ids are stable), keeping
    /// its state, owner, place and storage. A part stored in a destination that no longer
    /// holds its definition goes to the definition's current destination. Parts of removed
    /// definitions are dropped (XP already earned stays); extra copies stay unsorted in the
    /// world. Instance ids in the inventory and the per-part sort-XP record are remapped.</summary>
    public static class SaveMigration
    {
        public static Dictionary<string, object> Remap(Dictionary<string, object> d, ItemWorld w, ItemDatabase db, out string report)
        {
            var rows = Json.Arr(d["items"]);
            var defs = Json.Arr(d["item_defs"]);
            var free = new Dictionary<int, Queue<int>>();
            foreach (var inst in w.instances)
            {
                if (!free.TryGetValue(inst.defId, out var q)) free[inst.defId] = q = new Queue<int>();
                q.Enqueue(inst.id);
            }
            // sorted / installed / carried parts first: they carry progress
            var order = Enumerable.Range(0, rows.Count).OrderBy(k => (int)(double)((List<object>)rows[k])[1] == (int)ItemState.InWorld ? 1 : 0).ThenBy(k => k).ToList();
            var map = new Dictionary<int, int>();
            var newRows = new List<object>();
            var storage = new Dictionary<string, object>();
            int dropped = 0, rehomed = 0;
            foreach (int k in order)
            {
                var e = new List<object>((List<object>)rows[k]);
                int oldId = (int)(double)e[0], defId = (int)(double)defs[k];
                if (!free.TryGetValue(defId, out var q) || q.Count == 0) { dropped++; continue; }
                int nid = q.Dequeue();
                map[oldId] = nid;
                e[0] = (double)nid;
                var state = (ItemState)(int)(double)e[1];
                // unsorted parts go back to where the new world generated them: the layout may
                // have changed with the catalog, so an old position could be inside furniture now
                if (state == ItemState.InWorld) { e = new List<object> { e[0], e[1], "world" }; }
                if (state == ItemState.Stored && e[2] is string owner && owner.StartsWith("box:"))
                {
                    string box = owner.Substring(4);
                    var def = db.Get(defId);
                    bool station = db.stations.ContainsKey(box);
                    if (!station && (!db.boxes.ContainsKey(box) || def.storageBox != box)) { box = def.storageBox; e[2] = "box:" + box; rehomed++; }
                    if (!storage.TryGetValue(box, out var l)) storage[box] = l = new List<object>();
                    ((List<object>)l).Add((double)nid);
                }
                newRows.Add(e);
            }
            var o = new Dictionary<string, object>(d)
            {
                ["items"] = newRows,
                ["item_defs"] = newRows.Select(r => (object)(double)w.instances[(int)(double)((List<object>)r)[0]].defId).ToList(),
                ["storage"] = storage,
                ["catalog_hash"] = db.catalogHash.ToString(),
                ["catalog_version"] = (double)Json.Int(db.config, "version", 1),
                ["item_count"] = (double)w.Count,
            };
            var inv = new Dictionary<string, object>(Json.Obj(d["inventory"]));
            inv["items"] = Json.Arr(inv.GetValueOrDefault("items")).Select(x => (int)(double)x).Where(map.ContainsKey).Select(x => (object)(double)map[x]).ToList();
            if (db.Get(Json.Int(inv, "held_def", -1)) == null) inv["held_def"] = -1.0;
            o["inventory"] = inv;
            var prog = new Dictionary<string, object>(Json.Obj(d["progression"]));
            prog["sort_rewarded"] = Json.Arr(prog.GetValueOrDefault("sort_rewarded")).Select(x => (int)(double)x).Where(map.ContainsKey).Select(x => (object)(double)map[x]).ToList();
            o["progression"] = prog;
            var progress = new Dictionary<string, object>(Json.Obj(d["progress"]));
            progress["discovered"] = Json.Arr(progress.GetValueOrDefault("discovered")).Where(x => db.Get((int)(double)x) != null).ToList();
            progress["revealed"] = Json.Arr(progress.GetValueOrDefault("revealed")).Where(x => db.Get((int)(double)x) != null).ToList();
            o["progress"] = progress;
            report = $"{map.Count} parts carried over" + (rehomed > 0 ? $", {rehomed} moved to their new destinations" : "") + (dropped > 0 ? $", {dropped} of removed kinds dropped" : "");
            return o;
        }
    }
}
