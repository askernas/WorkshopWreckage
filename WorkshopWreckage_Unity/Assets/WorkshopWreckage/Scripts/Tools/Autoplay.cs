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
            GameEvents.Raised += e => _events.Add(e);   // every gameplay event of the run (roadmap 3A checks)
            var ic = g.interaction;
            var w = g.world;
            g.SetState("menu");
            yield return g.Capture(Path.Combine(outDir, "00_main_menu.png"), 15);
            g.SetState("play");
            yield return Frames(10);
            if (g.args.TryGetValue("apsection", out var section) && section == "arm")
            {
                yield return ArmChecks();   // QA: the Sorting Arm acceptance section alone
                Finish();
                yield break;
            }
            if (section == "tutorial")
            {
                yield return TutorialChecks();   // QA: the one-time robot-part tutorial
                Finish();
                yield break;
            }
            if (section == "start")
            {
                yield return StartAreaChecks();   // QA: starter assortment, Retrobot chassis, mixing, determinism
                Finish();
                yield break;
            }
            if (section == "dest")
            {
                yield return DestinationChecks();   // QA: exact destination counts and destination levels
                Finish();
                yield break;
            }
            if (section == "pile")
            {
                yield return PileChecks();   // QA: dropped parts pile up
                Finish();
                yield break;
            }

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
                yield return new WaitForSeconds(0.5f);   // stepping up eases over a few frames
                var pp = g.player.transform.position;
                float under = g.debris.At(pp);
                bool climbed = maxY > 0.05f;
                // a steep heap face blocks like furniture; either way the feet are never inside the parts
                g.player.Teleport(hc);
                yield return new WaitForSeconds(1.0f);
                var top = g.player.transform.position;
                Check("player walks over parts, not through them", pp.y >= under - 0.05f && top.y >= g.debris.At(top) - 0.05f && top.y > 0.05f,
                      $"{(climbed ? $"climbed to {maxY:0.00} m" : "blocked by a steep face")}, feet {pp.y:0.00} m over parts {under:0.00} m; on the heap: feet {top.y:0.00} m over parts {g.debris.At(top):0.00} m");
                yield return g.Capture(Path.Combine(outDir, "00b_standing_on_parts.png"), 2);
            }
            g.player.enabled = false;

            yield return PlaytestChecks();

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
            Check("you start with 10 pockets and no automatic stacking (every part its own slot)", g.inventory.capacity == 10 && !g.inventory.stacking && g.inventory.Stacks().Count == g.inventory.Count && picked == 10,
                  $"capacity {g.inventory.capacity}, {g.inventory.Stacks().Count} slots for {g.inventory.Count} parts");
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
            // a wrong destination the crosshair can actually reach (stray parts may lie in front of some)
            string wrong = null;
            foreach (var cand in g.db.boxes.Keys.Where(b => b != heldDef.storageBox && g.db.IsOpenKind(b)).Take(25))
            {
                yield return AimAtDest(cand);
                ic.UpdateTarget();
                if (ic.targetType == "dest" && ic.targetDest == cand) { wrong = cand; break; }
            }
            int before = g.inventory.Count;
            int evWrong = _events.Count;
            ic.Primary();
            Check("a refused deposit raises no gameplay event", _events.Count == evWrong, $"{_events.Count - evWrong} events");
            Check("wrong destination refused, nothing lost", ic.targetDest == wrong && g.inventory.Count == before && w.instances[held].state == ItemState.Carried, "target " + (ic.targetDest ?? ic.targetType));

            // 4. store everything by aiming at the right destinations
            int stored = 0, viaAim = 0, shots = 0;
            int evStore = _events.Count;
            long sortedStat0 = g.stats.Get("PartSorted");
            var tries = new Dictionary<int, int>();
            int cleared = 0, gaveUp = 0;
            while (g.inventory.Count > 0)
            {
                int id = g.inventory.HeldId();
                if (id < 0) { g.inventory.SelectSlot(0); id = g.inventory.HeldId(); }
                var dest = w.DefOf(id).storageBox;
                if (!g.db.boxes.ContainsKey(dest)) { ic.DepositStation(id); stored++; continue; }
                // like a player: stray parts lying in the destination are moved out of the way first
                cleared += ClearStrays(dest);
                // never loop forever on one part
                tries[id] = tries.GetValueOrDefault(id) + 1;
                if (tries[id] > 3) { gaveUp++; g.inventory.Remove(id); g.world.SetState(id, ItemState.InWorld, "world"); g.index.Insert(id); continue; }
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
            Check("all carried items stored in their own destinations", stored == picked && gaveUp == 0 && g.storage.Stored(heldDef.storageBox).Contains(held), $"{stored} stored, {viaAim} via crosshair aim, {cleared} strays moved out of the way, {gaveUp} given up");
            Check("progress counts sorted items", g.progress.sortedTotal == stored, $"{g.progress.sortedTotal} sorted");
            var sortedEv = _events.Skip(evStore).Where(e => e.kind == GameEventKind.PartSorted).ToList();
            Check("every accepted deposit raises exactly one PartSorted event, with its part, category and destination",
                  sortedEv.Count == stored && sortedEv.All(e => e.actor == "player" && e.partDef >= 0 && e.category != "" && e.destination != "") && sortedEv.Select(e => e.opId).Distinct().Count() == sortedEv.Count
                  && g.stats.Get("PartSorted") - sortedStat0 == stored,
                  $"{sortedEv.Count} events for {stored} stored; counter +{g.stats.Get("PartSorted") - sortedStat0}");

            // 5. save / load round trip (a save first lets falling parts come to rest)
            g.piles.Finish();
            var snap = Snapshot();
            var statsBefore = Json.Write(g.stats.ToDict());
            string lvBefore = $"level {g.progression.level} xp {g.progression.xp}";
            g.SaveGame("autotest");
            g.StartWorld(g.world.seed, null);
            Check("new world is reset", g.progress.sortedTotal == 0, "");
            int evLoad = _events.Count, lvSaved = g.progression.level;
            g.LoadGame("autotest");
            bool statsSame = Json.Write(g.stats.ToDict()) == statsBefore;   // straight after loading, before any frame
            g.player.enabled = false;
            yield return Frames(5);
            var after = Snapshot();
            // loading itself raises nothing; the frames after it may legitimately finish pending objectives
            // (a first level-up), but never repeat a level, a sort or an activation from before the save
            bool repeats = _events.Skip(evLoad).Any(e => e.kind is GameEventKind.PartSorted or GameEventKind.RobotActivated or GameEventKind.PartStaged || (e.kind == GameEventKind.LevelReached && e.count <= lvSaved));
            Check("gameplay counters survive save/load and loading repeats no event", statsSame && !repeats,
                  $"stats identical after load {statsSame}; after it: {string.Join(", ", _events.Skip(evLoad).Select(e => e.kind + (e.kind == GameEventKind.LevelReached ? " " + e.count : "")))} ({lvBefore} -> level {g.progression.level} xp {g.progression.xp})");
            Check("save/load restores identical state", snap == after, snap == after ? $"{snap.GetHashCode()} vs {after.GetHashCode()}"
                  : "differs: " + string.Join(" ", snap.Split('|').Except(after.Split('|')).Take(3)) + " -> " + string.Join(" ", after.Split('|').Except(snap.Split('|')).Take(3)));
            File.Delete(SaveSystem.PathOf("autotest"));

            // 6. Retrobot: assembly from 5 parts, activation, autonomous sorting
            yield return RetrobotChecks();
            // 7. the other four robots, all working together
            yield return AllRobotsChecks();
            // 7b. the Sorting Arm: sockets, assembly, carry, mount, local sorting, save/load
            yield return ArmChecks();
            // 7c. no part can end up out of reach (drops, integrity check, recover action)
            yield return ReachChecks();
            yield return PileChecks();
            // 7d. a save survives a change of the item catalog (P1-4)
            yield return MigrationChecks();
            yield return DestinationChecks();
            {
                // partial set credit: half a set pays its share of the completion XP, the rest comes at the end
                var pr = FreshProgression();
                long x0 = pr.xp;
                pr.OnDefinitionHalf(-424242, 6);
                long half = pr.xp - x0;
                pr.OnDefinitionHalf(-424242, 6);   // once only
                long again = pr.xp - x0 - half;
                pr.OnDefinitionCompleted(-424242, 6);
                long whole = pr.xp - x0;
                long x1 = pr.xp;
                pr.OnDefinitionCompleted(-424243, 6);   // a set finished without the half step
                long direct = pr.xp - x1;
                Check("partial set credit: half a set pays half its XP once, completion the rest (same total)", half > 0 && again == 0 && Mathf.Abs(whole - direct) <= 1 && Mathf.Abs(half * 2 - whole) <= 1,
                      $"half {half}, whole {whole}, without half {direct}");
            }
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

            // 9b. component rescale and the robot carry limit
            yield return RescaleChecks();
            // 10. restoration: with its zone mostly restored the lathe runs again
            {
                foreach (var r in g.robots) r.enabled = false;
                g.Presort(0.7f);
                yield return new WaitForSeconds(3f);
                var sp = g.builder.machineSpindle.position;
                Check("restoring a zone repairs the lathe (it runs, lamp on)", g.MachineRunning && g.builder.machineLamp.intensity > 0.5f,
                      $"zone {g.layout.zones[g.layout.ZoneAt(sp)].name} {g.progress.ZonePercent(g.layout.ZoneAt(sp)):0}% restored");
                LookFrom(sp + new Vector3(0.9f, 0.4f, 1.7f), sp);   // from inside the room (Unity +z = north here)
                yield return g.Capture(Path.Combine(outDir, "21_lathe_running.png"), 3);
            }

            g.PlaceCamera("0.6,1.65,3.6,0,-18");
            g.hud.SetHudVisible(true);
            yield return g.Capture(Path.Combine(outDir, "04_hud_overview.png"), 10);
            Finish();
        }

        void Finish()
        {
            File.WriteAllText(Path.Combine(outDir, "autoplay_report.json"), Json.Write(new Dictionary<string, object> { ["checks"] = checks, ["passed"] = (double)passed, ["failed"] = (double)failed }));
            Debug.Log($"[autoplay] {passed} passed, {failed} failed");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        readonly List<GameEvent> _events = new();
        int EventsSince(int from, GameEventKind k, System.Func<GameEvent, bool> where = null) => _events.Skip(from).Count(e => e.kind == k && (where == null || where(e)));

        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        /// <summary>Moves stray parts out of a destination onto the floor in front of its
        /// unit, as a player would before storing there. Returns how many.</summary>
        int ClearStrays(string dest)
        {
            var strays = g.StraysIn(dest);
            if (strays.Count == 0) return 0;
            var unit = g.units[Json.Str(g.db.boxes[dest], "unit")];
            var front = Vector3.ProjectOnPlane(unit.transform.TransformDirection(new Vector3(0, 0, -1)), Vector3.up).normalized;
            var basePos = unit.DestPoint(g.db.boxes[dest]);
            basePos.y = 0;
            int k = 0;
            foreach (var id in strays)
            {
                var def = g.world.DefOf(id);
                var p = basePos + front * (unit.size.z * 0.5f + 0.5f + 0.12f * (k / 4)) + Vector3.Cross(Vector3.up, front) * (0.12f * (k % 4) - 0.18f);
                k++;
                g.index.Remove(id);
                g.world.SetTransform(id, Matrix4x4.TRS(new Vector3(p.x, g.debris.At(p) + ItemDatabase.Extents(def)[2] * 0.5f + 0.01f, p.z), Quaternion.identity, Vector3.one * def.worldScale));
                g.world.SetState(id, ItemState.InWorld, "world");
                g.index.Insert(id);
                // onto its real support (the rule a load settles by), not just the debris height
                var b = g.index.WorldBounds(id);
                float dy = b.min.y - g.interaction.SupportUnder(id, b, new Dictionary<int, float>());
                if (dy > 0)
                {
                    var m = g.world.instances[id].transform;
                    m.m13 -= dy;
                    g.index.Remove(id);
                    g.world.SetTransform(id, m);
                    g.index.Insert(id);
                }
            }
            return strays.Count;
        }

        /// <summary>Would the part fall if `under` were gone?</summary>
        bool OnlySupport(int id, int under)
        {
            var b = g.index.WorldBounds(id);
            return b.min.y - g.interaction.SupportUnder(id, b, new Dictionary<int, float> { [under] = 50f }) > 0.03f;
        }

        /// <summary>Puts everything carried into the sorting tray (test setup).</summary>
        void EmptyHands()
        {
            foreach (var id in g.inventory.items.ToList()) if (!g.interaction.DropInTray(id, false)) { g.inventory.Remove(id); g.world.SetState(id, ItemState.InWorld, "world"); g.index.Insert(id); }
        }

        /// <summary>The robot-part tutorial: not for a robot's pickup or a robot-themed spare part,
        /// shown on the player's first real robot part, never again, and the flag is saved.</summary>
        IEnumerator TutorialChecks()
        {
            var w = g.world;
            var robotParts = w.instances.Where(i => i.state == ItemState.InWorld && w.DefOf(i.id).IsRobotPart).Select(i => i.id).ToList();
            var spare = w.instances.Where(i => i.state == ItemState.InWorld && !w.DefOf(i.id).IsRobotPart
                                               && w.DefOf(i.id).category >= 7).Select(i => i.id).DefaultIfEmpty(-1).First();
            if (spare < 0) spare = w.instances.First(i => i.state == ItemState.InWorld && !w.DefOf(i.id).IsRobotPart).id;
            bool before = !g.progress.milestones.Contains(Game.RobotPartTutorialKey) && !g.hud.TutorialShowing;
            // a robot picks up a robot part, the player an ordinary part: nothing
            w.SetState(robotParts[0], ItemState.Carried, "robot:retrobot");
            g.index.Remove(robotParts[0]);
            w.SetState(spare, ItemState.Carried, "player");
            g.index.Remove(spare);
            yield return Frames(2);
            bool quiet = !g.hud.TutorialShowing && !g.progress.milestones.Contains(Game.RobotPartTutorialKey);
            // the player's first robot part: shown
            w.SetState(robotParts[1], ItemState.Carried, "player");
            g.index.Remove(robotParts[1]);
            yield return Frames(2);
            bool shown = g.hud.TutorialShowing && g.progress.milestones.Contains(Game.RobotPartTutorialKey);
            yield return g.Capture(Path.Combine(outDir, "07g_tutorial_robot_part.png"), 3);
            // dismissed, then a second robot part: not again
            g.hud.DismissTutorial();
            w.SetState(robotParts[2], ItemState.Carried, "player");
            g.index.Remove(robotParts[2]);
            yield return Frames(2);
            bool once = !g.hud.TutorialShowing;
            // the flag is in the save and comes back with it
            var saved = Json.Write(g.progress.ToDict());
            var reloaded = new ProgressTracker(w, g.layout.zones.Select(z => z.name).ToList());
            reloaded.Load(Json.Obj(Json.Parse(saved)));
            bool persisted = reloaded.milestones.Contains(Game.RobotPartTutorialKey) && !reloaded.MarkOnce(Game.RobotPartTutorialKey);
            Check("the robot-part tutorial: not for a robot's pickup or an ordinary part, shown on the player's first robot part, once, saved",
                  before && quiet && shown && once && persisted,
                  $"before {before}; robot pickup + {w.DefOf(spare).displayName}: quiet {quiet}; first robot part ({w.DefOf(robotParts[1]).displayName}): shown {shown}; second: not again {once}; saved {persisted}");
        }

        /// <summary>The start: a starter assortment (different large parts, one copy each, the
        /// rest in the piles) and Retrobot's chassis in the open straight ahead, not in your pockets.</summary>
        IEnumerator StartAreaChecks()
        {
            var w = g.world;
            var sp = Shared.Pos(g.layout.spawn.x, 0, g.layout.spawn.z);
            var starter = g.starterIds;
            var defs = starter.Select(i => w.instances[i].defId).ToList();
            bool distinct = defs.Distinct().Count() == defs.Count;
            bool notAll = defs.All(d => w.IdsOf(d).Count(i => (w.instances[i].Position - sp).magnitude < 4.5f) < w.TotalOf(d));
            int cats = starter.Select(i => w.DefOf(i).category).Distinct().Count();
            Check("a starter assortment by the start: different large parts, one copy each, their other copies elsewhere",
                  starter.Count >= 5 && distinct && notAll && cats >= 4 && starter.All(i => w.instances[i].state == ItemState.InWorld && (w.instances[i].Position - sp).magnitude < 4.5f),
                  string.Join(", ", starter.Select(i => $"{w.DefOf(i).displayName} ({w.DefOf(i).CategoryName}, 1 of {w.TotalOf(w.instances[i].defId)}) {(w.instances[i].Position - sp).magnitude:0.0} m")));
            var torso = g.workbench.PartIds(g.workbench.P("retrobot")).Select(id => w.instances[id]).FirstOrDefault(i => w.DefOf(i.id).shape == "Torso");
            var d = torso != null ? torso.Position - sp : Vector3.zero;
            float off = Mathf.Abs(Vector2.SignedAngle(new Vector2(0, -1), new Vector2(d.x, -d.z)));   // Unity +z = Godot -z = ahead
            var tb = torso == null ? default : g.index.WorldBounds(torso.id);
            var top = torso == null ? -1 : g.index.QuerySphere(tb.center, tb.extents.magnitude + 0.1f).Where(o => o != torso.id && w.instances[o].state == ItemState.InWorld)
                .Where(o => g.index.WorldBounds(o) is var ob && ob.min.y > tb.max.y - 0.01f && ob.min.x < tb.max.x && ob.max.x > tb.min.x && ob.min.z < tb.max.z && ob.max.z > tb.min.z).DefaultIfEmpty(-1).First();
            Check("Retrobot's chassis (torso) lies in the open straight ahead of the start, not in the inventory, exactly one",
                  torso != null && torso.state == ItemState.InWorld && !g.inventory.items.Contains(torso.id) && d.magnitude < 4f && off < 25 && top < 0 && w.TotalOf(torso.defId) == 1,
                  torso == null ? "no torso" : $"{w.DefOf(torso.id).displayName} {d.magnitude:0.0} m from the spawn, {off:0} deg off ahead, {(top < 0 ? "nothing on it" : $"under {w.DefOf(top).displayName} (its bounds {g.index.WorldBounds(top).min.y:0.000}-{g.index.WorldBounds(top).max.y:0.000} m at {w.instances[top].Position}, the torso's {tb.min.y:0.000}-{tb.max.y:0.000} m at {torso.Position} size {tb.size})")}");
            // mixing: what lies around a part is rarely the same part, and a set is spread over a few piles
            var loose = w.instances.Where(i => i.state == ItemState.InWorld && !w.DefOf(i.id).IsRobotPart).ToList();
            int samples = 0;
            double sameDef = 0, sameCat = 0;
            for (int k = 0; k < loose.Count; k += 8)
            {
                var i = loose[k];
                var nb = g.index.QuerySphere(i.Position, 0.4f).Where(o => o != i.id && w.instances[o].state == ItemState.InWorld).ToList();
                if (nb.Count == 0) continue;
                samples++;
                sameDef += nb.Count(o => w.instances[o].defId == i.defId) / (double)nb.Count;
                sameCat += nb.Count(o => w.DefOf(o).category == w.DefOf(i.id).category) / (double)nb.Count;
            }
            var spans = g.db.order.Where(x => !g.db.Get(x).IsRobotPart && w.TotalOf(x) >= 4).Select(x =>
            {
                var ps = w.IdsOf(x).Select(i => w.instances[i].Position).ToList();
                return ps.Max(a => ps.Max(b => (a - b).magnitude));
            }).OrderBy(x => x).ToList();
            float median = spans.Count > 0 ? spans[spans.Count / 2] : 0;
            sameDef /= Mathf.Max(1, samples); sameCat /= Mathf.Max(1, samples);
            // per pile site (the generator's own record): how much of a pile is its biggest category,
            // and in how many piles a set's copies lie
            var (a1, gen1) = g.GenerateOnly(w.seed);
            var (a2, _) = g.GenerateOnly(w.seed);
            bool same = a1 != null && a2 != null && a1.instances.Zip(a2.instances, (x, y) => x.transform == y.transform).All(x => x);
            double dominant = 0, onePile = 0, pilesPerSet = 0;
            int placed = 0, sets = 0;
            if (a1 != null)
            {
                foreach (var grp in a1.instances.Where(i => gen1.pileOf[i.id] >= 0 && !a1.DefOf(i.id).IsRobotPart).GroupBy(i => gen1.pileOf[i.id]))
                {
                    int n = grp.Count();
                    dominant += grp.GroupBy(i => a1.DefOf(i.id).category).Max(c => c.Count());
                    placed += n;
                }
                foreach (var x in g.db.order.Where(x => !g.db.Get(x).IsRobotPart && a1.TotalOf(x) >= 3))
                {
                    int piles = a1.IdsOf(x).Select(i => gen1.pileOf[i]).Distinct().Count();
                    sets++;
                    pilesPerSet += piles;
                    if (piles == 1) onePile++;
                }
                dominant /= Mathf.Max(1, placed); pilesPerSet /= Mathf.Max(1, sets); onePile /= Mathf.Max(1, sets);
            }
            Check("parts are mixed: no pile is mostly one category, a set's copies lie in several piles, few neighbours are the same part",
                  dominant < 0.4 && onePile < 0.15 && pilesPerSet >= 2 && sameDef < 0.12,
                  $"biggest category per pile {dominant:P0} of its parts; sets of 3+ in one pile only {onePile:P0}, piles per set {pilesPerSet:0.0}; {samples} parts sampled: neighbours within 0.4 m the same part {sameDef:P1}, the same category {sameCat:P1}; median span of a set's copies {median:0.0} m");
            Check("world generation is deterministic from the seed", same, a1 == null ? "generation failed" : $"{a1.Count} parts");
            // what a new game shows: the chassis ahead, the assortment to the right
            g.player.Teleport(sp);
            g.player.SetView(0, -0.5f);
            yield return g.Capture(Path.Combine(outDir, "07e_start_ahead.png"), 3);
            g.player.SetView(-0.75f, -0.6f);
            yield return g.Capture(Path.Combine(outDir, "07f_start_right.png"), 3);
        }

        /// <summary>Playtest feedback: a fair, readable workshop.</summary>
        IEnumerator PlaytestChecks()
        {
            var w = g.world;
            var loose = w.instances.Where(i => i.state == ItemState.InWorld).ToList();
            // every part visible and reachable: none high on shelves, none hidden inside boxes, bins or crates
            float maxY = WorkshopLayout.MaxStrayShelf + 0.5f;
            int high = loose.Count(i => i.Position.y > maxY);
            int hidden = loose.Count(i => g.InsideContainer(i.Position, 0));
            Check("every unsorted part lies in reach and in sight (none high up, none inside containers)", high == 0 && hidden == 0, $"{high} above {maxY:0.0} m, {hidden} inside containers");
            // the start: Retrobot's parts alone on open floor near you, and one small set close by
            var sp = Shared.Pos(g.layout.spawn.x, 0, g.layout.spawn.z);
            var retro = g.workbench.PartIds(g.workbench.P("retrobot")).Select(id => w.instances[id]).ToList();
            bool Buried(ItemInstance i)
            {
                var ib = g.index.WorldBounds(i.id);
                return g.index.QuerySphere(ib.center, ib.extents.magnitude + 0.1f).Any(o =>
                {
                    if (o == i.id) return false;
                    var ob = g.index.WorldBounds(o);
                    return ob.min.y > ib.max.y - 0.01f && ob.min.x < ib.max.x && ob.max.x > ib.min.x && ob.min.z < ib.max.z && ob.max.z > ib.min.z;   // lies on top of it
                });
            }
            string OnTop(ItemInstance i)
            {
                var ib = g.index.WorldBounds(i.id);
                var o = g.index.QuerySphere(ib.center, ib.extents.magnitude + 0.1f).FirstOrDefault(x => x != i.id && g.index.WorldBounds(x).min.y > ib.max.y - 0.01f);
                return $" (under {w.DefOf(o).displayName} at {w.instances[o].Position.y:0.00} m; part top {ib.max.y:0.00} m)";
            }
            Check("Retrobot's parts lie in the open near the start (nothing on top of them)", retro.All(i => i.state == ItemState.InWorld && (i.Position - sp).magnitude < 7f && !Buried(i)),
                  string.Join(", ", retro.Select(i => $"{(i.Position - sp).magnitude:0.0} m{(Buried(i) ? " buried" + OnTop(i) : "")}")));
            yield return StartAreaChecks();

            // nothing floats: take a part that carries another; the one on top comes down onto what is below
            var top = loose.Where(i => i.Position.y > 0.1f && i.Position.y < 1.0f && !g.db.Get(i.defId).IsRobotPart)
                .Select(i => (i, under: g.index.QuerySphere(i.Position - Vector3.up * 0.06f, 0.05f).Where(o => o != i.id && w.instances[o].state == ItemState.InWorld && w.instances[o].Position.y < i.Position.y - 0.02f && !g.db.Get(w.instances[o].defId).IsRobotPart).ToList()))
                .FirstOrDefault(x => x.under.Count >= 1 && OnlySupport(x.i.id, x.under[0]));
            if (top.i != null)
            {
                float y0 = top.i.Position.y;
                float Gap(int id) { var bb = g.index.WorldBounds(id); return bb.min.y - g.interaction.SupportUnder(id, bb, new Dictionary<int, float>()); }
                // gaps that were there before (a long part bridging two supports reads as a gap
                // under its middle) are not what this checks: only what the removal causes. Wider than the
                // after-check: its centre (the top part) comes down, and the parts it then reaches were there before
                var before = g.index.QuerySphere(top.i.Position, 1.0f).ToDictionary(o => o, Gap);
                g.player.Teleport(top.i.Position + new Vector3(0.6f, -top.i.Position.y, 0.6f));
                g.interaction.Pickup(top.under[0], false);
                yield return new WaitForSeconds(0.3f);
                for (float t = 0; t < 10 && g.piles.Settling; t += Time.deltaTime) yield return null;
                // nothing within half a metre is left hanging in the air
                var around = g.index.QuerySphere(top.i.Position, 0.5f).Where(o => w.instances[o].state == ItemState.InWorld).ToList();
                float Worse(int o) => Gap(o) - before.GetValueOrDefault(o, 0f);
                int worstId = around.Count > 0 ? around.OrderByDescending(Worse).First() : -1;
                float worst = worstId >= 0 ? Worse(worstId) : 0;
                string who = worstId >= 0 ? $" ({w.DefOf(worstId).displayName}: gap {Gap(worstId):0.000} m, before {before.GetValueOrDefault(worstId, -1f):0.000} m)" : "";
                Check("nothing floats when the part under it is taken away", worst < 0.05f, $"top part {y0:0.000} -> {top.i.Position.y:0.000} m; worst new gap of {around.Count} parts nearby {worst:0.000} m{who}; settle: {g.piles.lastInfo}");
                EmptyHands();
            }
            else Check("a part falls when the part under it is taken away", false, "no stacked pair found");

            // stray parts in a destination block it until they are taken out
            string box = g.db.boxes.Keys.FirstOrDefault(bx => g.StraysIn(bx).Count > 0 && g.world.IdsOf(g.db.order.FirstOrDefault(d => g.db.Get(d).storageBox == bx)).Any(i => w.instances[i].state == ItemState.InWorld));
            if (box != null)
            {
                int own = g.world.IdsOf(g.db.order.First(d => g.db.Get(d).storageBox == box)).First(i => w.instances[i].state == ItemState.InWorld);
                g.inventory.capacity = 200;   // test only: room for every stray
                Take(own);
                var blocked = g.storage.Check(box, own);
                var strays = g.StraysIn(box);
                foreach (var st in strays) Take(st);
                var after = g.storage.Check(box, own);
                Check("stray parts lying in a destination block it until they are taken out", blocked == StorageSystem.Result.Blocked && after == StorageSystem.Result.Ok,
                      $"{box}: {blocked} with {strays.Count} strays, then {after}");
                EmptyHands();
                g.ApplySkills();
            }
            else Check("stray parts lying in a destination block it until they are taken out", false, "no destination with strays");
            yield return new WaitForSeconds(1f);   // parts dropped into the tray finish their flight
        }

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
            var wb = g.workbench;
            var others = g.robots.Where(r => r.id != "retrobot" && !wb.P(r.id).crafted && r.Mobile).ToList();
            Check("base capacities follow the capability matrix (1 / 1 / 1 / 3 / 1, not scaled by world size)",
                  g.robots.First(r => r.id == "retrobot").Capacity == 1 && g.robots.First(r => r.id == "unibot").Capacity == 1 && g.robots.First(r => r.id == "spiderbot").Capacity == 1
                  && g.robots.First(r => r.id == "caterpillarbot").Capacity == 3 && g.robots.First(r => r.id == "hoverbot").Capacity == 1,
                  string.Join(", ", g.robots.Select(r => $"{r.displayName} {r.Capacity}")));
            var retro = g.retrobot; var uni = g.robots.First(r => r.id == "unibot");
            Check("Unibot is about twice as fast as Retrobot", uni.WalkSpeed / retro.BaseStat("walk_speed") is > 1.8f and < 2.2f, $"{uni.WalkSpeed:0.00} vs {retro.BaseStat("walk_speed"):0.00} m/s");
            var mobile = g.robots.Where(r => r.Mobile).ToList();
            Check("five robot types at toy scale, up to three Hoverbots", mobile.Count(r => !wb.P(r.id).crafted) == 5 && g.robots.Count(r => r.type == "hoverbot") == 3 && mobile.All(r => r.Height <= 0.31f),
                  string.Join(", ", mobile.Select(r => $"{r.displayName} {r.Height:0.00} m")));
            LevelTo(g.progression.maxLevel);
            // half-built on the bench: ghosts show what is still to find
            foreach (var r in others) wb.ForceInstall(wb.P(r.id), wb.Required(wb.P(r.id)) / 2);
            var bay = g.workbench.transform;
            var front = wb.Front;
            var benchMid = bay.position + Vector3.up * 0.9f;
            LookFrom(benchMid + front * 1.5f + Vector3.up * 0.55f, benchMid);
            yield return g.Capture(Path.Combine(outDir, "15_robots_half_built.png"), 3);
            LookFrom(wb.MatSpot(3).pos + front * 0.75f + Vector3.up * 0.35f + Vector3.Cross(Vector3.up, front) * 0.3f, wb.MatSpot(3).pos + Vector3.up * 0.08f);
            yield return g.Capture(Path.Combine(outDir, "15b_caterpillarbot_half_built.png"), 3);
            foreach (var r in others) wb.ForceInstall(wb.P(r.id));
            Check("a finished robot waits for the player to activate it", others.All(r => !r.Active && wb.CanActivate(wb.P(r.id))), string.Join(", ", others.Select(r => RobotWorkbench.StatusText(wb.StatusOf(wb.P(r.id))))));
            foreach (var r in others) wb.Activate(wb.P(r.id));
            float t0 = Time.realtimeSinceStartup;
            while (others.Any(r => !r.Active) && Time.realtimeSinceStartup - t0 < 15) yield return null;
            Check("activating a robot raises one RobotActivated event for it", others.All(r => _events.Count(e => e.kind == GameEventKind.RobotActivated && e.actor == r.Owner) == 1),
                  string.Join(", ", others.Select(r => $"{r.id} {_events.Count(e => e.kind == GameEventKind.RobotActivated && e.actor == r.Owner)}")));
            Check("all robots start work once activated", others.All(r => r.Active), string.Join(", ", others.Select(r => r.Describe())));
            var mid = (g.robots[0].home + g.robots[4].home) * 0.5f;
            // size line-up: everyone at the repair bay, seen from eye height
            var saved = g.robots.ToDictionary(r => r, r => (r.transform.position, r.transform.rotation));
            foreach (var r in g.robots) { r.enabled = false; r.transform.SetPositionAndRotation(r.home + Vector3.up * (r.id == "hoverbot" ? 0.25f : 0), r.homeRot); }
            LookFrom(mid + front * 1.5f + Vector3.up * (PlayerController.Eye - 0.0f), mid + Vector3.up * 0.1f);
            yield return g.Capture(Path.Combine(outDir, "16_robots_lineup_eye_level.png"), 3);
            LookFrom(mid + front * 0.85f + Vector3.up * 0.35f, mid + Vector3.up * 0.14f);
            yield return g.Capture(Path.Combine(outDir, "17_robots_lineup_close.png"), 3);
            foreach (var r in g.robots) { r.transform.SetPositionAndRotation(saved[r].Item1, saved[r].Item2); r.enabled = true; }

            // a controlled arrangement for Caterpillarbot (capacity 3): three loose parts of one kind in a
            // little cluster on open floor in front of it, and the player's focus command on that kind,
            // so "collects several parts before a delivery run" no longer depends on what lies nearby
            var catBot = g.robots.First(r => r.id == "caterpillarbot");
            var catDef = g.db.order.Select(d => g.db.Get(d)).FirstOrDefault(d => !d.IsRobotPart && ItemDatabase.Extents(d)[0] < 0.1f
                && g.world.IdsOf(d.id).Count(i => g.world.instances[i].state == ItemState.InWorld && g.world.instances[i].owner == "world") >= 3);
            if (catDef != null)
            {
                var cf = Vector3.ProjectOnPlane(catBot.transform.forward, Vector3.up).normalized;
                var cs = Vector3.Cross(Vector3.up, cf);
                int ck = 0;
                foreach (var id in g.world.IdsOf(catDef.id).Where(i => g.world.instances[i].state == ItemState.InWorld && g.world.instances[i].owner == "world").Take(3).ToList())
                {
                    var p = catBot.transform.position + cf * 0.55f + cs * ((ck++ - 1) * 0.14f);
                    g.index.Remove(id);
                    g.world.SetTransform(id, Matrix4x4.TRS(new Vector3(p.x, g.debris.At(p) + ItemDatabase.Extents(catDef)[2] * 0.5f + 0.01f, p.z), Quaternion.identity, Vector3.one * catDef.worldScale));
                    g.index.Insert(id);
                }
                catBot.CommandFocus(catDef.id);
            }
            var before = g.robots.ToDictionary(r => r.id, r => (r.delivered, r.staged));
            var catPick0 = (catBot.stows, catBot.unstows, catBot.pickCount, catBot.delivered + catBot.staged);
            int evRobots = _events.Count;
            Time.timeScale = 4;
            bool[] shot = new bool[5];
            bool doubleHeld = false;
            t0 = Time.realtimeSinceStartup;
            int catMax = 0;
            var uniStored0 = uni.delivered;
            var spiderPicks = new HashSet<int>();
            var spider = g.robots.First(r => r.id == "spiderbot");
            while (Time.realtimeSinceStartup - t0 < 90 && g.robots.Where(r => r.Active).Any(r => r.delivered + r.staged - before[r.id].delivered - before[r.id].staged < 2))
            {
                catMax = Mathf.Max(catMax, g.robots.First(r => r.id == "caterpillarbot").carried.Count);
                foreach (var c in spider.carried) spiderPicks.Add(g.world.instances[c].defId);
                if (spider.targetId >= 0 && spider.assignedDef >= 0 && g.world.instances[spider.targetId].defId != spider.assignedDef) spiderPicks.Add(-99);
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
            Check("Unibot never stores directly: everything goes to staging zones (still unsorted)", uni.delivered == uniStored0 && uni.staged > before["unibot"].staged
                  && g.world.instances.Where(i => i.owner.StartsWith("stage:")).All(i => i.state == ItemState.InWorld), $"{uni.staged - before["unibot"].staged} staged, {uni.delivered - uniStored0} stored");
            Check("Caterpillarbot collects several parts before a delivery run", catMax >= 2, $"up to {catMax} on board");
            int catPicked = catBot.pickCount - catPick0.pickCount, catPut = catBot.delivered + catBot.staged - catPick0.Item4;
            Check("Caterpillarbot stows each part it grips into its front box and takes each out again with the grippers to put it away",
                  catPicked >= 2 && catBot.stows - catPick0.stows == catPicked && catBot.unstows - catPick0.unstows >= catPut,
                  $"{catPicked} gripped, {catBot.stows - catPick0.stows} stowed; {catPut} put away, {catBot.unstows - catPick0.unstows} taken out");
            Check("Caterpillarbot never draws a part in from afar: every grip within 5 cm, the rest deferred", catBot.pickCount >= 1 && catBot.maxGripGap <= 0.05f, $"largest grip gap {catBot.maxGripGap * 100:0.0} cm, {catBot.deferred} deferred");
            int robSorted = g.robots.Sum(r => r.delivered - before[r.id].delivered), robStaged = g.robots.Sum(r => r.staged - before[r.id].staged);
            int evSorted = EventsSince(evRobots, GameEventKind.PartSorted, e => e.actor.StartsWith("robot:")), evStaged = EventsSince(evRobots, GameEventKind.PartStaged);
            Check("robot deliveries and stagings raise exactly one event each, with the robot's type",
                  evSorted == robSorted && evStaged == robStaged && _events.Skip(evRobots).Where(e => e.kind is GameEventKind.PartSorted or GameEventKind.PartStaged && e.actor.StartsWith("robot:")).All(e => e.robotType != ""),
                  $"{evSorted} sorted events / {robSorted} stored, {evStaged} staged events / {robStaged} staged; {EventsSince(evRobots, GameEventKind.RobotTripCompleted)} trips");
            Check("Unibot's grippers open at contact, close before the lift and open to let go", uni.pickCount >= 1 && uni.minOpenAtContact >= 0.95f && uni.minClosedAtAttach >= 0.99f && (uni.releases == 0 || uni.minOpenAtRelease >= 0.99f),
                  $"{uni.pickCount} pick-ups, {uni.releases} releases; lowest opening at contact {uni.minOpenAtContact:0.00}, closure {uni.minClosedAtAttach:0.00}, opening at release {uni.minOpenAtRelease:0.00}");
            Check("Unibot holds its load in the grippers while it rolls and turns", uni.carrySyncMax <= 0.01f, $"largest gap {uni.carrySyncMax * 100:0.0} cm");
            Check("Unibot never draws a part in from afar: every grip within 5 cm, the rest deferred", uni.pickCount >= 1 && uni.maxGripGap <= 0.05f, $"largest grip gap {uni.maxGripGap * 100:0.0} cm, {uni.deferred} deferred");
            Check("Spiderbot collects only its one assigned definition", !spiderPicks.Contains(-99), $"definitions carried: {spiderPicks.Count}, now {spider.Describe()}");
            yield return RobotSpecChecks(hover, uni, spider);
            // battery: a robot running low finishes its load, goes home, charges and resumes
            var sp = g.robots.First(r => r.id == "spiderbot");
            sp.SetBattery(0.05f);
            Time.timeScale = 4;
            float tc = Time.realtimeSinceStartup, gc = Time.time;
            bool charged = false, wentHome = false;
            // a game-time budget (trip home + a full charge), so a slow frame rate on Massive doesn't fail it
            while (Time.time - gc < 240 && Time.realtimeSinceStartup - tc < 180 && !charged)
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
            yield return ModuleChecks();
        }

        /// <summary>Modify Robot (WORKSHOP_WRECKAGE_MODIFY_ROBOT_MODULAR_COMPONENTS_SPEC §13): the module registry's
        /// conservation, swaps, propeller rules, incomplete / repaired builds, the build notes and save/load.</summary>
        IEnumerator ModuleChecks()
        {
            var m = g.modules;
            var built = RobotModules.Chassis.Where(m.Built).ToList();
            string Where() => string.Join(";", m.all.Select(x => x.id + "@" + x.location));
            Check("every activated robot carries its own factory modules and the 24-module audit is clean",
                  built.Count >= 5 && built.All(r => m.Factory(r) && m.Complete(r)) && m.Audit().Count == 0, $"built {string.Join(",", built)}; {string.Join("; ", m.Audit())}");
            Check("a first build is noted as colour coordinated, in the chassis' own scheme, all originals",
                  built.All(r => _events.Count(e => e.kind == GameEventKind.RobotBuilt && e.destination == r && (e.count & 15) == 15) == 1),
                  string.Join(", ", _events.Where(e => e.kind == GameEventKind.RobotBuilt).Select(e => $"{e.destination}:{e.category}/{e.count}")));
            var hovers = built.Where(RobotModules.IsHover).ToList();
            Check("a propeller never goes on another chassis", m.CanSwap("retrobot", "hoverbot", ModuleSlot.Prop) != "" && m.CanInstall("prop.hoverbot", "unibot") != "", m.CanSwap("retrobot", "hoverbot", ModuleSlot.Prop));
            int ev0 = _events.Count;
            // a head swap between two robots: each keeps the other's head in its own paint
            bool swapped = m.Swap("retrobot", "hoverbot", ModuleSlot.Head, g.playTime);
            var retro = g.retrobot; var hov = g.robots.First(r => r.id == "hoverbot");
            retro.ApplyModules(); hov.ApplyModules();
            yield return Frames(2);
            Check("two robots exchange heads in one step; the models draw the other's head",
                  swapped && m.At("retrobot", ModuleSlot.Head).id == "head.hoverbot" && m.At("hoverbot", ModuleSlot.Head).id == "head.retrobot"
                  && retro.ModelFroms.h == "hoverbot" && hov.ModelFroms.h == "retrobot" && m.Audit().Count == 0,
                  $"{m.At("retrobot", ModuleSlot.Head)?.id} / {m.At("hoverbot", ModuleSlot.Head)?.id}; {string.Join("; ", m.Audit())}");
            Check("the first cross-robot exchange is noted once", _events.Skip(ev0).Count(e => e.kind == GameEventKind.FirstModuleTransfer) == 1 && _events.Skip(ev0).Count(e => e.kind == GameEventKind.ModuleExchanged) == 2, "");
            if (hovers.Count >= 2)
            {
                bool pswap = m.Swap(hovers[0], hovers[1], ModuleSlot.Prop, g.playTime);
                Check("two Hoverbots exchange propellers", pswap && m.At(hovers[0], ModuleSlot.Prop).origin == hovers[1] && m.Audit().Count == 0, Where());
            }
            // remove a head: incomplete; the same head back: repaired, coordinated again
            int ev1 = _events.Count;
            var uniHead = m.Remove("unibot", ModuleSlot.Head);
            Check("removing a module parks it in a spare slot and leaves the robot incomplete",
                  uniHead != null && uniHead.BenchSlot >= 0 && !m.Complete("unibot") && m.Missing("unibot").Single() == ModuleSlot.Head
                  && _events.Skip(ev1).Count(e => e.kind == GameEventKind.RobotIncomplete && e.destination == "unibot") == 1 && m.Audit().Count == 0, Where());
            m.Install(uniHead.id, "unibot", g.playTime);
            var note = _events.Skip(ev1).LastOrDefault(e => e.kind == GameEventKind.RobotBuilt);
            Check("refitting completes it again: noted as a repair, colour coordinated, its own scheme",
                  m.Complete("unibot") && note.destination == "unibot" && (note.count & 1) == 0 && (note.count & 6) == 6 && note.category == "green", $"{note.category}/{note.count}");
            // a mixed build: the Hoverbot head Retrobot borrowed goes onto Unibot
            var spare = m.Remove("unibot", ModuleSlot.Head);
            var borrowed = m.Remove("retrobot", ModuleSlot.Head);   // head.hoverbot
            int ev2 = _events.Count;
            m.Install(borrowed.id, "unibot", g.playTime);
            var mixed = _events.Skip(ev2).LastOrDefault(e => e.kind == GameEventKind.RobotBuilt);
            Check("a robot finished with another robot's head is noted as not colour coordinated",
                  mixed.destination == "unibot" && (mixed.count & 2) == 0 && mixed.category == "mixed" && m.Audit().Count == 0, $"{mixed.category}/{mixed.count}");
            // exchange on install: the occupied slot's module takes the spare's place
            int benchBefore = m.Spares.Count();
            m.Install(spare.id, "unibot", g.playTime);
            Check("installing into an occupied slot exchanges, never discards", m.At("unibot", ModuleSlot.Head).id == "head.unibot" && m.Get("head.hoverbot").BenchSlot >= 0 && m.Spares.Count() == benchBefore && m.Audit().Count == 0, Where());
            m.Install("head.hoverbot", "retrobot", g.playTime);   // Retrobot keeps Hoverbot's head through the save
            foreach (var r in g.robots) r.ApplyModules();
            // save / load keeps every assignment and the notes; loading raises nothing
            var before = Where();
            int notes = m.builds.Count;
            g.SaveGame("autotest_mod");
            g.StartWorld(g.world.seed, null);
            int evLoad = _events.Count;
            g.LoadGame("autotest_mod");
            g.player.enabled = false;
            yield return Frames(4);
            m = g.modules;
            Check("module assignments, mixed colours and build notes survive save/load without events",
                  Where() == before && m.builds.Count == notes && m.Audit().Count == 0 && g.retrobot.ModelFroms.h == "hoverbot"
                  && !_events.Skip(evLoad).Any(e => e.kind is GameEventKind.RobotBuilt or GameEventKind.ModuleFitted or GameEventKind.ModuleExchanged or GameEventKind.ModuleRemoved),
                  $"{(Where() == before ? "same" : before + "  vs  " + Where())}; notes {notes} -> {m.builds.Count}");
            // a save from before modules: each operational robot maps to its own factory modules
            var save = SaveSystem.Read("autotest_mod");
            save.Remove("modules");
            SaveSystem.Write("autotest_mod", save);
            g.StartWorld(g.world.seed, null);
            g.LoadGame("autotest_mod");
            yield return Frames(4);
            m = g.modules;
            Check("an old save without modules migrates to each robot's own modules, no duplicates",
                  RobotModules.Chassis.Where(m.Built).SequenceEqual(RobotModules.Chassis.Where(c => g.robots.First(r => r.id == c).Operational))
                  && RobotModules.Chassis.Where(m.Built).All(m.Factory) && m.Audit().Count == 0 && g.retrobot.ModelFroms.h == null, Where());
            File.Delete(SaveSystem.PathOf("autotest_mod"));
            yield return SummonChecks();
        }

        /// <summary>Modify Robot §6-§8: summoning to the workbench (cargo set down, no reservations left, a
        /// parking spot each), docking, cancelling, an incomplete robot kept from work, release and save/load.</summary>
        IEnumerator SummonChecks()
        {
            var m = g.modules;
            Robot R(string id) => g.robots.First(r => r.id == id);
            var uni = R("unibot"); var hov = R("hoverbot"); var retro = g.retrobot;
            Time.timeScale = 4;
            float t0 = Time.realtimeSinceStartup;
            while (uni.carried.Count == 0 && Time.realtimeSinceStartup - t0 < 30) yield return null;   // summoned with a load
            int ev0 = _events.Count;
            bool hadLoad = uni.carried.Count > 0;
            uni.Summon(); hov.Summon(); retro.Summon();
            yield return Frames(2);
            bool cancelled = retro.CancelSummon();
            yield return Frames(2);
            Check("a summons taken back before docking sends the robot back to work", cancelled && retro.Active && !retro.AtBench, retro.Describe());
            float g0 = Time.time;
            t0 = Time.realtimeSinceStartup;
            while (!(uni.Docked && hov.Docked) && Time.time - g0 < 120 && Time.realtimeSinceStartup - t0 < 90) yield return null;
            Time.timeScale = 1;
            float Off(Robot r) { var (p, _) = g.workbench.ParkSpot(r.id); return new Vector2(r.transform.position.x - p.x, r.transform.position.z - p.z).magnitude; }
            Check("summoned robots set their loads down, drop their reservations and dock at their own spots",
                  uni.Docked && hov.Docked && uni.carried.Count == 0 && hov.carried.Count == 0 && Off(uni) < 0.45f && Off(hov) < 0.2f
                  && !g.world.instances.Any(i => i.owner == uni.Owner || i.owner == hov.Owner) && uni.targetId < 0 && hov.targetId < 0
                  && _events.Skip(ev0).Count(e => e.kind == GameEventKind.RobotSummoned) == 2,
                  $"{(hadLoad ? "with" : "without")} a load; {uni.Describe()} ({Off(uni):0.00} m) / {hov.Describe()} ({Off(hov):0.00} m)");
            // docked robots don't work; a Hoverbot without its propeller sets down and can't be released
            int d0 = uni.delivered + uni.staged;
            float y0 = hov.transform.position.y;
            var prop = m.Remove("hoverbot", ModuleSlot.Prop);
            hov.ApplyModules();
            yield return new WaitForSeconds(3);
            string why = hov.ReleaseFromBench();
            Check("an incomplete robot stays docked: it lands without its propeller and can't be released",
                  prop != null && hov.Docked && why != "" && hov.transform.position.y < y0 - 0.1f && uni.delivered + uni.staged == d0, $"'{why}'; height {y0:0.00} -> {hov.transform.position.y:0.00}");
            yield return ModifyScreenShot("hoverbot", ModuleSlot.Prop, "21_modify_incomplete.png");
            // save mid-modification: docked, incomplete and on-the-way robots come back as they were
            var spider = R("spiderbot");
            spider.Summon();
            g.SaveGame("autotest_dock");
            g.StartWorld(g.world.seed, null);
            g.LoadGame("autotest_dock");
            g.player.enabled = false;
            yield return Frames(4);
            m = g.modules; uni = R("unibot"); hov = R("hoverbot"); spider = R("spiderbot");
            Check("saved mid-modification: docked robots stay docked at their spots, the incomplete one too, a summoned one resumes",
                  uni.Docked && hov.Docked && !m.Complete("hoverbot") && Off(uni) < 0.45f && spider.AtBench && m.Audit().Count == 0,
                  $"{uni.Describe()} / {hov.Describe()} / {spider.Describe()}");
            m.Install(prop.id, "hoverbot", g.playTime);
            hov.ApplyModules();
            // a mixed look at the bench: Unibot's green left arm on the Hoverbot, the Hoverbot's on Unibot
            m.Swap("hoverbot", "unibot", ModuleSlot.ArmL, g.playTime);
            hov.ApplyModules(); uni.ApplyModules();
            yield return ModifyScreenShot("hoverbot", ModuleSlot.ArmL, "22_modify_mixed.png");
            Check("the Modify Robot screen exchanges arms between docked robots; both models show the other's arm",
                  hov.ModelFroms.l == "unibot" && uni.ModelFroms.l == "hoverbot" && m.Audit().Count == 0, $"{hov.ModelFroms.l} / {uni.ModelFroms.l}");
            m.Swap("hoverbot", "unibot", ModuleSlot.ArmL, g.playTime);
            hov.ApplyModules(); uni.ApplyModules();
            string r1 = hov.ReleaseFromBench(), r2 = uni.ReleaseFromBench();
            yield return Frames(2);
            Check("complete again, docked robots are released back to work", r1 == "" && r2 == "" && hov.Active && uni.Active, $"'{r1}' '{r2}'");
            spider.CancelSummon(); spider.ReleaseFromBench();
            File.Delete(SaveSystem.PathOf("autotest_dock"));
        }

        IEnumerator ModifyScreenShot(string robotId, ModuleSlot slot, string file)
        {
            g.hud.SelectModify(robotId, slot);
            g.SetState("paused");
            g.hud.OpenScreen("modify");
            yield return g.Capture(Path.Combine(outDir, file), 5);
            g.hud.BackFromSubScreen();
            g.SetState("play");
        }

        /// <summary>Robot specialisation, upgrades and the second Hoverbot
        /// (robots_and_levels.md §19, robot_blueprints_and_assembly.md §10 test 14).</summary>
        IEnumerator RobotSpecChecks(Robot hover, Robot uni, Robot spider)
        {
            var wb = g.workbench;
            var pr = g.progression;
            var retro = g.retrobot;
            bool High(string box) => g.db.boxes.TryGetValue(box, out var bd) && Json.Num(bd, "elevation_m") > 1.0;
            bool Loose(ItemInstance i) => i.state == ItemState.InWorld && !g.robotReserved.Contains(i.id) && g.world.DefOf(i.id).robotCarry && !g.world.DefOf(i.id).IsRobotPart && !i.owner.StartsWith("stage:");

            // Scenario A/B: a part left in the staging zone below a high destination is stored by Hoverbot
            var staged = g.world.instances.FirstOrDefault(i => Loose(i) && i.Position.y < 0.4f && High(g.world.DefOf(i.id).storageBox) && ItemDatabase.Extents(g.world.DefOf(i.id))[0] <= hover.MaxItemSize);
            if (staged != null)
            {
                var box = g.world.DefOf(staged.id).storageBox;
                var spot = hover.StagingSpot(box).Value;
                var def = g.world.DefOf(staged.id);
                g.index.Remove(staged.id);
                g.world.SetTransform(staged.id, Matrix4x4.TRS(spot + Vector3.up * (ItemDatabase.Extents(def)[2] * 0.5f + 0.01f), Quaternion.identity, Vector3.one * def.worldScale));
                g.world.SetState(staged.id, ItemState.InWorld, "stage:" + box);
                g.index.Insert(staged.id);
                Check("staged parts stay UNSORTED and don't count towards their set", !ProgressTracker.Accounted(staged.state) && !g.progress.IsComplete(staged.defId), $"part {staged.id} waits below {box}");
                int hd = hover.delivered;
                hover.SetBattery(1);
                // every part waiting in a staging zone right now (the planted one and Unibot's)
                var waiting = g.world.instances.Where(i => i.state == ItemState.InWorld && i.owner.StartsWith("stage:")).Select(i => i.id).ToList();
                bool Done() => waiting.Any(i => g.world.instances[i].state == ItemState.Stored);
                Time.timeScale = 4;
                float t1 = Time.realtimeSinceStartup, g1 = Time.time;
                // a game-time budget (finish the current load, fly to a zone, store high), so a slow frame rate on
                // Large and Massive doesn't fail it
                while (Time.time - g1 < 200 && Time.realtimeSinceStartup - t1 < 150 && !Done()) yield return null;
                Time.timeScale = 1;
                Check("Hoverbot empties the staging zones and stores those parts high up", Done() && hover.delivered > hd,
                      $"{waiting.Count(i => g.world.instances[i].state == ItemState.Stored)} of {waiting.Count} staged parts stored after {Time.time - g1:0} s game time ({Time.realtimeSinceStartup - t1:0} s real), {hover.Describe()}");
            }
            else Check("Hoverbot empties the staging zones and stores those parts high up", false, "no candidate part");

            // Scenario C: Spiderbot on an assignment that needs climbing
            var shelfPart = g.world.instances.Where(i => Loose(i) && i.Position.y > 0.55f && i.Position.y < spider.ReachHeight - 0.05f && ItemDatabase.Extents(g.world.DefOf(i.id))[0] <= spider.MaxItemSize
                                                                && g.UnitAt(i.Position) != null && !g.Covered(i.id) && spider.CanPick(i.id))   // an uncovered part it would take: covered ones wait
                .OrderBy(i => (i.Position - spider.transform.position).sqrMagnitude).FirstOrDefault();
            if (shelfPart != null)
            {
                spider.Assign(shelfPart.defId, false);
                bool climbed = false;
                Time.timeScale = 4;
                float t1 = Time.realtimeSinceStartup;
                spider.SetBattery(1);
                while (Time.realtimeSinceStartup - t1 < 120 && !(climbed && shelfPart.state != ItemState.InWorld))
                {
                    if (spider.Climbing && !climbed) { climbed = true; Time.timeScale = 1; LookAtRobot(spider, 1.0f, 0.9f, 0.6f); yield return g.Capture(Path.Combine(outDir, "18b_spiderbot_climbing.png"), 1); Time.timeScale = 4; }
                    yield return null;
                }
                Time.timeScale = 1;
                Check("Spiderbot climbs furniture to reach parts of its assignment", climbed, $"{g.world.DefOf(shelfPart.id).displayName} at {shelfPart.Position.y:0.00} m: {shelfPart.state} ({spider.PickHint(shelfPart.id)}); {spider.Describe()}");
            }
            else Check("Spiderbot climbs furniture to reach parts of its assignment", false, "no part on furniture in range");

            // upgrades: better at their jobs, never past their permanent limits
            pr.skillPoints += 60;
            float reach0 = retro.ReachHeight;
            foreach (var grp in g.db.boxes.Where(kv => Json.Num(kv.Value, "elevation_m") <= retro.ReachHeight).GroupBy(kv => Json.Str(kv.Value, "kind")))
                Debug.Log($"[reachstat] before the upgrade, {grp.Key}: {grp.Count()} within the reach rule, {grp.Count(kv => retro.ReachableDest(kv.Key))} physically reachable");
            for (int k = 0; k < 3; k++) g.BuyRobotUpgrade("retro_reach");
            var highBox = g.db.boxes.Keys.FirstOrDefault(High);
            Check("Retrobot's Arm Reach raises its reach, but never to the high shelves", retro.ReachHeight > reach0 && retro.ReachHeight <= retro.LimitOf("reach_height") + 1e-3f && highBox != null && !retro.ReachableDest(highBox),
                  $"{reach0:0.00} -> {retro.ReachHeight:0.00} m (limit {retro.LimitOf("reach_height"):0.00})");
            // the telescopic forearms make the promised reach physical: a drawer the upgrade brings within
            // reach is one the arms can actually put a part into
            var midDrawer = g.db.boxes.Where(kv => Json.Str(kv.Value, "kind") == "drawer" && Json.Num(kv.Value, "elevation_m") > reach0 + 0.02f && Json.Num(kv.Value, "elevation_m") <= retro.ReachHeight).Select(kv => kv.Key).FirstOrDefault();
            Check("Retrobot's telescopic arms reach the drawers the Arm Reach upgrade brings within range", midDrawer != null && retro.ReachableDest(midDrawer), midDrawer == null ? "no such drawer" : $"{midDrawer} at {Json.Num(g.db.boxes[midDrawer], "elevation_m"):0.00} m");
            foreach (var grp in g.db.boxes.Where(kv => Json.Num(kv.Value, "elevation_m") <= retro.ReachHeight).GroupBy(kv => Json.Str(kv.Value, "kind")))
                Debug.Log($"[reachstat] {grp.Key}: {grp.Count()} within the upgraded reach rule, {grp.Count(kv => retro.ReachableDest(kv.Key))} physically reachable by the telescopic arms");
            float uniReach0 = uni.ReachHeight;
            for (int k = 0; k < 3; k++) g.BuyRobotUpgrade("uni_reach");
            g.BuyRobotUpgrade("cat_reach"); g.BuyRobotUpgrade("cat_reach");
            var catR = g.robots.First(r => r.id == "caterpillarbot");
            Check("Unibot and Caterpillarbot get Arm Reach (telescopic arms) within their limits", uni.ReachHeight > uniReach0 && uni.ReachHeight <= uni.LimitOf("reach_height") + 1e-3f && catR.ReachHeight <= catR.LimitOf("reach_height") + 1e-3f && catR.ReachHeight > catR.BaseStat("reach_height"),
                  $"Unibot {uniReach0:0.00} -> {uni.ReachHeight:0.00} m, Caterpillarbot {catR.BaseStat("reach_height"):0.00} -> {catR.ReachHeight:0.00} m");
            g.BuyRobotUpgrade("uni_capacity"); g.BuyRobotUpgrade("uni_capacity");
            Check("Unibot carries more after upgrades but still never stores", uni.Capacity == 3 && !uni.CanDirectStore && !uni.ReachableDest(g.db.boxes.Keys.First()), $"capacity {uni.Capacity}");
            g.BuyRobotUpgrade("retro_speed"); g.BuyRobotUpgrade("retro_speed"); g.BuyRobotUpgrade("retro_speed");
            Check("Retrobot never becomes faster than Unibot", retro.WalkSpeed < uni.BaseStat("walk_speed"), $"{retro.WalkSpeed:0.00} vs {uni.BaseStat("walk_speed"):0.00} m/s");
            g.BuyRobotUpgrade("hover_speed");
            var hovers = g.robots.Where(r => r.type == "hoverbot").ToList();
            Check("Hoverbot upgrades are shared by the whole fleet", hovers.All(h => Mathf.Approximately(h.WalkSpeed, hover.WalkSpeed)) && hover.WalkSpeed > hover.BaseStat("walk_speed"), string.Join(", ", hovers.Select(h => $"{h.displayName} {h.WalkSpeed:0.00}")));

            // 14. a second Hoverbot, crafted from ordinary parts carried to the bench
            var h2 = wb.P("hoverbot_2");
            var r2 = wb.RobotOf(h2);
            Check("Hoverbot #2's project opens once Hoverbot is operational", wb.Unlocked(h2) && wb.OnBench(h2) && r2.gameObject.activeSelf, wb.Summary(h2));
            var consumed = new List<int>();
            foreach (var kv in h2.materials)
            {
                var pick = g.world.instances.Where(i => Loose(i) && g.world.DefOf(i.id).CategoryName == kv.Key).Take(kv.Value).ToList();
                foreach (var i in pick) { Take(i.id); if (g.interaction.DepositStation(i.id)) consumed.Add(i.id); }
                if (consumed.Count > 6 && consumed.Count < 12)
                {
                    var (sp, _) = wb.MatSpot(h2.mat);
                    LookFrom(sp + wb.Front * 0.6f + Vector3.up * 0.3f, sp + Vector3.up * 0.1f);
                    yield return g.Capture(Path.Combine(outDir, "15c_hoverbot2_being_crafted.png"), 3);
                }
            }
            Check("recipe materials are built into Hoverbot #2 (installed, not duplicated)", wb.Complete(h2) && consumed.All(i => g.world.instances[i].state == ItemState.Installed && !g.inventory.items.Contains(i)), wb.Summary(h2));
            // a consumed part still counts for its set, so crafting never makes a set impossible
            int cd = g.world.instances[consumed[0]].defId;
            foreach (var i in g.world.IdsOf(cd).Where(i => g.world.instances[i].state != ItemState.Stored && g.world.instances[i].state != ItemState.Installed).ToList())
            {
                if (g.world.instances[i].state == ItemState.Carried && g.world.instances[i].owner != "player") continue;
                Take(i);
                g.inventory.Remove(i);
                g.storage.Deposit(g.world.DefOf(i).storageBox, i);
            }
            Check("a set with parts used in crafting can still be completed", g.progress.IsComplete(cd), $"{g.db.Get(cd).displayName}: {g.progress.SortedOf(cd)}/{g.world.TotalOf(cd)} accounted");
            wb.Activate(h2);
            float t2 = Time.realtimeSinceStartup;
            while (!r2.Active && Time.realtimeSinceStartup - t2 < 12) yield return null;
            Check("Hoverbot #2 activates with its own identity next to the first", r2.Active && r2.id != hover.id && hover.Active && wb.Unlocked(wb.P("hoverbot_3")) == (pr.level >= wb.P("hoverbot_3").level),
                  $"{r2.displayName} ({r2.id}): {r2.Describe()}");

            // debug mode (unlocks.json debug_mode / --debug): "unlock now" ignores the blueprint level
            var h3 = wb.P("hoverbot_3");
            var r3 = wb.RobotOf(h3);
            int keepLevel = pr.level;
            pr.level = 1;
            string dmsg = Unlocks.DebugMode ? wb.DebugUnlock(h3) : "debug mode off";
            t2 = Time.realtimeSinceStartup;
            while (Unlocks.DebugMode && !r3.Active && Time.realtimeSinceStartup - t2 < 12) yield return null;
            Check("debug mode: the workbench's 'unlock now' builds and starts a robot below its blueprint level", !Unlocks.DebugMode || (r3.Active && wb.Operational(h3)), dmsg);
            pr.level = keepLevel;
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

            // Handful: a part with a same-kind neighbour right next to it (both uncovered: Handful leaves buried copies)
            var withPair = g.world.instances.Where(i => Free(i) && !g.Covered(i.id)).FirstOrDefault(i => g.index.QuerySphere(i.Position, 0.2f).Any(o => o != i.id && g.world.instances[o].defId == i.defId && Free(g.world.instances[o]) && !g.Covered(o)));
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
                int superInTray = g.world.IdsOf(c.defId).Count(i => g.world.instances[i].state == ItemState.InWorld && g.world.instances[i].owner == "tray");
                Check("Supermagnet pulls only exact matches, unsorted, within range (what doesn't fit goes to the sorting tray)", pulled > 0 && got.Count == pulled - g.tools.lastToTray && superInTray >= g.tools.lastToTray && got.All(i => g.world.instances[i].defId == c.defId),
                      $"{pulled} × {g.world.DefOf(c.id).displayName}: {got.Count} to the pockets, {g.tools.lastToTray} to the tray");
            }
            else Check("Supermagnet pulls only exact matches, unsorted, within range", false, "no group found");

            EmptyHands();
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

            EmptyHands();
            // Area Sweep: everything loose around you, up to the limit
            var heap = g.layout.piles.Where(p => p.kind == "heap" && p.surface == 0).Skip(1).First();
            var hc = Shared.Pos(heap.center.x, 0, heap.center.y);
            g.player.Teleport(hc + new Vector3(1.0f, 0, 0));
            g.player.SetView(Mathf.Atan2(1.0f, 0), -0.7f);
            yield return Frames(3);
            int b0 = g.inventory.Count;
            int swept = g.tools.AreaSweep();
            yield return new WaitForSeconds(2.5f);
            Check("Area Sweep collects the loose parts around you (overflow to the sorting tray)", swept > 0 && g.inventory.Count - b0 == swept - g.tools.lastToTray, $"{swept} parts swept, inventory +{g.inventory.Count - b0}, tray +{g.tools.lastToTray} (capacity {g.inventory.capacity}, now {g.inventory.Count})");

            // Automatic Storage Delivery: a robot fetches the player's load and stores it
            // (from open floor by the start: the feature, not a robot squeezing into a heap)
            g.player.Teleport(Shared.Pos(g.layout.spawn.x, 0, g.layout.spawn.z));
            yield return Frames(2);
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
            var zr = g.robots.First(r => r.id == "retrobot");
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

            // Sorting tray: parts put there are picked up by the robots first
            if (g.inventory.Count < 6) { g.player.Teleport(hc + new Vector3(1.0f, 0, 0)); yield return Frames(2); g.tools.AreaSweep(); yield return Frames(5); }
            g.player.Teleport(g.TrayCenter + new Vector3(0, 0, 1.2f));
            int put = 0;
            foreach (var id in g.inventory.items.ToList()) { if (put >= 8) break; if (g.interaction.DropInTray(id, false)) put++; }
            yield return new WaitForSeconds(0.5f);
            int inTray = g.TrayCount;
            LookFrom(g.TrayCenter + new Vector3(0.9f, 1.1f, 1.4f), g.TrayCenter);
            g.interaction.UpdateTarget();   // automated runs don't update the aim every frame
            yield return g.Capture(Path.Combine(outDir, "22_sorting_tray.png"), 3);
            Time.timeScale = 4;
            float t2 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t2 < 60 && g.TrayCount > inTray - 3) yield return null;
            Time.timeScale = 1;
            Check("robots empty the sorting tray first", put > 0 && inTray >= put && g.TrayCount <= inTray - 3, $"{put} put in, {inTray} in tray, {g.TrayCount} left");
        }

        /// <summary>Puts a part into the player's hands, wherever it lies (test setup).</summary>
        void Take(int id)
        {
            var i = g.world.instances[id];
            if (i.state == ItemState.InWorld) { g.index.Remove(id); g.world.SetState(id, ItemState.Carried, "player"); }
            if (i.state == ItemState.Carried && i.owner == "player" && !g.inventory.items.Contains(id)) g.inventory.Add(id);
        }

        /// <summary>Earns real level points (completions of stand-in destinations) until the level is reached.</summary>
        void LevelTo(int level)
        {
            var pr = g.progression;
            for (int k = 0; pr.level < level && k < 5000; k++) pr.OnDefinitionCompleted(-100000 - pr.DestinationsCompleted, 1);
        }

        Progression FreshProgression()
        {
            var pr = new Progression(Shared.ReadObject(Shared.DataPath("progression.json")), g.presetId);
            pr.SetDestinationTotal(g.CompletableDestinations());
            return pr;
        }

        /// <summary>Stands in front of a workbench mat and aims at the bench top there.</summary>
        IEnumerator AimAtBench(int mat)
        {
            var (spot, _) = g.workbench.MatSpot(mat);
            LookFrom(spot + g.workbench.Front * 0.85f + Vector3.up * 0.75f, spot + g.workbench.Front * 0.2f);
            yield return Frames(2);
            g.interaction.UpdateTarget();
        }

        /// <summary>Visible vertex count of a robot model (solid parts only, ghosts excluded).</summary>
        static int SolidVerts(Robot r) => r.ModelTransform.GetComponentsInChildren<MeshFilter>(true)
            .Where(f => f.GetComponent<MeshRenderer>() is MeshRenderer mr && mr.sharedMaterial != null && !mr.sharedMaterial.shader.name.Contains("Ghost")).Sum(f => f.sharedMesh != null ? f.sharedMesh.vertexCount : 0);

        /// <summary>Every unique robot part is in exactly one place: the workshop, a hand,
        /// the holding tray or a robot.</summary>
        bool PartsIntact(out string detail)
        {
            int bad = 0;
            foreach (var id in g.db.RobotPartIds().SelectMany(d => g.world.IdsOf(d)))
            {
                var i = g.world.instances[id];
                int places = (i.state == ItemState.InWorld ? 1 : 0) + (g.inventory.items.Contains(id) ? 1 : 0)
                    + g.storage.contents.Values.Count(l => l.Contains(id)) + (i.state == ItemState.Installed ? 1 : 0)
                    + g.robots.Count(r => r.carried.Contains(id));
                if (places != 1) bad++;
            }
            detail = bad == 0 ? $"{g.db.RobotPartIds().Count} unique parts, each in one place" : $"{bad} parts in the wrong number of places";
            return bad == 0;
        }

        /// <summary>The Robot Workbench acceptance tests (robot_blueprints_and_assembly.md §10)
        /// with Retrobot and Unibot, driven through the same interaction code the player uses.</summary>
        IEnumerator RetrobotChecks()
        {
            var wb = g.workbench;
            var rb = g.retrobot;
            var pr = g.progression;
            var retro = wb.P("retrobot");
            var uniP = wb.P("unibot");
            var parts = wb.PartIds(retro);
            Check("Retrobot has 5 assembly parts", parts.Count == 5, $"{parts.Count} parts");
            Check("Retrobot inactive before assembly", !rb.Active && !rb.Operational, rb.Describe());
            // the parts' discovery XP is earned first, then the player goes back to level 1
            foreach (var id in parts.Concat(wb.PartIds(uniP))) g.progress.Discover(g.world.instances[id].defId);
            pr.level = 1; pr.xp = 0; pr.completedDestinations.Clear();
            wb.CheckBlueprints(false);
            Check("blueprints lock robots behind the player levels in unlocks.json", !wb.Unlocked(retro) && wb.projects.All(p => p.level == Unlocks.Robot(p.id, -1)),
                  string.Join(", ", wb.projects.Select(p => $"{p.name} lv {p.level}")));

            // 1-2. a part found below its blueprint level can be picked up and carried
            int uniPart = wb.PartIds(uniP)[0];
            var up = g.world.instances[uniPart];
            g.player.Teleport(up.Position + new Vector3(0.5f, -up.Position.y, 0.5f));
            bool picked = g.interaction.Pickup(uniPart, false);
            Check("a level-1 player picks up and carries a Unibot part (blueprint at level 5)", picked && g.inventory.items.Contains(uniPart) && wb.StateOf(uniPart) == RobotWorkbench.PartState.Carried,
                  g.world.DefOf(uniPart).displayName);
            // robot helpers never deliver robot parts for you
            Check("the sorting tray refuses robot parts", !g.interaction.DropInTray(uniPart, false), "");

            // 3-4. personally delivered at the bench, it waits safely in the holding tray
            yield return AimAtBench(1);
            g.inventory.Select(up.defId);
            bool aimed = g.interaction.targetType == "station";
            g.interaction.Primary();
            yield return Frames(3);
            Check("the player delivers it in person at the Robot Workbench", aimed && wb.StateOf(uniPart) == RobotWorkbench.PartState.Delivered && !g.inventory.items.Contains(uniPart),
                  $"aim: {g.interaction.targetType}, state {wb.StateOf(uniPart)}");
            // 5. no assembly while the blueprint is locked
            Check("a locked blueprint can't be assembled", wb.InstallAll(uniP) == 0 && wb.InstalledCount(uniP) == 0 && wb.CanInstall(uniP, uniPart).Contains("level 5"), wb.CanInstall(uniP, uniPart));

            // Retrobot: three parts delivered at level 1 wait in the tray
            foreach (var id in parts.Take(3)) { Take(id); g.interaction.DepositStation(id); }
            yield return Frames(2);
            Check("parts delivered before the blueprint wait in the holding tray", wb.DeliveredIds(retro).Count == 3 && wb.InstalledCount(retro) == 0 && wb.StatusOf(retro) == RobotWorkbench.Status.AwaitingBlueprint,
                  wb.Summary(retro));
            yield return AimAtBench(0);
            g.hud.SetHudVisible(true);
            yield return g.Capture(Path.Combine(outDir, "05_workbench_locked.png"), 4);
            g.hud.SetHudVisible(false);

            // 6-7. reaching level 2 unlocks the blueprint; the delivered parts are still there
            LevelTo(2);
            Check("reaching level 2 unlocks Retrobot's blueprint, delivered parts kept", wb.Unlocked(retro) && wb.DeliveredIds(retro).Count == 3 && wb.StatusOf(retro) == RobotWorkbench.Status.ReadyToAssemble,
                  $"level {pr.level}, {RobotWorkbench.StatusText(wb.StatusOf(retro))}");
            // 8. installing visibly changes the robot on the bench
            int v0 = SolidVerts(rb);
            int n = wb.InstallAll(retro);
            yield return Frames(2);
            int v1 = SolidVerts(rb);
            Check("installing parts visibly builds the robot on the bench", n == 3 && v1 > v0, $"{n} installed, solid geometry {v0} -> {v1} vertices");
            // 9-10. the missing parts are named; it can't be activated yet
            var missing = parts.Where(id => wb.StateOf(id) != RobotWorkbench.PartState.Installed).ToList();
            Check("missing components are identified", missing.Count == 2 && wb.Summary(retro).Contains("3 of 5"), wb.Summary(retro));
            Check("an incomplete robot can't be activated", !wb.CanActivate(retro) && !wb.Activate(retro) && !rb.Active, RobotWorkbench.StatusText(wb.StatusOf(retro)));
            Check("each robot part is in exactly one place", PartsIntact(out var d1), d1);
            // the half-built Retrobot, and held in the hands
            var (spot, _) = wb.MatSpot(0);
            LookFrom(spot + wb.Front * 0.55f + Vector3.up * 0.32f + Vector3.Cross(Vector3.up, wb.Front) * 0.2f, spot + Vector3.up * 0.12f);
            yield return g.Capture(Path.Combine(outDir, "05b_retrobot_on_the_bench.png"), 4);
            yield return AimAtBench(0);
            g.interaction.BeginInspect();
            bool held = g.interaction.inspecting && g.interaction.inspectRobot == rb && !g.player.inputEnabled;
            g.held.RotateBy(new Vector2(70, 8));
            g.hud.SetHudVisible(true);
            yield return g.Capture(Path.Combine(outDir, "05c_retrobot_in_hands.png"), 40);
            g.interaction.EndInspect();
            yield return Frames(2);
            g.hud.SetHudVisible(false);
            Check("an unfinished robot can be picked up and turned in the hands, then put back", held && !g.interaction.inspecting && g.player.inputEnabled && rb.ModelTransform.parent == rb.transform,
                  "");
            // 12. partial assembly survives save / load
            string Bench() => $"{string.Join(",", wb.InstalledIds(retro))}|{string.Join(",", wb.DeliveredIds(uniP))}|{SolidVerts(rb)}|{pr.level}";
            var wbBefore = Bench();
            g.SaveGame("autotest_wb");
            g.StartWorld(g.world.seed, null);
            g.LoadGame("autotest_wb");
            g.player.enabled = false;
            yield return Frames(4);
            rb = g.retrobot;
            var wbAfter = Bench();
            Check("partial assembly and the holding tray survive save/load", wbBefore == wbAfter && PartsIntact(out _), $"{wbBefore}  vs  {wbAfter}");
            File.Delete(SaveSystem.PathOf("autotest_wb"));
            // 13. a part can't be delivered twice
            var again = g.interaction.DepositStation(parts[0]);
            bool intact = PartsIntact(out var d2);
            Check("an installed part can't be delivered again (no duplicates)", !again && intact, d2);

            // the last two parts, found in the workshop: installed as they are delivered (unlocked)
            foreach (var id in missing) { Take(id); g.interaction.DepositStation(id); }
            yield return Frames(2);
            Check("with the blueprint unlocked, a delivered part is installed at once", wb.Complete(retro) && wb.StatusOf(retro) == RobotWorkbench.Status.ReadyForActivation, wb.Summary(retro));
            g.OpenWorkbench("retrobot");
            yield return g.Capture(Path.Combine(outDir, "05d_workbench_screen.png"), 8);
            {
                // playtest 2026-10-25: the options flashed every second (all buttons rebuilt by the refresh)
                var before = g.hud.WorkbenchButtons();
                float tw = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - tw < 2.5f) yield return null;
                var after = g.hud.WorkbenchButtons();
                Check("the open workbench screen keeps its buttons through its refresh (no flashing)", before.Count > 0 && before.SequenceEqual(after) && after.All(b => b != null),
                      $"{before.Count} buttons before, {after.Count} after, {after.Count(b => before.Contains(b))} the same");
            }
            g.SetState("play");
            g.player.enabled = false;
            // 11. activation: it hops off the bench and starts work
            Check("a complete robot is activated by the player", wb.Activate(retro), "");
            float t0 = Time.realtimeSinceStartup;
            while (!rb.Active && Time.realtimeSinceStartup - t0 < 12) { LookAtRobot(1.0f, 0.9f, 0.4f); yield return null; }
            Check("Retrobot activates, leaves the bench and is managed with the others", rb.Active && rb.Operational && (rb.transform.position - rb.home).magnitude < 0.3f && wb.StatusOf(retro) == RobotWorkbench.Status.Operational,
                  rb.Describe());
            yield return new WaitForSeconds(1.2f);
            Check("objectives advance with play (first part, sorting, Retrobot)", g.objectives.done.Contains("first_part") && g.objectives.done.Contains("sort_10") && g.objectives.done.Contains("retrobot_parts") && g.objectives.done.Contains("retrobot_on"),
                  $"done: {string.Join(", ", g.objectives.done)}; now: {g.objectives.HudText()}");
            LookAtRobot(0.7f, 0.45f, 0.15f);
            yield return g.Capture(Path.Combine(outDir, "06_retrobot_online.png"), 3);

            // 15. every preset can reach every blueprint level from completed destinations
            var cfgRobots = Robot.AllConfig();
            int maxBp = wb.projects.Where(p => !p.crafted).Max(p => p.level), maxCraft = wb.projects.Max(p => p.level);
            int dests = g.CompletableDestinations();
            Check("every blueprint level is reachable by completing destinations (robots by 75% of them, crafted Hoverbots and level 25 by 85%)",
                  pr.DestinationsForLevel(maxBp) <= dests * 0.75 && pr.DestinationsForLevel(maxCraft) <= dests * 0.85 && pr.DestinationsForLevel(pr.maxLevel) <= dests * 0.85,
                  $"{g.presetId}: {dests} destinations; level {maxBp} at {pr.DestinationsForLevel(maxBp)}, level {maxCraft} at {pr.DestinationsForLevel(maxCraft)}, level {pr.maxLevel} at {pr.DestinationsForLevel(pr.maxLevel)}");

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
            Check("Retrobot sorts parts on his own, one at a time", rb.delivered >= 1 && rb.delivered + rb.staged >= 4 && sortedNow == rb.delivered && rb.Capacity == 1, $"{rb.delivered} stored, {rb.staged} piled below high destinations, {sortedNow} newly sorted");
            Check("parts for destinations above his reach are piled in front of them, still unsorted",
                  rb.staged == 0 || rb.IsPiledBelowDestination(rb.lastStagedId), rb.staged == 0 ? "none needed yet" : $"part {rb.lastStagedId} waits below {g.world.DefOf(rb.lastStagedId).storageBox}");
            Check("robot sorting is recorded as robot-assisted", g.telemetry.sortedRobot - robotBefore == rb.delivered, $"{g.telemetry.sortedRobot - robotBefore} robot sorts");
            Check("Retrobot's claws reach the part when he can stand beside it (no grabbing at thin air)", rb.pickCount >= 1 && rb.maxPickGapInRange <= 0.05f, $"{rb.pickCount} pick-ups, largest claw-to-part gap {rb.maxPickGapInRange * 100:0.0} cm where he could stand beside it and it lies within his arms' range ({rb.outOfRange} interactions out of range or blocked by piles; largest gap overall {rb.maxGripGap * 100:0.0} cm)");
            Check("Retrobot reaches into the drawers he fills", rb.drawerStoreCount == 0 || rb.maxDrawerGapInRange <= 0.05f, rb.drawerStoreCount == 0 ? "no drawer store in this run" : $"{rb.drawerStoreCount} drawer stores, largest claw-to-drawer gap {rb.maxDrawerGapInRange * 100:0.0} cm within his arms' range (overall {rb.maxDrawerGap * 100:0.0} cm; all stores {rb.storeCount}, largest {rb.maxStoreGap * 100:0.0} cm incl. deep shelf containers)");
            Check("Retrobot sets staged parts down where his claws are", rb.stageCount == 0 || rb.maxStageGapInRange <= 0.05f, rb.stageCount == 0 ? "no staging in this run" : $"{rb.stageCount} staged, largest claw-to-spot gap {rb.maxStageGapInRange * 100:0.0} cm within his arms' range (overall {rb.maxStageGap * 100:0.0} cm)");
            Check("the final alignment before a grip or release never stalls (bounded at 1.6 s)", rb.maxAlignTime <= 1.7f, $"longest {rb.maxAlignTime:0.00} s");
            Check("Retrobot's grippers are open when they reach a part", rb.pickCount >= 1 && rb.minOpenAtContact >= 0.95f, $"lowest opening at contact {rb.minOpenAtContact:0.00} over {rb.pickCount} pick-ups");
            Check("the grippers close on the part before it is lifted", rb.pickCount >= 1 && rb.minClosedAtAttach >= 0.99f, $"lowest closure at the grip {rb.minClosedAtAttach:0.00}");
            Check("the grippers open before a part is let go (storing or staging)", rb.releases >= 1 && rb.minOpenAtRelease >= 0.99f, $"{rb.releases} releases, lowest opening {rb.minOpenAtRelease:0.00}");
            Check("a carried part stays in the grippers while he walks and turns", rb.carrySyncMax <= 0.01f, $"largest gap {rb.carrySyncMax * 100:0.0} cm");
            Check("no part is drawn into the claws from afar: every grip within 5 cm, the rest deferred", rb.maxGripGap <= 0.05f, $"largest grip gap {rb.maxGripGap * 100:0.0} cm, {rb.deferred} parts deferred");
            Check("every direct store is within his arms' reach (deeper containers are staged instead)", rb.storeCount == 0 || rb.maxStoreGap <= 0.05f, rb.storeCount == 0 ? "no direct store in this run" : $"{rb.storeCount} stored, largest claw-to-target gap {rb.maxStoreGap * 100:0.0} cm");

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

        /// <summary>Sorting Arm vertical slice (docs/ROBOT_WORKSHOP_ASSET_AUDIT.md §5): one overhead
        /// rail mount per drawer cabinet and shelving unit, the full-path reach / clearance QA,
        /// player access around a parked arm, sorting at a cabinet and at a shelf, relocation both
        /// ways, save/load at both. The mobile robots are paused so only the arm touches the test parts.</summary>
        IEnumerator ArmChecks()
        {
            var wb = g.workbench;
            var cabinets = g.units.Values.Where(u => u.type == "drawer_cabinet").ToList();
            var shelves = g.units.Values.Where(u => u.type == "shelving").ToList();
            bool CrateUnderDeck(Dictionary<string, object> d) => Json.Str(d, "kind") == "wooden_crate" && Json.Int(d, "level") < Json.Arr(g.units[Json.Str(d, "unit")].unit["levels"]).Count - 1;
            bool layoutOk = cabinets.Concat(shelves).All(u =>
            {
                var mine = g.sockets.Values.Where(s => s.unit == u.id).ToList();
                var expect = u.dests.Where(d => !CrateUnderDeck(d)).Select(d => Json.Str(d, "id")).ToHashSet();
                return mine.Count == 1 && mine[0].dests.ToHashSet().SetEquals(expect) && (u.type != "drawer_cabinet" || expect.Count == 36);
            }) && g.sockets.Count == cabinets.Count + shelves.Count;
            int crates = shelves.Sum(u => u.dests.Count(CrateUnderDeck));
            Check("one overhead rail mount per drawer cabinet (all 36 drawers) and per shelving unit (all its containers but crates under a deck)", layoutOk,
                  $"{g.sockets.Count} mounts on {cabinets.Count} cabinets + {shelves.Count} shelves; {crates} crates under a deck left out (9.5 cm under the next beam)");
            bool noColliders = g.sockets.Values.All(s => s.GetComponentsInChildren<Collider>().All(c => c.isTrigger));
            Check("the mounts add no solid colliders (only the aim trigger on the carriage)", noColliders, "");
            var p = wb.P("sorting_arm");
            var bot = wb.RobotOf(p);
            var arm = bot?.arm;
            if (arm == null) { Check("the Sorting Arm exists as the sixth workbench project", false, ""); yield break; }
            Check("the Sorting Arm is the sixth workbench project with 12 unique parts", RobotWorkbench.Types.Length == 6 && wb.Required(p) == 12 && p.level == Unlocks.Robot("sorting_arm"),
                  $"{wb.Required(p)} parts, blueprint level {p.level}");
            var paused = g.robots.Where(r => r.Mobile && r.Active).ToList();
            foreach (var r in paused) r.paused = true;
            wb.ForceInstall(p);
            if (!wb.Activate(p)) wb.DebugUnlock(p);   // the arm-only section starts below the blueprint level
            float t0 = Time.realtimeSinceStartup;
            while (!bot.Operational && Time.realtimeSinceStartup - t0 < 15) yield return null;
            Check("an activated arm waits folded at the workbench (it never roams)", bot.Operational && arm.place == SortingArm.Place.Bench && (bot.transform.position - bot.home).magnitude < 0.05f, arm.Describe());
            if (!bot.Operational) { foreach (var r in paused) r.paused = false; yield break; }

            // carry: two-handed, no pocket slot, no pickups meanwhile
            int pockets = g.inventory.items.Count;
            arm.PickUp();
            var loose = g.world.instances.First(i => i.state == ItemState.InWorld && !g.world.DefOf(i.id).IsRobotPart);
            bool refused = !g.interaction.Pickup(loose.id, false);
            Check("the arm is carried in both hands: no inventory slot, no part pickups", g.CarriedArm == arm && g.inventory.items.Count == pockets && refused, arm.Describe());

            int total = g.world.instances.Count;
            // ---- cabinet
            var sk = g.sockets.Values.Where(s => s.cabinet).OrderBy(s => s.id).First();
            // the nearest shelf holding parts the arm can carry (large-parts shelves hold none: it leaves those alone)
            bool Carryable(string b) => g.StraysIn(b).Count == 0 && g.world.instances.Any(i => i.state is ItemState.InWorld or ItemState.Stored
                && g.world.DefOf(i.id) is var d && d.storageBox == b && d.robotCarry && !d.IsRobotPart && ItemDatabase.Extents(d)[0] <= bot.MaxItemSize);
            var shelfMounts = g.sockets.Values.Where(s => !s.cabinet).OrderBy(s => (s.su.transform.position - sk.su.transform.position).sqrMagnitude).ToList();
            var shelfSk = shelfMounts.FirstOrDefault(s => s.dests.Count(b => Carryable(b)) >= 4) ?? shelfMounts.First();
            string why = arm.Mount(sk);
            Check($"the arm mounts on {sk.id} and serves all 36 drawers of that cabinet, nothing else", why == "" && arm.place == SortingArm.Place.Mounted && sk.occupant == arm && arm.Served.SetEquals(sk.dests) && sk.dests.Count == 36
                  && sk.dests.All(g.ArmServes) && !shelfSk.dests.Any(g.ArmServes), why == "" ? arm.Describe() : why);
            var rep = arm.ReachTest();
            Check("cabinet: the whole cycle path (travel, pickup, transport, insert, release, withdraw, park) reaches all 36 drawers and the piles with clearance", ReachOk(rep), rep.ToString());
            bot.paused = true;
            arm.ParkNow();
            yield return Frames(3);
            yield return ArmAccess(arm, sk);
            bot.paused = false;
            g.hud.SetHudVisible(false);
            {
                var t = sk.su.transform;
                var c = t.TransformPoint(Batch.G(0, 1.5f, sk.su.size.z / 2));
                LookFrom(c + sk.Front * 3.0f + t.TransformDirection(Vector3.right) * 0.7f + Vector3.up * 0.1f, c + Vector3.up * 0.8f);
                yield return g.Capture(Path.Combine(outDir, "17a_arm_cabinet_installation.png"), 2);
                LookFrom(c + sk.Front * 2.4f + Vector3.up * 0.15f, c + Vector3.up * 0.2f);
                yield return g.Capture(Path.Combine(outDir, "17b_arm_parked_fronts_clear.png"), 2);
                // the lowest drawer farthest from the parking end
                var far = sk.dests.Where(b => Json.Int(g.db.boxes[b], "row") == 5).OrderByDescending(b => Mathf.Abs(sk.su.FrontCenterG(sk.su.SlotOf(b)).x - sk.parkX)).First();
                bot.paused = true;
                arm.QaPose(far, "insert");
                yield return Frames(12);
                var fp = sk.su.DestPoint(g.db.boxes[far]);
                LookFrom(fp + sk.Front * 2.2f + t.TransformDirection(Vector3.right) * (sk.su.FrontCenterG(sk.su.SlotOf(far)).x > 0 ? -1.2f : 1.2f) + Vector3.up * 1.0f, fp + Vector3.up * 0.4f);
                yield return g.Capture(Path.Combine(outDir, "17c_arm_lowest_farthest_drawer.png"), 2);
                arm.ParkNow();
                bot.paused = false;
            }
            g.hud.SetHudVisible(true);
            // sort at the cabinet: corner drawers first, then the centre, plus a part for the shelf it must leave alone
            var corners = sk.dests.Where(b => { var d = g.db.boxes[b]; int r = Json.Int(d, "row"), c = Json.Int(d, "col"); return (r == 0 || r == 5) && (c == 0 || c == 5); })
                .Concat(sk.dests.Where(b => { var d = g.db.boxes[b]; return Json.Int(d, "row") is 2 or 3 && Json.Int(d, "col") is 2 or 3; })).Concat(sk.dests).Distinct().ToList();
            var foreign = PartsFor(shelfSk.dests, bot, 1);
            yield return ArmSort(arm, bot, PartsFor(corners, bot, 4), foreign, "cabinet (corner and centre drawers)");

            // ---- relocation cabinet -> shelf
            var armObj = arm.gameObject;
            arm.PickUp();
            t0 = Time.realtimeSinceStartup;
            while (arm.place != SortingArm.Place.Carried && Time.realtimeSinceStartup - t0 < 20) yield return null;
            why = arm.Mount(shelfSk);
            Check($"relocation cabinet → shelf: the same arm mounts on {shelfSk.id}, serves that shelf and no longer the cabinet", why == "" && arm.gameObject == armObj && g.sockets.Values.Count(s => s.occupant != null) == 1
                  && sk.occupant == null && shelfSk.occupant == arm && arm.Served.SetEquals(shelfSk.dests) && shelfSk.dests.All(g.ArmServes) && !sk.dests.Any(g.ArmServes), why == "" ? arm.Describe() : why);
            rep = arm.ReachTest();
            Check($"shelf: the whole cycle path reaches all {shelfSk.dests.Count} containers on every level and the piles with clearance", ReachOk(rep), rep.ToString());
            bot.paused = true;
            arm.ParkNow();
            yield return Frames(3);
            yield return ArmAccess(arm, shelfSk);
            g.hud.SetHudVisible(false);
            {
                var t = shelfSk.su.transform;
                var c = t.TransformPoint(Batch.G(0, 1.6f, shelfSk.su.size.z / 2));
                LookFrom(c + shelfSk.Front * 2.0f + t.TransformDirection(Vector3.right) * 0.6f + Vector3.up * 0.05f, c + Vector3.up * 0.9f);
                yield return g.Capture(Path.Combine(outDir, "17d_arm_shelf_installation.png"), 2);
                // working: level over a mid-level container
                var mid = shelfSk.dests.OrderBy(b => Mathf.Abs(Json.Int(g.db.boxes[b], "level") - 2)).ThenBy(b => Mathf.Abs(shelfSk.su.transform.InverseTransformPoint(shelfSk.su.DestPoint(g.db.boxes[b])).x)).First();
                arm.QaPose(mid, "insert");
                yield return Frames(3);
                var mp = arm.CarryPoint(mid);
                LookFrom(mp + shelfSk.Front * 1.9f + t.TransformDirection(Vector3.right) * 0.9f + Vector3.up * 0.2f, mp + Vector3.up * 0.5f);
                yield return g.Capture(Path.Combine(outDir, "17e_arm_working_at_shelf.png"), 2);
                arm.ParkNow();
            }
            g.hud.SetHudVisible(true);
            bot.paused = false;
            var levels = shelfSk.dests.GroupBy(b => Json.Int(g.db.boxes[b], "level")).OrderByDescending(gr => gr.Key).SelectMany(gr => gr.Take(2)).Concat(shelfSk.dests).Distinct().ToList();
            yield return ArmSort(arm, bot, PartsFor(levels, bot, 4), PartsFor(sk.dests, bot, 1), "shelf (top, middle and bottom levels)");

            // save / load at the shelf
            g.SaveGame("autotest_arm");
            g.StartWorld(g.world.seed, null);
            g.LoadGame("autotest_arm");
            yield return Frames(5);
            bot = g.workbench.RobotOf(g.workbench.P("sorting_arm"));
            arm = bot.arm;
            sk = g.sockets[sk.id]; shelfSk = g.sockets[shelfSk.id];
            Check("save/load at a shelf: the arm comes back mounted on the same shelf mount, parked, serving it", arm.place == SortingArm.Place.Mounted && arm.socket == shelfSk && shelfSk.occupant == arm
                  && arm.Served.SetEquals(shelfSk.dests) && Mathf.Abs(shelfSk.cx - shelfSk.parkX) < 0.01f && Mathf.Abs(shelfSk.cH - shelfSk.hPark) < 0.01f, arm.Describe());

            // ---- relocation shelf -> cabinet
            armObj = arm.gameObject;
            arm.PickUp();
            t0 = Time.realtimeSinceStartup;
            while (arm.place != SortingArm.Place.Carried && Time.realtimeSinceStartup - t0 < 20) yield return null;
            why = arm.Mount(sk);
            Check($"relocation shelf → cabinet: the same arm mounts back on {sk.id}, serves the cabinet and no longer the shelf", why == "" && arm.gameObject == armObj && shelfSk.occupant == null && sk.occupant == arm
                  && arm.Served.SetEquals(sk.dests) && !shelfSk.dests.Any(g.ArmServes), why == "" ? arm.Describe() : why);
            yield return ArmSort(arm, bot, PartsFor(sk.dests.AsEnumerable().Reverse().ToList(), bot, 2), new List<int>(), "cabinet after relocation");
            g.SaveGame("autotest_arm");
            g.StartWorld(g.world.seed, null);
            g.LoadGame("autotest_arm");
            yield return Frames(5);
            bot = g.workbench.RobotOf(g.workbench.P("sorting_arm"));
            arm = bot.arm;
            Check("save/load at a cabinet: the arm comes back mounted on the same cabinet mount", arm.place == SortingArm.Place.Mounted && arm.socket != null && arm.socket.id == sk.id && g.sockets[sk.id].occupant == arm
                  && arm.Served.SetEquals(g.sockets[sk.id].dests), arm.Describe());
            arm.PickUp();   // a busy arm finishes its part first, then comes into the hands
            float tw = Time.realtimeSinceStartup;
            while (arm.place != SortingArm.Place.Carried && Time.realtimeSinceStartup - tw < 20) yield return null;
            g.SaveGame("autotest_arm");
            g.StartWorld(g.world.seed, null);
            g.LoadGame("autotest_arm");
            yield return Frames(5);
            bot = g.workbench.RobotOf(g.workbench.P("sorting_arm"));
            Check("an arm saved while carried is set down at the workbench on load (nothing lost)", bot.arm.place == SortingArm.Place.Bench && g.sockets.Values.All(s => s.occupant == null) && g.CarriedArm == null
                  && g.world.instances.Count == total, bot.arm.Describe());
            File.Delete(SaveSystem.PathOf("autotest_arm"));
            foreach (var r in g.robots.Where(r => r.Mobile)) r.paused = false;
        }

        static bool ReachOk(SortingArm.ReachReport r) => r.targets > 0 && r.worstErr < 0.01f && r.mountClear > 0.01f && r.furnClear > 0.005f && r.maxJump < 25;

        /// <summary>Up to n loose parts (one per destination, in the given order) the arm can carry.</summary>
        List<int> PartsFor(List<string> boxes, Robot bot, int n)
        {
            var ids = new List<int>();
            bool Fits(int i) { var d = g.world.DefOf(i); return d.robotCarry && !d.IsRobotPart && ItemDatabase.Extents(d)[0] <= bot.MaxItemSize; }
            foreach (var box in boxes)
            {
                if (g.StraysIn(box).Count > 0) continue;
                var id = g.world.instances.Where(i => i.state == ItemState.InWorld && !ids.Contains(i.id)).Select(i => i.id)
                    .Where(i => g.world.DefOf(i).storageBox == box && Fits(i)).DefaultIfEmpty(-1).First();
                if (id < 0)
                {
                    // no loose part of this destination left: take one out of it, as the player would
                    id = g.storage.Stored(box).Where(Fits).DefaultIfEmpty(-1).First();
                    if (id >= 0) g.storage.Remove(box, id);
                }
                else if (g.storage.Stored(box).Count >= g.db.BoxCapacity(box)) continue;
                if (id >= 0) ids.Add(id);
                if (ids.Count == n) break;
            }
            return ids;
        }

        /// <summary>Stages the parts (and the foreign ones, for a unit it does not serve) and lets the
        /// mounted arm sort: each part stored exactly once in its own destination, none lost or
        /// duplicated, the foreign parts left where they lie.</summary>
        IEnumerator ArmSort(SortingArm arm, Robot bot, List<int> mine, List<int> foreign, string what)
        {
            foreach (var id in mine.Concat(foreign)) StageAt(id, g.world.DefOf(id).storageBox, bot);
            var foreignAt = foreign.ToDictionary(i => i, i => g.world.instances[i].Position);
            int total = g.world.instances.Count, deliveredBefore = bot.delivered, sortedBefore = g.progress.sortedTotal;
            long xpBefore = g.progression.xp;
            Time.timeScale = 3;
            float t0 = Time.realtimeSinceStartup;
            float maxJump = 0;
            Vector3? last = null;
            while (mine.Any(i => g.world.instances[i].state != ItemState.Stored) && Time.realtimeSinceStartup - t0 < 90)
            {
                // a part in the claws stays with the claws (no jumps of the held model)
                if (arm.heldId >= 0) { var gp = arm.GetComponentInChildren<SortingArmModel>().GripPoint; if (last.HasValue) maxJump = Mathf.Max(maxJump, (gp - last.Value).magnitude); last = gp; }
                else last = null;
                yield return null;
            }
            float wait = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - wait < 3) yield return null;   // time to (wrongly) touch a foreign part
            Time.timeScale = 1;
            bool once = mine.All(i => g.world.instances[i].state == ItemState.Stored && g.storage.Stored(g.world.DefOf(i).storageBox).Count(x => x == i) == 1
                                      && g.db.boxes.Keys.Count(b => g.storage.Stored(b).Contains(i)) == 1);
            bool untouched = foreign.All(i => g.world.instances[i].state == ItemState.InWorld && (g.world.instances[i].Position - foreignAt[i]).magnitude < 0.01f);
            Check($"the arm sorts staged parts at the {what} through normal storage: each stored once, nothing lost, other units' parts untouched",
                  mine.Count >= 2 && once && untouched && g.world.instances.Count == total && bot.delivered - deliveredBefore >= mine.Count && g.progress.sortedTotal - sortedBefore >= mine.Count && g.progression.xp > xpBefore && maxJump < 0.25f,
                  $"{mine.Count(i => g.world.instances[i].state == ItemState.Stored)}/{mine.Count} stored ({string.Join(", ", mine.Select(i => g.world.DefOf(i).storageBox))}), delivered {bot.delivered - deliveredBefore}, " +
                  $"{foreign.Count} foreign untouched {untouched}, largest held-part step {maxJump * 100:0.0} cm/frame");
        }

        /// <summary>A parked arm leaves the unit usable: no visible part of the arm or its mount inside
        /// the space in front of the unit (up to its top, 0.9 m out), no sight line from the player's
        /// eye to any destination crossing it, every destination can be aimed at, and (cabinet) every
        /// drawer opens and closes.</summary>
        IEnumerator ArmAccess(SortingArm arm, ArmSocket sk)
        {
            var su = sk.su;
            float w = su.size.x, d = su.size.z;
            var vol = Obb.In(su.transform, new Vector3(-w / 2, 0, d / 2 - 0.02f), new Vector3(w / 2, sk.top, d / 2 + 0.9f));
            var parts = arm.VisibleParts().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
            float gap = parts.Min(r => Obb.Gap(Obb.World(r.bounds.min, r.bounds.max), vol));
            float low = parts.Min(r => r.bounds.min.y);
            // baseline: what the aiming helper reaches with the arm and mount out of the way (their
            // colliders off); the parked arm must not take any of it away
            var cols = arm.GetComponentsInChildren<Collider>().Concat(sk.GetComponentsInChildren<Collider>()).Where(c => c.enabled).ToList();
            foreach (var c in cols) c.enabled = false;
            var baseline = new HashSet<string>();
            bool hit = false;
            IEnumerator Aim(string dest)   // the helper settles the camera over frames: a second try before calling it a miss
            {
                hit = false;
                for (int k = 0; k < 2 && !hit; k++)
                {
                    yield return AimAtDest(dest);
                    g.interaction.UpdateTarget();
                    hit = g.interaction.targetType == "dest" && g.interaction.targetDest == dest;
                }
            }
            foreach (var dest in sk.dests)
            {
                yield return Aim(dest);
                if (hit) baseline.Add(dest);
            }
            foreach (var c in cols) c.enabled = true;
            int blocked = 0, aimed = 0, opened = 0;
            string bad = "";
            bool closed = true;
            var missed = new List<string>();
            foreach (var dest in sk.dests)
            {
                yield return Aim(dest);
                var cam = g.player.cam.transform;
                var ray = new Ray(cam.position, cam.forward);
                if (parts.Any(r => r.bounds.IntersectRay(ray, out float dist) && dist < 1.3f)) { blocked++; if (bad == "") bad = dest + " hidden"; }
                // the first thing the aim ray meets (triggers included) is never the arm or its mount
                var first = Physics.RaycastAll(ray, 4f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance).Select(h => h.collider).FirstOrDefault();
                if (first != null && cols.Contains(first)) { blocked++; if (bad.Length < 160) bad += $"{dest}: aim caught by {first.name}; "; }
                if (hit)
                {
                    aimed++;
                    if (sk.cabinet && g.inventory.HeldId() < 0)
                    {
                        g.interaction.Primary();
                        if (su.OpenCount > 0) opened++;
                        // let it close before the next one (an open drawer would sit in the next aim)
                        Time.timeScale = 4;
                        float tc = Time.realtimeSinceStartup;
                        while (su.OpenCount > 0 && Time.realtimeSinceStartup - tc < 4) yield return null;
                        Time.timeScale = 1;
                        closed &= su.OpenCount == 0;
                    }
                }
                else if (baseline.Contains(dest)) missed.Add(dest);
            }
            int n = sk.dests.Count, nb = baseline.Count;
            Check($"{sk.unit}: the parked arm and its mount leave the front clear — every {(sk.cabinet ? "drawer" : "container")} visible and aimable{(sk.cabinet ? ", opens and closes" : "")}",
                  gap > 0 && blocked == 0 && (!sk.cabinet || (opened == aimed && closed)),
                  $"lowest visible part {low:0.00} m (unit top {sk.top:0.00} m), gap to the access space {gap * 100:0.0} cm; {n - blocked}/{n} sight lines clear, {aimed}/{n} aimed with the arm parked ({nb}/{n} without arm and mount: the aiming helper's own limit)" +
                  (sk.cabinet ? $", {opened}/{aimed} opened, all closed again {closed}" : "") + (missed.Count > 0 ? $"; helper misses with the arm (not caught by it): {string.Join(" ", missed)}" : "") + (bad != "" ? $"; problems: {bad}" : ""));
        }

        /// <summary>Puts a part on the staging pile of its destination (as a robot would).</summary>
        void StageAt(int id, string box, Robot r)
        {
            var spot = r.StagingSpot(box).Value;
            var def = g.world.DefOf(id);
            var e = ItemDatabase.Extents(def);
            g.index.Remove(id);
            g.world.SetTransform(id, Matrix4x4.TRS(spot + Vector3.up * (e[2] * 0.5f), Quaternion.identity, Vector3.one * def.worldScale));
            g.world.SetState(id, ItemState.InWorld, "stage:" + box);
            g.index.Insert(id);
        }

        /// <summary>Parts never end up where nobody can reach them (IMPLEMENTATION_AUDIT P1-3).</summary>
        IEnumerator ReachChecks()
        {
            var bad = g.UnreachableParts();
            Check("integrity: every unsorted part can be reached (after robots, drops and settling)", bad.Count == 0,
                  bad.Count == 0 ? "" : string.Join("; ", bad.Take(5).Select(b => $"{g.world.DefOf(b.id).displayName}: {b.why}")));
            // a drop aimed at the top of tall shelving lands on open floor or a low deck instead
            var shelf = g.units.Values.FirstOrDefault(u => u.type == "shelving");
            var part = g.world.instances.FirstOrDefault(i => i.state == ItemState.InWorld && !g.world.DefOf(i.id).IsRobotPart && !g.robotReserved.Contains(i.id) && !g.interaction.busy.Contains(i.id));
            if (shelf != null && part != null)
            {
                var front = shelf.transform.TransformDirection(new Vector3(0, 0, -1));
                front = new Vector3(front.x, 0, front.z).normalized;
                var top = shelf.transform.position + Vector3.up * 2.7f;
                LookFrom(shelf.transform.position + front * 1.1f + Vector3.up * PlayerController.Eye, top);
                Take(part.id);
                g.inventory.Select(g.world.DefOf(part.id).id);
                g.interaction.DropHeld();
                yield return new WaitForSeconds(0.5f);
                for (float t = 0; t < 10 && g.piles.Settling; t += Time.deltaTime) yield return null;
                var p = g.world.instances[part.id].Position;
                Check("a part dropped at the top of tall shelving lands within reach (floor or a low deck)", g.world.instances[part.id].state == ItemState.InWorld && p.y < Game.MaxDropDeck + 0.4f && !g.UnreachableParts().Any(b => b.id == part.id),
                      $"landed at {p.y:0.00} m");
                // the safety net: a part forced onto a unit's top is found and recovered into the tray
                g.index.Remove(part.id);
                g.world.SetTransform(part.id, Matrix4x4.TRS(top, Quaternion.identity, Vector3.one * g.world.DefOf(part.id).worldScale));
                g.index.Insert(part.id);
                bool found = g.UnreachableParts().Any(b => b.id == part.id);
                int moved = g.RecoverUnreachable();
                Check("unreachable parts are detected and 'Recover unreachable parts' moves them to the sorting tray", found && moved >= 1 && g.InTray(g.world.instances[part.id].Position) && g.UnreachableParts().Count == 0,
                      $"found {found}, moved {moved}");
            }
        }

        /// <summary>Parts dropped on one spot pile up: no two share space (playtest 2026-10-25).</summary>
        IEnumerator PileChecks()
        {
            // a new world: the generated heaps have fallen into place, nothing hangs in the air
            for (float t = 0; t < 15 && g.piles.Settling; t += Time.deltaTime) yield return null;
            var loose = g.world.instances.Where(i => i.state == ItemState.InWorld && !g.InTray(i.Position) && i.Position.y < Game.MaxDropDeck + 0.4f).Select(i => i.id).ToList();
            var sample = loose.Where((id, k) => k % 8 == 0).ToList();
            var air = new List<string>();
            float deep0 = 0;
            foreach (var id in sample)
            {
                var (supported, deep) = g.piles.Contact(id);
                if (!supported) air.Add($"{g.world.DefOf(id).displayName} at {g.world.instances[id].Position}");
                if (deep > 0.01f) Debug.Log($"[piles] start overlap {deep * 1000:0} mm: {g.piles.lastContact}");
                deep0 = Mathf.Max(deep0, deep);
            }
            foreach (var a in air.Take(10)) Debug.Log("[piles] in the air at the start: " + a);
            Check("a new world's parts all rest on something (nothing in mid-air at the start)", air.Count == 0,
                  $"{sample.Count} of {loose.Count} loose parts checked, in the air: {air.Count}{(air.Count > 0 ? " (" + string.Join("; ", air.Take(3)) + ")" : "")}; deepest overlap {deep0 * 1000:0.0} mm; {g.piles.lastInfo}");
            var hs = g.layout.piles.First(p => p.kind == "heap" && p.surface == 0);
            var hc = new Vector3(hs.center.x, 0.3f, hs.center.y);
            var toRoom = new Vector3(-hc.x, 0, -hc.z).normalized;
            LookFrom(hc + toRoom * 1.3f + Vector3.up * 0.9f, hc + Vector3.down * 0.3f);
            yield return g.Capture(Path.Combine(outDir, "07d_start_heap.png"), 3);

            var parts = g.world.instances.Where(i => i.state == ItemState.InWorld && !g.world.DefOf(i.id).IsRobotPart && !g.robotReserved.Contains(i.id) && !g.interaction.busy.Contains(i.id))
                .OrderBy(i => (i.Position - g.player.transform.position).sqrMagnitude).Take(8).Select(i => i.id).ToList();
            var eye = g.player.transform.position + Vector3.up * PlayerController.Eye;
            var fwd = g.player.transform.forward; fwd = new Vector3(fwd.x, 0, fwd.z).normalized;
            LookFrom(eye, eye + fwd * 1.0f + Vector3.down * PlayerController.Eye);
            foreach (var id in parts)
            {
                Take(id);
                g.inventory.Select(g.world.DefOf(id).id);
                g.interaction.DropHeld();
                yield return new WaitForSeconds(0.3f);
            }
            for (float t = 0; t < 12 && g.piles.Settling; t += Time.deltaTime) yield return null;
            string settle = g.piles.lastInfo;
            foreach (var id in parts) Debug.Log($"[piles] final {g.world.DefOf(id).displayName} {g.world.instances[id].state} {g.world.instances[id].Position}");
            // no air: each part touches what it rests on; nothing sits inside another part
            var floating = new List<string>();
            float deepest = 0;
            foreach (var id in parts)
            {
                var (supported, deep) = g.piles.Contact(id);
                if (!supported) floating.Add($"{g.world.DefOf(id).displayName} at {g.world.instances[id].Position.y:0.000} m");
                if (deep > 0.006f) Debug.Log($"[piles] overlap {deep * 1000:0} mm: {g.piles.lastContact}");
                deepest = Mathf.Max(deepest, deep);
            }
            float top = parts.Max(id => g.index.WorldBounds(id).max.y);
            float spread = parts.Max(a => parts.Max(b => new Vector2(g.world.instances[a].Position.x - g.world.instances[b].Position.x, g.world.instances[a].Position.z - g.world.instances[b].Position.z).magnitude));
            Check("parts dropped on one spot tumble into a pile: each rests on something, none inside another", parts.Count >= 4 && floating.Count == 0 && deepest < 0.006f && !g.piles.Settling,
                  $"{parts.Count} dropped, pile top {top:0.00} m, spread {spread:0.00} m, deepest overlap {deepest * 1000:0.0} mm, floating: {(floating.Count == 0 ? "none" : string.Join("; ", floating))}; {settle}");
            yield return g.Capture(Path.Combine(outDir, "07e_drop_pile.png"), 3);
        }

        /// <summary>Save migration: a save whose catalog differs is mapped by definition id.</summary>
        /// <summary>Exact destination accounting and destination-based levels: every ordinary part
        /// has exactly one destination, every X/X target is the assigned count, a completion gives
        /// one point once (player or robot), single sorts give none, and points survive save/load.</summary>
        IEnumerator DestinationChecks()
        {
            // 1. accounting: unique ids, one destination per part, assigned totals, achievable
            var ordinary = g.world.instances.Where(i => !g.world.DefOf(i.id).IsRobotPart).ToList();
            bool idsOk = g.world.instances.Select((inst, k) => inst.id == k).All(x => x);
            var defDest = new Dictionary<int, string>();
            int dupDefs = 0, missing = 0, overCap = 0, labelBad = 0, empty = 0;
            foreach (var box in g.db.boxes.Keys)
            {
                var defs = g.db.order.Where(d => g.db.Get(d).storageBox == box).ToList();
                foreach (var d in defs) if (!defDest.TryAdd(d, box)) dupDefs++;
                int assigned = g.AssignedCount(box);
                if (assigned == 0) { empty++; continue; }
                if (assigned != defs.Sum(g.world.TotalOf)) labelBad++;
                if (assigned > g.db.BoxCapacity(box)) overCap++;
                var lbl = g.LabelFor(box).Item3;
                if (lbl != "" && !lbl.EndsWith("/" + assigned)) labelBad++;
            }
            foreach (var i in ordinary) if (!g.db.boxes.ContainsKey(g.world.DefOf(i.id).storageBox)) missing++;
            int assignedSum = g.db.boxes.Keys.Sum(g.AssignedCount), dests = g.CompletableDestinations();
            Check("destination accounting is exact: unique part ids, every ordinary part in exactly one destination, X/X targets = assigned parts, all achievable",
                  idsOk && dupDefs == 0 && missing == 0 && overCap == 0 && labelBad == 0 && assignedSum == ordinary.Count && dests + empty == g.db.boxes.Count && g.progression.destinationTotal == dests,
                  $"{g.presetId}: {ordinary.Count} ordinary parts, {assignedSum} assigned; {g.db.boxes.Count} destinations = {dests} completable + {empty} empty; duplicates {dupDefs}, without destination {missing}, over capacity {overCap}, wrong targets {labelBad}");

            // 2. a real completion in this world: points only for the last copy, once, also for a robot
            var pr = g.progression;
            string target = g.db.boxes.Keys.Where(b => g.AssignedCount(b) >= 3 && g.storage.Stored(b).Count == 0 && g.StraysIn(b).Count == 0)
                .Where(b => g.db.order.Where(d => g.db.Get(d).storageBox == b).SelectMany(g.world.IdsOf).All(id => g.world.instances[id].state == ItemState.InWorld))
                .OrderBy(b => g.AssignedCount(b)).FirstOrDefault();
            if (target == null) { Check("a destination to complete exists", false, ""); yield break; }
            var ids = g.db.order.Where(d => g.db.Get(d).storageBox == target).SelectMany(g.world.IdsOf).ToList();
            int pts0 = pr.DestinationsCompleted, lv0 = pr.level;
            for (int k = 0; k < ids.Count - 1; k++) { g.index.Remove(ids[k]); g.storage.Deposit(target, ids[k]); }
            int ptsPartial = pr.DestinationsCompleted;
            g.index.Remove(ids[^1]);
            g.storage.Deposit(target, ids[^1], "robot:retrobot");   // a robot stores the last copy
            int ptsDone = pr.DestinationsCompleted;
            // emptying and refilling it gives nothing
            g.storage.Remove(target, ids[0]);
            g.world.SetState(ids[0], ItemState.Carried, "player");
            g.storage.Deposit(target, ids[0]);
            int ptsRefill = pr.DestinationsCompleted;
            yield return Frames(2);
            string label = g.LabelFor(target).Item3;
            Check("completing a destination gives exactly one level point, the last copy may be a robot's, refilling gives none",
                  ptsPartial == pts0 && ptsDone == pts0 + 1 && ptsRefill == ptsDone && label == $"{ids.Count}/{ids.Count}",
                  $"{g.db.BoxLabel(target)} ({ids.Count} parts, label {label}): points {pts0} -> {ptsPartial} before the last copy -> {ptsDone} -> {ptsRefill} after a refill; level {lv0} -> {pr.level}");

            // 3. rules on a fresh progression: single sorts never level, the curve rises, the save keeps it
            var f = FreshProgression();
            for (int k = 0; k < 5000; k++) f.OnSorted(-1 - k, false);
            for (int k = 0; k < 50; k++) f.OnZoneRestored(k, 25);
            bool sortsNoLevel = f.level == 1 && f.xp > 0;
            f.OnDefinitionCompleted(-1, 3);
            bool firstGivesTwo = f.level == 2 && f.skillPoints == 2;
            f.OnDefinitionCompleted(-1, 3);
            bool once = f.DestinationsCompleted == 1;
            var inc = Enumerable.Range(2, f.maxLevel - 1).Select(l => f.DestinationsForLevel(l) - f.DestinationsForLevel(l - 1)).ToList();
            bool rising = inc[0] == 1 && inc.Zip(inc.Skip(1), (a, b) => b >= a).All(x => x) && inc[^1] > inc[1];
            for (int k = 2; f.DestinationsCompleted < f.DestinationsForLevel(9); k++) f.OnDefinitionCompleted(-k, 3);
            var saved = Json.Write(f.ToDict());
            var r = FreshProgression();
            r.Load(Json.Obj(Json.Parse(saved)));
            bool roundTrip = r.level == f.level && r.skillPoints == f.skillPoints && r.DestinationsCompleted == f.DestinationsCompleted && Json.Write(r.ToDict()) == saved;
            // a save from the XP levels: completed sets become points, nothing earned is taken away
            var legacy = Json.Obj(Json.Parse(saved));
            legacy.Remove("completed_destinations");
            legacy["level"] = 3.0;
            var m = FreshProgression();
            m.Load(legacy);
            bool migrated = m.DestinationsCompleted == f.DestinationsCompleted && m.level == f.level;
            Check("levels come only from destinations: 5000 single sorts give level 1, the first completion level 2, the curve rises, points survive save/load and old saves",
                  sortsNoLevel && firstGivesTwo && once && rising && roundTrip && migrated,
                  $"sorts only: level {(sortsNoLevel ? 1 : 0)}; first completion -> level 2 with 2 points {firstGivesTwo}; once {once}; increments {string.Join(" ", inc)}; round trip {roundTrip}; legacy {migrated} (level {m.level})");
        }

        IEnumerator MigrationChecks()
        {
            int sorted0 = g.progress.sortedTotal, inv0 = g.inventory.items.Count;
            long xp0 = g.progression.xp;
            var perDef = g.db.order.ToDictionary(d => d, d => g.progress.SortedOf(d));
            g.SaveGame("autotest_mig");
            var save = SaveSystem.Read("autotest_mig");
            save["catalog_hash"] = "an-older-catalog";   // pretend the parts catalog changed since
            SaveSystem.Write("autotest_mig", save);
            g.StartWorld(g.world.seed, null);
            g.LoadGame("autotest_mig");
            yield return Frames(5);
            bool same = g.db.order.All(d => g.progress.SortedOf(d) == perDef[d]);
            Check("a save from an older parts catalog is converted, keeping every sorted part, XP and pockets", g.migrationReport != "" && same && g.progress.sortedTotal == sorted0 && g.progression.xp == xp0 && g.inventory.items.Count == inv0,
                  $"{g.migrationReport}; sorted {sorted0} -> {g.progress.sortedTotal}, xp {xp0} -> {g.progression.xp}");
            save.Remove("item_defs");   // a pre-migration (version 1) save can't be mapped: refused with a message
            Check("an old save without part identities is refused with a clear message", SaveSystem.Check(save, g.db).Contains("older item catalog"), SaveSystem.Check(save, g.db));
            File.Delete(SaveSystem.PathOf("autotest_mig"));
        }

        /// <summary>Component rescale and the robot carry limit (ComponentScale, docs/COMPONENT_RESCALE.md).</summary>
        IEnumerator RescaleChecks()
        {
            var sortable = g.db.order.Select(g.db.Get).Where(d => !d.IsRobotPart).ToList();
            bool on = ComponentScale.Enabled;
            float largestBase = sortable.Max(d => ItemDatabase.Extents(d)[0] / d.componentScale), largest = sortable.Max(d => ItemDatabase.Extents(d)[0]);
            Check("component rescale: factors 1x to the configured maximum by size, the largest parts keep their size, eligibility from the catalogue list",
                  !on || sortable.All(d => d.componentScale >= 1f && d.componentScale <= ComponentScale.MaxFactor + 1e-4f) && largest <= largestBase + 1e-5f
                      && sortable.All(d => d.robotCarry == !ComponentScale.PlayerOnly.Contains(d.id)) && ComponentScale.PlayerOnly.Count == 200,
                  $"{(on ? "on" : "off")}: {sortable.Count(d => d.robotCarry)} of {sortable.Count} definitions robot-eligible in {g.presetId}, factor {sortable.Min(d => d.componentScale):0.00}-{sortable.Max(d => d.componentScale):0.00}, largest {largest * 1000:0.0} mm");
            if (!on) yield break;

            // a saved part at its old size takes the new size once, with its lowest point where it was
            var small = g.world.instances.FirstOrDefault(i => i.state == ItemState.InWorld && g.world.DefOf(i.id).componentScale > 1.2f);   // well scaled at any max factor in use
            if (small != null)
            {
                var def = g.world.DefOf(small.id);
                var m = small.transform;
                var old = Matrix4x4.TRS(m.GetColumn(3), m.rotation, Vector3.one * def.baseWorldScale);
                old = Matrix4x4.Translate(Vector3.up * (ComponentScale.LowestY(m, def) - ComponentScale.LowestY(old, def))) * old;
                var r1 = ComponentScale.Resize(old, def);
                var r2 = ComponentScale.Resize(r1, def);
                float s1 = ((Vector3)r1.GetColumn(0)).magnitude;
                bool same = Enumerable.Range(0, 16).All(k => Mathf.Abs(r1[k] - r2[k]) < 1e-6f);
                Check("an old-size saved part loads at the new size once (never twice), resting where it lay",
                      Mathf.Abs(s1 - def.worldScale) < def.worldScale * 1e-3f && Mathf.Abs(ComponentScale.LowestY(r1, def) - ComponentScale.LowestY(old, def)) < 0.001f && same,
                      $"{def.displayName}: scale {def.baseWorldScale:0.0000} -> {s1:0.0000} (x{def.componentScale:0.00}), lowest point {ComponentScale.LowestY(old, def) * 100:0.00} -> {ComponentScale.LowestY(r1, def) * 100:0.00} cm, again unchanged {same}");
            }

            // a player-only part right in front of Retrobot: it is never picked up, Retrobot sorts on
            var retro = g.retrobot;
            var big = g.world.instances.Where(i => i.state == ItemState.InWorld && !g.robotReserved.Contains(i.id) && !g.world.DefOf(i.id).robotCarry && !i.owner.StartsWith("stage:")
                                                  && ItemDatabase.Extents(g.world.DefOf(i.id))[0] <= retro.MaxItemSize)   // small enough for its claws: only the 80 % rule stops it
                .OrderBy(i => (i.Position - retro.transform.position).sqrMagnitude).FirstOrDefault();
            if (big != null && retro.Active)
            {
                var def = g.world.DefOf(big.id);
                var p = retro.transform.position + retro.transform.forward * 0.45f;
                p.y = g.debris != null ? g.debris.At(p) : 0;
                var m0 = big.transform;
                g.index.Remove(big.id);
                var placed = Matrix4x4.TRS(p, Quaternion.Euler(0, 30, def.restMode == RestMode.Long ? 90 : 0), Vector3.one * def.worldScale);
                placed = Matrix4x4.Translate(Vector3.up * (p.y - ComponentScale.LowestY(placed, def))) * placed;
                g.world.SetTransform(big.id, placed);
                g.index.Insert(big.id);
                int ev0 = _events.Count, picks0 = g.robots.Sum(r => r.pickCount);
                foreach (var r in g.robots) { r.enabled = true; r.SetBattery(1); }
                Time.timeScale = 4;
                float g0 = Time.time, t0 = Time.realtimeSinceStartup;
                while (Time.time - g0 < 40 && Time.realtimeSinceStartup - t0 < 40) yield return null;
                Time.timeScale = 1;
                int picks = g.robots.Sum(r => r.pickCount) - picks0;
                Check("robots never pick up a player-only part, even within their claws' size, and keep sorting the rest",
                      big.state == ItemState.InWorld && !g.robotReserved.Contains(big.id) && picks > 0,
                      $"{def.displayName} ({ItemDatabase.Extents(def)[0] * 1000:0} mm, Retrobot lifts {retro.MaxItemSize * 1000:0}) left at {(big.Position - p).magnitude * 100:0} cm from where it was put; {picks} other parts picked meanwhile");
                var spider = g.robots.FirstOrDefault(r => r.id == "spiderbot");
                string said = spider != null && spider.Active ? spider.Assign(def.id, false) : retro.CommandFocus(def.id);
                Check("a robot told to sort a player-only kind declines it", said.Contains("too big"), said);

                // the player can still take it
                bool room = g.inventory.Count < g.inventory.capacity;
                bool took = room && g.interaction.Pickup(big.id, false);
                Check("the player picks up player-only parts as before", !room || took && big.state == ItemState.Carried, room ? $"picked {took}" : "pockets full (skipped)");
                if (took) g.inventory.Remove(big.id);

                // an older save in which Retrobot carries it: Retrobot sets it down at its feet on loading
                g.world.SetState(big.id, ItemState.InWorld, "world");
                g.world.SetTransform(big.id, m0);
                g.index.Insert(big.id);
                foreach (var r in g.robots) r.enabled = false;
                g.SaveGame("autotest_scale");
                var save = SaveSystem.Read("autotest_scale");
                var rows = Json.Arr(save["items"]);
                var defs = Json.Arr(save["item_defs"]);
                int k = rows.FindIndex(r => (int)(double)((List<object>)r)[0] == big.id);
                var row = new List<object> { (double)big.id, (double)(int)ItemState.Carried, retro.Owner };
                if (k >= 0) rows[k] = row; else { rows.Add(row); defs.Add((double)big.defId); }
                save["version"] = 2.0;
                SaveSystem.Write("autotest_scale", save);
                int count0 = g.world.Count;
                g.StartWorld(g.world.seed, null);
                g.LoadGame("autotest_scale");
                foreach (var r in g.robots) r.enabled = false;
                yield return Frames(5);
                var back = g.world.instances[big.id];
                retro = g.retrobot;   // the loaded world's
                float d = Flat(back.Position - retro.transform.position).magnitude;
                Check("a robot that carried a now player-only part in a save sets it down at its feet (not deleted, not moved away)",
                      back.state == ItemState.InWorld && !retro.carried.Contains(big.id) && d < 1.0f && g.world.Count == count0 && back.Position.y >= -0.01f,
                      $"{back.state} {d * 100:0} cm from Retrobot, {g.world.Count} parts");
                foreach (var r in g.robots) r.enabled = true;
                File.Delete(SaveSystem.PathOf("autotest_scale"));
            }
            else Check("robots never pick up a player-only part, even within their claws' size, and keep sorting the rest", false, big == null ? "no candidate part" : "Retrobot is not active");
            // over the whole run so far: no robot picked up or sorted a player-only part
            var bad = _events.Where(e => (e.kind == GameEventKind.PartPickedUp || e.kind == GameEventKind.PartSorted) && e.actor.StartsWith("robot") && e.partDef >= 0 && !g.db.CanRobotCarry(e.partDef)).ToList();
            Check("no robot handled a player-only part during the run", bad.Count == 0, bad.Count == 0 ? $"{_events.Count(e => e.kind == GameEventKind.PartPickedUp && e.actor.StartsWith("robot"))} robot pickups checked" : string.Join(", ", bad.Take(5).Select(e => e.actor + " " + e.partDef)));
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

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
