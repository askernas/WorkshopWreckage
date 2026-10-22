using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Scripted gameplay validation in the running game (port of autoplay.gd):
    /// the same checks, driven through the same Interaction code the player uses.</summary>
    public class Autoplay
    {
        Game g;
        string outDir;
        readonly List<object> checks = new();
        int passed, failed;

        public IEnumerator Run(Game game, string dir)
        {
            g = game;
            outDir = dir;
            Directory.CreateDirectory(outDir);
            var ic = g.interaction;
            var w = g.world;
            g.SetState("menu");
            yield return g.Capture(Path.Combine(outDir, "00_main_menu.png"), 15);
            g.SetState("play");
            yield return Frames(10);

            // 0. keyboard turning: inject real Q / E key presses through the Input System
            var kb = UnityEngine.InputSystem.Keyboard.current ?? UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            g.player.enabled = true;
            float yaw0 = g.player.Yaw;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Q));
            yield return new WaitForSeconds(0.2f);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            yield return null;
            float yawLeft = g.player.Yaw;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.E));
            yield return new WaitForSeconds(0.4f);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            yield return null;
            float yawRight = g.player.Yaw;
            Check("Q turns left, E turns right", yawLeft > yaw0 + 0.1f && yawRight < yawLeft - 0.2f, $"yaw {yaw0:0.00} -> {yawLeft:0.00} (Q) -> {yawRight:0.00} (E)");

            // 0b. walking into a heap: the player climbs onto the parts instead of passing through
            {
                var hs = g.layout.piles.First(p => p.kind == "heap" && p.surface == 0);
                var hc = Shared.Pos(hs.center.x, 0, hs.center.y);
                var start = hc + new Vector3(1.6f, 0, 0.4f);
                g.player.Teleport(start);
                var d = hc - start;
                g.player.SetView(Mathf.Atan2(-d.x, d.z), -0.3f);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
                float maxY = 0;
                for (float t = 0; t < 0.9f; t += Time.deltaTime) { maxY = Mathf.Max(maxY, g.player.transform.position.y); yield return null; }
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                yield return Frames(3);
                var pp = g.player.transform.position;
                float under = g.debris.At(pp);
                bool climbed = maxY > 0.05f;
                // a steep heap face blocks like furniture; either way the feet are never inside the parts
                g.player.Teleport(hc);
                yield return new WaitForSeconds(1.0f);
                var top = g.player.transform.position;
                Check("player walks over parts, not through them", pp.y >= under - 0.05f && top.y >= g.debris.At(top) - 0.05f && top.y > 0.05f,
                      $"{(climbed ? $"climbed to {maxY:0.00} m" : "blocked by a steep face")}; on the heap: feet {top.y:0.00} m over parts {g.debris.At(top):0.00} m");
                yield return g.Capture(Path.Combine(outDir, "00b_standing_on_parts.png"), 2);
            }
            g.player.enabled = false;

            // 1. pick individual items from a heap by aiming at them
            var heap = g.layout.piles.First(p => p.kind == "heap" && p.surface == 0);   // procedural: first floor heap
            var pile = ItemsNear(new Vector3(heap.center.x, 0, heap.center.y), 1.2f);
            int visibleBefore = g.renderer.VisibleCount, picked = 0, exact = 0;
            foreach (var id in pile.Take(40))
            {
                if (w.instances[id].state != ItemState.InWorld) continue;
                LookAtItem(id);
                yield return Frames(2);
                ic.UpdateTarget();
                if (ic.targetType != "item") continue;
                int tid = ic.targetItem;
                if (tid == id) exact++;
                if (ic.Pickup(tid, true)) picked++;
                yield return Frames(1);
                if (picked >= 12) break;
            }
            Check("picked individual items from a heap", picked >= 10, $"{picked} picked ({exact} aimed exactly at the intended item)");
            yield return Frames(15);
            Check("pile shrinks one item per pickup", g.renderer.VisibleCount == visibleBefore - picked, $"visible {visibleBefore} -> {g.renderer.VisibleCount}");
            Check("inventory holds every picked item", g.inventory.Count == picked && w.CountInState(ItemState.Carried) == picked, $"inventory {g.inventory.Count}, carried {w.CountInState(ItemState.Carried)}");
            yield return g.Capture(Path.Combine(outDir, "01_after_pickups.png"), 5);
            g.hud.SetHudVisible(true);
            var aim = ItemsNear(new Vector3(-2.2f, 0, 1.6f), 1.0f);
            if (aim.Count > 0) { LookAtItem(aim[0]); yield return Frames(3); ic.UpdateTarget(); yield return g.Capture(Path.Combine(outDir, "01b_hud_target.png"), 3); }
            g.SetState("inventory");
            yield return g.Capture(Path.Combine(outDir, "01c_inventory_screen.png"), 5);
            g.SetState("play");
            g.player.enabled = false;
            g.hud.SetHudVisible(false);

            // 2. inspection of a world item
            var near = ItemsNear(new Vector3(3.6f, 0, -2.9f), 1.0f);
            if (near.Count > 0)
            {
                LookAtItem(near[0]);
                yield return Frames(2);
                ic.UpdateTarget();
                ic.BeginInspect();
                yield return Frames(25);
                g.hud.SetHudVisible(true);
                yield return g.Capture(Path.Combine(outDir, "02_inspection.png"), 3);
                Check("inspection hides the world item, no duplicate", ic.inspecting && g.renderer.IsHidden(ic.inspectWorldId), "");
                ic.EndInspect();
                g.hud.SetHudVisible(false);
            }

            // 3. wrong destination refused
            int held = g.inventory.HeldId();
            var heldDef = w.DefOf(held);
            var wrong = g.db.boxes.Keys.First(b => b != heldDef.storageBox && g.db.IsOpenKind(b));
            yield return AimAtDest(wrong);
            ic.UpdateTarget();
            int before = g.inventory.Count;
            ic.Primary();
            Check("wrong destination refused, nothing lost", ic.targetDest == wrong && g.inventory.Count == before && w.instances[held].state == ItemState.Carried, "target " + (ic.targetDest ?? ic.targetType));

            // 4. store everything by aiming at the right destinations
            int stored = 0, viaAim = 0, shots = 0;
            while (g.inventory.Count > 0)
            {
                int id = g.inventory.HeldId();
                var dest = w.DefOf(id).storageBox;
                if (!g.db.boxes.ContainsKey(dest)) { ic.DepositStation(id); stored++; continue; }
                yield return AimAtDest(dest);
                ic.UpdateTarget();
                if (ic.targetDest == dest) { viaAim++; ic.Primary(); }
                else ic.Deposit(dest, id);
                yield return Frames(12);
                if (w.instances[id].state == ItemState.Stored) stored++;
                if (shots < 2)
                {
                    g.hud.SetHudVisible(true);
                    yield return g.Capture(Path.Combine(outDir, $"03_store_{shots}.png"), 4);
                    g.hud.SetHudVisible(false);
                    shots++;
                }
            }
            Check("all carried items stored in their own destinations", stored == picked && g.storage.Stored(heldDef.storageBox).Contains(held), $"{stored} stored, {viaAim} via crosshair aim");
            Check("progress counts sorted items", g.progress.sortedTotal == stored, $"{g.progress.sortedTotal} sorted");

            // 5. save / load round trip
            var snap = Snapshot();
            g.SaveGame("autotest");
            g.StartWorld(g.world.seed, null);
            Check("new world is reset", g.progress.sortedTotal == 0, "");
            g.LoadGame("autotest");
            g.player.enabled = false;
            yield return Frames(5);
            var after = Snapshot();
            Check("save/load restores identical state", snap == after, $"{snap.GetHashCode()} vs {after.GetHashCode()}");
            File.Delete(SaveSystem.PathOf("autotest"));

            // 6. Retrobot: assembly from 5 parts, activation, autonomous sorting
            yield return RetrobotChecks();
            // 7. the other four robots, all working together
            yield return AllRobotsChecks();
            // 8. player tools from skills
            yield return ToolChecks();
            // 9. robots survive a save/load round trip (zone, battery, record, carried parts)
            {
                var cat = g.robots.First(r => r.id == "caterpillarbot");
                cat.AssignZone(g.player.transform.position);
                g.robots.First(r => r.id == "unibot").SetBattery(0.5f);
                string State() => string.Join(";", g.robots.Select(r => $"{r.id}:{r.Active}:{r.Battery:0.00}:{r.delivered}:{r.staged}:{r.zoneCenter?.ToString("0.0") ?? "-"}:{r.carried.Count}"));
                foreach (var r in g.robots) r.enabled = false;   // freeze so the state can't move between save and compare
                var robotsBefore = State();
                g.SaveGame("autotest2");
                g.StartWorld(g.world.seed, null);
                g.LoadGame("autotest2");
                foreach (var r in g.robots) r.enabled = false;
                yield return Frames(3);
                var robotsAfter = State();
                Check("robots survive save/load (active, battery, record, zone, carried)", robotsBefore == robotsAfter, robotsAfter == robotsBefore ? robotsBefore.Substring(0, Mathf.Min(80, robotsBefore.Length)) + "…" : $"{robotsBefore}  vs  {robotsAfter}");
                foreach (var r in g.robots) r.enabled = true;
                File.Delete(SaveSystem.PathOf("autotest2"));
            }

            g.PlaceCamera("0.6,1.65,3.6,0,-18");
            g.hud.SetHudVisible(true);
            yield return g.Capture(Path.Combine(outDir, "04_hud_overview.png"), 10);
            File.WriteAllText(Path.Combine(outDir, "autoplay_report.json"), Json.Write(new Dictionary<string, object> { ["checks"] = checks, ["passed"] = (double)passed, ["failed"] = (double)failed }));
            Debug.Log($"[autoplay] {passed} passed, {failed} failed");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        /// <summary>Frames Retrobot from the front-side, trying other angles until the
        /// camera has a clear line of sight and is not inside furniture or walls.</summary>
        void LookAtRobot(float dist, float height, float side) => LookAtRobot(g.retrobot, dist, height, side);

        void LookAtRobot(Robot robot, float dist, float height, float side)
        {
            var r = robot.transform;
            var at = r.position + Vector3.up * 0.15f;
            float a0 = Mathf.Atan2(side, dist) * Mathf.Rad2Deg;
            float d = new Vector2(side, dist).magnitude;
            var lo = g.layout.roomMin; var hi = g.layout.roomMax;
            foreach (var da in new[] { 0f, 25f, -25f, 50f, -50f, 80f, -80f, 120f, -120f, 180f })
            {
                foreach (var dd in new[] { d, d * 0.75f, d * 0.55f })
                {
                    var dir = Quaternion.AngleAxis(a0 + da, Vector3.up) * r.forward;
                    var eye = at + dir * dd + Vector3.up * (height - 0.15f);
                    bool inside = eye.x > lo.x + 0.3f && eye.x < hi.x - 0.3f && -eye.z > lo.z + 0.3f && -eye.z < hi.z - 0.3f;
                    if (!inside || Physics.Linecast(eye, at + Vector3.up * 0.2f, 1 << Layers.Env) || Physics.CheckSphere(eye, 0.25f, 1 << Layers.Env)) continue;
                    LookFrom(eye, at);
                    return;
                }
            }
            LookFrom(at + r.forward * dist + Vector3.up * (height - 0.15f), at);
        }

        IEnumerator AllRobotsChecks()
        {
            var others = g.robots.Where(r => r.id != "retrobot").ToList();
            Check("five robots at toy scale", g.robots.Count == 5 && g.robots.All(r => r.Height <= 0.31f), string.Join(", ", g.robots.Select(r => $"{r.displayName} {r.Height:0.00} m")));
            // half-built: ghosts show what is missing
            foreach (var r in others)
            {
                var ids = g.db.RobotPartIds(r.id).SelectMany(d => g.world.IdsOf(d)).ToList();
                foreach (var id in ids.Take(ids.Count / 2)) Install(id);
            }
            g.repairBay.Refresh();
            var bay = g.repairBay.transform;
            var mid = (g.robots[0].home + g.robots[4].home) * 0.5f;
            var front = Vector3.ProjectOnPlane(bay.TransformDirection(Batch.G(0, 0, 1)), Vector3.up).normalized;
            LookFrom(mid + front * 1.6f + Vector3.up * 0.9f, mid + Vector3.up * 0.12f);
            yield return g.Capture(Path.Combine(outDir, "15_robots_half_built.png"), 3);
            foreach (var r in others)
                foreach (var id in g.db.RobotPartIds(r.id).SelectMany(d => g.world.IdsOf(d))) Install(id);
            g.repairBay.Refresh(true);
            float t0 = Time.realtimeSinceStartup;
            while (others.Any(r => !r.Active) && Time.realtimeSinceStartup - t0 < 15) yield return null;
            Check("all robots activate once their parts are installed", others.All(r => r.Active), string.Join(", ", others.Select(r => r.Describe())));
            // size line-up: everyone at the repair bay, seen from eye height
            var saved = g.robots.ToDictionary(r => r, r => (r.transform.position, r.transform.rotation));
            foreach (var r in g.robots) { r.enabled = false; r.transform.SetPositionAndRotation(r.home + Vector3.up * (r.id == "hoverbot" ? 0.25f : 0), r.homeRot); }
            LookFrom(mid + front * 1.5f + Vector3.up * (PlayerController.Eye - 0.0f), mid + Vector3.up * 0.1f);
            yield return g.Capture(Path.Combine(outDir, "16_robots_lineup_eye_level.png"), 3);
            LookFrom(mid + front * 0.85f + Vector3.up * 0.35f, mid + Vector3.up * 0.14f);
            yield return g.Capture(Path.Combine(outDir, "17_robots_lineup_close.png"), 3);
            foreach (var r in g.robots) { r.transform.SetPositionAndRotation(saved[r].Item1, saved[r].Item2); r.enabled = true; }

            var before = g.robots.ToDictionary(r => r.id, r => (r.delivered, r.staged));
            Time.timeScale = 4;
            bool[] shot = new bool[5];
            bool doubleHeld = false;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 90 && g.robots.Any(r => r.delivered + r.staged - before[r.id].delivered - before[r.id].staged < 2))
            {
                for (int i = 0; i < 5; i++)
                {
                    var r = g.robots[i];
                    if (!shot[i] && r.carried.Count > 0 && r.Speed > r.WalkSpeed * 0.6f)
                    {
                        shot[i] = true;
                        Time.timeScale = 1;
                        LookAtRobot(r, r.id == "hoverbot" ? 1.4f : 0.8f, r.id == "hoverbot" ? 0.5f : 0.45f, 0.45f);
                        yield return g.Capture(Path.Combine(outDir, $"18_{r.id}_working.png"), 1);
                        Time.timeScale = 4;
                    }
                }
                var held = g.robots.SelectMany(r => r.carried).ToList();
                if (held.Count != held.Distinct().Count()) doubleHeld = true;
                yield return null;
            }
            Time.timeScale = 1;
            foreach (var r in others)
                Check($"{r.displayName} gathers and delivers on its own", r.delivered + r.staged > before[r.id].delivered + before[r.id].staged,
                      $"{r.delivered - before[r.id].delivered} stored, {r.staged - before[r.id].staged} piled, {r.Describe()}");
            var hover = g.robots.First(r => r.id == "hoverbot");
            Check("no part is ever held by two robots", !doubleHeld && g.world.instances.Where(i => i.state == ItemState.Carried && i.owner.StartsWith("robot:")).All(i => g.robots.Count(r => r.carried.Contains(i.id)) == 1), "");
            Check("Hoverbot stores into destinations the small robots can't reach", hover.delivered > before["hoverbot"].delivered, $"{hover.delivered - before["hoverbot"].delivered} stored by Hoverbot");
            // battery: a robot running low finishes its load, goes home, charges and resumes
            var sp = g.robots.First(r => r.id == "spiderbot");
            sp.SetBattery(0.05f);
            Time.timeScale = 4;
            float tc = Time.realtimeSinceStartup;
            bool charged = false, wentHome = false;
            while (Time.realtimeSinceStartup - tc < 60 && !charged)
            {
                if (sp.StepName == "Charging") wentHome = (new Vector2(sp.transform.position.x - sp.home.x, sp.transform.position.z - sp.home.z)).magnitude < 0.4f;
                if (wentHome && sp.StepName != "Charging" && sp.Battery >= 0.99f) charged = true;
                yield return null;
            }
            Time.timeScale = 1;
            Check("a robot low on battery goes home, charges and goes back to work", charged, sp.Describe());
            g.SetState("paused");
            g.hud.OpenScreen("robots");
            yield return g.Capture(Path.Combine(outDir, "20_robots_screen.png"), 5);
            g.hud.BackFromSubScreen();
            g.SetState("play");
        }

        IEnumerator ToolChecks()
        {
            var pr = g.progression;
            pr.level = pr.maxLevel;
            pr.skillPoints = 99;
            foreach (var s in pr.skills.Where(s => s.Active).OrderBy(s => s.unlockLevel)) g.UnlockSkill(s.id);
            Check("tool skills unlock (levels, points, prerequisites)", pr.Has("bulk_pickup_count") && pr.Has("magnet_range_m") && pr.Has("scan_radius_m") && pr.Has("supermagnet"),
                  $"{pr.unlocked.Count} skills, inventory capacity {g.inventory.capacity}");
            Check("Inventory Manifest reveals the totals of discovered parts", pr.Has("reveal_totals") && g.progress.discovered.All(g.progress.revealed.Contains),
                  $"{g.progress.revealed.Count} of {g.progress.discovered.Count} discovered parts show their total");
            foreach (var r in g.robots) r.enabled = false;   // keep robots out of the way while measuring
            bool Free(ItemInstance i) => i.state == ItemState.InWorld && !g.robotReserved.Contains(i.id) && !g.world.DefOf(i.id).IsRobotPart && i.Position.y < 0.6f;

            // Handful: a part with a same-kind neighbour right next to it
            var withPair = g.world.instances.Where(Free).FirstOrDefault(i => g.index.QuerySphere(i.Position, 0.2f).Any(o => o != i.id && g.world.instances[o].defId == i.defId && Free(g.world.instances[o])));
            if (withPair != null)
            {
                g.player.Teleport(withPair.Position + new Vector3(0.8f, -withPair.Position.y, 0.8f));
                int before = g.inventory.Count;
                g.interaction.Pickup(withPair.id, true);
                yield return Frames(20);
                Check("Handful also picks up nearby copies of the same part", g.inventory.Count - before >= 2 && g.inventory.items.Skip(before).All(i => g.world.instances[i].defId == withPair.defId),
                      $"{g.inventory.Count - before} × {g.world.DefOf(withPair.id).displayName}");
            }
            else Check("Handful also picks up nearby copies of the same part", false, "no pair found");

            // Scanner: counts unsorted copies of the held part in range
            g.inventory.Select(withPair?.defId ?? -1);
            int found = g.tools.Scan();
            var me = g.player.transform.position;
            int expect = g.world.IdsOf(g.inventory.heldDef).Count(i => g.world.instances[i].state == ItemState.InWorld && (g.world.instances[i].Position - me).magnitude <= (float)pr.Effect("scan_radius_m"));
            Check("Scanner finds the unsorted copies of the held part", found == expect, $"{found} found, {expect} expected");
            yield return g.Capture(Path.Combine(outDir, "19_scanner.png"), 4);

            // Supermagnet: exact-match pull of the held part's definition
            var group = g.world.instances.Where(Free).GroupBy(i => i.defId).Select(gr => gr.ToList())
                .Select(l => (l, c: l.First())).FirstOrDefault(x => x.l.Count(o => (o.Position - x.c.Position).magnitude < 3f) >= 3);
            if (group.c != null)
            {
                var c = group.c;
                g.player.Teleport(c.Position + new Vector3(0.7f, -c.Position.y, 0.4f));
                g.interaction.Pickup(c.id, true);
                yield return Frames(5);
                g.inventory.Select(c.defId);
                int before = g.inventory.Count;
                int pulled = g.tools.Supermagnet();
                yield return new WaitForSeconds(1.5f);
                var got = g.inventory.items.Skip(before).ToList();
                Check("Supermagnet pulls only exact matches, unsorted, within range", pulled > 0 && got.Count == pulled && got.All(i => g.world.instances[i].defId == c.defId),
                      $"{pulled} × {g.world.DefOf(c.id).displayName}");
            }
            else Check("Supermagnet pulls only exact matches, unsorted, within range", false, "no group found");

            // Magnetic glove (empty hand: no filters): only magnetic parts come
            var mag = g.world.instances.Where(Free).FirstOrDefault(i => g.world.DefOf(i.id).magnetic && i.Position.y < 0.3f);
            if (mag != null)
            {
                g.player.Teleport(mag.Position + new Vector3(0, -mag.Position.y, -0.5f));
                g.player.SetView(0, -0.6f);
                g.inventory.heldDef = -1;
                int before = g.inventory.Count;
                for (float t = 0; t < 1.5f; t += Time.deltaTime) { g.tools.MagnetTick(Time.deltaTime); yield return null; }
                var got = g.inventory.items.Skip(before).ToList();
                Check("Magnetic glove pulls magnetic parts only", got.Count > 0 && got.All(i => g.world.DefOf(i).magnetic), $"{got.Count} parts pulled");
            }
            else Check("Magnetic glove pulls magnetic parts only", false, "no magnetic part found");
            foreach (var r in g.robots) r.enabled = true;

            // Area Sweep: everything loose around you, up to the limit
            var heap = g.layout.piles.Where(p => p.kind == "heap" && p.surface == 0).Skip(1).First();
            var hc = Shared.Pos(heap.center.x, 0, heap.center.y);
            g.player.Teleport(hc + new Vector3(1.0f, 0, 0));
            g.player.SetView(Mathf.Atan2(1.0f, 0), -0.7f);
            yield return Frames(3);
            int b0 = g.inventory.Count;
            int swept = g.tools.AreaSweep();
            yield return new WaitForSeconds(2.5f);
            Check("Area Sweep collects the loose parts around you", swept > 0 && g.inventory.Count - b0 == swept, $"{swept} parts swept, inventory +{g.inventory.Count - b0} (capacity {g.inventory.capacity}, now {g.inventory.Count})");

            // Automatic Storage Delivery: a robot fetches the player's load and stores it
            var handed = g.inventory.items.ToList();
            string hm = g.RobotHandover();
            Time.timeScale = 4;
            float t1 = Time.realtimeSinceStartup;
            bool Delivered(int i) => g.world.instances[i].state == ItemState.Stored || g.world.instances[i].state == ItemState.InWorld;   // stored, or piled below a high destination
            float logT = 0;
            while (Time.realtimeSinceStartup - t1 < 60 && !(g.inventory.Count < handed.Count && handed.Any(Delivered)))
            {
                if ((logT -= Time.unscaledDeltaTime) <= 0)
                {
                    logT = 3;
                    var extra = g.inventory.items.Except(handed).Select(i => $"{i}:{g.world.instances[i].owner}").Take(5);
                    Debug.Log($"[autoplay] handover wait: inventory {g.inventory.Count} (handed {handed.Count}), new [{string.Join(",", extra)}], " + string.Join(" | ", g.robots.Select(r => r.Describe())));
                }
                yield return null;
            }
            Time.timeScale = 1;
            int storedByRobot = handed.Count(i => g.world.instances[i].state == ItemState.Stored);
            Check("Automatic Storage Delivery: a robot takes your parts and delivers them", g.inventory.Count < handed.Count && handed.Any(Delivered),
                  $"{hm}; {handed.Count - g.inventory.Count} taken, {storedByRobot} stored, {handed.Count(i => g.world.instances[i].state == ItemState.InWorld)} piled so far");

            // Task Coordination: a robot works only inside its zone
            var zr = g.robots.First(r => r.id == "spiderbot");
            int oldTarget = zr.targetId;   // a part it was already picking up doesn't count
            zr.AssignZone(hc);
            Time.timeScale = 4;
            t1 = Time.realtimeSinceStartup;
            int firstTarget = -1;
            while (Time.realtimeSinceStartup - t1 < 30 && firstTarget < 0) { if (zr.targetId >= 0 && zr.targetId != oldTarget) firstTarget = zr.targetId; yield return null; }
            Time.timeScale = 1;
            float dz = firstTarget >= 0 ? new Vector2(g.world.instances[firstTarget].Position.x - hc.x, g.world.instances[firstTarget].Position.z - hc.z).magnitude : -1;
            Check("Task Coordination: an assigned robot works only in its zone", firstTarget >= 0 && dz <= zr.zoneRadius, $"target {dz:0.0} m from the zone centre (radius {zr.zoneRadius})");
            zr.AssignZone(null);
        }

        void Install(int id)
        {
            if (g.world.instances[id].state == ItemState.Stored) return;
            if (g.world.instances[id].state == ItemState.InWorld) { g.index.Remove(id); g.world.SetState(id, ItemState.Carried, "player"); }
            g.storage.Deposit(g.world.DefOf(id).storageBox, id);
        }

        IEnumerator RetrobotChecks()
        {
            var rb = g.retrobot;
            var parts = g.db.RobotPartIds("retrobot").SelectMany(d => g.world.IdsOf(d)).ToList();
            Check("Retrobot has 5 assembly parts", parts.Count == 5, $"{parts.Count} parts");
            Check("Retrobot inactive before assembly", !rb.Active, rb.Describe());
            for (int k = 0; k < parts.Count; k++)
            {
                int id = parts[k];
                if (g.world.instances[id].state == ItemState.InWorld)
                {
                    g.index.Remove(id);
                    g.world.SetState(id, ItemState.Carried, "player");
                    g.inventory.Add(id);
                }
                g.interaction.DepositStation(id);
                yield return Frames(2);
                if (k == 2)
                {
                    Check("Retrobot stays inactive while parts are missing", !rb.Active, rb.Describe());
                    LookAtRobot(0.75f, 0.5f, 0.25f);
                    yield return g.Capture(Path.Combine(outDir, "05_retrobot_partial.png"), 4);
                }
            }
            float t0 = Time.realtimeSinceStartup;
            while (!rb.Active && Time.realtimeSinceStartup - t0 < 10) { LookAtRobot(0.75f, 0.5f, 0.2f); yield return null; }
            Check("Retrobot activates when all 5 parts are installed", rb.Active, rb.Describe());
            yield return new WaitForSeconds(1.2f);
            Check("objectives advance with play (first part, sorting, Retrobot)", g.objectives.done.Contains("first_part") && g.objectives.done.Contains("sort_10") && g.objectives.done.Contains("retrobot_parts"),
                  $"done: {string.Join(", ", g.objectives.done)}; now: {g.objectives.HudText()}");
            LookAtRobot(0.7f, 0.45f, 0.15f);
            yield return g.Capture(Path.Combine(outDir, "06_retrobot_online.png"), 3);

            // let him work on his own (sped up), photographing a pick-up and a carry
            int sortedBefore = g.progress.sortedTotal, robotBefore = g.telemetry.sortedRobot;
            Time.timeScale = 3;
            bool shotPick = false, shotCarry = false, shotWalk = false;
            t0 = Time.realtimeSinceStartup;
            Time.timeScale = 4;
            while ((rb.delivered < 2 || rb.delivered + rb.staged < 4) && Time.realtimeSinceStartup - t0 < 180)
            {
                if (!shotWalk && rb.StepName == "ToItem" && rb.carried.Count == 0) { shotWalk = true; Time.timeScale = 1; LookAtRobot(0.9f, 0.5f, 0.6f); yield return g.Capture(Path.Combine(outDir, "07_retrobot_walking.png"), 1); Time.timeScale = 3; }
                if (!shotPick && rb.StepName == "Picking") { shotPick = true; Time.timeScale = 1; yield return new WaitForSeconds(0.35f); LookAtRobot(0.7f, 0.4f, 0.5f); yield return g.Capture(Path.Combine(outDir, "08_retrobot_bending.png"), 1); Time.timeScale = 3; }
                if (!shotCarry && rb.StepName == "ToDest" && rb.carried.Count > 0) { shotCarry = true; Time.timeScale = 1; yield return new WaitForSeconds(0.4f); LookAtRobot(0.8f, 0.45f, 0.45f); yield return g.Capture(Path.Combine(outDir, "09_retrobot_carrying.png"), 1); Time.timeScale = 3; }
                yield return null;
            }
            Time.timeScale = 1;
            int sortedNow = g.progress.sortedTotal - sortedBefore;
            Check("Retrobot sorts parts on his own, one at a time", rb.delivered >= 2 && sortedNow == rb.delivered, $"{rb.delivered} stored, {rb.staged} piled below high destinations, {sortedNow} newly sorted");
            Check("parts for destinations above his reach are piled in front of them, still unsorted",
                  rb.staged == 0 || rb.IsPiledBelowDestination(rb.lastStagedId), rb.staged == 0 ? "none needed yet" : $"part {rb.lastStagedId} waits below {g.world.DefOf(rb.lastStagedId).storageBox}");
            Check("robot sorting is recorded as robot-assisted", g.telemetry.sortedRobot - robotBefore == rb.delivered, $"{g.telemetry.sortedRobot - robotBefore} robot sorts");

            // size reference: seen from the player's eye height (1.65 m) while he walks
            var sizeShots = new (string name, float dist, float side)[]
            {
                ("10_size_eye_level_2m", 1.9f, 0.6f), ("11_size_eye_level_4m", 3.6f, 1.2f),
                ("12_walking_profile", 0.4f, 2.4f), ("13_walking_from_behind", -2.6f, 0.8f), ("14_size_at_your_feet", 0.9f, 0.3f),
            };
            foreach (var (name, dist, side) in sizeShots)
            {
                t0 = Time.realtimeSinceStartup;
                while (rb.Speed < rb.WalkSpeed * 0.8f && Time.realtimeSinceStartup - t0 < 30) yield return null;
                yield return new WaitForSeconds(0.25f);
                LookAtRobot(dist, PlayerController.Eye, side);
                yield return g.Capture(Path.Combine(outDir, name + ".png"), 1);
                yield return new WaitForSeconds(1.2f);
            }
        }

        void Check(string name, bool ok, string detail)
        {
            checks.Add(new Dictionary<string, object> { ["name"] = name, ["ok"] = ok, ["detail"] = detail });
            if (ok) passed++; else failed++;
            Debug.Log($"[autoplay] {(ok ? "PASS" : "FAIL")} {name}  {detail}");
        }

        List<int> ItemsNear(Vector3 godotCenter, float r)
        {
            var c = Shared.Pos(godotCenter.x, godotCenter.y, godotCenter.z);
            return g.world.instances.Where(i => i.state == ItemState.InWorld && new Vector2(i.Position.x - c.x, i.Position.z - c.z).magnitude < r)
                .OrderByDescending(i => i.Position.y).Select(i => i.id).ToList();
        }

        void LookAtItem(int id)
        {
            var p = g.world.instances[id].Position;
            var from = g.player.transform.position + Vector3.up * PlayerController.Eye;
            var flat = new Vector3(from.x - p.x, 0, from.z - p.z);
            if (flat.magnitude < 0.1f) flat = Vector3.back;
            LookFrom(p + flat.normalized * 0.9f + Vector3.up * 0.9f, p);
        }

        void LookFrom(Vector3 eye, Vector3 at)
        {
            var d = (at - eye).normalized;
            // Unity direction -> Godot yaw/pitch (mirror Z)
            var gd = new Vector3(d.x, d.y, -d.z);
            float yaw = Mathf.Atan2(-gd.x, -gd.z);
            float pitch = Mathf.Asin(Mathf.Clamp(gd.y, -1, 1));
            g.player.Teleport(eye - Vector3.up * PlayerController.Eye);
            g.player.SetView(yaw, pitch);
            g.player.head.localPosition = new Vector3(0, PlayerController.Eye, 0);
        }

        IEnumerator AimAtDest(string dest)
        {
            var d = g.db.boxes[dest];
            var u = g.units[Json.Str(d, "unit")];
            Vector3 target, front;
            if (u.type == "drawer_cabinet")
            {
                var m = u.FrontWorld(u.SlotOf(dest));
                target = m.GetColumn(3);
                front = -u.transform.forward;
            }
            else
            {
                var m = u.DestWorld(dest);
                var inner = StorageUnit.Inner(d);
                target = (Vector3)m.GetColumn(3) + Vector3.up * Mathf.Min(inner.y, 0.25f) * 0.5f;
                front = -u.transform.forward;
            }
            var eye = target + front.normalized * 1.0f;
            eye.y = Mathf.Clamp(target.y + 0.35f, 0.9f, 1.75f);
            LookFrom(eye, target);
            yield return Frames(3);
        }

        string Snapshot()
        {
            var parts = g.world.instances.Where(i => i.state != ItemState.InWorld || i.moved)
                .Select(i => $"{i.id}:{(int)i.state}:{i.owner}:{i.Position.x:0.000},{i.Position.y:0.000},{i.Position.z:0.000}");
            return string.Join("|", parts) + $"#{g.progress.sortedTotal}#{string.Join(",", g.inventory.items)}#{g.progress.discovered.Count}";
        }
    }
}
