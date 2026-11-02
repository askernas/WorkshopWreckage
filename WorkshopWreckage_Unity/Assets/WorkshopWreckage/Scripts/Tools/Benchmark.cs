using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Standard benchmark on the shared camera poses :
    /// frame times, 1 % lows, draw calls, GPU/CPU time, picking cost, memory.</summary>
    public class Benchmark
    {
        public IEnumerator Run(Game g, string outPath)
        {
            var cfg = Shared.ReadObject(Shared.DataPath("benchmark_cameras.json"));
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            g.player.enabled = false;
            g.hud.SetHudVisible(false);
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var sysMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            var gfxMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
            var totalMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var texMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Texture Memory");
            var meshMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Mesh Memory");
            // render-thread / present markers: only readable in development builds (WW_DEV_BUILD=1)
            if (g.args.ContainsKey("benchmarkerdump"))   // QA: every recorder this player offers
            {
                var hs = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(hs);
                File.WriteAllLines(outPath + ".markers.txt", hs.Select(h => { var d = ProfilerRecorderHandle.GetDescription(h); return d.Category.Name + "	" + d.Name; }).OrderBy(x => x));
            }
            var markers = Markers(g.args.TryGetValue("benchmarkers", out var mk) ? mk.Split(';') : DefaultMarkers);
            yield return null;
            g.builder.RefreshProbe();
            var results = new Dictionary<string, object>
            {
                ["engine"] = "Unity " + Application.unityVersion,
                ["renderer"] = "URP Forward+ (" + SystemInfo.graphicsDeviceType + ")",
                ["gpu"] = SystemInfo.graphicsDeviceName,
                ["resolution"] = new List<object> { (double)Screen.width, (double)Screen.height },
                ["items"] = (double)g.world.Count,
                ["seed"] = (double)g.world.seed,
                ["item_meshes"] = (double)g.renderer.GroupCount,
                ["scene_renderers"] = SceneRenderers(g),
                ["startup_ms"] = g.startupMs,
                ["development_build"] = Debug.isDebugBuild,
            };
            var poses = new List<object>();
            var all = new List<float>();
            var timings = new FrameTiming[1];
            foreach (var po in Json.Arr(cfg["poses"]))
            {
                if (g.args.ContainsKey("benchactiveonly")) break;   // QA: only the active pose
                var pose = Json.Obj(po);
                var p = Json.Arr(pose["pos"]);
                g.PlaceCamera(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}", Json.F(p[0]), Json.F(p[1]), Json.F(p[2]), Json.Num(pose, "yaw"), Json.Num(pose, "pitch")));
                yield return Measure(Json.Str(pose, "name"));
            }
            // active: every mobile robot working and three Sorting Arms sorting staged parts
            ActivateEverything(g);
            var sk = g.sockets.Values.OrderBy(x => x.id).First();
            var eye = sk.point + sk.facing * Vector3.forward * 3.2f + Vector3.down * 0.4f;
            var look = Quaternion.LookRotation(sk.point + Vector3.down * 1.0f - eye);
            g.PlaceCamera(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}", eye.x, eye.y, -eye.z, -look.eulerAngles.y, -NormalAngle(look.eulerAngles.x)));
            yield return Measure("active_robots_and_arms");

            IEnumerator Measure(string poseName)
            {
                Robot.cpuMs = 0; Robot.worstMs = 0; SortingArm.cpuMs = 0;
                float warm = 0;
                while (warm < (float)Json.Num(cfg, "warmup_s", 1.5)) { yield return null; warm += Time.unscaledDeltaTime; }
                var frames = new List<float>();
                double dc = 0, bt = 0, tr = 0, gpu = 0, cpu = 0, rt = 0, pw = 0, cf = 0, sp = 0;
                var mSum = new double[markers.Count];
                int n = 0, gpuN = 0;
                Robot.cpuMs = 0; Robot.worstMs = 0; SortingArm.cpuMs = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long last = sw.ElapsedTicks;
                int gc0 = System.GC.CollectionCount(0);
                long mem0 = System.GC.GetTotalMemory(false);
                while (sw.Elapsed.TotalSeconds < Json.Num(cfg, "measure_s", 5))
                {
                    yield return null;
                    long now = sw.ElapsedTicks;
                    frames.Add((float)((now - last) * 1000.0 / System.Diagnostics.Stopwatch.Frequency));
                    last = now;
                    dc += draws.LastValue; bt += batches.LastValue; tr += tris.LastValue; sp += setPass.LastValue;
                    for (int k = 0; k < markers.Count; k++) mSum[k] += markers[k].rec.LastValue;
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0)
                    {
                        gpu += timings[0].gpuFrameTime;
                        cpu += timings[0].cpuMainThreadFrameTime;
                        rt += timings[0].cpuRenderThreadFrameTime;
                        pw += timings[0].cpuMainThreadPresentWaitTime;
                        cf += timings[0].cpuFrameTime;
                        gpuN++;
                    }
                    n++;
                }
                frames.RemoveAt(0);
                all.AddRange(frames);
                var pick = MeasurePicking(g, Json.Int(cfg, "picks_per_pose", 300));
                var e = Stats(frames);
                e["name"] = poseName;
                e["draw_calls"] = System.Math.Round(dc / n);
                e["batches"] = System.Math.Round(bt / n);
                e["triangles"] = System.Math.Round(tr / n);
                e["gpu_ms"] = gpuN > 0 ? System.Math.Round(gpu / gpuN, 2) : -1.0;
                e["cpu_main_ms"] = gpuN > 0 ? System.Math.Round(cpu / gpuN, 2) : -1.0;
                e["cpu_render_ms"] = gpuN > 0 ? System.Math.Round(rt / gpuN, 2) : -1.0;
                e["present_wait_ms"] = gpuN > 0 ? System.Math.Round(pw / gpuN, 2) : -1.0;
                e["cpu_frame_ms"] = gpuN > 0 ? System.Math.Round(cf / gpuN, 2) : -1.0;
                e["setpass_calls"] = System.Math.Round(sp / n);
                var me = new Dictionary<string, object>();
                for (int k = 0; k < markers.Count; k++) me[markers[k].name] = System.Math.Round(mSum[k] / n / 1e6, 3);   // ns -> ms per frame
                if (markers.Count > 0) e["markers_ms"] = me;
                e["gc_collections"] = (double)(System.GC.CollectionCount(0) - gc0);
                e["managed_alloc_mb"] = System.Math.Round((System.GC.GetTotalMemory(false) - mem0) / 1048576.0, 1);
                e["worst_frame_ms"] = System.Math.Round(frames.Max(), 1);
                e["pick_avg_us"] = pick.avg;
                e["pick_max_us"] = pick.max;
                e["pick_hit_rate"] = pick.hit;
                e["visible_items"] = (double)g.renderer.VisibleCount;
                e["item_batches"] = new List<object> { (double)g.renderer.DrawnBatches, (double)g.renderer.NearBatches, (double)g.renderer.CulledBatches, (double)g.renderer.OccludedBatches };   // drawn, of them detailed, skipped, of them hidden behind something
                e["robots_cpu_ms_per_frame"] = System.Math.Round(Robot.cpuMs / System.Math.Max(1, n), 3);
                e["robot_worst_update_ms"] = System.Math.Round(Robot.worstMs, 1);
                e["robot_worst_update"] = Robot.worstWhat;
                e["arms_cpu_ms_per_frame"] = System.Math.Round(SortingArm.cpuMs / System.Math.Max(1, n), 3);
                poses.Add(e);
                Debug.Log($"[bench] {e["name"]}: {e["fps_avg"]} fps avg, {e["fps_1pct_low"]} 1% low, {e["draw_calls"]} draw calls, gpu {e["gpu_ms"]} ms, pick {pick.avg} us");
            }
            results["poses"] = poses;
            // save / load timing on this preset (P2-6)
            var swSave = System.Diagnostics.Stopwatch.StartNew();
            g.SaveGame("bench_tmp");
            results["save_ms"] = System.Math.Round(swSave.Elapsed.TotalMilliseconds, 1);
            var swLoad = System.Diagnostics.Stopwatch.StartNew();
            g.LoadGame("bench_tmp");
            results["load_ms"] = System.Math.Round(swLoad.Elapsed.TotalMilliseconds, 1);
            results["save_kb"] = System.Math.Round(new FileInfo(SaveSystem.PathOf("bench_tmp")).Length / 1024.0, 1);
            File.Delete(SaveSystem.PathOf("bench_tmp"));
            Debug.Log($"[bench] save {results["save_ms"]} ms, load {results["load_ms"]} ms, {results["save_kb"]} kB");
            results["overall"] = Stats(all);
            results["system_used_mem_mb"] = System.Math.Round(sysMem.LastValue / 1048576.0, 1);
            results["total_used_mem_mb"] = System.Math.Round(totalMem.LastValue / 1048576.0, 1);
            results["gfx_used_mem_mb"] = System.Math.Round(gfxMem.LastValue / 1048576.0, 1);
            results["texture_mem_mb"] = System.Math.Round(texMem.LastValue / 1048576.0, 1);
            results["mesh_mem_mb"] = System.Math.Round(meshMem.LastValue / 1048576.0, 1);
            results["process_working_set_mb"] = System.Math.Round(System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576.0, 1);
            File.WriteAllText(outPath, Json.Write(results));
            Debug.Log("[bench] wrote " + outPath);
            draws.Dispose(); batches.Dispose(); tris.Dispose(); sysMem.Dispose(); gfxMem.Dispose(); totalMem.Dispose();
            setPass.Dispose(); texMem.Dispose(); meshMem.Dispose();
            foreach (var m in markers) m.rec.Dispose();
            Application.Quit();
        }

        // main / render thread markers; "gpu:" = the marker's GPU time (development builds only)
        static readonly string[] DefaultMarkers =
        {
            "PlayerLoop", "Gfx.WaitForGfxCommandsFromMainThread", "Gfx.WaitForPresentOnGfxThread", "Gfx.PresentFrame", "Device.Present",
            "GfxDeviceD3D12.WaitForGPU", "RenderLoop.DrawSRPBatcher", "RenderLoop.ScheduleDraw", "Batch.DrawInstanced", "Gfx.UpdateBufferRanges",
            "Inl_DrawOpaqueObjects", "Inl_Draw Main Light Shadowmap", "Inl_Draw Additional Lights Shadowmap",
            "gpu:DrawOpaqueObjects", "gpu:Draw Main Light Shadowmap", "gpu:Draw Additional Lights Shadowmap", "gpu:DepthPrepass", "gpu:DrawDepthNormalPrepass", "gpu:SSAO",
        };

        /// <summary>Recorders for the named profiler markers that exist in this player (missing ones are skipped).</summary>
        static List<(string name, ProfilerRecorder rec)> Markers(IEnumerable<string> names)
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var byName = new Dictionary<string, ProfilerRecorderDescription>();
            foreach (var h in handles) { var d = ProfilerRecorderHandle.GetDescription(h); byName[d.Name] = d; }
            var list = new List<(string, ProfilerRecorder)>();
            foreach (var n in names)
            {
                bool gpu = n.StartsWith("gpu:");
                string m = gpu ? n.Substring(4) : n;
                if (!byName.TryGetValue(m, out var d)) continue;
                var opt = ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame | (gpu ? ProfilerRecorderOptions.GpuRecorder : 0);
                list.Add((n, ProfilerRecorder.StartNew(d.Category, m, 1, opt)));
            }
            return list;
        }

        static Dictionary<string, object> Stats(List<float> frames)
        {
            float avg = frames.Average();
            var sorted = frames.OrderBy(x => x).ToList();
            var worst = sorted.Skip((int)(sorted.Count * 0.99f)).ToList();
            float low = worst.Count > 0 ? worst.Average() : avg;
            return new Dictionary<string, object>
            {
                ["frames"] = (double)frames.Count,
                ["frame_ms_avg"] = System.Math.Round(avg, 3),
                ["fps_avg"] = System.Math.Round(1000.0 / avg, 1),
                ["fps_1pct_low"] = System.Math.Round(1000.0 / low, 1),
                ["frame_ms_p99"] = System.Math.Round(sorted[Mathf.Max(0, (int)(sorted.Count * 0.99f) - 1)], 3),
            };
        }

        static (double avg, double max, double hit) MeasurePicking(Game g, int count)
        {
            var cam = g.player.cam.transform;
            var rng = new System.Random(5);
            double total = 0, worst = 0;
            int hits = 0;
            for (int i = 0; i < count; i++)
            {
                var dir = (cam.forward + cam.right * (float)(rng.NextDouble() * 0.7 - 0.35) + cam.up * (float)(rng.NextDouble() * 0.5 - 0.25)).normalized;
                var (id, _) = g.index.Pick(cam.position, dir, Interaction.BaseReach);
                total += g.index.LastPickMicros;
                worst = System.Math.Max(worst, g.index.LastPickMicros);
                if (id >= 0) hits++;
            }
            return (System.Math.Round(total / count, 1), System.Math.Round(worst, 1), System.Math.Round((double)hits / count, 2));
        }
        static float NormalAngle(float a) => a > 180 ? a - 360 : a;

        /// <summary>Builds and starts every robot (mobile ones go to work, the first three
        /// Sorting Arms are mounted on the first sockets, each with parts staged below it).</summary>
        static void ActivateEverything(Game g)
        {
            var wb = g.workbench;
            g.progression.level = g.progression.maxLevel;
            foreach (var p in wb.projects)
            {
                var r = wb.RobotOf(p);
                if (r == null || r.Operational) continue;
                if (p.crafted) wb.FillRecipe(p); else wb.ForceInstall(p);
                r.SetOperational();
            }
            // QA switches: --benchnorobots keeps the mobile robots idle, --benchnoarms leaves the arms unmounted
            if (g.args.ContainsKey("benchnorobots")) foreach (var r in g.robots.Where(r => r.Mobile)) r.paused = true;
            var arms = g.args.ContainsKey("benchnoarms") ? new List<SortingArm>() : g.robots.Where(r => r.arm != null).Select(r => r.arm).ToList();
            var sockets = g.sockets.Values.OrderBy(s => s.id).ToList();
            for (int k = 0; k < arms.Count && k < sockets.Count; k++)
            {
                arms[k].PickUp();
                arms[k].Mount(sockets[k]);
                foreach (var box in sockets[k].dests.Take(6))
                {
                    var id = g.world.instances.Where(i => i.state == ItemState.InWorld && g.world.DefOf(i.id).storageBox == box).Select(i => i.id).DefaultIfEmpty(-1).First();
                    var spot = g.robots[0].StagingSpot(box);
                    if (id < 0 || !spot.HasValue) continue;
                    var def = g.world.DefOf(id);
                    g.index.Remove(id);
                    g.world.SetTransform(id, Matrix4x4.TRS(spot.Value + Vector3.up * (ItemDatabase.Extents(def)[2] * 0.5f), Quaternion.identity, Vector3.one * def.worldScale));
                    g.world.SetState(id, ItemState.InWorld, "stage:" + box);
                    g.index.Insert(id);
                }
            }
        }

        /// <summary>Active mesh renderers in the scene, by top-level owner (draw-call budget).</summary>
        static Dictionary<string, object> SceneRenderers(Game g)
        {
            var o = new Dictionary<string, object>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var t = r.transform;
                while (t.parent != null && t.parent != g.transform) t = t.parent;
                string key = t.name.StartsWith("Unit_") ? "storage units" : t.name.StartsWith("Robot_") ? "robots" : t.name;
                o[key] = (o.TryGetValue(key, out var v) ? (double)v : 0) + 1.0;
                o["materials:" + key] = (o.TryGetValue("materials:" + key, out var m) ? (double)m : 0) + r.sharedMaterials.Length;
            }
            return o;
        }
    }
}
