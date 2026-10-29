using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage.Pegboard
{
    // Spanners and wrenches: combination spanner, adjustable wrench, pipe wrench, monkey wrench, hex key set.
    public static partial class ToolFactory
    {
        const int Tin = 1;

        static void RegisterWrenches()
        {
            Add("combo_wrench", ComboWrench);
            Add("adjustable_wrench", AdjustableWrench);
            Add("pipe_wrench", PipeWrench);
            Add("monkey_wrench", MonkeyWrench);
            Add("hex_key_set", HexKeySet);
        }

        static Vector2 Rot(Vector2 v, float deg)
        {
            float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>An open jaw (a C-shaped plate): centre c, outer radius r, mouth half-width hw, the mouth facing
        /// direction `dirDeg` (0 = +Y), cut in to `depth` past the centre.</summary>
        static void OpenEnd(ToolMesh b, Vector2 c, float r, float hw, float depth, float dirDeg, float z0, float z1, int slot, Color col)
        {
            var d = Rot(Vector2.up, dirDeg); var s = new Vector2(d.y, -d.x);
            float h = Mathf.Sqrt(Mathf.Max(1e-6f, r * r - hw * hw));
            Vector2 pPlus = c + d * h + s * hw, pMinus = c + d * h - s * hw;
            float a0 = Mathf.Atan2(pPlus.y - c.y, pPlus.x - c.x), a1 = Mathf.Atan2(pMinus.y - c.y, pMinus.x - c.x);
            // the long way round from one lip to the other
            if (a1 <= a0) a1 += Mathf.PI * 2;
            var pts = new List<Vector2>();
            var arc = new List<Vector2>(ToolMesh.Arc(c, r, a0, a1, 18));
            // walk the side away from the mouth: if the arc passes through the mouth, go the other way
            var mid = arc[arc.Count / 2];
            if (Vector2.Dot(mid - c, d) > 0) { arc = new List<Vector2>(ToolMesh.Arc(c, r, a0, a1 - Mathf.PI * 2, 18)); }
            pts.AddRange(arc);
            pts.Add(c - s * hw - d * depth);
            pts.Add(c + s * hw - d * depth);
            b.Plate(pts, z0, z1, slot, col);
        }

        // combination spanner: open end at the bottom, ring end at the top (hangs on its ring)
        static void ComboWrench(ToolMesh b)
        {
            const float t = 0.012f;
            b.Strip(new Vector2(0, 0.13f), new Vector2(0, 0.86f), 0.075f, 0.062f, -t, t, Own, White);
            // a raised rib down the shank
            b.Strip(new Vector2(0, 0.2f), new Vector2(0, 0.8f), 0.03f, 0.026f, -t * 1.6f, -t, Own, White);
            OpenEnd(b, new Vector2(0, 0.075f), 0.075f, 0.036f, 0.0f, 195, -t, t, Own, White);
            b.Ring(new Vector2(0, 0.915f), 0.072f, 0.042f, -t * 1.3f, t * 1.3f, Own, White);
            b.Hook(new Vector2(0, 0.93f));
        }

        // adjustable wrench: the head with a fixed and a sliding jaw and the worm, a hanging hole in the handle
        static void AdjustableWrench(ToolMesh b)
        {
            const float t = 0.02f;
            var handle = new List<Vector2> { new(-0.05f, 0.06f) };
            handle.AddRange(ToolMesh.Arc(new Vector2(0, 0.06f), 0.05f, Mathf.PI, Mathf.PI * 2, 8));
            handle.Add(new Vector2(0.065f, 0.66f));
            handle.Add(new Vector2(-0.065f, 0.66f));
            b.Plate(handle, -t * 0.8f, t * 0.8f, Own, White);
            b.Ring(new Vector2(0, 0.06f), 0.03f, 0.018f, -t * 0.85f, t * 0.85f, Own, White);
            // head body
            OpenEnd(b, new Vector2(0, 0.8f), 0.15f, 0.06f, -0.01f, 15, -t, t, Own, White);
            // sliding jaw and worm
            b.Box(new Vector3(-0.07f, 0.72f, 0), new Vector3(0.07f, 0.12f, t * 1.6f), Tin, White, 15);
            b.Cyl(new Vector3(-0.05f, 0.69f, 0), new Vector3(0.05f, 0.69f, 0), 0.022f, Tin, White, 10);
            for (int i = 0; i < 6; i++)
                b.Cyl(new Vector3(-0.045f + i * 0.018f, 0.69f, 0), new Vector3(-0.04f + i * 0.018f, 0.69f, 0), 0.026f, Tin, White, 10);
            b.Hook(new Vector2(0, 0.06f));
        }

        // pipe wrench: painted I-beam handle, steel hook jaw and knurled nut
        static void PipeWrench(ToolMesh b)
        {
            const float t = 0.025f;
            b.Strip(new Vector2(0, 0.04f), new Vector2(0, 0.7f), 0.085f, 0.11f, -t, t, Own, White);
            b.Strip(new Vector2(0, 0.06f), new Vector2(0, 0.68f), 0.11f, 0.13f, -t * 0.4f, t * 0.4f, Own, White);   // flanges
            b.Ring(new Vector2(0, 0.06f), 0.03f, 0.016f, -t * 1.05f, t * 1.05f, Own, White);
            // fixed jaw on the handle head
            b.Plate(new[] { new Vector2(-0.07f, 0.68f), new Vector2(0.09f, 0.68f), new Vector2(0.1f, 0.78f), new Vector2(-0.02f, 0.8f), new Vector2(-0.07f, 0.76f) }, -t * 1.1f, t * 1.1f, Tin, White);
            // hook jaw: shank through the head, the toothed hook on top
            b.Box(new Vector3(-0.04f, 0.83f, 0), new Vector3(0.05f, 0.2f, t * 1.4f), Tin, White);
            b.Plate(new[] { new Vector2(-0.07f, 0.9f), new Vector2(0.12f, 0.88f), new Vector2(0.13f, 0.96f), new Vector2(0.04f, 1f), new Vector2(-0.07f, 0.98f) }, -t * 1.2f, t * 1.2f, Tin, White);
            for (int i = 0; i < 5; i++)
                b.Box(new Vector3(0.0f + i * 0.022f, 0.875f, 0), new Vector3(0.012f, 0.014f, t * 2.2f), Tin, White, 45);
            // knurled adjusting nut
            b.Cyl(new Vector3(-0.04f, 0.71f, 0), new Vector3(-0.04f, 0.79f, 0), 0.055f, Tin, DarkSteel, 14);
            b.Hook(new Vector2(0, 0.06f));
        }

        // monkey wrench: steel bar with a sliding jaw and a wooden handle
        static void MonkeyWrench(ToolMesh b)
        {
            const float t = 0.022f;
            b.Strip(new Vector2(0, 0.45f), new Vector2(0, 0.9f), 0.05f, 0.05f, -t, t, Own, White);
            b.Box(new Vector3(0.03f, 0.96f, 0), new Vector3(0.16f, 0.07f, t * 2.2f), Own, White);       // fixed jaw
            b.Box(new Vector3(0.03f, 0.79f, 0), new Vector3(0.16f, 0.07f, t * 2.2f), Tin, White);       // sliding jaw
            b.Cyl(new Vector3(0, 0.62f, 0), new Vector3(0, 0.7f, 0), 0.045f, Tin, DarkSteel, 12);       // screw ring
            Handle(b, 0.0f, 0.46f, 0.05f, Acc, Wood, false);
            b.Cyl(new Vector3(0, 0.44f, 0), new Vector3(0, 0.48f, 0), 0.054f, Tin, White, 12);          // ferrule
            b.Hook(new Vector2(0, 0.04f), 2);
        }

        // hex key set: graduated L keys in a blue holder (hangs by the holder)
        static void HexKeySet(ToolMesh b)
        {
            b.Box(new Vector3(0, 0.9f, 0.005f), new Vector3(0.75f, 0.12f, 0.06f), Acc, Blue);
            for (int i = 0; i < 8; i++)
            {
                float x = -0.32f + i * 0.09f, len = 0.38f + i * 0.06f, r = 0.012f + i * 0.0025f;
                b.Box(new Vector3(x, 0.9f - len * 0.5f, -0.03f), new Vector3(r * 2, len, r * 2), Own, White);
                b.Box(new Vector3(x + 0.05f, 0.95f, -0.03f), new Vector3(0.1f + r, r * 2, r * 2), Own, White);
            }
            b.Hook(new Vector2(-0.25f, 0.9f), 2);
            b.Hook(new Vector2(0.25f, 0.9f), 2);
        }
    }
}
