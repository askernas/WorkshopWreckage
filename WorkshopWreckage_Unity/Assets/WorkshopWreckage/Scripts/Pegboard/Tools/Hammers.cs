using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage.Pegboard
{
    // Hammers: the handle is the tool's own colour (wood, fibreglass), the head steel or rubber. They hang
    // head up on two pins just under the head.
    public static partial class ToolFactory
    {
        static void RegisterHammers()
        {
            Add("hammer_claw", ClawHammer);
            Add("hammer_ball_peen", BallPeen);
            Add("mallet", Mallet);
        }

        static void HammerHandle(ToolMesh b, float top, float r)
        {
            b.Lathe(Vector3.zero, new[] { new Vector2(r * 0.7f, 0), new Vector2(r * 1.15f, 0.04f), new Vector2(r * 1.1f, 0.3f), new Vector2(r * 0.8f, 0.55f), new Vector2(r * 0.75f, top - 0.05f), new Vector2(r * 0.9f, top) }, Own, White);
        }

        /// <summary>A forged hammer neck along -X from the eye: waisted, flaring into the bell, a bevelled striking face.</summary>
        static void HammerNeck(ToolMesh b, float y, float x0, float x1, float r)
        {
            float bell = x0 + (x1 - x0) * 0.72f, rim = x1 + 0.014f;
            b.Cone(new Vector3(x0, y, 0), new Vector3(bell, y, 0), r * 0.84f, r * 0.9f, Tin, White, 14);
            b.Cone(new Vector3(bell, y, 0), new Vector3(rim + 0.012f, y, 0), r * 0.9f, r * 1.1f, Tin, White, 14, false);   // no caps: they doubled the neighbours' end discs
            b.Cyl(new Vector3(rim + 0.012f, y, 0), new Vector3(rim, y, 0), r * 1.1f, Tin, White, 14);
            b.Cone(new Vector3(rim, y, 0), new Vector3(x1, y, 0), r * 1.1f, r * 0.96f, Tin, Chrome, 14);   // ground face
        }

        /// <summary>The handle's end showing in the top of the eye, split by a steel wedge.</summary>
        static void HammerWedge(ToolMesh b, float y, float w)
        {
            b.Box(new Vector3(0, y, 0), new Vector3(w, 0.006f, 0.03f), Own, White);
            b.Box(new Vector3(0, y + 0.002f, 0), new Vector3(w * 0.85f, 0.006f, 0.007f), Acc, DarkSteel);
        }

        static void ClawHammer(ToolMesh b)
        {
            HammerHandle(b, 0.9f, 0.045f);
            // head: the eye with its cheeks, the forged neck to the striking face, the curved split claw
            b.Box(new Vector3(0, 0.93f, 0), new Vector3(0.12f, 0.12f, 0.07f), Tin, White);
            HammerWedge(b, 0.99f, 0.07f);
            HammerNeck(b, 0.93f, -0.055f, -0.23f, 0.05f);
            var claw = new List<Vector2> { new(0.05f, 0.98f), new(0.15f, 0.97f), new(0.25f, 0.92f), new(0.31f, 0.84f), new(0.29f, 0.83f), new(0.22f, 0.88f), new(0.12f, 0.9f), new(0.05f, 0.88f) };
            b.Plate(claw, -0.028f, -0.004f, Tin, White);
            b.Plate(claw, 0.004f, 0.028f, Tin, White);
            // the web joining the two claw tines at their root, the V of the split behind it
            b.Plate(new List<Vector2> { new(0.05f, 0.975f), new(0.13f, 0.968f), new(0.12f, 0.905f), new(0.05f, 0.885f) }, -0.006f, 0.006f, Tin, White);
            b.Hook(new Vector2(-0.07f, 0.86f), 2);
            b.Hook(new Vector2(0.07f, 0.86f), 2);
        }

        static void BallPeen(ToolMesh b)
        {
            HammerHandle(b, 0.86f, 0.04f);
            b.Box(new Vector3(0, 0.9f, 0), new Vector3(0.09f, 0.1f, 0.065f), Tin, White);
            HammerWedge(b, 0.95f, 0.055f);
            HammerNeck(b, 0.9f, -0.04f, -0.22f, 0.046f);
            b.Cone(new Vector3(0.04f, 0.9f, 0), new Vector3(0.12f, 0.9f, 0), 0.04f, 0.035f, Tin, White, 12);
            b.Lathe(new Vector3(0.12f, 0.9f, 0), new[] { new Vector2(0.035f, 0), new Vector2(0.045f, 0) }, Tin, White, 12);
            // the ball: a short stack of cones along +X
            b.Push(new Vector3(0.12f, 0.9f, 0), -90);
            b.Lathe(Vector3.zero, new[] { new Vector2(0.03f, 0), new Vector2(0.05f, 0.03f), new Vector2(0.048f, 0.06f), new Vector2(0.03f, 0.085f), new Vector2(0, 0.095f) }, Tin, White, 12);
            b.Pop();
            b.Hook(new Vector2(-0.065f, 0.84f), 2);
            b.Hook(new Vector2(0.065f, 0.84f), 2);
        }

        // club hammer (the reference's yellow-and-black one): a short square head, yellow with dark faces, on a
        // wooden handle with a red and black grip
        static void Mallet(ToolMesh b)
        {
            HammerHandle(b, 0.8f, 0.042f);
            b.Lathe(Vector3.zero, new[] { new Vector2(0.05f, -0.003f), new Vector2(0.056f, 0.02f), new Vector2(0.056f, 0.14f), new Vector2(0.048f, 0.16f), new Vector2(0, 0.16f) }, Acc, Rubber, 12);   // the grip's end a hair past the handle's (flush ends z-fought)
            b.Lathe(new Vector3(0, 0.16f, 0), new[] { new Vector2(0.05f, 0), new Vector2(0.054f, 0.01f), new Vector2(0.054f, 0.12f), new Vector2(0.044f, 0.14f), new Vector2(0, 0.14f) }, Acc, Red, 12);
            b.Box(new Vector3(0, 0.88f, 0), new Vector3(0.3f, 0.12f, 0.11f), Acc, Yellow);
            b.Box(new Vector3(0, 0.88f, 0), new Vector3(0.12f, 0.135f, 0.125f), Acc, Rubber);   // the band round the eye
            foreach (float sx in new[] { -1f, 1f })
                b.Box(new Vector3(sx * 0.165f, 0.88f, 0), new Vector3(0.03f, 0.105f, 0.095f), Tin, DarkSteel);   // striking faces
            b.Hook(new Vector2(-0.07f, 0.8f), 2);
            b.Hook(new Vector2(0.07f, 0.8f), 2);
        }
    }
}
