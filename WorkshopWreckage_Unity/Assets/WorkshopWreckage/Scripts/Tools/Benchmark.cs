using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Standard benchmark on the shared camera poses (port of Godot benchmark.gd):
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
                ["startup_ms"] = g.startupMs,
                ["development_build"] = Debug.isDebugBuild,
            };
            var poses = new List<object>();
            var all = new List<float>();
            var timings = new FrameTiming[1];
            foreach (var po in Json.Arr(cfg["poses"]))
            {
                var pose = Json.Obj(po);
                var p = Json.Arr(pose["pos"]);
                g.PlaceCamera(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}", Json.F(p[0]), Json.F(p[1]), Json.F(p[2]), Json.Num(pose, "yaw"), Json.Num(pose, "pitch")));
                float warm = 0;
                while (warm < (float)Json.Num(cfg, "warmup_s", 1.5)) { yield return null; warm += Time.unscaledDeltaTime; }
                var frames = new List<float>();
                double dc = 0, bt = 0, tr = 0, gpu = 0, cpu = 0;
                int n = 0, gpuN = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long last = sw.ElapsedTicks;
                while (sw.Elapsed.TotalSeconds < Json.Num(cfg, "measure_s", 5))
                {
                    yield return null;
                    long now = sw.ElapsedTicks;
                    frames.Add((float)((now - last) * 1000.0 / System.Diagnostics.Stopwatch.Frequency));
                    last = now;
                    dc += draws.LastValue; bt += batches.LastValue; tr += tris.LastValue;
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0)
                    {
                        gpu += timings[0].gpuFrameTime;
                        cpu += timings[0].cpuMainThreadFrameTime;
                        gpuN++;
                    }
                    n++;
                }
                frames.RemoveAt(0);
                all.AddRange(frames);
                var pick = MeasurePicking(g, Json.Int(cfg, "picks_per_pose", 300));
                var e = Stats(frames);
                e["name"] = Json.Str(pose, "name");
                e["draw_calls"] = System.Math.Round(dc / n);
                e["batches"] = System.Math.Round(bt / n);
                e["triangles"] = System.Math.Round(tr / n);
                e["gpu_ms"] = gpuN > 0 ? System.Math.Round(gpu / gpuN, 2) : -1.0;
                e["cpu_main_ms"] = gpuN > 0 ? System.Math.Round(cpu / gpuN, 2) : -1.0;
                e["pick_avg_us"] = pick.avg;
                e["pick_max_us"] = pick.max;
                e["pick_hit_rate"] = pick.hit;
                e["visible_items"] = (double)g.renderer.VisibleCount;
                poses.Add(e);
                Debug.Log($"[bench] {e["name"]}: {e["fps_avg"]} fps avg, {e["fps_1pct_low"]} 1% low, {e["draw_calls"]} draw calls, gpu {e["gpu_ms"]} ms, pick {pick.avg} us");
            }
            results["poses"] = poses;
            results["overall"] = Stats(all);
            results["system_used_mem_mb"] = System.Math.Round(sysMem.LastValue / 1048576.0, 1);
            results["total_used_mem_mb"] = System.Math.Round(totalMem.LastValue / 1048576.0, 1);
            results["gfx_used_mem_mb"] = System.Math.Round(gfxMem.LastValue / 1048576.0, 1);
            results["process_working_set_mb"] = System.Math.Round(System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576.0, 1);
            File.WriteAllText(outPath, Json.Write(results));
            Debug.Log("[bench] wrote " + outPath);
            draws.Dispose(); batches.Dispose(); tris.Dispose(); sysMem.Dispose(); gfxMem.Dispose(); totalMem.Dispose();
            Application.Quit();
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
                var (id, _) = g.index.Pick(cam.position, dir, Interaction.Reach);
                total += g.index.LastPickMicros;
                worst = System.Math.Max(worst, g.index.LastPickMicros);
                if (id >= 0) hits++;
            }
            return (System.Math.Round(total / count, 1), System.Math.Round(worst, 1), System.Math.Round((double)hits / count, 2));
        }
    }
}
