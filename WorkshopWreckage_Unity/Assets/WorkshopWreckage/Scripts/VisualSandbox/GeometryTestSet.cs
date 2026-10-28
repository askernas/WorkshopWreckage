using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage.Sandbox
{
    /// <summary>The Variant B geometry pass's representative set: one real catalogue part per role, at its real
    /// size, in a row on the floor (the camera presets 6 and 7 frame it), so BASELINE and VARIANT B can be compared
    /// on exactly these shapes. Roles whose part the loaded preset lacks are skipped (and logged).</summary>
    public class GeometryTestSet
    {
        public const float Gap = 0.08f;
        public static readonly (string role, Func<string, bool> match, bool largest)[] Roles =
        {
            ("large rusty gear", k => k.StartsWith("gear:spur:"), true),
            ("smaller gear", k => k.StartsWith("gear:spur:") || k.StartsWith("gear:pinion:"), false),
            ("cylindrical housing", k => k == "mach:motor_housing", true),
            ("washer / ring", k => k == "washer:flat" || k == "washer:fender", true),
            ("bolt / fastener", k => k.StartsWith("screw:hex_bolt:"), true),
            ("hex nut", k => k == "nut:hex", true),
            ("painted metal part", k => k == "mach:bracket", true),
            ("industrial box", k => k == "mach:gearbox", true),
            ("large round component", k => k == "spec:flywheel" || k == "mach:pump_housing", true),
            ("bearing", k => k.StartsWith("bearing:ball:"), true),
        };

        public readonly SandboxItems items;
        public readonly ItemDatabase db;
        public readonly Vector3 origin;
        public Bounds bounds, first;
        public readonly List<string> log = new();

        public GeometryTestSet(ItemDatabase db, SandboxItems items, Vector3 origin) { this.db = db; this.items = items; this.origin = origin; }

        public void Build()
        {
            var defs = db.order.Select(db.Get).Where(d => d != null && !string.IsNullOrEmpty(d.geometryKey)).ToList();
            var w = new ItemWorld(db);
            var used = new HashSet<string>();
            float x = 0;
            bounds = new Bounds(origin, Vector3.zero);
            log.Clear();
            int n = 0;
            foreach (var (role, match, largest) in Roles)
            {
                var c = defs.Where(d => match(d.geometryKey) && !used.Contains(d.geometryKey)).OrderBy(d => d.worldScale).ToList();
                if (c.Count == 0) { log.Add($"{role}: none in this preset"); continue; }
                var def = largest ? c[^1] : c[0];
                used.Add(def.geometryKey);
                var e = MeshLibrary.Get(def.geometryKey, MeshLibrary.LodWorld);
                var size = e.bounds.size * def.worldScale;
                var centre = new Vector3(origin.x + x + size.x / 2, size.y / 2, origin.z);
                w.Add(def.id, Matrix4x4.TRS(centre - e.bounds.center * def.worldScale, Quaternion.identity, Vector3.one * def.worldScale)).state = ItemState.InWorld;
                var b = new Bounds(centre, size);
                if (n++ == 0) first = b;
                bounds.Encapsulate(b);
                x += size.x + Gap;
                log.Add($"{role}: {def.geometryKey} ({def.id}), {Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 100:0.0} cm");
            }
            items.Show(w);
            Debug.Log("[sandbox] geometry test set: " + string.Join("; ", log));
        }

        /// <summary>For every procedural part key in the database: the shape mesh's bounds against the world mesh's
        /// (the pile and gallery place parts by those bounds, so they must agree), and the triangle counts.</summary>
        public static List<string> Audit(ItemDatabase db)
        {
            var o = new List<string>();
            var keys = db.order.Select(db.Get).Where(d => d != null && !string.IsNullOrEmpty(d.geometryKey)).Select(d => d.geometryKey)
                .Distinct().Where(k => ShapeMeshes.Has(k) && !SalvageMeshes.Has(k)).OrderBy(k => k).ToList();
            float worst = 0; string worstKey = "";
            long t1 = 0, t2 = 0, t3 = 0;
            var off = new List<string>();
            foreach (var k in keys)
            {
                var a = MeshLibrary.Get(k, MeshLibrary.LodWorld);
                var b = MeshLibrary.Get(k, ShapeMeshes.Lod);
                var h = MeshLibrary.Get(k, ShapeMeshes.LodHero);
                float m = Mathf.Max(a.bounds.size.x, Mathf.Max(a.bounds.size.y, a.bounds.size.z));
                float d = Mathf.Max((a.bounds.size - b.bounds.size).magnitude, (a.bounds.center - b.bounds.center).magnitude) / m;
                if (d > worst) { worst = d; worstKey = k; }
                if (d > 0.08f) off.Add($"{k} {d * 100:0}% (world {a.bounds.size.x:0.00}x{a.bounds.size.y:0.00}x{a.bounds.size.z:0.00}, shape {b.bounds.size.x:0.00}x{b.bounds.size.y:0.00}x{b.bounds.size.z:0.00})");
                t1 += Tris(a.mesh); t2 += Tris(b.mesh); t3 += Tris(h.mesh);
            }
            o.Add($"shape audit: {keys.Count} procedural keys; triangles summed world {t1:N0}, shape {t2:N0} (x{(float)t2 / Mathf.Max(1, t1):0.0}), hero {t3:N0} (x{(float)t3 / Mathf.Max(1, t1):0.0}); largest bounds deviation {worst * 100:0.0}% ({worstKey})");
            if (off.Count > 0) o.Add($"bounds deviating > 8%: {off.Count}: " + string.Join("; ", off));
            return o;
        }

        static long Tris(Mesh m) { long n = 0; for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3; return n; }
    }
}
