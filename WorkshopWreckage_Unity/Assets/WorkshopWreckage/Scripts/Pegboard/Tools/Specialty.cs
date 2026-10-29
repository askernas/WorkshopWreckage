using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage.Pegboard
{
    // Clamps: F-clamp, C-clamp, bar clamp, quick-grip clamp. The painted parts are the tool's own colour.
    public static partial class ToolFactory
    {
        static void RegisterSpecialty()
        {
            Add("f_clamp", FClamp);
            Add("c_clamp", CClamp);
            Add("bar_clamp", BarClamp);
            Add("quick_clamp", QuickClamp);
        }

        // F-clamp: a steel bar, a fixed jaw at the top, a sliding jaw with screw and wooden handle below
        static void FClamp(ToolMesh b)
        {
            b.Box(new Vector3(-0.15f, 0.497f, 0), new Vector3(0.05f, 0.994f, 0.016f), Tin, White);   // the bar's top stays inside the fixed jaw (flush tops z-fought)
            b.Plate(new[] { new Vector2(-0.18f, 1f), new Vector2(0.2f, 1f), new Vector2(0.2f, 0.94f), new Vector2(-0.1f, 0.9f), new Vector2(-0.18f, 0.9f) }, -0.03f, 0.03f, Own, White);
            b.Plate(new[] { new Vector2(-0.18f, 0.58f), new Vector2(0.2f, 0.62f), new Vector2(0.2f, 0.68f), new Vector2(-0.1f, 0.7f), new Vector2(-0.18f, 0.7f) }, -0.03f, 0.03f, Own, White);
            b.Cyl(new Vector3(0.12f, 0.62f, 0), new Vector3(0.12f, 0.88f, 0), 0.015f, Tin, White, 8);
            b.Cyl(new Vector3(0.12f, 0.88f, 0), new Vector3(0.12f, 0.92f, 0), 0.035f, Tin, White, 10);
            b.Push(new Vector3(0.12f, 0, 0)); Handle(b, 0.36f, 0.58f, 0.04f, Acc, Wood, false); b.Pop();
            b.Hook(new Vector2(-0.15f, 0.95f), 1);
        }

        // C-clamp: a cast C frame with the screw through the lower arm and a sliding T bar
        static void CClamp(ToolMesh b)
        {
            var outer = new List<Vector2>(ToolMesh.Arc(new Vector2(0.1f, 0.55f), 0.45f, Mathf.PI * 0.5f, Mathf.PI * 1.5f, 16));
            var inner = new List<Vector2>(ToolMesh.Arc(new Vector2(0.1f, 0.55f), 0.32f, Mathf.PI * 1.5f, Mathf.PI * 0.5f, 16));
            outer.Add(new Vector2(0.25f, 0.1f)); outer.Add(new Vector2(0.25f, 0.23f));
            outer.AddRange(inner);
            outer.Add(new Vector2(0.25f, 0.87f)); outer.Add(new Vector2(0.25f, 1f));
            b.Plate(outer, -0.04f, 0.04f, Own, White);
            b.Box(new Vector3(-0.27f, 0.55f, 0), new Vector3(0.05f, 0.5f, 0.1f), Own, White);   // rib
            b.Cyl(new Vector3(0.22f, -0.0f, 0), new Vector3(0.22f, 0.72f, 0), 0.03f, Tin, White, 8);
            b.Cyl(new Vector3(0.22f, 0.72f, 0), new Vector3(0.22f, 0.76f, 0), 0.06f, Tin, White, 10);
            b.Cyl(new Vector3(0.08f, 0.02f, 0), new Vector3(0.36f, 0.02f, 0), 0.02f, Tin, White, 8);
            b.Hook(new Vector2(-0.12f, 0.92f), 1);
        }

        // bar clamp: a long steel bar with a fixed and a sliding jaw (own colour) and black pads
        static void BarClamp(ToolMesh b)
        {
            b.Box(new Vector3(0, 0.5f, 0), new Vector3(0.04f, 1f, 0.012f), Tin, White);
            b.Box(new Vector3(0.09f, 0.95f, 0), new Vector3(0.2f, 0.08f, 0.05f), Own, White);
            b.Box(new Vector3(0.09f, 0.55f, 0), new Vector3(0.2f, 0.08f, 0.05f), Own, White);
            b.Box(new Vector3(0.18f, 0.905f, 0), new Vector3(0.05f, 0.012f, 0.05f), Acc, Rubber);
            b.Box(new Vector3(0.18f, 0.595f, 0), new Vector3(0.05f, 0.012f, 0.05f), Acc, Rubber);
            b.Box(new Vector3(0.02f, 0.45f, 0), new Vector3(0.1f, 0.14f, 0.05f), Own, White);
            b.Box(new Vector3(0.08f, 0.38f, 0), new Vector3(0.04f, 0.12f, 0.03f), Acc, Rubber);
            b.Hook(new Vector2(0, 0.98f), 1);
        }

        // quick-grip clamp: a plastic body and pistol grip in the tool's colour, a bar and two black pads
        static void QuickClamp(ToolMesh b)
        {
            b.Box(new Vector3(0, 0.5f, 0), new Vector3(0.05f, 1f, 0.014f), Tin, White);
            b.Box(new Vector3(0.12f, 0.96f, 0), new Vector3(0.22f, 0.07f, 0.06f), Own, White);
            b.Box(new Vector3(0.22f, 0.92f, 0), new Vector3(0.07f, 0.03f, 0.08f), Acc, Rubber);
            b.Plate(new[] { new Vector2(-0.04f, 0.6f), new Vector2(0.3f, 0.62f), new Vector2(0.3f, 0.58f), new Vector2(0.2f, 0.52f), new Vector2(0.24f, 0.28f), new Vector2(0.14f, 0.28f), new Vector2(0.08f, 0.48f), new Vector2(-0.04f, 0.48f) }, -0.035f, 0.035f, Own, White);
            b.Box(new Vector3(0.22f, 0.65f, 0), new Vector3(0.07f, 0.03f, 0.08f), Acc, Rubber);
            b.Strip(new Vector2(0.05f, 0.47f), new Vector2(0.12f, 0.3f), 0.04f, 0.04f, -0.025f, 0.025f, Acc, Rubber);   // trigger
            b.Hook(new Vector2(0, 0.98f), 1);
        }
    }
}
