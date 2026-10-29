using UnityEngine;

namespace WorkshopWreckage.Pegboard
{
    // The split tool sets (docs/PEGBOARD.md "Tool sets"): a holder that hangs on the board like any tool, and the
    // loose pieces that go on the holder once it is mounted, each on its own place (child slots). Every piece
    // has its own size and a colour-coded ring that matches a dot on the holder, the way real sets are marked.
    // The pieces are built at their real proportions; their size on the board comes from the definitions
    // (size_m, times the board's small-tool scale).
    public static partial class ToolFactory
    {
        /// <summary>Anchor kind: where a child slot's tool goes on its holder (not a peg on the board).</summary>
        public const float AnchorKind = 9;

        // metric sockets, 3/8" drive: size (mm), outer diameter and height (mm, real tools)
        static readonly float[] SocketMm = { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 };
        static readonly float[] SocketOd = { 15.9f, 17f, 18f, 19f, 20f, 21.5f, 22.5f, 24f, 25f, 26.5f };
        static readonly float[] SocketH = { 28, 28, 28, 28, 28.5f, 29, 29, 30, 30, 30 };
        /// <summary>The socket holder's length (mm, real): ten sockets side by side with a gap and the end caps.</summary>
        const float SocketGap = 8, SocketEnd = 19;
        // deep sockets (the old deep socket rail, split): the same ten sizes, 63 mm long, the drive end narrower
        // than the barrel on the larger sizes
        const float DeepH = 63, DeepDrive = 22;

        // 1/4" hex power bits, 50 mm (the old bit set, split): the tip types and sizes
        static readonly string[] BitTip = { "PH1", "PH2", "PH3", "PZ1", "PZ2", "PZ3", "SL4", "SL5.5", "SL6.5", "T10", "T15", "T20", "T25", "T30", "H4", "H5" };
        const float BitLen = 50, BitHex = 6.35f, BitPitch = 8, BitGap = 4.5f, BitEnd = 14;

        // hex keys, short series (ISO 2936): across flats, long arm and short arm (mm)
        static readonly float[] HexMm = { 1.5f, 2, 2.5f, 3, 4, 5, 6, 8 };
        static readonly float[] HexLong = { 46, 52, 58.5f, 63.5f, 70, 80, 90, 100 };
        static readonly float[] HexShort = { 15.5f, 18, 20.5f, 23, 25, 28, 32, 36 };
        const float HexGap = 9, HexEnd = 14;

        /// <summary>The colour code of a set's places (the same on the piece and on its holder).</summary>
        static readonly Color[] Code =
        {
            new(0.72f, 0.12f, 0.08f, 0), new(0.9f, 0.45f, 0.08f, 0), new(0.9f, 0.76f, 0.1f, 0), new(0.25f, 0.6f, 0.2f, 0), new(0.12f, 0.45f, 0.62f, 0),
            new(0.18f, 0.25f, 0.62f, 0), new(0.48f, 0.22f, 0.6f, 0), new(0.85f, 0.85f, 0.82f, 0), new(0.45f, 0.3f, 0.18f, 0), new(0.08f, 0.08f, 0.09f, 0),
        };

        static void RegisterSets()
        {
            Add("socket_holder", SocketHolder);
            for (int i = 0; i < SocketMm.Length; i++) { int k = i; Add($"metric_socket_{SocketMm[i]:0}", b => MetricSocket(b, k)); }
            Add("deep_socket_holder", b => SocketHolder(b, true));
            for (int i = 0; i < SocketMm.Length; i++) { int k = i; Add($"deep_socket_{SocketMm[i]:0}", b => DeepSocket(b, k)); }
            Add("bit_holder", BitHolder);
            for (int i = 0; i < BitTip.Length; i++) { int k = i; Add(BitModel(i), b => Bit(b, k)); }
            Add("hex_key_holder", HexKeyHolder);
            for (int i = 0; i < HexMm.Length; i++) { int k = i; Add($"hex_key_{HexMm[i] * 10:0}", b => HexKey(b, k)); }
        }

        static float SocketRailMm()
        {
            float l = 2 * SocketEnd + SocketGap * (SocketMm.Length - 1);
            foreach (var d in SocketOd) l += d;
            return l;
        }

        static void SocketHolder(ToolMesh b) => SocketHolder(b, false);

        // the socket holder: a rail (its own colour) with a clip strip and ten studs, a colour dot and a stamped
        // size plate at each; it hangs turned a quarter (slot angle 90), so the studs (+X) point up and the
        // sockets stand on them. The deep holder adds a back fence the long sockets stand against, and a pale
        // band at each end, so the two holders are told apart at a glance.
        static void SocketHolder(ToolMesh b, bool deep)
        {
            float len = SocketRailMm(), u = 1 / len;
            b.Box(new Vector3(-0.012f, 0.5f, 0), new Vector3(0.025f, 1f, 0.06f), Own, White);
            b.Box(new Vector3(0.004f, 0.5f, 0), new Vector3(0.008f, 0.985f, 0.03f), Tin, DarkSteel);   // the clip strip
            b.Hook(new Vector2(-0.012f, 0.07f), 2);
            b.Hook(new Vector2(-0.012f, 0.93f), 2);
            if (deep)
            {
                b.Box(new Vector3(0.05f, 0.5f, 0.026f), new Vector3(0.09f, 0.97f, 0.008f), Own, White);   // the back fence
                b.Box(new Vector3(-0.012f, 0.025f, -0.031f), new Vector3(0.024f, 0.03f, 0.002f), Acc, Paper);
                b.Box(new Vector3(-0.012f, 0.975f, -0.031f), new Vector3(0.024f, 0.03f, 0.002f), Acc, Paper);
            }
            float y = SocketEnd;
            for (int i = 0; i < SocketMm.Length; i++)
            {
                float c = (y + SocketOd[i] / 2) * u, sq = 9.5f * u;   // 3/8" square stud
                b.Box(new Vector3(0.008f + 5 * u, c, 0), new Vector3(10 * u, sq, sq), Tin, Chrome);
                b.Box(new Vector3(-0.012f, c, -0.0305f), new Vector3(0.018f, SocketOd[i] * u * 0.55f, 0.002f), Acc, Code[i]);   // the colour dot on the rail's face
                b.Anchor(new Vector3(0.008f, c, 0));   // the socket's drive end sits here
                y += SocketOd[i] + SocketGap;
            }
        }

        // a metric socket: a chrome barrel (its diameter for its size), the hex opening on top, the square drive
        // underneath, a knurled band and a ring in the size's colour
        static void MetricSocket(ToolMesh b, int i)
        {
            float h = SocketH[i], r = SocketOd[i] / 2 / h, drive = 0.38f;
            if (i < 4) b.Cyl(new Vector3(0, 0, 0), new Vector3(0, drive, 0), r * 0.9f, Own, White, 16);   // drive end (narrower on the small sizes)
            b.Cyl(new Vector3(0, i < 4 ? drive : 0, 0), new Vector3(0, 1, 0), r, Own, White, 16);   // one barrel on the big sizes: two equal ones left a doubled disc inside
            b.Cyl(new Vector3(0, 0.62f, 0), new Vector3(0, 0.74f, 0), r * 1.03f, Acc, Code[i], 16);   // the size ring
            b.Cyl(new Vector3(0, 0.08f, 0), new Vector3(0, 0.2f, 0), r * (i < 4 ? 0.93f : 1.03f), Tin, Steel, 12);   // knurl band
            b.Cyl(new Vector3(0, 0.995f, 0), new Vector3(0, 1.007f, 0), SocketMm[i] / 2 / h * 1.15f, Acc, Rubber, 6);   // the hex opening (0.007 proud: 0.002 z-fought at range)
            b.Cyl(new Vector3(0, -0.007f, 0), new Vector3(0, 0.005f, 0), 9.5f / 2 / h * 1.1f, Acc, Rubber, 4);   // the square drive
            b.Hook(new Vector2(0, 0.02f), 2);
        }

        // a deep socket: the drive end (narrower than the barrel on the big sizes), a long chrome barrel, the size
        // ring in the size's colour high up, a knurl band and a groove half way, the hex opening on top
        static void DeepSocket(ToolMesh b, int i)
        {
            float h = DeepH, r = SocketOd[i] / 2 / h, rd = Mathf.Min(SocketOd[i], DeepDrive) / 2 / h, drive = 0.22f;
            b.Cyl(new Vector3(0, 0, 0), new Vector3(0, drive, 0), rd, Own, White, 16);   // drive end
            if (r > rd + 0.001f) b.Cone(new Vector3(0, drive, 0), new Vector3(0, drive + 0.05f, 0), rd, r, Own, White, 16, false);
            b.Cyl(new Vector3(0, drive + 0.04f, 0), new Vector3(0, 1, 0), r, Own, White, 16);
            b.Cyl(new Vector3(0, 0.78f, 0), new Vector3(0, 0.84f, 0), r * 1.03f, Acc, Code[i], 16);   // the size ring
            b.Cyl(new Vector3(0, 0.04f, 0), new Vector3(0, 0.12f, 0), rd * 1.03f, Tin, Steel, 12);   // knurl band
            b.Cyl(new Vector3(0, 0.5f, 0), new Vector3(0, 0.52f, 0), r * 0.97f, Tin, Steel, 16);   // the groove half way
            b.Cyl(new Vector3(0, 0.995f, 0), new Vector3(0, 1.007f, 0), SocketMm[i] / 2 / h * 1.15f, Acc, Rubber, 6);   // the hex opening (0.007 proud: 0.002 z-fought at range)
            b.Cyl(new Vector3(0, -0.007f, 0), new Vector3(0, 0.005f, 0), 9.5f / 2 / h * 1.1f, Acc, Rubber, 4);   // the square drive
            b.Hook(new Vector2(0, 0.01f), 2);
        }

        /// <summary>"PH2" -> "bit_ph2", "SL5.5" -> "bit_sl55".</summary>
        static string BitModel(int i) => "bit_" + BitTip[i].ToLowerInvariant().Replace(".", "");
        static int BitType(int i) => BitTip[i].StartsWith("PH") ? 0 : BitTip[i].StartsWith("PZ") ? 1 : BitTip[i].StartsWith("SL") ? 2 : BitTip[i].StartsWith("T") ? 3 : 4;
        static float BitSize(int i) => float.Parse(BitTip[i].Substring(BitType(i) >= 3 ? 1 : 2), System.Globalization.CultureInfo.InvariantCulture);
        static float BitRailMm() => 2 * BitEnd + BitTip.Length * BitPitch + (BitTip.Length - 1) * BitGap;

        // the bit holder: a rail (its own colour) with a rubber strip and sixteen short hex sleeves, the colour
        // of the tip type under each; hung turned a quarter (slot angle 90) like the socket holders, the bits
        // stand in their sleeves, tips up
        static void BitHolder(ToolMesh b)
        {
            float len = BitRailMm(), u = 1 / len;
            b.Box(new Vector3(-0.03f, 0.5f, 0), new Vector3(0.06f, 1f, 0.1f), Own, White);
            b.Box(new Vector3(0.002f, 0.5f, 0), new Vector3(0.006f, 0.98f, 0.085f), Acc, Rubber);
            b.Hook(new Vector2(-0.03f, 0.06f), 2);
            b.Hook(new Vector2(-0.03f, 0.94f), 2);
            float y = BitEnd;
            for (int i = 0; i < BitTip.Length; i++)
            {
                float c = (y + BitPitch / 2) * u, sl = 6 * u;
                b.Push(new Vector3(0.005f, c, 0), -90);   // the sleeve along +X
                b.Cyl(new Vector3(0, 0, 0), new Vector3(0, sl, 0), BitHex * 0.75f * u, Tin, DarkSteel, 6);
                b.Pop();
                b.Box(new Vector3(-0.03f, c, -0.0505f), new Vector3(0.04f, BitPitch * 0.7f * u, 0.002f), Acc, Code[BitType(i) * 2]);   // the type colour
                b.Anchor(new Vector3(0.005f + sl * 0.3f, c, 0));   // the bit's hex end sits in its sleeve
                y += BitPitch + BitGap;
            }
        }

        // a 1/4" hex power bit: the hex shank with the ball groove, a band in the tip type's colour, a round torsion
        // neck and the tip, modelled for its type (Phillips cross, Pozidriv cross with ribs, flat blade, Torx star,
        // hex), bigger for the bigger size
        static void Bit(ToolMesh b, int i)
        {
            int type = BitType(i);
            float u = 1 / BitLen, hex = BitHex / Mathf.Sqrt(3) * u, neck = 2.4f * u, sz = BitSize(i);
            b.Cyl(new Vector3(0, 0, 0), new Vector3(0, 0.2f, 0), hex, Own, White, 6);
            b.Cyl(new Vector3(0, 0.2f, 0), new Vector3(0, 0.24f, 0), hex * 0.8f, Own, White, 12);   // the ball groove
            b.Cyl(new Vector3(0, 0.24f, 0), new Vector3(0, 0.44f, 0), hex, Own, White, 6);
            b.Cyl(new Vector3(0, 0.3f, 0), new Vector3(0, 0.38f, 0), hex * 1.02f, Acc, Code[type * 2], 6);   // the type band
            b.Cone(new Vector3(0, 0.44f, 0), new Vector3(0, 0.5f, 0), hex * 0.9f, neck, Own, White, 12, false);
            b.Cyl(new Vector3(0, 0.5f, 0), new Vector3(0, 0.76f, 0), neck, Own, White, 12);   // torsion neck
            switch (type)
            {
                case 0: case 1:   // Phillips / Pozidriv: a cone with a cross of fins
                {
                    float r = (1.6f + sz * 0.7f) * u, t = (0.5f + sz * 0.25f) * u;
                    b.Cone(new Vector3(0, 0.76f, 0), new Vector3(0, 0.88f, 0), neck, r, Own, White, 12, false);
                    b.Cone(new Vector3(0, 0.88f, 0), new Vector3(0, 1f, 0), r * 0.7f, r * 0.15f, Own, White, 8, true);
                    b.Box(new Vector3(0, 0.93f, 0), new Vector3(r * 2f, 0.12f, t), Own, White);
                    b.Box(new Vector3(0, 0.93f, 0), new Vector3(t, 0.12f, r * 2f), Own, White);
                    if (type == 1)   // the Pozidriv ribs between the fins
                    {
                        b.Push(Matrix4x4.TRS(new Vector3(0, 0.93f, 0), Quaternion.Euler(0, 45, 0), Vector3.one));
                        b.Box(Vector3.zero, new Vector3(r * 1.5f, 0.08f, t * 0.45f), Own, White);
                        b.Box(Vector3.zero, new Vector3(t * 0.45f, 0.08f, r * 1.5f), Own, White);
                        b.Pop();
                    }
                    break;
                }
                case 2:   // slotted: the neck flares into a flat blade as wide as the size
                {
                    float w = sz * u, t = 0.7f * u;
                    b.Cone(new Vector3(0, 0.76f, 0), new Vector3(0, 0.84f, 0), neck, neck * 1.05f, Own, White, 12, false);
                    b.Strip(new Vector2(0, 0.82f), new Vector2(0, 1f), neck * 2, w, -t * 0.9f, t * 0.9f, Own, White);
                    break;
                }
                case 3:   // Torx: a six-lobed star, the lobes round the core
                {
                    float r = (0.9f + sz * 0.11f) * u;
                    b.Cone(new Vector3(0, 0.76f, 0), new Vector3(0, 0.82f, 0), neck, r, Own, White, 12, false);
                    b.Cyl(new Vector3(0, 0.82f, 0), new Vector3(0, 1f, 0), r * 0.72f, Own, White, 12);
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * 60 * Mathf.Deg2Rad;
                        var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r * 0.72f;
                        b.Cyl(new Vector3(0, 0.82f, 0) + o, new Vector3(0, 1f, 0) + o, r * 0.3f, Own, White, 8);
                    }
                    break;
                }
                default:   // hex: a smaller hex, the size across flats
                {
                    float r = sz / Mathf.Sqrt(3) * u;
                    b.Cone(new Vector3(0, 0.76f, 0), new Vector3(0, 0.8f, 0), neck, Mathf.Max(neck, r), Own, White, 12, false);
                    b.Cyl(new Vector3(0, 0.8f, 0), new Vector3(0, 1f, 0), r, Own, White, 6);
                    break;
                }
            }
            b.Hook(new Vector2(0, 0.02f), 2);
        }

        static float HexRailMm()
        {
            float l = 2 * HexEnd + HexGap * (HexMm.Length - 1);
            foreach (var d in HexMm) l += Mathf.Max(d, 4) + 2;
            return l;
        }

        // the hex key holder: a bar (its own colour) with a sleeve per key and a colour dot over it; it hangs flat
        // (slot angle 0) and each key hangs in its sleeve, the long arm down
        static void HexKeyHolder(ToolMesh b)
        {
            float len = HexRailMm(), u = 1 / len;
            b.Box(new Vector3(0.5f, 0, 0), new Vector3(1, 0.24f, 0.16f), Own, White);
            b.Box(new Vector3(0.5f, 0.125f, -0.02f), new Vector3(1, 0.012f, 0.12f), Tin, DarkSteel);   // the top lip
            b.Hook(new Vector2(0.12f, 0.04f), 2);
            b.Hook(new Vector2(0.88f, 0.04f), 2);
            float x = HexEnd;
            for (int i = 0; i < HexMm.Length; i++)
            {
                float w = Mathf.Max(HexMm[i], 4) + 2, c = (x + w / 2) * u;
                b.Ring(new Vector2(c, -0.06f), w * 0.55f * u, HexMm[i] * 0.55f * u, -0.1f, -0.08f, Tin, Chrome, 10);   // the sleeve, front
                b.Box(new Vector3(c, 0.075f, -0.081f), new Vector3(w * 0.7f * u, 0.05f, 0.002f), Acc, Code[i]);   // colour dot
                b.Anchor(new Vector3(c, -0.06f, -0.1f - HexMm[i] / 2 * u - 0.004f));   // the key's elbow, in front of its sleeve
                x += w + HexGap;
            }
        }

        // a hex key: an L of hex bar (black oxide), the long arm down, the short arm out to the side, the tip of
        // the short arm dipped in the size's colour
        static void HexKey(ToolMesh b, int i)
        {
            float L = HexLong[i], r = HexMm[i] / Mathf.Sqrt(3) / L, s = HexShort[i] / L, bend = r * 2.2f;
            b.Cyl(new Vector3(0, 0, 0), new Vector3(0, 1 - bend, 0), r, Own, White, 6);
            b.Cyl(new Vector3(0, 1, -bend), new Vector3(0, 1, -s), r, Own, White, 6);   // the short arm points out of the board, at the player
            b.Push(new Vector3(0, 1 - bend, -bend));   // the bend: three short segments round the corner
            for (int k = 0; k < 3; k++)
            {
                float a0 = k * 30f * Mathf.Deg2Rad, a1 = (k + 1) * 30f * Mathf.Deg2Rad;
                var p0 = new Vector3(0, Mathf.Sin(a0), Mathf.Cos(a0)) * bend;
                var p1 = new Vector3(0, Mathf.Sin(a1), Mathf.Cos(a1)) * bend;
                b.Cyl(p0, p1, r, Own, White, 6);
            }
            b.Pop();
            b.Cyl(new Vector3(0, 1, -s + Mathf.Min(0.22f, s * 0.45f)), new Vector3(0, 1, -s - 0.002f), r * 1.08f, Acc, Code[i], 6);   // the dipped tip: the size colour
            b.Hook(new Vector2(0, 1), 2);   // the elbow
        }
    }
}
