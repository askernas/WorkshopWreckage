using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage.Pegboard
{
    // Cutting and shaping: hand saw, hacksaw, file, wood chisel, cold chisel, scraper, nail punch.
    public static partial class ToolFactory
    {
        static void RegisterCutting()
        {
            Add("hand_saw", HandSaw);
            Add("hacksaw", Hacksaw);
            Add("file", FileTool);
            Add("wood_chisel", WoodChisel);
            Add("cold_chisel", ColdChisel);
            Add("scraper", Scraper);
            Add("nail_punch", NailPunch);
        }

        // hand saw: a broad tapered blade, teeth down the front edge, a closed D handle (own colour) riveted on top
        static void HandSaw(ToolMesh b)
        {
            var blade = new List<Vector2> { new(-0.13f, 0.74f), new(0.1f, 0.74f), new(0.035f, 0.0f) };
            int teeth = 44;
            for (int i = 0; i <= teeth; i++)
            {
                float t = i / (float)teeth, y = t * 0.74f, x = Mathf.Lerp(-0.08f, -0.13f, t);
                blade.Add(new Vector2(x - (i % 2 == 0 ? 0 : 0.012f), y));
            }
            b.Plate(blade, -0.003f, 0.003f, Tin, White);
            // the D loop and the cheek that clamps the blade
            var loop = new[] { new Vector2(-0.13f, 0.72f), new Vector2(-0.14f, 0.9f), new Vector2(-0.07f, 0.99f), new Vector2(0.05f, 0.99f), new Vector2(0.11f, 0.9f), new Vector2(0.09f, 0.72f) };
            for (int i = 0; i + 1 < loop.Length; i++) b.Strip(loop[i], loop[i + 1], 0.055f, 0.055f, -0.022f, 0.022f, Own, White);
            b.Plate(new[] { new Vector2(-0.15f, 0.64f), new Vector2(0.12f, 0.64f), new Vector2(0.11f, 0.78f), new Vector2(-0.14f, 0.78f) }, -0.024f, 0.024f, Own, White);
            for (int i = 0; i < 3; i++) b.Cyl(new Vector3(-0.08f + i * 0.08f, 0.7f, -0.027f), new Vector3(-0.08f + i * 0.08f, 0.7f, 0.027f), 0.013f, Acc, Brass, 8);
            b.Hook(new Vector2(-0.02f, 0.95f), 1);
        }

        // hacksaw: a frame in the tool's colour, the blade between the ends, a pistol grip at one end
        static void Hacksaw(ToolMesh b)
        {
            b.Box(new Vector3(0.12f, 0.52f, 0), new Vector3(0.035f, 0.82f, 0.03f), Own, White);                       // back of the frame
            b.Box(new Vector3(0.05f, 0.92f, 0), new Vector3(0.17f, 0.035f, 0.03f), Own, White);
            b.Box(new Vector3(0.05f, 0.12f, 0), new Vector3(0.17f, 0.035f, 0.03f), Own, White);
            b.Box(new Vector3(-0.03f, 0.52f, 0), new Vector3(0.025f, 0.82f, 0.003f), Tin, White);                     // blade
            b.Cyl(new Vector3(-0.03f, 0.95f, 0), new Vector3(-0.03f, 1f, 0), 0.02f, Tin, White, 8);                    // tension knob
            b.Plate(new[] { new Vector2(-0.05f, 0.1f), new Vector2(0.14f, 0.1f), new Vector2(0.12f, -0.0f), new Vector2(0.05f, -0.02f), new Vector2(-0.02f, 0.0f) }, -0.026f, 0.026f, Acc, Rubber);
            b.Hook(new Vector2(0.05f, 0.92f), 1);
            b.Hook(new Vector2(0.05f, 0.12f), 1);
        }

        static void FileTool(ToolMesh b)
        {
            Handle(b, 0.66f, 1f, 0.05f, Acc, Wood, false);
            b.Cyl(new Vector3(0, 0.64f, 0), new Vector3(0, 0.68f, 0), 0.054f, Tin, Brass, 10);
            b.Plate(new[] { new Vector2(-0.04f, 0.62f), new Vector2(0.04f, 0.62f), new Vector2(0.04f, 0.15f), new Vector2(0.015f, 0.0f), new Vector2(-0.015f, 0.0f), new Vector2(-0.04f, 0.15f) }, -0.012f, 0.012f, Own, White);
            for (int i = 0; i < 22; i++) b.Box(new Vector3(0, 0.05f + i * 0.025f, -0.0125f), new Vector3(0.07f, 0.004f, 0.001f), Own, DarkSteel, 25);
            b.Hook(new Vector2(0, 0.64f), 2);
        }

        // wood chisel: the handle (own colour) with a steel hoop, a bevelled blade
        static void WoodChisel(ToolMesh b)
        {
            Handle(b, 0.56f, 1f, 0.043f, Own, White, false);
            b.Cyl(new Vector3(0, 0.965f, 0), new Vector3(0, 1f, 0), 0.036f, Tin, White, 12);
            b.Cone(new Vector3(0, 0.5f, 0), new Vector3(0, 0.58f, 0), 0.018f, 0.034f, Tin, White, 10);
            b.Plate(new[] { new Vector2(-0.022f, 0.51f), new Vector2(0.022f, 0.51f), new Vector2(0.034f, 0.03f), new Vector2(0.034f, 0), new Vector2(-0.034f, 0), new Vector2(-0.034f, 0.03f) }, -0.008f, 0.006f, Tin, Chrome);
            b.Hook(new Vector2(0, 0.54f), 2);
        }

        // cold chisel: one octagonal steel bar painted the tool's colour, bright at the edge
        static void ColdChisel(ToolMesh b)
        {
            b.Cyl(new Vector3(0, 0.12f, 0), new Vector3(0, 0.95f, 0), 0.06f, Own, White, 8);
            b.Cone(new Vector3(0, 0.95f, 0), new Vector3(0, 1f, 0), 0.06f, 0.045f, Own, White, 8);
            b.Plate(new[] { new Vector2(-0.055f, 0.12f), new Vector2(0.055f, 0.12f), new Vector2(0.065f, 0f), new Vector2(-0.065f, 0f) }, -0.03f, 0.03f, Tin, Chrome);
            b.Hook(new Vector2(0, 0.6f), 2);
        }

        static void Scraper(ToolMesh b)
        {
            Handle(b, 0.45f, 1f, 0.065f, Own, White, false);
            b.Ring(new Vector2(0, 0.94f), 0.03f, 0.018f, -0.06f, 0.06f, Own, White, 10);
            b.Strip(new Vector2(0, 0.46f), new Vector2(0, 0.12f), 0.04f, 0.06f, -0.006f, 0.006f, Tin, White);
            b.Plate(new[] { new Vector2(-0.1f, 0.14f), new Vector2(0.1f, 0.14f), new Vector2(0.11f, 0), new Vector2(-0.11f, 0) }, -0.003f, 0.003f, Tin, Chrome);
            b.Hook(new Vector2(0, 0.94f));
        }

        static void NailPunch(ToolMesh b)
        {
            b.Cyl(new Vector3(0, 0.3f, 0), new Vector3(0, 1f, 0), 0.085f, Own, White, 10);
            for (int i = 0; i < 9; i++) b.Cyl(new Vector3(0, 0.4f + i * 0.06f, 0), new Vector3(0, 0.42f + i * 0.06f, 0), 0.09f, Own, DarkSteel, 10);
            b.Cone(new Vector3(0, 0.3f, 0), new Vector3(0, 0.05f, 0), 0.085f, 0.03f, Own, White, 10);
            b.Cyl(new Vector3(0, 0.05f, 0), new Vector3(0, 0f, 0), 0.02f, Own, White, 8);
            b.Hook(new Vector2(0, 0.7f), 2);
        }
    }
}
