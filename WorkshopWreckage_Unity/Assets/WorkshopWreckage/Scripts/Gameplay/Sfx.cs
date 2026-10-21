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
        public static float masterVolume = 0.8f;

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

        public static void Play(string name, Vector3? at = null, float volumeDb = 0, float pitchVar = 0.08f)
        {
            if (_i == null || !_i._clips.TryGetValue(name, out var clip)) return;
            var list = at.HasValue ? _i._3d : _i._2d;
            var src = list.Find(s => !s.isPlaying) ?? list[0];
            if (at.HasValue) src.transform.position = at.Value;
            src.clip = clip;
            src.volume = Mathf.Pow(10, volumeDb / 20f) * masterVolume;
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
