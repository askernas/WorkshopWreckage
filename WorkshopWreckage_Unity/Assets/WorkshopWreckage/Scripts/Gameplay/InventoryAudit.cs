using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Endgame inventory audit (design v1.0 §13.3): from 95 % restored the Tab screen lists what is
    /// left and where it is, with no level bonus. At 99 % it names the spot of every remaining copy and
    /// the scanner (V, empty-handed) outlines them all, so the end never becomes pixel hunting.</summary>
    public static class InventoryAudit
    {
        public const float OpensAt = 95f, PreciseAt = 99f;

        public static bool Open(Game g) => g.progress != null && g.progress.OverallPercent >= OpensAt;
        public static bool Precise(Game g) => g.progress != null && g.progress.OverallPercent >= PreciseAt;

        /// <summary>Ordinary copies not yet in their own destination (and not used by crafting).</summary>
        public static List<ItemInstance> Remaining(Game g) =>
            g.world.instances.Where(i => !g.world.DefOf(i.id).IsRobotPart && i.state != ItemState.Installed && !g.progress.accepted.Contains(i.id)).ToList();

        /// <summary>Robot components still lying in the workshop or carried by the player.</summary>
        public static List<ItemInstance> LooseRobotParts(Game g) =>
            g.world.instances.Where(i => g.world.DefOf(i.id).IsRobotComponent && (i.state == ItemState.InWorld || i.state == ItemState.Carried)).ToList();   // Part Radar spares never block the end

        /// <summary>Where a copy is, in the audit's words.</summary>
        public static string Where(Game g, ItemInstance i, bool precise)
        {
            if (i.state == ItemState.Carried) return i.owner == "player" ? "in your inventory" : "carried by a robot";
            if (i.state == ItemState.Stored) return $"stored in the wrong place ({g.db.BoxLabel(i.owner.StartsWith("box:") ? i.owner.Substring(4) : i.owner)})";
            if (g.InTray(i.Position)) return "in the sorting tray";
            if (i.owner.StartsWith("stage:")) return $"staged below {g.db.BoxLabel(i.owner.Substring(6))}";
            string zone = g.layout.zones[g.layout.ZoneAt(i.Position)].name;
            if (!precise) return zone;
            string height = i.Position.y < 0.15f ? "on the floor" : i.Position.y < 1.0f ? $"low, {i.Position.y:0.0} m up" : $"high, {i.Position.y:0.0} m up";
            return $"{zone}, {height}, near {NearestUnit(g, i.Position)}";
        }

        static string NearestUnit(Game g, Vector3 p)
        {
            StorageUnit best = null; float bd = float.MaxValue;
            foreach (var u in g.units.Values)
            {
                float d = (u.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = u; }
            }
            if (best == null) return "the middle of the room";
            string kind = best.type == "drawer_cabinet" ? "drawer cabinet" : best.type == "pallet_zone" ? "pallet area" : "shelving";
            return $"{kind} {best.id} ({Mathf.Sqrt(bd):0.0} m)";
        }

        /// <summary>The Tab-screen section; "" below 95 %.</summary>
        public static string Report(Game g)
        {
            if (!Open(g)) return "";
            bool precise = Precise(g);
            var left = Remaining(g);
            var t = $"\n<b><color=#d8b45a>INVENTORY AUDIT</color></b>  <color=#b8a890>({(precise ? "precise: every copy located, V with empty hands outlines them" : $"opens fully at {PreciseAt:0} %")})</color>\n";
            if (left.Count == 0) t += "Every ordinary part is in its own destination.\n";
            var defs = left.GroupBy(i => i.defId)
                           .Select(gr => (def: g.db.Get(gr.Key), copies: gr.ToList(), need: g.world.TotalOf(gr.Key) - g.progress.AcceptedOf(gr.Key)))
                           .OrderBy(d => d.need).ThenBy(d => d.def.displayName).ToList();
            int oneAway = defs.Count(d => d.need == 1);
            t += $"{defs.Count} destination{(defs.Count == 1 ? "" : "s")} still open, {left.Count} part{(left.Count == 1 ? "" : "s")} to place · {oneAway} one part from complete\n";
            const int MaxRows = 14;   // the Tab screen does not scroll: the nearest-to-complete first
            foreach (var d in defs.Take(MaxRows))
            {
                t += $"   <b>{d.def.displayName}</b>  {g.progress.AcceptedOf(d.def.id)}/{g.world.TotalOf(d.def.id)}{(!d.def.robotCarry ? "  <color=#d98a4a>player-only</color>" : "")}  → {g.db.BoxLabel(d.def.storageBox)}"
                     + $"  <color=#b8a890>·  {string.Join("; ", d.copies.GroupBy(i => Where(g, i, precise)).Select(gr => gr.Count() > 1 ? $"{gr.Key} ×{gr.Count()}" : gr.Key))}</color>\n";
            }
            if (defs.Count > MaxRows) t += $"   … and {defs.Count - MaxRows} more\n";
            var parts = LooseRobotParts(g);
            if (parts.Count > 0)
                t += $"Robot components not at the workbench: {parts.Count}" + (precise
                    ? "  ·  " + string.Join("; ", parts.Select(i => $"{g.world.DefOf(i.id).displayName}: {Where(g, i, true)}")) + "\n"
                    : $"  ·  {string.Join(", ", parts.GroupBy(i => Where(g, i, false)).Select(gr => $"{gr.Key} ×{gr.Count()}"))}  (Robot Part Sense finds the nearest)\n");
            return t;
        }
    }
}
