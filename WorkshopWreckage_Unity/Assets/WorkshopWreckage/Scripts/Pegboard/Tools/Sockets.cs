using UnityEngine;

namespace WorkshopWreckage.Pegboard
{
    // Socket rails, ratchet, extension and T-bars, the bit set, the spark plug wrench.
    public static partial class ToolFactory
    {
        static void RegisterSockets()
        {
            Add("socket_rail", SocketRail);
            Add("ratchet", Ratchet);
            Add("extension_bar", ExtensionBar);
            Add("t_bar", TBar);
            Add("t_handle", THandle);
            Add("bit_set", BitSet);
            Add("spark_plug_wrench", SparkPlugWrench);
        }

        // a rail (its own colour) with ten graduated sockets standing on it, small at one end. It hangs turned a
        // quarter (slot angle 90): the rail runs along the board and the sockets stand up off it (+X).
        static void SocketRail(ToolMesh b)
        {
            b.Box(new Vector3(-0.012f, 0.5f, 0.0f), new Vector3(0.025f, 1f, 0.06f), Own, White);
            b.Box(new Vector3(0.006f, 0.5f, 0.0f), new Vector3(0.012f, 0.98f, 0.022f), Tin, DarkSteel);   // the clip strip
            float y = 0.03f;
            for (int i = 0; i < 10; i++)
            {
                float r = 0.02f + i * 0.0032f, h = 0.075f + i * 0.004f;
                y += r;
                b.Cyl(new Vector3(0.012f, y, 0), new Vector3(0.012f + h, y, 0), r, Tin, Chrome, 12);
                b.Cyl(new Vector3(0.012f + h * 0.55f, y, 0), new Vector3(0.012f + h * 0.62f, y, 0), r * 1.04f, Tin, Steel, 12);   // the size band
                b.Cyl(new Vector3(0.012f + h, y, 0), new Vector3(0.0125f + h, y, 0), r * 0.62f, Acc, Rubber, 6);   // the drive hole
                y += r + 0.01f;
            }
            b.Hook(new Vector2(-0.012f, 0.06f), 2);
            b.Hook(new Vector2(-0.012f, 0.94f), 2);
        }

        static void Ratchet(ToolMesh b)
        {
            Handle(b, 0.0f, 0.55f, 0.045f, Acc, Red, false);
            b.Lathe(new Vector3(0, 0.08f, 0), new[] { new Vector2(0.05f, 0), new Vector2(0.052f, 0.02f), new Vector2(0.052f, 0.2f), new Vector2(0.05f, 0.22f) }, Acc, Rubber, 12);   // grip band
            b.Strip(new Vector2(0, 0.5f), new Vector2(0, 0.84f), 0.06f, 0.075f, -0.016f, 0.016f, Own, White);
            b.Cyl(new Vector3(0, 0.9f, -0.03f), new Vector3(0, 0.9f, 0.03f), 0.1f, Own, White, 18);
            b.Cyl(new Vector3(0, 0.9f, -0.035f), new Vector3(0, 0.9f, -0.03f), 0.06f, Tin, DarkSteel, 12);
            b.Box(new Vector3(0.0f, 0.9f, 0.05f), new Vector3(0.04f, 0.04f, 0.04f), Tin, White);
            b.Box(new Vector3(0.06f, 0.83f, -0.032f), new Vector3(0.05f, 0.025f, 0.01f), Tin, White, 30);
            b.Hook(new Vector2(0, 0.04f), 0);
        }

        static void ExtensionBar(ToolMesh b)
        {
            b.Cyl(new Vector3(0, 0.06f, 0), new Vector3(0, 0.94f, 0), 0.03f, Own, White, 10);
            b.Cyl(new Vector3(0, 0.88f, 0), new Vector3(0, 1f, 0), 0.055f, Own, White, 12);
            b.Box(new Vector3(0, 0.03f, 0), new Vector3(0.05f, 0.06f, 0.05f), Own, White);
            b.Hook(new Vector2(0, 0.85f), 2);
        }

        static void TBar(ToolMesh b)
        {
            b.Cyl(new Vector3(0, 0f, 0), new Vector3(0, 1f, 0), 0.028f, Own, White, 10);
            b.Box(new Vector3(0, 0.5f, 0), new Vector3(0.06f, 0.1f, 0.06f), Own, White);
            b.Cyl(new Vector3(0, 0.5f, 0), new Vector3(0, 0.5f, 0.09f), 0.022f, Tin, White, 8);
            b.Hook(new Vector2(0, 0.3f), 2);
            b.Hook(new Vector2(0, 0.7f), 2);
        }

        // T-handle hex driver: the cross handle is the tool's colour
        static void THandle(ToolMesh b)
        {
            b.Cyl(new Vector3(0, 0f, 0), new Vector3(0, 0.86f, 0), 0.03f, Tin, White, 8);
            b.Push(new Vector3(-0.32f, 0.92f, 0), -90);
            b.Lathe(Vector3.zero, new[] { new Vector2(0.045f, 0), new Vector2(0.065f, 0.05f), new Vector2(0.075f, 0.32f), new Vector2(0.065f, 0.59f), new Vector2(0.045f, 0.64f) }, Own, White, 12);
            b.Pop();
            b.Hook(new Vector2(-0.12f, 0.84f), 2);
            b.Hook(new Vector2(0.12f, 0.84f), 2);
        }

        // bit set: a base bar (own colour) with two rows of hex bits standing up off it (hangs turned a quarter,
        // like the socket rails)
        static void BitSet(ToolMesh b)
        {
            b.Box(new Vector3(-0.03f, 0.5f, 0.0f), new Vector3(0.07f, 1f, 0.11f), Own, White);
            b.Box(new Vector3(0.01f, 0.5f, 0.0f), new Vector3(0.012f, 0.97f, 0.1f), Acc, Rubber);
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < 16; i++)
                {
                    float y = 0.05f + i * 0.06f, z = row == 0 ? -0.028f : 0.022f, h = row == 0 ? 0.16f : 0.21f;
                    b.Cyl(new Vector3(0.015f, y, z), new Vector3(0.015f + h, y, z), 0.014f, Tin, (i + row) % 3 == 0 ? DarkSteel : Chrome, 6);
                }
            b.Hook(new Vector2(-0.03f, 0.08f), 2);
            b.Hook(new Vector2(-0.03f, 0.92f), 2);
        }

        static void SparkPlugWrench(ToolMesh b)
        {
            b.Cyl(new Vector3(0, 0f, 0), new Vector3(0, 0.75f, 0), 0.085f, Own, White, 6);
            b.Cyl(new Vector3(0, 0.75f, 0), new Vector3(0, 0.82f, 0), 0.06f, Own, White, 12);
            b.Cyl(new Vector3(-0.45f, 0.88f, 0), new Vector3(0.45f, 0.88f, 0), 0.035f, Tin, White, 10);
            b.Hook(new Vector2(-0.2f, 0.85f), 2);
            b.Hook(new Vector2(0.2f, 0.85f), 2);
        }
    }
}
