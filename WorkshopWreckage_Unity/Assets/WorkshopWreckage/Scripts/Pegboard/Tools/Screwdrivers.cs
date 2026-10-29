using UnityEngine;

namespace WorkshopWreckage.Pegboard
{
    // Screwdrivers and drivers: the handle is the tool's own colour, the shank is steel. They stand tip down in
    // their racks (or on two pins), so the hook is under the handle.
    public static partial class ToolFactory
    {
        static void RegisterScrewdrivers()
        {
            Add("screwdriver_flat", b => Screwdriver(b, 0.4f, 0.052f, 0.016f, 0));
            Add("screwdriver_phillips", b => Screwdriver(b, 0.4f, 0.052f, 0.016f, 1));
            Add("screwdriver_stubby", b => Screwdriver(b, 0.58f, 0.13f, 0.035f, 1));
            Add("screwdriver_stubby_flat", b => Screwdriver(b, 0.58f, 0.13f, 0.035f, 0));
            Add("screwdriver_precision", b => Precision(b, 0));
            Add("screwdriver_precision_wide", b => Precision(b, 1));
            Add("screwdriver_precision_phillips", b => Precision(b, 2));
            Add("screwdriver_precision_torx", b => Precision(b, 3));
            Add("nut_driver", b => Screwdriver(b, 0.42f, 0.05f, 0.02f, 2));
        }

        /// <summary>handle = the share of the length that is handle; tip 0 flat, 1 cross, 2 hex socket.</summary>
        static void Screwdriver(ToolMesh b, float handle, float r, float shankR, int tip)
        {
            float y0 = 1 - handle;
            Handle(b, y0, 1, r, Own, White);
            b.Cone(new Vector3(0, y0 - 0.035f, 0), new Vector3(0, y0 + 0.01f, 0), shankR * 1.5f, r * 0.62f, Tin, White);   // ferrule
            switch (tip)
            {
                case 0:
                    b.Cyl(new Vector3(0, 0.07f, 0), new Vector3(0, y0, 0), shankR, Tin, White, 10);
                    b.Plate(new[] { new Vector2(-shankR, 0.075f), new Vector2(shankR, 0.075f), new Vector2(shankR * 1.5f, 0.02f), new Vector2(shankR * 1.4f, 0), new Vector2(-shankR * 1.4f, 0), new Vector2(-shankR * 1.5f, 0.02f) }, -shankR * 0.35f, shankR * 0.35f, Tin, White);
                    break;
                case 1:
                    b.Cyl(new Vector3(0, 0.06f, 0), new Vector3(0, y0, 0), shankR, Tin, White, 10);
                    b.Cone(new Vector3(0, 0.06f, 0), new Vector3(0, 0, 0), shankR, shankR * 0.25f, Tin, White, 4);
                    break;
                default:
                    b.Cyl(new Vector3(0, 0.12f, 0), new Vector3(0, y0, 0), shankR, Tin, White, 10);
                    b.Cone(new Vector3(0, 0.12f, 0), new Vector3(0, 0.1f, 0), shankR, shankR * 1.7f, Tin, White, 6);
                    b.Cyl(new Vector3(0, 0, 0), new Vector3(0, 0.1f, 0), shankR * 1.7f, Tin, White, 6);
                    break;
            }
            b.Hook(new Vector2(0, y0 - 0.02f), 2);
        }

        // precision screwdriver: slim knurled handle with a swivel cap; tip 0 narrow flat, 1 wide flat, 2 cross,
        // 3 star (torx)
        static void Precision(ToolMesh b, int tip)
        {
            b.Lathe(new Vector3(0, 0.45f, 0), new[] { new Vector2(0.02f, 0), new Vector2(0.04f, 0.04f), new Vector2(0.042f, 0.46f), new Vector2(0.03f, 0.5f), new Vector2(0.0f, 0.5f) }, Own, White);
            for (int i = 0; i < 8; i++)
                b.Cyl(new Vector3(0, 0.52f + i * 0.045f, 0), new Vector3(0, 0.54f + i * 0.045f, 0), 0.046f, Tin, DarkSteel, 8);
            b.Cyl(new Vector3(0, 0.95f, 0), new Vector3(0, 1f, 0), 0.03f, Acc, Chrome, 10);
            b.Cyl(new Vector3(0, 0.05f, 0), new Vector3(0, 0.46f, 0), 0.012f, Tin, White, 8);
            switch (tip)
            {
                case 0: b.Plate(new[] { new Vector2(-0.012f, 0.05f), new Vector2(0.012f, 0.05f), new Vector2(0.008f, 0), new Vector2(-0.008f, 0) }, -0.003f, 0.003f, Tin, White); break;
                case 1: b.Plate(new[] { new Vector2(-0.012f, 0.06f), new Vector2(0.012f, 0.06f), new Vector2(0.02f, 0.012f), new Vector2(0.02f, 0), new Vector2(-0.02f, 0), new Vector2(-0.02f, 0.012f) }, -0.003f, 0.003f, Tin, White); break;
                case 2: b.Cone(new Vector3(0, 0.05f, 0), new Vector3(0, 0, 0), 0.012f, 0.004f, Tin, White, 4); break;
                default: b.Cone(new Vector3(0, 0.05f, 0), new Vector3(0, 0.008f, 0), 0.012f, 0.009f, Tin, White, 6); b.Cyl(new Vector3(0, 0, 0), new Vector3(0, 0.008f, 0), 0.009f, Tin, DarkSteel, 6); break;
            }
            b.Hook(new Vector2(0, 0.44f), 2);
        }
    }
}
