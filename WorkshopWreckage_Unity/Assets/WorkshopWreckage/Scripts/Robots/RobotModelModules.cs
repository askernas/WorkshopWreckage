using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Interchangeable modules on the shared model hierarchy (Modify Robot spec §3-§5, §9). The chassis
    /// owns the pivots (head mount, shoulders, Hoverbot's propeller mast), the animation and every capability;
    /// a fitted module supplies only its geometry and paint. A module always wears its origin robot's locked
    /// colours (Retrobot blue, Unibot green, Spiderbot violet, Caterpillarbot yellow, the three Hoverbot
    /// skins), whatever chassis it sits on. A robot wearing its own factory modules builds exactly as
    /// before (the same code path), so the approved looks never change.</summary>
    public abstract partial class RobotModel
    {
        /// <summary>Origin robot id of the module in each slot, set by the robot before a Build: null = its own
        /// factory module (the model's own code path), "" = the slot is empty (nothing drawn), else the donor.</summary>
        public string headFrom, armLFrom, armRFrom, propFrom;

        System.Func<string, Material, Material> _modFinish;
        bool _modEmpty;

        /// <summary>Rebuilds with the same installed parts (after a module change).</summary>
        public void Rebuild() { if (installed != null) Build(installed); }

        /// <summary>Does the model's own code draw a module from `from` (its own factory module: null)? The three
        /// Hoverbots share one model, so it draws all Hoverbot modules itself, each in its own skin.</summary>
        protected virtual bool OwnBuild(string from) => from == null;

        /// <summary>Builds `build` as the module of `origin`: every primitive in that robot's finish, or none at
        /// all for an empty slot ("").</summary>
        protected void AsModule(string origin, System.Action build)
        {
            var (f, e) = (_modFinish, _modEmpty);
            _modEmpty = origin == "";
            _modFinish = _modEmpty ? (_, _) => null : (p, m) => OriginFinish(origin, p, m);
            try { build(); }
            finally { (_modFinish, _modEmpty) = (f, e); }
        }

        /// <summary>The locked finish of a module from `origin` (the same materials its own robot uses).</summary>
        public static Material OriginFinish(string origin, string part, Material m) => origin switch
        {
            "retrobot" => FamilySkin(m, FamilyBlue),
            "spiderbot" => FamilySkin(m, FamilyViolet),
            "unibot" or "caterpillarbot" => FamilySkin(m),
            _ when RobotModules.IsHover(origin) => HoverbotModel.ModulePaint(ModuleSkin.HoverbotFactory(origin), part, m),
            _ => FamilySkin(m),
        };

        /// <summary>The height of the head of `origin` (Hoverbot's chassis-owned propeller mast sits on top of it).</summary>
        public static float HeadHeightOf(string origin) => origin switch
        {
            "retrobot" => 0.28f, "unibot" => 0.32f, "spiderbot" => 0.27f, "caterpillarbot" => 0.28f, _ => 0.31f,
        };

        /// <summary>Builds the head slot: the model's own head, or the fitted donor head in the donor's design
        /// and paint, or nothing. A chassis that owns the head-top mount (HeadTopTaken) suppresses the donor's
        /// beacon in the head builders themselves.</summary>
        protected void BuildHeadModule(System.Action own)
        {
            if (OwnBuild(headFrom)) { own(); return; }
            AsModule(headFrom, () => DonorHead(head, headFrom));
        }

        /// <summary>Builds one arm slot: the model's own arm (`own`, its usual spec), or the donor's arm with
        /// the donor's paint and gripper size on this chassis' shoulder, scale and twist (the host's
        /// telescope reach comes from the chassis as always), or nothing.</summary>
        protected void BuildArmModule(Transform arm, float side, string part, ArmSpec own)
        {
            string from = side < 0 ? armLFrom : armRFrom;
            if (OwnBuild(from)) { FamilyArm(arm, side, part, own); return; }
            var spec = DonorArm(from);
            spec.twist = own.twist;
            spec.clawPart = side < 0 ? "left claw" : "right claw";
            AsModule(from, () => FamilyArm(arm, side, side < 0 ? "left arm" : "right arm", spec));
        }

        /// <summary>The arm design of each origin (the same specs its own model builds with).</summary>
        static ArmSpec DonorArm(string origin) => origin switch
        {
            "unibot" => new ArmSpec { paint = ToyGreen, paintDeep = ToyGreenDeep, red = ToyRed, clawScale = 1.08f },
            "spiderbot" => new ArmSpec { paint = ToyGreen, paintDeep = ToyGreenDeep, red = ToyRed, clawScale = 1.15f },
            "caterpillarbot" or "retrobot" => new ArmSpec { paint = ToyYellow, paintDeep = ToyYellowDeep, red = ToyRed, clawScale = 1.08f },
            _ => new ArmSpec { paint = ToyYellow, paintDeep = ToyYellowDeep, red = ToyRed, clawScale = 1.3f },   // Hoverbots
        };

        /// <summary>The head design of each origin, built on `pivot` (the same calls its own model makes).</summary>
        void DonorHead(Transform pivot, string origin)
        {
            switch (origin)
            {
                case "retrobot":
                    FamilyHead(pivot, "head", new HeadSpec { w = 0.39f, h = 0.28f, d = 0.32f, eyeX = 0.097f, eyeY = 0.46f, earY = 0.47f, paint = ToyYellow, beacon = true, antennas = true, mouth = true });
                    break;
                case "unibot":
                    DetailedHead(pivot, ToyGreen, new Vector3(0.36f, 0.32f, 0.32f), 1, 0.078f,
                        "head shell", "optical eye", null, "antenna pair", "antenna pair", "head shell", "head shell", null, false, false, 1f, true);
                    break;
                case "spiderbot":
                    FamilyHead(pivot, "head", new HeadSpec
                    {
                        w = 0.34f, h = 0.27f, d = 0.28f, eyes = 2, eyeX = 0.08f, eyeScale = 0.95f, eyeY = 0.52f, earY = 0.5f, paint = ToyGreen,
                        beacon = true, antennas = true, mouth = true, eyePart = "eye pair", antennaPart = "antenna pair",
                    });
                    break;
                case "caterpillarbot":
                    FamilyHead(pivot, "head", new HeadSpec
                    {
                        w = 0.42f, h = 0.28f, d = 0.34f, eyes = 1, eyeScale = 1.55f, eyeY = 0.53f, earY = 0.5f, paint = ToyYellow,
                        beacon = true, antennas = true, mouth = true, eyePart = "optical eye", antennaPart = "antenna pair", beaconPart = "siren light",
                    });
                    break;
                default:
                    HoverHead(pivot, ModuleSkin.HoverbotFactory(origin));
                    break;
            }
        }

        // ------------------------------------------------------------------ the Hoverbot head (any chassis)

        /// <summary>Hoverbot's head: the family head in red with two bezel eyes, ear caps, antennas and a mouth
        /// grille, the loose wire under it and, on a pinstriped skin, the painted face frame and the forehead
        /// emblem. Paint comes from M (the Hoverbot's module skins, or OriginFinish on another chassis).</summary>
        protected void HoverHead(Transform pivot, ModuleSkin skin)
        {
            const float HeadH = 0.31f;
            DetailedHead(pivot, ToyRed, new Vector3(0.44f, HeadH, 0.32f), 2, 0.06f,
                "head", "left eye lens", "right eye lens", "left antenna", "right antenna", "left ear cap", "right ear cap", null, true, true, familyEars: true);
            var w = new Batch();   // the loose wire under the head (it is on the sheet)
            Pipe(w, "head", new[] { new Vector3(0.19f, 0.03f, 0.06f), new Vector3(0.205f, -0.005f, 0.08f), new Vector3(0.212f, -0.03f, 0.07f) }, 0.004f, Rubber, Brass);
            if (skin.Stripe is Material hs)
            {
                // a painted frame on the flat of the face, just inside its rounded edges (the eye rings sit over
                // it), and the emblem on the forehead between the eyes
                float bev = Mathf.Min(0.058f, HeadH * 0.19f), wide = skin.emblem == "flame" ? 0.007f : 0.0045f;
                float fx = 0.22f - bev - 0.004f, fy0 = bev + 0.004f, fy1 = HeadH - bev - 0.004f, fz = -0.16f - 0.0012f;
                foreach (var sx in new[] { -1f, 1f })
                    w.RoundedBox(new Vector3(sx * fx, (fy0 + fy1) / 2, fz), new Vector3(wide, fy1 - fy0 + wide, 0.0018f), 0.0008f, M("head", hs), 1);
                foreach (var y in new[] { fy0, fy1 })
                    w.RoundedBox(new Vector3(0, y, fz), new Vector3(2 * fx + wide, wide, 0.0018f), 0.0008f, M("head", hs), 1);
                Emblem(w, "head", skin.emblem, new Vector3(0, 0.228f, -0.16f), new Vector3(0, 0, -1), skin.emblem == "flame" ? 0.055f : 0.05f, hs);
            }
            Done(w, pivot);
        }

        /// <summary>Emblem linework: tongues grown from a root (across, up, in units of the emblem's size), at an angle
        /// (deg), with a length and greatest width; drawn in three overlapping segments tapering to a point (flame),
        /// swelling in the middle (petal) or flat (line).</summary>
        static readonly (float u, float v, float a, float l, float w, char taper)[] LotusStrokes =
        {
            (0, -0.32f, 90, 0.78f, 0.26f, 'p'), (-0.04f, -0.34f, 122, 0.66f, 0.22f, 'p'), (0.04f, -0.34f, 58, 0.66f, 0.22f, 'p'),
            (-0.08f, -0.36f, 152, 0.5f, 0.18f, 'p'), (0.08f, -0.36f, 28, 0.5f, 0.18f, 'p'), (-0.42f, -0.4f, 0, 0.84f, 0.06f, 'l'),
        }, FlameStrokes =
        {
            (0, -0.5f, 90, 1.05f, 0.22f, 'f'), (-0.06f, -0.48f, 112, 0.82f, 0.17f, 'f'), (0.06f, -0.48f, 68, 0.82f, 0.17f, 'f'),
            (-0.12f, -0.46f, 136, 0.56f, 0.14f, 'f'), (0.12f, -0.46f, 44, 0.56f, 0.14f, 'f'),
            (-0.4f, -0.08f, 70, 0.34f, 0.1f, 'f'), (0.4f, -0.08f, 110, 0.34f, 0.1f, 'f'),
        };

        /// <summary>A painted emblem centred at c on a surface with outward normal n (batch coordinates), upright
        /// (or upside down) along the surface.</summary>
        protected void Emblem(Batch b, string part, string kind, Vector3 c, Vector3 n, float size, Material mat, bool flip = false)
        {
            var nU = new Vector3(n.x, n.y, -n.z).normalized;
            var upU = Vector3.ProjectOnPlane(Vector3.up, nU).normalized * (flip ? -1 : 1);
            var rot = Quaternion.LookRotation(nU, upU);
            var rightU = rot * Vector3.right;
            foreach (var s in kind == "lotus" ? LotusStrokes : FlameStrokes)
            {
                var dir = new Vector2(Mathf.Cos(s.a * Mathf.Deg2Rad), Mathf.Sin(s.a * Mathf.Deg2Rad));
                int segs = s.taper == 'l' ? 1 : 3;
                for (int i = 0; i < segs; i++)
                {
                    float w = s.taper == 'f' ? 1f - 0.32f * i : s.taper == 'p' ? (i == 1 ? 1f : i == 0 ? 0.62f : 0.5f) : 1f;
                    float t = (i + 0.5f) / segs, len = s.l / segs * (segs > 1 ? 1.25f : 1f);
                    var q = new Vector2(s.u, s.v) + dir * (s.l * t);
                    var o = (rightU * q.x + upU * q.y) * size + nU * 0.0012f;
                    b.RoundedBox(c + new Vector3(o.x, o.y, -o.z), new Vector3(len * size, s.w * w * size, 0.0018f), 0.0008f, M(part, mat), 1, rot * Quaternion.Euler(0, 0, s.a));
                }
            }
        }
    }
}
