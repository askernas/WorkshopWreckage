using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace WorkshopWreckage
{
    /// <summary>Player collection tools unlocked through skills (progression.json):
    /// Handful (bulk pickup of the same part around the one you pick), Magnetic Glove
    /// (hold B: copies of the magnetic part in your first slot fly to you, as many as the glove's
    /// level I-X; playtest 2026-10-25, it replaced the material / category filters that matched the part
    /// in hand), Parts Scanner (V: highlights unsorted copies of the part in hand and
    /// its destination), Supermagnet (X: exact-match attraction of the held part's
    /// definition). All of them move real items into the inventory: never sorted
    /// parts, never parts reserved or carried by robots, never robot components, and
    /// never more than the free inventory space. Collected parts still have to be
    /// delivered to their destinations by hand.</summary>
    public class PlayerTools : MonoBehaviour
    {
        Game g;
        float _magnetT, _superReady, _scanUntil;
        int _magnetTaken;
        readonly List<int> _scanned = new();
        string _scanDest;
        Material _outline, _destGhost;
        MaterialPropertyBlock _mpb;
        Mesh _cube;

        Progression P => g.progression;
        Inventory Inv => g.inventory;
        public int lastScanCount, lastSuperCount, lastMagnetCount;

        public void Init(Game game)
        {
            g = game;
            _outline = new Material(EnvMaterials.Outline);
            _destGhost = EnvMaterials.NewGhost();
            _destGhost.SetColor("_Color", new Color(0.4f, 1f, 0.55f, 0.35f));
            _mpb = new MaterialPropertyBlock();
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cube = c.GetComponent<MeshFilter>().sharedMesh;
            Destroy(c);
        }

        public void ResetState() { _scanned.Clear(); _scanUntil = 0; _superReady = 0; _magnetTaken = 0; }

        Vector3 Hand => g.held.HandWorld().GetColumn(3);
        /// <summary>The glove sweeps low in front of the player.</summary>
        Vector3 MagnetOrigin
        {
            get
            {
                var t = g.player.transform;
                return t.position + Vector3.up * 0.6f + Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized * 0.5f;
            }
        }
        /// <summary>Magnetic Glove level 0-10: how many copies one hold of B pulls in.</summary>
        public int MagnetLevel => (int)P.Effect("magnet_level");
        int HeldDef => g.inventory.HeldId() >= 0 ? g.world.instances[g.inventory.HeldId()].defId : -1;

        /// <summary>Can the player's tools take this part right now?</summary>
        bool Takeable(int id)
        {
            var inst = g.world.instances[id];
            return inst.state == ItemState.InWorld && !g.robotReserved.Contains(id) && !g.interaction.busy.Contains(id) && !g.world.DefOf(id).IsRobotPart;
        }

        bool LineOfSight(Vector3 from, Vector3 to)
        {
            var d = to - from;
            float len = d.magnitude;
            if (len < 0.05f) return true;
            return !Physics.Raycast(from, d / len, len - 0.05f, 1 << Layers.Env);
        }

        readonly List<(Matrix4x4 from, ItemDefinition def)> _toSettle = new();

        /// <summary>Moves a part into the inventory with a flying animation. In a batch
        /// (settleNow = false) the parts around it settle only after the whole batch
        /// has been taken — settling parts are busy and would otherwise be skipped.</summary>
        bool Take(int id, float flyTime, bool settleNow = true)
        {
            if (!Takeable(id) || Inv.IsFull) return false;
            var inst = g.world.instances[id];
            var from = inst.transform;
            var def = g.world.DefOf(id);
            g.index.Remove(id);
            g.world.SetState(id, ItemState.Carried, "player");
            Inv.Add(id);
            if (settleNow) g.interaction.SettleNeighbours(from, def); else _toSettle.Add((from, def));
            g.interaction.StartFly(def, from, g.held.HandWorld(), flyTime);
            return true;
        }

        /// <summary>A pulled part that no longer fits in the pockets flies into the sorting tray.</summary>
        void ToTray(int id, float flyTime)
        {
            if (!Takeable(id)) return;
            var inst = g.world.instances[id];
            var from = inst.transform;
            var def = g.world.DefOf(id);
            var m = g.interaction.TrayMatrix(def);
            g.index.Remove(id);
            g.world.SetTransform(id, m);
            g.world.SetState(id, ItemState.InWorld, "tray");
            g.index.Insert(id);
            _toSettle.Add((from, def));
            g.interaction.StartFly(def, from, m, flyTime);
        }

        /// <summary>How many of `ids` fit in the pockets (the rest go to the tray).</summary>
        int Fits(int n) => Mathf.Min(n, Mathf.Max(0, Inv.capacity - Inv.Count));

        /// <summary>Recharge in proportion to what was pulled (at least 30 %).</summary>
        static float Recharge(float full, int pulled, int max) => full * Mathf.Clamp(pulled / (float)Mathf.Max(1, max), 0.3f, 1f);

        void SettleBatch()
        {
            foreach (var (from, def) in _toSettle) g.interaction.SettleNeighbours(from, def);
            _toSettle.Clear();
        }

        void Update()
        {
            if (g == null || g.world == null || P == null || g.state != "play" || g.interaction.inspecting || g.Automated) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.bKey.isPressed) MagnetTick(Time.deltaTime);
            else _magnetTaken = 0;
            if (kb.vKey.wasPressedThisFrame) Scan();
            if (kb.xKey.wasPressedThisFrame) Supermagnet();
            if (kb.nKey.wasPressedThisFrame) AreaSweep();
        }

        void LateUpdate() => DrawScan();

        // ---------------------------------------------------------------- handful

        /// <summary>Called after a manual pickup: with Handful, nearby copies of the
        /// same part come along (same definition only).</summary>
        public int OnManualPickup(int id)
        {
            if (P == null) return 0;
            int n = (int)P.Effect("bulk_pickup_count");
            if (n <= 1) return 0;
            float r = (float)P.Effect("bulk_pickup_radius_m");
            var p = g.world.instances[id].Position;
            int def = g.world.instances[id].defId;
            int taken = 0;
            foreach (var o in g.index.QuerySphere(p, r).Where(o => o != id && g.world.instances[o].defId == def && !g.Covered(o))
                         .OrderBy(o => (g.world.instances[o].Position - p).sqrMagnitude).Take(n - 1).ToList())
                if (Take(o, 0.18f + taken * 0.04f, false)) taken++;
            SettleBatch();
            if (taken > 0) g.telemetry?.SkillUsed("bulk_pickup");
            return taken;
        }

        // ---------------------------------------------------------------- magnet

        /// <summary>Hold B: copies of the magnetic part in the first inventory slot fly to the hand one
        /// at a time (nearest first), as many per hold as the glove's level (1-10).</summary>
        public int MagnetTick(float dt)
        {
            float range = (float)P.Effect("magnet_range_m") * g.Scale("tool_range");
            if (range <= 0) { if (_magnetT <= 0) { g.hud.OnFeedback("You need the Magnetic Glove skill (Esc → Skills)", false); _magnetT = 2; } _magnetT -= dt; return 0; }
            int max = MagnetLevel;
            _magnetT -= dt;
            if (_magnetT > 0 || _magnetTaken >= max) return 0;
            _magnetT = 0.11f;
            var hand = MagnetOrigin;
            var stacks = Inv.Stacks();
            if (stacks.Count == 0) { g.hud.OnFeedback("Magnetic Glove: pick up the part you want more of (it pulls copies of your first slot)", false); _magnetT = 2; return 0; }
            var want = g.db.Get(stacks[0].defId);
            if (!want.magnetic) { g.hud.OnFeedback($"Magnetic Glove: {want.displayName} isn't magnetic", false); _magnetT = 2; return 0; }
            int pick = -1;
            foreach (var i in g.index.QuerySphere(hand, range)
                         .Where(i => Takeable(i) && g.world.instances[i].defId == want.id)
                         .OrderBy(i => (g.world.instances[i].Position - hand).sqrMagnitude))
                if (LineOfSight(hand, g.world.instances[i].Position)) { pick = i; break; }
            if (pick < 0) { if (_magnetTaken == 0) { g.hud.OnFeedback($"Magnetic Glove: no {want.displayName} within {range:0.0} m", false); _magnetT = 2; } return 0; }
            if (Inv.IsFull) { g.hud.OnFeedback($"Your hands are full ({Inv.Count}/{Inv.capacity})", false); _magnetT = 1; return 0; }
            float dist = (g.world.instances[pick].Position - hand).magnitude;
            if (!Take(pick, Mathf.Lerp(0.12f, 0.35f, dist / Mathf.Max(range, 0.1f)))) return 0;
            _magnetTaken++;
            lastMagnetCount++;
            Sfx.Play("magnet", hand, -10, 0.15f);
            g.telemetry?.SkillUsed("magnet");
            return 1;
        }

        // ---------------------------------------------------------------- scanner

        /// <summary>V: highlights unsorted copies of the part in hand (or aimed at)
        /// within range and shows where it belongs.</summary>
        public int Scan()
        {
            float r = (float)P.Effect("scan_radius_m");
            if (r <= 0) { g.hud.OnFeedback("You need the Parts Scanner skill (Esc → Skills)", false); return 0; }
            int def = HeldDef >= 0 ? HeldDef : g.interaction.targetType == "item" ? g.world.instances[g.interaction.targetItem].defId : -1;
            if (def < 0) { g.hud.OnFeedback("Scanner: hold or aim at a part to scan for it", false); return 0; }
            var me = g.player.transform.position;
            _scanned.Clear();
            _scanned.AddRange(g.world.IdsOf(def).Where(i => g.world.instances[i].state == ItemState.InWorld && (g.world.instances[i].Position - me).magnitude <= r));
            _scanDest = P.Has("scan_highlights_destination") ? g.db.Get(def).storageBox : null;
            _scanUntil = Time.time + 6;
            lastScanCount = _scanned.Count;
            var d = g.db.Get(def);
            string remaining = P.Has("scan_shows_remaining") ? $"  ·  {g.world.IdsOf(def).Count(i => g.world.instances[i].state != ItemState.Stored)} not yet sorted in the workshop" : "";
            g.hud.OnFeedback($"Scanner: {_scanned.Count} × {d.displayName} within {r:0} m  →  {g.db.BoxLabel(d.storageBox)}{remaining}", true);
            Sfx.Play("discover", null, -8, 0);
            g.telemetry?.SkillUsed("scanner");
            return _scanned.Count;
        }

        void DrawScan()
        {
            if (Time.time > _scanUntil || g.world == null) return;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6);
            var rp = new RenderParams(_outline) { matProps = _mpb, shadowCastingMode = ShadowCastingMode.Off };
            foreach (var id in _scanned)
            {
                var inst = g.world.instances[id];
                if (inst.state != ItemState.InWorld) continue;
                var mesh = g.renderer.WorldMeshFor(id);
                _mpb.SetColor("_Color", Color.Lerp(new Color(0.3f, 0.9f, 1f), Color.white, pulse * 0.4f));
                for (int s = 0; s < mesh.subMeshCount; s++) Graphics.RenderMesh(rp, mesh, s, inst.transform);
            }
            if (_scanDest != null && g.db.boxes.TryGetValue(_scanDest, out var dd) && g.units.TryGetValue(Json.Str(dd, "unit"), out var unit))
            {
                var p = unit.DestPoint(dd);
                var size = new Vector3((float)Json.Num(dd, "width", 0.2), Mathf.Min(0.4f, (float)Json.Num(dd, "height", 0.2)), 0.08f);
                var m = Matrix4x4.TRS(p, unit.transform.rotation, size * (1.05f + 0.05f * pulse));
                Graphics.RenderMesh(new RenderParams(_destGhost) { shadowCastingMode = ShadowCastingMode.Off }, _cube, 0, m);
            }
        }

        // ---------------------------------------------------------------- supermagnet

        /// <summary>X: every unsorted copy of the held part's exact definition within
        /// range and line of sight flies to the hand (rules in progression.json).</summary>
        public int Supermagnet()
        {
            if (!P.Has("supermagnet")) { g.hud.OnFeedback("You need the Supermagnet skill (Esc → Skills)", false); return 0; }
            int def = HeldDef;
            if (def < 0) { g.hud.OnFeedback("Supermagnet: hold the part you want more of", false); return 0; }
            if (Time.time < _superReady) { g.hud.OnFeedback($"Supermagnet recharging ({_superReady - Time.time:0} s)", false); return 0; }
            var cfg = new Dictionary<string, object>(P.supermagnet)
            {
                ["radius_m"] = Json.Num(P.supermagnet, "radius_m", 6) * g.Scale("tool_range"),
                ["max_items_per_activation"] = (double)Mathf.RoundToInt(Json.Int(P.supermagnet, "max_items_per_activation", 25) * g.Scale("tool_items")),
            };
            var hand = Hand;
            bool los = Json.Bool(cfg, "line_of_sight_required", true);
            float radius = (float)Json.Num(cfg, "radius_m", 6);
            var candidates = g.world.IdsOf(def).Select(i =>
            {
                var inst = g.world.instances[i];
                float dist = (inst.Position - hand).magnitude;
                return new WorkshopWreckage.Supermagnet.Candidate
                {
                    id = i, defId = inst.defId, inWorld = inst.state == ItemState.InWorld && !g.interaction.busy.Contains(i),
                    reservedByRobot = g.robotReserved.Contains(i), robotPart = g.db.Get(def).IsRobotPart, distance = dist,
                    lineOfSight = dist <= radius && (!los || LineOfSight(hand, inst.Position)) && (inst.state != ItemState.InWorld || !g.Covered(i)),   // not from under a pile
                };
            }).ToList();
            var ids = WorkshopWreckage.Supermagnet.Select(def, candidates, cfg, Inv.capacity - Inv.Count);
            if (ids.Count == 0) { g.hud.OnFeedback("Supermagnet: no more of those in range", false); return 0; }
            int toHand = Fits(ids.Count);
            StartCoroutine(Pull(ids));
            _superReady = Time.time + Recharge((float)Json.Num(cfg, "cooldown_s", 20), ids.Count, Json.Int(cfg, "max_items_per_activation", 25));
            lastSuperCount = ids.Count;
            lastToTray = ids.Count - toHand;
            Sfx.Play("supermagnet", hand, -2, 0);
            g.hud.OnFeedback($"Supermagnet: {ids.Count} × {g.db.Get(def).displayName}" + (lastToTray > 0 ? $"  ({toHand} to your pockets, {lastToTray} to the sorting tray)" : ""), true);
            g.telemetry?.SkillUsed("supermagnet");
            return ids.Count;
        }

        // ---------------------------------------------------------------- area sweep

        /// <summary>N: every unsorted part lying around you (any kind) comes to your
        /// hands, nearest first, up to the sweep limit and free inventory space.</summary>
        public int AreaSweep()
        {
            float r = (float)P.Effect("area_collect_radius_m") * g.Scale("tool_range");
            if (r <= 0) { g.hud.OnFeedback("You need the Area Sweep skill (Esc → Skills)", false); return 0; }
            if (Time.time < _sweepReady) { g.hud.OnFeedback($"Area Sweep recharging ({_sweepReady - Time.time:0} s)", false); return 0; }
            int max = Mathf.RoundToInt((float)P.Effect("area_collect_max_items") * g.Scale("tool_items"));   // what doesn't fit goes to the tray
            var me = g.player.transform.position + Vector3.up * 0.5f;
            var ids = g.index.QuerySphere(me, r).Where(Takeable).Where(i => LineOfSight(Hand, g.world.instances[i].Position) && !g.Covered(i))
                .OrderBy(i => (g.world.instances[i].Position - me).sqrMagnitude).Take(max).ToList();
            if (ids.Count == 0) { g.hud.OnFeedback("Area Sweep: nothing loose around you", false); return 0; }
            int toHand = Fits(ids.Count);
            StartCoroutine(Pull(ids));
            _sweepReady = Time.time + Recharge((float)Mathf.Max(1f, (float)P.Effect("area_collect_cooldown_s")), ids.Count, max);
            lastToTray = ids.Count - toHand;
            Sfx.Play("supermagnet", Hand, -4, 0);
            g.hud.OnFeedback($"Area Sweep: {ids.Count} parts" + (lastToTray > 0 ? $"  ({toHand} to your pockets, {lastToTray} to the sorting tray)" : ""), true);
            g.telemetry?.SkillUsed("area_collection");
            return ids.Count;
        }

        float _sweepReady;
        /// <summary>How many parts the last Supermagnet / Area Sweep sent to the sorting tray.</summary>
        public int lastToTray;

        /// <summary>All parts change hands at once (nothing can grab them in between);
        /// only the flights are staggered for the effect.</summary>
        IEnumerator Pull(List<int> ids)
        {
            var hand = Hand;
            int k = 0;
            foreach (var id in ids)
            {
                float dist = (g.world.instances[id].Position - hand).magnitude;
                if (Inv.IsFull) { ToTray(id, 0.6f + 0.03f * k++); continue; }   // overflow: the robots sort it from the tray
                Take(id, Mathf.Lerp(0.25f, 0.7f, dist / 6f) + 0.04f * k++, false);
            }
            SettleBatch();
            yield break;
        }
    }
}
