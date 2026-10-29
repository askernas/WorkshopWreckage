using System.Collections.Generic;
using System.Linq;

namespace WorkshopWreckage
{
    /// <summary>One robot's measured performance (skill progression spec §8): parts stored and staged,
    /// destinations it helped complete and the ones whose last part it stored, average pick-to-drop time
    /// and how its working time splits into busy, charging, idle and blocked. Real counts only: nothing
    /// is estimated, and a robot that has not worked yet reports zeros. Saved with the robot.</summary>
    public class RobotStats
    {
        public int stored, staged, finals;
        public double busyS, chargingS, idleS, blockedS;
        double _deliverS;
        int _deliverN;
        /// <summary>Destinations it stored or staged a part for (they count as assisted once complete).</summary>
        public readonly HashSet<int> helped = new();
        readonly Dictionary<int, double> _pickedAt = new();

        public void Picked(int itemId, double t) => _pickedAt[itemId] = t;

        /// <summary>A part it stored (staging = false) or left in a staging zone.</summary>
        public void Dropped(int itemId, int defId, double t, bool staging)
        {
            if (staging) staged++; else stored++;
            helped.Add(defId);
            if (_pickedAt.Remove(itemId, out var t0) && t >= t0) { _deliverS += t - t0; _deliverN++; }
        }

        /// <summary>Time in one of StatusWord()'s states while the robot is in service.</summary>
        public void Tick(double dt, string word)
        {
            switch (word)
            {
                case "WORKING": case "DELIVERING": case "STAGING": busyS += dt; break;
                case "CHARGING": chargingS += dt; break;
                case "STAGING FULL": case "NO REACHABLE PART": case "WAITING FOR ACCESS": case "PLAYER HELP NEEDED": blockedS += dt; break;
                case "IDLE": idleS += dt; break;
            }
        }

        public double ServiceS => busyS + chargingS + idleS + blockedS;
        public double AvgDeliveryS => _deliverN > 0 ? _deliverS / _deliverN : -1;
        public int Assisted(ICollection<int> completed) => helped.Count(completed.Contains);
        public double Share(double s) => ServiceS > 0 ? s / ServiceS : 0;

        public Dictionary<string, object> ToDict() => new()
        {
            ["stored"] = (double)stored, ["staged"] = (double)staged, ["finals"] = (double)finals,
            ["busy_s"] = busyS, ["charging_s"] = chargingS, ["idle_s"] = idleS, ["blocked_s"] = blockedS,
            ["deliver_s"] = _deliverS, ["deliver_n"] = (double)_deliverN,
            ["helped"] = helped.Select(x => (object)(double)x).ToList(),
        };

        public void Load(Dictionary<string, object> d)
        {
            if (d == null) return;
            stored = Json.Int(d, "stored"); staged = Json.Int(d, "staged"); finals = Json.Int(d, "finals");
            busyS = Json.Num(d, "busy_s"); chargingS = Json.Num(d, "charging_s"); idleS = Json.Num(d, "idle_s"); blockedS = Json.Num(d, "blocked_s");
            _deliverS = Json.Num(d, "deliver_s"); _deliverN = Json.Int(d, "deliver_n");
            helped.Clear();
            foreach (var x in Json.Arr(d.GetValueOrDefault("helped"))) helped.Add((int)(double)x);
        }
    }
}
