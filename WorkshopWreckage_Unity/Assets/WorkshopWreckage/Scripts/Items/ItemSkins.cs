using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Skin-first component look (resources/WORKSHOP_WRECKAGE_SKIN_FIRST_PACKAGE,
    /// docs/SKIN_FIRST_COMPONENT_AUDIT.md). Picks a skin for a definition deterministically from
    /// its geometry key, material, finish and id. Nothing about the part changes but its look:
    /// the same mesh, the same shared material, two extra per-instance vectors for the shader.
    ///   A = (family + 8 * wear profile + 64 * metal finish, label on the largest body face,
    ///        label on the second face, label on the type plate)
    ///   B = (accent colour rgb for end bells, bezels, flanges and motor feet, condition 0 new .. 1 worn)
    /// Labels are tiles of Resources/ItemSkin/item_decals.png; a negative tile covers the whole face.
    /// Families follow the material and conditions follow the catalogue (oxidized parts are rusty,
    /// polished parts are clean), so the look never contradicts the HUD or the magnet.</summary>
    public static class ItemSkins
    {
        public const int Legacy = 0, Painted = 1, Metal = 2, Warm = 3, Rusty = 4, Worn = 5, Electronic = 6;

        // wear profiles: where a part is handled and where it rubs (WWItem.shader, Skin())
        public const int Housing = 0, MotorDarkFins = 1, MotorPaintFins = 2, Fastener = 3, Washer = 4, Gear = 5, Spring = 6, Pivot = 7;

        // metal finishes: colour, roughness, metallic response and surface pattern
        public const int FinSteel = 0, FinStainless = 1, FinZinc = 2, FinBlackOxide = 3, FinAluminium = 4, FinPolished = 5, FinBrass = 6, FinCopper = 7;

        /// <summary>QA: --skins=0 renders every part with the original salvage look (before / after shots).</summary>
        public static bool Enabled = true;

        // label tiles (tools/texgen/bake_item_decals.py)
        const int PlateKestrel = 1, PlateWerkmann = 2, PlateNordhall = 3, PlateBrass = 4, Caution = 5, Hazard = 6,
            StencilSerial = 7, QcSticker = 8, RotArrow = 9, PcbGreen = 10, PcbBlue = 11, PcbRed = 12, HighVoltage = 13,
            Refurb = 14, PlateSensor = 15, HeavyDuty = 16, PlateRatio = 17, StencilOil = 18, PlateOptic = 19,
            PlateArmy = 20, Stencil24V = 21, Calibrated = 22, PlateHydrax = 23;

        /// <summary>Industrial enamels for the four catalogue paints: deeper and more saturated than
        /// the old faded set so families read at a glance, never toy-bright. Yellow and blue lean
        /// green / cyan against the ACES tonemapper, which skews them to orange / violet.</summary>
        static readonly Dictionary<string, Color> Enamel = new()
        {
            ["blue"] = new Color(0.05f, 0.4f, 0.64f),      // industrial blue
            ["green"] = new Color(0.34f, 0.39f, 0.23f),    // olive drab
            ["red"] = new Color(0.64f, 0.14f, 0.08f),      // signal / oxide red
            ["yellow"] = new Color(0.88f, 0.78f, 0.09f),   // heavy-duty safety yellow
        };

        // accent colours (sRGB) for the accents: end bells, bezels, flanges, motor feet
        static readonly Color AccentBlack = new(0.1f, 0.1f, 0.105f), AccentCast = new(0.24f, 0.24f, 0.25f),
            AccentBronze = new(0.36f, 0.27f, 0.15f), AccentRust = new(0.3f, 0.16f, 0.08f), AccentGrey = new(0.36f, 0.37f, 0.39f),
            AccentSilver = new(0.66f, 0.67f, 0.69f), AccentNearBlack = new(0.06f, 0.06f, 0.07f);

        /// <summary>Skins are for sortable components; robot assembly parts keep their own look.</summary>
        public static bool Applies(ItemDefinition def) => Enabled && !def.IsRobotPart && !def.geometryKey.StartsWith("robot:");

        public static int FamilyOf(ItemDefinition def)
        {
            if (!Applies(def)) return Legacy;
            switch (def.material)
            {
                case "painted": return Painted;
                case "brass": case "copper": return Warm;
                case "oxidized": return Rusty;
                case "worn": return Worn;
                case "electronic": return Electronic;
                default: return Metal;   // steel, stainless, aluminium (+ zinc / black oxide finishes)
            }
        }

        public static int ProfileOf(ItemDefinition def)
        {
            string key = def.geometryKey;
            int colon = key.IndexOf(':');
            string kind = colon < 0 ? key : key.Substring(0, colon);
            switch (kind)
            {
                case "screw": case "nut": return Fastener;
                case "washer": return Washer;
                case "gear": return Gear;
                case "spring": return Spring;
                case "bearing": return Pivot;
                case "shaft": return def.subcategory == "threaded_rod" ? Fastener : Pivot;
            }
            switch (key)
            {
                case "mach:sprocket_hub": return Gear;
                case "spec:hinge": case "spec:u_joint": case "spec:cam": case "spec:flywheel": case "spec:wheel_hub": case "spec:track_idler": return Pivot;
                case "mach:drive_motor":
                    // dark ribs on most; ribs in the body colour on aluminium, aged, yellow (reads as a
                    // yellow motor, not a striped one) and some red motors
                    if (def.material is "aluminium" or "oxidized" or "worn" or "brass" or "copper" || def.finish == "yellow") return MotorPaintFins;
                    if (def.finish == "red" && Hash01(def.id * 41 + 3) < 0.5f) return MotorPaintFins;
                    return MotorDarkFins;
            }
            return Housing;
        }

        public static int FinishOf(ItemDefinition def)
        {
            if (def.finish == "polished") return FinPolished;
            switch (def.material)
            {
                case "brass": return FinBrass;
                case "copper": return FinCopper;
                case "stainless": return FinStainless;
                case "aluminium": return FinAluminium;
            }
            return def.finish switch { "zinc" => FinZinc, "black_oxide" => FinBlackOxide, _ => FinSteel };
        }

        /// <summary>The paint colour of a painted part (sRGB), keeping the catalogue's per-part shade.</summary>
        public static Color PaintOf(ItemDefinition def)
        {
            var baseCol = Enamel.TryGetValue(def.finish, out var e) ? e : new Color(0.4f, 0.4f, 0.4f);
            if (!ProceduralCatalog.Paints.TryGetValue(def.finish, out var cat)) return baseCol;
            Color.RGBToHSV(def.variantColor, out var h1, out var s1, out var v1);
            Color.RGBToHSV(cat, out var h0, out var s0, out var v0);
            Color.RGBToHSV(baseCol, out var h, out var s, out var v);
            // a fifth of the catalogue's hue spread, half its value spread: parts differ, families stay coherent
            h = Mathf.Repeat(h + Mathf.DeltaAngle(h0 * 360, h1 * 360) / 360f * 0.2f, 1f);
            v = Mathf.Clamp01(v * Mathf.Lerp(1f, v1 / Mathf.Max(0.05f, v0), 0.5f));
            s = Mathf.Clamp01(s * Mathf.Lerp(1f, s1 / Mathf.Max(0.05f, s0), 0.5f));
            return Color.HSVToRGB(h, s, v);
        }

        /// <summary>Linear base colour of a skinned part (instance buffer and material alike):
        /// enamel for painted parts, real metal albedos for bare metal.</summary>
        public static Color Base(ItemDefinition def, Color legacy)
        {
            Color c = def.material switch
            {
                "painted" => PaintOf(def),
                "steel" => def.finish switch { "black_oxide" => new Color(0.16f, 0.16f, 0.17f), "zinc" => new Color(0.64f, 0.68f, 0.72f), _ => new Color(0.66f, 0.67f, 0.69f) },
                "stainless" => new Color(0.72f, 0.72f, 0.72f),
                "aluminium" => new Color(0.8f, 0.81f, 0.82f),
                "brass" => new Color(0.78f, 0.68f, 0.42f),
                "copper" => new Color(0.78f, 0.45f, 0.3f),
                "oxidized" => new Color(0.55f, 0.3f, 0.15f),
                "worn" => new Color(0.56f, 0.56f, 0.57f),
                _ => legacy,
            };
            if (def.finish == "polished") c *= 1.06f;
            var lin = c.linear;
            lin.a = 1;
            return lin;
        }

        static float Hash01(int x)
        {
            unchecked
            {
                uint h = (uint)x * 2654435761u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return (h & 0xffff) / 65535f;
            }
        }

        static int Pick(int[] tiles, int id, int salt) => tiles[Mathf.Min(tiles.Length - 1, (int)(Hash01(id * 97 + salt) * tiles.Length))];

        /// <summary>Condition of a definition, 0 new .. 1 worn out; deterministic per definition so
        /// a part looks the same on the floor, in the hand and after loading a save.
        /// A workshop mix: ~30 % new, ~35 % clean used, ~25 % maintained, ~10 % heavily worn;
        /// catalogue conditions win (oxidized parts are rusty, worn parts used, polished parts clean).</summary>
        public static float Condition(ItemDefinition def)
        {
            float r = Hash01(def.id * 7 + 11), r2 = Hash01(def.id * 13 + 5);
            float c = def.material switch
            {
                "oxidized" => 0.55f + 0.4f * r,
                "worn" => 0.4f + 0.45f * r,
                "electronic" => 0.04f + 0.1f * r,
                _ => r < 0.3f ? 0.02f + 0.06f * r2 : r < 0.65f ? 0.12f + 0.13f * r2 : r < 0.9f ? 0.3f + 0.2f * r2 : 0.55f + 0.2f * r2,
            };
            if (def.finish == "polished") c = Mathf.Min(c, 0.02f + 0.1f * r2);
            if (def.geometryKey == "mach:drive_motor")   // art direction: five related motors, five identities
                c = def.material switch
                {
                    "painted" => def.finish switch
                    {
                        "blue" => Mathf.Min(c, 0.08f),            // precision motor: nearly pristine
                        "red" => Mathf.Clamp(c, 0.12f, 0.28f),     // light edge wear
                        "yellow" => Mathf.Clamp(c, 0.3f, 0.5f),    // heavy duty: handled
                        _ => c,
                    },
                    "oxidized" => 0.6f,                            // aged but maintained
                    "worn" => c,
                    _ => Mathf.Min(c, 0.18f),                      // industrial steel: minimal wear
                };
            return c;
        }

        /// <summary>(A, B) for a definition; condition is this instance's condition (Condition(def) plus a small jitter).</summary>
        public static (Vector4 a, Vector4 b) For(ItemDefinition def, float condition)
        {
            int fam = FamilyOf(def);
            if (fam == Legacy) return (Vector4.zero, Vector4.zero);
            string key = def.geometryKey;
            int id = def.id;
            int prof = ProfileOf(def);
            float tier = Condition(def);   // labels follow the definition, not the instance jitter
            bool fresh = tier < 0.1f, clean = tier < 0.28f, old = tier >= 0.52f;
            int body1 = 0, body2 = 0, plate = 0;

            // type plates on the existing plate accents: maker plates by family, specials by part
            int[] makers = fam switch
            {
                Painted when def.finish == "green" => new[] { PlateArmy, PlateArmy, PlateWerkmann },
                Painted when def.finish == "yellow" => new[] { HighVoltage, PlateKestrel, Caution },
                Painted when def.finish == "blue" => new[] { PlateNordhall, PlateKestrel, PlateKestrel },
                Painted => new[] { PlateWerkmann, PlateKestrel },
                Warm => new[] { PlateBrass },
                Rusty => new[] { PlateBrass, PlateWerkmann, Refurb },
                Worn => new[] { Refurb, PlateWerkmann, PlateKestrel },
                _ => new[] { PlateWerkmann, PlateKestrel, Calibrated },
            };
            plate = Pick(makers, id, 1);

            switch (key)
            {
                case "mach:drive_motor":
                case "mach:motor_housing":
                    // the rating plate carries the only label: maker plates, a warning on the yellow ones
                    plate = fam == Painted && def.finish == "yellow" ? Pick(new[] { Caution, HighVoltage }, id, 2)
                        : fam == Painted && def.finish == "blue" ? PlateNordhall : Pick(makers, id, 2);
                    if (fam == Rusty || fam == Warm) plate = PlateBrass;
                    break;
                case "mach:gearbox":
                    // the shafts cover most of the front: only the round rotation mark fits between them.
                    // The lid tells the gearbox's history: maker plate when new, a refurbishment sticker
                    // when overhauled, stencils when old; the caution sign only on the yellow ones
                    body1 = clean && Hash01(id * 5 + 1) < 0.6f ? RotArrow : 0;
                    body2 = fam == Painted && def.finish == "yellow" ? Caution
                        : fam == Rusty ? Pick(new[] { StencilSerial, StencilOil, PlateBrass }, id, 2)
                        : clean ? Pick(makers, id, 2)
                        : old ? Pick(new[] { StencilSerial, StencilOil }, id, 2)
                        : Pick(new[] { Refurb, QcSticker }, id, 2);
                    break;
                case "mach:reduction_cartridge":
                    plate = PlateRatio;
                    body1 = fam == Painted && def.finish == "yellow" ? Hazard : Pick(new[] { Caution, StencilSerial, RotArrow }, id, 4);
                    break;
                case "mach:hydraulic_actuator":
                    break;
                case "mach:limb_housing":
                    plate = Pick(new[] { QcSticker, Refurb, Calibrated, StencilSerial }, id, 5);
                    body1 = def.finish == "yellow" ? HeavyDuty : 0;
                    break;
                case "mach:chassis_bracket":
                    plate = Pick(new[] { StencilSerial, Stencil24V, QcSticker }, id, 6);
                    body1 = Pick(new[] { HeavyDuty, StencilSerial, 0 }, id, 7);
                    if (fam == Painted && def.finish == "yellow") body1 = Hazard;
                    break;
                case "mach:shoulder_yoke":
                    body1 = Pick(makers, id, 8);
                    break;
                case "mach:servo_joint":
                    plate = Pick(new[] { PlateKestrel, QcSticker, Calibrated }, id, 9);
                    break;
                case "spec:sensor_lens":
                    plate = PlateOptic;
                    break;
                case "elec:servo_controller":
                case "elec:motor_driver":
                case "elec:sensor_board":
                    body1 = -Pick(new[] { PcbGreen, PcbGreen, PcbBlue, PcbRed }, id, 10);   // the board face
                    break;
            }
            if (fresh && plate == Refurb) plate = Pick(new[] { PlateWerkmann, PlateKestrel }, id, 11);   // a new part is not refurbished

            var accent = fam switch
            {
                Painted => def.finish == "yellow" ? AccentBlack : AccentCast,
                Metal => def.material == "aluminium" || def.finish == "polished" ? AccentBlack : AccentGrey,
                Warm => AccentBronze,
                Rusty => AccentRust,
                Worn => AccentCast,
                _ => AccentCast,
            };
            if (key == "mach:drive_motor")   // end bells and foot
                accent = fam switch
                {
                    Painted => def.finish switch { "blue" => AccentNearBlack, "yellow" => AccentBlack, _ => AccentCast },
                    Metal => def.material == "aluminium" ? AccentBlack : AccentSilver,
                    _ => accent,
                };
            var al = accent.linear;
            int packed = fam + 8 * prof + 64 * FinishOf(def);
            return (new Vector4(packed, body1, body2, plate), new Vector4(al.r, al.g, al.b, condition));
        }
    }
}
