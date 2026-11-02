using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WorkshopWreckage.Sandbox
{
    /// <summary>A pile built the way the workshop builds its heaps: parts drawn from the database (the game's mix,
    /// spawn counts as weights, or every type alike), packed big-first on the game's heightfield (PileSurface) with
    /// random yaw and roll, then settled with the game's part physics (PileBodies, PileSim's bulk solver) in a
    /// physics scene of the sandbox's own. Regenerate draws a new seed unless the seed is kept; Reset puts the
    /// first pile back exactly (its settled transforms are kept, so it never depends on the physics repeating).</summary>
    public class PileGenerator
    {
        public class Config
        {
            public int count = 40, seed = 1;
            public float radius = 0.45f, sizeVariation;
            public bool reuseSeed, evenMix;
            public HashSet<int> categories = new();   // empty: every category

            public Config Copy() => new() { count = count, seed = seed, radius = radius, sizeVariation = sizeVariation, reuseSeed = reuseSeed, evenMix = evenMix, categories = new HashSet<int>(categories) };

            public Dictionary<string, object> ToDict() => new()
            {
                ["count"] = count, ["seed"] = seed, ["radius"] = (double)radius, ["size_variation"] = (double)sizeVariation,
                ["mix"] = evenMix ? "even" : "game", ["categories"] = categories.OrderBy(c => c).Select(c => (object)ItemDefinition.CategoryNames[c]).ToList(),
            };
        }

        public Config cfg = new();
        public readonly SandboxItems items;
        public readonly ItemDatabase db;
        public readonly Vector3 centre;
        public string lastInfo = "";
        public List<(int defId, Matrix4x4 m)> settled = new();
        List<(int defId, Matrix4x4 m)> _original;
        Config _originalCfg;
        Scene _scene;
        PhysicsScene _phys;
        float _builtRadius = -1;
        readonly List<GameObject> _bodies = new();

        public PileGenerator(ItemDatabase db, SandboxItems items, Vector3 centre)
        {
            this.db = db;
            this.items = items;
            this.centre = centre;
        }

        public Config OriginalConfig => _originalCfg;

        /// <summary>REGENERATE: a new selection (a new seed unless reuseSeed), placed, settled and shown.</summary>
        public void Regenerate()
        {
            if (!cfg.reuseSeed && _original != null) cfg.seed = new System.Random(cfg.seed * 7919 + 17).Next(1, 1_000_000);
            Generate();
        }

        /// <summary>RESET: the first pile of the session (or of the loaded settings), exactly as it settled.</summary>
        public void Reset()
        {
            if (_original == null) { Generate(); return; }
            cfg = _originalCfg.Copy();
            settled = new List<(int, Matrix4x4)>(_original);
            Show();
            lastInfo = $"reset to seed {cfg.seed}: {settled.Count} parts (stored transforms)";
        }

        public void Generate()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var rng = new System.Random(cfg.seed);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float Gauss(float sigma) => Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(1e-7f, (float)rng.NextDouble()))) * Mathf.Cos(2f * Mathf.PI * (float)rng.NextDouble()) * sigma;

            // the selection
            var pool = db.order.Select(db.Get).Where(d => d != null && d.spawnCount > 0 && (cfg.categories.Count == 0 || cfg.categories.Contains(d.category))).ToList();
            if (pool.Count == 0) { lastInfo = "no parts in the chosen categories"; return; }
            var weights = pool.Select(d => cfg.evenMix ? 1.0 : d.spawnCount).ToArray();
            double total = weights.Sum();
            var picks = new List<(ItemDefinition def, float scale, float key)>();
            for (int k = 0; k < cfg.count; k++)
            {
                double r = rng.NextDouble() * total;
                int i = 0;
                while (i < pool.Count - 1 && (r -= weights[i]) > 0) i++;
                var def = pool[i];
                float mul = 1 + cfg.sizeVariation * Range(-1, 1);
                float rad = MeshLibrary.UnitBounds(def.geometryKey).extents.magnitude * def.worldScale * mul;
                picks.Add((def, def.worldScale * mul, rad * Range(0.55f, 1.45f)));   // heaps go down foundation-first (WorldGenerator)
            }
            picks = picks.OrderByDescending(p => p.key).ToList();

            // packed on the game's heightfield (layout space: x, -z), as a heap
            var gc = new Vector2(centre.x, -centre.z);
            float span = cfg.radius * 1.6f;
            var surf = new PileSurface(new Rect(gc - Vector2.one * span, Vector2.one * span * 2), centre.y);
            var placed = new List<(int defId, Matrix4x4 m)>();
            int failed = 0;
            foreach (var (def, scale, _) in picks)
            {
                var b = MeshLibrary.UnitBounds(def.geometryKey).size;
                var half = b * 0.5f * scale;
                Matrix4x4? m = null;
                for (int attempt = 0; attempt < 14 && m == null; attempt++)
                {
                    float spread = 1 + attempt * 0.18f;
                    var at = gc + new Vector2(Gauss(cfg.radius * 0.45f * spread), Gauss(cfg.radius * 0.45f * spread));
                    if ((at - gc).magnitude > cfg.radius * 1.15f) continue;
                    m = surf.Place(def, half, at, Range(0, Mathf.PI * 2), Range(0, Mathf.PI * 2), scale, centre.y + 1.25f);
                }
                if (m == null) { failed++; continue; }
                placed.Add((def.id, m.Value));
            }
            float packMs = (float)sw.Elapsed.TotalMilliseconds;
            settled = Settle(placed, out int steps, out int awake);
            if (_original == null) { _original = new List<(int, Matrix4x4)>(settled); _originalCfg = cfg.Copy(); }
            Show();
            lastInfo = $"seed {cfg.seed}: {settled.Count} parts{(failed > 0 ? $" ({failed} found no room)" : "")}, packed {packMs:0} ms, settled {steps / 120f:0.0} s simulated"
                + $"{(awake > 0 ? $" ({awake} still moving)" : "")} in {sw.ElapsedMilliseconds} ms";
            Debug.Log("[sandbox] pile " + lastInfo);
        }

        void Show()
        {
            var w = new ItemWorld(db);
            foreach (var (defId, m) in settled) w.Add(defId, m).state = ItemState.InWorld;
            items.Show(w);
            foreach (var e in items.entries) e.group = "Pile";
        }

        // ---------------------------------------------------------------- physics

        void EnsureScene()
        {
            if (_scene.IsValid() && Mathf.Approximately(_builtRadius, cfg.radius)) return;
            if (_scene.IsValid()) { ClearBodies(); SceneManager.UnloadSceneAsync(_scene); }
            _scene = SceneManager.CreateScene("SandboxPile" + Time.frameCount, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _phys = _scene.GetPhysicsScene();
            _builtRadius = cfg.radius;
            Box(new Vector3(centre.x, centre.y - 0.25f, centre.z), new Vector3(20, 0.5f, 20), Quaternion.identity);   // floor
            // a low ring keeps a rolling part in the pile area (well outside the heap: it seldom touches anything)
            float ring = cfg.radius * 1.8f + 0.1f;
            for (int k = 0; k < 24; k++)
            {
                float a = k * Mathf.PI * 2 / 24;
                var rot = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0);
                Box(centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (ring + 0.1f) + Vector3.up * 0.3f, new Vector3(0.2f, 0.6f, ring * Mathf.PI * 2 / 24 * 1.1f), rot);
            }
        }

        void Box(Vector3 at, Vector3 size, Quaternion rot)
        {
            var go = new GameObject("static");
            SceneManager.MoveGameObjectToScene(go, _scene);
            go.transform.SetPositionAndRotation(at, rot);
            var c = go.AddComponent<BoxCollider>();
            c.size = size;
            c.sharedMaterial = PileBodies.Material;
            c.contactOffset = 0.002f;
        }

        void ClearBodies()
        {
            foreach (var go in _bodies) if (go != null) Object.DestroyImmediate(go);
            _bodies.Clear();
        }

        /// <summary>PileSim.SettleWorld's settle: the bulk solver at 1/120 s, up to 4 s, done when (nearly) nothing
        /// falls or slides any more. Bodies are destroyed straight after.</summary>
        List<(int, Matrix4x4)> Settle(List<(int defId, Matrix4x4 m)> placed, out int steps, out int awake)
        {
            EnsureScene();
            ClearBodies();
            var rbs = new List<Rigidbody>();
            foreach (var (defId, m) in placed)
            {
                var def = db.Get(defId);
                var go = new GameObject("part");
                SceneManager.MoveGameObjectToScene(go, _scene);
                go.transform.SetPositionAndRotation(m.GetColumn(3), m.rotation);
                go.transform.localScale = Vector3.one * m.GetColumn(0).magnitude;
                PileBodies.AddCollider(go, def);
                var rb = PileBodies.AddRigidbody(go, def);
                PileBodies.Bulk(rb);
                rbs.Add(rb);
                _bodies.Add(go);
            }
            bool Fast(Rigidbody r) => !r.IsSleeping() && r.linearVelocity.sqrMagnitude > 0.03f * 0.03f;
            const float Bulk = 1 / 120f;
            awake = rbs.Count;
            for (steps = 0; steps < 480; steps++)
            {
                _phys.Simulate(Bulk);
                if (steps % 12 == 11 && (awake = rbs.Count(Fast)) <= rbs.Count / 1000) break;
            }
            var result = new List<(int, Matrix4x4)>(placed.Count);
            for (int k = 0; k < placed.Count; k++)
            {
                var t = _bodies[k].transform;
                var p = t.position;
                // a part that left the area (or the world) keeps its packed spot
                result.Add((placed[k].defId, p.y < centre.y - 0.05f || float.IsNaN(p.x) ? placed[k].m : Matrix4x4.TRS(p, t.rotation, t.localScale)));
            }
            ClearBodies();
            return result;
        }

        public Dictionary<string, object> ToDict(bool withTransforms)
        {
            var d = cfg.ToDict();
            if (withTransforms)
                d["parts"] = settled.Select(s =>
                {
                    var p = (Vector3)s.m.GetColumn(3); var q = s.m.rotation; float sc = s.m.GetColumn(0).magnitude;
                    return (object)new List<object> { db.Get(s.defId).displayName, s.defId, (double)p.x, (double)p.y, (double)p.z, (double)q.x, (double)q.y, (double)q.z, (double)q.w, (double)sc };
                }).ToList();
            return d;
        }
    }
}
