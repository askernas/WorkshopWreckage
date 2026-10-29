using UnityEngine;

namespace WorkshopWreckage.Pegboard
{
    // Measuring tools: spirit level, try square, caliper, tape measure, steel rule.
    public static partial class ToolFactory
    {
        static void RegisterMeasuring()
        {
            Add("spirit_level", SpiritLevel);
            Add("try_square", TrySquare);
            Add("caliper", Caliper);
            Add("tape_measure", TapeMeasure);
            Add("steel_rule", SteelRule);
        }

        // an aluminium body in the tool's colour, three vials, rubber end caps; hangs from its end hole
        static void SpiritLevel(ToolMesh b)
        {
            b.Box(new Vector3(0, 0.5f, 0), new Vector3(0.11f, 0.94f, 0.04f), Own, White);
            b.Box(new Vector3(0, 0.5f, -0.021f), new Vector3(0.08f, 0.9f, 0.003f), Own, White);
            foreach (float y in new[] { 0.03f, 0.97f })
                b.Box(new Vector3(0, y, 0), new Vector3(0.118f, 0.06f, 0.046f), Acc, Rubber);
            b.Box(new Vector3(0, 0.5f, -0.024f), new Vector3(0.06f, 0.12f, 0.006f), Acc, DarkSteel);
            b.Cyl(new Vector3(0, 0.46f, -0.026f), new Vector3(0, 0.54f, -0.026f), 0.012f, Acc, Vial, 8);
            foreach (float y in new[] { 0.2f, 0.8f })
            {
                b.Ring(new Vector2(0, y), 0.032f, 0.022f, -0.026f, -0.02f, Acc, DarkSteel, 12);
                b.Cyl(new Vector3(-0.02f, y, -0.023f), new Vector3(0.02f, y, -0.023f), 0.01f, Acc, Vial, 8);
            }
            b.Ring(new Vector2(0, 0.9f), 0.028f, 0.016f, -0.023f, 0.023f, Acc, Rubber, 10);
            b.Hook(new Vector2(0, 0.9f));
        }

        // steel blade (own) in a wooden stock with brass rivets; the stock is the short leg at the top
        static void TrySquare(ToolMesh b)
        {
            b.Box(new Vector3(0.06f, 0.5f, 0), new Vector3(0.11f, 1f, 0.006f), Own, White);
            b.Box(new Vector3(-0.08f, 0.92f, 0), new Vector3(0.5f, 0.15f, 0.04f), Acc, Wood);
            for (int i = 0; i < 3; i++) b.Cyl(new Vector3(-0.25f + i * 0.14f, 0.92f, -0.022f), new Vector3(-0.25f + i * 0.14f, 0.92f, 0.022f), 0.012f, Acc, Brass, 8);
            for (int i = 0; i < 18; i++) b.Box(new Vector3(0.11f - (i % 2 == 0 ? 0.01f : 0.005f), 0.04f + i * 0.045f, -0.004f), new Vector3(i % 2 == 0 ? 0.02f : 0.01f, 0.004f, 0.002f), Acc, Rubber);
            b.Hook(new Vector2(-0.2f, 0.92f), 2);
            b.Hook(new Vector2(0.03f, 0.92f), 2);
        }

        // vernier caliper: the beam is the length, the jaws stick out to the side at the top
        static void Caliper(ToolMesh b)
        {
            b.Box(new Vector3(0, 0.5f, 0), new Vector3(0.06f, 1f, 0.012f), Own, White);
            b.Plate(new[] { new Vector2(0.03f, 0.92f), new Vector2(0.24f, 0.98f), new Vector2(0.24f, 1f), new Vector2(0.03f, 1f) }, -0.006f, 0.006f, Own, White);   // fixed jaw
            b.Plate(new[] { new Vector2(0.03f, 0.74f), new Vector2(0.24f, 0.8f), new Vector2(0.24f, 0.82f), new Vector2(0.03f, 0.86f) }, -0.008f, 0.008f, Own, White);   // sliding jaw
            b.Box(new Vector3(0.0f, 0.79f, -0.004f), new Vector3(0.09f, 0.14f, 0.02f), Own, White);                                                              // slider
            b.Plate(new[] { new Vector2(-0.03f, 0.95f), new Vector2(-0.1f, 1f), new Vector2(-0.1f, 0.99f), new Vector2(-0.03f, 0.9f) }, -0.004f, 0.004f, Own, White);  // inside jaws
            b.Cyl(new Vector3(0.0f, 0.71f, -0.006f), new Vector3(0.0f, 0.71f, -0.02f), 0.012f, Tin, White, 8);
            for (int i = 0; i < 20; i++) b.Box(new Vector3(-0.022f, 0.06f + i * 0.04f, -0.0065f), new Vector3(i % 5 == 0 ? 0.016f : 0.008f, 0.003f, 0.001f), Acc, Rubber);
            b.Hook(new Vector2(0, 0.1f), 2);
        }

        // tape measure: a round case (own colour) with a black band, belt clip and the brass hook end
        static void TapeMeasure(ToolMesh b)
        {
            b.Cyl(new Vector3(0, 0.5f, -0.18f), new Vector3(0, 0.5f, 0.18f), 0.5f, Own, White, 22);
            b.Cyl(new Vector3(0, 0.5f, -0.19f), new Vector3(0, 0.5f, -0.18f), 0.3f, Acc, Rubber, 18);
            b.Cyl(new Vector3(0, 0.5f, -0.2f), new Vector3(0, 0.5f, -0.19f), 0.12f, Tin, Chrome, 12);
            b.Box(new Vector3(0.38f, 0.1f, 0), new Vector3(0.24f, 0.2f, 0.38f), Own, White);
            b.Box(new Vector3(0.52f, 0.06f, 0), new Vector3(0.06f, 0.06f, 0.3f), Acc, Yellow);
            b.Box(new Vector3(0.56f, 0.06f, 0), new Vector3(0.02f, 0.1f, 0.3f), Acc, Brass);
            b.Box(new Vector3(0.25f, 0.78f, -0.1f), new Vector3(0.16f, 0.12f, 0.1f), Acc, Rubber);   // lock button
            b.Box(new Vector3(0, 0.55f, 0.2f), new Vector3(0.14f, 0.6f, 0.03f), Tin, White);           // belt clip
            b.Hook(new Vector2(0, 0.85f), 1);
        }

        static void SteelRule(ToolMesh b)
        {
            b.Box(new Vector3(0, 0.5f, 0), new Vector3(0.055f, 1f, 0.004f), Own, White);
            for (int i = 0; i < 40; i++) b.Box(new Vector3(-0.02f + (i % 4 == 0 ? 0.004f : 0), 0.03f + i * 0.024f, -0.0025f), new Vector3(i % 4 == 0 ? 0.016f : 0.008f, 0.002f, 0.001f), Acc, Rubber);
            b.Ring(new Vector2(0, 0.97f), 0.012f, 0.007f, -0.0025f, 0.0025f, Own, White, 8);
            b.Hook(new Vector2(0, 0.97f));
        }
    }
}
