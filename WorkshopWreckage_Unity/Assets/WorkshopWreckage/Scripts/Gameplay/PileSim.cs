using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WorkshopWreckage
{
    /// <summary>Parts tumble into real piles (playtest 2026-10-25: "they should pile up", "too
    /// much air"): a dropped part, and the parts around a removed one, become rigid bodies
    /// (convex hull of the part, mass from its weight) in a separate physics scene until
    /// everything there is at rest; then the final transforms go back to the item world.
    /// The scene only holds the room, decks, shelves and unit blockers as static boxes, so
    /// the player, robots and the rest of the game never touch it. Parts near a moving one
    /// join as static colliders (or sleeping bodies close by, which wake when they are hit).</summary>
    public class PileSim : MonoBehaviour
    {
        const float Step = 1 / 240f;
        const int MaxStepsPerFrame = 12;
        const float WakeRadius = 0.3f, StaticRadius = 0.55f;   // around a drop: bodies that may move / colliders only
        const int MaxBodies = 260;
        const float Timeout = 8f;

        class Body
        {
            public GameObject go;
            public Rigidbody rb;   // null: a static collider
            public Matrix4x4 start, last;   // where it lay; what the simulation last wrote
            public float scale;
            public bool moved;
        }

        Game g;
        Scene _scene;
        PhysicsScene _phys;
        WorkshopLayout _builtFor;
        readonly Dictionary<int, Body> _bodies = new();
        float _acc, _quiet, _since, _scanT;
        static PhysicsMaterial _mat;
        static readonly HashSet<EntityId> _baked = new();

        public int Count => _bodies.Count;
        public bool Settling => _bodies.Count > 0;
        public string lastInfo = "";

        public void Init(Game game) => g = game;

        /// <summary>New world or load: nothing is falling any more. The bodies go at once (DestroyImmediate
        /// here and below): a settle runs in the same frame, and a deferred Destroy left the last world's parts
        /// in the physics scene for it, so a loaded save's parts came to rest on them, in mid-air (playtest 2026-10-26).</summary>
        public void Clear()
        {
            foreach (var b in _bodies.Values) if (b.go != null) DestroyImmediate(b.go);
            _bodies.Clear();
        }

        void EnsureScene()
        {
            if (_scene.IsValid() && _builtFor == g.layout) return;
            if (_scene.IsValid()) { Clear(); SceneManager.UnloadSceneAsync(_scene); }
            _scene = SceneManager.CreateScene("PileSim" + Time.frameCount, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _phys = _scene.GetPhysicsScene();
            _builtFor = g.layout;
            _mat ??= new PhysicsMaterial("parts") { staticFriction = 0.85f, dynamicFriction = 0.7f, bounciness = 0.02f, frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum };
            BuildStatics();
        }

        // ---------------------------------------------------------------- the room as boxes

        void Box(Rect gr, float y0, float y1)   // a Godot-space rect (x, -z) from y0 to y1
        {
            if (y1 - y0 < 0.002f || gr.width < 0.002f || gr.height < 0.002f) return;
            var go = new GameObject("static");
            SceneManager.MoveGameObjectToScene(go, _scene);
            go.transform.position = new Vector3(gr.center.x, (y0 + y1) / 2, -gr.center.y);
            var c = go.AddComponent<BoxCollider>();
            c.size = new Vector3(gr.width, y1 - y0, gr.height);
            c.sharedMaterial = _mat;
            c.contactOffset = 0.002f;
        }

        void BuildStatics()
        {
            var l = g.layout;
            var room = new Rect(l.roomMin.x, l.roomMin.z, l.roomMax.x - l.roomMin.x, l.roomMax.z - l.roomMin.z);
            Box(new Rect(room.xMin - 1, room.yMin - 1, room.width + 2, room.height + 2), -0.5f, 0);   // floor
            Box(new Rect(room.xMin - 1, room.yMin - 1, 1, room.height + 2), 0, 3);                      // walls
            Box(new Rect(room.xMax, room.yMin - 1, 1, room.height + 2), 0, 3);
            Box(new Rect(room.xMin - 1, room.yMin - 1, room.width + 2, 1), 0, 3);
            Box(new Rect(room.xMin - 1, room.yMax, room.width + 2, 1), 0, 3);
            var decks = l.surfaces.Where(s => !s.floor).Select(s => (s.rect, s.y)).Concat(l.decks).ToList();
            foreach (var (r, y) in decks) Box(r, y - 0.03f, y);
            foreach (var s in l.surfaces) foreach (var (r, top) in s.obstacles) Box(r, s.y, top);
            // furniture and units: solid up to their lowest deck (nothing rolls under a bench or
            // into a cabinet), full height where there is no deck at all
            foreach (var b in l.floorBlockers)
            {
                float low = float.MaxValue;
                Rect under = b;
                foreach (var (r, y) in decks)
                    if (y > 0.01f && y < low && r.Overlaps(b) && r.Contains(b.center)) { low = y; under = r; }
                if (low == float.MaxValue) { Box(b, 0, 2.6f); continue; }
                // the unit's own footprint (the blocker less its 6 cm clearance margin): the margin
                // round a unit is open floor, no invisible ledge beside the bottom shelf, but nothing
                // slips in under its edge either (a pallet's top is inset 5 cm from the pallet's side)
                var foot = Rect.MinMaxRect(b.xMin + 0.06f, b.yMin + 0.06f, b.xMax - 0.06f, b.yMax - 0.06f);
                var c = Rect.MinMaxRect(Mathf.Max(b.xMin, Mathf.Min(under.xMin, foot.xMin)), Mathf.Max(b.yMin, Mathf.Min(under.yMin, foot.yMin)),
                                        Mathf.Min(b.xMax, Mathf.Max(under.xMax, foot.xMax)), Mathf.Min(b.yMax, Mathf.Max(under.yMax, foot.yMax)));
                Box(c, 0, low - 0.03f);
            }
        }

        // ---------------------------------------------------------------- bodies

        Body Make(int id, bool dynamic)
        {
            var inst = g.world.instances[id];
            var def = g.world.DefOf(id);
            var mesh = Pegboard.ToolFactory.Has(def.geometryKey) ? Pegboard.ToolFactory.CollisionMesh(def.geometryKey)   // board tools: unbevelled hull
                : MeshLibrary.Get(def.geometryKey, MeshLibrary.LodWorld).mesh;
            var go = new GameObject("part" + id);
            SceneManager.MoveGameObjectToScene(go, _scene);
            var m = inst.transform;
            go.transform.SetPositionAndRotation(m.GetColumn(3), m.rotation);
            go.transform.localScale = Vector3.one * def.worldScale;
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = true;
            if (_baked.Add(mesh.GetEntityId())) Physics.BakeMesh(mesh.GetEntityId(), true);
            mc.sharedMesh = mesh;
            mc.sharedMaterial = _mat;
            var e = ItemDatabase.Extents(def);
            mc.contactOffset = Mathf.Clamp(Mathf.Min(e[0], e[1], e[2]) * 0.08f, 0.0005f, 0.003f);
            var b = new Body { go = go, start = m, last = m, scale = def.worldScale };
            if (dynamic) MakeDynamic(b, def);
            _bodies[id] = b;
            return b;
        }

        void MakeDynamic(Body b, ItemDefinition def)
        {
            if (b.rb != null) return;
            var rb = b.go.AddComponent<Rigidbody>();
            // heavier parts weigh more, but compressed (square root): a stack whose masses differ
            // 100:1 is more than the solver can hold, and the heavy part sinks into the light ones
            rb.mass = Mathf.Sqrt(Mathf.Clamp(def.weightG, 1f, 2000f)) / 100f;
            rb.linearDamping = 0.25f;
            rb.angularDamping = 2.2f;   // rolling resistance: rods and nuts come to rest instead of rolling across the room
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;   // thin plates must not pass through small parts
            rb.solverIterations = 24;
            rb.solverVelocityIterations = 6;
            rb.maxDepenetrationVelocity = 0.15f;   // parts generated slightly inside each other ease apart, never pop
            rb.sleepThreshold = 0.002f;
            b.rb = rb;
        }

        bool CanMove(int id)
        {
            var i = g.world.instances[id];
            return !g.interaction.busy.Contains(id) && !g.robotReserved.Contains(id) && !g.InTray(i.Position) && i.Position.y < Game.MaxDropDeck + 0.4f;
        }

        /// <summary>Brings the parts around `c` into the simulation: the nearest ones (that may
        /// move) as sleeping bodies, the rest as colliders to land on.</summary>
        void AddAround(Vector3 c, float wake, float reach, int skip, int max = MaxBodies)
        {
            int n = _bodies.Count;
            foreach (var nid in g.index.QuerySphere(c, reach).OrderBy(i => (g.world.instances[i].Position - c).sqrMagnitude))
            {
                if (nid == skip || g.world.instances[nid].state != ItemState.InWorld) continue;
                var p = g.world.instances[nid].Position;
                bool near = new Vector2(p.x - c.x, p.z - c.z).magnitude < wake && CanMove(nid);
                if (_bodies.TryGetValue(nid, out var have))
                {
                    if (near && have.rb == null) { MakeDynamic(have, g.world.DefOf(nid)); have.rb.Sleep(); }
                    continue;
                }
                if (n++ >= max) break;
                var b = Make(nid, near);
                if (b.rb != null) b.rb.Sleep();
            }
        }

        /// <summary>Height of the pile (or floor, deck) top under a part of half-width `r` at `p`:
        /// where a part is let go just above, so it lands softly instead of dropping through.</summary>
        public float TopAt(Vector3 p, float r)
        {
            if (g == null || g.layout == null) return p.y;
            EnsureScene();
            AddAround(p, WakeRadius, StaticRadius, -1);
            Physics.SyncTransforms();
            r = Mathf.Clamp(r * 0.7f, 0.004f, 0.08f);
            // from below the next shelf or unit top over the spot, never from above the furniture
            var q = new Vector2(p.x, -p.z);
            float ceiling = p.y + 0.8f;
            foreach (var s in g.layout.surfaces) if (!s.floor && s.y > p.y + 0.03f && s.rect.Contains(q)) ceiling = Mathf.Min(ceiling, s.y - 0.035f);
            foreach (var (dr, y) in g.layout.decks) if (y > p.y + 0.03f && dr.Contains(q)) ceiling = Mathf.Min(ceiling, y - 0.035f);
            float from = Mathf.Max(p.y + r + 0.005f, ceiling - r);
            return _phys.SphereCast(new Vector3(p.x, from, p.z), r, Vector3.down, out var hit, from - p.y + r) ? Mathf.Clamp(hit.point.y, p.y, from) : p.y;
        }

        /// <summary>A part set down at its current transform (just above where it lands) falls
        /// and tumbles onto whatever is there. Call after it is InWorld and in the index.</summary>
        public void Drop(int id, Vector3 push = default, float lift = 0)
        {
            if (g == null || g.layout == null) return;
            EnsureScene();
            if (lift > 0) { var lm = g.world.instances[id].transform; lm.m13 += lift; g.world.SetTransform(id, lm); }
            // picked up and dropped again before the last pile came to rest: its old body (where it lay) goes
            if (_bodies.Remove(id, out var old)) DestroyImmediate(old.go);
            var p = g.world.instances[id].Position;
            AddAround(p, WakeRadius, StaticRadius, id);
            var b = Make(id, true);
            b.rb.maxDepenetrationVelocity = 0.6f;   // it is the one that has to make room
            b.rb.WakeUp();
            b.rb.linearVelocity = push;
            b.rb.angularVelocity = Random.insideUnitSphere * 0.4f;
            b.moved = true;
            _since = 0;
            _quiet = 0;
        }

        /// <summary>A part was taken away at `c` (radius r): what rested on or against it falls.</summary>
        public void Disturb(Vector3 c, float r)
        {
            if (g == null || g.layout == null) return;
            EnsureScene();
            AddAround(c, r + 0.12f, r + StaticRadius, -1);
            // only what touched it wakes (what lay beside it on the floor stays put); what they hit wakes in turn
            var gap = new Bounds(c, Vector3.one * (2 * r + 0.02f));
            foreach (var b in _bodies.Values)
                if (b.rb != null && b.go.GetComponent<Collider>().bounds.Intersects(gap)) b.rb.WakeUp();
            _since = 0;
            _quiet = 0;
        }

        // ---------------------------------------------------------------- per frame

        void Update()
        {
            if (_bodies.Count == 0) return;
            // taken away meanwhile (picked up, stored, swept): out of the simulation, and what lay on it wakes
            List<int> gone = null;
            // moved by the game itself (recovered into the tray, a test setup): no longer the simulation's to move
            foreach (var (id, b) in _bodies) if (g.world.instances[id].state != ItemState.InWorld || g.world.instances[id].transform != b.last) (gone ??= new()).Add(id);
            if (gone != null)
                foreach (var id in gone)
                {
                    var at = _bodies[id].go.transform.position;
                    DestroyImmediate(_bodies[id].go);
                    _bodies.Remove(id);
                    foreach (var o in _bodies.Values) if (o.rb != null && (o.go.transform.position - at).magnitude < 0.25f) o.rb.WakeUp();
                }

            _acc += Mathf.Min(Time.deltaTime, 0.05f);
            for (int k = 0; _acc >= Step && k < MaxStepsPerFrame; k++) { _phys.Simulate(Step); _acc -= Step; }
            _acc = Mathf.Min(_acc, Step);

            bool reindex = (_scanT -= Time.deltaTime) <= 0;
            bool awake = WriteMoved(reindex);
            // a part rolling away meets the parts lying there, not empty floor
            if (reindex)
            {
                _scanT = 0.1f;
                var rolling = _bodies.Where(kv => kv.Value.moved && kv.Value.rb != null && !kv.Value.rb.IsSleeping()).Select(kv => kv.Value.go.GetComponent<Collider>().bounds).ToList();
                // what it reaches can fall too (a part rolling out from under another lets it down)
                // (room for them however many are moving already: the start hands on hundreds, and a
                // fixed cap left the part lying on a rolling one in mid-air)
                foreach (var bb in rolling) { float e = Mathf.Max(bb.extents.x, bb.extents.z); AddAround(bb.center, e + 0.15f, e + 0.3f, -1, Mathf.Max(MaxBodies, _bodies.Count + 24)); }
            }
            _since += Time.deltaTime;
            _quiet = awake ? 0 : _quiet + Time.deltaTime;
            if (_quiet > 0.2f || _since > Timeout) Commit();
        }

        /// <summary>Moving parts' transforms into the world (and, every tenth of a second, the
        /// index, so a part that is still settling can be aimed at and picked up). True while
        /// anything is awake.</summary>
        bool WriteMoved(bool reindex)
        {
            bool awake = false;
            foreach (var (id, b) in _bodies)
            {
                if (b.rb == null || b.rb.IsSleeping()) continue;
                awake = true;
                var t = b.go.transform;
                if (!b.moved && ((Vector3)b.start.GetColumn(3) - t.position).sqrMagnitude > 1e-6f) b.moved = true;
                if (!b.moved) continue;
                b.last = Matrix4x4.TRS(t.position, t.rotation, Vector3.one * b.scale);
                g.world.SetTransform(id, b.last);
                if (reindex) g.index.Insert(id);
            }
            return awake;
        }

        /// <summary>A new world: the generator only estimates where parts rest, so every loose
        /// part (not the tray, not up on the storage units) falls once until the whole room is
        /// at rest (playtest 2026-10-25: parts floating in the starting heaps).</summary>
        public void SettleWorld()
        {
            if (g == null || g.layout == null) return;
            EnsureScene();
            Clear();
            var t0 = Time.realtimeSinceStartup;
            int n = 0, fixedParts = 0;
            foreach (var i in g.world.instances)
            {
                if (i.state != ItemState.InWorld) continue;
                bool loose = !g.InTray(i.Position) && i.Position.y < Game.MaxDropDeck + 0.4f;
                var b = Make(i.id, loose);
                if (loose)
                {
                    n++;
                    // the whole room at once: a coarser solver (a drop in play keeps the fine one)
                    b.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    b.rb.solverIterations = 10;
                    b.rb.solverVelocityIterations = 2;
                    b.rb.sleepThreshold = 0.006f;
                    b.rb.maxDepenetrationVelocity = 0.3f;
                }
                else fixedParts++;
            }
            const float Bulk = 1 / 120f;
            var rbs = _bodies.Values.Where(b => b.rb != null).Select(b => b.rb).ToList();
            // done when (nearly) nothing falls or slides any more: a big heap never quite sleeps,
            // its parts only creep by millimetres a second, and a bolt may spin on its shank in place
            bool Fast(Rigidbody r) => !r.IsSleeping() && r.linearVelocity.sqrMagnitude > 0.03f * 0.03f;
            int steps = 0, awake = rbs.Count;
            for (; steps < 480; steps++)   // up to 4 s simulated
            {
                _phys.Simulate(Bulk);
                if (steps % 12 == 11 && (awake = rbs.Count(Fast)) <= rbs.Count / 1000) break;
            }
            int moved = 0;
            foreach (var b in _bodies.Values)
                if (b.rb != null && ((Vector3)b.start.GetColumn(3) - b.go.transform.position).sqrMagnitude > 1e-6f) { b.moved = true; moved++; }
            var falling = _bodies.Where(kv => kv.Value.rb != null && Fast(kv.Value.rb)).Select(kv => (kv.Key, kv.Value.rb.linearVelocity)).ToList();
            _quietCommit = true;
            Commit();
            _quietCommit = false;
            // the few still under way finish falling in play, among the parts around them
            foreach (var (id, v) in falling)
            {
                var p = g.world.instances[id].Position;
                if (_bodies.Remove(id, out var old)) DestroyImmediate(old.go);
                AddAround(p, WakeRadius, StaticRadius, id, _bodies.Count + 40);
                var fb = Make(id, true);
                fb.rb.WakeUp();
                fb.rb.linearVelocity = v;
                fb.moved = true;
            }
            _since = _quiet = 0;
            Debug.Log($"[piles] world settled: {n} loose parts ({fixedParts} fixed), {moved} moved, {falling.Count} still falling (they finish in play), {steps * Bulk:0.0} s simulated in {(Time.realtimeSinceStartup - t0) * 1000:0} ms");
        }

        bool _quietCommit;

        /// <summary>Before a save: everything comes to rest now (up to 4 s simulated at once), so
        /// no part is saved in mid-air.</summary>
        public void Finish()
        {
            if (_bodies.Count == 0) return;
            for (int k = 0; k < 960; k++)
            {
                _phys.Simulate(Step);
                if (k % 24 == 23 && _bodies.Values.All(b => b.rb == null || b.rb.IsSleeping())) break;
            }
            foreach (var b in _bodies.Values)
                if (b.rb != null && (b.moved || ((Vector3)b.start.GetColumn(3) - b.go.transform.position).sqrMagnitude > 1e-6f)) b.moved = true;
            Commit();
        }

        /// <summary>Everything is at rest: final transforms into the world and the index.</summary>
        void Commit()
        {
            int moved = 0, reverted = 0;
            var l = g.layout;
            foreach (var (id, b) in _bodies)
            {
                if (b.moved)
                {
                    var t = b.go.transform;
                    var p = t.position;
                    bool lost = p.y < -0.05f || p.x < l.roomMin.x || p.x > l.roomMax.x || -p.z < l.roomMin.z || -p.z > l.roomMax.z || float.IsNaN(p.x);
                    var s0 = (Vector3)b.start.GetColumn(3);
                    if (!_quietCommit && new Vector2(p.x - s0.x, p.z - s0.z).magnitude > 0.25f) Debug.Log($"[piles] {g.world.DefOf(id).displayName} travelled {s0} -> {p}");
                    if (lost) { reverted++; g.world.SetTransform(id, b.start); }
                    else g.world.SetTransform(id, Matrix4x4.TRS(p, t.rotation, Vector3.one * b.scale));
                    g.index.Insert(id);
                    moved++;
                }
                DestroyImmediate(b.go);
            }
            lastInfo = $"{_bodies.Count} in the pile, {moved} moved, {reverted} put back, {_since:0.0} s";
            _bodies.Clear();
        }

        // ---------------------------------------------------------------- QA

        /// <summary>QA: does the part rest on something (floor, deck or another part) — its
        /// collider moved down 4 mm touches it — and how deep does it sit in another part?</summary>
        public string lastContact = "";

        public (bool supported, float deepest) Contact(int id)
        {
            EnsureScene();
            var near = g.index.QuerySphere(g.world.instances[id].Position, 0.5f).Where(o => o != id && g.world.instances[o].state == ItemState.InWorld).ToList();
            var probe = Make(id, false);
            _bodies.Remove(id);
            var others = new List<GameObject>();
            foreach (var o in near) { if (_bodies.ContainsKey(o)) continue; others.Add(Make(o, false).go); _bodies.Remove(o); }
            Physics.SyncTransforms();
            var pc = probe.go.GetComponent<MeshCollider>();
            float deepest = 0;
            foreach (var o in others)
            {
                var oc = o.GetComponent<MeshCollider>();
                if (Physics.ComputePenetration(pc, pc.transform.position, pc.transform.rotation, oc, oc.transform.position, oc.transform.rotation, out _, out float d) && d > deepest)
                {
                    deepest = d;
                    lastContact = $"{g.world.DefOf(id).displayName} in {o.name} {g.world.DefOf(int.Parse(o.name.Substring(4))).displayName} at {o.transform.position}; sizes {pc.bounds.size * 1000} / {oc.bounds.size * 1000} mm, mesh {pc.sharedMesh.vertexCount}v {pc.sharedMesh.bounds.size}";
                }
            }
            var hits = new Collider[256];   // a long part in a dense heap overlaps dozens of bounding boxes
            var start = probe.go.transform.position;
            probe.go.transform.position = start + Vector3.down * 0.004f;
            Physics.SyncTransforms();
            var bb = pc.bounds;
            int n = _phys.OverlapBox(bb.center, bb.extents, hits, Quaternion.identity);
            bool supported = false;
            for (int k = 0; k < n; k++)
            {
                if (hits[k] == pc) continue;
                if (Physics.ComputePenetration(pc, pc.transform.position, pc.transform.rotation, hits[k], hits[k].transform.position, hits[k].transform.rotation, out _, out _)) { supported = true; break; }
            }
            DestroyImmediate(probe.go);
            foreach (var o in others) DestroyImmediate(o);
            return (supported, deepest);
        }
    }
}
