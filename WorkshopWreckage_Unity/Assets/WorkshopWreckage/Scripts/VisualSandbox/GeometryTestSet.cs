using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WorkshopWreckage.Sandbox
{
    /// <summary>Variant B's representative set: one real catalogue part per role, at its real size, in a row on the
    /// floor (camera presets 6 and 7 frame it), and to its right a grid of identical parts at wear levels 0-3 (camera
    /// preset 8), so BASELINE, BP1 and iteration 3 can be compared on exactly these parts. Roles whose part the
    /// loaded preset lacks are skipped (and logged). The wear grid's conditions are set per instance through
    /// ItemRenderer.SkinCondition (<see cref="Wear"/>), the same in every style.</summary>
    public class GeometryTestSet
    {
        public const float Gap = 0.08f;

        /// <summary>Wear levels 0 new, 1 light, 2 well used, 3 heavy, as skin conditions.</summary>
        public static readonly float[] LevelCondition = { 0.03f, 0.18f, 0.4f, 0.68f };

        static bool Key(ItemDefinition d, string prefix) => d.geometryKey.StartsWith(prefix);
        static bool Mat(ItemDefinition d, params string[] m) => Array.IndexOf(m, d.material) >= 0;

        public static readonly (string role, Func<ItemDefinition, bool> match, bool largest)[] Roles =
        {
            ("solid gear", d => Key(d, "gear:spur:") && !ShapeMeshes.SpokedV2.Contains(d.geometryKey) && !Mat(d, "oxidized"), true),
            ("spoked gear", d => ShapeMeshes.SpokedV2.Contains(d.geometryKey), true),
            ("large rusty gear", d => Key(d, "gear:") && Mat(d, "oxidized"), true),
            ("small precision gear", d => Key(d, "gear:pinion:") && (d.finish == "polished" || Mat(d, "stainless", "brass", "steel")), false),
            ("painted housing", d => d.geometryKey == "mach:limb_housing" && Mat(d, "painted"), true),
            ("bearing", d => Key(d, "bearing:ball:"), true),
            ("cylinder", d => d.geometryKey == "mach:hydraulic_actuator" && Mat(d, "steel"), true),
            ("bolt", d => Key(d, "screw:hex_bolt:"), true),
            ("hex nut", d => d.geometryKey == "nut:hex", true),
            ("washer", d => d.geometryKey == "washer:flat" || d.geometryKey == "washer:fender", true),
            ("metal storage component", d => d.geometryKey == "spec:flywheel" && Mat(d, "steel", "stainless"), true),
            ("brass / copper component", d => (Key(d, "spec:") || Key(d, "mach:")) && Mat(d, "brass", "copper"), true),
            ("rubber component", d => d.geometryKey == "mach:claw_jaw", false),
        };

        /// <summary>The wear grid's rows, front to back (small parts in front, so the tall housings never hide them):
        /// the same definition four times, at levels 0-3.</summary>
        public static readonly (string row, Func<ItemDefinition, bool> match)[] WearRows =
        {
            ("brass", d => Key(d, "gear:") && Mat(d, "brass") && d.finish == "plain"),
            ("bare steel", d => Key(d, "gear:") && Mat(d, "steel") && d.finish == "plain"),
            ("painted", d => d.geometryKey == "mach:limb_housing" && Mat(d, "painted")),
        };

        public readonly SandboxItems items;
        public readonly ItemDatabase db;
        public readonly Vector3 origin;
        public Bounds bounds, first, wearBounds;
        public readonly List<string> log = new();
        /// <summary>Instance id -> condition, for the wear grid (ItemRenderer.SkinCondition).</summary>
        public readonly Dictionary<int, float> wear = new();
        public ItemWorld World { get; private set; }

        public GeometryTestSet(ItemDatabase db, SandboxItems items, Vector3 origin) { this.db = db; this.items = items; this.origin = origin; }

        public void Build()
        {
            var defs = db.order.Select(db.Get).Where(d => d != null && !string.IsNullOrEmpty(d.geometryKey) && ItemSkins.Applies(d)).ToList();
            var w = World = new ItemWorld(db);
            var used = new HashSet<string>();
            float x = 0;
            bounds = new Bounds(origin, Vector3.zero);
            log.Clear();
            wear.Clear();
            int n = 0;
            Bounds Place(ItemDefinition def, float px, float pz, out int id)
            {
                var e = MeshLibrary.Get(def.geometryKey, MeshLibrary.LodWorld);
                var size = e.bounds.size * def.worldScale;
                var centre = new Vector3(px + size.x / 2, size.y / 2, pz);
                var inst = w.Add(def.id, Matrix4x4.TRS(centre - e.bounds.center * def.worldScale, Quaternion.identity, Vector3.one * def.worldScale));
                inst.state = ItemState.InWorld;
                id = inst.id;
                return new Bounds(centre, size);
            }
            foreach (var (role, match, largest) in Roles)
            {
                var c = defs.Where(d => match(d) && !used.Contains(d.geometryKey)).OrderBy(d => d.worldScale).ToList();
                if (c.Count == 0) { log.Add($"{role}: none in this preset"); continue; }
                var def = largest ? c[^1] : c[0];
                used.Add(def.geometryKey);
                var b = Place(def, origin.x + x, origin.z, out _);
                if (n++ == 0) first = b;
                bounds.Encapsulate(b);
                x += b.size.x + Gap;
                log.Add($"{role}: {def.geometryKey} {def.material}/{def.finish} ({def.id}), {Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) * 100:0.0} cm");
            }
            // the wear grid: a column per level, a row per material, rows going back from the main row
            float gx = origin.x + x + 0.25f, gz = origin.z;
            bool firstRow = true;
            foreach (var (row, match) in WearRows)
            {
                var c = defs.Where(match).OrderBy(d => d.worldScale).ToList();
                if (c.Count == 0) { log.Add($"wear row {row}: none in this preset"); continue; }
                var def = c[c.Count / 2];
                var e = MeshLibrary.Get(def.geometryKey, MeshLibrary.LodWorld);
                var size = e.bounds.size * def.worldScale;
                float px = gx, depth = size.z;
                for (int j = 0; j < 4; j++)
                {
                    int lv = 3 - j;   // camera 8 looks along -z, so world +x is screen left: placed 3..0, read 0..3
                    var b = Place(def, px, gz, out int id);
                    wear[id] = LevelCondition[lv];
                    if (firstRow && j == 0) wearBounds = b; else wearBounds.Encapsulate(b);
                    firstRow = false;
                    px += size.x + Gap * 1.5f;
                }
                log.Add($"wear row {row}: {def.geometryKey} {def.material}/{def.finish} ({def.id}) at conditions {string.Join(", ", LevelCondition)}");
                gz -= depth + 0.2f;
            }
            items.Show(w);
            Debug.Log("[sandbox] geometry test set: " + string.Join("; ", log));
        }

        /// <summary>For every procedural part key in the database: the shape mesh's bounds against the world mesh's
        /// (the pile and gallery place parts by those bounds, so they must agree), and the triangle counts per tier of
        /// BP1 (version 1) and iteration 3 (version 2).</summary>
        public static List<string> Audit(ItemDatabase db)
        {
            var o = new List<string>();
            var keys = db.order.Select(db.Get).Where(d => d != null && !string.IsNullOrEmpty(d.geometryKey)).Select(d => d.geometryKey)
                .Distinct().Where(k => ShapeMeshes.Has(k) && !SalvageMeshes.Has(k)).OrderBy(k => k).ToList();
            float worst = 0; string worstKey = "";
            long t1 = 0, t2 = 0, t3 = 0, c2 = 0, s2 = 0, h2 = 0;
            var off = new List<string>();
            foreach (var k in keys)
            {
                var a = MeshLibrary.Get(k, MeshLibrary.LodWorld);
                t1 += Tris(a.mesh);
                t2 += Tris(MeshLibrary.Get(k, ShapeMeshes.Lod).mesh);
                t3 += Tris(MeshLibrary.Get(k, ShapeMeshes.LodHero).mesh);
                for (int tier = 0; tier < 3; tier++)
                {
                    var b = MeshLibrary.Get(k, ShapeMeshes.LodOf(2, tier));
                    if (tier == 0) c2 += Tris(b.mesh); else if (tier == 1) s2 += Tris(b.mesh); else h2 += Tris(b.mesh);
                    float m = Mathf.Max(a.bounds.size.x, Mathf.Max(a.bounds.size.y, a.bounds.size.z));
                    float d = Mathf.Max((a.bounds.size - b.bounds.size).magnitude, (a.bounds.center - b.bounds.center).magnitude) / m;
                    if (d > worst) { worst = d; worstKey = $"{k} tier {tier}"; }
                    if (d > 0.08f) off.Add($"{k} tier {tier} {d * 100:0}%");
                }
            }
            float R(long t) => (float)t / Mathf.Max(1, t1);
            o.Add($"shape audit: {keys.Count} procedural keys; triangles summed: world {t1:N0}; BP1 standard {t2:N0} (x{R(t2):0.0}), hero {t3:N0} (x{R(t3):0.0}); "
                + $"iteration 3 compact {c2:N0} (x{R(c2):0.0}), standard {s2:N0} (x{R(s2):0.0}), hero {h2:N0} (x{R(h2):0.0}); largest bounds deviation (iteration 3) {worst * 100:0.0}% ({worstKey})");
            if (off.Count > 0) o.Add($"bounds deviating > 8%: {off.Count}: " + string.Join("; ", off));
            o.AddRange(GearCost(keys));
            return o;
        }

        /// <summary>What spokes cost: every gear that iteration 3 keeps solid but could be cast with spokes (24+ teeth),
        /// built both ways at the standard and hero tiers: triangles, vertices, build time (best of 5, uncached).</summary>
        static List<string> GearCost(List<string> keys)
        {
            var o = new List<string>();
            long ts = 0, tk = 0;
            foreach (var k in keys.Where(k => k.StartsWith("gear:")))
            {
                var parts = k.Split(':');
                int teeth = parts.Length > 2 && parts[2].StartsWith("t") && int.TryParse(parts[2].Substring(1), out var t) ? t : 0;
                if (ShapeMeshes.Spoked(k, parts[1], teeth, 2) || !ShapeMeshes.Spoked(k, parts[1], teeth, 2, true)) continue;
                var row = new List<string>();
                foreach (int tier in new[] { ShapeMeshes.TierStandard, ShapeMeshes.TierHero })
                {
                    var (sm, sms) = Timed(k, ShapeMeshes.LodOf(2, tier, false));
                    var (km, kms) = Timed(k, ShapeMeshes.LodOf(2, tier, true));
                    row.Add($"{(tier == ShapeMeshes.TierHero ? "hero" : "standard")} solid {Tris(sm):N0} tris / {sm.vertexCount:N0} v / {sms:0.00} ms, spoked {Tris(km):N0} / {km.vertexCount:N0} / {kms:0.00} ms");
                    if (tier == ShapeMeshes.TierStandard) { ts += Tris(sm); tk += Tris(km); }
                    UnityEngine.Object.Destroy(sm); UnityEngine.Object.Destroy(km);
                }
                o.Add($"gear cost {k}: " + string.Join("; ", row));
            }
            if (ts > 0) o.Add($"gear cost summary (standard tier): solid {ts:N0} tris, the same gears spoked {tk:N0} tris (x{(float)tk / ts:0.00})");
            return o;
        }

        static (Mesh, double) Timed(string key, int lod)
        {
            double best = double.MaxValue;
            Mesh m = null;
            for (int i = 0; i < 5; i++)
            {
                var sw = Stopwatch.StartNew();
                var e = ShapeMeshes.Build(key, lod);
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
                if (m != null) UnityEngine.Object.Destroy(m);
                m = e.mesh;
            }
            return (m, best);
        }

        static long Tris(Mesh m) { long n = 0; for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3; return n; }
    }
}
