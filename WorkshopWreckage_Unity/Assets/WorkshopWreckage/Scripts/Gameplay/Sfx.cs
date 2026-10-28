using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Synthesised sound effects (same recipes as the Godot Audio autoload).</summary>
    public class Sfx : MonoBehaviour
    {
        const int Rate = 44100;
        static Sfx _i;
        readonly Dictionary<string, AudioClip> _clips = new();
        readonly List<AudioSource> _2d = new(), _3d = new();
        /// <summary>The workshop's own sounds (parts, drawers, steps, the lathe): the "Workshop" volume.</summary>
        public static float masterVolume = 0.8f;
        /// <summary>Robot motors, steps, servos and boot-ups; and the interface (clicks, chimes, saves, finds).</summary>
        public static float robotVolume = 0.8f, uiVolume = 0.8f;

        /// <summary>Settings (PlayerPrefs): master = the whole mix (AudioListener.volume), workshop =
        /// Sfx.masterVolume, robots, interface. A muted run (--mute, automated) plays nothing and never
        /// writes the saved values.</summary>
        public static float MasterSetting { get; private set; } = 1f;
        public static float SfxSetting { get; private set; } = 0.8f;
        public static float RobotSetting { get; private set; } = 0.8f;
        public static float UiSetting { get; private set; } = 0.8f;
        public static bool Muted { get; private set; }

        static readonly HashSet<string> RobotClips = new() { "robot_boot", "servo", "robot_step", "spider_step", "loop_wheel", "loop_tracks", "loop_rotor" };
        static readonly HashSet<string> UiClips = new() { "ui_click", "save", "milestone", "discover", "reject", "complete", "robot_part", "inspect" };

        /// <summary>The category volume a clip plays at.</summary>
        public static float VolumeOf(string clip) => RobotClips.Contains(clip) ? robotVolume : UiClips.Contains(clip) ? uiVolume : masterVolume;

        public static void LoadVolumes(bool mute)
        {
            Muted = mute;
            MasterSetting = Mathf.Clamp01(PlayerPrefs.GetFloat("ww_master_volume", 1f));
            SfxSetting = Mathf.Clamp01(PlayerPrefs.GetFloat("ww_sfx_volume", 0.8f));
            RobotSetting = Mathf.Clamp01(PlayerPrefs.GetFloat("ww_robot_volume", 0.8f));
            UiSetting = Mathf.Clamp01(PlayerPrefs.GetFloat("ww_ui_volume", 0.8f));
            AudioListener.volume = mute ? 0 : MasterSetting;
            masterVolume = mute ? 0 : SfxSetting;
            robotVolume = mute ? 0 : RobotSetting;
            uiVolume = mute ? 0 : UiSetting;
            Debug.Log($"[audio] master {MasterSetting:0.00}, workshop {SfxSetting:0.00}, robots {RobotSetting:0.00}, interface {UiSetting:0.00}{(mute ? ", muted run" : "")}");
        }

        public static void SetRobots(float v)
        {
            RobotSetting = Mathf.Clamp01(v);
            if (Muted) return;
            robotVolume = RobotSetting;
            PlayerPrefs.SetFloat("ww_robot_volume", RobotSetting);
            PlayerPrefs.Save();
        }

        public static void SetUi(float v)
        {
            UiSetting = Mathf.Clamp01(v);
            if (Muted) return;
            uiVolume = UiSetting;
            PlayerPrefs.SetFloat("ww_ui_volume", UiSetting);
            PlayerPrefs.Save();
        }

        public static void SetMaster(float v)
        {
            MasterSetting = Mathf.Clamp01(v);
            if (Muted) return;
            AudioListener.volume = MasterSetting;
            PlayerPrefs.SetFloat("ww_master_volume", MasterSetting);
            PlayerPrefs.Save();
        }

        public static void SetSfx(float v)
        {
            SfxSetting = Mathf.Clamp01(v);
            if (Muted) return;
            masterVolume = SfxSetting;
            PlayerPrefs.SetFloat("ww_sfx_volume", SfxSetting);
            PlayerPrefs.Save();
        }

        public static void Init(Transform parent)
        {
            if (_i != null) return;
            var go = new GameObject("Sfx");
            go.transform.SetParent(parent, false);
            _i = go.AddComponent<Sfx>();
            _i.Build();
        }

        void Build()
        {
            _clips["pickup"] = Clink(1900, 0.16f, 0.5f);
            _clips["pickup_heavy"] = Clink(900, 0.22f, 0.6f);
            _clips["drop_drawer"] = Rattle(5, 0.42f);
            _clips["drawer_open"] = Slide(0.16f, 140);
            _clips["drawer_close"] = Thunk(0.18f, 90);
            _clips["reject"] = Buzz(0.28f);
            _clips["complete"] = Chime(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.9f);
            _clips["milestone"] = Chime(new[] { 392f, 523.25f, 659.25f }, 0.7f);
            _clips["discover"] = Chime(new[] { 880f, 1318.5f }, 0.35f);
            _clips["ui_click"] = Clink(2600, 0.05f, 0.25f);
            _clips["step"] = Step();
            _clips["inspect"] = Whoosh(0.25f);
            _clips["settle"] = Rattle(2, 0.15f);
            _clips["drop_floor"] = Rattle(3, 0.25f);
            _clips["save"] = Chime(new[] { 659.25f, 987.77f }, 0.4f);
            _clips["robot_boot"] = Sweep(1.4f, 90, 620, 0.55f);
            _clips["servo"] = Sweep(0.16f, 320, 520, 0.3f);
            _clips["magnet"] = Sweep(0.14f, 520, 880, 0.22f);
            _clips["robot_step"] = Clink(3400, 0.035f, 0.18f);
            _clips["spider_step"] = Clink(5200, 0.02f, 0.12f);
            _clips["loop_wheel"] = Loop("wheel");
            _clips["loop_tracks"] = Loop("tracks");
            _clips["loop_rotor"] = Loop("rotor");
            _clips["loop_machine"] = Loop("machine");
            _clips["supermagnet"] = Sweep(0.9f, 140, 1250, 0.6f);
            _clips["robot_part"] = RobotFind();
            _clips["peg_hang"] = Clink(1250, 0.24f, 0.55f);   // a tool settling on its hooks
            for (int k = 0; k < 12; k++) _2d.Add(gameObject.AddComponent<AudioSource>());
            for (int k = 0; k < 10; k++)
            {
                var g = new GameObject("src3d");
                g.transform.SetParent(transform, false);
                var s = g.AddComponent<AudioSource>();
                s.spatialBlend = 1;
                s.minDistance = 1.5f;
                s.maxDistance = 25;
                _3d.Add(s);
            }
        }

        public static AudioClip Clip(string name) => _i != null && _i._clips.TryGetValue(name, out var c) ? c : null;

        /// <summary>Seamless one-second loops for robot locomotion (toy motors).</summary>
        static AudioClip Loop(string kind)
        {
            int n = Rate;
            var s = new float[n];
            var rng = new System.Random(kind.GetHashCode());
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * 0.08f;
                switch (kind)
                {
                    case "wheel":    // small electric motor whine + tyre hiss (whole cycles in 1 s)
                        s[i] = Mathf.Sin(2 * Mathf.PI * 880 * t) * 0.12f + Mathf.Sin(2 * Mathf.PI * 1320 * t) * 0.05f + lp * 0.25f;
                        break;
                    case "tracks":   // clattering links at 14 per second over a low rumble
                        float link = Mathf.Exp(-((t * 14) % 1) * 30);
                        s[i] = Mathf.Sin(2 * Mathf.PI * 55 * t) * 0.18f + lp * 0.5f * link + Mathf.Sin(2 * Mathf.PI * 2400 * t) * 0.08f * link;
                        break;
                    case "machine":  // lathe: mains hum with a belt whir
                        s[i] = Mathf.Sin(2 * Mathf.PI * 100 * t) * 0.16f + Mathf.Sin(2 * Mathf.PI * 300 * t) * 0.05f + lp * 0.12f * (0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 8 * t));
                        break;
                    default:         // two-blade rotor: 24 blade passes per second over a motor hum
                        float blade = 0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 24 * t);
                        s[i] = (lp * 0.6f * blade + Mathf.Sin(2 * Mathf.PI * 240 * t) * 0.08f);
                        break;
                }
            }
            return Make("loop_" + kind, s);
        }

        public static void Play(string name, Vector3? at = null, float volumeDb = 0, float pitchVar = 0.08f)
        {
            if (_i == null || !_i._clips.TryGetValue(name, out var clip)) return;
            var list = at.HasValue ? _i._3d : _i._2d;
            var src = list.Find(s => !s.isPlaying) ?? list[0];
            if (at.HasValue) src.transform.position = at.Value;
            src.clip = clip;
            src.volume = Mathf.Pow(10, volumeDb / 20f) * VolumeOf(name);
            src.pitch = 1 + Random.Range(-pitchVar, pitchVar);
            src.Play();
        }

        static AudioClip Make(string n, float[] s)
        {
            var c = AudioClip.Create(n, s.Length, 1, Rate, false);
            c.SetData(s, 0);
            return c;
        }

        static float Rnd() => Random.value * 2 - 1;

        static AudioClip Clink(float f, float dur, float amp)
        {
            int n = (int)(dur * Rate);
            var s = new float[n];
            var parts = new[] { (1f, 1f), (2.76f, 0.5f), (5.4f, 0.3f), (8.93f, 0.15f) };
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate, v = 0;
                foreach (var (m, a) in parts) v += Mathf.Sin(2 * Mathf.PI * f * m * t) * a * Mathf.Exp(-t * (18 + m * 6) / dur * 0.12f);
                v += Rnd() * 0.3f * Mathf.Exp(-t * 400);
                s[i] = v * amp * 0.5f;
            }
            return Make("clink", s);
        }

        static AudioClip Rattle(int hits, float dur)
        {
            int n = (int)(dur * Rate);
            var s = new float[n];
            var rng = new System.Random(hits * 31);
            for (int h = 0; h < hits; h++)
            {
                int start = h > 0 ? (int)(rng.NextDouble() * dur * 0.55 * Rate) : 0;
                float f = 1500 + (float)rng.NextDouble() * 1700;
                float a = (0.25f + (float)rng.NextDouble() * 0.25f) * (h == 0 ? 1 : 0.6f);
                for (int i = start; i < n; i++)
                {
                    float t = (float)(i - start) / Rate;
                    s[i] += (Mathf.Sin(2 * Mathf.PI * f * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * f * 2.7f * t)) * a * Mathf.Exp(-t * 55);
                }
            }
            for (int i = 0; i < Mathf.Min(n, (int)(0.08f * Rate)); i++)
            {
                float t = (float)i / Rate;
                s[i] += Mathf.Sin(2 * Mathf.PI * 110 * t) * 0.5f * Mathf.Exp(-t * 45);
            }
            return Make("rattle", s);
        }

        static AudioClip Slide(float dur, float f)
        {
            int n = (int)(dur * Rate);
            var s = new float[n];
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                lp = Mathf.Lerp(lp, Rnd(), 0.08f);
                s[i] = lp * 0.9f * Mathf.Sin(Mathf.PI * t / dur) + Mathf.Sin(2 * Mathf.PI * f * t) * 0.15f * Mathf.Sin(Mathf.PI * t / dur);
            }
            return Make("slide", s);
        }

        static AudioClip Thunk(float dur, float f)
        {
            int n = (int)(dur * Rate);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                s[i] = Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * 30) * 0.8f + Rnd() * Mathf.Exp(-t * 120) * 0.4f;
            }
            return Make("thunk", s);
        }

        /// <summary>Motor spin-up: a rising tone with a gritty square overtone.</summary>
        static AudioClip Sweep(float dur, float f0, float f1, float amp)
        {
            int n = (int)(dur * Rate);
            var s = new float[n];
            float ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate, u = t / dur;
                float f = Mathf.Lerp(f0, f1, u * u * (3 - 2 * u));
                ph += 2 * Mathf.PI * f / Rate;
                float env = Mathf.Min(1, t * 40) * Mathf.Min(1, (dur - t) * 12);
                float sq = Mathf.Sin(ph * 0.5f) > 0 ? 1 : -1;
                s[i] = (Mathf.Sin(ph) * 0.6f + sq * 0.12f + Mathf.Sin(ph * 3.01f) * 0.1f) * env * amp;
            }
            return Make("sweep" + f0, s);
        }

        /// <summary>A robot part found (playtest 2026-10-25: its own unmistakable sound, every
        /// time): three rising square-wave blips with a ring-modulated shimmer, then a short servo
        /// whirr down, nothing like the chimes of a completed box.</summary>
        static AudioClip RobotFind()
        {
            const float dur = 0.78f;
            int n = (int)(dur * Rate);
            var s = new float[n];
            var notes = new[] { 659.25f, 987.77f, 1318.5f };   // E5 B5 E6
            for (int k = 0; k < notes.Length; k++)
            {
                int start = (int)(k * 0.085f * Rate), len = (int)((k == 2 ? 0.3f : 0.075f) * Rate);
                float ph = 0;
                for (int i = 0; i < len && start + i < n; i++)
                {
                    float t = (float)i / Rate;
                    ph += 2 * Mathf.PI * notes[k] / Rate;
                    float sq = Mathf.Sin(ph) > 0 ? 1 : -1;
                    float ring = Mathf.Sin(ph * 1.5f) * Mathf.Sin(2 * Mathf.PI * 37 * t);
                    float env = Mathf.Min(1, t * 300) * Mathf.Exp(-t * (k == 2 ? 7f : 18f));
                    s[start + i] += (sq * 0.16f + Mathf.Sin(ph) * 0.3f + ring * 0.14f) * env;
                }
            }
            // servo tail under the last blip
            int t0 = (int)(0.3f * Rate);
            float sp = 0;
            for (int i = t0; i < n; i++)
            {
                float t = (float)(i - t0) / Rate, u = t / (dur - 0.3f);
                sp += 2 * Mathf.PI * Mathf.Lerp(420, 260, u) / Rate;
                float env = Mathf.Min(1, t * 30) * Mathf.Min(1, (1 - u) * 6) * 0.12f;
                s[i] += (Mathf.Sin(sp) + 0.4f * (Mathf.Sin(sp * 2) > 0 ? 1 : -1)) * env;
            }
            return Make("robot_part", s);
        }

        static AudioClip Buzz(float dur)
        {
            int n = (int)(dur * Rate);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Min(1, t * 80) * Mathf.Exp(-t * 6);
                float sq = (t * 140) % 1 < 0.5f ? 1 : -1;
                s[i] = (sq * 0.25f + Mathf.Sin(2 * Mathf.PI * 70 * t) * 0.4f) * env;
            }
            return Make("buzz", s);
        }

        static AudioClip Chime(float[] freqs, float dur)
        {
            int n = (int)(dur * Rate);
            var s = new float[n];
            for (int k = 0; k < freqs.Length; k++)
            {
                int start = (int)(k * dur * 0.12f * Rate);
                for (int i = start; i < n; i++)
                {
                    float t = (float)(i - start) / Rate, f = freqs[k];
                    s[i] += (Mathf.Sin(2 * Mathf.PI * f * t) + 0.35f * Mathf.Sin(4 * Mathf.PI * f * t) + 0.1f * Mathf.Sin(6 * Mathf.PI * f * t)) * 0.22f * Mathf.Exp(-t * 4.5f);
                }
            }
            return Make("chime", s);
        }

        static AudioClip Step()
        {
            int n = (int)(0.14f * Rate);
            var s = new float[n];
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                lp = Mathf.Lerp(lp, Rnd(), 0.15f);
                s[i] = (lp * 0.6f + Mathf.Sin(2 * Mathf.PI * 85 * t) * 0.5f) * Mathf.Exp(-t * 38);
            }
            return Make("step", s);
        }

        static AudioClip Whoosh(float dur)
        {
            int n = (int)(dur * Rate);
            var s = new float[n];
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                lp = Mathf.Lerp(lp, Rnd(), 0.05f + 0.2f * t / dur);
                s[i] = lp * Mathf.Sin(Mathf.PI * t / dur) * 0.6f;
            }
            return Make("whoosh", s);
        }
    }
}
