using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Component rescaling and the robot carry limit (shared/data/component_scale.json,
    /// docs/COMPONENT_RESCALE.md). A sortable definition's factor comes from its baseline size: the
    /// longest side of its world mesh at the catalogue's world_scale (mesh_scale_mm × visual_scale),
    /// never from a runtime transform, so it is the same in every preset and never applied twice.
    /// The smallest part gets max_factor, falling on a log scale to 1× at full_size_mm; parts at or
    /// above that keep their size (the largest parts never grow). The factor scales all three axes,
    /// and with them the rendering, every size rule (Fits, slots, grip, reach) and the drop heights;
    /// weights stay the catalogue's. "enabled": false restores the original sizes and lets robots
    /// take every part again.
    ///
    /// Robots handle the smallest robot_share (80 %) of the sortable definitions, ranked by final
    /// size over the whole catalogue (all presets): the list of player-only ids is generated
    /// (CatalogAuthoring.WriteComponentScale) and checked by ValidateAll. Ties: final longest side
    /// rounded to 0.1 mm, then the lower id counts as the smaller part.</summary>
    public static class ComponentScale
    {
        public static string Path => Shared.DataPath("component_scale.json");

        static bool _loaded;
        static bool _enabled;
        static float _maxFactor = 2f, _smallestMm = 33.5f, _fullMm = 250f, _share = 0.8f;
        static HashSet<int> _playerOnly = new();

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!System.IO.File.Exists(Path)) return;
                var d = Shared.ReadObject(Path);
                _enabled = Json.Bool(d, "enabled");
                _maxFactor = (float)Json.Num(d, "max_factor", 2);
                _smallestMm = (float)Json.Num(d, "smallest_mm", 33.5);
                _fullMm = (float)Json.Num(d, "full_size_mm", 250);
                _share = (float)Json.Num(d, "robot_share", 0.8);
                _playerOnly = Json.Arr(d.GetValueOrDefault("player_only")).Select(x => (int)(double)x).ToHashSet();
            }
            catch (System.Exception e) { Debug.LogError("component_scale.json: " + e.Message); _enabled = false; }
        }

        public static bool Enabled { get { Load(); return _enabled; } }
        public static float RobotShare { get { Load(); return _share; } }
        public static float MaxFactor { get { Load(); return _maxFactor; } }
        public static float SmallestMm { get { Load(); return _smallestMm; } }
        public static IReadOnlyCollection<int> PlayerOnly { get { Load(); return _playerOnly; } }

        /// <summary>Factor for a baseline longest side (mm), with the curve's anchors.</summary>
        public static float Factor(float baselineMm, float smallestMm, float fullMm, float maxFactor)
        {
            if (baselineMm >= fullMm) return 1f;
            if (baselineMm <= smallestMm) return maxFactor;
            return Mathf.Pow(maxFactor, 1f - Mathf.Log(baselineMm / smallestMm) / Mathf.Log(fullMm / smallestMm));
        }

        public static float Factor(float baselineMm) { Load(); return _enabled ? Factor(baselineMm, _smallestMm, _fullMm, _maxFactor) : 1f; }

        /// <summary>Longest side (mm) at the catalogue's world_scale, whatever the current factor.</summary>
        public static float BaselineMm(ItemDefinition def) => ItemDatabase.Extents(def)[0] * 1000f / def.componentScale;

        /// <summary>Applied once per definition when the catalogue is read (ItemDatabase.Load).</summary>
        public static void Apply(ItemDefinition def)
        {
            def.componentScale = 1f;
            def.robotCarry = true;
            if (def.IsRobotPart) return;
            def.componentScale = Factor(BaselineMm(def));
            def.worldScale = def.baseWorldScale * def.componentScale;
            def.robotCarry = !Enabled || !_playerOnly.Contains(def.id);
        }

        /// <summary>Rank key: final longest side in 0.1 mm, then the id.</summary>
        static (int size, int id) Key(ItemDefinition def, float finalMm) => (Mathf.RoundToInt(finalMm * 10f), def.id);

        /// <summary>The player-only ids for these sortable definitions (the largest 1 - share by final
        /// size, at the given anchors), and the smallest baseline they define.</summary>
        public static (List<int> playerOnly, float smallestMm) Rank(IEnumerable<ItemDefinition> defs, float fullMm, float maxFactor, float share)
        {
            var list = defs.Where(d => !d.IsRobotPart).GroupBy(d => d.id).Select(g => g.First()).ToList();
            var baseMm = list.ToDictionary(d => d.id, BaselineMm);
            float smallest = Mathf.Round(baseMm.Values.Min() * 10f) / 10f;
            var ranked = list.OrderBy(d => Key(d, baseMm[d.id] * Factor(baseMm[d.id], smallest, fullMm, maxFactor))).ToList();
            int eligible = Mathf.RoundToInt(ranked.Count * share);
            return (ranked.Skip(eligible).Select(d => d.id).OrderBy(x => x).ToList(), smallest);
        }

        /// <summary>Checks the stored anchors and list against the catalogue (ValidateAll).</summary>
        public static IEnumerable<string> Check(IEnumerable<ItemDefinition> defs)
        {
            Load();
            if (!_enabled) yield break;
            var (po, smallest) = Rank(defs, _fullMm, _maxFactor, _share);
            if (Mathf.Abs(smallest - _smallestMm) > 0.05f) yield return $"component_scale.json: smallest_mm is {_smallestMm}, the catalogue's smallest part is {smallest} mm (run CatalogAuthoring.WriteComponentScale)";
            if (!po.ToHashSet().SetEquals(_playerOnly)) yield return $"component_scale.json: the player-only list ({_playerOnly.Count}) does not match the ranking ({po.Count}, {po.Except(_playerOnly).Count()} missing) (run CatalogAuthoring.WriteComponentScale)";
        }

        /// <summary>Saves (version 3): a saved loose part takes its definition's current size, whatever
        /// size it was saved at (so loading twice never scales twice). Its lowest point stays where it
        /// was, so a part on the floor neither sinks in nor floats.</summary>
        public static Matrix4x4 Resize(Matrix4x4 m, ItemDefinition def)
        {
            float s = new Vector3(m.m00, m.m10, m.m20).magnitude;
            if (s <= 0 || Mathf.Abs(s - def.worldScale) <= def.worldScale * 1e-4f) return m;
            var rot = m.rotation;
            float low = LowestY(Matrix4x4.Rotate(rot), def);   // per unit of scale, below the pivot
            var pos = (Vector3)m.GetColumn(3);
            pos.y += low * (s - def.worldScale);
            return Matrix4x4.TRS(pos, rot, Vector3.one * def.worldScale);
        }

        /// <summary>Lowest point (world y) of the part's mesh bounds under this transform.</summary>
        public static float LowestY(Matrix4x4 m, ItemDefinition def)
        {
            var b = MeshLibrary.UnitBounds(def.geometryKey);
            float low = float.MaxValue;
            for (int k = 0; k < 8; k++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                low = Mathf.Min(low, m.MultiplyPoint3x4(c).y);
            }
            return low;
        }
    }
}
