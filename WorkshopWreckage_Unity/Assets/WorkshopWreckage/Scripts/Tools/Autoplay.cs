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
            ic.Primary();
            Check("wrong destination refused, nothing lost", ic.targetDest == wrong && g.inventory.Count == before && w.instances[held].state == ItemState.Carried, "target " + (ic.targetDest ?? ic.targetType));

            // 4. store everything by aiming at the right destinations
            int stored = 0, viaAim = 0, shots = 0;
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
            File.WriteAllText(Path.Combine(outDir, "autoplay_report.json"), Json.Write(new Dictionary<string, object> { ["checks"] = checks, ["passed"] = (double)passed, ["failed"] = (double)failed }));
            Debug.Log($"[autoplay] {passed} passed, {failed} failed");
            Application.Quit(failed == 0 ? 0 : 1);
        }

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
            var starter = g.starterIds;
            var sdef = starter.Count > 0 ? w.DefOf(starter[0]) : null;
            Check("a small starter set lies together right by the start, its box nearby", sdef != null && starter.Count == w.TotalOf(sdef.id) && starter.All(i => (w.instances[i].Position - sp).magnitude < 4.5f) && g.db.IsOpenKind(sdef.storageBox),
                  sdef == null ? "none" : $"{starter.Count} × {sdef.displayName} at {starter.Max(i => (w.instances[i].Position - sp).magnitude):0.0} m → {g.db.BoxLabel(sdef.storageBox)}");

            // nothing floats: take a part that carries another; the one on top comes down onto what is below
            var top = loose.Where(i => i.Position.y > 0.1f && i.Position.y < 1.0f && !g.db.Get(i.defId).IsRobotPart)
                .Select(i => (i, under: g.index.QuerySphere(i.Position - Vector3.up * 0.06f, 0.05f).Where(o => o != i.id && w.instances[o].state == ItemState.InWorld && w.instances[o].Position.y < i.Position.y - 0.02f && !g.db.Get(w.instances[o].defId).IsRobotPart).ToList()))
                .FirstOrDefault(x => x.under.Count >= 1 && OnlySupport(x.i.id, x.under[0]));
            if (top.i != null)
            {
                float y0 = top.i.Position.y;
                g.player.Teleport(top.i.Position + new Vector3(0.6f, -top.i.Position.y, 0.6f));
                g.interaction.Pickup(top.under[0], false);
                yield return new WaitForSeconds(1.2f);
                // nothing within half a metre is left hanging in the air
                float Gap(int id) { var bb = g.index.WorldBounds(id); return bb.min.y - g.interaction.SupportUnder(id, bb, new Dictionary<int, float>()); }
                var around = g.index.QuerySphere(top.i.Position, 0.5f).Where(o => w.instances[o].state == ItemState.InWorld).ToList();
                float worst = around.Count > 0 ? around.Max(Gap) : 0;
                Check("nothing floats when the part under it is taken away", worst < 0.05f, $"top part {y0:0.000} -> {top.i.Position.y:0.000} m; worst gap of {around.Count} parts nearby {worst:0.000} m; settle: {g.interaction.lastSettleInfo}");
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
            var others = g.robots.Where(r => r.id != "retrobot" && !wb.P(r.id).crafted).ToList();
            Check("base capacities follow the capability matrix (1 / 1 / 1 / 3 / 1, not scaled by world size)",
                  g.robots.First(r => r.id == "retrobot").Capacity == 1 && g.robots.First(r => r.id == "unibot").Capacity == 1 && g.robots.First(r => r.id == "spiderbot").Capacity == 1
                  && g.robots.First(r => r.id == "caterpillarbot").Capacity == 3 && g.robots.First(r => r.id == "hoverbot").Capacity == 1,
                  string.Join(", ", g.robots.Select(r => $"{r.displayName} {r.Capacity}")));
            var retro = g.retrobot; var uni = g.robots.First(r => r.id == "unibot");
            Check("Unibot is about twice as fast as Retrobot", uni.WalkSpeed / retro.BaseStat("walk_speed") is > 1.8f and < 2.2f, $"{uni.WalkSpeed:0.00} vs {retro.BaseStat("walk_speed"):0.00} m/s");
            Check("five robot types at toy scale, up to three Hoverbots", g.robots.Count(r => !wb.P(r.id).crafted) == 5 && g.robots.Count(r => r.type == "hoverbot") == 3 && g.robots.All(r => r.Height <= 0.31f),
                  string.Join(", ", g.robots.Select(r => $"{r.displayName} {r.Height:0.00} m")));
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

            var before = g.robots.ToDictionary(r => r.id, r => (r.delivered, r.staged));
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
            Check("Spiderbot collects only its one assigned definition", !spiderPicks.Contains(-99), $"definitions carried: {spiderPicks.Count}, now {spider.Describe()}");
            yield return RobotSpecChecks(hover, uni, spider);
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

        /// <summary>Robot specialisation, upgrades and the second Hoverbot
        /// (robots_and_levels.md §19, robot_blueprints_and_assembly.md §10 test 14).</summary>
        IEnumerator RobotSpecChecks(Robot hover, Robot uni, Robot spider)
        {
            var wb = g.workbench;
            var pr = g.progression;
            var retro = g.retrobot;
            bool High(string box) => g.db.boxes.TryGetValue(box, out var bd) && Json.Num(bd, "elevation_m") > 1.0;
            bool Loose(ItemInstance i) => i.state == ItemState.InWorld && !g.robotReserved.Contains(i.id) && !g.world.DefOf(i.id).IsRobotPart && !i.owner.StartsWith("stage:");

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
                float t1 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t1 < 60 && !Done()) yield return null;
                Time.timeScale = 1;
                Check("Hoverbot empties the staging zones and stores those parts high up", Done() && hover.delivered > hd,
                      $"{waiting.Count(i => g.world.instances[i].state == ItemState.Stored)} of {waiting.Count} staged parts stored, {hover.Describe()}");
            }
            else Check("Hoverbot empties the staging zones and stores those parts high up", false, "no candidate part");

            // Scenario C: Spiderbot on an assignment that needs climbing
            var shelfPart = g.world.instances.Where(i => Loose(i) && i.Position.y > 0.55f && i.Position.y < spider.ReachHeight - 0.05f && ItemDatabase.Extents(g.world.DefOf(i.id))[0] <= spider.MaxItemSize
                                                                && g.UnitAt(i.Position) != null)
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
                Check("Spiderbot climbs furniture to reach parts of its assignment", climbed, $"{g.world.DefOf(shelfPart.id).displayName} at {shelfPart.Position.y:0.00} m: {shelfPart.state}; {spider.Describe()}");
            }
            else Check("Spiderbot climbs furniture to reach parts of its assignment", false, "no part on furniture in range");

            // upgrades: better at their jobs, never past their permanent limits
            pr.skillPoints += 60;
            float reach0 = retro.ReachHeight;
            for (int k = 0; k < 3; k++) g.BuyRobotUpgrade("retro_reach");
            var highBox = g.db.boxes.Keys.FirstOrDefault(High);
            Check("Retrobot's Arm Reach raises its reach, but never to the high shelves", retro.ReachHeight > reach0 && retro.ReachHeight <= retro.LimitOf("reach_height") + 1e-3f && highBox != null && !retro.ReachableDest(highBox),
                  $"{reach0:0.00} -> {retro.ReachHeight:0.00} m (limit {retro.LimitOf("reach_height"):0.00})");
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

        /// <summary>Earns real XP (objective rewards) until the level is reached.</summary>
        void LevelTo(int level)
        {
            var pr = g.progression;
            for (int k = 0; pr.level < level && k < 5000; k++) pr.OnObjective("autotest_level_" + k);
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
            pr.level = 1; pr.xp = 0;
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

            // 15. every preset can reach every blueprint level from ordinary collection work
            var cfgRobots = Robot.AllConfig();
            int maxBp = wb.projects.Where(p => !p.crafted).Max(p => p.level), maxCraft = wb.projects.Max(p => p.level);
            int oItems = g.world.Count - g.db.RobotPartIds().Count, oDefs = g.db.order.Count(d => !g.db.Get(d).IsRobotPart && g.world.TotalOf(d) > 0);
            long at75 = pr.CollectionXp(oItems, oDefs, 0.75), at100 = pr.CollectionXp(oItems, oDefs, 1.0);
            Check("every blueprint level is reachable from collection work alone (robots by 75% of the sets, crafted Hoverbots by 100%)", at75 >= pr.XpAtLevelStart(maxBp) && at100 >= pr.XpAtLevelStart(maxCraft),
                  $"{g.presetId}: {at75:N0} XP at 75% vs {pr.XpAtLevelStart(maxBp):N0} for level {maxBp}; {at100:N0} XP at 100% vs {pr.XpAtLevelStart(maxCraft):N0} for level {maxCraft}");

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
