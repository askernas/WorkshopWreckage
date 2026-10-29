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

        /// <summary>Battery pockets (playtest 2026-10-26): Part Radar batteries ride in their own pockets
        /// (3, the Tool Belt adds more), apart from the parts: no part slot, not on the hotbar.</summary>
        public int batteryPockets = 3;
        public readonly List<int> batteries = new();
        public Func<int, bool> isBattery = _ => false;

        public Inventory(ItemWorld w) { world = w; }
        public int Count => items.Count;
        public int Batteries => batteries.Count;
        /// <summary>What counts against the capacity: the parts (batteries are in their pockets).</summary>
        public int Parts => items.Count;
        public bool Has(int id) => items.Contains(id) || batteries.Contains(id);
        public bool IsFull => Parts >= capacity;
        public bool HasRoomFor(int id) => isBattery(id) ? Batteries < batteryPockets : !IsFull;

        public bool Add(int id)
        {
            if (!HasRoomFor(id) || Has(id)) return false;
            if (isBattery(id)) { batteries.Add(id); Changed?.Invoke(); return true; }
            items.Add(id);
            if (heldDef < 0) { heldDef = world.instances[id].defId; _heldSlot = id; }
            Changed?.Invoke();
            return true;
        }

        public void Remove(int id)
        {
            if (batteries.Remove(id)) { Changed?.Invoke(); return; }
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
        /// <summary>Completion ledger (design v1.0 §3): every part that has ever been accepted
        /// into its own destination. Permanent: taking a part out again (crafting) never undoes it.</summary>
        public readonly HashSet<int> accepted = new();
        readonly Dictionary<int, int> _acceptedOf = new();
        /// <summary>Destinations whose every assigned copy has been accepted: complete for good.</summary>
        public readonly HashSet<int> completedDefs = new();
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
                    if (world.DefOf(i.id).IsRobotComponent) robotPartsFound++;
                }
                if (AcceptedNow(i)) Accept(i, false);
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
                if (def.IsRobotComponent) { robotPartsFound++; Mile("robot_part_" + def.id, "Robot component found: " + def.displayName); }
                Discover(inst.defId);
                Mile("first_pickup", "First part collected — every piece counts!");
            }
            else if (old != ItemState.InWorld && inst.state == ItemState.InWorld)
            {
                _collected[inst.defId] = _collected.GetValueOrDefault(inst.defId) - 1;
                if (def.IsRobotComponent) robotPartsFound--;
            }
            if (AcceptedNow(inst)) Accept(inst, true);
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

        /// <summary>Stored in the destination its definition is assigned to (not a workbench tray).</summary>
        bool AcceptedNow(ItemInstance i) => i.state == ItemState.Stored && !world.DefOf(i.id).IsRobotPart && i.owner == "box:" + world.DefOf(i.id).storageBox;

        /// <summary>Ledger entry: once per part; a destination completes once, when its last copy is accepted.</summary>
        void Accept(ItemInstance i, bool live)
        {
            if (!accepted.Add(i.id)) return;
            int have = _acceptedOf[i.defId] = _acceptedOf.GetValueOrDefault(i.defId) + 1, total = world.TotalOf(i.defId);
            if (live && total >= 2 && have == (total + 1) / 2 && have < total) DefinitionHalf?.Invoke(i.defId);
            if (have >= total && completedDefs.Add(i.defId) && live) DefinitionCompleted?.Invoke(i.defId);
        }

        /// <summary>Copies of the definition accepted into its destination so far (the ledger).</summary>
        public int AcceptedOf(int d) => _acceptedOf.GetValueOrDefault(d);

        /// <summary>Accounted for in its collection: stored in its destination, or
        /// installed at the Robot Workbench (robot part, or consumed by a recipe).
        /// Parts in staging zones, in hands or carried by robots are not.</summary>
        public static bool Accounted(ItemState s) => s == ItemState.Stored || s == ItemState.Installed;

        /// <summary>The destination is complete: every copy has been accepted into it at least once.</summary>
        public bool IsComplete(int defId) => completedDefs.Contains(defId);

        void Mile(string key, string text)
        {
            if (milestones.Add(key)) Milestone?.Invoke(key, text);
        }

        /// <summary>A saved once-per-game flag without a toast (tutorials). True the first time.</summary>
        public bool MarkOnce(string key) => milestones.Add(key);

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
            ["accepted"] = accepted.Select(x => (object)(double)x).ToList(),
            ["completed_defs"] = completedDefs.Select(x => (object)(double)x).ToList(),
        };

        public void Load(Dictionary<string, object> d)
        {
            discovered.Clear(); revealed.Clear(); milestones.Clear();
            foreach (var x in Json.Arr(d.GetValueOrDefault("discovered"))) discovered.Add((int)(double)x);
            foreach (var x in Json.Arr(d.GetValueOrDefault("revealed"))) revealed.Add((int)(double)x);
            foreach (var x in Json.Arr(d.GetValueOrDefault("milestones"))) milestones.Add(x.ToString());
            accepted.Clear(); _acceptedOf.Clear(); completedDefs.Clear();
            foreach (var x in Json.Arr(d.GetValueOrDefault("completed_defs"))) completedDefs.Add((int)(double)x);
            foreach (var x in Json.Arr(d.GetValueOrDefault("accepted")))
            {
                int id = (int)(double)x;
                if (id < 0 || id >= world.Count || !accepted.Add(id)) continue;
                _acceptedOf[world.instances[id].defId] = _acceptedOf.GetValueOrDefault(world.instances[id].defId) + 1;
            }
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

        /// <summary>Pick forgiveness for the pegboard's small tools (sockets, hex keys...): their pick box is at
        /// least this thick in every direction (metres, world), so a 2 mm key is not a pixel hunt.</summary>
        public const float MinToolPickHalf = 0.012f;

        public float RayObb(int id, Vector3 origin, Vector3 dir)
        {
            var m = world.instances[id].transform;
            var inv = m.inverse;
            var o = inv.MultiplyPoint3x4(origin);
            var d = inv.MultiplyVector(dir);
            var b = UnitBounds(id);
            if (world.DefOf(id).IsPegboardTool)
            {
                var ext = b.extents;
                for (int a = 0; a < 3; a++) ext[a] = Mathf.Max(ext[a], MinToolPickHalf / Mathf.Max(1e-5f, ((Vector3)m.GetColumn(a)).magnitude));
                b.extents = ext;
            }
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
        /// survives catalog changes (SaveMigration). Version 1 saves still load unchanged.
        /// 3: component rescale (ComponentScale). Loose parts are saved with their size, and every
        /// version loads them at their definition's current size (ApplyItems), so older saves get the new
        /// sizes once and a save never scales twice. A robot carrying a part that is player-only now sets
        /// it down (Robot.AdoptCarried).
        /// 4: every loose part is saved with its place (not only the moved ones), so a load or a catalog
        /// conversion puts back exactly what the player left and never the generator's spots.
        /// Saves are files named by the player (playtest 2026-10-28: "the ability to name your saves, and also to
        /// delete old saves"): "name" holds the name, the file is a free id (save_&lt;time&gt;; quick, autosave and
        /// the old slot1-3 keep theirs). Writing is checked: the new file is written beside the old one, read back,
        /// and only then swapped in, the old one kept as .bak; a load whose file is damaged falls back to the .bak.
        /// A deleted save goes to saves/deleted, not away.</summary>
        public const int Version = 4;
        public const string Quick = "quick", Auto = "autosave";
        /// <summary>Saves the automated tests make (and the copies they test with): never in the player's list.</summary>
        static readonly string[] TestPrefixes = { "autotest", "premig_", "bench_" };
        public static bool IsTestSlot(string slot) => TestPrefixes.Any(slot.StartsWith);
        public static string Dir => System.IO.Path.Combine(Application.persistentDataPath, "saves");
        public static string PathOf(string slot) => System.IO.Path.Combine(Dir, slot + ".json");
        public static string TrashDir => System.IO.Path.Combine(Dir, "deleted");
        /// <summary>Why the last Write failed ("" if it did not).</summary>
        public static string LastError = "";

        /// <summary>One save as the menus list it.</summary>
        public class SaveInfo
        {
            public string slot, name, world, error = "";
            public DateTime savedAt;
            public int level, sorted, seed;
            public double percent, playTime;
            /// <summary>The file was damaged and its backup (.bak) is what loads.</summary>
            public bool fromBackup;
            public bool Ok => error == "";
        }

        public static string DefaultName(string slot)
            => slot == Quick ? "Quicksave" : slot == Auto ? "Autosave"
             : slot.StartsWith("slot") && int.TryParse(slot.Substring(4), out var n) ? $"Slot {n}" : slot;

        /// <summary>A fresh file id for a new named save.</summary>
        public static string NewSlotId(bool test)
        {
            var b = (test ? "autotest_save_" : "save_") + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var id = b;
            for (int k = 2; System.IO.File.Exists(PathOf(id)) || System.IO.File.Exists(PathOf(id) + ".bak"); k++) id = $"{b}_{k}";
            return id;
        }

        // the list reads every save; a file is parsed again only when it changed
        static readonly Dictionary<string, (DateTime t, long len, SaveInfo info)> _infoCache = new();

        /// <summary>The saves, newest first: the player's (tests: only the test saves). A save whose file is
        /// lost but whose backup is there is listed too (it loads from the backup).</summary>
        public static List<SaveInfo> List(bool tests = false)
        {
            var o = new List<SaveInfo>();
            if (!System.IO.Directory.Exists(Dir)) return o;
            var slots = System.IO.Directory.GetFiles(Dir, "*.json").Select(System.IO.Path.GetFileNameWithoutExtension)
                .Concat(System.IO.Directory.GetFiles(Dir, "*.json.bak").Select(f => System.IO.Path.GetFileNameWithoutExtension(System.IO.Path.GetFileNameWithoutExtension(f))))
                .Distinct();
            foreach (var slot in slots)
                if (IsTestSlot(slot) == tests) o.Add(Info(slot));
            return o.OrderByDescending(i => i.savedAt).ToList();
        }

        public static SaveInfo Info(string slot)
        {
            var p = PathOf(slot);
            var fi = new System.IO.FileInfo(System.IO.File.Exists(p) ? p : p + ".bak");
            if (!fi.Exists) return null;
            if (_infoCache.TryGetValue(slot, out var c) && c.t == fi.LastWriteTimeUtc && c.len == fi.Length) return c.info;
            var info = new SaveInfo { slot = slot, name = DefaultName(slot), savedAt = fi.LastWriteTime, world = "" };
            var d = Read(slot, out info.fromBackup, out var err);
            if (d == null) info.error = err;
            else
            {
                var st = Json.Obj(d.GetValueOrDefault("stats")) ?? new Dictionary<string, object>();
                info.name = Json.Str(d, "name", info.name);
                if (DateTime.TryParse(Json.Str(d, "saved_at"), out var t)) info.savedAt = t;
                info.level = Json.Int(st, "level", 1);
                info.sorted = Json.Int(st, "sorted");
                info.percent = Json.Num(st, "percent");
                info.playTime = Json.Num(d, "play_time_s");
                info.world = WorldLabel(d);
                info.seed = Json.Int(d, "seed");
            }
            _infoCache[slot] = (fi.LastWriteTimeUtc, fi.Length, info);
            return info;
        }

        /// <summary>The name a save goes by (its own, or its file's).</summary>
        public static string NameOf(string slot) => Info(slot)?.name ?? DefaultName(slot);

        public static Dictionary<string, object> Serialize(Game g)
        {
            var items = new List<object>();
            var itemDefs = new List<object>();
            foreach (var i in g.world.instances)
            {
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
                ["loose_settled"] = true,   // every loose part at rest (SaveGame finishes the piles): a load leaves them be
                ["seed"] = (double)g.world.seed, ["item_count"] = (double)g.world.Count, ["catalog_hash"] = g.db.catalogHash.ToString(),
                ["play_time_s"] = g.playTime, ["progression"] = g.progression.ToDict(),
                ["robots"] = g.robots.ToDictionary(r => r.id, r => (object)r.ToDict()),
                ["objectives"] = g.objectives.ToList(),
                ["workbench"] = g.workbench.ToDict(),
                ["modules"] = g.modules.ToDict(),
                ["radar"] = g.radar.ToDict(),
                ["items"] = items, ["item_defs"] = itemDefs, ["storage"] = storage,
                ["inventory"] = new Dictionary<string, object> { ["items"] = g.inventory.items.Select(x => (object)(double)x).ToList(), ["batteries"] = g.inventory.batteries.Select(x => (object)(double)x).ToList(), ["held_def"] = (double)g.inventory.heldDef },
                ["progress"] = g.progress.ToDict(),
                ["game_stats"] = g.stats?.ToDict(),
                ["player"] = new Dictionary<string, object> { ["pos"] = new List<object> { (double)p.position.x, (double)p.position.y, (double)p.position.z }, ["yaw"] = (double)g.player.Yaw, ["pitch"] = (double)g.player.Pitch },
                ["stats"] = new Dictionary<string, object> { ["sorted"] = (double)g.progress.sortedTotal, ["percent"] = (double)g.progress.OverallPercent, ["level"] = (double)g.progression.level },
            };
        }

        public static string WorldLabel(Dictionary<string, object> d)
        {
            var p = Json.Str(d, "preset", "standard");
            return $"{char.ToUpper(p[0]) + p.Substring(1)}, seed {Json.Int(d, "seed")}";
        }

        /// <summary>Writes a save so that a failure never costs the one before: the new file goes beside the old
        /// one (.tmp), is flushed to the disk and read back, and only a file that reads back exactly replaces
        /// the old one, which stays as .bak. False (and LastError) if anything failed: the old save is untouched.</summary>
        public static bool Write(string slot, Dictionary<string, object> data)
        {
            LastError = "";
            var path = PathOf(slot);
            var tmp = path + ".tmp";
            try
            {
                System.IO.Directory.CreateDirectory(Dir);
                var text = Json.Write(data);
                using (var fs = new System.IO.FileStream(tmp, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                using (var w = new System.IO.StreamWriter(fs, new System.Text.UTF8Encoding(false)))
                {
                    w.Write(text);
                    w.Flush();
                    fs.Flush(true);   // on the disk, not in a cache, before the old file goes
                }
                if (System.IO.File.ReadAllText(tmp) != text) throw new System.IO.IOException("the written file did not read back the same");
                if (System.IO.File.Exists(path))
                {
                    try { System.IO.File.Replace(tmp, path, path + ".bak", true); }
                    catch (Exception) when (System.IO.File.Exists(tmp) && System.IO.File.Exists(path))
                    {
                        // a file system without an atomic replace: the old file is copied aside first
                        System.IO.File.Copy(path, path + ".bak", true);
                        System.IO.File.Copy(tmp, path, true);
                        System.IO.File.Delete(tmp);
                    }
                }
                else System.IO.File.Move(tmp, path);
                _infoCache.Remove(slot);
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogError($"[save] writing '{slot}' failed, the previous save is untouched: {e.Message}");
                try { if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp); } catch (Exception) { }
                return false;
            }
        }

        public static Dictionary<string, object> Read(string slot) => Read(slot, out _, out _);

        /// <summary>A save's contents: its file, or its backup (.bak, `fromBackup`) if the file is lost or
        /// damaged. Null and the reason in `error` if neither reads.</summary>
        public static Dictionary<string, object> Read(string slot, out bool fromBackup, out string error)
        {
            fromBackup = false;
            var d = TryRead(PathOf(slot), out error);
            if (d != null) return d;
            var b = TryRead(PathOf(slot) + ".bak", out var bakError);
            if (b == null) { if (error == "missing") error = bakError == "missing" ? "No save in that slot." : bakError; return null; }
            Debug.LogWarning($"[save] '{slot}': the save file is {error}; its backup is loaded instead");
            fromBackup = true;
            return b;
        }

        static Dictionary<string, object> TryRead(string path, out string error)
        {
            error = "";
            if (!System.IO.File.Exists(path)) { error = "missing"; return null; }
            try
            {
                var d = Json.Obj(Json.Parse(System.IO.File.ReadAllText(path)));
                if (d != null && d.ContainsKey("items") && d.ContainsKey("stats") && d.ContainsKey("storage")) return d;
                error = "incomplete";
            }
            catch (Exception e) { error = "damaged (" + e.Message + ")"; }
            return null;
        }

        /// <summary>Moves a save (and its backup) to saves/deleted: gone from the menus, not from the disk.</summary>
        public static bool Delete(string slot)
        {
            try
            {
                System.IO.Directory.CreateDirectory(TrashDir);
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                foreach (var f in new[] { PathOf(slot), PathOf(slot) + ".bak", PathOf(slot) + ".tmp" })
                    if (System.IO.File.Exists(f)) System.IO.File.Move(f, System.IO.Path.Combine(TrashDir, $"{stamp}_{System.IO.Path.GetFileName(f)}"));
                _infoCache.Remove(slot);
                return true;
            }
            catch (Exception e) { LastError = e.Message; Debug.LogError($"[save] deleting '{slot}' failed: {e.Message}"); return false; }
        }

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
                    inst.transform = ComponentScale.Resize(m, w.DefOf(inst.id));
                    inst.moved = true;
                }
            }
        }

        /// <summary>A save's one-line details for the menus.</summary>
        public static string Describe(SaveInfo s)
        {
            var en = System.Globalization.CultureInfo.InvariantCulture;   // the game is in English whatever Windows is set to
            if (!s.Ok) return $"can't be read: {s.error}  ·  {s.savedAt.ToString("d MMM yyyy HH:mm", en)}";
            var played = TimeSpan.FromSeconds(s.playTime);
            return FormattableString.Invariant($"{s.savedAt:d MMM yyyy  HH:mm}  ·  level {s.level}  ·  {s.sorted} sorted ({s.percent:0.0}%)  ·  played {(int)played.TotalHours} h {played.Minutes:00} min  ·  seed {s.seed}")
                 + (s.fromBackup ? "  ·  file damaged, its backup loads" : "");
        }
    }

    /// <summary>Carries a save over to a changed item catalog (IMPLEMENTATION_AUDIT P1-4).
    /// The world is generated from the new catalog with the save's seed; every saved part keeps
    /// its own instance where the new world has the same definition there (else an unused part of
    /// the same definition: definition ids are stable), with its state, owner, place and storage.
    /// A loose part keeps its saved place: a conversion never moves anything, and a copy the player
    /// took off a shelf can't come back there in another copy's name (playtest 2026-10-26). A part stored in a destination that no longer
    /// holds its definition goes to the definition's current destination. Parts of removed
    /// definitions are dropped (XP already earned stays); extra copies stay unsorted in the
    /// world. Instance ids in the inventory and the per-part sort-XP record are remapped.
    /// `added`: the parts the save did not have (new definitions, e.g. the pieces of a split tool set). They lie
    /// where the generator estimated, which was never settled: the caller lets them fall (PileSim.DropLoose).</summary>
    public static class SaveMigration
    {
        public static Dictionary<string, object> Remap(Dictionary<string, object> d, ItemWorld w, ItemDatabase db, out string report, out List<int> added)
        {
            var rows = Json.Arr(d["items"]);
            var defs = Json.Arr(d["item_defs"]);
            var free = new Dictionary<int, Queue<int>>();
            foreach (var inst in w.instances)
            {
                if (!free.TryGetValue(inst.defId, out var q)) free[inst.defId] = q = new Queue<int>();
                q.Enqueue(inst.id);
            }
            // each part keeps its own instance where the definition still matches; the rest take an
            // unused one, sorted / installed / carried parts first: they carry progress
            var same = new HashSet<int>();
            for (int k = 0; k < rows.Count; k++)
            {
                int id = (int)(double)((List<object>)rows[k])[0];
                if (id < w.Count && w.instances[id].defId == (int)(double)defs[k]) same.Add(k);
            }
            var taken = same.Select(k => (int)(double)((List<object>)rows[k])[0]).ToHashSet();
            foreach (var q0 in free.Values) { var keep = q0.Where(i => !taken.Contains(i)).ToList(); q0.Clear(); foreach (var i in keep) q0.Enqueue(i); }
            var order = same.OrderBy(k => k).Concat(Enumerable.Range(0, rows.Count).Where(k => !same.Contains(k))
                .OrderBy(k => (int)(double)((List<object>)rows[k])[1] == (int)ItemState.InWorld ? 1 : 0).ThenBy(k => k)).ToList();
            var map = new Dictionary<int, int>();
            var newRows = new List<object>();
            var storage = new Dictionary<string, object>();
            int dropped = 0, rehomed = 0;
            foreach (int k in order)
            {
                var e = new List<object>((List<object>)rows[k]);
                int oldId = (int)(double)e[0], defId = (int)(double)defs[k];
                int nid;
                if (same.Contains(k)) nid = oldId;
                else if (!free.TryGetValue(defId, out var q) || q.Count == 0) { dropped++; continue; }
                else nid = q.Dequeue();
                map[oldId] = nid;
                e[0] = (double)nid;
                var state = (ItemState)(int)(double)e[1];
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
            var kept = map.Values.ToHashSet();
            added = w.instances.Where(i => !kept.Contains(i.id)).Select(i => i.id).ToList();
            report = $"{map.Count} parts carried over" + (rehomed > 0 ? $", {rehomed} moved to their new destinations" : "") + (dropped > 0 ? $", {dropped} of removed kinds dropped" : "");
            return o;
        }
    }
}
