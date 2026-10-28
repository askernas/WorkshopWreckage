using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage.Pegboard
{
    // Pliers: two steel halves crossing at a rivet, jaws up, the grips in the tool's own colour. They hang on
    // a peg between the handles, under the rivet.
    public static partial class ToolFactory
    {
        static void RegisterPliers()
        {
            Add("pliers_combination", b => Pliers(b, 0.6f, 0.11f, 0.075f, 0.085f, false));
            Add("pliers_needle", b => Pliers(b, 0.5f, 0.06f, 0.03f, 0.08f, false));
            Add("pliers_slip_joint", b => Pliers(b, 0.7f, 0.12f, 0.07f, 0.1f, true));
            Add("pliers_locking", LockingPliers);
        }

        /// <summary>pivot = height of the rivet; jawW / jawTip = width of the closed jaws at the rivet / tip;
        /// spread = how far the handle ends stand apart; angled = water-pump style jaws bent to one side.</summary>
        static void Pliers(ToolMesh b, float pivot, float jawW, float jawTip, float spread, bool angled)
        {
            const float t = 0.016f;
            for (int side = -1; side <= 1; side += 2)
            {
                float z0 = side < 0 ? -t : 0, z1 = side < 0 ? 0 : t;
                // jaw half: from the rivet to the tip, on this side of the centre line
                float bend = angled ? 0.12f : 0;
                var jaw = new List<Vector2>
                {
                    new(0, pivot - 0.04f), new(side * jawW * 0.55f, pivot), new(side * jawW * 0.5f + bend, pivot + (1 - pivot) * 0.55f),
                    new(side * jawTip * 0.5f + bend * 1.4f, 1f), new(bend * 1.4f, 1f), new(bend, pivot + (1 - pivot) * 0.5f), new(0, pivot + 0.04f),
                };
                b.Plate(jaw, z0, z1, Tin, White);
                // the handle runs down on the opposite side (the halves cross at the rivet)
                var a = new Vector2(0, pivot); var mid = new Vector2(-side * spread * 0.55f, pivot * 0.55f); var end = new Vector2(-side * spread, 0.02f);
                b.Strip(a, mid, 0.05f, 0.04f, z0, z1, Tin, White);
                b.Strip(mid, end, 0.04f, 0.036f, z0, z1, Tin, White);
                // dipped grip in the tool's colour, slightly fatter than the steel
                var g0 = mid + (a - mid) * 0.35f;
                b.Strip(g0, mid, 0.058f, 0.054f, z0 - (side < 0 ? 0.006f : 0), z1 + (side > 0 ? 0.006f : 0), Own, White);
                b.Strip(mid, end, 0.054f, 0.05f, z0 - (side < 0 ? 0.006f : 0), z1 + (side > 0 ? 0.006f : 0), Own, White);
            }
            b.Cyl(new Vector3(0, pivot, -0.024f), new Vector3(0, pivot, 0.024f), 0.028f, Tin, White, 12);   // rivet
            Rivet(b, new Vector3(0, pivot, 0), 0.024f, 0.03f);
            if (angled)
                for (int i = 0; i < 4; i++)
                    b.Box(new Vector3(0.06f, pivot - 0.03f + i * 0.02f, -0.018f), new Vector3(0.05f, 0.008f, 0.006f), Tin, DarkSteel);
            b.Hook(new Vector2(0, pivot * 0.62f));
        }

        // locking pliers: a fixed handle with the adjusting screw, a curved lever with the release
        static void LockingPliers(ToolMesh b)
        {
            const float t = 0.02f;
            b.Plate(new[] { new Vector2(-0.02f, 1f), new Vector2(0.03f, 0.98f), new Vector2(0.04f, 0.8f), new Vector2(-0.03f, 0.74f) }, -t, t, Tin, White);   // fixed jaw
            b.Plate(new[] { new Vector2(0.04f, 1f), new Vector2(0.09f, 0.97f), new Vector2(0.11f, 0.8f), new Vector2(0.05f, 0.82f) }, -t, t, Tin, White);    // moving jaw
            b.Strip(new Vector2(0, 0.76f), new Vector2(-0.04f, 0.12f), 0.07f, 0.055f, -t, t, Own, White);                                                     // fixed handle
            b.Cyl(new Vector3(-0.04f, 0.13f, 0), new Vector3(-0.045f, 0f, 0), 0.022f, Tin, White, 10);                                                      // screw
            b.Cyl(new Vector3(-0.045f, 0.0f, 0), new Vector3(-0.045f, 0.035f, 0), 0.034f, Tin, DarkSteel, 12);                                              // knob
            b.Strip(new Vector2(0.08f, 0.82f), new Vector2(0.12f, 0.5f), 0.06f, 0.05f, -t * 0.8f, t * 0.8f, Own, White);                                    // lever
            b.Strip(new Vector2(0.12f, 0.5f), new Vector2(0.06f, 0.2f), 0.05f, 0.04f, -t * 0.8f, t * 0.8f, Own, White);
            b.Strip(new Vector2(0.08f, 0.4f), new Vector2(0.0f, 0.3f), 0.03f, 0.03f, -t * 1.2f, -t * 0.8f, Tin, White);                                     // release
            b.Cyl(new Vector3(0.05f, 0.8f, -0.03f), new Vector3(0.05f, 0.8f, 0.03f), 0.02f, Tin, White, 10);
            Rivet(b, new Vector3(0.05f, 0.8f, 0), 0.03f, 0.022f);
            Rivet(b, new Vector3(0.12f, 0.5f, 0), t * 0.8f, 0.016f);   // the lever's link pin
            b.Hook(new Vector2(0.02f, 0.45f));
        }

        /// <summary>Peened rivet heads on both faces of a joint: a low steel dome with a ground ring round it.</summary>
        static void Rivet(ToolMesh b, Vector3 at, float halfT, float r)
        {
            foreach (float s in new[] { -1f, 1f })
            {
                b.Push(Matrix4x4.TRS(at + new Vector3(0, 0, s * halfT), Quaternion.Euler(s * 90, 0, 0), Vector3.one));
                b.Lathe(Vector3.zero, new[] { new Vector2(r * 1.15f, -0.001f), new Vector2(r * 1.15f, 0.002f), new Vector2(r * 0.95f, 0.004f), new Vector2(r * 0.6f, 0.0075f), new Vector2(0, 0.009f) }, Tin, Chrome, 14);
                b.Pop();
            }
        }
    }
}
