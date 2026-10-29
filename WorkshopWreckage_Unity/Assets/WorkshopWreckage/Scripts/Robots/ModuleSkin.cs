using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>The paint skin of a detachable robot module (head, arm, propeller) or a chassis
    /// (WORKSHOP_WRECKAGE_MODIFY_ROBOT_MODULAR_COMPONENTS_SPEC §4: appearance belongs to the module, function to
    /// the chassis). Paint only: the same meshes in every skin; pinstripes and emblems are thin painted
    /// linework laid on the surface. The three Hoverbot factory skins follow resources/hoverbots/.</summary>
    public sealed class ModuleSkin
    {
        public readonly string id, name;
        /// <summary>Enamel of the shells and the head; small painted accents (antenna balls, ear cones, the
        /// rotor hub, the arms' shoulder rings); the arm segments; the propeller blades.</summary>
        public readonly Color body, accent, arm, blade;
        /// <summary>Pinstripes and emblems (null: plain enamel); the emblem drawn on the head and the saucer.</summary>
        public readonly Color? stripe;
        public readonly string emblem;
        readonly float _chips, _grime;

        ModuleSkin(string id, string name, Color body, Color accent, Color arm, Color blade, Color? stripe, string emblem, float chips, float grime)
        {
            this.id = id; this.name = name; this.body = body; this.accent = accent; this.arm = arm; this.blade = blade;
            this.stripe = stripe; this.emblem = emblem; _chips = chips; _grime = grime;
        }

        /// <summary>The approved weathered industrial red: models keep their own family finish untouched.</summary>
        public static readonly ModuleSkin ClassicRed = new("classic_red", "Classic Red", RobotModel.FamilyRed, RobotModel.FamilyRed, RobotModel.FamilyRed, RobotModel.FamilyRed, null, null, 0.5f, 0.5f);
        /// <summary>Satin warm ivory with restrained pink pinstripes, pink accents, pink arm segments, an
        /// ivory propeller striped in pink and a pink lotus on the forehead.</summary>
        public static readonly ModuleSkin WhitePink = new("white_pink", "Satin White / Pink", new Color(0.86f, 0.82f, 0.74f), new Color(0.84f, 0.2f, 0.4f),
            new Color(0.88f, 0.5f, 0.56f), new Color(0.86f, 0.82f, 0.74f), new Color(0.9f, 0.3f, 0.52f), "lotus", 0.4f, 0.35f);
        /// <summary>The industrial red with deliberate gold pinstripes and flame ornaments, a gold rotor hub and
        /// red blades striped in gold.</summary>
        public static readonly ModuleSkin RedYellow = new("red_yellow", "Red / Yellow Pinstripes", RobotModel.FamilyRed, RobotModel.FamilyRed,
            RobotModel.FamilyRed, RobotModel.FamilyRed, new Color(0.95f, 0.64f, 0.06f), "flame", 0.5f, 0.5f);

        public static readonly ModuleSkin[] All = { ClassicRed, WhitePink, RedYellow };
        public static ModuleSkin Get(string id) => System.Array.Find(All, s => s.id == id);

        /// <summary>The factory skin of a Hoverbot instance: the first is Classic Red, the second Red / Yellow
        /// (resources/hoverbots/hoverbot_2.png), the third Satin White / Pink (hoverbot_3.png).</summary>
        public static ModuleSkin HoverbotFactory(string robotId) => robotId switch
        {
            "hoverbot_2" => RedYellow,
            "hoverbot_3" => WhitePink,
            _ => ClassicRed,
        };

        public bool Factory => this == ClassicRed;

        Material _body, _bodyDeep, _accent, _arm, _armDeep, _blade, _stripe;
        Material Enamel(Color c) => c == RobotModel.FamilyRed
            ? RobotFinish.Paint(c, 1f, 0.5f, 0.5f, 0.8f, 0.2f, 0.2f, 0.08f)   // the family red itself (the same cached material)
            : RobotFinish.Paint(c, 1f, _chips, _grime, 0.6f, 0.15f, 0.1f, 0.05f);
        public Material Body => _body ??= Enamel(body);
        public Material BodyDeep => _bodyDeep ??= body == RobotModel.FamilyRed ? RobotFinish.Paint(RobotModel.FamilyRedDeep, 1f, 0.5f, 0.5f, 0.8f, 0.25f, 0.2f, 0.08f) : Enamel(RobotModel.DeepTone(body));
        public Material Accent => _accent ??= Enamel(accent);
        public Material Arm => _arm ??= Enamel(arm);
        public Material ArmDeep => _armDeep ??= arm == RobotModel.FamilyRed ? BodyDeep : Enamel(RobotModel.DeepTone(arm));
        public Material Blade => _blade ??= Enamel(blade);
        /// <summary>The rotor hub dome: gold on the flame skin (the reference), otherwise the accent.</summary>
        public Material Hub => emblem == "flame" && Stripe != null ? Stripe : Accent;
        /// <summary>Pinstripe enamel: crisper, lighter wear than the body so the linework reads.</summary>
        public Material Stripe => _stripe ??= stripe is Color c ? RobotFinish.Paint(c, 0.6f, 0.25f, 0.3f, 0.4f, 0.1f, 0.1f, 0.04f) : null;
    }
}
