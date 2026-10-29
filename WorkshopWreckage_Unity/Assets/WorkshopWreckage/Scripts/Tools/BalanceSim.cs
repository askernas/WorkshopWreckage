using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Deterministic robot-only balance run (skill progression spec §9):
    /// --balance=&lt;out.json&gt; --balprofile=&lt;name&gt; --balstage=&lt;stage&gt; [--balseconds=600] [--balseed=1].
    /// The workshop is sorted up to the stage in a seeded random order through the real StorageSystem, so
    /// levels and skill points come from the game's own rules (never granted by hand); the stage's robots
    /// are built from their kits; the profile's list is bought with the normal purchase checks; then the
    /// robots work alone for a fixed game time at a fixed 0.05 s step. The player stands still: this
    /// measures robots, not a human playing (player tools are bought but never used).</summary>
    public class BalanceSim
    {
        public IEnumerator Run(Game g, string outPath)
        {
            string profileName = g.args.GetValueOrDefault("balprofile", "balanced_generalist");
            string stageName = g.args.GetValueOrDefault("balstage", "mid");
            float seconds = float.Parse(g.args.GetValueOrDefault("balseconds", "600"), System.Globalization.CultureInfo.InvariantCulture);
            int seed = int.Parse(g.args.GetValueOrDefault("balseed", "1"));
            var stage = Json.Obj(Shared.ReadObject(Shared.DataPath("balance_profiles/stages.json"))[stageName]);
            var profile = Shared.ReadObject(Shared.DataPath($"balance_profiles/{profileName}.json"));
            Random.InitState(seed);
            g.player.enabled = false;
            yield return null;
            var wb = g.workbench;
            var pr = g.progression;

            // 1. the stage's robots, each built from its own kit (before sorting, so no kit part is sorted away)
            var robots = Json.Arr(stage["robots"]).Select(x => x.ToString()).ToList();
            foreach (var pid in robots)
            {
                var p = wb.P(pid);
                var r = p == null ? null : wb.RobotOf(p);
                if (r == null || r.Operational) continue;
                if (p.crafted) wb.FillRecipe(p); else wb.ForceInstall(p);
                r.SetOperational();
            }
            var arms = g.robots.Where(r => r.arm != null && r.Operational).Select(r => r.arm).ToList();
            var sockets = g.sockets.Values.OrderBy(s => s.id).ToList();
            for (int k = 0; k < arms.Count && k < sockets.Count; k++) { arms[k].PickUp(); arms[k].Mount(sockets[k]); }

            // 2. the workshop sorted up to the stage, one part at a time in a seeded order
            int targetLevel = Json.Int(stage, "level", 0);
            double targetPct = Json.Num(stage, "restored_percent", 0);
            var rng = new System.Random(seed);
            // "sets": whole destinations in a seeded order (a player finishing sets on the way to a level);
            // "parts": single parts in a seeded order (the late game: many sets nearly complete)
            var loose = g.world.instances.Where(i => i.state == ItemState.InWorld && !g.world.DefOf(i.id).IsRobotPart).Select(i => i.id).ToList();
            var order = Json.Str(stage, "order", "sets") == "parts" ? loose.OrderBy(_ => rng.Next()).ToList()
                : loose.GroupBy(id => g.world.instances[id].defId).OrderBy(_ => rng.Next()).SelectMany(gr => gr).ToList();
            int sortedIn = 0;
            foreach (var id in order)
            {
                if (targetLevel > 0 && pr.level >= targetLevel) break;
                if (targetPct > 0 && sortedIn % 10 == 0 && g.progress.OverallPercent >= targetPct) break;
                g.index.Remove(id);
                if (g.storage.Deposit(g.world.DefOf(id).storageBox, id) != StorageSystem.Result.Ok) { g.index.Insert(id); continue; }
                g.progress.Discover(g.world.instances[id].defId);
                sortedIn++;
            }
            wb.Refresh();
            foreach (var u in g.units.Values) u.RefreshLabels();
            int legal = pr.skillPoints + pr.SkillPointsSpent;

            // 3. the profile's purchases, in its order, with the normal checks: a step the profile cannot
            // afford yet is saved for (pass 1 stops there); what is left is then spent greedily (pass 2)
            var buy = Json.Arr(profile["buy"]).Select(x => x.ToString()).ToList();
            var bought = new List<object>();
            var skipped = new Dictionary<string, object>();
            bool Try(string id, out string why)
            {
                bool skill = pr.Skill(id) != null;
                why = skill ? pr.CanUnlock(id) : pr.CanBuyUpgrade(id, g.robots.Any(r => r.type == pr.Upgrade(id)?.robot && r.Operational));
                if (why != "") return false;
                bool ok = skill ? g.UnlockSkill(id) : g.BuyRobotUpgrade(id);
                if (ok) bought.Add(skill ? id : $"{id}#{pr.RankOf(id)}");
                return ok;
            }
            var done = new HashSet<int>();
            for (int k = 0; k < buy.Count; k++)
            {
                if (Try(buy[k], out var why)) { done.Add(k); continue; }
                if (why.StartsWith("Needs")) break;
            }
            for (int k = 0; k < buy.Count; k++)
            {
                if (done.Contains(k)) continue;
                if (Try(buy[k], out var why)) done.Add(k);
                else if (why != "Maximum rank" && why != "Already unlocked") skipped[buy[k]] = why;
            }
            Debug.Log($"[balance] {profileName} @ {stageName}: level {pr.level}, {pr.DestinationsCompleted} destinations, {g.progress.OverallPercent:0.0} % restored, legal points {legal}, spent {pr.SkillPointsSpent}, left {pr.skillPoints}");

            // 4. robots alone, fixed step
            int completed0 = pr.DestinationsCompleted;
            double pct0 = g.progress.OverallPercent;
            int sorted0 = g.progress.sortedTotal;
            var samples = new List<object>();
            Time.captureDeltaTime = 0.05f;
            float t0 = Time.time, nextSample = 60;
            var wall = System.Diagnostics.Stopwatch.StartNew();
            while (Time.time - t0 < seconds)
            {
                yield return null;
                float t = Time.time - t0;
                if (t >= nextSample)
                {
                    nextSample += 60;
                    samples.Add(new Dictionary<string, object> { ["t"] = (double)Mathf.Round(t), ["completed"] = (double)(pr.DestinationsCompleted - completed0), ["sorted"] = (double)(g.progress.sortedTotal - sorted0), ["level"] = (double)pr.level });
                }
            }
            Time.captureDeltaTime = 0;

            var per = new List<object>();
            foreach (var r in g.robots.Where(r => r.Operational))
            {
                var s = r.stats;
                per.Add(new Dictionary<string, object>
                {
                    ["robot"] = r.id, ["type"] = r.type, ["stored"] = (double)s.stored, ["staged"] = (double)s.staged,
                    ["assisted"] = (double)s.Assisted(pr.completedDestinations), ["finals"] = (double)s.finals,
                    ["avg_delivery_s"] = System.Math.Round(s.AvgDeliveryS, 1),
                    ["busy"] = System.Math.Round(s.Share(s.busyS), 3), ["idle"] = System.Math.Round(s.Share(s.idleS), 3),
                    ["blocked"] = System.Math.Round(s.Share(s.blockedS), 3), ["charging"] = System.Math.Round(s.Share(s.chargingS), 3),
                });
            }
            var res = new Dictionary<string, object>
            {
                ["profile"] = profileName, ["stage"] = stageName, ["seed"] = (double)seed, ["seconds"] = (double)seconds,
                ["preset"] = g.presetId, ["robot_only"] = true,
                ["level"] = (double)pr.level, ["legal_points"] = (double)legal, ["spent"] = (double)pr.SkillPointsSpent, ["unspent"] = (double)pr.skillPoints,
                ["restored_percent_start"] = System.Math.Round(pct0, 2), ["restored_percent_end"] = System.Math.Round(g.progress.OverallPercent, 2),
                ["completed_at_start"] = (double)completed0, ["completed_in_run"] = (double)(pr.DestinationsCompleted - completed0),
                ["sorted_in_run"] = (double)(g.progress.sortedTotal - sorted0), ["level_end"] = (double)pr.level,
                ["bought"] = bought, ["not_bought"] = skipped, ["robots"] = per, ["samples"] = samples,
                ["wall_s"] = System.Math.Round(wall.Elapsed.TotalSeconds, 1),
            };
            File.WriteAllText(outPath, Json.Write(res));
            Debug.Log($"[balance] {profileName} @ {stageName}: {res["completed_in_run"]} destinations completed, {res["sorted_in_run"]} parts sorted in {seconds:0} s game time ({res["wall_s"]} s wall) -> {outPath}");
            Application.Quit();
        }
    }
}
