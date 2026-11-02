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

        /// <summary>Runs every check; a check group that throws is reported as a failure and the run goes on
        /// with the next group, so the summary and report are always written.</summary>
        public IEnumerator Run(Game game, string dir) => Guarded(RunAll(game, dir));

        /// <summary>Steps a coroutine (and every coroutine it yields, recursively) inside a try/catch: an
        /// exception ends only the group that threw, as a FAIL naming the group and the exception.</summary>
        IEnumerator Guarded(IEnumerator e)
        {
            while (true)
            {
                object cur;
                try
                {
                    if (!e.MoveNext()) yield break;
                    cur = e.Current;
                }
                catch (System.Exception ex)
                {
                    string n = e.GetType().Name, group = n.StartsWith("<") && n.IndexOf('>') > 1 ? n.Substring(1, n.IndexOf('>') - 1) : n;
                    Debug.LogException(ex);
                    Check($"{group} ran to the end (the rest of this group was skipped)", false, $"{ex.GetType().Name}: {ex.Message} | {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
                    yield break;
                }
                yield return cur is IEnumerator sub && !(cur is CustomYieldInstruction) ? Guarded(sub) : cur;
            }
        }

        IEnumerator RunAll(Game game, string dir)
        {
            g = game;
            outDir = dir;
            Directory.CreateDirectory(outDir);
            GameEvents.Raised += e => _events.Add(e);   // every gameplay event of the run (roadmap 3A checks)
            RobotWorkbench.QaIgnoreToolRequirements = true;   // robot sections test levels and order; "pegboard" tests the tool board's requirements
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
            if (section == "outline")
            {
                yield return OutlineShots();   // QA: the hover highlight is a thin glowing rim
                Finish();
                yield break;
            }
            if (section == "loadkeep")
            {
                yield return LoadKeepChecks();   // QA: a load (also converted to a changed catalog) moves nothing
                Finish();
                yield break;
            }
            if (section == "meshdump")
            {
                MeshDump();   // QA: every item mesh as OBJ (geometry audit, playtest 2026-10-28)
                Finish();
                yield break;
            }
            if (section == "partcompare")
            {
                yield return PartCompare();   // QA: each loose robot part next to the same part cut out of its robot
                Finish();
                yield break;
            }
            if (section == "partsides")
            {
                yield return PartSides();   // QA: each loose robot part from six sides (holes, missing backs)
                Finish();
                yield break;
            }
            if (section == "partaudit")
            {
                yield return PartAudit();   // QA: each robot part on its robot and where it lies in the workshop
                Finish();
                yield break;
            }
            if (section == "partshots")
            {
                yield return PartShots();   // QA: close-ups of single parts (geometry checks)
                Finish();
                yield break;
            }
            if (section == "flight")
            {
                yield return FlightChecks();   // QA: a part in the air looks as it does on the ground; the Unibot tyre
                Finish();
                yield break;
            }
            if (section == "ghost")
            {
                yield return GhostChecks();   // QA: heads and arms go on the bench ghost before the chassis is found
                Finish();
                yield break;
            }
            if (section == "camshake")
            {
                yield return CamShake(g.args.GetValueOrDefault("apslot", "slot1"));   // QA: the view holds still while the player stands still
                Finish();
                yield break;
            }
            if (section == "benchshots")
            {
                yield return BenchShots(g.args.GetValueOrDefault("apslot", "slot1"));   // QA: the bench screen's two pages on a save
                Finish();
                yield break;
            }
            if (section == "pickslot")
            {
                yield return PickSlot(g.args.GetValueOrDefault("apslot", "slot1"));   // QA: the part picker at each mat of a player's save
                Finish();
                yield break;
            }
            if (section == "loadslot")
            {
                yield return LoadSlotChecks(g.args.GetValueOrDefault("apslot", "slot1"));   // QA: a player's save loads with its loose parts at rest
                Finish();
                yield break;
            }
            if (section == "loose")
            {
                yield return LooseChecks();   // QA: found heads and arms go straight on a built robot (Modify robots)
                Finish();
                yield break;
            }
            if (section == "strip")
            {
                yield return StripChecks();   // QA: an arm stripped off a built robot goes on one being assembled
                Finish();
                yield break;
            }
            if (section == "tools")
            {
                yield return ToolChecks();   // QA: player tools (glove levels, playtest 2026-10-25)
                Finish();
                yield break;
            }
            if (section == "donor")
            {
                yield return DonorPartChecks();   // QA: other robots' heads and arms on Retrobot (playtest 2026-10-25)
                Finish();
                yield break;
            }
            if (section == "input")
            {
                yield return InputAudioChecks();   // QA: focus loss, capture guard, saved volumes
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
            if (section == "design")
            {
                yield return DesignChecks();   // QA: Game Design v1.0 acceptance tests (P/R/S/E, §15)
                Finish();
                yield break;
            }
            if (section == "armpool")
            {
                ArmPoolChecks();   // QA: the three Sorting Arm kits are copies; any part goes on the arm being built (playtest 2026-10-26)
                Finish();
                yield break;
            }
            if (section == "idler")
            {
                // QA: close-ups of a Track Idler Wheel (its hub caps z-fought with the flanges, playtest 2026-10-26)
                g.player.enabled = false;
                var ids = g.world.instances.Where(i => g.world.DefOf(i.id).subcategory == "track_idler" && i.state == ItemState.InWorld && !g.Covered(i.id)).Take(2).ToList();
                for (int k = 0; k < ids.Count; k++)
                {
                    var at = ids[k].Position;
                    LookFrom(at + new Vector3(0.12f, 0.18f, 0.1f), at);
                    yield return Frames(4);
                    yield return g.Capture(Path.Combine(outDir, $"idler_{k}.png"), 2);
                }
                Check("track idler close-ups", ids.Count > 0, $"{ids.Count} uncovered idler wheels");
                Finish();
                yield break;
            }
            if (section == "lowshelf")
            {
                yield return LowShelfChecks();   // QA: Unibot and Hoverbot store into the low shelves without Arm Reach (playtest 2026-10-26)
                Finish();
                yield break;
            }
            if (section == "pegboard")
            {
                yield return PegboardChecks();   // QA: the tool pegboard (docs/PEGBOARD.md): hang, refuse, no XP, save/load, full board
                Finish();
                yield break;
            }
            if (section == "pegguard")
            {
                yield return PegGuardChecks();   // QA: a board tool in a robot's hands, in a wrong box or on the board without its holder
                Finish();
                yield break;
            }
            if (section == "saves")
            {
                yield return SaveChecks();   // QA: save and load (playtest 2026-10-28: a refused save lost an evening's progress)
                Finish();
                yield break;
            }
            if (section == "armwatch")
            {
                yield return ArmWatch(g.args.GetValueOrDefault("apslot", "slot1"));   // QA: why mounted Sorting Arms leave staged parts (playtest 2026-10-29)
                Finish();
                yield break;
            }
            if (section == "pegmigrate")
            {
                yield return PegMigrateChecks(g.args.GetValueOrDefault("apslot", "premig_slot1"));   // QA: a save from before the pegboard sections loads under the new rules
                Finish();
                yield break;
            }
            if (section == "radarplans")
            {
                yield return RadarPlanChecks();   // QA: the robot parts pool and the Part Radar plans (playtest 2026-10-26)
                Finish();
                yield break;
            }
            if (section == "settings")
            {
                SettingsChecks();   // QA: graphics presets and the sound categories
                Finish();
                yield break;
            }
            if (section == "skills")
            {
                yield return SkillChecks();   // QA: the skill revamp (docs/SKILL_REVAMP.md): points, costs, the four skills, respec, save/load
                Finish();
                yield break;
            }
            if (section == "pegplace")
            {
                yield return PegPlacementChecks();   // QA: placement rules 2026-10-30 on the pegboard
                Finish();
                yield break;
            }
            if (section == "inventory")
            {
                yield return InventoryOrderChecks();   // QA: the hand is last in, first out unless scrolled (playtest 2026-10-31)
                Finish();
                yield break;
            }
            if (section == "placement")
            {
                g.progression.skillPoints += 5;
                yield return InventoryOrderChecks();
                yield return PlacementChecks();   // QA: placement rules 2026-10-30 (click and Automatic Storage Delivery)
                Finish();
                yield break;
            }
            if (section == "skillmigrate")
            {
                yield return SkillMigrateChecks(g.args.GetValueOrDefault("apslot", "premig_slot1"));   // QA: migration A on a copy of an older save
                Finish();
                yield break;
            }
            if (section == "skillpanel")
            {
                yield return SkillPanelChecks();   // QA: the paged two-column skill panel (docs/SKILL_PANEL.md)
                Finish();
                yield break;
            }
            if (section == "respec")
            {
                yield return RespecChecks();   // QA: full respec, upgrade previews, robot telemetry (skill progression spec §6-8)
                Finish();
                yield break;
            }
            if (section == "kits")
            {
                KitChecks();   // QA: every unit's robot parts lie in the world from the start (playtest 2026-10-26)
                Finish();
                yield break;
            }
            if (section == "cater")
            {
                yield return CaterChecks();   // QA: Caterpillarbot fills its boxes for one destination (playtest 2026-10-26)
                Finish();
                yield break;
            }
            if (section == "tray")
            {
                yield return TrayChecks();   // QA: walking robots empty the whole sorting tray (playtest 2026-10-26)
                Finish();
                yield break;
            }
            if (section == "pile")
            {
                yield return PileChecks();   // QA: dropped parts pile up
                Finish();
                yield break;
            }
            if (section == "binpile")
            {
                yield return BinPileChecks();   // QA: parts put in the robot parts bin fall and pile up (playtest 2026-11-02)
                Finish();
                yield break;
            }
            if (section == "traypile")
            {
                yield return TrayPileChecks();   // QA: parts dropped in the sorting tray fall and pile up (playtest 2026-11-02)
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

            // 0b. walking into the parts: the player climbs onto them instead of passing through
            {
                var hc = WorldGenerator.CrowdedFloorSpots(g.world, 1)[0];   // the floor has no heaps: the most crowded spot
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
            var heap = WorldGenerator.CrowdedFloorSpots(w, 1)[0];   // the floor's most crowded spot (Unity space)
            var pile = ItemsNear(new Vector3(heap.x, 0, -heap.z), 1.2f);
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
            Check("you start with 10 pockets; the hotbar shows the inventory order (copies side by side share a slot)", g.inventory.capacity == 10 && g.inventory.Stacks().SelectMany(s => s.ids).SequenceEqual(g.inventory.items) && picked == 10,
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

        /// <summary>Playtest 2026-10-25: a head or arm found for another robot can go on Retrobot. Unibot's
        /// left arm (before the torso: it waits in the tray, then goes on) and Hoverbot's head build
        /// Retrobot; it activates wearing them as modules; Retrobot's own left arm, found later, goes on
        /// Unibot; nothing is duplicated or lost, and it all survives save/load.</summary>
        IEnumerator DonorPartChecks()
        {
            var wb = g.workbench;
            var retro = wb.P("retrobot");
            var uni = wb.P("unibot");
            var rb = g.retrobot;
            g.player.enabled = false;
            int Part(string robot, string key) => wb.PartIds(wb.P(robot)).First(i => Robot.PartKey(g.world.DefOf(i)) == key);
            int uArm = Part("unibot", "left arm"), hHead = Part("hoverbot", "head"), rArm = Part("retrobot", "left arm"), rHead = Part("retrobot", "head");
            LevelTo(2);
            // Unibot and Hoverbot must be locked for their parts to be donors. Every robot is level 1 since
            // 2026-10-29, so the lock is the pegboard reward: keep the real tool requirement, not the QA bypass.
            RobotWorkbench.QaIgnoreToolRequirements = false;
            wb.selected = "retrobot";
            // Unibot's arm before Retrobot's torso: into Unibot's tray, then on once the torso is in
            Take(uArm); g.interaction.DepositStation(uArm);
            bool waited = g.world.instances[uArm].state == ItemState.Stored && wb.DonorIn(retro, ModuleSlot.ArmL) < 0;
            int torso = Part("retrobot", "torso");
            Take(torso); g.interaction.DepositStation(torso);
            int pulled = wb.InstallAll(retro);
            bool armOn = wb.DonorIn(retro, ModuleSlot.ArmL) == uArm;
            // Hoverbot's head with the torso in: into the parts pool, offered on the bench screen, fitted by choice
            Take(hHead); var (_, headMsg) = wb.Deliver(hHead);
            bool pooled = g.world.instances[hHead].state == ItemState.Stored && wb.PoolContents().Contains(hHead) && wb.PoolDonors(retro).Contains(hHead);
            bool headOn = pooled && wb.Install(retro, hHead) && wb.DonorIn(retro, ModuleSlot.Head) == hHead;
            // Retrobot's own left arm and head now have nowhere on Retrobot
            Take(rArm); g.interaction.DepositStation(rArm);
            Take(rHead); g.interaction.DepositStation(rHead);
            string ownWhy = wb.CanInstall(retro, rArm);
            // refused because the slot holds Unibot's arm (wording since hands-on building 2026-10-29: "already has a left arm fitted")
            bool ownRefused = g.world.instances[rArm].state == ItemState.Stored && ownWhy.Contains("already has a left arm") && wb.DonorIn(retro, ModuleSlot.ArmL) == uArm;
            foreach (var key in new[] { "right arm", "leg assembly" }) { int id = Part("retrobot", key); Take(id); g.interaction.DepositStation(id); }
            wb.InstallAll(retro);   // "Build from the parts pool"
            yield return Frames(2);
            bool complete = wb.Complete(retro) && wb.Required(retro) == 5;
            var froms = rb.ModelFroms;
            Check("another robot's head and arm go on Retrobot: delivered parts wait in the parts pool until built on; a pooled head is fitted by choice; Retrobot's own then waits",
                  waited && pulled >= 1 && armOn && headOn && ownRefused && complete && froms.h == "hoverbot" && froms.l == "unibot",
                  $"waited {waited}; pulled {pulled}, arm on {armOn}; head: {headMsg}; own arm: {ownWhy}; complete {complete} ({wb.InstalledCount(retro)}/{wb.Required(retro)}); bench model {froms}");
            var (spot, _) = wb.MatSpot(0);
            LookFrom(spot + wb.Front * 0.55f + Vector3.up * 0.32f + Vector3.Cross(Vector3.up, wb.Front) * 0.2f, spot + Vector3.up * 0.12f);
            yield return g.Capture(Path.Combine(outDir, "30_retrobot_donor_bench.png"), 4);
            // activation: the donors become its mounted modules, its own wait at the factory
            g.SetState("play");
            g.player.enabled = false;
            bool act = wb.Activate(retro);
            float t0 = Time.realtimeSinceStartup;
            while (!rb.Active && Time.realtimeSinceStartup - t0 < 12) { LookAtRobot(1.0f, 0.9f, 0.4f); yield return null; }
            var m = g.modules;
            var audit = m.Audit();
            bool mods = m.At("retrobot", ModuleSlot.ArmL)?.origin == "unibot" && m.At("retrobot", ModuleSlot.Head)?.origin == "hoverbot" && m.At("retrobot", ModuleSlot.ArmR)?.origin == "retrobot"
                        && m.Get("arm_l.retrobot").location == RobotModules.FactoryLoc && m.Get("head.retrobot").location == RobotModules.FactoryLoc;
            Check("Retrobot activates wearing Unibot's arm and Hoverbot's head as modules; its own wait at the factory; the module audit is clean",
                  act && rb.Operational && mods && audit.Count == 0 && rb.ModelFroms.h == "hoverbot" && rb.ModelFroms.l == "unibot",
                  $"operational {rb.Operational}; head {m.At("retrobot", ModuleSlot.Head)?.id}, left {m.At("retrobot", ModuleSlot.ArmL)?.id}, right {m.At("retrobot", ModuleSlot.ArmR)?.id}; audit: {(audit.Count == 0 ? "clean" : string.Join("; ", audit))}");
            LookAtRobot(0.7f, 0.45f, 0.15f);
            yield return g.Capture(Path.Combine(outDir, "31_retrobot_donor_online.png"), 3);
            // Unibot: its left arm is on Retrobot; Retrobot's own (in its tray) goes on instead.
            // Unibot's blueprint now: as if the pliers section were completed (its pegboard reward)
            g.pegRewards.GrantAll(new[] { "unibot" });
            LevelTo(5);
            wb.selected = "unibot";
            int uTorso = Part("unibot", "torso");
            Take(uTorso); g.interaction.DepositStation(uTorso);
            int got = wb.InstallAll(uni);   // its torso from the pool, and Retrobot's spare arm and head
            // Retrobot's spare head goes on too (Unibot's head shell then waits)
            bool uniArm = wb.DonorIn(uni, ModuleSlot.ArmL) == rArm && wb.DonorIn(uni, ModuleSlot.Head) == rHead && got == 3 && wb.Required(uni) == 9 - 1 - 1 + 2;   // its own head shell and left arm out, the two donors in
            bool intact = PartsIntact(out var pi);
            Check("Retrobot's own left arm and head, waiting in the parts pool, go on Unibot (its own left arm is on Retrobot)",
                  uniArm && intact, $"installed {got}; Unibot left arm slot: {(wb.DonorIn(uni, ModuleSlot.ArmL) is int x && x >= 0 ? g.world.DefOf(x).displayName : "empty")}; {wb.InstalledCount(uni)}/{wb.Required(uni)}; head slot {wb.DonorIn(uni, ModuleSlot.Head)} (Retrobot's {rHead}); installed [{string.Join(", ", wb.InstalledIds(uni).Select(i => g.world.DefOf(i).displayName))}]; {pi}");
            // save / load keeps all of it
            string State() => $"{string.Join(",", wb.InstalledIds(retro))}|{string.Join(",", wb.InstalledIds(uni))}|{Json.Write(m.ToDict()["locations"])}|{Json.Write(m.ToDict()["waiting"])}";
            var before = State();
            g.SaveGame("autotest_donor");
            g.StartWorld(g.world.seed, null);
            g.LoadGame("autotest_donor");
            g.player.enabled = false;
            yield return Frames(4);
            wb = g.workbench; retro = wb.P("retrobot"); uni = wb.P("unibot"); m = g.modules;
            var after = State();
            var audit2 = m.Audit();
            Check("donor heads and arms (on the bench and as modules) survive save/load, audit clean",
                  before == after && audit2.Count == 0 && g.retrobot.ModelFroms.l == "unibot", before == after ? $"same; audit {(audit2.Count == 0 ? "clean" : string.Join("; ", audit2))}" : $"{before}  vs  {after}");
            File.Delete(SaveSystem.PathOf("autotest_donor"));
            RobotWorkbench.QaIgnoreToolRequirements = true;
        }

        /// <summary>Playtest 2026-10-25: the aimed-at part gets a thin glowing rim, not a shell much
        /// larger than the part. Screenshots of a small, a long and a large part, close and at reach.</summary>
        IEnumerator OutlineShots()
        {
            g.SetState("play");
            g.player.enabled = false;
            g.interaction.enabledInput = true;   // automated runs hide the hover each frame; aimed shots need it
            var eye = g.player.transform.position + Vector3.up * PlayerController.Eye;
            var near = g.world.instances.Where(i => i.state == ItemState.InWorld && !g.InTray(i.Position) && i.Position.y < 0.3f)
                .OrderBy(i => (i.Position - eye).sqrMagnitude).Take(400).ToList();
            float Size(ItemInstance i) => ItemDatabase.Extents(g.world.DefOf(i.id)).Max();
            var picks = new[] { near.OrderBy(Size).First(), near.OrderByDescending(i => { var e = ItemDatabase.Extents(g.world.DefOf(i.id)); return e.Max() / Mathf.Max(0.001f, e.Min()); }).First(), near.OrderByDescending(Size).First() };
            int n = 0, shown = 0;
            foreach (var p in picks)
                foreach (var dist in new[] { 0.45f, 1.1f })
                {
                    var at = p.Position;
                    var from = at + new Vector3(0.6f, 0.75f, 0.5f).normalized * dist;
                    LookFrom(from, at);
                    yield return Frames(2);
                    g.interaction.UpdateTarget();
                    yield return Frames(1);
                    if (g.interaction.targetType == "item") shown++;
                    yield return g.Capture(Path.Combine(outDir, $"34_outline_{n++}_{g.world.DefOf(p.id).displayName.Replace(' ', '_').Replace('/', '_')}_{dist:0.00}m.png"), 2);
                }
            // the Part Afterglow skill is retired (skill revamp 2026-10-30): it can't be bought, so nothing blinks
            var glow = g.progression.Skill("part_afterglow");
            Check("Part Afterglow is retired: not buyable, no afterglow", glow != null && glow.Retired && !g.UnlockSkill("part_afterglow") && !g.interaction.AfterglowOn,
                  $"status {glow?.status}, afterglow on {g.interaction.AfterglowOn}");
            g.interaction.enabledInput = false;
            Check("the aimed-at part shows its highlight (screenshots 34_outline_*)", shown >= 4, $"{shown} of 6 aimed shots show a highlight");
        }

        /// <summary>Playtest 2026-10-25 ("I should be able to choose not installed parts and put on the robot if
        /// I remove something"): Retrobot is built with Unibot's left arm, its own waits in the tray. Under Modify
        /// robots Unibot's arm comes off and Retrobot's own goes on from the tray; Unibot's head, found and
        /// carried, replaces Retrobot's (which goes to the bench); unbuilt Unibot then needs no head; nothing is
        /// duplicated or lost, and it survives save/load.</summary>
        IEnumerator LooseChecks()
        {
            var wb = g.workbench;
            var retro = wb.P("retrobot");
            var uni = wb.P("unibot");
            var m = g.modules;
            g.player.enabled = false;
            int Part(string robot, string key) => wb.PartIds(wb.P(robot)).First(i => Robot.PartKey(g.world.DefOf(i)) == key);
            int uArm = Part("unibot", "left arm"), rArm = Part("retrobot", "left arm"), uHead = Part("unibot", "head shell");
            LevelTo(2);
            // Unibot's own blueprint must be locked for its parts to be donors ("its own robot cannot use it").
            // Every robot is level 1 since 2026-10-29, so the lock is the pegboard reward (pliers section):
            // keep the real tool requirement here instead of the global QA bypass.
            RobotWorkbench.QaIgnoreToolRequirements = false;
            bool uniLocked = !wb.Unlocked(uni) && wb.Unlocked(retro);
            wb.selected = "retrobot";
            Take(uArm); g.interaction.DepositStation(uArm);
            foreach (var id in wb.PartIds(retro).Where(id => id != rArm).ToList()) { Take(id); g.interaction.DepositStation(id); }
            wb.InstallAll(retro);
            Take(rArm); g.interaction.DepositStation(rArm);   // its own: nowhere to go, waits in the tray
            g.SetState("play");
            g.player.enabled = false;
            bool act = wb.Activate(retro);
            float t0 = Time.realtimeSinceStartup;
            while (!g.retrobot.Active && Time.realtimeSinceStartup - t0 < 12) { LookAtRobot(1.0f, 0.9f, 0.4f); yield return null; }
            bool donorOn = m.At("retrobot", ModuleSlot.ArmL)?.id == "arm_l.unibot";
            // Modify: take Unibot's arm off, Retrobot's own from the tray goes on
            var off = m.Remove("retrobot", ModuleSlot.ArmL);
            bool listed = wb.LooseModules(ModuleSlot.ArmL).Any(x => x.id == rArm && x.m.id == "arm_l.retrobot");
            bool fit = wb.FitLoose(g.retrobot, rArm, ModuleSlot.ArmL);
            g.retrobot.ApplyModules();
            bool ownOn = m.At("retrobot", ModuleSlot.ArmL)?.id == "arm_l.retrobot" && g.world.instances[rArm].state == ItemState.Installed && m.Complete("retrobot");
            bool intact = PartsIntact(out var pi);
            Check("a found arm waiting in the tray goes straight on a built robot once its slot is free",
                  uniLocked && act && donorOn && off != null && listed && fit && ownOn && m.Audit().Count == 0 && intact,
                  $"Unibot locked {uniLocked}; activated {act}; built with Unibot's arm {donorOn}; off {off?.id}; listed {listed}; fitted {fit}; own arm on {ownOn}; audit {(m.Audit().Count == 0 ? "clean" : string.Join("; ", m.Audit()))}; {pi}");
            // a found head carried by the player replaces the fitted one, which goes to the bench
            int need0 = wb.Required(uni);
            Take(uHead);
            bool fit2 = wb.FitLoose(g.retrobot, uHead, ModuleSlot.Head);
            g.retrobot.ApplyModules();
            bool headOn = m.At("retrobot", ModuleSlot.Head)?.id == "head.unibot" && m.Get("head.retrobot").BenchSlot >= 0 && !g.inventory.items.Contains(uHead);
            int need1 = wb.Required(uni);
            // each part keeps its own look: Unibot's head on the Retrobot model, its own arm drawn as its own (null = no donor)
            bool looks = g.retrobot.ModelFroms.h == "unibot" && g.retrobot.ModelFroms.l is null or "retrobot";
            bool intact2 = PartsIntact(out var pi2);
            Check("a found head in the player's hands replaces a robot's head (the old one goes to the bench); its own robot, unbuilt, then needs no head",
                  fit2 && headOn && need1 == need0 - 1 && m.Audit().Count == 0 && intact2 && looks,
                  $"fitted {fit2}; model {g.retrobot.ModelFroms} (own colours {looks}); head {m.At("retrobot", ModuleSlot.Head)?.id}, Retrobot's own at {m.Get("head.retrobot").location}; Unibot needs {need0} -> {need1}; {pi2}");
            LookAtRobot(0.7f, 0.45f, 0.15f);
            yield return g.Capture(Path.Combine(outDir, "35_loose_parts_fitted.png"), 3);
            string State() => $"{Json.Write(m.ToDict()["locations"])}|{Json.Write(m.ToDict()["waiting"])}|{g.world.instances[rArm].owner}|{g.world.instances[uHead].owner}|{wb.Required(wb.P("unibot"))}";
            var before = State();
            g.SaveGame("autotest_loose");
            g.StartWorld(g.world.seed, null);
            g.LoadGame("autotest_loose");
            g.player.enabled = false;
            yield return Frames(4);
            m = g.modules; wb = g.workbench;
            var after = State();
            Check("found parts fitted as modules survive save/load, audit clean", before == after && m.Audit().Count == 0,
                  before == after ? $"same; audit {(m.Audit().Count == 0 ? "clean" : string.Join("; ", m.Audit()))}" : $"{before}  vs  {after}");
            File.Delete(SaveSystem.PathOf("autotest_loose"));
            RobotWorkbench.QaIgnoreToolRequirements = true;
        }

        /// <summary>Playtest 2026-10-25: strip a built robot and fit the part to another. Retrobot is
        /// built and activated; its left arm comes off (a spare on the bench) and goes into Unibot's
        /// assembly, standing in for Unibot's own; it can come off again; Unibot activates wearing it;
        /// nothing is duplicated or lost, and it survives save/load.</summary>
        IEnumerator StripChecks()
        {
            var wb = g.workbench;
            var retro = wb.P("retrobot");
            var uni = wb.P("unibot");
            var m = g.modules;
            g.player.enabled = false;
            int Part(string robot, string key) => wb.PartIds(wb.P(robot)).First(i => Robot.PartKey(g.world.DefOf(i)) == key);
            void Deliver(RobotWorkbench.Project p, IEnumerable<int> ids) { foreach (var id in ids) { Take(id); g.interaction.DepositStation(id); } wb.InstallAll(p); }
            LevelTo(5);
            // Retrobot built and running
            wb.selected = "retrobot";
            Deliver(retro, wb.PartIds(retro).ToList());
            g.SetState("play");
            g.player.enabled = false;
            bool act = wb.Activate(retro);
            float t0 = Time.realtimeSinceStartup;
            while (!g.retrobot.Active && Time.realtimeSinceStartup - t0 < 12) { LookAtRobot(1.0f, 0.9f, 0.4f); yield return null; }
            // its left arm off: a spare on the bench
            var arm = m.Remove("retrobot", ModuleSlot.ArmL);
            bool off = arm != null && arm.id == "arm_l.retrobot" && arm.BenchSlot >= 0 && !m.Complete("retrobot");
            // Unibot: everything but its own left arm
            wb.selected = "unibot";
            int uArm = Part("unibot", "left arm");
            Deliver(uni, wb.PartIds(uni).Where(id => id != uArm).ToList());
            int req0 = wb.Required(uni);
            string why = wb.CanFitSpare(uni, arm);
            bool fit = why == "" && wb.FitSpare(uni, arm);
            bool counted = wb.Required(uni) == req0 - 1 && wb.Complete(uni) && wb.InstalledKeys(uni).Contains("left arm") && wb.DonorOrigin(uni, ModuleSlot.ArmL) == "retrobot";
            // and off again, and back on
            bool back = wb.RemoveSpare(uni, ModuleSlot.ArmL) != null && arm.BenchSlot >= 0 && !wb.Complete(uni) && wb.FitSpare(uni, arm);
            // Unibot's own left arm now has no place on Unibot
            Take(uArm); g.interaction.DepositStation(uArm);
            string ownWhy = wb.CanInstall(uni, uArm);
            Check("an arm stripped off a built robot goes into another robot's assembly in place of its own, and can come off again",
                  act && off && fit && counted && back && ownWhy.Contains("Retrobot") && m.Audit().Count == 0,
                  $"Retrobot active {act}; off {off} ({arm?.location}); fit: {(why == "" ? "ok" : why)}; counted {counted} ({wb.InstalledCount(uni)}/{wb.Required(uni)}, was {req0}); off and on again {back}; own arm: {ownWhy}; audit {string.Join("; ", m.Audit())}");
            yield return Frames(2);
            var ub = wb.RobotOf(uni);
            LookAtRobot(ub, 0.9f, 0.5f, 0.25f);
            yield return g.Capture(Path.Combine(outDir, "32_strip_unibot_bench.png"), 3);
            // activation: the stripped arm becomes Unibot's module, Unibot's own waits at the factory
            bool act2 = wb.Activate(uni);
            t0 = Time.realtimeSinceStartup;
            while (!ub.Active && Time.realtimeSinceStartup - t0 < 12) { LookAtRobot(ub, 1.0f, 0.9f, 0.4f); yield return null; }
            var audit = m.Audit();
            bool mods = m.At("unibot", ModuleSlot.ArmL)?.id == "arm_l.retrobot" && m.Get("arm_l.unibot").location == RobotModules.FactoryLoc && m.At("retrobot", ModuleSlot.ArmL) == null && !m.Reserved.Any();
            Check("the robot activates wearing the stripped arm; the stripped robot misses it; the module audit is clean",
                  act2 && ub.Operational && mods && audit.Count == 0 && ub.ModelFroms.l == "retrobot",
                  $"operational {ub.Operational}; Unibot left {m.At("unibot", ModuleSlot.ArmL)?.id}; Retrobot left {m.At("retrobot", ModuleSlot.ArmL)?.id ?? "none"}; own {m.Get("arm_l.unibot").location}; model {ub.ModelFroms}; audit {(audit.Count == 0 ? "clean" : string.Join("; ", audit))}");
            LookAtRobot(ub, 0.7f, 0.45f, 0.15f);
            yield return g.Capture(Path.Combine(outDir, "33_strip_unibot_online.png"), 3);
            // a reservation (Retrobot's head into Spiderbot's assembly) survives save/load as well
            var head = m.Remove("retrobot", ModuleSlot.Head);
            var spider = wb.P("spiderbot");
            LevelTo(spider.level);
            wb.selected = "spiderbot";
            // its torso first: the head goes on the torso
            Deliver(spider, wb.PartIds(spider).Where(id => wb.MainSlotOf(id) != ModuleSlot.Head).ToList());
            string resWhy = head == null ? "no head" : wb.CanFitSpare(spider, head);
            bool res = resWhy == "" && wb.FitSpare(spider, head);
            string State() => $"{Json.Write(m.ToDict()["locations"])}|{Json.Write(m.ToDict()["waiting"])}|{wb.InstalledCount(wb.P("spiderbot"))}/{wb.Required(wb.P("spiderbot"))}";
            var before = State();
            g.SaveGame("autotest_strip");
            g.StartWorld(g.world.seed, null);
            g.LoadGame("autotest_strip");
            g.player.enabled = false;
            yield return Frames(4);
            m = g.modules; wb = g.workbench;
            var after = State();
            Check("stripped and refitted modules (mounted and in an assembly) survive save/load, audit clean",
                  res && before == after && m.Audit().Count == 0 && m.ReservedFor("spiderbot", ModuleSlot.Head)?.id == "head.retrobot",
                  before == after ? $"same; reserved {(res ? "ok" : resWhy)}; audit {(m.Audit().Count == 0 ? "clean" : string.Join("; ", m.Audit()))}" : $"{before}  vs  {after}");
            File.Delete(SaveSystem.PathOf("autotest_strip"));
        }

        /// <summary>Mouse and sound: losing focus in play pauses, frees the cursor and silences
        /// the game; a fresh capture skips the recentring frames; the volumes are saved, and a
        /// muted run never writes them. (The OS-level Alt+Tab test drives a normal build.)</summary>
        IEnumerator InputAudioChecks()
        {
            g.SetState("play");
            yield return Frames(2);
            g.HandleFocus(false);
            bool lost = g.state == "paused" && Cursor.lockState == CursorLockMode.None && Cursor.visible && AudioListener.pause;
            g.HandleFocus(true);
            bool back = !AudioListener.pause && g.state == "paused";
            g.SetState("play");
            // the capture guard: no look on the capturing frames, a click only once locked a frame
            Cursor.lockState = CursorLockMode.Locked;
            yield return null;
            bool guard0 = !PlayerController.LookReady;
            yield return Frames(3);
            bool guardOk = guard0 && (Cursor.lockState != CursorLockMode.Locked || (PlayerController.LookReady && PlayerController.ClickReady));
            string lockNote = Cursor.lockState == CursorLockMode.Locked ? "locked" : "lock not granted without focus";
            Cursor.lockState = CursorLockMode.None;
            // saved volumes (the player's own values are put back afterwards)
            bool hadM = PlayerPrefs.HasKey("ww_master_volume"), hadS = PlayerPrefs.HasKey("ww_sfx_volume");
            float oldM = PlayerPrefs.GetFloat("ww_master_volume", 1f), oldS = PlayerPrefs.GetFloat("ww_sfx_volume", 0.8f);
            Sfx.LoadVolumes(true);
            Sfx.SetMaster(0.11f);
            bool muteWritesNothing = AudioListener.volume == 0 && Sfx.masterVolume == 0 && PlayerPrefs.GetFloat("ww_master_volume", 1f) == oldM;
            Sfx.LoadVolumes(false);
            bool defaultsOn = hadM || (AudioListener.volume == 1f && Sfx.masterVolume == 0.8f);
            Sfx.SetMaster(0.5f);
            Sfx.SetSfx(0.3f);
            Sfx.LoadVolumes(false);
            bool saved = Mathf.Approximately(AudioListener.volume, 0.5f) && Mathf.Approximately(Sfx.masterVolume, 0.3f);
            if (hadM) PlayerPrefs.SetFloat("ww_master_volume", oldM); else PlayerPrefs.DeleteKey("ww_master_volume");
            if (hadS) PlayerPrefs.SetFloat("ww_sfx_volume", oldS); else PlayerPrefs.DeleteKey("ww_sfx_volume");
            PlayerPrefs.Save();
            Sfx.LoadVolumes(true);
            Check("mouse and sound: focus loss pauses, frees the cursor and stops the sound; the capture skips the warp frames; volumes are saved, --mute writes nothing",
                  lost && back && guardOk && muteWritesNothing && defaultsOn && saved,
                  $"focus lost: paused + cursor free + sound paused {lost}; back: sound on, still paused until resumed {back}; capture guard {guardOk} ({lockNote}); mute writes nothing {muteWritesNothing}; sound on by default {defaultsOn}; saved 0.5/0.3 restored {saved}");
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
            // the floor is one spread over the whole room (no heaps, playtest 2026-10-29): a set's copies lie far apart
            Check("parts are mixed: no site is mostly one category, a set's copies lie far apart, few neighbours are the same part",
                  dominant < 0.4 && median > 4f && sameDef < 0.12,
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
            var others = g.robots.Where(r => r.id != "retrobot" && wb.P(r.id).after == null && r.Mobile).ToList();
            Check("base capacities follow the capability matrix (1 / 1 / 1 / 3 / 1, not scaled by world size)",
                  g.robots.First(r => r.id == "retrobot").Capacity == 1 && g.robots.First(r => r.id == "unibot").Capacity == 1 && g.robots.First(r => r.id == "spiderbot").Capacity == 1
                  && g.robots.First(r => r.id == "caterpillarbot").Capacity == 3 && g.robots.First(r => r.id == "hoverbot").Capacity == 1,
                  string.Join(", ", g.robots.Select(r => $"{r.displayName} {r.Capacity}")));
            var retro = g.retrobot; var uni = g.robots.First(r => r.id == "unibot");
            Check("Unibot is about twice as fast as Retrobot", uni.WalkSpeed / retro.BaseStat("walk_speed") is > 1.8f and < 2.2f, $"{uni.WalkSpeed:0.00} vs {retro.BaseStat("walk_speed"):0.00} m/s");
            var mobile = g.robots.Where(r => r.Mobile).ToList();
            Check("five robot types at toy scale, up to three Hoverbots", mobile.Count(r => wb.P(r.id).after == null) == 5 && g.robots.Count(r => r.type == "hoverbot") == 3 && mobile.All(r => r.Height <= 0.31f),
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
            Check("Unibot without Arm Reach stores only into the low destinations; the rest goes to staging zones (still unsorted)", uni.staged > before["unibot"].staged
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
            // playtest 2026-10-26: without Arm Reach, Unibot and Hoverbot put parts into the low shelves (tossed in
            // under a deck when the claws can't place them) and the bottom drawers, not the second level
            var lows = g.db.boxes.Where(kv => Json.Str(kv.Value, "kind") != "floor_pallet" && Json.Num(kv.Value, "elevation_m") <= 0.2).Select(kv => kv.Key).ToList();
            string shelfLow = lows.FirstOrDefault(b => Json.Str(g.db.boxes[b], "kind") != "drawer"), shelfMid = g.db.boxes.Keys.FirstOrDefault(b => Json.Str(g.db.boxes[b], "kind") != "drawer" && Json.Str(g.db.boxes[b], "kind") != "floor_pallet" && Mathf.Abs((float)Json.Num(g.db.boxes[b], "elevation_m") - 0.62f) < 0.01f);
            var hov = g.robots.First(r => r.type == "hoverbot");
            int uniLow = lows.Count(uni.ReachableDest), hovLow = lows.Count(hov.ReachableDest);
            Check("without Arm Reach Unibot and Hoverbot store into every low shelf box and bottom drawer (not Unibot the second level)",
                  lows.Count > 0 && shelfLow != null && uniLow == lows.Count && hovLow == lows.Count && !uni.ReachableDest(shelfMid),
                  $"{lows.Count} low destinations: Unibot {uniLow}, Hoverbot {hovLow}; Unibot at {shelfMid} {uni.ReachableDest(shelfMid)}");
            float uniReach0 = uni.ReachHeight;
            for (int k = 0; k < 3; k++) g.BuyRobotUpgrade("uni_reach");
            g.BuyRobotUpgrade("cat_reach"); g.BuyRobotUpgrade("cat_reach");
            var catR = g.robots.First(r => r.id == "caterpillarbot");
            Check("Unibot and Caterpillarbot get Arm Reach (telescopic arms) within their limits", uni.ReachHeight > uniReach0 && uni.ReachHeight <= uni.LimitOf("reach_height") + 1e-3f && catR.ReachHeight <= catR.LimitOf("reach_height") + 1e-3f && catR.ReachHeight > catR.BaseStat("reach_height"),
                  $"Unibot {uniReach0:0.00} -> {uni.ReachHeight:0.00} m, Caterpillarbot {catR.BaseStat("reach_height"):0.00} -> {catR.ReachHeight:0.00} m");
            g.BuyRobotUpgrade("uni_capacity"); g.BuyRobotUpgrade("uni_capacity");
            // playtest 2026-10-26: with full Arm Reach both reach the D drawer row and the second shelf level (tossed in)
            string dRow = g.db.boxes.Keys.First(b => Json.Str(g.db.boxes[b], "kind") == "drawer" && b.EndsWith("-D1"));
            string shelf1 = g.db.boxes.Keys.First(b => Json.Str(g.db.boxes[b], "kind") != "drawer" && Json.Str(g.db.boxes[b], "kind") != "floor_pallet" && Mathf.Abs((float)Json.Num(g.db.boxes[b], "elevation_m") - 0.62f) < 0.01f);
            string cRow = dRow.Replace("-D1", "-C1");
            Check("Unibot carries more after upgrades; with Arm Reach it stores itself, up to the D drawers and the second shelf level",
                  uni.Capacity == 3 && uni.CanDirectStore && uni.ReachableDest(dRow) && uni.ReachableDest(shelf1) && !uni.ReachableDest(cRow), $"capacity {uni.Capacity}, reach {uni.ReachHeight:0.00} m");
            Check("Retrobot with full Arm Reach stores into the D drawers and the second shelf level, not the C drawers",
                  retro.ReachableDest(dRow) && retro.ReachableDest(shelf1) && !retro.ReachableDest(cRow), $"reach {retro.ReachHeight:0.00} m: {dRow} {retro.ReachableDest(dRow)}, {shelf1} {retro.ReachableDest(shelf1)}, {cRow} {retro.ReachableDest(cRow)}");
            g.BuyRobotUpgrade("retro_speed"); g.BuyRobotUpgrade("retro_speed"); g.BuyRobotUpgrade("retro_speed");
            Check("Retrobot never becomes faster than Unibot", retro.WalkSpeed < uni.BaseStat("walk_speed"), $"{retro.WalkSpeed:0.00} vs {uni.BaseStat("walk_speed"):0.00} m/s");
            g.BuyRobotUpgrade("hover_speed");
            var hovers = g.robots.Where(r => r.type == "hoverbot").ToList();
            Check("Hoverbot upgrades are shared by the whole fleet", hovers.All(h => Mathf.Approximately(h.WalkSpeed, hover.WalkSpeed)) && hover.WalkSpeed > hover.BaseStat("walk_speed"), string.Join(", ", hovers.Select(h => $"{h.displayName} {h.WalkSpeed:0.00}")));

            // 14. a second Hoverbot, built from its own kit (playtest 2026-10-26: every robot part is in the world from the start)
            var h2 = wb.P("hoverbot_2");
            var r2 = wb.RobotOf(h2);
            Check("Hoverbot #2's project opens once Hoverbot is operational", wb.Unlocked(h2) && wb.OnBench(h2) && r2.gameObject.activeSelf, wb.Summary(h2));
            var kit2 = wb.PartIds(h2);
            Check("Hoverbot #2 has its own kit of 28 unique parts, the same part keys as Hoverbot's",
                  !h2.crafted && kit2.Count == 28 && kit2.Select(i => Robot.PartKey(g.world.DefOf(i))).ToHashSet().SetEquals(wb.PartIds(wb.P("hoverbot")).Select(i => Robot.PartKey(g.world.DefOf(i)))),
                  $"{kit2.Count} parts, e.g. {g.world.DefOf(kit2[0]).displayName} -> \"{Robot.PartKey(g.world.DefOf(kit2[0]))}\"");
            int delivered = 0;
            foreach (var id in kit2)
            {
                if (g.world.instances[id].state != ItemState.InWorld) continue;
                Take(id);
                if (g.interaction.DepositStation(id)) delivered++;
            }
            wb.InstallAll(h2, false);
            {
                var (sp, _) = wb.MatSpot(h2.mat);
                LookFrom(sp + wb.Front * 0.6f + Vector3.up * 0.3f, sp + Vector3.up * 0.1f);
                yield return g.Capture(Path.Combine(outDir, "15c_hoverbot2_being_built.png"), 3);
            }
            Check("Hoverbot #2 is built from its delivered kit", wb.Complete(h2) && delivered == 28 && wb.InstalledIds(h2).All(kit2.Contains), $"{delivered} delivered; {wb.Summary(h2)}");
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
            foreach (var s in pr.skills.Where(s => s.Active)) g.UnlockSkill(s.id);
            g.pegRewards.GrantAll(new[] { "area_sweep" });   // Area Sweep is the full pegboard's bonus only (skill revamp)
            Check("tool skills unlock (levels, points, prerequisites)", pr.Has("bulk_pickup_count") && pr.Has("magnet_level") && pr.Has("scan_radius_m") && pr.Has("supermagnet"),
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
            // Magnetic glove (playtest 2026-10-25): levels I-X; one hold of B pulls in as many copies of the
            // magnetic part in the first slot as its level, nothing else
            var glove = Enumerable.Range(4, 7).Select(n => "magnet_" + n).Where(pr.unlocked.Contains).ToList();
            foreach (var id in glove) pr.unlocked.Remove(id);   // level III for the count
            var mag = g.world.instances.Where(Free).Where(i => g.world.DefOf(i.id).magnetic && i.Position.y < 0.3f)
                .FirstOrDefault(i => g.world.IdsOf(i.defId).Count(o => o != i.id && Free(g.world.instances[o]) && (g.world.instances[o].Position - i.Position).magnitude < 1.2f) >= 4);
            if (mag != null)
            {
                Take(mag.id);
                var copy = g.world.IdsOf(mag.defId).Where(o => Free(g.world.instances[o])).OrderBy(o => (g.world.instances[o].Position - mag.Position).sqrMagnitude).First();
                var at = g.world.instances[copy].Position;
                g.player.Teleport(at + new Vector3(0, -at.y, -0.5f));
                g.player.SetView(0, -0.6f);
                int before = g.inventory.Count, lvl = g.tools.MagnetLevel;
                for (float w = 0; w < 1.5f && g.interaction.busy.Count > 0; w += Time.deltaTime) yield return null;   // let the parts round the picked one settle
                int pulledNow = g.tools.Glove();
                float cdLeft = g.tools.CooldownLeft("magnetic_glove");
                int again = g.tools.Glove();   // recharging: nothing
                yield return g.Capture(Path.Combine(outDir, "37_skill_bar.png"), 2);
                yield return Frames(30);
                var got = g.inventory.items.Skip(before).ToList();
                g.tools.ResetState();
                // a part that isn't magnetic pulls nothing
                EmptyHands();
                var plain = g.world.instances.Where(Free).FirstOrDefault(i => !g.world.DefOf(i.id).magnetic);
                int none = -1;
                if (plain != null) { Take(plain.id); int b2 = g.inventory.Count; g.tools.Glove(); yield return Frames(10); none = g.inventory.Count - b2; }
                float cd3 = PlayerTools.Tuned("magnetic_glove", "cooldown_s", 3, -1);
                Check("Magnetic glove level III (key 1) pulls exactly 3 copies of the first slot's magnetic part, nothing else, then recharges (unlocks.json skill_tuning); a non-magnetic part pulls none",
                      lvl == 3 && pulledNow == 3 && got.Count == 3 && got.All(i => g.world.instances[i].defId == mag.defId) && none == 0 && again == 0
                      && Mathf.Abs(cdLeft - cd3) < 0.5f && g.tools.Slot("magnetic_glove") == 1,
                      $"level {lvl}: {got.Count} × {g.world.DefOf(mag.id).displayName} (all the same {got.All(i => g.world.instances[i].defId == mag.defId)}); cooldown {cdLeft:0.0} s of {cd3}, used again {again}; non-magnetic {none}");
            }
            else Check("Magnetic glove level III pulls exactly 3 copies of the first slot's magnetic part", false, "no cluster of a magnetic part found");
            foreach (var id in glove) pr.unlocked.Add(id);
            g.tools.ResetState();
            // a save with the old filter skills gets their points back
            var old = pr.ToDict();
            old["unlocked"] = ((List<object>)old["unlocked"]).Concat(new object[] { "filter_material", "filter_category" }).ToList();
            int pts = pr.skillPoints;
            pr.Load(old);
            // Part Sense I (skill revamp 2026-10-30): the nearest robot part within 2 m glows for 2 s, then 90 s recharge
            {
                g.tools.ResetState();
                var higher = Enumerable.Range(2, 9).Select(k => $"part_sense_{k}").Where(pr.unlocked.Contains).ToList();
                foreach (var s in higher) pr.unlocked.Remove(s);   // level I, which this check is about
                int rank = g.tools.Level("part_sense");
                var pme0 = g.player.transform.position;
                var close = g.world.instances.Where(i => i.state == ItemState.InWorld && g.world.DefOf(i.id).IsRobotComponent && !g.robotReserved.Contains(i.id))
                    .OrderBy(i => (i.Position - pme0).sqrMagnitude).First();
                var stand = close.Position + Vector3.right * 0.8f; stand.y = 0;
                g.player.Teleport(stand);   // within level I's 2 m
                yield return Frames(2);
                int sensed = g.tools.PartSense();
                int sid = g.tools.senseId;
                var pme = g.player.transform.position;
                var nearest = g.world.instances.Where(i => i.state == ItemState.InWorld && g.world.DefOf(i.id).IsRobotComponent && !g.robotReserved.Contains(i.id))
                    .OrderBy(i => (i.Position - pme).sqrMagnitude).FirstOrDefault();
                float cdS = g.tools.CooldownLeft("part_sense");
                if (sid >= 0)
                {
                    var at = g.world.instances[sid].Position;
                    LookFrom(at + new Vector3(0.7f, 0.9f, 0.6f), at + Vector3.Cross(Vector3.up, at - (at + new Vector3(0.7f, 0.9f, 0.6f))).normalized * 0.3f);
                    yield return g.Capture(Path.Combine(outDir, "38_part_sense.png"), 2);
                }
                float t0 = Time.time;
                while (Time.time - t0 < 2.3f) yield return null;
                Check("Part Sense I (key 5): the nearest robot part within 2 m glows for 2 s, then the skill recharges 90 s (screenshot 38)",
                      rank == 1 && sensed == 1 && nearest != null && sid == nearest.id && g.tools.senseId < 0 && Mathf.Abs(cdS - 90) < 0.5f && g.tools.Slot("part_sense") == 5,
                      $"rank {rank}; glowing {(sid >= 0 ? g.world.DefOf(sid).displayName : "none")} (nearest {nearest?.id}), after 2 s {g.tools.senseId}, cooldown {cdS:0.0} s");
                foreach (var s in higher) pr.unlocked.Add(s);
                g.player.Teleport(pme0);
                g.tools.ResetState();
            }
            Check("the glove has ten levels (I-X, level X: 10 parts, 30 s); a save with the retired filter skills refunds their points",
                  g.tools.MagnetLevel == 10 && PlayerTools.Tuned("magnetic_glove", "pull_items", 10, 0) == 10 && g.tools.CooldownOf("magnetic_glove") == 30 && pr.skillPoints == pts + 2 && !pr.unlocked.Contains("filter_material"),
                  $"level {g.tools.MagnetLevel}, pulls {PlayerTools.Tuned("magnetic_glove", "pull_items", 10, 0)}, cooldown {g.tools.CooldownOf("magnetic_glove")} s; points {pts} -> {pr.skillPoints}");
            foreach (var r in g.robots) r.enabled = true;

            EmptyHands();
            // Area Sweep: everything loose around you, up to the limit
            var hc = WorldGenerator.CrowdedFloorSpots(g.world, 2)[1];   // the second most crowded floor spot
            g.player.Teleport(hc + new Vector3(1.0f, 0, 0));
            g.player.SetView(Mathf.Atan2(1.0f, 0), -0.7f);
            yield return Frames(3);
            int b0 = g.inventory.Count;
            int swept = g.tools.AreaSweep();
            yield return new WaitForSeconds(2.5f);
            Check("Area Sweep collects the loose parts around you (overflow to the sorting tray)", swept > 0 && g.inventory.Count - b0 == swept - g.tools.lastToTray, $"{swept} parts swept, inventory +{g.inventory.Count - b0}, tray +{g.tools.lastToTray} (capacity {g.inventory.capacity}, now {g.inventory.Count})");

            // Request Pickup (H; called Automatic Storage Delivery before the skill revamp): a robot fetches the player's load and stores it
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
            Check("Request Pickup (formerly Automatic Storage Delivery): a robot takes your parts and delivers them", g.inventory.Count < handed.Count && handed.Any(Delivered),
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
            bool zTray = firstTarget >= 0 && g.InTray(g.world.instances[firstTarget].Position);   // the sorting tray comes first, zone or not
            Check("Task Coordination: an assigned robot works only in its zone (or empties the sorting tray)", firstTarget >= 0 && (dz <= zr.zoneRadius + 0.05f || zTray),
                  $"target {dz:0.00} m from the zone centre (radius {zr.zoneRadius}){(zTray ? ", in the sorting tray" : "")}");
            zr.AssignZone(null);

            // Sorting tray: parts put there are picked up by the robots first
            if (g.inventory.Count < 6) { g.player.Teleport(hc + new Vector3(1.0f, 0, 0)); yield return Frames(2); g.tools.ResetState(); g.tools.AreaSweep(); yield return Frames(5); }
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
            int atTray = 0;   // walking robots fetching from the tray at once (playtest 2026-10-26: one at a time)
            while (Time.realtimeSinceStartup - t2 < 60 && g.TrayCount > inTray - 3)
            {
                atTray = Mathf.Max(atTray, g.robots.Count(r => r.Active && r.locomotion != "hover" && r.targetId >= 0
                                                               && g.world.instances[r.targetId].state == ItemState.InWorld && g.InTray(g.world.instances[r.targetId].Position)));
                yield return null;
            }
            Time.timeScale = 1;
            Check("robots empty the sorting tray first, one walking robot at a time", put > 0 && inTray >= put && g.TrayCount <= inTray - 3 && atTray <= 1,
                  $"{put} put in, {inTray} in tray, {g.TrayCount} left; at most {atTray} walking robot(s) at the tray");
        }

        /// <summary>Playtest 2026-10-28, hands-on building through the player's own controls: a part held to a robot on
        /// the bench snaps on (in any order: the chassis can come last), a part can be taken out of the pool tray, a
        /// fitted part can be swapped or taken off until the robot is committed, and only a committed robot activates.
        /// The Hoverbots pool their kits (any unit's lower shell goes on any Hoverbot).</summary>
        IEnumerator GhostChecks()
        {
            var wb = g.workbench;
            var ic = g.interaction;
            IEnumerator AimAtRobot(RobotWorkbench.Project p)
            {
                var (spot, _) = wb.MatSpot(p.mat);
                var stand = spot + wb.Front * 0.75f;
                LookFrom(new Vector3(stand.x, PlayerController.Eye, stand.z), spot + Vector3.up * 0.05f);   // standing at the mat
                yield return Frames(2);
                ic.UpdateTarget();
            }
            IEnumerator TryOn(RobotWorkbench.Project p, int id)
            {
                if (g.world.instances[id].state == ItemState.Stored) wb.TakeFromPool(id); else Take(id);
                g.inventory.Select(g.world.instances[id].defId);
                Report(wb.Fit(p, id));   // workbench mode (the bench screen's arrows)
                yield return Frames(2);
            }
            void Report((bool ok, string text) r) { if (!r.ok) Debug.Log("[ghost] " + r.text); }
            string Key(int id) => Robot.PartKey(g.world.DefOf(id));
            foreach (var (unit, chassis) in new[] { ("retrobot", new[] { "torso" }), ("hoverbot", new[] { "lower saucer shell", "upper saucer shell" }) })
            {
                var p = wb.P(unit);
                var rb = g.robots.Find(r => r.id == unit);
                LevelTo(p.level);
                var parts = wb.PartIds(p);
                var early = parts.Where(id => !chassis.Contains(Key(id)) && p.requires.TryGetValue(Key(id), out var req) && req.Any(chassis.Contains)).Take(3).ToList();
                int v0 = SolidVerts(rb);
                yield return AimAtRobot(p);
                bool aimed = ic.targetType == "station" && !wb.AtPool(ic.targetPoint) && wb.ActiveOnMat(ic.MatAt(ic.targetPoint)) == p;
                foreach (var id in early) yield return TryOn(p, id);
                int v1 = SolidVerts(rb);
                var keys = early.Select(Key).ToList();
                Check($"{p.name}: {string.Join(", ", keys)} held to the robot snap onto the ghost before the {string.Join(" and ", chassis)}",
                      aimed && early.Count > 0 && early.All(wb.IsFitted) && keys.All(wb.InstalledKeys(p).Contains) && !chassis.Any(wb.InstalledKeys(p).Contains) && v1 > v0,
                      $"aim {ic.targetType}, {early.Count(wb.IsFitted)}/{early.Count} fitted, solid geometry {v0} -> {v1} vertices");
                var (spot, _) = wb.MatSpot(p.mat);
                LookFrom(spot + wb.Front * 0.6f + Vector3.up * 0.35f + Vector3.Cross(Vector3.up, wb.Front) * 0.2f, spot + Vector3.up * 0.14f);
                yield return g.Capture(Path.Combine(outDir, $"ghost_{unit}_before_chassis.png"), 4);

                // a fitted part comes off again (into the hands), and goes back on
                var off = wb.TakeOff(p, early[0]);
                yield return Frames(2);
                Check($"{p.name}: a fitted part comes off into the hands until committed", off.ok && !wb.IsFitted(early[0]) && g.inventory.Has(early[0]) && !wb.InstalledKeys(p).Contains(keys[0]), off.text);
                yield return TryOn(p, early[0]);

                // the chassis: delivered to the pool, taken out of the tray by aiming at it, then tried on
                var shells = parts.Where(id => chassis.Contains(Key(id))).ToList();
                if (unit == "hoverbot")
                {
                    // the Hoverbots pool their kits: Hoverbot #2's lower shell goes on the first Hoverbot
                    var other = wb.PartIds(wb.P("hoverbot_2")).First(id => Key(id) == chassis[0]);
                    shells[shells.FindIndex(id => Key(id) == chassis[0])] = other;
                }
                foreach (var id in shells) { Take(id); ic.DepositStation(id); }
                yield return Frames(2);
                for (float t = 0; t < 8 && wb.BinSettling; t += Time.deltaTime) yield return null;   // it falls onto the heap first
                var pos = wb.PoolModelPosition(shells[0]);
                if (pos is Vector3 pp) { var stand = pp + wb.Front * 0.8f; LookFrom(new Vector3(stand.x, PlayerController.Eye, stand.z), pp); }   // standing at the bin (it is 0.5 m deep)
                yield return Frames(2);
                ic.UpdateTarget();
                int aimedPart = ic.targetPoolPart;
                Check($"{p.name}: the {Key(shells[0])} lies in the parts box where the crosshair finds it", pos != null && aimedPart == shells[0],
                      $"aimed at {(aimedPart >= 0 ? g.world.DefOf(aimedPart).displayName : "nothing")}; target {ic.targetType}, at pool {wb.AtPool(ic.targetPoint)}, bench-local {Batch.G(wb.transform.InverseTransformPoint(ic.targetPoint))}, model {pos}");
                foreach (var id in shells) yield return TryOn(p, id);
                Check($"{p.name}: the {string.Join(" and ", chassis)} still goes on afterwards" + (unit == "hoverbot" ? " (one of them Hoverbot #2's)" : ""),
                      shells.All(wb.IsFitted) && chassis.All(wb.InstalledKeys(p).Contains) && SolidVerts(rb) > v1, wb.Summary(p));
                yield return g.Capture(Path.Combine(outDir, $"ghost_{unit}_chassis_added.png"), 4);
                Check($"{p.name}: each robot part is in exactly one place", PartsIntact(out var d), d);
            }

            // swap: another head tried on in place of a fitted one; the first comes back into the hands
            {
                var p = wb.P("hoverbot");
                int head = wb.InstalledIds(p).First(id => Robot.PartKey(g.world.DefOf(id)) == "head");
                int head2 = wb.PartIds(wb.P("hoverbot_2")).First(id => Robot.PartKey(g.world.DefOf(id)) == "head");
                yield return TryOn(p, head2);
                Check("Hoverbot: Hoverbot #2's head swaps in for the fitted head, which comes back into the hands",
                      wb.IsFitted(head2) && !wb.IsFitted(head) && g.inventory.Has(head) && wb.InstalledIds(p).Count(id => Robot.PartKey(g.world.DefOf(id)) == "head") == 1,
                      $"{wb.DonorOrigin(p, ModuleSlot.Head) ?? "own"} head on");
            }

            // the part picker (playtest 2026-10-28): at the robot, the wheel / Q / E scroll through the found parts
            // that fit it (pool and hands), LMB tries the shown one on, V applies
            {
                var p = wb.P("unibot");
                LevelTo(p.level);
                foreach (var id in g.inventory.items.Where(x => g.world.DefOf(x).IsRobotPart).ToList()) ic.DepositStation(id);
                var kit = wb.PartIds(p).Where(id => g.world.instances[id].state != ItemState.Installed).Take(3).ToList();
                foreach (var id in kit) { Take(id); ic.DepositStation(id); }
                // LMB on the parts bin (carrying a part) only puts it in: the workbench does not open (playtest 2026-10-31)
                int binned = wb.PartIds(p).First(id => g.world.instances[id].state != ItemState.Installed && !kit.Contains(id) && !g.inventory.Has(id));
                Take(binned);
                var bp = wb.PoolPoint;
                var binStand = bp + wb.Front * 0.75f;
                LookFrom(new Vector3(binStand.x, PlayerController.Eye, binStand.z), bp);
                yield return Frames(2);
                ic.UpdateTarget();
                bool atBin = ic.targetType == "station" && wb.AtPool(ic.targetPoint);
                ic.Primary();
                yield return Frames(4);
                yield return g.Capture(Path.Combine(outDir, "parts_bin.png"), 2);
                Check("LMB on the robot parts bin only puts the carried part in; the workbench stays closed",
                      atBin && g.state == "play" && !g.inventory.Has(binned) && g.world.instances[binned].state == ItemState.Stored,
                      $"aim {ic.targetType} at bin {wb.AtPool(ic.targetPoint)}, state {g.state}, part {g.world.instances[binned].state}");
                // LMB on the bench (carrying a part) opens workbench mode on its blueprints; the part is in the bin
                int carried = wb.PartIds(p).First(id => g.world.instances[id].state != ItemState.Installed && !kit.Contains(id) && id != binned);
                Take(carried);
                yield return AimAtRobot(p);
                bool aimed = ic.targetType == "station";
                ic.Primary();
                yield return Frames(4);
                bool opened = g.state == "workbench" && !g.hud.WorkbenchOnParts && !g.inventory.Has(carried) && g.world.instances[carried].state == ItemState.Stored;
                yield return g.Capture(Path.Combine(outDir, "ghost_bench_opened.png"), 2);
                Check("Unibot: LMB on the Robot Workbench opens workbench mode (blueprints); the carried part goes in the parts bin; no picker outside it",
                      aimed && opened && !ic.PickerActive, $"aim {ic.targetType}, state {g.state}, on parts {g.hud.WorkbenchOnParts}, part {g.world.instances[carried].state}");
                g.hud.WorkbenchChoose(p.id);
                yield return Frames(2);
                var slot = wb.Slots(p).First(x => x.ring.Count > 0 && x.on < 0);
                int second = slot.ring[0];
                wb.Fit(p, second, true);
                g.SetState("play");
                yield return Frames(2);
                bool fitted = wb.IsFitted(second) && !g.inventory.Has(second);
                Check("Unibot: on the parts page a found part from the box is fitted", fitted, $"{slot.key}: {g.world.DefOf(second).displayName}");
                wb.Commit(p);   // the bench screen's Apply
                yield return Frames(2);
                Check("Unibot: Apply locks the fitted part in", fitted && !wb.IsFitted(second) && wb.InstalledIds(p).Contains(second) && wb.FittedIds(p).Count == 0, wb.Summary(p));
                Check("each robot part is in exactly one place (picker)", PartsIntact(out var d), d);
            }

            // commit: complete the Retrobot by hand; it only activates once committed, then nothing comes off
            {
                var p = wb.P("retrobot");
                foreach (var id in wb.PartIds(p).Where(id => g.world.instances[id].state != ItemState.Installed).ToList()) yield return TryOn(p, id);
                bool blocked = wb.Complete(p) && !wb.CanActivate(p) && wb.StatusOf(p) != RobotWorkbench.Status.ReadyForActivation;
                int n = wb.Commit(p);
                var last = wb.InstalledIds(p)[0];
                Check("Retrobot: every part fitted, it activates only once committed",
                      blocked && n == wb.Required(p) && wb.CanActivate(p) && wb.FittedIds(p).Count == 0,
                      $"{n} committed, {wb.Summary(p)}");
                // an applied part comes off and goes back on in the same place, like a tried-on one (playtest 2026-10-29)
                var off = wb.TakeOff(p, last, true);
                bool offOk = off.ok && !wb.InstalledIds(p).Contains(last) && !wb.CanActivate(p);
                var on = wb.Fit(p, last, true);
                wb.Commit(p);
                int arm = wb.InstalledIds(p).Where(id => wb.MainSlotOf(id) == ModuleSlot.ArmL).DefaultIfEmpty(-1).First();
                var armOff = arm >= 0 ? wb.TakeOff(p, arm, true) : (false, "no left arm");
                bool armBack = armOff.Item1 && wb.Fit(p, arm, true).ok && wb.Commit(p) == 1;
                Check("Retrobot: an applied part (and an applied arm) comes off and back on in its place until Activate",
                      offOk && on.ok && armBack && wb.CanActivate(p), $"{off.text} / {on.text} / {armOff.Item2}");
                wb.Activate(p);
                Check("Retrobot: once activated its parts stay on", !wb.TakeOff(p, last).ok && !wb.Removable(p, last), wb.Summary(p));
            }

            // fitted parts survive save / load (and stay loose)
            {
                var p = wb.P("hoverbot");
                var before = string.Join(",", wb.FittedIds(p));
                g.SaveGame("autotest_fit");
                g.StartWorld(g.world.seed, null);
                g.LoadGame("autotest_fit");
                g.player.enabled = false;
                yield return Frames(4);
                Check("fitted (uncommitted) parts are kept through save / load", before != "" && string.Join(",", wb.FittedIds(p)) == before, before);
            }
        }

        /// <summary>Playtest 2026-10-28: a part picked up, dropped or put in the tray is drawn in the air by the world
        /// renderer (its world mesh, skin and wear), not by a stand-in with the old look; the Unibot tyre is round.</summary>
        IEnumerator FlightChecks()
        {
            var ic = g.interaction;
            var r = g.renderer;
            var tire = g.world.instances.FirstOrDefault(i => g.world.DefOf(i.id).geometryKey == "robot:tire");
            if (tire != null)
            {
                var m = r.WorldMeshFor(tire.id);
                // laid on open floor beside where it lies, tilted a little, to see it whole
                var def = g.world.DefOf(tire.id);
                var pos = new Vector3(tire.Position.x + 1.2f, 0, tire.Position.z + 1.2f);
                g.index.Remove(tire.id);
                g.world.SetTransform(tire.id, Matrix4x4.TRS(pos + Vector3.up * (MeshLibrary.UnitBounds(def.geometryKey).extents.y * def.worldScale + 0.002f), Quaternion.Euler(0, 0, 0), Vector3.one * def.worldScale));
                g.index.Insert(tire.id);
                g.renderer.Refresh(tire.id);
                LookFrom(new Vector3(pos.x + 0.55f, PlayerController.Eye, pos.z + 0.4f), pos);   // from standing height (lower puts the body under the floor)
                yield return g.Capture(Path.Combine(outDir, "flight_unibot_tire.png"), 4);
                Check("the Unibot tyre is a round rubber tyre (built like the robot's, not the 7-sided export)", m.vertexCount > 1000, $"{m.name}: {m.vertexCount} vertices, {tire.state} at {tire.Position}");
            }
            else Check("the Unibot tyre is a round rubber tyre", false, "no Unibot tyre in the world");
            var eye = g.player.transform.position;
            var part = g.world.instances.Where(i => i.state == ItemState.InWorld && ItemSkins.Applies(g.world.DefOf(i.id)) && !g.world.DefOf(i.id).IsRobotPart)
                .OrderBy(i => (i.Position - eye).sqrMagnitude).First();
            int id = part.id;
            ic.Pickup(id, true);
            yield return null;
            bool upFlying = r.IsFlying(id);
            float t0 = Time.time;
            while (Time.time - t0 < 0.4f) yield return null;
            bool upDone = !r.IsFlying(id) && g.inventory.Has(id);
            g.inventory.Select(part.defId);
            ic.DropInTray(id, false);
            yield return null;
            bool trayFlying = r.IsFlying(id) && r.IsHidden(id);
            var mid = g.held.HandWorld().GetColumn(3);
            LookFrom((Vector3)mid + Vector3.up * 0.25f - g.player.transform.forward * 0.35f, ic.TrayMatrix(g.world.DefOf(id)).GetColumn(3));
            yield return g.Capture(Path.Combine(outDir, "flight_mid_air.png"), 1);
            t0 = Time.time;
            while (Time.time - t0 < 0.5f) yield return null;
            bool trayDone = !r.IsFlying(id) && !r.IsHidden(id) && g.world.instances[id].state == ItemState.InWorld;
            Check($"a {g.world.DefOf(id).displayName} is drawn by the world renderer while it flies (picked up, put in the tray), then lies there normally",
                  upFlying && upDone && trayFlying && trayDone, $"pickup: flying {upFlying}, done {upDone}; tray: flying (hidden) {trayFlying}, done {trayDone}");

        }

        /// <summary>QA: writes every item geometry key's meshes (both LODs) to outDir/meshes/<key>__lod<n>.obj, one
        /// group per submesh, with the material slot in the group name; also a key list with one example item.</summary>
        /// Also (overlap audit 2026-10-29): the shape-mesh LODs the playtest look draws in the world (4-6), pegboard
        /// tools without an item, every robot model fully built (models/<type>.obj, model space, one group per mesh
        /// object) and the rest of the scene (scene/<root>.obj, world space); mesh_keys.tsv gains min / max world scale.
        void MeshDump()
        {
            var dir = Path.Combine(outDir, "meshes");
            Directory.CreateDirectory(dir);
            var keys = g.db.definitions.Values.GroupBy(d => d.geometryKey).Where(k => !string.IsNullOrEmpty(k.Key)).ToList();
            var list = new System.Text.StringBuilder();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var all = keys.Select(k => (k.Key, name: k.First().displayName, n: k.Count(), lo: k.Min(d => d.worldScale), hi: k.Max(d => d.worldScale))).ToList();
            foreach (var tk in WorkshopWreckage.Pegboard.ToolFactory.Keys)
                if (!all.Any(a => a.Key == tk)) all.Add((tk, tk, 0, 0f, 0f));
            foreach (var k in all)
            {
                list.Append(k.Key).Append('\t').Append(k.name).Append('\t').Append(k.n).Append('\t').Append(k.lo.ToString("R", inv)).Append('\t').Append(k.hi.ToString("R", inv)).Append('\n');
                var lods = new List<int> { MeshLibrary.LodInspect, MeshLibrary.LodWorld };
                if (MeshLibrary.Extra != null && WorkshopWreckage.Sandbox.ShapeMeshes.Has(k.Key)) lods.AddRange(new[] { 4, 5, 6 });
                foreach (int lod in lods)
                {
                    var e = MeshLibrary.Get(k.Key, lod);
                    if (e?.mesh == null) continue;
                    var sb = new System.Text.StringBuilder();
                    AppendVerts(sb, e.mesh, Matrix4x4.identity, inv);
                    for (int sm = 0; sm < e.mesh.subMeshCount; sm++)
                    {
                        sb.Append("g sub").Append(sm).Append("_slot").Append(e.slots[sm]).Append('\n');
                        var t = e.mesh.GetTriangles(sm);
                        for (int i = 0; i < t.Length; i += 3) sb.Append("f ").Append(t[i] + 1).Append(' ').Append(t[i + 1] + 1).Append(' ').Append(t[i + 2] + 1).Append('\n');
                    }
                    File.WriteAllText(Path.Combine(dir, k.Key.Replace(":", "_") + "__lod" + lod + ".obj"), sb.ToString());
                }
            }
            File.WriteAllText(Path.Combine(outDir, "mesh_keys.tsv"), list.ToString());
            Check("every item mesh dumped", keys.Count > 0, $"{keys.Count} geometry keys");

            // every robot model with all its parts, in model space (built at ~1 m; the tsv gives the world scale)
            var mdir = Path.Combine(outDir, "models");
            Directory.CreateDirectory(mdir);
            var mlist = new System.Text.StringBuilder();
            var temp = new List<GameObject>();
            void DumpModel(string name, RobotModel m, float scale)
            {
                mlist.Append(name).Append('\t').Append(scale.ToString("R", inv)).Append('\n');
                File.WriteAllText(Path.Combine(mdir, name + ".obj"), ObjOf(m.transform, inv));
            }
            foreach (var type in new[] { "retrobot", "unibot", "spiderbot", "caterpillarbot", "hoverbot", "sorting_arm" })
            {
                var have = new HashSet<string>();
                foreach (var p in g.workbench.projects.Where(p => p.type == type))
                {
                    have.UnionWith(p.requires.Keys); have.UnionWith(p.requires.Values.SelectMany(v => v)); have.UnionWith(p.partOrder);
                }
                var go = new GameObject("QA_model_" + type);
                temp.Add(go);
                RobotModel m = type switch
                {
                    "unibot" => go.AddComponent<UnibotModel>(), "spiderbot" => go.AddComponent<SpiderbotModel>(),
                    "caterpillarbot" => go.AddComponent<CaterpillarbotModel>(), "hoverbot" => go.AddComponent<HoverbotModel>(),
                    "sorting_arm" => go.AddComponent<SortingArmModel>(), _ => go.AddComponent<RetrobotModel>(),
                };
                m.Init();
                m.Build(have);
                var live = g.robots.Find(r => r.type == type);
                DumpModel(type, m, live != null ? live.GetComponentInChildren<RobotModel>(true).transform.localScale.x : 1f);
            }
            {
                var go = new GameObject("QA_model_radar");
                temp.Add(go);
                var m = go.AddComponent<PartRadarModel>();
                m.battery = true;
                m.Init();
                m.Build(new HashSet<string>(PartRadarModel.Parts));
                DumpModel("part_radar", m, 1f);
            }
            File.WriteAllText(Path.Combine(mdir, "models.tsv"), mlist.ToString());

            // the rest of the scene (room, furniture, workbench, pegboard, live robots...), world space, per root
            var sdir = Path.Combine(outDir, "scene");
            Directory.CreateDirectory(sdir);
            int roots = 0;
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (temp.Contains(root) || !root.activeInHierarchy) continue;
                var obj = ObjOf(root.transform, inv, true);
                if (obj.Length == 0) continue;
                File.WriteAllText(Path.Combine(sdir, root.name.Replace(' ', '_').Replace('/', '_') + "_" + roots++ + ".obj"), obj);
            }
            foreach (var go in temp) Object.Destroy(go);
            Check("every robot model and scene root dumped", true, $"{mlist.ToString().Split('\n').Length - 1} models, {roots} scene roots");
        }

        /// <summary>QA (MeshDump): "v x y z r g b nx ny nz" per vertex (white without vertex colours), so the overlap
        /// audit can tell faces that shade differently from coincident ones that shade the same.</summary>
        static void AppendVerts(System.Text.StringBuilder sb, Mesh mesh, Matrix4x4 mtx, System.Globalization.CultureInfo inv)
        {
            var vs = mesh.vertices; var ns = mesh.normals; var cs = mesh.colors;
            for (int i = 0; i < vs.Length; i++)
            {
                var v = mtx.MultiplyPoint3x4(vs[i]);
                var n = i < ns.Length ? mtx.MultiplyVector(ns[i]).normalized : Vector3.zero;
                var c = i < cs.Length ? cs[i] : Color.white;
                sb.Append("v ").Append(v.x.ToString("R", inv)).Append(' ').Append(v.y.ToString("R", inv)).Append(' ').Append(v.z.ToString("R", inv))
                  .Append(' ').Append(c.r.ToString("0.###", inv)).Append(' ').Append(c.g.ToString("0.###", inv)).Append(' ').Append(c.b.ToString("0.###", inv))
                  .Append(' ').Append(n.x.ToString("0.####", inv)).Append(' ').Append(n.y.ToString("0.####", inv)).Append(' ').Append(n.z.ToString("0.####", inv)).Append('\n');
            }
        }

        /// <summary>QA (MeshDump): every active mesh object under `root` as OBJ, one group per object and submesh
        /// ("g <path>|sub<n>"), in root space (or world space).</summary>
        static string ObjOf(Transform root, System.Globalization.CultureInfo inv, bool world = false)
        {
            var sb = new System.Text.StringBuilder();
            int baseV = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(false))
            {
                var mesh = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mesh == null || mr == null || !mr.enabled || !mesh.isReadable) continue;
                var mtx = world ? mf.transform.localToWorldMatrix : root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var path = mf.name;
                for (var t = mf.transform.parent; t != null && t != root; t = t.parent) path = t.name + "/" + path;
                AppendVerts(sb, mesh, mtx, inv);
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    if (mesh.GetTopology(sm) != MeshTopology.Triangles) continue;
                    sb.Append("g ").Append(path.Replace(' ', '_')).Append("|sub").Append(sm).Append('\n');
                    var t = mesh.GetTriangles(sm);
                    for (int i = 0; i < t.Length; i += 3) sb.Append("f ").Append(t[i] + 1 + baseV).Append(' ').Append(t[i + 1] + 1 + baseV).Append(' ').Append(t[i + 2] + 1 + baseV).Append('\n');
                }
                baseV += mesh.vertexCount;
            }
            return sb.ToString();
        }

        /// <summary>QA close-ups: one part of each listed geometry key laid on open floor in front of the player,
        /// photographed from two sides (partshot_<key>_a/b.png). Keys from --apkeys=a,b (default: the limb housing).</summary>
        IEnumerator PartShots()
        {
            var keys = (g.args.TryGetValue("apkeys", out var ks) ? ks : "mach:limb_housing").Split(',');
            foreach (var key in keys)
            {
                // a painted one first (bare metal glares in the close-up)
                var inst = g.world.instances.Where(i => g.world.DefOf(i.id).geometryKey == key && i.state == ItemState.InWorld)
                    .OrderBy(i => g.world.DefOf(i.id).material == "painted" ? 0 : 1).FirstOrDefault();
                if (inst == null) { Check($"a {key} lies in the world", false, "none found"); continue; }
                var def = g.world.DefOf(inst.id);
                // in the hands and turned in the inspect view (F): large, centred, slowly rotating
                string tag = key.Replace(":", "_");
                float size = MeshLibrary.UnitBounds(def.geometryKey).size.magnitude * def.worldScale;
                // where it lies, in the room's own light
                g.player.enabled = false;
                g.hud.SetHudVisible(false);
                float far = 0.35f + size * 1.5f;
                LookFrom(inst.Position + new Vector3(far * 0.6f, far * 0.7f, far * 0.6f), inst.Position);
                yield return Frames(6);
                yield return g.Capture(Path.Combine(outDir, $"partshot_{tag}_w.png"), 1);
                // F inspects what the crosshair is on before what is in the hands: aim at open air
                var air = inst.Position + Vector3.up * 2.2f;
                LookFrom(air, air + new Vector3(1, 0.25f, 0.3f));
                yield return Frames(2);
                g.player.enabled = true;
                g.index.Remove(inst.id);
                g.world.SetState(inst.id, ItemState.Carried, "player");
                g.inventory.Add(inst.id);
                g.inventory.Select(inst.defId);
                g.interaction.UpdateTarget();
                bool held = g.interaction.targetType != "item";   // else F would inspect the aimed item
                g.hud.SetHudVisible(false);
                g.interaction.BeginInspect();
                yield return Frames(25);
                yield return g.Capture(Path.Combine(outDir, $"partshot_{tag}_a.png"), 1);
                yield return Frames(70);
                yield return g.Capture(Path.Combine(outDir, $"partshot_{tag}_b.png"), 1);
                g.interaction.EndInspect();
                g.hud.SetHudVisible(true);
                // back where it lay
                g.inventory.Remove(inst.id);
                g.world.SetState(inst.id, ItemState.InWorld, "world");
                g.index.Insert(inst.id);
                Check($"close-ups of {def.displayName} ({key})", held, $"{(held ? "" : "an aimed item was inspected instead; ")}{MeshLibrary.Get(key, MeshLibrary.LodWorld).mesh.vertexCount} vertices, {size:0.00} m");
            }
        }

        /// <summary>Playtest 2026-10-29 ("the robot parts we find look different in size and scale from the parts on
        /// the robots"): every robot part kind of each first unit, cut out of its robot model (built with only that
        /// part, at the robot's world scale) and floated beside its loose item at world size. partcompare/<type>_<key>.png
        /// (robot left, loose right) and partcompare.tsv (both sizes, largest side first, in mm).</summary>
        IEnumerator PartCompare()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var dir = Path.Combine(outDir, "partcompare");
            Directory.CreateDirectory(dir);
            var tsv = new System.Text.StringBuilder("type\tkey\tgeometry_key\trobot_mm\tloose_mm\tratio\tloose_xyz_mm\n");
            var lo = g.layout.roomMin; var hi = g.layout.roomMax;
            var spawn = Shared.Pos(g.layout.spawn.x, 0, g.layout.spawn.z);
            var mid = new Vector3((lo.x + hi.x) / 2, 0, -(lo.z + hi.z) / 2);
            var fwd = mid - spawn; fwd.y = 0; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            var stage = spawn + fwd * 1.4f + Vector3.up * 1.3f;
            g.hud.SetHudVisible(false);
            // a close-up camera of its own over the player's (the player cannot float at the stage), and a plain
            // grey card behind the parts
            var main = Camera.main;
            var cam = new GameObject("QA_compare_cam").AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.depth = main.depth + 10;
            cam.fieldOfView = 30;
            cam.nearClipPlane = 0.005f;
            var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(card.GetComponent<Collider>());
            card.GetComponent<Renderer>().material.color = new Color(0.42f, 0.43f, 0.45f);
            string Mm(Vector3 v) { var a = new[] { v.x, v.y, v.z }.OrderByDescending(x => x).Select(x => (x * 1000).ToString("0", inv)); return string.Join("x", a); }
            Bounds BoundsOf(GameObject go)
            {
                var rs = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
                if (rs.Count == 0) return new Bounds(go.transform.position, Vector3.zero);
                var b = rs[0].bounds;
                foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
                return b;
            }
            void Put(GameObject go, Vector3 at) { var b = BoundsOf(go); go.transform.position += at - b.center; }
            // every robot's parts, then the Part Radar's (built at true size; its cell is a model of its own)
            var jobs = new List<(string type, float scale, ItemDefinition def, string key)>();
            foreach (var type in new[] { "retrobot", "unibot", "spiderbot", "caterpillarbot", "hoverbot", "sorting_arm" })
            {
                var live = g.robots.Find(r => r.type == type);
                float scale = live != null ? live.GetComponentInChildren<RobotModel>(true).transform.localScale.x : 1f;
                foreach (var id in g.workbench.PartIds(g.workbench.P(type))) jobs.Add((type, scale, g.world.DefOf(id), Robot.PartKey(g.world.DefOf(id))));
            }
            foreach (var did in g.db.order)
            {
                var d = g.db.Get(did);
                if (d.subcategory == "part_radar") jobs.Add(("part_radar", 1f, d, d.craftingPurpose.Substring(d.craftingPurpose.LastIndexOf(':') + 1)));
            }
            foreach (var (type, scale, def, key) in jobs)
            {
                {
                    var go = new GameObject("QA_cut_" + type);
                    RobotModel m = type switch
                    {
                        "unibot" => go.AddComponent<UnibotModel>(), "spiderbot" => go.AddComponent<SpiderbotModel>(),
                        "caterpillarbot" => go.AddComponent<CaterpillarbotModel>(), "hoverbot" => go.AddComponent<HoverbotModel>(),
                        "sorting_arm" => go.AddComponent<SortingArmModel>(), "part_radar" => go.AddComponent<PartRadarModel>(), _ => go.AddComponent<RetrobotModel>(),
                    };
                    if (m is PartRadarModel prm && key == "battery") prm.battery = true;
                    m.Init();
                    m.Build(new HashSet<string> { key });
                    go.transform.localScale = Vector3.one * scale;
                    yield return null;
                    var rb = BoundsOf(go);
                    var loose = HeldView.MakeModel(def);
                    loose.transform.localScale = Vector3.one * def.worldScale;
                    yield return null;
                    var lb = BoundsOf(loose);
                    float big = Mathf.Max(rb.size.magnitude, lb.size.magnitude, 0.03f);
                    float gap = 0.04f + big * 0.15f;
                    Put(go, stage - right * (rb.extents.magnitude * 0.5f + gap));
                    Put(loose, stage + right * (lb.extents.magnitude * 0.5f + gap));
                    float width = rb.size.magnitude * 0.5f + lb.size.magnitude * 0.5f + gap * 2;
                    float dist = Mathf.Max(0.12f, width * 0.5f / Mathf.Tan(24 * Mathf.Deg2Rad) * 1.15f);
                    var eye = stage - fwd * dist + Vector3.up * dist * 0.35f;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(stage - eye));
                    card.transform.SetPositionAndRotation(stage + fwd * (big + 0.05f), Quaternion.LookRotation(fwd));
                    card.transform.localScale = Vector3.one * (width * 3 + 0.3f);
                    string tag = (type + "_" + key).Replace(' ', '_');
                    yield return g.Capture(Path.Combine(dir, tag + ".png"), 4);
                    float ratio = lb.size.magnitude > 0 ? rb.size.magnitude / lb.size.magnitude : 0;
                    tsv.Append(type).Append('\t').Append(key).Append('\t').Append(def.geometryKey).Append('\t').Append(Mm(rb.size)).Append('\t')
                       .Append(Mm(lb.size)).Append('\t').Append(ratio.ToString("0.00", inv)).Append('\t')
                       .Append(string.Join("x", new[] { 0, 1, 2 }.Select(i => (MeshLibrary.UnitBounds(def.geometryKey).size[i] * def.worldScale * 1000).ToString("0", inv)))).Append('\n');
                    Object.Destroy(go); Object.Destroy(loose);
                }
            }
            g.hud.SetHudVisible(true);
            Object.Destroy(cam.gameObject); Object.Destroy(card);
            File.WriteAllText(Path.Combine(outDir, "partcompare.tsv"), tsv.ToString());
            Check("every robot part kind compared with its robot (partcompare.tsv)", true, $"{tsv.ToString().Count(c => c == '\n') - 1} parts");
        }

        /// <summary>Playtest 2026-10-29 ("the tracks look hollow", "the saucer shells have no inside"): every loose robot
        /// part floated in the room and seen from front, back, left, right, above and below,
        /// partsides/<type>_<key>_<0-5>.png, so holes and missing backs show.</summary>
        IEnumerator PartSides()
        {
            var dir = Path.Combine(outDir, "partsides");
            Directory.CreateDirectory(dir);
            var spawn = Shared.Pos(g.layout.spawn.x, 0, g.layout.spawn.z);
            var stage = spawn + Vector3.up * 1.4f;
            g.hud.SetHudVisible(false);
            g.player.enabled = false;
            var main = Camera.main;
            var cam = new GameObject("QA_sides_cam").AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.depth = main.depth + 10;
            cam.fieldOfView = 30;
            cam.nearClipPlane = 0.005f;
            var views = new[] { new Vector3(0, 0.25f, -1), new Vector3(0, 0.25f, 1), new Vector3(-1, 0.25f, 0), new Vector3(1, 0.25f, 0), new Vector3(0.15f, 1, -0.1f), new Vector3(0.15f, -1, -0.1f) };
            int n = 0;
            foreach (var did in g.db.order)
            {
                var def = g.db.Get(did);
                if (!def.geometryKey.StartsWith("robot:part:") || def.craftingPurpose.StartsWith("robot:hoverbot_") || def.craftingPurpose.StartsWith("robot:sorting_arm_")) continue;
                var go = HeldView.MakeModel(def);
                go.transform.localScale = Vector3.one * def.worldScale;
                go.transform.position = stage;
                yield return null;
                var rs = go.GetComponentsInChildren<Renderer>();
                var b = rs[0].bounds; foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
                float dist = Mathf.Max(0.12f, b.size.magnitude * 0.5f / Mathf.Tan(15 * Mathf.Deg2Rad) * 1.15f);
                string tag = def.geometryKey.Substring("robot:part:".Length).Replace(':', '_');
                for (int k = 0; k < views.Length; k++)
                {
                    var eye = b.center + views[k].normalized * dist;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(b.center - eye, Mathf.Abs(views[k].normalized.y) > 0.9f ? Vector3.forward : Vector3.up));
                    yield return g.Capture(Path.Combine(dir, $"{tag}_{k}.png"), 1);
                }
                Object.Destroy(go);
                n++;
            }
            Object.Destroy(cam.gameObject);
            g.player.enabled = true;
            g.hud.SetHudVisible(true);
            Check("every loose robot part seen from six sides (partsides/)", n > 0, $"{n} parts");
        }

        /// <summary>Part audit 2026-10-29 ("a render of each robot part as it looks in the workshop, compared to how it
        /// looks on the robot"): per part four shots in the room's own light, partaudit/<type>_<key>_{a,b,c,d}.png:
        /// a = the finished robot on the floor, b = the same robot ghosted with only this part solid, c = one loose
        /// copy where it lies in the workshop, seen from as far as the robot shots (same scale), d = that copy close up.
        /// partaudit.tsv lists where each copy lay.</summary>
        IEnumerator PartAudit()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var dir = Path.Combine(outDir, "partaudit");
            Directory.CreateDirectory(dir);
            var tsv = new System.Text.StringBuilder("type\tkey\tname\tloose_mm\tlies\tshot_c_m\n");
            var lo = g.layout.roomMin; var hi = g.layout.roomMax;
            var spawn = Shared.Pos(g.layout.spawn.x, 0, g.layout.spawn.z);
            var mid = new Vector3((lo.x + hi.x) / 2, 0, -(lo.z + hi.z) / 2);
            var fwd = mid - spawn; fwd.y = 0; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            var stage = spawn + fwd * 1.6f;
            g.hud.SetHudVisible(false);
            g.player.enabled = false;
            var main = Camera.main;
            var cam = new GameObject("QA_audit_cam").AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.depth = main.depth + 10;
            cam.fieldOfView = 30;
            cam.nearClipPlane = 0.005f;
            var ghost = EnvMaterials.NewGhost();
            ghost.SetColor("_Color", new Color(0.8f, 0.85f, 0.9f, 0.18f));
            string Mm(Vector3 v) { var a = new[] { v.x, v.y, v.z }.OrderByDescending(x => x).Select(x => (x * 1000).ToString("0", inv)); return string.Join("x", a); }
            Bounds BoundsOf(GameObject go)
            {
                var rs = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
                if (rs.Count == 0) return new Bounds(go.transform.position, Vector3.zero);
                var b = rs[0].bounds;
                foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
                return b;
            }
            float Dist(float size) => Mathf.Max(0.15f, size * 0.5f / Mathf.Tan(15 * Mathf.Deg2Rad) * 1.25f);
            // a view of `at` from `dist` away, slightly from above, turned until nothing solid is in the way
            void Aim(Vector3 at, float dist, Vector3 baseDir)
            {
                Vector3 eye = at;
                for (int k = 0; k < 12; k++)
                {
                    var d = Quaternion.Euler(0, k * 30f, 0) * baseDir;
                    float up = k < 6 ? 0.55f : 1.2f;
                    eye = at + (new Vector3(d.x, 0, d.z).normalized + Vector3.up * up).normalized * dist;
                    if (!Physics.Linecast(eye, at + Vector3.up * 0.01f)) break;
                }
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            }
            RobotModel Make(string type, float scale, HashSet<string> have)
            {
                var go = new GameObject("QA_audit_" + type);
                RobotModel m = type switch
                {
                    "unibot" => go.AddComponent<UnibotModel>(), "spiderbot" => go.AddComponent<SpiderbotModel>(),
                    "caterpillarbot" => go.AddComponent<CaterpillarbotModel>(), "hoverbot" => go.AddComponent<HoverbotModel>(),
                    "sorting_arm" => go.AddComponent<SortingArmModel>(), "part_radar" => go.AddComponent<PartRadarModel>(), _ => go.AddComponent<RetrobotModel>(),
                };
                if (m is PartRadarModel prm && have.Contains("battery")) prm.battery = true;
                m.Init();
                m.Build(have);
                go.transform.localScale = Vector3.one * scale;
                return m;
            }
            void Floor(GameObject go) { var b = BoundsOf(go); go.transform.position += new Vector3(stage.x - b.center.x, -b.min.y, stage.z - b.center.z); }
            var jobs = new List<(string type, float scale, ItemDefinition def, string key)>();
            foreach (var type in new[] { "retrobot", "unibot", "spiderbot", "caterpillarbot", "hoverbot", "sorting_arm" })
            {
                var live = g.robots.Find(r => r.type == type);
                float scale = live != null ? live.GetComponentInChildren<RobotModel>(true).transform.localScale.x : 1f;
                foreach (var did in g.db.RobotPartIds(type)) jobs.Add((type, scale, g.db.Get(did), Robot.PartKey(g.db.Get(did))));
            }
            foreach (var did in g.db.order)
            {
                var d = g.db.Get(did);
                if (d.subcategory == "part_radar") jobs.Add(("part_radar", 1f, d, d.craftingPurpose.Substring(d.craftingPurpose.LastIndexOf(':') + 1)));
            }
            // the live robots and the parts' world copies stay where they are; robots walking into a shot are parked
            foreach (var r in g.robots) r.gameObject.SetActive(false);
            var face = Quaternion.LookRotation(-fwd) * Quaternion.Euler(0, -25, 0);
            int shots = 0, missing = 0;
            foreach (var (type, scale, def, key) in jobs)
            {
                string tag = (type + "_" + key).Replace(' ', '_');
                var all = new HashSet<string>(jobs.Where(j => j.type == type).Select(j => j.key));
                if (type == "part_radar") all.Remove("battery");
                // a: the finished robot
                var full = Make(type, scale, all);
                full.transform.rotation = face;
                yield return null;
                Floor(full.gameObject);
                var rb = BoundsOf(full.gameObject);
                float dist = Dist(rb.size.magnitude);
                Aim(rb.center, dist, -fwd);
                yield return g.Capture(Path.Combine(dir, tag + "_a.png"), 3);
                // b: the robot ghosted, only this part solid (the same pose, so it shows where the part sits)
                var rest = Make(type, scale, new HashSet<string>(all.Where(k => k != key)));
                var part = Make(type, scale, new HashSet<string> { key });
                foreach (var m in new[] { rest, part }) { m.transform.rotation = face; m.transform.position = full.transform.position; }
                Object.Destroy(full.gameObject);
                foreach (var r in rest.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(_ => ghost).ToArray();
                yield return null;
                yield return g.Capture(Path.Combine(dir, tag + "_b.png"), 3);
                Object.Destroy(rest.gameObject); Object.Destroy(part.gameObject);
                // e: a loose copy where it lies in the wreckage, as the player comes across it
                var inst = g.world.IdsOf(def.id).Select(i => g.world.instances[i]).FirstOrDefault(i => i.state == ItemState.InWorld);
                if (inst == null) { missing++; tsv.Append($"{type}\t{key}\t{def.displayName}\t\tno copy in the wreckage\t\n"); continue; }
                string lies = inst.owner.StartsWith("stage:") ? "staging pile" : "in the wreckage";
                var wb = g.index.WorldBounds(inst.id);
                Aim(wb.center, 0.9f, -fwd);
                yield return g.Capture(Path.Combine(dir, tag + "_e.png"), 3);
                // c, d: the same copy set down on the open floor in its rest pose beside the finished robot (same
                // scale in one frame), then close up; put back where it lay afterwards
                var was = g.world.instances[inst.id].transform;
                var full2 = Make(type, scale, all);
                full2.transform.rotation = face;
                yield return null;
                Floor(full2.gameObject);
                var fb = BoundsOf(full2.gameObject);
                var right = Vector3.Cross(Vector3.up, -fwd).normalized;   // the camera's right, seen from -fwd
                var spot = stage + right * (fb.extents.x + fb.extents.z + 0.08f);
                var rot = Quaternion.Euler(0, 20, 0) * (def.restMode == RestMode.Side ? Quaternion.Euler(90, 0, 0) : def.restMode == RestMode.Long ? Quaternion.Euler(0, 0, 90) : Quaternion.identity);
                g.index.Remove(inst.id);
                g.world.SetTransform(inst.id, Matrix4x4.TRS(spot + Vector3.up * 0.5f, rot, Vector3.one * def.worldScale));
                g.index.Insert(inst.id);
                var sb = g.index.WorldBounds(inst.id);
                g.index.Remove(inst.id);
                g.world.SetTransform(inst.id, Matrix4x4.TRS(spot + Vector3.up * (0.5f - sb.min.y + 0.001f) + (spot - sb.center).x * Vector3.right + (spot - sb.center).z * Vector3.forward, rot, Vector3.one * def.worldScale));
                g.index.Insert(inst.id);
                yield return null;
                var both = fb; both.Encapsulate(g.index.WorldBounds(inst.id));
                Aim(both.center, Dist(both.size.magnitude), -fwd);
                yield return g.Capture(Path.Combine(dir, tag + "_c.png"), 3);
                var refMat = full2.GetComponentInChildren<Renderer>().sharedMaterial;
                Object.Destroy(full2.gameObject);
                wb = g.index.WorldBounds(inst.id);
                Aim(wb.center, Dist(wb.size.magnitude) * 1.4f, -fwd);
                yield return g.Capture(Path.Combine(dir, tag + "_d.png"), 3);
                // f: a fixed camera 0.35 m away and a 40 mm reference cube beside the part, so sizes compare between runs
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(cube.GetComponent<Collider>());
                cube.GetComponent<Renderer>().sharedMaterial = refMat;
                cube.transform.localScale = Vector3.one * 0.04f;
                cube.transform.position = new Vector3(wb.center.x, 0.02f, wb.center.z) + right * (wb.extents.x + wb.extents.z + 0.04f);
                cube.transform.rotation = Quaternion.LookRotation(fwd);
                yield return null;
                Aim((wb.center + cube.transform.position) * 0.5f, 0.35f, -fwd);
                yield return g.Capture(Path.Combine(dir, tag + "_f.png"), 3);
                Object.Destroy(cube);
                g.index.Remove(inst.id);
                g.world.SetTransform(inst.id, was);
                g.index.Insert(inst.id);
                shots++;
                tsv.Append(type).Append('\t').Append(key).Append('\t').Append(def.displayName).Append('\t').Append(Mm(wb.size)).Append('\t')
                   .Append(lies).Append('\t').Append(dist.ToString("0.00", inv)).Append('\n');
            }
            foreach (var r in g.robots) r.gameObject.SetActive(true);
            Object.Destroy(cam.gameObject);
            g.player.enabled = true;
            g.hud.SetHudVisible(true);
            File.WriteAllText(Path.Combine(outDir, "partaudit.tsv"), tsv.ToString());
            Check("every robot part shot on its robot and in the workshop (partaudit.tsv)", missing == 0, $"{shots} parts, {missing} without a copy");
        }

        /// <summary>Puts a part into the player's hands, wherever it lies (test setup).</summary>
        void Take(int id)
        {
            var i = g.world.instances[id];
            if (i.state == ItemState.InWorld) { g.index.Remove(id); g.world.SetState(id, ItemState.Carried, "player"); }
            if (i.state == ItemState.Carried && i.owner == "player" && !g.inventory.Has(id)) g.inventory.Add(id);
        }

        /// <summary>Stores every copy of a definition that the player could pick up (loose, in hand or in the
        /// wrong place) into its destination; true if the destination is complete afterwards.</summary>
        bool CompleteDef(int defId)
        {
            string box = g.db.Get(defId).storageBox;
            if (g.world.IdsOf(defId).Any(i => g.world.instances[i].state == ItemState.Carried && g.world.instances[i].owner != "player")) return false;
            foreach (var i in g.world.IdsOf(defId).ToList())
            {
                var inst = g.world.instances[i];
                if (g.progress.accepted.Contains(i) || inst.state == ItemState.Installed) continue;
                if (inst.state == ItemState.Stored && inst.owner.StartsWith("box:")) { g.storage.Remove(inst.owner.Substring(4), i); g.world.SetState(i, ItemState.InWorld, ""); g.index.Insert(i); }
                Take(i);
                g.inventory.Remove(i);
                if (g.storage.Deposit(box, i) != StorageSystem.Result.Ok) return false;
            }
            return g.progress.IsComplete(defId);
        }

        /// <summary>Game Design v1.0 §15: the acceptance tests that can be proven without a long robot run.</summary>
        /// <summary>Playtest 2026-10-26: "robots are still struggling to pick up things in the sorting tray".
        /// Retrobot and Unibot are built, the tray is filled with parts they may carry (its back row against the
        /// wall too), and they must take every one out, however far over the rim it lies.</summary>
        /// <summary>A new game: every robot unit (Hoverbot #2/#3 and Sorting Arm #2/#3 too) has its whole kit of
        /// unique parts lying in the world, each one with the same part keys as the first unit of its type.</summary>
        void KitChecks()
        {
            var wb = g.workbench;
            var rows = new List<string>();
            bool ok = true;
            foreach (var p in wb.projects)
            {
                var ids = wb.PartIds(p);
                var first = wb.P(p.type);
                var keys = ids.Select(i => Robot.PartKey(g.world.DefOf(i))).ToHashSet();
                bool inWorld = ids.All(i => g.world.instances[i].state == ItemState.InWorld);
                bool same = keys.SetEquals(wb.PartIds(first).Select(i => Robot.PartKey(g.world.DefOf(i))));
                bool good = !p.crafted && ids.Count > 0 && ids.Count == keys.Count && inWorld && same;
                ok &= good;
                rows.Add($"{p.id} {ids.Count}{(good ? "" : $" (crafted {p.crafted}, in world {inWorld}, keys {same})")}");
            }
            Check("every robot unit's kit lies in the world from the start (Hoverbot #2/#3, Sorting Arm #2/#3 too)", ok && wb.projects.Count == 10, string.Join(", ", rows));
            Check("robot parts are unique, each in one place", PartsIntact(out var detail), detail);
        }

        /// <summary>Radar parts collect in the robot parts pool; the radar is built only once its plans
        /// (2 skill points) are bought, at once if all eight parts are already there.</summary>
        /// <summary>Without Arm Reach, Unibot and Hoverbot put parts into the low shelf boxes and the bottom
        /// drawers (a walker tosses them in under a deck); higher ones still go to staging for Unibot.</summary>
        IEnumerator LowShelfChecks()
        {
            var wb = g.workbench;
            var uni = g.robots.First(r => r.id == "unibot");
            var hov = g.robots.First(r => r.id == "hoverbot");
            var lows = g.db.boxes.Where(kv => Json.Str(kv.Value, "kind") != "floor_pallet" && Json.Num(kv.Value, "elevation_m") <= 0.2).Select(kv => kv.Key).ToList();
            string mid = g.db.boxes.Keys.First(b => Json.Str(g.db.boxes[b], "kind") != "drawer" && Json.Str(g.db.boxes[b], "kind") != "floor_pallet" && Mathf.Abs((float)Json.Num(g.db.boxes[b], "elevation_m") - 0.62f) < 0.01f);
            int uniLow = lows.Count(uni.ReachableDest), hovLow = lows.Count(hov.ReachableDest), retLow = lows.Count(g.retrobot.ReachableDest);
            Check("without Arm Reach Unibot, Hoverbot and Retrobot can store into every low shelf box and bottom drawer (Unibot not the second level)",
                  lows.Count > 0 && uniLow == lows.Count && hovLow == lows.Count && retLow == lows.Count && !uni.ReachableDest(mid) && uni.ReachHeight <= uni.BaseStat("reach_height") + 1e-3f,
                  $"{lows.Count} low destinations: Unibot {uniLow}, Hoverbot {hovLow}, Retrobot {retLow}; Unibot at {mid} {uni.ReachableDest(mid)}, reach {uni.ReachHeight:0.00} m");

            // a working run: both built and activated, every part sent to a low destination
            LevelTo(g.progression.maxLevel);
            foreach (var r in new[] { uni, hov }) { wb.ForceInstall(wb.P(r.id)); wb.Activate(wb.P(r.id)); }
            float t0 = Time.realtimeSinceStartup;
            while (!(uni.Active && hov.Active) && Time.realtimeSinceStartup - t0 < 15) yield return null;
            int u0 = uni.delivered, h0 = hov.delivered, tz0 = uni.tosses;
            var lowSet = lows.ToHashSet();
            var stored0 = lows.Sum(b => g.storage.Stored(b).Count);
            Time.timeScale = 4;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 75 && (uni.delivered - u0 < 2 || hov.delivered - h0 < 2)) yield return null;
            Time.timeScale = 1;
            int lowStored = lows.Sum(b => g.storage.Stored(b).Count) - stored0;
            Check("Unibot (no Arm Reach) and Hoverbot really put parts away into low destinations",
                  uni.delivered > u0 && hov.delivered > h0 && lowStored > 0,
                  $"Unibot stored {uni.delivered - u0} ({uni.tosses - tz0} tossed in), Hoverbot stored {hov.delivered - h0}; {lowStored} new parts in low destinations; {uni.Describe()} / {hov.Describe()}");
        }

        /// <summary>The tool pegboard (docs/PEGBOARD.md): the tools are unique player-only items in the world, each
        /// hangs only in its own slot (aimed with the crosshair), counts toward restored % with no XP or level, and
        /// the board survives a save and load.</summary>
        IEnumerator PegboardChecks()
        {
            var pb = g.builder.pegboard;
            var it = g.interaction;
            g.player.enabled = false;
            var tools = g.world.instances.Where(i => g.world.DefOf(i.id).IsPegboardTool).ToList();
            var defs = tools.Select(i => g.world.DefOf(i.id)).ToList();
            Check("every board tool is one unique, player-only item lying in the world with its own slot destination",
                  pb != null && tools.Count == pb.Total && pb.Total >= 60 && defs.Select(d => d.id).Distinct().Count() == tools.Count && defs.All(d => d.spawnCount == 1 && !d.robotCarry && !g.db.CanRobotCarry(d.id))
                  && tools.All(i => i.state == ItemState.InWorld && i.zone == -1) && defs.All(d => g.db.stations.ContainsKey(d.storageBox) && d.storageBox == "PB-" + d.PegSlot) && pb.Hung == 0,
                  $"{tools.Count} tools, {pb?.Total} slots, {pb?.Hung} hung at the start");

            RobotWorkbench.QaIgnoreToolRequirements = false;
            var cfg = pb.cfg;
            var layout = pb.LayoutProblems();
            Check($"all {cfg.tools.Count} catalogue tools in 6 sections: every slot in one, laid out with no overlaps, inside its section and under the signs",
                  pb.Total == cfg.tools.Count && cfg.sections.Count == 6 && cfg.sections.Sum(s => s.slots.Count) == pb.Total && cfg.errors.Count == 0 && layout.Count == 0,
                  $"{pb.Total} tools; {string.Join(", ", cfg.sections.Select(s => $"{s.id} {s.slots.Count}"))}; {cfg.errors.Count} config errors; layout: {string.Join("; ", layout.Take(4))}");
            Check("no twins: every board tool has a name of its own", defs.Select(d => d.displayName).Distinct().Count() == defs.Count,
                  string.Join(", ", defs.GroupBy(d => d.displayName).Where(gr => gr.Count() > 1).Select(gr => gr.Key)));
            Check("the safety net finds nothing wrong in a new world", g.PegboardGuard(false).Count == 0, string.Join("; ", g.PegboardGuard(false).Take(4)));
            var newFloat = tools.Where(i => i.state == ItemState.InWorld && !g.piles.Contact(i.id).supported).ToList();
            Check("a new world: every board tool lying about rests on something (none floats)", newFloat.Count == 0,
                  string.Join(", ", newFloat.Take(5).Select(i => $"{g.world.DefOf(i.id).displayName} at {i.Position.y:0.000} m")));
            yield return Frames(2);
            Check("each section's sign shows its count: \"0 / 20 TOOLS RESTORED\" over Spanners & Wrenches",
                  pb.SignText("spanners") == "0 / 20 TOOLS RESTORED" && cfg.sections.All(s => pb.SignText(s.id) == $"0 / {s.slots.Count} TOOLS RESTORED"), pb.SignText("spanners") ?? "(none)");
            int Tool(string slot) => g.world.instances.First(i => g.world.DefOf(i.id).IsPegboardTool && g.world.DefOf(i.id).PegSlot == slot).id;

            ItemInstance a = tools[0], b = tools[1];
            var bAt = pb.SlotCentre(g.world.DefOf(b.id).PegSlot);
            var c = tools.Skip(2).First(i => Vector3.Distance(pb.SlotCentre(g.world.DefOf(i.id).PegSlot), bAt) > 0.6f);   // far from b's own slot (its aim assist)
            IEnumerator AimAtSlot(string slot)
            {
                var at = pb.SlotCentre(slot);
                LookFrom(at - pb.transform.forward * 1.1f + Vector3.down * 0.15f, at);
                yield return Frames(2);
                it.UpdateTarget();
            }
            long xp0 = g.progression.xp; int lv0 = g.progression.level, pts0 = g.progression.skillPoints;
            float pct0 = g.progress.OverallPercent;
            Check("a new game: no pegboard reward yet (Part Radar blueprint locked, no Area Sweep)",
                  g.pegRewards.Granted.Count == 0 && !g.radar.HasPlans && g.tools.Level("area_sweep") == 0, string.Join(",", g.pegRewards.Granted));
            it.Pickup(c.id, false);
            yield return AimAtSlot(g.world.DefOf(b.id).PegSlot);
            it.Primary();
            yield return Frames(2);
            Check("a tool offered at another tool's place is refused and unlocks nothing",
                  pb.Hung == 0 && g.inventory.Has(c.id) && g.pegRewards.Granted.Count == 0 && !g.radar.HasPlans, $"hung {pb.Hung}, granted {string.Join(",", g.pegRewards.Granted)}");
            g.inventory.Remove(c.id); g.world.SetState(c.id, ItemState.InWorld, "world");   // out of the hands again: the checks below aim at its slot
            it.Pickup(a.id, false);
            yield return AimAtSlot(g.world.DefOf(a.id).PegSlot);
            var info = it.Describe();
            Check("aiming at the held tool's slot targets it and prompts to hang it, with the slot highlighted",
                  it.targetType == "peg" && it.targetPeg == g.world.DefOf(a.id).PegSlot && info.tone == 1 && info.hint.Contains("Hang"),
                  $"target {it.targetType} {it.targetPeg}, hint '{info.hint}'");
            yield return g.Capture(Path.Combine(outDir, "peg_highlight.png"), 1);
            int firstT0 = g.hud.toastLog.Count;
            it.Primary();
            yield return Frames(2);
            var firstSaid = g.hud.toastLog.Skip(firstT0).ToList();
            Check("the first tool in its place unlocks the Part Radar blueprint at once, with the one-time notice",
                  pb.Hung == 1 && g.pegRewards.Has("part_radar") && g.radar.HasPlans && !g.progression.Has("part_radar_plans")
                  && firstSaid.Count(t => t.Contains("Blueprint unlocked: Part Radar") && t.Contains("Complete tool sections to unlock more blueprints.")) == 1,
                  string.Join(" | ", firstSaid));
            yield return g.Capture(Path.Combine(outDir, "peg_first_tool_notice.png"), 1);
            Check("[LMB] hangs it: stored in its slot, drawn on the board, restored % up, no XP, level or skill point",
                  pb.IsOccupied(g.world.DefOf(a.id).PegSlot) && pb.Hung == 1 && a.state == ItemState.Stored && a.owner == "box:" + g.world.DefOf(a.id).storageBox && !g.inventory.Has(a.id)
                  && g.progress.OverallPercent > pct0 && g.progression.xp == xp0 && g.progression.level == lv0 && g.progression.skillPoints == pts0,
                  $"hung {pb.Hung}, state {a.state} {a.owner}, {pct0:0.000}% -> {g.progress.OverallPercent:0.000}%, xp {xp0} -> {g.progression.xp}");

            // the wrong tool: refused at another tool's empty slot, and not taken by the tray
            it.Pickup(b.id, false);
            yield return AimAtSlot(g.world.DefOf(c.id).PegSlot);
            info = it.Describe();
            it.Primary();
            Check("a wrong tool is never accepted: refused at another tool's slot, still in hand",
                  it.targetType == "peg" && it.targetPeg == g.world.DefOf(c.id).PegSlot && info.tone == -1 && !pb.IsOccupied(it.targetPeg) && g.inventory.Has(b.id) && b.state == ItemState.Carried && pb.Hung == 1,
                  $"target {it.targetPeg}, hint '{info.hint}', still carried {g.inventory.Has(b.id)}");
            Check("tools do not go in the sorting tray (only the player hangs them)", !it.DropInTray(b.id, false) && g.inventory.Has(b.id), "");
            Check("a robot never picks a board tool", g.robots.All(r => tools.All(i => !g.db.CanRobotCarry(i.defId))), "");

            // save and load: the hung tool is back on the board, the carried one in hand
            g.piles.Finish();
            g.SaveGame("autotest_peg");
            g.StartWorld(g.world.seed, null);
            Check("a new world starts with an empty board and no pegboard rewards", g.builder.pegboard.Hung == 0 && g.pegRewards.Granted.Count == 0 && !g.radar.HasPlans,
                  $"{g.builder.pegboard.Hung} hung, granted {string.Join(",", g.pegRewards.Granted)}");
            g.LoadGame("autotest_peg");
            g.player.enabled = false;
            yield return Frames(3);
            pb = g.builder.pegboard;
            a = g.world.instances[a.id]; b = g.world.instances[b.id];
            Check("save and load restore the board: the hung tool in its slot, the other still carried, the Part Radar blueprint kept",
                  pb.Hung == 1 && pb.IsOccupied(g.world.DefOf(a.id).PegSlot) && a.state == ItemState.Stored && g.inventory.Has(b.id) && g.progression.xp == xp0 && g.pegRewards.Has("part_radar"),
                  $"hung {pb.Hung}, a {a.state}, b carried {g.inventory.Has(b.id)}, xp {g.progression.xp}");

            // a set: its pieces can be found and carried first, but go up only once their holder is mounted
            if (g.inventory.Has(b.id)) it.HangTool(b.id);
            int so1 = Tool("SO01"), sk1 = Tool("SK01"), hk8 = Tool("HK08");
            Take(so1);
            yield return AimAtSlot("SO01");
            info = it.Describe();
            bool socketRefused = !it.HangTool(so1);
            Check("a socket is refused until its holder is up: \"Mount the metric socket holder first.\"",
                  socketRefused && info.tone == -1 && info.hint.Contains("Mount the metric socket holder first.") && g.inventory.Has(so1) && !pb.IsOccupied("SO01"),
                  $"hint '{info.hint}', refused {socketRefused}, still carried {g.inventory.Has(so1)}");
            Take(sk1);
            bool holderUp = it.HangTool(sk1), socketUp = it.HangTool(so1);
            Take(hk8);
            bool keyRefused = !it.HangTool(hk8);
            Check("with the holder mounted the socket goes on its own place on it; a hex key still waits for its own holder",
                  holderUp && socketUp && pb.IsOccupied("SK01") && pb.IsOccupied("SO01") && keyRefused && g.inventory.Has(hk8), $"holder {holderUp}, socket {socketUp}, key refused {keyRefused}");
            if (g.inventory.Has(hk8)) { g.inventory.Remove(hk8); g.world.SetState(hk8, ItemState.InWorld, "world"); }
            var near = pb.SlotCentre("SK01");
            LookFrom(near - pb.transform.forward * 0.55f + Vector3.down * 0.05f, near);
            yield return Frames(3);
            yield return g.Capture(Path.Combine(outDir, "peg_socket_holder.png"), 1);

            // the split deep socket rail and bit set (2026-10-28): a holder each, every piece its own tool and place
            var deepSet = cfg.slotById["SK03"].children; var bitSet = cfg.slotById["SK08"].children;
            string Key(string slot) => cfg.toolById[cfg.slotById[slot].tool].GeometryKey;
            int Verts(string slot) => WorkshopWreckage.Pegboard.ToolFactory.Build(Key(slot), MeshLibrary.LodWorld).mesh.vertexCount;
            float Tall(string slot) { var ub = MeshLibrary.UnitBounds(Key(slot)).size; return ub.y / Mathf.Max(ub.x, ub.z); }
            var tipVerts = new[] { "BT02", "BT05", "BT08", "BT11", "BT15" }.Select(Verts).ToList();   // PH2, PZ2, SL5.5, T15, H4
            Check("the deep socket rail is a holder + 10 deep sockets (10-19 mm) and the bit set a holder + 16 screwdriver bits (PH, PZ, slotted, Torx, hex), each with its own model; deep sockets keep their long shape",
                  deepSet.Count == 10 && bitSet.Count == 16 && deepSet.Concat(bitSet).Select(Key).Distinct().Count() == 26
                  && tipVerts.Distinct().Count() == tipVerts.Count && deepSet.All(sl => Tall(sl) > 2.2f) && Tall("SO01") < 1.9f,
                  $"tip vertices {string.Join("/", tipVerts)}; deep height/width {Tall("DS01"):0.00}..{Tall("DS10"):0.00}, standard {Tall("SO01"):0.00}");
            int ds3 = Tool("DS03"), ds7 = Tool("DS07"), sk3 = Tool("SK03"), bt5 = Tool("BT05"), sk8 = Tool("SK08");
            Take(ds3); g.inventory.Select(g.world.instances[ds3].defId);   // in hand: only the held tool counts (placement rules 2026-10-30)
            yield return AimAtSlot("DS03");
            var deepHint = it.Describe().hint;
            bool deepRefused = !it.HangTool(ds3);
            Take(bt5); g.inventory.Select(g.world.instances[bt5].defId);
            yield return AimAtSlot("BT05");
            var bitHint = it.Describe().hint;
            bool bitRefused = !it.HangTool(bt5);
            Take(sk3); Take(sk8);
            bool holdersUp = it.HangTool(sk3) && it.HangTool(sk8);
            bool piecesUp = it.HangTool(bt5) && it.HangTool(ds7) && it.HangTool(ds3);   // in any order
            Check("deep sockets and bits wait for their own holder (\"Mount the deep socket holder first.\" / \"... screwdriver bit holder first.\"), then go on their places one at a time",
                  deepRefused && bitRefused && deepHint.Contains("Mount the deep socket holder first.") && bitHint.Contains("Mount the screwdriver bit holder first.") && holdersUp && piecesUp
                  && pb.IsOccupied("DS03") && pb.IsOccupied("DS07") && pb.IsOccupied("BT05") && !pb.IsOccupied("DS01") && !pb.IsOccupied("BT01"),
                  $"hints '{deepHint}' / '{bitHint}'; holders {holdersUp}, pieces {piecesUp}");
            int world0 = g.world.Count, hung0 = pb.Hung;
            g.piles.Finish();
            g.SaveGame("autotest_peg_sets");
            g.LoadGame("autotest_peg_sets");
            g.player.enabled = false;
            yield return Frames(3);
            pb = g.builder.pegboard;
            Check("a partly filled deep socket holder and bit holder survive save and load: the same pieces on the same places, nothing duplicated",
                  pb.Hung == hung0 && g.world.Count == world0 && new[] { "SK03", "DS03", "DS07", "SK08", "BT05" }.All(pb.IsOccupied)
                  && deepSet.Concat(bitSet).Count(pb.IsOccupied) == 3 && g.PegboardGuard(false).Count == 0,
                  $"hung {hung0} -> {pb.Hung}, world {world0} -> {g.world.Count}, pieces up {deepSet.Concat(bitSet).Count(pb.IsOccupied)}");
            // the small pieces lie on open floor and can be aimed at and picked up like any part
            var feet = g.player.transform.position;
            // an open floor spot near the player: nothing overhead (no bench or shelf over it) and no other loose
            // item within 35 cm (a part lying there took the aim: "aimed 0/6", 2026-10-30)
            var small = new[] { "DS01", "BT01", "BT07", "BT10", "BT15", "HK01" };
            var smallIds = small.Select(Tool).ToHashSet();
            int Blocker(Vector3 c) => g.world.instances.Where(i => i.state == ItemState.InWorld && !smallIds.Contains(i.id))
                .Where(i => Vector2.Distance(new Vector2(i.Position.x, i.Position.z), new Vector2(c.x, c.z)) < 0.35f && Mathf.Abs(i.Position.y - c.y) < 0.6f)
                .Select(i => i.id).DefaultIfEmpty(-1).First();
            var spot = feet;
            bool clear = false;
            for (int k = 0; k < 160 && !clear; k++)
            {
                float ang = k * 47f * Mathf.Deg2Rad, rad = 0.8f + (k % 8) * 0.3f;
                var cand = g.SafeDropPoint(feet + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * rad, feet);
                if (cand.y < 0.05f && !Physics.Raycast(cand + Vector3.up * 0.03f, Vector3.up, 2.5f) && Blocker(cand) < 0) { spot = cand; clear = true; }
            }
            int aimed = 0, picked = 0;
            bool input0 = it.enabledInput;
            it.enabledInput = true;   // automated runs hide the hover each frame; aiming needs it
            var sizes = new List<string>();
            foreach (var slot in small)
            {
                int id = Tool(slot);
                var def = g.world.DefOf(id);
                if (g.inventory.Has(id)) g.inventory.Remove(id);
                var ub = MeshLibrary.UnitBounds(def.geometryKey).size * def.worldScale;
                g.index.Remove(id);
                g.world.SetTransform(id, Matrix4x4.TRS(spot + Vector3.up * (ub.z / 2 + 0.002f), Quaternion.Euler(90, 0, 0), Vector3.one * def.worldScale));
                g.world.SetState(id, ItemState.InWorld, "world");
                g.index.Insert(id);
                LookFrom(spot + Vector3.up * 0.65f + (feet - spot).normalized * 0.3f, spot);
                yield return Frames(2);
                it.UpdateTarget();
                if (it.targetType == "item" && it.targetItem == id) aimed++;
                string targetWas = $"{it.targetType} {(it.targetItem >= 0 && it.targetItem != id ? g.world.DefOf(it.targetItem).displayName + " " : "")}{it.targetItem}/{id} at {spot.x:0.00},{spot.y:0.00},{spot.z:0.00}";
                if (slot == "DS01") yield return g.Capture(Path.Combine(outDir, "peg_small_aim.png"), 1);
                if (it.Pickup(id, false) && g.inventory.Has(id)) picked++;
                sizes.Add($"{def.displayName} {Mathf.Max(ub.x, ub.y, ub.z) * 100:0.0} cm (aim {targetWas})");
            }
            it.enabledInput = input0;
            Check("the smallest set pieces (deep socket, PH, slotted, Torx and hex bits, the 1.5 mm key) lying on the floor are targeted and picked up",
                  clear && aimed == 6 && picked == 6, $"clear spot {clear}; aimed {aimed}/6, picked {picked}/6; {string.Join(", ", sizes)}");

            // pegboard rewards (unlocks.json pegboard_rewards, docs/PEGBOARD.md "Rewards"): blueprints and Area Sweep
            var wb = g.workbench;
            var rw = g.pegRewards;
            var spider = wb.P("spiderbot"); var hb1 = wb.P("hoverbot"); var hb2 = wb.P("hoverbot_2");
            string MsOf(string r) { var m = rw.For(r); return m == null ? "start" : m.tools > 0 ? $"tools {m.tools}" : m.section ?? (m.all ? "all" : $"any {m.sections}"); }
            var table = new Dictionary<string, string>
            {
                ["retrobot"] = "start", ["part_radar"] = "tools 1", ["unibot"] = "pliers", ["caterpillarbot"] = "precision", ["hoverbot"] = "hammers",
                ["sorting_arm"] = "screwdrivers", ["spiderbot"] = "spanners", ["hoverbot_2"] = "sockets", ["hoverbot_3"] = "sockets",
                ["sorting_arm_2"] = "any 3", ["sorting_arm_3"] = "any 5", ["area_sweep"] = "all",
            };
            Check("reward table: Retrobot from the start, first tool → Part Radar, Pliers → Unibot, Precision → Caterpillarbot, Hammers → Hoverbot #1, Screwdrivers → Sorting Arm #1, Spanners → Spiderbot, Sockets → Hoverbot #2 and #3, any 3 → Arm #2, any 5 → Arm #3, all → Area Sweep",
                  table.All(kv => MsOf(kv.Key) == kv.Value) && rw.Milestones.SelectMany(m => m.unlocks).Count() == table.Count - 1,
                  string.Join(", ", table.Keys.Select(k => $"{k} {MsOf(k)}")) + $"; section sizes from the slots: {string.Join(", ", cfg.sections.Select(s => $"{s.id} {pb.SectionTotal(s.id)}"))}, {pb.Total} in all");
            var robotsOpen = wb.projects.Where(p => !wb.Operational(p) && wb.Unlocked(p)).Select(p => p.id).ToList();
            Check("after the first tool only Retrobot (from the start) and the Part Radar are open; the bench says what Spiderbot needs and how far",
                  robotsOpen.SequenceEqual(new[] { "retrobot" }) && rw.Granted.SequenceEqual(new[] { "part_radar" }) && g.tools.Level("area_sweep") == 0
                  && wb.LockReason(spider).Contains("Requires: Spanners & Wrenches") && wb.LockReason(spider).Contains($"{pb.SectionHung("spanners")} / {pb.SectionTotal("spanners")} tools restored")
                  && wb.LockReason(hb1).Contains("Hammers, Chisels & Saws") && wb.OtherUnitsText(hb1).Contains("Hoverbot #2, Hoverbot #3: Sockets & Keys"),
                  $"open: {string.Join(",", robotsOpen)}; granted: {string.Join(",", rw.Granted)}; spider: '{wb.LockReason(spider)}'");
            g.OpenWorkbench("spiderbot");
            yield return Frames(4);
            yield return g.Capture(Path.Combine(outDir, "peg_bench_spider_locked.png"), 2);
            g.SetState("play");
            g.player.enabled = false;

            // sections in an order unlike the table: every one gives its own reward, the counts their arms, each once
            List<string> HangSection(string sec)
            {
                int t0 = g.hud.toastLog.Count;
                foreach (var slot in cfg.sectionById[sec].slots.Where(s => !pb.IsOccupied(s)).OrderBy(s => cfg.slotById[s].IsChild ? 1 : 0).ToList())
                {
                    int id = Tool(slot);
                    if (!g.inventory.Has(id)) Take(id);
                    it.HangTool(id);
                }
                return g.hud.toastLog.Skip(t0).ToList();
            }
            var expected = new HashSet<string> { "part_radar" };
            var steps = new (string sec, string[] gets)[]
            {
                ("precision", new[] { "caterpillarbot" }),
                ("sockets", new[] { "hoverbot_2", "hoverbot_3" }),
                ("pliers", new[] { "unibot", "sorting_arm_2" }),   // the third section: two milestones at once
                ("hammers", new[] { "hoverbot" }),
                ("spanners", new[] { "spiderbot", "sorting_arm_3" }),   // the fifth
                ("screwdrivers", new[] { "sorting_arm", "area_sweep" }),   // the whole board
            };
            var allSaid = new List<string>();
            foreach (var (sec, gets) in steps)
            {
                var said = HangSection(sec);
                yield return Frames(2);
                allSaid.AddRange(said);
                expected.UnionWith(gets);
                var granted = rw.Granted.ToHashSet();
                var unlockToasts = said.Where(t => t.Contains("unlocked:")).ToList();
                bool robotsOk = gets.Where(r => wb.P(r) != null).All(r => wb.Unlocked(wb.P(r)) && unlockToasts.Count(t => t.Contains(wb.P(r).name)) == 1);
                bool othersLocked = wb.projects.Where(p => !expected.Contains(p.id) && p.id != "retrobot").All(p => !wb.Unlocked(p));
                Check($"{cfg.sectionById[sec].name} complete ({pb.SectionsComplete} of {cfg.sections.Count}): unlocks {string.Join(" + ", gets)}, announced once; nothing else",
                      pb.SectionComplete(sec) && granted.SetEquals(expected) && robotsOk && othersLocked && Rewards(rw.Evaluate(pb)) == 0,
                      $"granted {string.Join(",", granted.OrderBy(x => x))}; said: {string.Join(" | ", said)}");
                if (sec == "sockets")
                    Check("Hoverbot #2 and #3 open while Hoverbot #1 is still locked: #2 is the unit on the Hoverbot mat",
                          wb.Unlocked(hb2) && wb.Unlocked(wb.P("hoverbot_3")) && !wb.Unlocked(hb1) && wb.ActiveOnMat(hb1.mat) == hb2 && wb.OnBench(hb2)
                          && !wb.CanInstall(hb2, wb.PoolIds(hb2).First()).Contains("Requires"),
                          $"active on the mat: {wb.ActiveOnMat(hb1.mat).id}; hb1 '{wb.LockReason(hb1)}'");
                if (sec == "pliers")
                {
                    var signAt = pb.transform.TransformPoint(new Vector3((cfg.sectionById["pliers"].x0 + cfg.sectionById["pliers"].x1) / 2, pb.cfg.PanelCentre.y + 0.3f, pb.cfg.PanelFront));
                    LookFrom(signAt - pb.transform.forward * 1.3f, signAt);
                    yield return Frames(3);
                    yield return g.Capture(Path.Combine(outDir, "peg_pliers_complete.png"), 1);
                }
            }
            int Rewards(List<(Pegboard.PegboardRewards.Milestone m, List<string> rewards)> got) => got.Sum(x => x.rewards.Count);
            Check("the first tool's notice and every reward toast appear exactly once in the whole run",
                  allSaid.Count(t => t.Contains("Blueprint unlocked: Part Radar")) == 0 && firstSaid.Count(t => t.Contains("Blueprint unlocked: Part Radar") && t.Contains("Complete tool sections to unlock more blueprints.")) == 1
                  && allSaid.Count(t => t.Contains("Area Sweep")) == 1, $"first tool: {string.Join(" | ", firstSaid)}");

            // every tool hung: the board is complete (100 %), still no XP; holders go up before their pieces
            int refused = 0;
            int toasts0 = g.hud.toastLog.Count;
            foreach (var i in g.world.instances.Where(i => g.world.DefOf(i.id).IsPegboardTool && i.state != ItemState.Stored)
                                               .OrderBy(i => cfg.slotById[g.world.DefOf(i.id).PegSlot].IsChild ? 1 : 0).ToList())
            {
                if (!g.inventory.Has(i.id)) Take(i.id);
                if (!it.HangTool(i.id)) refused++;
            }
            yield return Frames(3);
            Check("every tool hangs in its own slot: the board shows all of them, none missing, 100 %, no XP",
                  refused == 0 && pb.Missing == 0 && pb.Percent >= 99.99f && g.progression.xp == xp0,
                  $"{pb.Hung}/{pb.Total} hung, {refused} refused, xp {g.progression.xp}");
            Check("the full board: TOOL WALL COMPLETE! with its line, every section complete, every blueprint open, Area Sweep a free bonus (0.5 m, 300 s), still no XP",
                  allSaid.Any(t => t.Contains("TOOL WALL COMPLETE!") && t.Contains("The inventor would almost be impressed.")) && pb.SectionsComplete == 6 && wb.projects.All(wb.Unlocked)
                  && g.tools.Level("area_sweep") == 1 && Mathf.Approximately(g.tools.CooldownOf("area_sweep"), rw.SweepCooldown) && rw.SweepCooldown == 300 && Mathf.Approximately(rw.SweepRadius, 0.5f)
                  && !g.progression.Has("area_collect_radius_m") && g.progression.xp == xp0, $"sweep level {g.tools.Level("area_sweep")}, cooldown {g.tools.CooldownOf("area_sweep")} s, radius {rw.SweepRadius} m");
            foreach (var (holder, shot) in new[] { ("SK01", "peg_socket_holder_full.png"), ("HX01", "peg_hex_holder_full.png"), ("SK03", "peg_deep_holder_full.png"), ("SK08", "peg_bit_holder_full.png") })
            {
                var at = pb.SlotCentre(holder);
                LookFrom(at - pb.transform.forward * 0.42f + Vector3.down * 0.03f, at);
                yield return Frames(3);
                yield return g.Capture(Path.Combine(outDir, shot), 1);
            }
            var grantedBefore = rw.Granted.OrderBy(x => x).ToList();
            g.piles.Finish();
            g.SaveGame("autotest_peg");
            int toasts1 = g.hud.toastLog.Count;
            g.LoadGame("autotest_peg");
            g.player.enabled = false;
            yield return Frames(3);
            pb = g.builder.pegboard;
            Check("a full board with its holders survives save and load, with no celebration on loading",
                  pb.Missing == 0 && pb.SectionsComplete == 6 && !g.hud.toastLog.Skip(toasts1).Any(t => t.Contains("COMPLETE")) && g.PegboardGuard(false).Count == 0,
                  $"{pb.Hung}/{pb.Total}, {pb.SectionsComplete} sections");
            Check("save and load keep every reward, with no notice again: all blueprints open, Area Sweep on its 300 s cooldown",
                  g.pegRewards.Granted.OrderBy(x => x).SequenceEqual(grantedBefore) && !g.hud.toastLog.Skip(toasts1).Any(t => t.Contains("unlocked")) && g.workbench.projects.All(g.workbench.Unlocked)
                  && g.tools.Level("area_sweep") == 1 && Mathf.Approximately(g.tools.CooldownOf("area_sweep"), 300) && g.pegRewards.Evaluate(pb).Count == 0,
                  $"granted {string.Join(",", g.pegRewards.Granted)}; toasts on load: {string.Join(" | ", g.hud.toastLog.Skip(toasts1))}");
            var mid = pb.transform.TransformPoint(new Vector3(0, pb.cfg.PanelCentre.y, pb.cfg.PanelFront));
            LookFrom(mid - pb.transform.forward * 3.2f, mid);
            yield return Frames(3);
            yield return g.Capture(Path.Combine(outDir, "peg_full.png"), 1);
        }

        /// <summary>The pegboard safety net end to end: a save in which Retrobot holds a board tool, another tool lies
        /// in an ordinary box and a socket hangs without its holder. After loading each lies loose in the sorting tray
        /// (not deposited into it: the tray's store refuses tools), exactly once, no robot holds or takes it, and the
        /// player picks it up and hangs it (the socket once its holder is up).</summary>
        IEnumerator PegGuardChecks()
        {
            RobotWorkbench.QaIgnoreToolRequirements = false;
            g.player.enabled = false;
            var wb = g.workbench;
            var rp = wb.P("retrobot");
            wb.ForceInstall(rp);
            if (!wb.Activate(rp)) wb.DebugUnlock(rp);
            float t0 = Time.realtimeSinceStartup;
            while (!g.retrobot.Operational && Time.realtimeSinceStartup - t0 < 15) yield return null;
            var pb = g.builder.pegboard;
            ItemInstance Loose(System.Func<ItemDefinition, bool> f) => g.world.instances.First(i => i.state == ItemState.InWorld && g.world.DefOf(i.id).IsPegboardTool && f(g.world.DefOf(i.id)));
            bool Plain(ItemDefinition d) => !pb.cfg.slotById[d.PegSlot].IsChild && pb.cfg.slotById[d.PegSlot].children.Count == 0;   // not a set's holder or piece
            var held = Loose(Plain);
            var boxed = Loose(d => Plain(d) && d.id != held.defId);
            var piece = Loose(d => pb.cfg.slotById[d.PegSlot].IsChild && pb.cfg.slotById[d.PegSlot].parent == "SK01");
            string box = g.db.boxes.Keys.First(k => !k.StartsWith("PB-"));
            foreach (var x in new[] { held, boxed, piece }) g.index.Remove(x.id);
            g.world.SetState(held.id, ItemState.Carried, g.retrobot.Owner);
            g.storage.contents.TryAdd(box, new List<int>());
            g.storage.contents[box].Add(boxed.id);
            g.world.SetState(boxed.id, ItemState.Stored, "box:" + box);
            string pdest = "PB-" + g.world.DefOf(piece.id).PegSlot;
            g.storage.contents.TryAdd(pdest, new List<int>());
            g.storage.contents[pdest].Add(piece.id);
            g.world.SetState(piece.id, ItemState.Stored, "box:" + pdest);
            int count0 = g.world.Count;
            foreach (var r in g.robots) r.enabled = false;
            g.SaveGame("autotest_guard");
            g.StartWorld(g.world.seed, null);
            g.LoadGame("autotest_guard");
            foreach (var r in g.robots) r.enabled = false;
            yield return Frames(5);
            pb = g.builder.pegboard;
            var ids = new[] { held.id, boxed.id, piece.id };
            var inst = ids.Select(id => g.world.instances[id]).ToList();
            var retro = g.retrobot;
            Check("a tool a robot held, one in an ordinary box and a socket on the board without its holder all lie loose in the sorting tray after loading",
                  inst.All(i => i.state == ItemState.InWorld && i.owner == "tray" && g.InTray(i.Position)) && !retro.carried.Any(ids.Contains)
                  && !g.storage.Stored(box).Contains(boxed.id) && !g.storage.Stored(pdest).Contains(piece.id) && !pb.IsOccupied(g.world.DefOf(piece.id).PegSlot),
                  string.Join("; ", inst.Select(i => $"{g.world.DefOf(i.id).displayName}: {i.state} {i.owner} in tray {g.InTray(i.Position)}")) + $"; Retrobot holds {retro.carried.Count}, operational {retro.Operational}");
            var defs = g.db.order.Select(g.db.Get).Where(d => d.IsPegboardTool).ToList();
            Check("every board tool still exists exactly once, nothing added or lost, and the safety net is clean now",
                  defs.Count == pb.cfg.tools.Count && defs.All(d => g.world.IdsOf(d.id).Count() == 1) && g.world.Count == count0 && g.PegboardGuard(false).Count == 0,
                  $"{defs.Count(d => g.world.IdsOf(d.id).Count() == 1)}/{defs.Count} once, world {count0} -> {g.world.Count}; {string.Join("; ", g.PegboardGuard(false).Take(3))}");
            // the robots work on (sped up) with the tools in the tray: none takes one (tools are player-only)
            int ev0 = _events.Count;
            foreach (var r in g.robots) { r.enabled = true; r.SetBattery(1); }
            Time.timeScale = 4;
            float g0 = Time.time; t0 = Time.realtimeSinceStartup;
            while (Time.time - g0 < 40 && Time.realtimeSinceStartup - t0 < 40) yield return null;
            Time.timeScale = 1;
            foreach (var r in g.robots) r.enabled = false;
            var robotTouched = _events.Skip(ev0).Where(e => e.kind is GameEventKind.PartPickedUp or GameEventKind.PartSorted && e.actor.StartsWith("robot") && e.partDef >= 0 && g.db.Get(e.partDef).IsPegboardTool).ToList();
            Check("no robot picks a tool out of the sorting tray (the robots worked 40 s with them lying there)",
                  robotTouched.Count == 0 && inst.All(i => i.state == ItemState.InWorld && g.InTray(i.Position)),
                  $"{_events.Skip(ev0).Count(e => e.kind == GameEventKind.PartPickedUp && e.actor.StartsWith("robot"))} robot pick-ups meanwhile, {robotTouched.Count} of a tool; {string.Join(", ", inst.Select(i => i.state + " " + i.owner))}");
            // the player takes them out of the tray and hangs them
            EmptyHands();
            bool picked = ids.All(id => g.interaction.Pickup(id, false));
            bool hungHeld = g.interaction.HangTool(held.id), hungBoxed = g.interaction.HangTool(boxed.id);
            bool pieceRefused = !g.interaction.HangTool(piece.id);
            int holder = g.world.instances.First(i => g.world.DefOf(i.id).IsPegboardTool && g.world.DefOf(i.id).PegSlot == "SK01").id;
            Take(holder);
            bool holderUp = g.interaction.HangTool(holder);
            bool hungPiece = g.interaction.HangTool(piece.id);
            Check("the player picks each one up from the tray and hangs it (the socket once its holder is up)",
                  picked && hungHeld && hungBoxed && pieceRefused && holderUp && hungPiece && pb.Hung == 4 && g.PegboardGuard(false).Count == 0,
                  $"picked {picked}, hung {hungHeld}/{hungBoxed}, socket refused first {pieceRefused}, holder {holderUp}, socket {hungPiece}; board {pb.Hung}");
            File.Delete(SaveSystem.PathOf("autotest_guard"));
        }

        /// <summary>A save from before the sections and split sets (copied into the saves folder as premig_*): it loads
        /// under the new rules with nothing lost or doubled. An old set that was hung stays up as its holder with the
        /// pieces new and loose; one that was not is the holder, its pieces loose. Robots that ran keep running.</summary>
        /// <summary>Save and load (playtest 2026-10-28: saving over a converted save was refused as "a different
        /// workshop", and the next load lost the evening's progress). Converted save saved over and loaded; a round
        /// trip that changes nothing; the backup when a file is damaged; a failed write that is told and leaves
        /// nothing behind; named saves, overwrite and delete through the menus; the autosave before a load.</summary>
        IEnumerator SaveChecks()
        {
            void Clean()
            {
                foreach (var pat in new[] { "autotest_sv_*", "autotest_save_*", "autotest_autosave*" })
                    foreach (var f in Directory.GetFiles(SaveSystem.Dir, pat)) File.Delete(f);
                if (Directory.Exists(SaveSystem.TrashDir))
                    foreach (var f in Directory.GetFiles(SaveSystem.TrashDir, "*autotest_*")) File.Delete(f);
            }
            Clean();
            // 1. the playtest: an older catalog's save loads (converted), the player plays on and saves over it
            File.Copy(SaveSystem.PathOf("premig_slot1"), SaveSystem.PathOf("autotest_sv_conv"), true);
            g.LoadGame("autotest_sv_conv");
            g.player.enabled = false;
            yield return Frames(3);
            bool converted = g.migrationReport != "";
            int level0 = g.progression.level, sorted0 = g.progress.sortedTotal;
            var part = g.world.instances.First(i => i.state == ItemState.InWorld && !g.world.DefOf(i.id).IsPegboardTool && !g.world.DefOf(i.id).IsRobotPart).id;
            Take(part);
            g.playTime += 600;
            double play1 = g.playTime;
            var t0 = Time.realtimeSinceStartup;
            bool saved = g.SaveGame("autotest_sv_conv");
            float saveMs = (Time.realtimeSinceStartup - t0) * 1000;
            g.LoadGame("autotest_sv_conv");
            yield return Frames(3);
            Check("a save from an older parts catalog, loaded (converted), played on and saved over, loads again with the progress since (playtest 2026-10-28: that save was refused as \"a different workshop\")",
                  converted && saved && g.migrationReport == "" && g.inventory.Has(part) && g.progression.level == level0 && g.progress.sortedTotal == sorted0 && System.Math.Abs(g.playTime - play1) < 5,
                  $"converted {converted}, saved {saved} in {saveMs:0} ms, converted again {g.migrationReport != ""}, part in hand {g.inventory.Has(part)}, level {level0} -> {g.progression.level}, sorted {sorted0} -> {g.progress.sortedTotal}, play {play1 / 60:0} -> {g.playTime / 60:0} min");

            // 2. a save and its load change nothing
            g.piles.Finish();
            var before = SaveSystem.Serialize(g);
            g.SaveGame("autotest_sv_rt", "Round trip");
            g.LoadGame("autotest_sv_rt");
            yield return Frames(3);
            var after = SaveSystem.Serialize(g);
            var diffs = new List<string>();
            foreach (var k in before.Keys.Where(k => k != "saved_at" && k != "play_time_s"))
            {
                if (k == "items")
                {
                    var a = Json.Arr(before[k]); var b = Json.Arr(after[k]);
                    int bad = 0; float worst = 0;
                    for (int r = 0; r < Mathf.Min(a.Count, b.Count); r++)
                    {
                        var ea = (List<object>)a[r]; var eb = (List<object>)b[r];
                        if (ea.Count != eb.Count || !ea.Take(3).SequenceEqual(eb.Take(3))) { bad++; continue; }
                        for (int q = 3; q < ea.Count; q++) worst = Mathf.Max(worst, Mathf.Abs((float)((double)ea[q] - (double)eb[q])));
                    }
                    if (a.Count != b.Count || bad > 0 || worst > 0.001f) diffs.Add($"items ({a.Count} vs {b.Count}, {bad} state/owner, worst place {worst * 1000:0.0} mm)");
                }
                else if (!after.ContainsKey(k) || Json.Write(before[k]) != Json.Write(after[k])) diffs.Add(k);
            }
            Check("a save and its load change nothing: every part's state, owner and place, storage, inventory, robots, workbench, skills, progress",
                  diffs.Count == 0 && g.currentSave == "autotest_sv_rt" && g.currentSaveName == "Round trip", diffs.Count == 0 ? $"{before.Count} sections equal" : "differs: " + string.Join(", ", diffs));

            // 3. the old file stays as a backup, and a damaged file loads from it
            g.SaveGame("autotest_sv_rt");
            var path = SaveSystem.PathOf("autotest_sv_rt");
            bool bak = File.Exists(path + ".bak") && !File.Exists(path + ".tmp");
            var full = File.ReadAllText(path);
            File.WriteAllText(path, full.Substring(0, full.Length / 2));   // as if the game crashed halfway through writing
            var info = SaveSystem.Info("autotest_sv_rt");
            int toasts = g.hud.toastLog.Count;
            g.LoadGame("autotest_sv_rt");
            yield return Frames(3);
            bool told = g.hud.toastLog.Skip(toasts).Any(x => x.Contains("was damaged"));
            Check("each save keeps the one before as a backup; a damaged save file loads its backup and says so",
                  bak && info != null && info.fromBackup && g.inventory.Has(part) && told,
                  $"backup {bak}, listed as from backup {info?.fromBackup}, part in hand {g.inventory.Has(part)}, told {told}");

            // 4. a save that can't be written is told, and leaves nothing half-written
            Directory.CreateDirectory(SaveSystem.PathOf("autotest_sv_fail"));   // a folder where the file should go
            toasts = g.hud.toastLog.Count;
            bool failed = !g.SaveGame("autotest_sv_fail", "Fail");
            bool failTold = g.hud.toastLog.Skip(toasts).Any(x => x.Contains("NOT saved"));
            bool noTmp = !File.Exists(SaveSystem.PathOf("autotest_sv_fail") + ".tmp");
            Directory.Delete(SaveSystem.PathOf("autotest_sv_fail"));
            Check("a save that can't be written fails out loud (\"SAVE FAILED ... NOT saved\") and leaves no half-written file", failed && failTold && noTmp,
                  $"failed {failed}, told {failTold}, no .tmp {noTmp}; error '{SaveSystem.LastError}'");

            // 5. the menus: a named save, the same name again (asks, then overwrites), load and delete
            g.SetState("paused");
            g.hud.OpenSub("save");
            yield return Frames(2);
            g.hud.QaSaveName("Friday evening");
            bool clicked = g.hud.QaClick("Save as new");
            yield return Frames(2);
            var named = SaveSystem.List(true).FirstOrDefault(x => x.name == "Friday evening");
            yield return g.Capture(Path.Combine(outDir, "saves_save_screen.png"), 2);
            g.hud.QaSaveName("Friday evening");
            bool asked = g.hud.QaClick("Save as new") && SaveSystem.List(true).Count(x => x.name == "Friday evening") == 1;
            bool again = g.hud.QaClick("Overwrite 'Friday evening'");
            int copies = SaveSystem.List(true).Count(x => x.name == "Friday evening");
            Check("Save: a typed name makes a new named save, listed under its name; saving the same name again asks once, then updates that save (no second copy)",
                  clicked && named != null && named.slot.StartsWith("autotest_save_") && asked && again && copies == 1 && g.currentSave == named?.slot,
                  $"clicked {clicked}, listed {named?.slot}, asked {asked}, overwritten {again}, copies {copies}, this game's save {g.currentSave}");
            g.hud.OpenSub("load");
            yield return Frames(2);
            var row = named == null ? null : g.hud.QaRowText(named.slot);
            yield return g.Capture(Path.Combine(outDir, "saves_load_screen.png"), 2);
            bool armed = named != null && g.hud.QaClick("Delete", named.slot) && File.Exists(SaveSystem.PathOf(named.slot));
            bool deleted = named != null && g.hud.QaClick("Confirm?", named.slot);
            yield return Frames(2);
            bool gone = named != null && !File.Exists(SaveSystem.PathOf(named.slot)) && SaveSystem.List(true).All(x => x.slot != named.slot)
                        && Directory.GetFiles(SaveSystem.TrashDir, "*" + named.slot + ".json").Length == 1;
            Check("Load: the save is listed with its name and details; Delete asks once, then moves it to saves/deleted and off the list",
                  row != null && row.Contains("Friday evening") && row.Contains("level") && armed && deleted && gone && g.currentSave == null,
                  $"row '{row?.Replace("\n", " / ")}', asked {armed}, deleted {deleted}, gone {gone}");
            bool loadedByMenu = g.hud.QaClick("Load", "autotest_sv_conv");
            yield return Frames(3);
            g.player.enabled = false;
            Check("Load: a click on a save's Load button loads it", loadedByMenu && g.state == "play" && g.currentSave == "autotest_sv_conv" && g.inventory.Has(part),
                  $"clicked {loadedByMenu}, state {g.state}, save {g.currentSave}");

            // 6. the autosave: before another save loads, what is unsaved is kept
            g.autosaveInTests = true;
            g.playTime += 120;
            double play2 = g.playTime;
            g.LoadGame("autotest_sv_rt");
            yield return Frames(3);
            g.autosaveInTests = false;
            var auto = SaveSystem.Info(g.AutosaveSlot);
            Check("loading another save first keeps this game's unsaved play in the autosave", auto != null && auto.Ok && System.Math.Abs(auto.playTime - play2) < 3 && auto.name == "Autosave",
                  $"autosave {(auto == null ? "none" : $"{auto.playTime / 60:0.0} min, '{auto.name}'")}, expected {play2 / 60:0.0} min");
            Clean();
        }

        /// <summary>QA diagnostics (playtest 2026-10-29: "the robot arms stop working after a while, it seems they
        /// are only delivering the items that were present when they were mounted"): loads a save and lets it
        /// play for --apminutes (default 5); every 15 s, for each mounted arm, what it is doing and, for every
        /// part lying near its unit's staging spots, why it would or would not take it.</summary>
        IEnumerator ArmWatch(string slot)
        {
            g.LoadGame(slot);
            g.player.enabled = false;
            float minutes = float.Parse(g.args.GetValueOrDefault("apminutes", "5"), System.Globalization.CultureInfo.InvariantCulture);
            var t0 = Time.time;
            while (Time.time - t0 < minutes * 60)
            {
                foreach (var r in g.robots.Where(r => r.arm != null))
                {
                    var a = r.arm;
                    if (a.place != SortingArm.Place.Mounted) { Debug.Log($"[armwatch] {Time.time - t0:0}s {r.displayName}: {a.place}"); continue; }
                    var why = new Dictionary<string, int>();
                    foreach (var i in g.world.instances)
                    {
                        if (i.state != ItemState.InWorld) continue;
                        var box = g.world.DefOf(i.id).storageBox;
                        if (box == null || !a.Served.Contains(box)) continue;
                        var spot = r.StagingSpot(box);
                        if (!spot.HasValue || (i.owner != "stage:" + box && Vector3.ProjectOnPlane(i.Position - spot.Value, Vector3.up).magnitude > 1.2f)) continue;
                        var w = a.WhyNot(i.id);
                        if (w == "") w = "takeable";
                        else if (Time.time - t0 < 1) Debug.Log($"[armwatch]   #{i.id} {g.world.DefOf(i.id).displayName} for {box} (owner {i.owner}): {w}");
                        if (w.Contains("(")) w = w.Substring(0, w.IndexOf('(')).Trim().TrimEnd(':');
                        why[w] = why.GetValueOrDefault(w) + 1;
                    }
                    Debug.Log($"[armwatch] {Time.time - t0:0}s {r.displayName} on {a.socket.Label}: working {a.Working}, mode {r.mode}, paused {r.paused}, busy {a.BusyQA}, held {a.heldId}, delivered {r.delivered}, idle '{a.idleReason}' | "
                              + (why.Count == 0 ? "no parts near its staging spots" : string.Join(", ", why.Select(kv => $"{kv.Key}: {kv.Value}"))));
                }
                yield return new WaitForSeconds(15);
            }
        }

        IEnumerator PegMigrateChecks(string slot)
        {
            // load a copy: loading re-saves an older save (the skill migration), and the fixture must stay as it is
            File.Copy(SaveSystem.PathOf(slot), SaveSystem.PathOf("autotest_pegmig"), true);
            slot = "autotest_pegmig";
            RobotWorkbench.QaIgnoreToolRequirements = false;
            var save = SaveSystem.Read(slot);
            var hungBefore = Json.Obj(save?.GetValueOrDefault("storage"))?.Where(kv => kv.Key.StartsWith("PB-") && Json.Arr(kv.Value).Count > 0).Select(kv => kv.Key).ToList() ?? new List<string>();
            var opBefore = Json.Obj(save?.GetValueOrDefault("robots"))?.Where(kv => Json.Bool(Json.Obj(kv.Value), "operational")).Select(kv => kv.Key).ToList() ?? new List<string>();
            int partsBefore = Json.Int(save, "item_count", -1);
            // robot parts by where they are (state and owner), discoveries and announced blueprints, from the save itself
            var rows = Json.Arr(save?.GetValueOrDefault("items")).Select(r => (List<object>)r).ToList();
            var rowDefs = Json.Arr(save?.GetValueOrDefault("item_defs")).Select(x => (int)(double)x).ToList();
            string Where(int state, string owner) => (ItemState)state == ItemState.InWorld ? "loose" : $"{(ItemState)state} {owner}";
            var partsWhereBefore = rows.Where(r => g.db.Get(rowDefs[(int)(double)r[0]])?.IsRobotPart == true)
                .GroupBy(r => Where((int)(double)r[1], r[2].ToString())).ToDictionary(gr => gr.Key, gr => gr.Count());
            var discBefore = Json.Arr(Json.Obj(save?.GetValueOrDefault("progress"))?.GetValueOrDefault("discovered")).Select(x => (int)(double)x).Where(d => g.db.Get(d) != null).ToHashSet();
            var announcedBefore = Json.Arr(Json.Obj(save?.GetValueOrDefault("workbench"))?.GetValueOrDefault("announced")).Select(x => x.ToString()).ToList();
            g.LoadGame(slot);
            g.player.enabled = false;
            yield return Frames(10);
            var pb = g.builder.pegboard;
            var tools = g.world.instances.Where(i => g.world.DefOf(i.id).IsPegboardTool).ToList();
            var defs = g.db.order.Select(g.db.Get).Where(d => d.IsPegboardTool).ToList();
            Check($"'{slot}' loads: every one of the {pb.cfg.tools.Count} catalogue board tools exists exactly once",
                  save != null && defs.Count == pb.cfg.tools.Count && tools.Count == pb.cfg.tools.Count && defs.All(d => g.world.IdsOf(d.id).Count() == 1), $"{defs.Count} definitions, {tools.Count} tools");
            var stillHung = hungBefore.Where(box => g.storage.Stored(box).Count == 1).ToList();
            Check("every tool that was on the board is still on it (an old set as its holder)", stillHung.Count == hungBefore.Count && pb.Hung == hungBefore.Count,
                  $"{hungBefore.Count} hung before, {stillHung.Count} still, board {pb.Hung}; holders: SK01 {pb.IsOccupied("SK01")} HX01 {pb.IsOccupied("HX01")} SK03 {pb.IsOccupied("SK03")} SK08 {pb.IsOccupied("SK08")}");
            var pieces = tools.Where(i => pb.cfg.slotById[g.world.DefOf(i.id).PegSlot].IsChild).ToList();
            var unreachable = g.UnreachableParts().Select(u => u.id).ToHashSet();
            Check($"the new set pieces ({pb.cfg.slots.Count(s => s.IsChild)}: sockets, deep sockets, bits, hex keys) lie loose in the world, reachable, not restored",
                  pieces.Count == pb.cfg.slots.Count(s => s.IsChild) && pieces.All(i => i.state == ItemState.InWorld && !unreachable.Contains(i.id)) && !pieces.Any(i => pb.IsOccupied(g.world.DefOf(i.id).PegSlot)),
                  $"{pieces.Count} pieces: {string.Join(", ", pieces.GroupBy(i => i.state).Select(gr => $"{gr.Count()} {gr.Key}"))}, {pieces.Count(i => unreachable.Contains(i.id))} unreachable");
            // the added pieces were never settled (generated spots): they fall into place after loading (playtest 2026-10-28)
            float fallT = 0;
            for (; fallT < 15 && g.piles.Settling; fallT += Time.deltaTime) yield return null;
            var looseTools = tools.Where(i => i.state == ItemState.InWorld).ToList();
            var floating = looseTools.Where(i => !g.piles.Contact(i.id).supported).ToList();
            Check($"every loose board tool rests on the floor, a deck or other parts after loading (none floats); the {g.migrationAdded.Count} added parts fell into place",
                  g.migrationAdded.Count > 0 && floating.Count == 0 && !g.piles.Settling,
                  $"{looseTools.Count} loose tools, {floating.Count} floating ({string.Join(", ", floating.Take(5).Select(i => $"{g.world.DefOf(i.id).displayName} at {i.Position.y:0.000} m"))}); settled after {fallT:0.0} s; {g.piles.lastInfo}");
            unreachable = g.UnreachableParts().Select(u => u.id).ToHashSet();
            Check("no board tool is lost (unreachable) or in a wrong place; the safety net finds nothing", tools.All(i => !unreachable.Contains(i.id)) && g.PegboardGuard(false).Count == 0,
                  string.Join("; ", g.PegboardGuard(false).Take(4)));
            var opAfter = g.robots.Where(r => r.Operational).Select(r => r.id).ToList();
            Check("robots that were operational stay operational, whatever the new blueprint rules say", opBefore.All(opAfter.Contains),
                  $"before: {string.Join(", ", opBefore)}; after: {string.Join(", ", opAfter)}");
            var wb = g.workbench;
            Check("an older save keeps every blueprint it had (announced or built) and gets what its board has earned; the rest stays locked",
                  wb.projects.All(p => wb.Unlocked(p) == (wb.Operational(p) || announcedBefore.Contains(p.id) || p.id == "retrobot"
                                                         || g.pegRewards.Milestones.Any(m => m.unlocks.Contains(p.id) && g.pegRewards.Met(m, pb))))
                  && g.pegRewards.Has("part_radar") == (pb.Hung > 0 || g.pegRewards.Granted.Contains("part_radar")),
                  string.Join("; ", wb.projects.Select(p => $"{p.id} {(wb.Unlocked(p) ? "open" : "locked")}{(wb.Operational(p) ? " (built)" : "")}")));
            var partsWhereAfter = g.world.instances.Where(i => g.world.DefOf(i.id).IsRobotPart)
                .GroupBy(i => Where((int)i.state, i.owner)).ToDictionary(gr => gr.Key, gr => gr.Count());
            Check("robot parts are where they were: installed on the same robots, in the same pool and stations, carried by the same hands, the rest loose",
                  partsWhereBefore.Count > 0 && partsWhereBefore.Count == partsWhereAfter.Count && partsWhereBefore.All(kv => partsWhereAfter.GetValueOrDefault(kv.Key) == kv.Value),
                  string.Join("; ", partsWhereBefore.Keys.Union(partsWhereAfter.Keys).OrderBy(k => k).Select(k => $"{k}: {partsWhereBefore.GetValueOrDefault(k)} -> {partsWhereAfter.GetValueOrDefault(k)}")));
            Check("discoveries and announced blueprints are kept",
                  discBefore.IsSubsetOf(g.progress.discovered) && announcedBefore.All(a => wb.ToDict()["announced"] is List<object> l && l.Contains(a)),
                  $"discovered {discBefore.Count} -> {g.progress.discovered.Count}; announced before: {string.Join(",", announcedBefore)}");
            var locked = wb.projects.Where(p => !p.crafted && !wb.Unlocked(p)).ToList();
            var refused = locked.Select(p => (p, why: wb.PartIds(p).Select(id => wb.CanInstall(p, id)).FirstOrDefault() ?? "")).ToList();
            Check("a locked robot can't be built: the bench refuses its parts with the reason, and it can't be activated",
                  locked.Count > 0 && refused.All(x => x.why.Contains("Requires") || x.why.Contains("operational first") || x.why.Contains("level")) && locked.All(p => !wb.CanActivate(p)),
                  string.Join("; ", refused.Take(4).Select(x => $"{x.p.id}: \"{x.why}\"")));
            var placed = pieces.OrderBy(i => g.world.DefOf(i.id).PegSlot).Select(i => $"{g.world.DefOf(i.id).PegSlot} {i.Position.x:0.0000},{i.Position.y:0.0000},{i.Position.z:0.0000}").ToList();
            File.WriteAllLines(Path.Combine(outDir, "pegmigrate_pieces.txt"), placed);
            Debug.Log($"[pegmigrate] {slot}: {partsBefore} saved parts; hung {hungBefore.Count}; operational {string.Join(",", opAfter)}; pieces {string.Join(" | ", placed)}");
            var mid = pb.transform.TransformPoint(new Vector3(0, pb.cfg.PanelCentre.y, pb.cfg.PanelFront));
            LookFrom(mid - pb.transform.forward * 3.2f, mid);
            yield return Frames(3);
            yield return g.Capture(Path.Combine(outDir, "pegmigrate_board.png"), 1);
        }

        IEnumerator RadarPlanChecks()
        {
            var wb = g.workbench;
            var radar = g.radar;
            g.player.enabled = false;
            var parts = g.world.instances.Where(i => g.world.DefOf(i.id).IsToolItem && !g.world.DefOf(i.id).IsRadarBattery && i.state == ItemState.InWorld)
                         .GroupBy(i => g.world.DefOf(i.id).ToolType).Select(gr => gr.First().id).ToList();
            var plans = g.progression.skills.FirstOrDefault(s => s.id == "part_radar_plans");
            string last = "";
            foreach (var id in parts) Take(id);
            // handed over the way the player does it ([G] at the bench: everything the bench takes from your hands)
            var offered = g.interaction.StationParts();
            bool allOffered = parts.All(offered.Contains);
            foreach (var id in offered.ToList()) { var r = g.workbench.Deliver(id); if (parts.Contains(id)) last = r.text; }
            yield return Frames(2);
            bool waits = !radar.assembled && !radar.HasPlans && parts.All(id => wb.PoolContents().Contains(id)) && last.Contains("blueprint locked");
            Check("radar parts go into the robot parts pool; without the plans the radar is not built",
                  parts.Count == PartRadarModel.Parts.Length && allOffered && waits && plans != null && plans.Retired && !g.UnlockSkill("part_radar_plans"),
                  $"{parts.Count} part types, offered at the bench {allOffered}, assembled {radar.assembled}, pool {wb.PoolContents().Count}; plans skill {plans?.status}; \"{last}\"");
            g.OpenWorkbench("retrobot");
            yield return Frames(4);
            yield return g.Capture(Path.Combine(outDir, "radar_bench_screen.png"), 2);
            g.SetState("play");
            var pr = g.progression;
            int sp = g.progression.skillPoints;
            g.pegRewards.GrantAll(new[] { "part_radar" });   // the first tool on the pegboard (the Part Radar Plans skill is retired)
            radar.TryAssemble();
            yield return Frames(2);
            Check("the Part Radar blueprint (from the pegboard) builds it from the pool at once, no points",
                  radar.HasPlans && radar.assembled && g.progression.skillPoints == sp && !parts.Any(id => wb.PoolContents().Contains(id)),
                  $"assembled {radar.assembled}, points {sp} → {g.progression.skillPoints}");

            // battery pockets: 3 of their own, beside full hands; the Tool Belt adds 3
            var inv = g.inventory;
            var filler = g.world.instances.Where(i => i.state == ItemState.InWorld && !g.world.DefOf(i.id).IsRobotPart).Take(inv.capacity - inv.Parts).Select(i => i.id).ToList();
            foreach (var id in filler) Take(id);
            var bats = g.world.instances.Where(i => g.world.DefOf(i.id).IsRadarBattery && i.state == ItemState.InWorld).Take(7).Select(i => i.id).ToList();
            int took = bats.Count(id => { var before = inv.Batteries; Take(id); return inv.Batteries > before; });
            bool three = inv.IsFull && took == 3 && inv.Batteries == 3 && inv.Parts == inv.capacity;
            pr.level = pr.maxLevel; pr.skillPoints = 99;
            g.UnlockSkill("pockets_1"); g.UnlockSkill("tool_belt");
            int took2 = bats.Skip(3).Count(id => { var before = inv.Batteries; Take(id); return inv.Batteries > before; });
            Check("batteries ride in their own 3 pockets beside full hands; the Tool Belt adds 3 more",
                  three && took2 == 3 && inv.batteryPockets == 6 && inv.Batteries == 6,
                  $"first {took} taken with hands {inv.Parts}/{inv.capacity}; after the Tool Belt {took2} more, {inv.Batteries}/{inv.batteryPockets} pockets");

            // the LED reads under the workshop lights: a big flash as the pulse goes out, then a steady bright amber glow
            g.SetState("play");
            radar.Put(true);
            radar.TrySwap();
            float tw = Time.time;
            while (radar.Swapping && Time.time - tw < 5) yield return null;
            yield return new WaitForSeconds(0.4f);
            radar.TryPulse();
            yield return Frames(2);
            float flash = radar.LightNow;
            yield return g.Capture(Path.Combine(outDir, "radar_flash.png"), 1);
            float lit = 0;
            if (radar.Locked >= 0)
            {
                var cam = g.player.cam.transform;
                var to = g.world.instances[radar.Locked].Position;
                LookFrom(cam.position, new Vector3(to.x, cam.position.y - 0.3f, to.z));
                yield return new WaitForSeconds(0.7f);
                lit = radar.LightNow;
                yield return g.Capture(Path.Combine(outDir, "radar_lit.png"), 1);
            }
            Check("the radar LED flashes bright as a pulse goes out and glows bright amber while on target (screenshots radar_flash, radar_lit)",
                  flash > 20 && lit > 4, $"flash light {flash:0.0}, on-target light {lit:0.0}, locked {radar.Locked}");

            // batteries stay off the hotbar; one spent by a pulse leaves the game
            tw = Time.time;
            while (radar.Pulsing && Time.time - tw < 6) yield return null;
            int spent = radar.battery;
            if (spent >= 0) radar.charges[spent] = 1;
            radar.TryPulse();
            yield return Frames(2);
            var si = spent >= 0 ? g.world.instances[spent] : null;
            bool offBar = !inv.Stacks().Any(s => s.ids.Any(inv.isBattery));
            Check("batteries ride in the pockets (not the hotbar); a battery spent by a pulse is out of the game",
                  offBar && si != null && radar.battery < 0 && si.owner == PartRadar.Spent && !inv.Has(spent) && !g.renderer.Shows(si),
                  $"hotbar clear {offBar}, spent {spent} → {si?.state} {si?.owner}, loaded {radar.battery}, pockets {inv.Batteries}");
            radar.Put(false);
        }

        /// <summary>A preset sets the pipeline, the look and the shadows; one changed value makes it Custom;
        /// every clip plays at its category's volume. (Automated runs never write the saved values.)</summary>
        void SettingsChecks()
        {
            var urp = (UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            var b = g.builder;
            GameSettings.SetPreset(g, 0);
            bool low = urp.renderScale == 0.75f && urp.msaaSampleCount == 1 && urp.mainLightShadowmapResolution == 1024 && b.bloom.active
                       && b.shafts.Count > 0 && b.shafts.All(s => !s.activeSelf) && b.shadowLights.Count > 0 && b.shadowLights.All(l => l.shadows == LightShadows.Hard) && ItemRenderer.ShadowMode == 2;
            Check("settings: Low sets scale, AA, shadows, part shadows and shafts", low,
                  $"scale {urp.renderScale}, MSAA {urp.msaaSampleCount}, shadow map {urp.mainLightShadowmapResolution}, grain {b.grain.active}, shafts {b.shafts.Count(s => s.activeSelf)}/{b.shafts.Count} on, {b.shadowLights.Count} shadow lights, part shadows {ItemRenderer.ShadowMode}");
            GameSettings.Grain = 2; GameSettings.Changed(g);
            bool grainOn = b.grain.active && Mathf.Approximately(b.grain.intensity.value, 0.06f) && GameSettings.Preset == 0;
            GameSettings.SetPreset(g, 3);
            bool grainKept = GameSettings.Grain == 2 && b.grain.active;
            GameSettings.Grain = 0; GameSettings.Changed(g);
            Check("settings: film grain is its own setting (a preset never changes it, Off removes it)", grainOn && grainKept && !b.grain.active && GameSettings.Preset == 3,
                  $"medium on {grainOn}, kept by Ultra {grainKept}, off {!b.grain.active}, preset {GameSettings.PresetNames[GameSettings.Preset]}");
            GameSettings.SetPreset(g, 0);
            GameSettings.Shadows = 0; GameSettings.Changed(g);
            Check("settings: one changed value makes it Custom; shadows off", GameSettings.Preset == GameSettings.Custom && b.shadowLights.All(l => l.shadows == LightShadows.None), GameSettings.PresetNames[GameSettings.Preset]);
            GameSettings.Brightness = 0.5f; GameSettings.Changed(g);
            bool bright = Mathf.Approximately(b.colorAdjust.postExposure.value, WorkshopBuilder.Exposure + 0.5f + GameLook.ExposureOffset);
            GameSettings.ResetDefaults(g);
            bool high = GameSettings.Preset == 2 && urp.renderScale == 1 && urp.msaaSampleCount == 2 && urp.mainLightShadowmapResolution == 4096 && !b.grain.active && b.shafts.All(s => s.activeSelf)
                        && b.shadowLights.All(l => l.shadows == LightShadows.Soft) && Mathf.Approximately(b.colorAdjust.postExposure.value, WorkshopBuilder.Exposure + GameLook.ExposureOffset) && Mathf.Approximately(g.player.cam.fieldOfView, 72);
            Check("settings: brightness moves the exposure; defaults put the designed look back", bright && high, $"bright {bright}, high {high}");
            float w0 = Sfx.masterVolume, r0 = Sfx.robotVolume, u0 = Sfx.uiVolume;
            Sfx.masterVolume = 0.9f; Sfx.robotVolume = 0.3f; Sfx.uiVolume = 0.6f;   // distinct values (a muted run has them all at 0)
            Check("settings: each sound plays at its category's volume",
                  Sfx.VolumeOf("servo") == 0.3f && Sfx.VolumeOf("loop_tracks") == 0.3f && Sfx.VolumeOf("ui_click") == 0.6f && Sfx.VolumeOf("robot_part") == 0.6f && Sfx.VolumeOf("pickup") == 0.9f,
                  "servo and tracks robots, ui_click and robot_part interface, pickup workshop");
            Sfx.masterVolume = w0; Sfx.robotVolume = r0; Sfx.uiVolume = u0;
        }

        /// <summary>Full respec gives back exactly what was spent and loses nothing carried; the previews show
        /// "now → after" within the limits; a robot's measured record survives a save.</summary>
        /// <summary>Skill revamp 2026-10-30 (docs/SKILL_REVAMP.md): the shared point pool, one point per level, no level
        /// requirements, cost 1, Part Sense / Deeper Pockets / Sort Inventory / Automatic Storage Delivery, the retired
        /// skills, Request Pickup, the dev respec and save/load.</summary>
        IEnumerator SkillChecks()
        {
            var pr = g.progression;
            var t = g.tools;
            var it = g.interaction;
            g.player.enabled = false;
            foreach (var r in g.robots) r.enabled = false;
            var active = pr.skills.Where(s => s.Active).ToList();
            var groups = new HashSet<string>();
            int robotSteps = pr.robotUpgrades.Where(u => u.Active && (u.exclusive == "" || groups.Add(u.exclusive))).Sum(u => u.maxRank);
            int steps = active.Count + robotSteps;
            Check("a new game: level 1, 0 points; the budget is round(47.5 % of the buyable steps), max level = budget + 1 (preliminary)",
                  pr.level == 1 && pr.skillPoints == 0 && pr.unlocked.Count == 0 && pr.BuyableSteps == steps && pr.PointBudget == (int)System.Math.Round(0.475 * steps, System.MidpointRounding.AwayFromZero)
                  && pr.maxLevel == pr.PointBudget + 1 && pr.budgetVersion == "prelim-2" && pr.Validate().Count == 0,
                  $"level {pr.level}, {pr.skillPoints} points; {active.Count} player steps + {robotSteps} robot steps = {steps}; budget {pr.PointBudget}, max level {pr.maxLevel}; problems: {string.Join("; ", pr.Validate())}");
            int total = pr.destinationTotal;
            int atMax = pr.DestinationsForLevel(pr.maxLevel);
            var at = new[] { 0.25, 0.5, 0.75 }.Select(f => pr.DestinationsForLevel(1 + (int)System.Math.Round(f * pr.PointBudget))).ToList();
            Check("the curve: the first point after the first completed place, early points fast, the max level at ~85 % of this world's places",
                  pr.DestinationsForLevel(2) == 1 && System.Math.Abs(atMax - 0.85 * total) <= 0.02 * total && at[0] < 0.15 * total,
                  $"{total} places; 25/50/75 % of the points at {string.Join("/", at)} places; max level {pr.maxLevel} at {atMax} ({100.0 * atMax / total:0.0} %)");
            LevelTo(6);
            Check("every level-up gives exactly one point (no halfway points, no bonuses)", pr.level == 6 && pr.skillPoints == 5, $"level {pr.level}, {pr.skillPoints} points");
            var firsts = active.Where(s => s.requires.Count == 0 && s.id != "auto_delivery").ToList();
            Check("no skill has a level requirement: every first level can be bought at level 6 with a point",
                  firsts.All(s => pr.CanUnlock(s.id) == ""), string.Join(", ", firsts.Where(s => pr.CanUnlock(s.id) != "").Select(s => $"{s.id}: {pr.CanUnlock(s.id)}")));
            Check("every buyable level costs 1 (Request Pickup keeps its 3 for now); robot ranks all cost 1",
                  active.All(s => s.cost == (s.id == "auto_delivery" ? 3 : 1)) && pr.robotUpgrades.All(u => Enumerable.Range(0, u.maxRank).All(r => u.CostOf(r) == 1)),
                  string.Join(", ", active.Where(s => s.cost != 1).Select(s => $"{s.id} {s.cost}")));
            var retired = new[] { "part_afterglow", "part_radar_plans", "area_collection" };
            Check("Part Radar Plans, Area Sweep (the skill) and Part Afterglow are out of the shop: retired, not buyable, not counted",
                  retired.All(id => pr.Skill(id) != null && pr.Skill(id).Retired && pr.CanUnlock(id) != "" && !g.UnlockSkill(id)) && !g.radar.HasPlans && t.Level("area_sweep") == 0,
                  string.Join(", ", retired.Select(id => $"{id}: {pr.CanUnlock(id)}")));
            Check("levels are bought in order: Deeper Pockets III needs II", pr.CanUnlock("pockets_3").StartsWith("Requires") && pr.CanUnlock("part_sense_2").StartsWith("Requires"), pr.CanUnlock("pockets_3"));
            // a robot upgrade needs no level, only its robot built
            var pj = g.workbench.P("retrobot");
            foreach (var id in g.workbench.PartIds(pj)) { Take(id); it.DepositStation(id); }
            g.workbench.InstallAll(pj);
            var retro = g.workbench.RobotOf(pj);
            retro.SetOperational();
            int sp = pr.skillPoints;
            bool upg = g.BuyRobotUpgrade("retro_persistence") && g.BuyRobotUpgrade("retro_strategist") == false && g.BuyRobotUpgrade("retro_set") && g.BuyRobotUpgrade("retro_strategist");
            Check("robot upgrades share the pool and have no level requirement (Local Persistence, Set Awareness, Completion Strategist at level 6), 1 point per rank",
                  upg && pr.skillPoints == sp - 3 && pr.RankOf("retro_strategist") == 1, $"points {sp} -> {pr.skillPoints}; {pr.CanBuyUpgrade("retro_persistence", true)}");

            // Deeper Pockets I-X: 10 -> 20, battery pockets untouched; the Tool Belt keeps its 3 battery pockets
            pr.skillPoints = 100;
            int cap0 = g.inventory.capacity, bat0 = g.inventory.batteryPockets;
            var caps = new List<int>();
            for (int k = 1; k <= 10; k++) { g.UnlockSkill(k == 1 ? "pockets_1" : $"pockets_{k}"); caps.Add(g.inventory.capacity); }
            bool beltOk = g.UnlockSkill("tool_belt") && g.inventory.batteryPockets == bat0 + 3 && g.inventory.capacity == 20;
            Check("Deeper Pockets I-X: 10 parts, +1 per level, 20 at X; battery pockets kept (3, the Tool Belt +3)",
                  cap0 == 10 && caps.SequenceEqual(Enumerable.Range(11, 10)) && bat0 == 3 && beltOk,
                  $"{cap0} -> {string.Join(",", caps)}; batteries {bat0} -> {g.inventory.batteryPockets}");
            bool overflow = !g.UnlockSkill("pockets_10");
            Check("Deeper Pockets stops at X", overflow && pr.CanUnlock("pockets_10") == "Already unlocked", pr.CanUnlock("pockets_10"));

            // Part Sense: range, parts, 2 s, cooldown per level; nothing in range costs no cooldown
            var sense = new[] { (1, 2f, 1, 90f), (5, 6f, 3, 50f), (10, 15f, 8, 15f) };
            Check("Part Sense data: range/parts/cooldown per level from the table, 2 s glow at every level",
                  sense.All(x => Mathf.Approximately(PlayerTools.Tuned("part_sense", "range_m", x.Item1, 0), x.Item2) && (int)PlayerTools.Tuned("part_sense", "parts", x.Item1, 0) == x.Item3
                                 && Mathf.Approximately(PlayerTools.Tuned("part_sense", "cooldown_s", x.Item1, 0), x.Item4))
                  && Enumerable.Range(1, 10).All(l => Mathf.Approximately(PlayerTools.Tuned("part_sense", "duration_s", l, 0), 2)),
                  string.Join("; ", Enumerable.Range(1, 10).Select(l => $"{l}: {PlayerTools.Tuned("part_sense", "range_m", l, 0)} m {PlayerTools.Tuned("part_sense", "parts", l, 0)} p {PlayerTools.Tuned("part_sense", "cooldown_s", l, 0)} s")));
            g.UnlockSkill("part_sense");
            var comps = g.world.instances.Where(i => i.state == ItemState.InWorld && g.world.DefOf(i.id).IsRobotComponent && !g.robotReserved.Contains(i.id)).ToList();
            var target = comps.OrderBy(i => (i.Position - g.player.transform.position).sqrMagnitude).First();
            // a spot with no robot part within 2 m: nothing happens and the skill does not recharge
            Vector3 far = Vector3.zero; bool foundFar = false;
            for (int k = 0; k < 400 && !foundFar; k++)
            {
                var c = Shared.Pos(Random.Range(-8f, 8f), 0, Random.Range(-8f, 8f));
                if (comps.All(i => (i.Position - c).sqrMagnitude > 3.5f * 3.5f)) { far = c; foundFar = true; }
            }
            g.player.Teleport(far);
            yield return Frames(2);
            int none = t.PartSense();
            bool noCd = t.CooldownLeft("part_sense") == 0;
            var near = target.Position + Vector3.right * 0.8f; near.y = 0;
            g.player.Teleport(near);
            yield return Frames(2);
            int used = t.PartSense();
            bool glows = t.senseIds.Contains(target.id) && t.senseIds.Count == 1 && Mathf.Abs(t.CooldownLeft("part_sense") - 90) < 1;
            float t0 = Time.time;
            while (Time.time - t0 < 1.0f) yield return null;
            bool stillOn = t.senseIds.Count > 0;
            while (Time.time - t0 < 2.3f) yield return null;
            bool off = t.senseIds.Count == 0;
            Check("Part Sense I: no robot part within 2 m → nothing, no cooldown; within range → the nearest glows for 2 s, then 90 s cooldown",
                  foundFar && none == 0 && noCd && used == 1 && glows && stillOn && off,
                  $"far spot {foundFar}: used {none}, cooldown {noCd}; near ({(target.Position - near).magnitude:0.0} m): used {used}, glows {glows}, at 1 s {stillOn}, at 2.3 s off {off}");
            for (int k = 2; k <= 10; k++) g.UnlockSkill($"part_sense_{k}");
            t.ResetState();
            int inRange = comps.Count(i => (i.Position - g.player.transform.position).sqrMagnitude <= 15 * 15);
            t.PartSense();
            Check("Part Sense X: up to 8 parts within 15 m, cooldown 15 s", t.Level("part_sense") == 10 && t.senseIds.Count == Mathf.Min(8, inRange) && Mathf.Abs(t.CooldownLeft("part_sense") - 15) < 1,
                  $"level {t.Level("part_sense")}, {t.senseIds.Count} glow of {inRange} in range, cooldown {t.CooldownLeft("part_sense"):0}");

            // Sort Inventory: storage area, then type, same type together, otherwise stable
            g.player.Teleport(Shared.Pos(g.layout.spawn.x, 0, g.layout.spawn.z));
            foreach (var id in g.inventory.items.ToList()) { g.inventory.Remove(id); g.world.SetState(id, ItemState.InWorld, "world"); g.index.Insert(id); }
            var pool = g.world.instances.Where(i => i.state == ItemState.InWorld && !g.world.DefOf(i.id).IsRobotPart && !g.world.DefOf(i.id).IsToolItem && !g.world.DefOf(i.id).IsPegboardTool
                                                     && !g.robotReserved.Contains(i.id) && t.AreaOf(i.id) != null).ToList();
            var twoKinds = pool.GroupBy(i => i.defId).Where(gr => gr.Count() >= 2).Take(3).SelectMany(gr => gr.Take(2)).ToList();
            var mixed = twoKinds.Concat(pool.Where(i => !twoKinds.Contains(i)).GroupBy(i => t.AreaOf(i.id)).Take(6).Select(gr => gr.First())).ToList();
            var rng = new System.Random(7);
            mixed = mixed.OrderBy(_ => rng.Next()).ToList();
            foreach (var i in mixed) Take(i.id);
            var before = g.inventory.items.ToList();
            g.UnlockSkill("sort_inventory_1");
            int sorted = t.SortInventory();
            var after = g.inventory.items.ToList();
            string Key(int id) => t.AreaOf(id) + "|" + g.world.DefOf(id).displayName + "|" + g.world.DefOf(id).id;
            bool areaOrder = after.Zip(after.Skip(1), (a, b) => string.CompareOrdinal(Key(a), Key(b)) <= 0).All(x => x);
            bool together = after.GroupBy(id => g.world.instances[id].defId).All(gr => { var ix = gr.Select(id => after.IndexOf(id)).ToList(); return ix.Max() - ix.Min() == ix.Count - 1; });
            bool stable = after.GroupBy(id => g.world.instances[id].defId).All(gr => gr.SequenceEqual(before.Where(gr.Contains)));
            Check("Sort Inventory I: the whole inventory by storage area, then type; same type together, otherwise stable; nothing lost; 30 s cooldown",
                  sorted == 1 && before.Count >= 8 && after.Count == before.Count && after.All(before.Contains) && areaOrder && together && stable && Mathf.Abs(t.CooldownLeft("sort_inventory") - 30) < 1,
                  $"{before.Count} parts; areas {string.Join(" ", after.Select(t.AreaOf))}; order ok {areaOrder}, together {together}, stable {stable}, cooldown {t.CooldownLeft("sort_inventory"):0}");
            Check("Sort Inventory cooldowns per level: 30 28 26 24 22 20 17 14 12 10", Enumerable.Range(1, 10).Select(l => (int)PlayerTools.Tuned("sort_inventory", "cooldown_s", l, 0)).SequenceEqual(new[] { 30, 28, 26, 24, 22, 20, 17, 14, 12, 10 }), "");
            Check("Sort Inventory while recharging does nothing", t.SortInventory() == 0, "");

            // Automatic Storage Delivery and the placement rules (2026-10-30): their own section, run here too
            yield return PlacementChecks();
            g.player.enabled = false;
            var other = pool.First(i => i.state == ItemState.InWorld);
            Take(other.id);
            Check("the skill bar shows Part Sense, Sort Inventory and Storage Delivery on keys 5, 6 and 7",
                  t.Slot("part_sense") == 5 && t.Slot("sort_inventory") == 6 && t.Slot("storage_delivery") == 7 && PlayerTools.Usable.Select(u => t.Slot(u.id)).Distinct().Count() == PlayerTools.Usable.Length, "");

            // Request Pickup: the old robot handover, separate in name and key
            var rp = pr.Skill("auto_delivery");
            Check("Request Pickup (H) is the old robot handover: its own name, cost 3, still needs Robot Task Coordination",
                  rp.name == "Request Pickup" && rp.cost == 3 && rp.requires.SequenceEqual(new[] { "robot_coordination" }) && g.RobotHandover().Contains("Request Pickup"), g.RobotHandover());

            // the skill menu
            g.SetState("paused");
            g.hud.OpenSub("skills");
            g.hud.ShowSkillPage("player");
            yield return g.Capture(Path.Combine(outDir, "skills_menu.png"), 6);
            var rowsShown = g.hud.SkillRowsShown();
            var senseRow = rowsShown.FindIndex(r => r.name == "Part Sense");
            g.hud.SkillFocus(rowsShown.FindIndex(r => r.on == 0 && r.gears == 10));
            string zeroDetail = g.hud.SkillDetailText;
            Check("the skill menu shows the shared points, level / max as gears, now → next, the cost and that not everything can be maxed",
                  g.hud.SkillPointsText.Contains("skill point") && g.hud.SkillSharedText.Contains("can't max everything") && senseRow >= 0 && rowsShown[senseRow].on == 10 && rowsShown[senseRow].chip == "MAX"
                  && rowsShown.Any(r => r.on == 0 && r.gears == 10 && r.chip == "1 pt") && zeroDetail.Contains("Next level (1)") && zeroDetail.Contains("Cost")
                  && !rowsShown.Any(r => r.name.Contains("Part Radar Plans") || r.name.Contains("Part Afterglow")),
                  string.Join("; ", rowsShown.Select(r => $"{r.name} {r.on}/{r.gears} {r.chip}")));
            g.SetState("play");

            // the normal respec is closed; the dev respec (F8, debug mode) refunds everything at what it cost
            int spent = pr.SkillPointsSpent, all = pr.skillPoints + spent;
            int normal = g.Respec();
            int dev = g.Respec(true);
            Check("no free respec in the menu; the dev respec gives every point back, none created; nothing carried is lost",
                  normal == 0 && dev == spent && pr.skillPoints == all && pr.unlocked.Count == 0 && pr.RanksBought == 0 && g.inventory.capacity == 10 && g.inventory.Has(other.id),
                  $"normal {normal}; dev {dev} of {spent}; points {pr.skillPoints} of {all}");

            // save and load keep the new system as it is: no migration
            g.UnlockSkill("part_sense"); g.UnlockSkill("pockets_1"); g.BuyRobotUpgrade("retro_speed");
            int pts = pr.skillPoints, lvl = pr.level;
            var had = pr.unlocked.OrderBy(x => x).ToList();
            g.SaveGame("autotest_skills");
            int toasts = g.hud.toastLog.Count;
            g.LoadGame("autotest_skills");
            yield return Frames(5);
            pr = g.progression;
            Check("save and load keep level, points, skills and robot ranks; a current save is not migrated",
                  pr.level == lvl && pr.skillPoints == pts && pr.unlocked.OrderBy(x => x).SequenceEqual(had) && pr.RankOf("retro_speed") == 1 && pr.migratedFrom == null
                  && !g.hud.toastLog.Skip(toasts).Any(x => x.Contains("NEW SKILL SYSTEM")) && g.inventory.capacity == 11,
                  $"level {pr.level}/{lvl}, points {pr.skillPoints}/{pts}, skills {string.Join(",", pr.unlocked)}");
            File.Delete(SaveSystem.PathOf("autotest_skills"));
        }

        /// <summary>Skill revamp migration A on a copy of an older save (--apslot, default premig_slot1; the fixture itself
        /// is never written): level recomputed from completed destinations, purchases reset, the earned points unspent,
        /// robots / blueprints / pegboard / carried parts kept, a backup, one notice, and no second migration.</summary>
        IEnumerator SkillMigrateChecks(string fixture)
        {
            const string slot = "autotest_skillmig";
            var raw = Json.Obj(Json.Parse(File.ReadAllText(SaveSystem.PathOf(fixture))));
            File.Copy(SaveSystem.PathOf(fixture), SaveSystem.PathOf(slot), true);
            var backups = Path.Combine(SaveSystem.Dir, "backup");
            foreach (var f in Directory.Exists(backups) ? Directory.GetFiles(backups, slot + ".*") : new string[0]) File.Delete(f);
            var rp = Json.Obj(raw["progression"]);
            int oldLevel = Json.Int(rp, "level", 1), oldPoints = Json.Int(rp, "skill_points"), oldSkills = Json.Arr(rp.GetValueOrDefault("unlocked")).Count;
            var opBefore = (Json.Obj(raw.GetValueOrDefault("robots")) ?? new Dictionary<string, object>()).Where(kv => Json.Bool(Json.Obj(kv.Value), "operational")).Select(kv => kv.Key).OrderBy(x => x).ToList();
            var inv0 = Json.Obj(raw["inventory"]);
            int invBefore = Json.Arr(inv0.GetValueOrDefault("items")).Count + Json.Arr(inv0.GetValueOrDefault("batteries")).Count;   // batteries are saved apart
            int toasts = g.hud.toastLog.Count;
            g.LoadGame(slot);
            yield return Frames(5);
            var pr = g.progression;
            var said = g.hud.toastLog.Skip(toasts).ToList();
            int earned = pr.LevelFor(pr.DestinationsCompleted);
            Check("an older save is migrated once: level from its completed destinations, every purchase reset, the earned points (level − 1) unspent, old spent points not added",
                  Json.Int(rp, "version", 2) < Progression.SaveVersion && pr.level == earned && pr.skillPoints == pr.level - 1 && pr.unlocked.Count == 0 && pr.RanksBought == 0,
                  $"{fixture}: before level {oldLevel}, {oldPoints} points, {oldSkills} skills; now level {pr.level} ({pr.DestinationsCompleted} of {pr.destinationTotal} destinations), {pr.skillPoints} points");
            var opAfter = g.robots.Where(r => r.Operational).Select(r => r.id).OrderBy(x => x).ToList();
            Check("built robots, blueprints and the pegboard are kept; no carried part is lost even over the new capacity",
                  opAfter.SequenceEqual(opBefore) && g.inventory.Parts + g.inventory.Batteries == invBefore && g.workbench.projects.Where(g.workbench.Operational).All(g.workbench.Unlocked),
                  $"robots {string.Join(",", opBefore)} -> {string.Join(",", opAfter)}; carried {invBefore} -> {g.inventory.Parts} parts + {g.inventory.Batteries} batteries (capacity {g.inventory.capacity}); hung {g.builder.pegboard.Hung}");
            var bak = Directory.Exists(backups) ? Directory.GetFiles(backups, slot + ".pre-skills-*.json") : new string[0];
            var now = Json.Obj(Json.Parse(File.ReadAllText(SaveSystem.PathOf(slot))));
            Check("a backup of the file as it was, the slot saved again at once in the new version, one clear notice",
                  bak.Length == 1 && File.ReadAllText(bak[0]) == File.ReadAllText(SaveSystem.PathOf(fixture)) && Json.Int(Json.Obj(now["progression"]), "version") == Progression.SaveVersion
                  && said.Count(x => x.Contains("NEW SKILL SYSTEM") && x.Contains($"level {pr.level}") && x.Contains($"{pr.skillPoints} skill point")) == 1,
                  $"backups {bak.Length}; saved version {Json.Int(Json.Obj(now["progression"]), "version")}; said: {string.Join(" | ", said)}");
            g.SetState("paused");
            g.hud.OpenSub("skills");
            yield return g.Capture(Path.Combine(outDir, "skillmig_menu.png"), 6);
            g.SetState("play");
            int pts = pr.skillPoints;
            toasts = g.hud.toastLog.Count;
            g.LoadGame(slot);
            yield return Frames(5);
            pr = g.progression;
            var bak2 = Directory.GetFiles(backups, slot + ".pre-skills-*.json");
            Check("loading it again does not migrate again: same points, no new backup, no notice",
                  pr.migratedFrom == null && pr.skillPoints == pts && bak2.Length == 1 && !g.hud.toastLog.Skip(toasts).Any(x => x.Contains("NEW SKILL SYSTEM")), $"points {pts} -> {pr.skillPoints}, backups {bak2.Length}");
            File.Delete(SaveSystem.PathOf(slot));
            if (File.Exists(SaveSystem.PathOf(slot) + ".bak")) File.Delete(SaveSystem.PathOf(slot) + ".bak");
            foreach (var f in bak2) File.Delete(f);
        }

        /// <summary>Placement rules 2026-10-30 (docs/SKILL_REVAMP.md §1.6): a click and Automatic Storage Delivery both
        /// need the right part in hand and its own storage place under the crosshair, within reach and with room; the
        /// inventory is never searched for something that fits. A click stores the part in hand only, then the hand
        /// takes the next part in the inventory order; ASD stores up to its level of the held part's kind, the held one
        /// counted, from anywhere in the inventory; Sort Inventory sets the order the hotbar shows and the hand follows.</summary>
        /// <summary>The hand is last in, first out (playtest 2026-10-31): 7 copies of a kind, then 3 other parts picked
        /// up and thrown away at once (the real drop) throws those 3, newest first, and the hand is back on the kind.
        /// A part scrolled to stays chosen as its copies go; a pickup puts the new part in hand again.</summary>
        IEnumerator InventoryOrderChecks()
        {
            var inv = g.inventory;
            var it = g.interaction;
            g.player.enabled = false;
            it.enabledInput = false;
            int cap = inv.capacity;
            inv.capacity = Mathf.Max(cap, 12);
            void Empty() { foreach (var id in inv.items.ToList()) { inv.Remove(id); g.world.SetState(id, ItemState.InWorld, "world"); g.index.Insert(id); } }
            bool Loose(ItemInstance i) => i.state == ItemState.InWorld && !g.world.DefOf(i.id).IsRobotPart && !g.world.DefOf(i.id).IsToolItem
                                          && !g.world.DefOf(i.id).IsPegboardTool && !g.robotReserved.Contains(i.id) && !inv.isBattery(i.id);
            Empty();
            var A = g.world.instances.Where(Loose).GroupBy(i => i.defId).First(gr => gr.Count() >= 9);
            var others = g.world.instances.Where(i => Loose(i) && i.defId != A.Key).GroupBy(i => i.defId).Take(4).Select(gr => gr.First().id).ToList();
            Check("inventory test set-up: a kind with 7+ loose copies and 3+ other parts", others.Count >= 4, $"{g.db.Get(A.Key).displayName}: {A.Count()}, others {others.Count}");
            var a7 = A.Take(7).Select(i => i.id).ToList();
            foreach (var id in a7) Take(id);
            foreach (var id in others.Take(3)) Take(id);
            Check("the part just picked up is in hand", inv.HeldId() == others[2], $"held {inv.HeldId()}, last picked {others[2]}");
            var thrown = new List<int>();
            for (int n = 0; n < 3; n++) { thrown.Add(inv.HeldId()); it.DropHeld(); yield return null; }
            Check("throwing three at once throws the three just picked up, newest first", thrown.SequenceEqual(new[] { others[2], others[1], others[0] }),
                  $"thrown {string.Join(",", thrown)}, picked {string.Join(",", others.Take(3))}");
            Check("then the hand is back on the 7 (the last copy picked up)", inv.HeldId() == a7[^1] && inv.Count == 7, $"held {inv.HeldId()}, count {inv.Count}");

            // scrolled to the kind: it stays in hand while copies go, even with a newer part behind it
            Take(others[0]);
            inv.SelectSlot(inv.Stacks().FindIndex(st => st.defId == A.Key));
            it.DropHeld(); yield return null;
            Check("a part scrolled to stays chosen: the next copy is in hand, not the newest part", inv.ChosenByPlayer && inv.heldDef == A.Key,
                  $"chosen {inv.ChosenByPlayer}, held {g.db.Get(inv.heldDef).displayName}");
            Take(others[3]);
            Check("a pickup puts the new part in hand again (last in, first out)", inv.HeldId() == others[3] && !inv.ChosenByPlayer, $"held {inv.HeldId()}, chosen {inv.ChosenByPlayer}");

            yield return null;
            Empty();
            inv.capacity = cap;
            it.enabledInput = true;
            g.player.enabled = true;
        }

        IEnumerator PlacementChecks()
        {
            var pr = g.progression;
            var t = g.tools;
            var it = g.interaction;
            var inv = g.inventory;
            g.player.enabled = false;
            foreach (var r in g.robots) r.enabled = false;
            it.enabledInput = false;
            pr.skillPoints += 20;
            t.ResetState();
            void Empty() { foreach (var id in inv.items.ToList()) { inv.Remove(id); g.world.SetState(id, ItemState.InWorld, "world"); g.index.Insert(id); } }
            void Hold(int id) => inv.SelectSlot(inv.Stacks().FindIndex(s => s.ids.Contains(id)));
            bool Ordinary(ItemInstance i) => i.state == ItemState.InWorld && !g.world.DefOf(i.id).IsRobotPart && !g.world.DefOf(i.id).IsToolItem && !g.world.DefOf(i.id).IsPegboardTool
                                              && !g.robotReserved.Contains(i.id) && t.AreaOf(i.id) != null && g.db.boxes.ContainsKey(g.world.DefOf(i.id).storageBox);
            int Room(string b) => g.db.BoxCapacity(b) - g.storage.Stored(b).Count;
            Empty();
            // the kind K: the most loose copies its storage place has room for (a drawer, so it can be aimed at)
            var kinds = g.world.instances.Where(Ordinary).GroupBy(i => i.defId)
                .Select(gr => (def: gr.Key, box: g.world.DefOf(gr.First().id).storageBox, n: gr.Count()))
                .Where(k => g.units[Json.Str(g.db.boxes[k.box], "unit")].type == "drawer_cabinet" && g.StraysIn(k.box).Count == 0)
                .OrderByDescending(k => Mathf.Min(k.n, Room(k.box))).ToList();
            var K = kinds[0];
            var O = kinds.First(k => k.box != K.box && k.n >= 3);
            string destK = K.box, destO = O.box;
            int NextK() { var i = g.world.instances.First(x => x.defId == K.def && Ordinary(x)); Take(i.id); return i.id; }
            int NextO() { var i = g.world.instances.First(x => x.defId == O.def && Ordinary(x)); Take(i.id); return i.id; }
            int StoredK() => g.storage.Stored(destK).Count;
            bool Free() => t.CooldownLeft("storage_delivery") == 0;
            Check("placement test set-up: a kind with enough loose copies and room (and a second kind with its own place)", Mathf.Min(K.n, Room(destK)) >= 10,
                  $"K {g.db.Get(K.def).displayName} → {destK}: {K.n} loose, room {Room(destK)}; O {g.db.Get(O.def).displayName} → {destO}");
            if (!pr.Has("storage_delivery")) g.UnlockSkill("storage_delivery_1");

            // 1. the wrong part in hand, the right one further in: neither a click nor ASD stores anything
            int o1 = NextO(), k1 = NextK();
            Hold(o1);
            yield return AimAtDest(destK);
            it.UpdateTarget();
            int s0 = StoredK();
            it.Primary();
            int asd1 = t.StorageDelivery();
            yield return Frames(5);
            Check("1. wrong part in hand, the right one further in the inventory: no click placement and no ASD, no cooldown",
                  it.targetType == "dest" && it.targetDest == destK && StoredK() == s0 && inv.Has(k1) && inv.Has(o1) && asd1 == 0 && Free(),
                  $"aimed {it.targetType} {it.targetDest}; stored {s0} -> {StoredK()}; ASD {asd1}, cooldown {t.CooldownLeft("storage_delivery"):0}");

            // 2. the right part in hand, the wrong place under the crosshair
            Hold(k1);
            yield return AimAtDest(destO);
            it.UpdateTarget();
            int sO = g.storage.Stored(destO).Count;
            it.Primary();
            int asd2 = t.StorageDelivery();
            yield return Frames(5);
            Check("2. right part in hand, wrong place under the crosshair: nothing stored, no cooldown",
                  it.targetDest == destO && g.storage.Stored(destO).Count == sO && StoredK() == s0 && inv.Has(k1) && asd2 == 0 && Free(), $"ASD {asd2}");

            // 3. the right part and place, but out of reach, or the place full
            var front = AimPoint(destK);
            var away = front.eye + (front.eye - front.target).normalized * 3.5f;
            LookFrom(away, front.target);
            yield return Frames(3);
            it.UpdateTarget();
            string farType = it.targetType;
            int asd3 = t.StorageDelivery();
            bool farOk = farType != "dest" && asd3 == 0 && Free() && StoredK() == s0;
            float dist = (away - front.target).magnitude;
            var box = g.db.boxes[destK];
            var cap0 = box.GetValueOrDefault("capacity");
            box["capacity"] = (double)StoredK();   // full, for this check only
            yield return AimAtDest(destK);
            it.UpdateTarget();
            it.Primary();
            int asd3b = t.StorageDelivery();
            yield return Frames(5);
            bool fullOk = StoredK() == s0 && inv.Has(k1) && asd3b == 0 && Free();
            if (cap0 == null) box.Remove("capacity"); else box["capacity"] = cap0;
            Check("3. right part and place, but out of reach or the place full: nothing stored, no cooldown",
                  farOk && fullOk, $"{dist:0.0} m away (reach {it.Reach:0.0}): target {farType}, ASD {asd3}; full: ASD {asd3b}, stored {s0} -> {StoredK()}");

            // 4. a click stores the part in hand only; the hand then takes the next part in the order, whatever it is
            Empty();
            k1 = NextK(); o1 = NextO(); int k2 = NextK();   // order K, O, K
            Hold(k1);
            yield return AimAtDest(destK);
            it.UpdateTarget();
            it.Primary();
            yield return Frames(15);
            int heldAfter = inv.HeldId();
            it.UpdateTarget();
            it.Primary();   // O in hand: refused, the K further in is not taken
            yield return Frames(15);
            Check("4. a click stores exactly the part in hand, then the next in the inventory order comes up (no skipping to a part that fits)",
                  StoredK() == s0 + 1 && !inv.Has(k1) && heldAfter == o1 && inv.Has(o1) && inv.Has(k2) && StoredK() == s0 + 1,
                  $"stored {s0} -> {StoredK()}; in hand after: {(heldAfter >= 0 ? g.world.DefOf(heldAfter).displayName : "nothing")} (expected {g.db.Get(O.def).displayName})");

            // 5. ASD I counts the part in hand and stores one; 8. a click still works during its cooldown
            int k3 = NextK();
            Hold(k2);   // chosen after the pickup: a pickup puts the new part in hand (last in, first out)
            it.UpdateTarget();
            int s5 = StoredK();
            int asd5 = t.StorageDelivery();
            yield return Frames(15);
            bool five = asd5 == 1 && StoredK() == s5 + 1 && !inv.Has(k2) && inv.Has(k3) && Mathf.Abs(t.CooldownLeft("storage_delivery") - 90) < 1;
            Hold(k3);
            it.UpdateTarget();
            it.Primary();
            yield return Frames(15);
            Check("5. ASD I: the part in hand counts, one part stored, no more; cooldown 90 s", five, $"ASD {asd5}, stored {s5} -> {s5 + asd5}");
            Check("8. a click stores while ASD recharges", !inv.Has(k3) && StoredK() == s5 + 2 && t.CooldownLeft("storage_delivery") > 80 && t.StorageDelivery() == 0, $"stored {StoredK() - s5} of 2");

            // 6. ASD III takes more of the held kind from anywhere in the inventory, the held one counted, up to 3
            g.UnlockSkill("storage_delivery_2"); g.UnlockSkill("storage_delivery_3");
            t.ResetState();
            Empty();
            int a1 = NextK(); int b1 = NextO(); int a2 = NextK(); int b2 = NextO(); int a3 = NextK(); int a4 = NextK();   // K O K O K K
            Hold(a2);
            yield return AimAtDest(destK);
            it.UpdateTarget();
            int s6 = StoredK();
            int asd6 = t.StorageDelivery();
            yield return Frames(15);
            Check("6. ASD III after a valid aim: the held part and two more of its kind from elsewhere in the inventory (3 = its level), other kinds untouched, cooldown 70 s",
                  asd6 == 3 && StoredK() == s6 + 3 && !inv.Has(a2) && !inv.Has(a1) && !inv.Has(a3) && inv.Has(a4) && inv.Has(b1) && inv.Has(b2) && Mathf.Abs(t.CooldownLeft("storage_delivery") - 70) < 1,
                  $"ASD {asd6}; left: {string.Join(", ", inv.items.Select(i => g.world.DefOf(i).displayName))}");

            // 7. Sort Inventory sets the real order: the hotbar shows it and a sorted group comes up one by one
            t.ResetState();
            Empty();
            int c1 = NextK(); int d1 = NextO(); int c2 = NextK(); int d2 = NextO(); int c3 = NextK();   // K O K O K
            if (!pr.Has("sort_inventory")) g.UnlockSkill("sort_inventory_1");
            t.SortInventory();
            var order = inv.items.ToList();
            bool barSame = inv.Stacks().SelectMany(s => s.ids).SequenceEqual(order);
            bool barStacked = inv.Stacks().Select(s => s.defId).SequenceEqual(order.Select(i => g.world.instances[i].defId).Distinct());   // one slot per sorted group
            var ks = order.Where(i => g.world.instances[i].defId == K.def).ToList();
            bool grouped = order.IndexOf(ks[^1]) - order.IndexOf(ks[0]) == ks.Count - 1;
            Hold(ks[0]);
            yield return AimAtDest(destK);
            var held7 = new List<int>();
            for (int k = 0; k < 3; k++)
            {
                it.UpdateTarget();
                held7.Add(inv.HeldId());
                it.Primary();
                yield return Frames(15);
            }
            int afterGroup = inv.HeldId();
            Check("7. Sort Inventory: the hotbar shows the sorted order, each group in one slot; clicks store the K group one by one, then the next kind comes up",
                  barSame && barStacked && grouped && held7.SequenceEqual(ks) && !ks.Any(inv.Has) && afterGroup >= 0 && g.world.instances[afterGroup].defId == O.def,
                  $"order {string.Join(" ", order.Select(i => g.world.instances[i].defId == K.def ? "K" : "O"))}; bar same {barSame}, stacked {barStacked}; stored {held7.Count(i => !inv.Has(i))} of {ks.Count}");
            int e1 = NextK(); NextK();
            Hold(e1);
            it.UpdateTarget();
            Check("no Store all at a storage place: with two that fit, the hint offers [LMB] only", it.Describe().hint == "[LMB] Store here", it.Describe().hint);
            Empty();
            t.ResetState();
            it.enabledInput = true;
        }

        /// <summary>Placement rules 2026-10-30 on the pegboard (docs/PEGBOARD.md): only the tool in hand is hung, at its
        /// own slot within reach; the inventory is never searched for a tool that fits; no G "Hang all"; the next item
        /// in the inventory order comes up. The first-tool reward still comes with the first click.</summary>
        IEnumerator PegPlacementChecks()
        {
            var it = g.interaction;
            var inv = g.inventory;
            var pb = g.builder.pegboard;
            var cfg = pb.cfg;
            g.player.enabled = false;
            foreach (var r in g.robots) r.enabled = false;
            it.enabledInput = false;
            void Empty() { foreach (var id in inv.items.ToList()) { inv.Remove(id); g.world.SetState(id, ItemState.InWorld, "world"); g.index.Insert(id); } }
            void Hold(int id) => inv.SelectSlot(inv.Stacks().FindIndex(s => s.ids.Contains(id)));
            string Slot(int id) => g.world.DefOf(id).PegSlot;
            bool Plain(ItemDefinition d) => !cfg.slotById[d.PegSlot].IsChild && cfg.slotById[d.PegSlot].children.Count == 0;   // not a set's holder or piece
            IEnumerator Aim(string slot, float back)
            {
                var at = pb.SlotCentre(slot);
                LookFrom(at - pb.transform.forward * back + Vector3.down * 0.15f, at);
                yield return Frames(2);
                it.UpdateTarget();
            }
            Empty();
            var loose = g.world.instances.Where(i => i.state == ItemState.InWorld && g.world.DefOf(i.id).IsPegboardTool && Plain(g.world.DefOf(i.id)) && !pb.IsOccupied(g.world.DefOf(i.id).PegSlot)).ToList();
            int t1 = loose[0].id;
            int t2 = loose.First(i => Vector3.Distance(pb.SlotCentre(Slot(i.id)), pb.SlotCentre(Slot(t1))) > 0.6f).id;   // away from t1's aim assist
            int part = g.world.instances.First(i => i.state == ItemState.InWorld && !g.world.DefOf(i.id).IsPegboardTool && !g.world.DefOf(i.id).IsRobotPart
                                                     && !g.world.DefOf(i.id).IsToolItem && !g.robotReserved.Contains(i.id)).id;
            int hung0 = pb.Hung;
            bool firstFree = !g.pegRewards.Has("part_radar");

            // P1. the wrong item in hand (a part, then another tool), the right tool further in: a click hangs nothing
            Take(part); Take(t2); Take(t1);   // order: part, t2, t1
            Hold(part);
            yield return Aim(Slot(t1), 1.1f);
            string aimed = it.targetType + " " + it.targetPeg;
            it.Primary();
            yield return Frames(2);
            bool p1a = it.targetType == "peg" && it.targetPeg == Slot(t1) && pb.Hung == hung0 && inv.Has(t1) && inv.Has(part);
            Hold(t2);
            yield return Aim(Slot(t1), 1.1f);
            var info = it.Describe();
            it.Primary();
            yield return Frames(2);
            bool p1b = it.targetPeg == Slot(t1) && info.tone == -1 && pb.Hung == hung0 && inv.Has(t1) && inv.Has(t2);
            Check("P1. wrong item in hand (a part, or another tool), the right tool further in the inventory: a click hangs nothing",
                  p1a && p1b, $"aimed {aimed}; part {p1a}, other tool {p1b} (hint '{info.hint}'); hung {hung0} -> {pb.Hung}");

            // P2. G at the pegboard: no Hang all
            Hold(t1);
            yield return Aim(Slot(t1), 1.1f);
            info = it.Describe();
            it.DepositAll();
            yield return Frames(2);
            Check("P2. no G Hang all: G at the board hangs nothing; the hint offers [LMB] only",
                  pb.Hung == hung0 && inv.Has(t1) && inv.Has(t2) && info.tone == 1 && info.hint.StartsWith("[LMB]") && !info.hint.Contains("[G]"), $"hint '{info.hint}', hung {pb.Hung}");

            // P3. the right tool and slot, but out of reach
            yield return Aim(Slot(t1), it.Reach + 1.5f);
            string far = it.targetType;
            it.Primary();
            yield return Frames(2);
            Check("P3. right tool in hand, its slot out of reach: not targeted, nothing hung", far != "peg" && pb.Hung == hung0 && inv.Has(t1), $"target '{far}' from {it.Reach + 1.5f:0.0} m");

            // P4. a click hangs exactly the tool in hand, then the next item in the order comes up (no skip to a tool)
            Empty();
            Take(t1); Take(part); Take(t2);   // order: t1, part, t2
            Hold(t1);
            int said0 = g.hud.toastLog.Count;
            yield return Aim(Slot(t1), 1.1f);
            it.Primary();
            yield return Frames(2);
            int next = inv.HeldId();
            var said = g.hud.toastLog.Skip(said0).ToList();
            yield return Aim(Slot(t2), 1.1f);
            it.Primary();   // the part in hand: t2 further in is not hung
            yield return Frames(2);
            Check("P4. a click hangs only the tool in hand, then the next item in the inventory order is in hand (no skip to the other tool)",
                  pb.IsOccupied(Slot(t1)) && !inv.Has(t1) && pb.Hung == hung0 + 1 && next == part && !pb.IsOccupied(Slot(t2)) && inv.Has(t2),
                  $"hung {g.world.DefOf(t1).displayName}: {pb.IsOccupied(Slot(t1))}; in hand after: {(next >= 0 ? g.world.DefOf(next).displayName : "nothing")}; t2 hung {pb.IsOccupied(Slot(t2))}");
            if (firstFree)
                Check("P5. the first tool hung by a click still unlocks the Part Radar blueprint once",
                      g.pegRewards.Has("part_radar") && g.radar.HasPlans && said.Count(t => t.Contains("Blueprint unlocked: Part Radar")) == 1, string.Join(" | ", said));

            // P6. the next tool after a hang: the right one in hand at its slot hangs
            Hold(t2);
            yield return Aim(Slot(t2), 1.1f);
            it.Primary();
            yield return Frames(2);
            Check("P6. the right tool in hand at its own slot hangs", pb.IsOccupied(Slot(t2)) && !inv.Has(t2) && pb.Hung == hung0 + 2 && inv.HeldId() == part, $"hung {pb.Hung}");

            // H. a set piece aims at its holder (docs/PEGBOARD.md "Tool sets"): anywhere on its own mounted holder,
            // mounted pieces included, hangs it in its own place; another holder, the wrong item in hand, a missing
            // holder, its place taken or out of reach hangs nothing
            int ToolIn(string slot) => g.world.instances.First(i => g.world.DefOf(i.id).IsPegboardTool && g.world.DefOf(i.id).PegSlot == slot).id;
            var H = cfg.slots.First(x => x.children.Count >= 3 && !pb.IsOccupied(x.id) && !x.children.Any(pb.IsOccupied)
                                         && g.world.instances[ToolIn(x.id)].state == ItemState.InWorld && x.children.All(c => g.world.instances[ToolIn(c)].state == ItemState.InWorld));
            var H2 = cfg.slots.Where(x => x.children.Count > 0 && x.id != H.id && !pb.IsOccupied(x.id)).OrderBy(x => Vector3.Distance(pb.SlotCentre(x.id), pb.SlotCentre(H.id))).First();
            string c1 = H.children[0], c3 = H.children[H.children.Count - 1], c2 = H.children[H.children.Count / 2];   // first and last place, one between
            int holder = ToolIn(H.id), p1 = ToolIn(c1), p2 = ToolIn(c2), p3 = ToolIn(c3);
            string sec = cfg.SectionOf(H.id).id;
            int secHung0 = pb.SectionHung(sec);
            IEnumerator AimAt(Vector3 at, float back)
            {
                LookFrom(at - pb.transform.forward * back + Vector3.down * 0.15f, at);
                yield return Frames(2);
                it.UpdateTarget();
            }
            string Name(int id) => g.world.DefOf(id).displayName;
            string HoldName() => inv.HeldId() >= 0 ? Name(inv.HeldId()) : "nothing";
            Empty();
            int hungH = pb.Hung;

            // H1. its holder not mounted: aiming at the holder's place says so, nothing hangs
            Take(p1); Hold(p1);
            yield return AimAt(pb.SlotCentre(H.id), 1.1f);
            info = it.Describe();
            string h1t = it.targetType + " " + it.targetPeg;
            it.Primary();
            yield return Frames(2);
            Check($"H1. a set piece in hand ({Name(p1)}), its holder not mounted: the holder's place is the target, the hint says to mount it first, nothing hangs",
                  h1t == "peg " + c1 && info.hint.StartsWith("Mount") && !pb.IsOccupied(c1) && inv.Has(p1) && pb.Hung == hungH,
                  $"target {h1t}; hint '{info.hint}'; hung {pb.Hung - hungH}");

            // the holder goes up
            Take(holder); Hold(holder);
            yield return Aim(H.id, 1.1f);
            it.Primary();
            yield return Frames(2);
            bool mounted = pb.IsOccupied(H.id) && !inv.Has(holder);

            // H2. the right piece, aimed at the far end of its mounted holder: its own place, then the next item in the order
            Take(part);   // order: p1, part
            Hold(p1);
            yield return AimAt(pb.SlotCentre(c3), 1.1f);
            info = it.Describe();
            string h2t = it.targetType + " " + it.targetPeg;
            it.Primary();
            yield return Frames(2);
            Check($"H2. the right piece in hand, aimed at the far end of its mounted holder ({cfg.toolById[H.tool].name}): it hangs in its own place, not the one aimed at; the next item in the order comes up",
                  mounted && h2t == "peg " + c1 && info.tone == 1 && info.hint.Contains(" on the ") && pb.IsOccupied(c1) && !inv.Has(p1) && !pb.IsOccupied(c3) && inv.HeldId() == part,
                  $"holder up {mounted}; target {h2t} (own {c1}, aimed {c3}); hint '{info.hint}'; hung {pb.IsOccupied(c1)}; in hand after: {HoldName()}");

            // H3. a partly filled holder is still the target: aimed at the piece just mounted
            Take(p2); Hold(p2);
            yield return AimAt(pb.SlotCentre(c1), 1.1f);
            string h3t = it.targetType + " " + it.targetPeg;
            it.Primary();
            yield return Frames(2);
            Check("H3. a partly filled holder is still a target: aimed at a mounted piece, the next piece hangs in its own place",
                  h3t == "peg " + c2 && pb.IsOccupied(c2) && !inv.Has(p2) && pb.IsOccupied(c1), $"target {h3t} (own {c2}); hung {pb.IsOccupied(c2)}");

            // H4. the right piece aimed at another (the nearest) holder: that holder is the target, nothing hangs
            Take(p3); Hold(p3);   // order: part, p3
            yield return AimAt(pb.SlotCentre(H2.id), 1.1f);
            info = it.Describe();
            string h4t = it.targetType + " " + it.targetPeg;
            it.Primary();
            yield return Frames(2);
            Check($"H4. the right piece aimed at another holder ({cfg.toolById[H2.tool].name}, the nearest): that holder is the target, nothing hangs",
                  h4t == "peg " + H2.id && info.tone != 1 && !pb.IsOccupied(c3) && inv.Has(p3) && !pb.IsOccupied(H2.id),
                  $"target {h4t}; hint '{info.hint}'; hung {pb.IsOccupied(c3)}");

            // H5. the wrong item in hand, the piece further in the inventory: aimed at its holder, nothing hangs
            Hold(part);
            yield return AimAt(pb.SlotCentre(H.id), 1.1f);
            it.Primary();
            yield return Frames(2);
            Check("H5. the wrong item in hand, the right piece further in the inventory: aimed at its holder, nothing hangs",
                  !pb.IsOccupied(c3) && inv.Has(p3) && inv.Has(part), $"in hand {HoldName()}; hung {pb.IsOccupied(c3)}");

            // H6. its place already taken (set up directly on the board): no second piece goes up
            pb.Install(c3, g.world.DefOf(p3));
            int hungTaken = pb.Hung;
            Hold(p3);
            yield return AimAt(pb.SlotCentre(H.id), 1.1f);
            it.Primary();
            yield return Frames(2);
            bool h6 = pb.Hung == hungTaken && inv.Has(p3) && !g.storage.Stored(Pegboard.PegboardConfig.DestPrefix + c3).Contains(p3);
            pb.Remove(c3);
            Check("H6. the piece's place already taken: nothing hangs, the piece stays in hand", h6 && !pb.IsOccupied(c3), $"hung {hungTaken} -> {pb.Hung}; in inventory {inv.Has(p3)}");

            // H7. out of reach: the holder is not targeted
            yield return AimAt(pb.SlotCentre(H.id), it.Reach + 1.5f);
            string far7 = it.targetType;
            it.Primary();
            yield return Frames(2);
            Check("H7. the right piece and its holder, out of reach: not targeted, nothing hangs", far7 != "peg" && !pb.IsOccupied(c3) && inv.Has(p3), $"target '{far7}' from {it.Reach + 1.5f:0.0} m");

            // H8. back in reach, aimed at the holder's centre: it hangs; the section counts it, rewards follow the board
            yield return AimAt(pb.SlotCentre(H.id), 1.1f);
            it.Primary();
            yield return Frames(2);
            var rw = g.pegRewards;
            bool rewardsMatch = rw.Milestones.All(ms => rw.Met(ms, pb) == ms.unlocks.All(rw.Has));
            Check($"H8. aimed at the holder's centre, the piece hangs; the {sec} section counts holder and pieces, rewards match the board",
                  pb.IsOccupied(c3) && !inv.Has(p3) && pb.SectionHung(sec) == secHung0 + 4 && pb.Hung == hungH + 4 && rewardsMatch && g.PegboardGuard(false).Count == 0,
                  $"hung {pb.Hung - hungH}; section {sec} {secHung0} -> {pb.SectionHung(sec)}/{pb.SectionTotal(sec)}; rewards match {rewardsMatch}; granted [{string.Join(", ", rw.Granted)}]");

            // H9. save/load keeps the holder and its pieces
            string PegState() { var b = g.builder.pegboard; return $"{b.Hung}|{b.SectionHung(sec)}|{string.Join(",", b.Occupied.OrderBy(x => x))}|{string.Join(",", g.pegRewards.Granted.OrderBy(x => x))}"; }
            var pegBefore = PegState();
            g.SaveGame("autotest_pegholder");
            g.StartWorld(g.world.seed, null);
            g.LoadGame("autotest_pegholder");
            g.player.enabled = false;
            yield return Frames(4);
            var pegAfter = PegState();
            Check("H9. the holder and its pieces hung by aiming at the holder survive save/load (board, section, rewards)",
                  pegBefore == pegAfter && g.builder.pegboard.IsOccupied(c3), pegBefore == pegAfter ? $"same: {pegBefore.Split('|')[0]} hung" : $"{pegBefore}  vs  {pegAfter}");
            File.Delete(SaveSystem.PathOf("autotest_pegholder"));
            inv = g.inventory;
            Empty();
            g.interaction.enabledInput = true;
        }

        (Vector3 eye, Vector3 target) AimPoint(string dest)
        {
            var d = g.db.boxes[dest];
            var u = g.units[Json.Str(d, "unit")];
            Vector3 target = u.type == "drawer_cabinet" ? (Vector3)u.FrontWorld(u.SlotOf(dest)).GetColumn(3)
                : (Vector3)u.DestWorld(dest).GetColumn(3) + Vector3.up * Mathf.Min(StorageUnit.Inner(d).y, 0.25f) * 0.5f;
            var eye = target + (-u.transform.forward).normalized * 1.0f;
            eye.y = Mathf.Clamp(target.y + 0.35f, 0.9f, 1.75f);
            return (eye, target);
        }

        /// <summary>The skill panel (docs/SKILL_PANEL.md): pages, the shared points, gear rows, the detail column
        /// from hover and selection, buying and refusals, robot pages (built and not), save / load.</summary>
        IEnumerator SkillPanelChecks()
        {
            var pr = g.progression;
            var wb = g.workbench;
            var pj = wb.P("retrobot");
            foreach (var id in wb.PartIds(pj)) { Take(id); g.interaction.DepositStation(id); }
            wb.InstallAll(pj);
            wb.RobotOf(pj).SetOperational();
            pr.skillPoints = 3;   // QA: a few points to spend in the panel
            g.SetState("paused");
            g.hud.OpenSub("skills");
            yield return Frames(3);
            var pages = g.hud.SkillPages();
            var robotTypes = pr.robotUpgrades.Select(u => u.robot).Distinct().ToList();
            Check("1 pages: PLAYER first, then one page per robot type with upgrades, in the agreed order",
                  pages[0] == "player" && pages.Count == robotTypes.Count + 1 && robotTypes.All(pages.Contains)
                  && pages.Skip(1).Take(6).SequenceEqual(new[] { "retrobot", "unibot", "caterpillarbot", "spiderbot", "hoverbot", "sorting_arm" }.Where(robotTypes.Contains)),
                  string.Join(", ", pages));
            Check("2 it opens on PLAYER SKILLS with the back arrow muted and the next arrow live",
                  g.hud.SkillPageIndex == 0 && g.hud.SkillPageTitle == "PLAYER SKILLS" && !g.hud.SkillPrevEnabled && g.hud.SkillNextEnabled,
                  $"page {g.hud.SkillPageIndex} '{g.hud.SkillPageTitle}', prev {g.hud.SkillPrevEnabled}, next {g.hud.SkillNextEnabled}");
            Check("3 the shared-points message and the available points are shown",
                  g.hud.SkillSharedText.Contains("Skill points are shared between player skills and robot upgrades.") && g.hud.SkillPointsText.Contains("<b>3</b> skill points"),
                  g.hud.SkillPointsText);
            yield return g.Capture(Path.Combine(outDir, "skillpanel_player.png"), 6);

            // the player rows: one per skill, a gear per level, black = bought
            int levels = pr.skills.Count(s => !s.Retired);
            var rows = g.hud.SkillRowsShown();
            int pockets = rows.FindIndex(r => r.name == "Deeper Pockets");
            Check("4 player rows: every skill once (a gear per skill level in all ; 10 for ten-level skills, fewer otherwise), none bought yet",
                  rows.Sum(r => r.gears) == levels && rows.Select(r => r.name).Distinct().Count() == rows.Count && pockets >= 0 && rows[pockets].gears == 10 && rows.Any(r => r.gears == 1) && rows.All(r => r.on == 0) && rows.All(r => r.level.StartsWith("0 / ")),
                  string.Join("; ", rows.Select(r => $"{r.name} {r.on}/{r.gears} {r.chip}")));

            // hover and keyboard selection drive the detail column; values come from the game data
            UnityEngine.EventSystems.ExecuteEvents.Execute(g.hud.SkillRowObject(pockets), new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current), UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
            string d = g.hud.SkillDetailText;
            int cap = g.inventory.capacity, step = (int)pr.Skill("pockets_1").effects["inventory_capacity"];
            Check("5 hovering a row shows its name, level, current and next effect and cost (from the data)",
                  g.hud.SkillFocusRow == pockets && d.Contains("DEEPER POCKETS") && d.Contains("Level 0 / 10") && d.Contains("Not learned yet") && d.Contains($"Carries {cap + step} parts") && d.Contains("1 skill point"),
                  d);
            int sel = rows.FindIndex(r => r.name == "Part Sense");
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(g.hud.SkillRowObject(sel));
            yield return Frames(1);
            d = g.hud.SkillDetailText;
            Check("6 selecting a row with keyboard / controller shows its detail too (Part Sense: range, parts, cooldown)",
                  g.hud.SkillFocusRow == sel && d.Contains("PART SENSE") && d.Contains($"Range: {PlayerTools.Tuned("part_sense", "range_m", 1, 0):0} m") && d.Contains($"Cooldown: {PlayerTools.Tuned("part_sense", "cooldown_s", 1, 0):0} s"),
                  d);

            // buying: points, gears, level and detail update at once
            g.hud.SkillRowClick(pockets);
            yield return Frames(1);
            rows = g.hud.SkillRowsShown();
            d = g.hud.SkillDetailText;
            Check("7 clicking buys the next level: 1 point spent, a gear turns black, the detail and points update, success is said",
                  pr.unlocked.Contains("pockets_1") && pr.skillPoints == 2 && rows[pockets].on == 1 && rows[pockets].level == "1 / 10" && g.inventory.capacity == cap + step
                  && d.Contains("Level 1 / 10") && d.Contains($"Carries {cap + step} parts") && d.Contains($"Carries {cap + 2 * step} parts") && g.hud.SkillPointsText.Contains("<b>2</b>") && g.hud.SkillStatusText.Contains("Learned"),
                  $"points {pr.skillPoints}; row {rows[pockets]}; status '{g.hud.SkillStatusText}'; {d}");

            // locked: Supermagnet needs Magnetic Glove II
            int sup = rows.FindIndex(r => r.name.StartsWith("Supermagnet"));
            g.hud.SkillRowClick(sup);
            Check("8 a locked skill says LOCKED and why; clicking buys nothing",
                  sup >= 0 && g.hud.SkillRowsShown()[sup].chip == "LOCKED" && !pr.unlocked.Contains("supermagnet") && pr.skillPoints == 2 && g.hud.SkillStatusText.Contains("locked") && g.hud.SkillDetailText.Contains("locked: Requires"),
                  $"{(sup >= 0 ? g.hud.SkillRowsShown()[sup].ToString() : "no Supermagnet row")}; '{g.hud.SkillStatusText}'");

            // not enough points
            pr.skillPoints = 0;
            g.hud.SkillRowClick(pockets);
            Check("9 without points: the cost shows as unaffordable and a click says so, nothing bought",
                  !pr.unlocked.Contains("pockets_2") && g.hud.SkillStatusText.Contains("Not enough skill points") && g.hud.SkillRowsShown()[pockets].chip == "1 pt" && g.hud.SkillDetailText.Contains("not enough skill points"),
                  $"'{g.hud.SkillStatusText}'");

            // max level
            int belt = rows.FindIndex(r => r.name == "Tool Belt");
            pr.skillPoints = 1;
            g.hud.SkillRowClick(belt);
            g.hud.SkillFocus(belt);
            Check("10 a maxed skill: all gears black, MAX, MAX LEVEL REACHED, and a click says so",
                  pr.unlocked.Contains("tool_belt") && g.hud.SkillRowsShown()[belt].chip == "MAX" && g.hud.SkillRowsShown()[belt].on == g.hud.SkillRowsShown()[belt].gears && g.hud.SkillDetailText.Contains("MAX LEVEL REACHED"),
                  g.hud.SkillDetailText);
            g.hud.SkillRowClick(belt);
            bool maxSaid = g.hud.SkillStatusText.Contains("max level reached");

            // page turning with the arrows, and the end stop
            g.hud.SkillArrow(1).onClick.Invoke();
            yield return Frames(1);
            Check("11 the next arrow turns to RETROBOT UPGRADES; only Retrobot's upgrades, a gear per rank",
                  maxSaid && g.hud.SkillPageTitle == "RETROBOT UPGRADES" && g.hud.SkillPrevEnabled
                  && g.hud.SkillRowsShown().Select(r => r.name).SequenceEqual(pr.robotUpgrades.Where(u => u.robot == "retrobot").Select(u => u.name))
                  && g.hud.SkillRowsShown().Select(r => r.gears).SequenceEqual(pr.robotUpgrades.Where(u => u.robot == "retrobot").Select(u => u.maxRank)),
                  $"max said {maxSaid}; '{g.hud.SkillPageTitle}': {string.Join("; ", g.hud.SkillRowsShown().Select(r => $"{r.name} {r.gears}"))}");
            Check("12 the shared-points message stays on robot pages", g.hud.SkillSharedText.Contains("shared between player skills and robot upgrades") && g.hud.SkillPointsText.Contains("<b>0</b>"), g.hud.SkillPointsText);
            yield return g.Capture(Path.Combine(outDir, "skillpanel_retrobot.png"), 6);

            // a built robot: buy a rank from the same pool
            pr.skillPoints = 2;
            var first = pr.robotUpgrades.First(u => u.robot == "retrobot");
            g.hud.SkillFocus(0);
            string rd = g.hud.SkillDetailText;
            g.hud.SkillRowClick(0);
            Check("13 a robot upgrade is bought from the shared pool: rank 1, a black gear, current and next stat lines",
                  pr.RankOf(first.id) == 1 && pr.skillPoints == 2 - first.CostOf(0) && g.hud.SkillRowsShown()[0].on == 1 && rd.Contains("Rank 0 / ") && rd.Contains("Next rank (1)") && g.hud.SkillDetailText.Contains("Rank 1 / "),
                  $"{first.id} rank {pr.RankOf(first.id)}, points {pr.skillPoints}; before: {rd}");

            // an unbuilt robot: listed for planning, cannot be bought
            var unbuilt = pages.Skip(1).FirstOrDefault(t => !g.robots.Any(r => r.type == t && r.Operational));
            g.hud.ShowSkillPage(unbuilt);
            int before = pr.skillPoints;
            g.hud.SkillRowClick(0);
            yield return g.Capture(Path.Combine(outDir, "skillpanel_unbuilt.png"), 6);
            Check("14 an unbuilt robot's page lists its upgrades with a not-built note; buying is refused with the reason",
                  unbuilt != null && g.hud.SkillNoteText.Contains("not built yet") && g.hud.SkillRowsShown().All(r => r.chip == "LOCKED") && pr.skillPoints == before
                  && pr.robotUpgrades.Where(u => u.robot == unbuilt).All(u => pr.RankOf(u.id) == 0) && g.hud.SkillStatusText.Contains("Build the robot first"),
                  $"{unbuilt}: note '{g.hud.SkillNoteText}', status '{g.hud.SkillStatusText}'");

            for (int i = 0; i < pages.Count + 2; i++) g.hud.SkillPageStep(1);
            bool atEnd = g.hud.SkillPageIndex == pages.Count - 1 && !g.hud.SkillNextEnabled && g.hud.SkillPrevEnabled;
            for (int i = 0; i < pages.Count + 2; i++) g.hud.SkillPageStep(-1);
            Check("15 paging stops at both ends (arrows muted there)", atEnd && g.hud.SkillPageIndex == 0 && !g.hud.SkillPrevEnabled, $"end ok {atEnd}, back to {g.hud.SkillPageIndex}");

            // save / load keeps what was bought; the panel shows it again
            int pts = pr.skillPoints;
            g.SetState("play");
            g.SaveGame("autotest_skillpanel");
            g.LoadGame("autotest_skillpanel");
            yield return Frames(5);
            pr = g.progression;
            g.SetState("paused");
            g.hud.OpenSub("skills");
            g.hud.ShowSkillPage("player");
            var after = g.hud.SkillRowsShown();
            g.hud.ShowSkillPage("retrobot");
            var afterRobot = g.hud.SkillRowsShown();
            Check("16 after save and load the panel shows the same levels, ranks and points",
                  after[pockets].on == 1 && after[belt].chip == "MAX" && afterRobot[0].on == 1 && pr.skillPoints == pts && g.hud.SkillPointsText.Contains($"<b>{pts}</b>"),
                  $"pockets {after[pockets]}, belt {after[belt]}, {afterRobot[0]}, points {pr.skillPoints}/{pts}");
            g.SetState("play");
        }


        IEnumerator RespecChecks()
        {
            var wb = g.workbench;
            var pr = g.progression;
            var pj = wb.P("retrobot");
            foreach (var id in wb.PartIds(pj)) { Take(id); g.interaction.DepositStation(id); }
            wb.InstallAll(pj);
            var retro = wb.RobotOf(pj);
            retro.SetOperational();
            pr.level = 12;
            pr.skillPoints = 30;   // QA: a budget to spend; the check is that respec neither creates nor loses any
            int total = pr.skillPoints + pr.SkillPointsSpent;
            foreach (var s in new[] { "pockets_1", "pockets_2", "tool_belt", "quick_hands", "robot_coordination", "destination_orders" }) g.UnlockSkill(s);
            foreach (var u in new[] { "retro_capacity", "retro_capacity", "retro_speed" }) g.BuyRobotUpgrade(u);
            Check("previews: the next rank shows now → after", g.hud.UpgradePreview(pr, retro, pr.Upgrade("retro_speed"), 2).Contains("→")
                  && g.hud.UpgradePreview(pr, retro, pr.Upgrade("retro_persistence"), 1).Contains("m"), g.hud.UpgradePreview(pr, retro, pr.Upgrade("retro_speed"), 2));
            Check("previews: a capped stat says so", g.hud.UpgradePreview(pr, retro, pr.Upgrade("retro_reach"), 3).Contains("→"), g.hud.UpgradePreview(pr, retro, pr.Upgrade("retro_reach"), 3));
            // a full inventory (12 with two pocket levels) and a robot carrying a full load of 3
            var loose = g.world.instances.Where(i => i.state == ItemState.InWorld && g.db.CanRobotCarry(i.defId) && !g.world.DefOf(i.id).IsRobotPart).Select(i => i.id).Take(18).ToList();
            foreach (var id in loose.Take(g.inventory.capacity)) Take(id);
            int held = g.inventory.Count;
            var load = loose.Skip(held).Take(3).ToList();
            foreach (var id in load) { g.index.Remove(id); g.world.SetState(id, ItemState.Carried, retro.Owner); retro.carried.Add(id); }
            int mods = g.world.instances.Count(i => i.state == ItemState.Installed);
            retro.preferDef = g.world.instances[loose[0]].defId; retro.orderState = "WORKING";   // a lasting order (Destination Orders)
            int spent = pr.SkillPointsSpent;
            int back = g.Respec(true);   // the dev respec (F8): the menu has none (skill revamp)
            Check("respec: every point back, none created", back == spent && pr.SkillPointsSpent == 0 && pr.skillPoints == total, $"spent {spent}, back {back}, now {pr.skillPoints} of {total}");
            Check("respec: skills and upgrades are gone, their effects too", pr.unlocked.Count == 0 && pr.RanksBought == 0 && g.inventory.capacity == 10 && retro.Capacity == 1,
                  $"{pr.unlocked.Count} skills, {pr.RanksBought} ranks, inventory {g.inventory.capacity}, Retrobot capacity {retro.Capacity}");
            Check("respec: nothing carried is lost (over capacity only takes no more)", g.inventory.Count == held && held > 10 && g.inventory.IsFull
                  && load.All(id => g.world.instances[id].state == ItemState.Carried && g.world.instances[id].owner == retro.Owner) && retro.carried.Count == 3,
                  $"inventory {g.inventory.Count}/{g.inventory.capacity} (had {held}), Retrobot carries {retro.carried.Count}/{retro.Capacity}");
            Check("respec: robot modules stay installed; a lasting order without Destination Orders ends", g.world.instances.Count(i => i.state == ItemState.Installed) == mods && retro.orderState == "",
                  $"installed {mods} -> {g.world.instances.Count(i => i.state == ItemState.Installed)}, order '{retro.orderState}'");
            Check("respec: levels are bought in order again (Deeper Pockets II needs I)", pr.CanUnlock("pockets_2") != "" && pr.CanUnlock("pockets_1") == "", pr.CanUnlock("pockets_2"));
            Check("respec: no free respec in the menu at any level (only the dev respec)", g.Respec() == 0 && pr.respecLevel == int.MaxValue, $"respec level {pr.respecLevel}");
            // telemetry: real counts, saved with the robot
            retro.stats.Picked(load[0], 10); retro.stats.Dropped(load[0], g.world.instances[load[0]].defId, 22, false); retro.stats.Tick(30, "WORKING"); retro.stats.Tick(10, "STAGING FULL");
            g.SaveGame("autotest_respec");
            g.LoadGame("autotest_respec");
            yield return Frames(5);
            var r2 = g.robots.First(r => r.id == "retrobot");
            Check("telemetry: a robot's record survives a save", r2.stats.stored == 1 && System.Math.Abs(r2.stats.AvgDeliveryS - 12) < 0.01 && System.Math.Abs(r2.stats.Share(r2.stats.blockedS) - 0.25) < 0.01,
                  $"stored {r2.stats.stored}, {r2.stats.AvgDeliveryS:0.0} s per part, blocked {r2.stats.Share(r2.stats.blockedS) * 100:0}%");
            File.Delete(SaveSystem.PathOf("autotest_respec"));
        }

        void ArmPoolChecks()
        {
            var wb = g.workbench;
            var (a1, a2) = (wb.P("sorting_arm"), wb.P("sorting_arm_2"));
            g.progression.level = 25;
            var kit1 = wb.PartIds(a1);
            var kit2 = wb.PartIds(a2);
            foreach (var id in kit2) { Take(id); g.interaction.DepositStation(id); }
            wb.InstallAll(a1, false);
            Check("Sorting Arm #2's kit builds Sorting Arm #1, the arm on the mat", wb.Complete(a1) && wb.InstalledIds(a1).All(kit2.Contains), wb.Summary(a1));
            Take(kit1[0]); g.interaction.DepositStation(kit1[0]);
            Check("Sorting Arm #1's own part then waits in the parts pool", g.world.instances[kit1[0]].state == ItemState.Stored && wb.CanInstall(a1, kit1[0]) != "", wb.CanInstall(a1, kit1[0]));
            foreach (var id in kit1.Skip(1)) { Take(id); g.interaction.DepositStation(id); }
            wb.RobotOf(a1).SetOperational();
            wb.InstallAll(a2, false);
            Check("Sorting Arm #1's kit then builds Sorting Arm #2", wb.Unlocked(a2) && wb.Complete(a2) && wb.InstalledIds(a2).All(kit1.Contains), wb.Summary(a2));
            Check("robot parts are unique, each in one place", PartsIntact(out var detail), detail);
        }

        /// <summary>Caterpillarbot alone in the room: every delivery run carries parts for one destination only,
        /// and it leaves with a part short of a full load only when no other part for that place was left that
        /// it could take.</summary>
        IEnumerator CaterChecks()
        {
            var wb = g.workbench;
            var pj = wb.P("caterpillarbot");
            foreach (var id in wb.PartIds(pj)) { Take(id); g.interaction.DepositStation(id); }
            wb.InstallAll(pj);
            wb.Activate(pj);
            var cat = wb.RobotOf(pj);
            float t0 = Time.realtimeSinceStartup;
            while (!cat.Active && Time.realtimeSinceStartup - t0 < 15) yield return null;
            g.player.enabled = false;
            var runs = new List<(int n, int boxes, int left)>();
            var load = new List<int>();
            bool unloading = false;
            int leftAtDeparture = 0;
            Time.timeScale = 4f;
            float tg = 0;
            while (tg < 420f && runs.Count < 5)
            {
                tg += Time.deltaTime;
                if (!unloading && cat.carried.Count > load.Count)
                {
                    load = cat.carried.ToList();
                    // other parts for the same area it could still take, while it is loading
                    var box = g.world.DefOf(load[0]).storageBox;
                    leftAtDeparture = g.world.instances.Count(i => i.state == ItemState.InWorld && cat.SameArea(box, g.db.Get(i.defId).storageBox) && cat.CanPick(i.id) && !g.Covered(i.id));
                }
                else if (!unloading && load.Count > 0 && cat.carried.Count < load.Count)
                {
                    unloading = true;
                    var first = g.world.DefOf(load[0]).storageBox;
                    runs.Add((load.Count, load.Count(id => !cat.SameArea(first, g.world.DefOf(id).storageBox)), leftAtDeparture));
                    Debug.Log($"[catertest] run {runs.Count}: {load.Count} part(s) for {string.Join(", ", load.Select(id => g.world.DefOf(id).storageBox).Distinct())}; {leftAtDeparture} more for that area were left");
                }
                else if (unloading && cat.carried.Count == 0) { unloading = false; load.Clear(); }
                yield return null;
            }
            Time.timeScale = 1f;
            int cap = cat.Capacity;
            Check("Caterpillarbot: every delivery run carries parts for one area only (the same storage unit, or destinations side by side)", runs.Count >= 3 && runs.All(r => r.boxes == 0),
                  string.Join("; ", runs.Select(r => $"{r.n} part(s), {r.boxes} outside the area")));
            Check("Caterpillarbot: it fills all its boxes before it drives off (playtest 2026-10-28: \"still only taking one item at the time\"; short only when nothing more for that area was left)",
                  runs.Count >= 3 && runs.All(r => r.n >= cap || r.left == 0) && runs.Count(r => r.n >= cap) * 2 > runs.Count,
                  string.Join("; ", runs.Select(r => $"{r.n}/{cap} on board, {r.left} left for that area")));
        }

        IEnumerator TrayChecks()
        {
            var wb = g.workbench;
            var bots = new List<Robot>();
            foreach (var rid in new[] { "retrobot", "unibot" })
            {
                var pj = wb.P(rid);
                foreach (var id in wb.PartIds(pj)) { Take(id); g.interaction.DepositStation(id); }
                wb.InstallAll(pj);
                wb.Activate(pj);
                bots.Add(wb.RobotOf(pj));
            }
            float t0 = Time.realtimeSinceStartup;
            while (bots.Any(b => !b.Active) && Time.realtimeSinceStartup - t0 < 15) yield return null;
            var r = g.TrayRectU;
            int maxCarry = 0;
            var loose = g.world.instances.Where(i => i.state == ItemState.InWorld && !g.InTray(i.Position) && g.db.CanRobotCarry(i.defId)
                                                      && bots.Any(b => b.CanPick(i.id))).Select(i => i.id).Take(8).ToList();
            foreach (var id in loose) { Take(id); g.interaction.DropInTray(id, false); }
            yield return new WaitForSeconds(1f);
            var inTray = loose.Where(id => g.world.instances[id].state == ItemState.InWorld && g.InTray(g.world.instances[id].Position)).ToList();
            int back = inTray.Count(id => g.world.instances[id].Position.z > r.center.y + 0.15f || g.world.instances[id].Position.z < r.center.y - 0.15f);
            Time.timeScale = 4f;
            float tg = 0;
            while (tg < 480f && inTray.Any(id => g.world.instances[id].state == ItemState.InWorld && g.InTray(g.world.instances[id].Position)))
            {
                tg += Time.deltaTime;
                maxCarry = Mathf.Max(maxCarry, bots.Sum(b => b.carried.Count));
                if (Mathf.FloorToInt(tg / 20f) != Mathf.FloorToInt((tg - Time.deltaTime) / 20f))
                    Debug.Log($"[traytest] {tg:0} s: {inTray.Count(id => g.world.instances[id].state == ItemState.InWorld && g.InTray(g.world.instances[id].Position))} left; " +
                              string.Join(" | ", bots.Select(b => b.Describe())));
                yield return null;
            }
            Time.timeScale = 1f;
            var left = inTray.Where(id => g.world.instances[id].state == ItemState.InWorld && g.InTray(g.world.instances[id].Position)).ToList();
            Check("walking robots take every part they can carry out of the sorting tray, the back row too", inTray.Count >= 6 && left.Count == 0,
                  $"{inTray.Count} parts in the tray ({back} off its middle line), {left.Count} left after {tg:0} s game time" +
                  (left.Count > 0 ? ": " + string.Join(", ", left.Select(id => $"{g.world.DefOf(id).displayName} at z {g.world.instances[id].Position.z - r.center.y:+0.00;-0.00} ({string.Join("/", bots.Select(b => b.PickHint(id)))})")) : ""));
            // the settings screen: values and on/off states must be readable at a glance (same playtest)
            g.SetState("paused");
            g.hud.OpenSub("settings");
            yield return g.Capture(Path.Combine(outDir, "tray_settings.png"), 6);
            // the skills screen: a skill with levels is one row; every level-up pays exactly one point (skill revamp)
            int pts0 = g.progression.skillPoints, lv0 = g.progression.level;
            LevelTo(12);
            int gained = g.progression.skillPoints - pts0;
            Check("every level-up pays exactly one skill point", gained == 12 - lv0, $"levels {lv0}-12 paid {gained} points");
            foreach (var id in new[] { "magnet_1", "magnet_2", "magnet_3", "part_sense", "part_sense_2" }) g.UnlockSkill(id);
            g.hud.OpenSub("skills");
            yield return g.Capture(Path.Combine(outDir, "tray_skills.png"), 6);
            g.SetState("play");
        }

        /// <summary>A load, and a load converted to a changed catalog, put every part back exactly
        /// where it was: nothing moves, and a stray taken off a shelf never comes back there.</summary>
        IEnumerator LoadKeepChecks()
        {
            g.player.enabled = false;
            yield return Frames(10);
            // a shelf stray taken down to the floor, and another copy of a kind stored while a copy of it still lies on a shelf
            var strays = g.world.instances.Where(i => i.state == ItemState.InWorld && i.Position.y > 0.5f && i.Position.y < Game.MaxDropDeck && !g.InTray(i.Position)
                && !g.world.DefOf(i.id).IsRobotPart).OrderBy(i => i.id).ToList();
            var down = strays.First();
            var floor = g.SafeDropPoint(g.player.transform.position + g.player.transform.forward, g.player.transform.position);
            g.world.SetTransform(down.id, Matrix4x4.TRS(floor + Vector3.up * 0.05f, Quaternion.identity, Vector3.one * g.world.DefOf(down.id).worldScale));
            g.index.Insert(down.id);
            var shelfCopy = strays.Skip(1).First(i => g.world.IdsOf(i.defId).Any(o => o != i.id && g.world.instances[o].state == ItemState.InWorld && g.world.instances[o].Position.y < 0.3f));
            var stored = g.world.IdsOf(shelfCopy.defId).First(o => o != shelfCopy.id && g.world.instances[o].state == ItemState.InWorld && g.world.instances[o].Position.y < 0.3f);
            Take(stored); g.inventory.Remove(stored);
            bool depOk = g.storage.Deposit(g.world.DefOf(stored).storageBox, stored) == StorageSystem.Result.Ok;
            yield return Frames(5);
            Dictionary<int, (ItemState s, Vector3 p)> Snap() => g.world.instances.ToDictionary(i => i.id, i => (i.state, i.Position));
            string Diff(Dictionary<int, (ItemState s, Vector3 p)> a)
            {
                var now = Snap();
                var bad = a.Where(kv => now[kv.Key].s != kv.Value.s || (kv.Value.s == ItemState.InWorld && (now[kv.Key].p - kv.Value.p).magnitude > 0.0005f)).ToList();
                foreach (var kv in bad.Take(5)) Debug.Log($"[loadkeep] {g.world.DefOf(kv.Key).displayName} #{kv.Key}: {kv.Value.s} {kv.Value.p} -> {now[kv.Key].s} {now[kv.Key].p}");
                return bad.Count == 0 ? "" : $"{bad.Count} part(s) changed";
            }
            g.piles.Finish();   // SaveGame lands parts still falling from the new world first; compare with what it saves
            var before = Snap();
            g.SaveGame("autotest_keep");
            g.StartWorld(g.world.seed, null);
            g.LoadGame("autotest_keep");
            g.player.enabled = false;
            yield return Frames(30);
            string d1 = Diff(before);
            Check("a load puts every part back exactly where it was (nothing settles, nothing moves)", depOk && d1 == "", d1 == "" ? $"{before.Count} parts identical" : d1);
            // the same save as if made with an older catalog: it is converted, and still nothing moves
            var raw = SaveSystem.Read("autotest_keep");
            raw["catalog_hash"] = "0";
            SaveSystem.Write("autotest_keep", raw);
            g.StartWorld(g.world.seed, null);
            g.LoadGame("autotest_keep");
            g.player.enabled = false;
            yield return Frames(30);
            string d2 = Diff(before);
            Check("a save converted to a changed catalog keeps every part's identity and place: the stray taken off the shelf stays on the floor, the stored copy stays stored",
                  g.migrationReport != "" && d2 == "" && g.world.instances[down.id].Position.y < 0.3f && g.world.instances[stored].state == ItemState.Stored,
                  $"{g.migrationReport}; " + (d2 == "" ? $"{before.Count} parts identical" : d2));
            File.Delete(SaveSystem.PathOf("autotest_keep"));
        }

        /// <summary>Loads a save slot (read only) and checks that its loose parts rest on something: a save
        /// converted to a new catalog, or written right after one, must not leave parts in mid-air.</summary>
        /// <summary>A save is loaded and the player stands still for 10 s: frame times and how far the eye moves
        /// from frame to frame (playtest 2026-10-26: "the camera is shaking").</summary>
        IEnumerator CamShake(string slot)
        {
            if (slot != "new") g.LoadGame(slot);
            g.player.enabled = true;   // the controller runs (no input): gravity, standing on parts, head bob
            yield return Frames(120);
            var head = g.player.head;
            var dts = new List<float>(); var dy = new List<float>(); var dxz = new List<float>(); var rot = new List<float>();
            Vector3 lastP = head.position; Quaternion lastR = head.rotation;
            for (float t = 0; t < 10; t += Time.unscaledDeltaTime)
            {
                yield return null;
                dts.Add(Time.unscaledDeltaTime * 1000);
                var p = head.position;
                dy.Add(Mathf.Abs(p.y - lastP.y) * 1000); dxz.Add(new Vector2(p.x - lastP.x, p.z - lastP.z).magnitude * 1000);
                rot.Add(Quaternion.Angle(lastR, head.rotation));
                lastP = p; lastR = head.rotation;
            }
            float P(List<float> l, float q) { var o = l.OrderBy(x => x).ToList(); return o[Mathf.Min(o.Count - 1, (int)(q * o.Count))]; }
            Debug.Log($"[camshake] {dts.Count} frames: frame ms p50 {P(dts, .5f):0.0} p95 {P(dts, .95f):0.0} max {dts.Max():0.0}, over 25 ms {dts.Count(x => x > 25)}; eye mm/frame vertical p50 {P(dy, .5f):0.00} p95 {P(dy, .95f):0.00} max {dy.Max():0.00}, horizontal max {dxz.Max():0.00}; turn deg max {rot.Max():0.000}; robots working {g.robots.Count(r => r.Operational)}");
            Check("standing still: the eye moves under 1 mm per frame", P(dy, .95f) < 1 && dxz.Max() < 1, $"vertical p95 {P(dy, .95f):0.00} mm, horizontal max {dxz.Max():0.00} mm");
            Check("standing still: frames under 25 ms (95 %)", P(dts, .95f) < 25, $"p95 {P(dts, .95f):0.0} ms, max {dts.Max():0.0} ms");
            // standing ON loose parts, with uneven frames (every 4th frame 40-90 ms late): the eye stays put
            var spot = g.world.instances.Where(i => i.state == ItemState.InWorld && i.Position.y < 0.3f)
                .Select(i => i.Position).FirstOrDefault(p => g.debris.Under(p, 0.16f) is float h && h > 0.04f && h < 0.2f && g.debris.At(p) > 0.04f);
            g.player.Teleport(new Vector3(spot.x, g.debris.Under(spot, 0.16f), spot.z));
            yield return Frames(90);
            float lo = float.MaxValue, hi = float.MinValue, step = 0;
            var rng = new System.Random(3);
            float lastY = head.position.y;
            for (int f = 0; f < 240; f++)
            {
                if (f % 4 == 0) System.Threading.Thread.Sleep(40 + rng.Next(50));
                yield return null;
                float y = head.position.y;
                lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); step = Mathf.Max(step, Mathf.Abs(y - lastY)); lastY = y;
            }
            Debug.Log($"[camshake] on parts at {spot} (debris {g.debris.Under(spot, 0.16f) * 100:0.0} cm): eye range {(hi - lo) * 1000:0.0} mm, largest frame step {step * 1000:0.0} mm");
            Check("standing on loose parts with uneven frames: the eye holds still (under 3 mm)", hi - lo < 0.003f, $"eye range {(hi - lo) * 1000:0.0} mm, largest step {step * 1000:0.0} mm");
        }

        /// <summary>QA (playtest 2026-10-28): loads a player's save and, at every mat of the bench, reports what the
        /// part picker offers (and why not), with a screenshot of each.</summary>
        IEnumerator PickSlot(string slot)
        {
            g.LoadGame(slot);
            g.player.enabled = false;
            yield return Frames(30);
            var wb = g.workbench;
            var ic = g.interaction;
            var pool = wb.PoolContents();
            Debug.Log($"[pickslot] pool {pool.Count}: {string.Join(", ", pool.Select(i => g.world.DefOf(i).displayName))}; hands: {string.Join(", ", g.inventory.items.Select(i => g.world.DefOf(i).displayName))}");
            foreach (int mat in wb.projects.Select(p => p.mat).Distinct().OrderBy(m => m))
            {
                var p = wb.ActiveOnMat(mat);
                var (spot, _) = wb.MatSpot(mat);
                var stand = spot + wb.Front * 0.75f;
                LookFrom(new Vector3(stand.x, PlayerController.Eye, stand.z), spot + Vector3.up * 0.05f);
                yield return Frames(2);
                ic.UpdateTarget();
                var info = ic.Describe();
                var why = pool.Where(id => g.world.DefOf(id).craftingPurpose?.StartsWith("robot:") == true)
                    .Select(id => $"{g.world.DefOf(id).displayName}: {(wb.CanFit(p, id, out _, false) is string w && w != "" ? w : "fits")}").Take(12);
                Debug.Log($"[pickslot] mat {mat} {p.name}: aim {ic.targetType}, unlocked {wb.Unlocked(p)} ({wb.LockReason(p)}), operational {wb.Operational(p)}, picker {ic.PickerActive} ({ic.fitChoices.Count}); hud '{info.title}' / '{info.sub}' / '{info.hint}'; {string.Join(" | ", why)}");
                yield return g.Capture(Path.Combine(outDir, $"pickslot_{p.id}.png"), 2);
            }
            Check("picker report written (see [pickslot] in the log)", true, slot);
        }

        /// <summary>QA (playtest 2026-10-28): the bench screen on a player's save: the blueprints (a robot, the
        /// Part Radar), then a robot's parts page, stepping a place to another found part (▶).</summary>
        IEnumerator BenchShots(string slot)
        {
            g.LoadGame(slot);
            g.player.enabled = false;
            yield return Frames(30);
            var wb = g.workbench;
            var p = wb.projects.Where(q => !wb.Operational(q) && wb.Unlocked(q) && !q.crafted)
                .OrderByDescending(q => wb.Slots(q).Count(x => x.ring.Count > (x.on >= 0 ? 1 : 0))).First();
            // the parts box on the bench, from where the player stands
            var pp = wb.PoolPoint;
            LookFrom(pp + wb.Front * 0.55f + Vector3.up * 0.6f, pp);
            yield return Frames(4);
            yield return g.Capture(Path.Combine(outDir, "bench_0_box.png"), 3);
            g.OpenWorkbench(p.id);
            yield return Frames(6);
            bool list = !g.hud.WorkbenchOnParts;
            yield return g.Capture(Path.Combine(outDir, "bench_1_blueprints.png"), 3);
            g.hud.WorkbenchChoose(p.id);
            yield return Frames(6);
            yield return g.Capture(Path.Combine(outDir, "bench_2_parts.png"), 3);
            var slots = wb.Slots(p);
            var step = slots.FirstOrDefault(x => x.ring.Count > (x.on >= 0 ? 1 : 0));
            string before = step.on >= 0 ? g.world.DefOf(step.on).displayName : "-", after = "-";
            bool changed = false;
            if (step.ring != null)
            {
                int n = step.ring.Count, idx = step.ring.IndexOf(step.on);
                int target = step.on >= 0 ? step.ring[(idx + 1) % n] : step.ring[0];
                var (ok, text) = wb.Fit(p, target, true);
                yield return Frames(6);
                var now = wb.Slots(p).First(x => x.key == step.key);
                after = now.on >= 0 ? g.world.DefOf(now.on).displayName : "-";
                changed = ok && now.on == target && (step.on < 0 || g.world.instances[step.on].state == ItemState.Stored);
                Debug.Log($"[benchshots] {p.name} {step.key}: {before} -> {after}  ({text})");
                yield return g.Capture(Path.Combine(outDir, "bench_3_stepped.png"), 3);
            }
            Debug.Log($"[benchshots] radar: plans {g.radar.HasPlans}, missing {string.Join(", ", g.radar.MissingNames())}  ->  '{g.radar.WhyNotBuilt()}'");
            Debug.Log($"[benchshots] {p.name}: {string.Join(" | ", slots.Select(x => $"{x.key}: on {(x.on >= 0 ? g.world.DefOf(x.on).displayName : "-")}, {x.ring.Count} in ring"))}");
            Check("the bench opens on its blueprints; Build shows the parts; ▶ fits the next found part at once (the old one back in the pool)",
                  PartsIntact(out var d) && list && changed, $"{p.name}, {step.key}: {before} → {after}; {d}");
            g.SetState("play");
        }

        IEnumerator LoadSlotChecks(string slot)
        {
            g.LoadGame(slot);
            g.player.enabled = false;
            yield return Frames(30);
            // the contact probe also misses some parts at rest (thin ones, deep in a heap): a new world, which
            // is always settled, is the yardstick
            float Unsupported(out int n)
            {
                var loose = g.world.instances.Where(i => i.state == ItemState.InWorld && !g.InTray(i.Position) && i.Position.y < Game.MaxDropDeck + 0.4f).ToList();
                n = loose.Count;
                var bad = loose.Where(i => !g.piles.Contact(i.id).supported).ToList();
                foreach (var i in bad.Take(12))
                    Debug.Log($"[loadslot] unsupported {g.world.DefOf(i.id).displayName} at {i.Position} (moved {i.moved}), bounds min y {g.index.WorldBounds(i.id).min.y:0.000}");
                return n == 0 ? 1 : bad.Count / (float)n;
            }
            float loaded = Unsupported(out int nLoaded);
            yield return g.Capture(Path.Combine(outDir, "loadslot.png"), 3);
            g.StartWorld(g.world.seed, null);
            g.player.enabled = false;
            yield return Frames(30);
            float fresh = Unsupported(out int nFresh);
            Check($"the loose parts of save '{slot}' rest on something after loading, as in a new world", nLoaded > 0 && loaded <= fresh * 1.5f + 0.02f,
                  $"unsupported by the probe: loaded {loaded:P1} of {nLoaded}, new world {fresh:P1} of {nFresh}");
        }

        IEnumerator DesignChecks()
        {
            var pr = g.progression;
            var p = g.progress;
            bool standard = g.presetId == "standard";
            // a few destinations that are simple to fill: robot-carriable, 3+ copies, none on a robot
            var defs = g.db.order.Where(d => g.world.TotalOf(d) >= 3 && !g.db.Get(d).IsRobotPart && g.db.Get(d).robotCarry && g.storage.Stored(g.db.Get(d).storageBox).Count == 0).Take(60).ToList();
            int levelUps = 0;
            pr.LeveledUp += _ => levelUps++;

            // P-01: many correctly stored parts across unfinished destinations give no level
            int stored = 0;
            foreach (var d in defs.Skip(4))
                foreach (var i in g.world.IdsOf(d).Take(g.world.TotalOf(d) - 1).ToList())
                {
                    Take(i); g.inventory.Remove(i);
                    if (g.storage.Deposit(g.db.Get(d).storageBox, i) == StorageSystem.Result.Ok) stored++;
                }
            Check("P-01: correctly stored parts in unfinished destinations give no level", stored > 100 && pr.level == 1 && levelUps == 0 && pr.DestinationsCompleted == 0 && pr.xp > 0,
                  $"{stored} parts stored over {defs.Count - 4} destinations: level {pr.level}, {pr.DestinationsCompleted} completed, xp {pr.xp}");
            // P-02: the last part of the first destination raises level 1 -> 2
            bool c0 = CompleteDef(defs[0]);
            Check("P-02: completing the first destination raises the level from 1 to 2", c0 && pr.level == 2 && levelUps == 1, $"{g.db.Get(defs[0]).displayName}: level {pr.level}");
            // P-03: a robot's last part completes exactly like the player's
            var d1 = defs[1];
            foreach (var i in g.world.IdsOf(d1).ToList())
            {
                Take(i); g.inventory.Remove(i);
                g.storage.Deposit(g.db.Get(d1).storageBox, i, "robot:retrobot");
            }
            Check("P-03: a robot completes a destination exactly like the player", p.IsComplete(d1) && pr.completedDestinations.Contains(d1) && pr.DestinationsCompleted == 2, $"{pr.DestinationsCompleted} completed");
            // P-05: taking a copy out and storing it again pays nothing twice
            {
                int ups = levelUps, done = pr.DestinationsCompleted;
                long xp = pr.xp;
                var i = g.world.IdsOf(defs[0])[0];
                string box = g.db.Get(defs[0]).storageBox;
                g.storage.Remove(box, i); g.world.SetState(i, ItemState.InWorld, ""); g.index.Insert(i);
                bool still = p.IsComplete(defs[0]);
                Take(i); g.inventory.Remove(i); g.storage.Deposit(box, i);
                Check("P-05: a destination is rewarded once, even after a part is taken out and stored again", still && levelUps == ups && pr.DestinationsCompleted == done && pr.xp == xp,
                      $"completed {done} -> {pr.DestinationsCompleted}, xp {xp} -> {pr.xp}");
            }
            // P-04: picking up, staging and score never give a level; the level always follows completions
            {
                int lv = pr.level;
                var loose = g.world.instances.Where(i => i.state == ItemState.InWorld && !g.world.DefOf(i.id).IsRobotPart).Take(5).Select(i => i.id).ToList();
                foreach (var i in loose) Take(i);
                for (int k = 0; k < 30; k++) pr.OnDiscovered(-900000 - k, false);   // score only
                foreach (var i in loose) { g.inventory.Remove(i); g.world.SetState(i, ItemState.InWorld, "stage:" + g.world.DefOf(i).storageBox); g.index.Insert(i); }
                Check("P-04: pickup, staging and score never raise the level", pr.level == lv && pr.level == pr.LevelFor(pr.DestinationsCompleted), $"level {lv} -> {pr.level}, xp {pr.xp}");
            }
            // P-06: only destinations that really hold parts count towards the curve
            Check("P-06: the level curve counts only assigned destinations", pr.destinationTotal == g.CompletableDestinations() && pr.destinationTotal == g.db.order.Count(d => g.world.TotalOf(d) > 0 && !g.db.Get(d).IsRobotPart),
                  $"{pr.destinationTotal} on the curve, {g.CompletableDestinations()} completable");
            // P-08: level 25 at 414 of 500 on Standard
            if (standard)
                Check("P-08: the max level needs ~85 % of the completed destinations (preliminary curve)", System.Math.Abs(pr.DestinationsForLevel(pr.maxLevel) - 0.85 * pr.destinationTotal) <= 0.02 * pr.destinationTotal,
                      $"max level {pr.maxLevel} at {pr.DestinationsForLevel(pr.maxLevel)} of {pr.destinationTotal}");
            // R-01/R-02: robot intelligence is bought, never given by the player's level
            var retro = g.robots.First(r => r.type == "retrobot");
            // a started destination whose last copy lies within Retrobot's reach, stored by Retrobot itself
            var started = defs.Skip(4).First(d => !p.IsComplete(d) && retro.ReachableDest(g.db.Get(d).storageBox)
                && g.world.IdsOf(d).Where(i => !p.accepted.Contains(i)).All(i => g.world.instances[i].state == ItemState.InWorld && g.world.instances[i].Position.y <= retro.ReachHeight
                    && ItemDatabase.Extents(g.world.DefOf(i))[0] <= retro.MaxItemSize));
            LevelTo(20);
            retro.ApplyUpgrades();
            Check("R-01/R-02: a robot without AI upgrades has no set bonus, even at level 20", retro.FocusBonus(started) == 0, $"level {pr.level}, bonus {retro.FocusBonus(started):0.00} m");
            // R-04: Set Awareness pulls towards a nearly complete, reachable destination
            pr.upgradeRanks["retro_set"] = 1;
            retro.ApplyUpgrades();
            yield return null;
            float bonus = retro.FocusBonus(started);
            Check("R-04: Set Awareness gives a nearly complete, reachable destination a pull", bonus > 1f, $"{g.db.Get(started).displayName} {p.AcceptedOf(started)}/{g.world.TotalOf(started)}: {bonus:0.00} m");
            // R-05: ...but none to a destination whose last copy only the player can carry
            var pos = g.db.order.Where(d => g.world.TotalOf(d) >= 2 && !g.db.Get(d).IsRobotPart && !g.db.Get(d).robotCarry && !p.IsComplete(d)).Take(1).ToList();
            if (pos.Count > 0)
            {
                int po = pos[0];
                var first = g.world.IdsOf(po)[0];
                Take(first); g.inventory.Remove(first); g.storage.Deposit(g.db.Get(po).storageBox, first);
                yield return null;
                Check("R-05: a destination robots can't finish draws no set bonus", retro.FocusBonus(po) == 0, $"{g.db.Get(po).displayName} {p.AcceptedOf(po)}/{g.world.TotalOf(po)}: {retro.FocusBonus(po):0.00} m");
            }
            pr.upgradeRanks.Remove("retro_set");
            retro.ApplyUpgrades();

            // S-01: the staging hard cap and the Courier Hand-off soft limit
            Check("S-01: a staging zone holds at most 14 parts; coordinated couriers stop at 10", Robot.StagingCapacity == 14 && Robot.StagingSoftLimit == 10, $"hard {Robot.StagingCapacity}, soft {Robot.StagingSoftLimit}");

            // E-03: save and load keep the ledger, the level and every part
            {
                var acc = p.accepted.ToList();
                int lv = pr.level, done = pr.DestinationsCompleted, ups = levelUps;
                var states = g.world.instances.Select(i => (int)i.state).ToList();
                g.SaveGame("autotest_design");
                g.StartWorld(g.world.seed, null);
                g.LoadGame("autotest_design");
                yield return Frames(5);
                pr = g.progression; p = g.progress;
                bool same = p.accepted.SetEquals(acc) && pr.level == lv && pr.DestinationsCompleted == done && g.world.instances.Select(i => (int)i.state).SequenceEqual(states);
                Check("E-03: save/load keeps accepted parts, completions and the level, with no part lost or doubled", same && g.world.Count == states.Count,
                      $"accepted {acc.Count} -> {p.accepted.Count}, level {lv} -> {pr.level}, completed {done} -> {pr.DestinationsCompleted}");
                File.Delete(SaveSystem.PathOf("autotest_design"));
            }
            // E-04: a respec returns exactly the robot points spent
            {
                pr.skillPoints += 10;
                int before = pr.skillPoints;
                pr.upgradeRanks["retro_persistence"] = 2;   // 1 + 1 points
                pr.skillPoints -= 2;
                int back = pr.RefundRobotUpgrades(true);   // the dev refund: the normal one is closed (skill revamp)
                Check("E-04: a robot respec returns exactly the robot points invested", back == 2 && pr.skillPoints == before && pr.RanksBought == 0, $"{back} back, {pr.skillPoints} (was {before})");
            }
            // E-06: the inventory audit accounts for every remaining part, wherever it is
            {
                var left = InventoryAudit.Remaining(g);
                int expect = g.world.instances.Count(i => !g.world.DefOf(i.id).IsRobotPart && i.state != ItemState.Installed && !p.accepted.Contains(i.id));
                Check("E-06: the inventory audit finds every remaining part and says where it is", left.Count == expect && left.All(i => InventoryAudit.Where(g, i, true) != ""),
                      $"{left.Count} remaining, {expect} expected");
            }
            // E-07: Large and Massive have their own destinations
            Check("E-07: this preset's destinations are its own (one per definition with parts)", g.CompletableDestinations() > 0 && g.CompletableDestinations() == g.db.order.Count(d => g.world.TotalOf(d) > 0 && !g.db.Get(d).IsRobotPart),
                  $"{g.world.Count} parts, {g.CompletableDestinations()} destinations");
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
            // playtest 2026-10-26: every robot's blueprint is open from level 1 (they are hard enough to
            // assemble); the lock itself is still exercised here with test levels 2 and 5
            Check("every robot blueprint unlocks at level 1 (unlocks.json)", wb.projects.All(p => p.level == Unlocks.Robot(p.id, -1) && p.level == 1),
                  string.Join(", ", wb.projects.Select(p => $"{p.name} lv {p.level}")));
            retro.level = 2; uniP.level = 5;
            var parts = wb.PartIds(retro);
            Check("Retrobot has 5 assembly parts", parts.Count == 5, $"{parts.Count} parts");
            Check("Retrobot inactive before assembly", !rb.Active && !rb.Operational, rb.Describe());
            // the parts' discovery XP is earned first, then the player goes back to level 1
            foreach (var id in parts.Concat(wb.PartIds(uniP))) g.progress.Discover(g.world.instances[id].defId);
            pr.level = 1; pr.xp = 0; pr.completedDestinations.Clear();
            wb.CheckBlueprints(false);
            Check("a blueprint above the player's level stays locked", !wb.Unlocked(retro) && !wb.Unlocked(uniP), wb.LockReason(retro));

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
            Check("parts delivered before the blueprint wait in the parts pool", wb.DeliveredIds(retro).Count == 3 && wb.InstalledCount(retro) == 0 && wb.StatusOf(retro) == RobotWorkbench.Status.AwaitingBlueprint,
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
            Check("partial assembly and the parts pool survive save/load", wbBefore == wbAfter && PartsIntact(out _), $"{wbBefore}  vs  {wbAfter}");
            File.Delete(SaveSystem.PathOf("autotest_wb"));
            // 13. a part can't be delivered twice
            var again = g.interaction.DepositStation(parts[0]);
            bool intact = PartsIntact(out var d2);
            Check("an installed part can't be delivered again (no duplicates)", !again && intact, d2);

            // the last two parts, found in the workshop: they wait in the parts pool (the player picks what goes
            // on which robot); once every part is there the autoplay builds it, as the player would
            foreach (var id in missing) { Take(id); g.interaction.DepositStation(id); }
            bool waitedInPool = !wb.Complete(retro) && missing.All(id => wb.PoolContents().Contains(id));
            int built = AutoAssemble();
            yield return Frames(2);
            Check("delivered parts wait in the parts pool; with all of them found the robot is built from the pool, ready for activation",
                  waitedInPool && built >= 1 && wb.Complete(retro) && wb.StatusOf(retro) == RobotWorkbench.Status.ReadyForActivation, $"waited in the pool {waitedInPool}; {wb.Summary(retro)}");
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
            int maxBp = wb.projects.Where(p => p.after == null).Max(p => p.level), maxCraft = wb.projects.Max(p => p.level);
            int dests = g.CompletableDestinations();
            Check("every blueprint level is reachable by completing destinations (robots by 75% of them, crafted Hoverbots and the max level by about 85%)",
                  pr.DestinationsForLevel(maxBp) <= dests * 0.75 && pr.DestinationsForLevel(maxCraft) <= dests * 0.85 && pr.DestinationsForLevel(pr.maxLevel) <= dests * 0.87,
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
            foreach (var p in wb.projects) p.level = Unlocks.Robot(p.id, p.level);   // back to unlocks.json
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
            int pockets0 = g.inventory.items.Count;
            if (!wb.Activate(p)) wb.DebugUnlock(p);   // the arm-only section starts below the blueprint level
            float t0 = Time.realtimeSinceStartup;
            while (!bot.Operational && Time.realtimeSinceStartup - t0 < 15) yield return null;
            Check("a built arm comes straight into the player's empty hands (with parts in them it waits at the bench)", bot.Operational
                  && (pockets0 == 0 ? g.CarriedArm == arm : arm.place == SortingArm.Place.Floor && (bot.transform.position - bot.home).magnitude < 0.05f), $"{pockets0} part(s) in the pockets; " + arm.Describe());
            if (!bot.Operational) { foreach (var r in paused) r.paused = false; yield break; }

            // set down anywhere (the same click as mounting, off a mount), picked up again only with empty hands
            if (arm.place != SortingArm.Place.Carried) arm.PickUp();
            g.interaction.SetArmDown(arm, false);
            var me = g.player.transform.position;
            var down = arm.transform.position;
            float off = Vector3.ProjectOnPlane(down - me, Vector3.up).magnitude;
            Check("the carried arm is set down folded on the floor in front of the player, not at the bench", arm.place == SortingArm.Place.Floor && g.CarriedArm == null && Mathf.Abs(down.y) < 0.01f && off > 0.5f && off < 1.2f
                  && g.sockets.Values.All(s => s.occupant != arm), $"{off:0.00} m from the player at {down}; " + arm.Describe());
            LookFrom(down + Vector3.ProjectOnPlane(me - down, Vector3.up).normalized * 1.6f + Vector3.up * 1.1f, down + Vector3.up * 0.25f);
            yield return g.Capture(Path.Combine(outDir, "17_arm_set_down.png"), 2);
            var loose = g.world.instances.First(i => i.state == ItemState.InWorld && !g.world.DefOf(i.id).IsRobotPart && !g.InTray(i.Position));
            bool tookPart = g.interaction.Pickup(loose.id, false);
            bool armRefused = !arm.PickUp();
            g.interaction.DropHeld();
            yield return Frames(30);
            Check("with a part in the hands the arm can't be picked up", tookPart && armRefused && arm.place == SortingArm.Place.Floor, $"took part {tookPart}, refused {armRefused}");

            // carry: two-handed, no pocket slot, no pickups meanwhile
            int pockets = g.inventory.items.Count;
            arm.PickUp();
            loose = g.world.instances.First(i => i.state == ItemState.InWorld && !g.world.DefOf(i.id).IsRobotPart && !g.interaction.busy.Contains(i.id));
            bool refused = !g.interaction.Pickup(loose.id, false);
            Check("the arm is carried in both hands: no inventory slot, no part pickups", pockets == 0 && g.CarriedArm == arm && g.inventory.items.Count == 0 && refused, arm.Describe());

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
            Check("an arm saved while carried is set down at the workbench on load (nothing lost)", bot.arm.place == SortingArm.Place.Floor && (bot.arm.transform.position - bot.home).magnitude < 0.01f
                  && g.sockets.Values.All(s => s.occupant == null) && g.CarriedArm == null && g.world.instances.Count == total, bot.arm.Describe());
            // set down somewhere else: it is still standing there after save/load
            g.player.Teleport(g.player.transform.position + Vector3.ProjectOnPlane(g.player.transform.forward, Vector3.up).normalized * -1.5f);
            yield return Frames(3);
            bot.arm.PickUp();
            g.interaction.SetArmDown(bot.arm, false);
            var spot = bot.arm.transform.position;
            g.SaveGame("autotest_arm");
            g.StartWorld(g.world.seed, null);
            g.LoadGame("autotest_arm");
            yield return Frames(5);
            bot = g.workbench.RobotOf(g.workbench.P("sorting_arm"));
            Check("save/load: an arm set down on the floor comes back where it stood", bot.arm.place == SortingArm.Place.Floor && (bot.arm.transform.position - spot).magnitude < 0.01f
                  && (spot - bot.home).magnitude > 0.2f, $"{bot.arm.transform.position} (set down at {spot}); " + bot.arm.Describe());
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
        /// <summary>Parts dropped in the sorting tray fall like any dropped part (playtest 2026-11-02: "gravity is a
        /// constant in the entire world"): they pile up from the tray's floor, inside the rim, nothing in mid-air.
        /// The robots are paused so the pile stays for the picture.</summary>
        IEnumerator TrayPileChecks()
        {
            for (float t = 0; t < 15 && g.piles.Settling; t += Time.deltaTime) yield return null;
            foreach (var r in g.robots) r.paused = true;
            var tc = g.TrayCenter;
            var parts = g.world.instances.Where(i => i.state == ItemState.InWorld && !g.InTray(i.Position) && !g.world.DefOf(i.id).IsRobotPart && !g.world.DefOf(i.id).IsPegboardTool
                                                     && !g.robotReserved.Contains(i.id) && !g.interaction.busy.Contains(i.id) && i.Position.y < 0.5f)
                .OrderBy(i => (i.Position - tc).sqrMagnitude).Take(30).Select(i => i.id).ToList();
            g.player.Teleport(tc + new Vector3(0, 0, 1.2f));
            yield return Frames(2);
            int put = 0;
            foreach (var id in parts) { Take(id); if (g.interaction.DropInTray(id, false)) put++; yield return new WaitForSeconds(0.08f); }
            yield return new WaitForSeconds(0.4f);
            for (float t = 0; t < 15 && g.piles.Settling; t += Time.deltaTime) yield return null;
            var inTray = parts.Where(id => g.world.instances[id].state == ItemState.InWorld && g.InTray(g.world.instances[id].Position)).ToList();
            var air = inTray.Where(id => !g.piles.Contact(id).supported).Select(id => $"{g.world.DefOf(id).displayName} at {g.world.instances[id].Position}").ToList();
            float low = inTray.Count > 0 ? inTray.Min(id => g.index.WorldBounds(id).min.y) : -1;
            float high = inTray.Count > 0 ? inTray.Max(id => g.index.WorldBounds(id).max.y) : -1;
            Check("parts dropped in the sorting tray stay in it", put == parts.Count && inTray.Count >= put - 1, $"{put} dropped, {inTray.Count} in the tray");
            Check("parts in the sorting tray rest on its floor or on each other (nothing in mid-air)", air.Count == 0,
                  $"in the air: {air.Count}{(air.Count > 0 ? " (" + string.Join("; ", air.Take(3)) + ")" : "")}; lowest bottom {low:0.000} m, highest top {high:0.000} m; {g.piles.lastInfo}");
            Check("the tray's pile starts at its floor", inTray.Count > 0 && low < WorkshopLayout.TrayFloor + 0.02f, $"lowest bottom {low:0.000} m (floor {WorkshopLayout.TrayFloor})");
            LookFrom(tc + new Vector3(0.6f, 1.0f, 1.1f), tc);
            yield return g.Capture(Path.Combine(outDir, "tray_pile.png"), 3);
            foreach (var r in g.robots) r.paused = false;
        }

        /// <summary>Parts put in the robot parts bin fall onto the heap (playtest 2026-11-02: "items placed in the
        /// robot parts bin needs to be affected by gravity ... cannot float in mid air"): one by one into the bin,
        /// then a few taken out again; each time the heap comes to rest on the bin's floor or on each other.</summary>
        IEnumerator BinPileChecks()
        {
            for (float t = 0; t < 15 && g.piles.Settling; t += Time.deltaTime) yield return null;
            foreach (var r in g.robots) r.paused = true;
            var wb = g.workbench;
            var ic = g.interaction;
            var parts = RobotWorkbench.Types.SelectMany(t => wb.PartIds(wb.P(t))).Where(id => g.world.instances[id].state == ItemState.InWorld && !g.robotReserved.Contains(id)).Take(24).ToList();
            int put = 0;
            foreach (var id in parts) { Take(id); if (ic.DepositStation(id)) put++; else g.inventory.Remove(id); yield return new WaitForSeconds(0.12f); }
            yield return Frames(3);
            var falling = wb.BinReport();
            for (float t = 0; t < 10 && wb.BinSettling; t += Time.deltaTime) yield return null;
            var r1 = wb.BinReport();
            Check("parts put in the robot parts bin fall into it", put >= 10 && r1.n >= put && r1.inside, $"{put} of {parts.Count} put in; {r1.n} in the bin; all inside {r1.inside}");
            Check("the bin's heap comes to rest on its floor or on other parts (nothing in mid-air)", r1.asleep == r1.n && r1.air == 0 && r1.low < 0.01f,
                  $"at rest {r1.asleep}/{r1.n}, in the air {r1.air}, lowest bottom {r1.low:0.000} m, top {r1.high:0.000} m (rim {RobotWorkbench.Bin.y}); just after: in the air {falling.air}");
            var bin = wb.transform.TransformPoint(Batch.G(RobotWorkbench.BinX, RobotWorkbench.BinFloor, RobotWorkbench.BinZ));
            LookFrom(bin + wb.Front * 0.45f + Vector3.up * 0.75f, bin + Vector3.up * 0.05f);   // looking down into it
            yield return g.Capture(Path.Combine(outDir, "bin_pile.png"), 3);
            // three taken out: the rest settle into the gaps
            var taken = wb.PoolContents().Where(id => g.world.DefOf(id).IsRobotPart).OrderByDescending(id => id).Take(3).ToList();
            foreach (var id in taken) wb.TakeFromPool(id);
            yield return Frames(2);
            for (float t = 0; t < 10 && wb.BinSettling; t += Time.deltaTime) yield return null;
            var r2 = wb.BinReport();
            Check("after parts are taken out of the bin, the rest settle (nothing left hanging)", r2.n == r1.n - taken.Count && r2.asleep == r2.n && r2.air == 0,
                  $"{taken.Count} taken; {r2.n} left, at rest {r2.asleep}, in the air {r2.air}");
            yield return g.Capture(Path.Combine(outDir, "bin_pile_after_take.png"), 3);
            foreach (var r in g.robots) r.paused = false;
        }

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
            var unreach0 = g.UnreachableParts();
            foreach (var (uid, why) in unreach0.Take(10)) Debug.Log($"[piles] unreachable at the start: {g.world.DefOf(uid).displayName} at {g.world.instances[uid].Position} ({why}) pile {(g.lastGen?.pileOf != null && uid < g.lastGen.pileOf.Length ? g.lastGen.pileOf[uid] : -9)}");
            Check("a new world has no unreachable parts once it has settled", unreach0.Count == 0, $"{unreach0.Count} unreachable");
            Check("a new world's parts all rest on something (nothing in mid-air at the start)", air.Count == 0,
                  $"{sample.Count} of {loose.Count} loose parts checked, in the air: {air.Count}{(air.Count > 0 ? " (" + string.Join("; ", air.Take(3)) + ")" : "")}; deepest overlap {deep0 * 1000:0.0} mm; {g.piles.lastInfo}");
            var hc = WorldGenerator.CrowdedFloorSpots(g.world, 1)[0] + Vector3.up * 0.3f;
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
            var ordinary = g.world.instances.Where(i => !g.world.DefOf(i.id).IsRobotPart && !g.world.DefOf(i.id).IsPegboardTool).ToList();   // board tools: their own slots (PegboardChecks)
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
            bool firstGivesTwo = f.level == 2 && f.skillPoints == 1;
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
            legacy.Remove("version");
            var m = FreshProgression();
            m.Load(legacy);
            bool migrated = m.DestinationsCompleted == f.DestinationsCompleted && m.level == f.level;
            Check("levels come only from destinations: 5000 single sorts give level 1, the first completion level 2, the curve rises, points survive save/load and old saves",
                  sortsNoLevel && firstGivesTwo && once && rising && roundTrip && migrated,
                  $"sorts only: level {(sortsNoLevel ? 1 : 0)}; first completion -> level 2 with 1 point {firstGivesTwo}; once {once}; increments {string.Join(" ", inc)}; round trip {roundTrip}; legacy {migrated} (level {m.level})");
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
            var sortable = g.db.order.Select(g.db.Get).Where(d => !d.IsRobotPart && !d.IsPegboardTool).ToList();   // board tools: not catalogue components, never rescaled, player-only by design
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
            var big = g.world.instances.Where(i => i.state == ItemState.InWorld && !g.robotReserved.Contains(i.id) && !g.world.DefOf(i.id).robotCarry && !g.world.DefOf(i.id).IsPegboardTool && !i.owner.StartsWith("stage:")
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

        /// <summary>Autoplay only: every robot whose parts are all in the parts pool is built from it ("Build from
        /// the parts pool" on the bench screen). In the game the player chooses which parts go on which robot.
        /// Returns how many robots were completed.</summary>
        int AutoAssemble()
        {
            var wb = g.workbench;
            int built = 0;
            foreach (var p in wb.projects)
            {
                if (p.crafted || wb.Complete(p) || wb.Operational(p)) continue;
                if (wb.InstalledCount(p) + wb.InstallableCount(p) < wb.Required(p)) continue;
                wb.InstallAll(p);
                if (wb.Complete(p)) { built++; Debug.Log($"[autoplay] auto-assembled {p.name} from the parts pool"); }
            }
            return built;
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
            var parts = g.world.instances
                .Select(i => $"{i.id}:{(int)i.state}:{i.owner}:{i.Position.x:0.000},{i.Position.y:0.000},{i.Position.z:0.000}");
            return string.Join("|", parts) + $"#{g.progress.sortedTotal}#{string.Join(",", g.inventory.items)}#{g.progress.discovered.Count}";
        }
    }
}
