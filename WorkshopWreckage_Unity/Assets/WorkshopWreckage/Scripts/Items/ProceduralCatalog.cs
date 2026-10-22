using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>The combinatorial parameter space of possible parts (67,040 + ids),
    /// a 1:1 port of the reference ItemCatalog: identical ids, names, dimensions
    /// and plausibility rules. Authoring picks presets' definitions from it; the
    /// game itself only reads the authored JSON catalog.</summary>
    public class ProceduralCatalog
    {
        public enum Cat { Screw, Nut, Washer, Gear, Spring, Bearing, Shaft, Electronic, Specialty, Machine, RobotPart }
        public const int RobotPartIdBase = 70000;
        public const float DefaultVisualScale = 2f;

        public static readonly string[] Materials = { "steel", "stainless", "brass", "copper", "aluminium", "painted", "oxidized", "worn" };
        public static readonly string[] Finishes = { "plain", "zinc", "black_oxide", "polished" };
        public static readonly string[] PaintColors = { "red", "blue", "green", "yellow" };
        public static readonly Dictionary<string, Color> Paints = new()
        {
            ["red"] = new Color(0.72f, 0.12f, 0.09f), ["blue"] = new Color(0.17f, 0.33f, 0.62f),
            ["green"] = new Color(0.24f, 0.45f, 0.25f), ["yellow"] = new Color(0.86f, 0.62f, 0.12f),
        };
        static readonly HashSet<string> MagneticMaterials = new() { "steel", "painted", "oxidized", "worn" };
        static readonly Dictionary<string, float> Density = new()
        {
            ["steel"] = 7.85f, ["stainless"] = 7.9f, ["brass"] = 8.5f, ["copper"] = 8.96f,
            ["aluminium"] = 2.7f, ["painted"] = 7.85f, ["oxidized"] = 7.85f, ["worn"] = 7.85f,
        };
        static readonly Dictionary<string, int> MaterialRarity = new()
        {
            ["steel"] = 0, ["stainless"] = 1, ["brass"] = 1, ["copper"] = 2, ["aluminium"] = 1, ["painted"] = 1, ["oxidized"] = 0, ["worn"] = 0,
        };
        static readonly (string name, float dia, float pitch)[] Threads =
        {
            ("M2", 2f, 0.4f), ("M2.5", 2.5f, 0.45f), ("M3", 3f, 0.5f), ("M4", 4f, 0.7f), ("M5", 5f, 0.8f),
            ("M6", 6f, 1f), ("M8", 8f, 1.25f), ("M10", 10f, 1.5f), ("M12", 12f, 1.75f),
        };
        static readonly string[] ScrewTypes = { "hex_bolt", "socket_cap", "pan_head", "countersunk", "carriage", "thumb" };
        static readonly float[] ScrewLengths = { 6, 8, 10, 12, 16, 20, 25, 30, 40, 50, 60, 80 };
        static readonly string[] NutTypes = { "hex", "nyloc", "square", "acorn", "flange", "wing" };
        static readonly string[] WasherTypes = { "flat", "split", "fender", "toothed", "square" };
        static readonly string[] GearTypes = { "spur", "pinion", "sprocket", "bevel", "ratchet" };
        static readonly int[] GearTeeth = { 8, 10, 12, 16, 20, 24, 32, 40 };
        static readonly float[] GearModules = { 0.5f, 0.8f, 1.0f, 1.5f };
        static readonly string[] SpringTypes = { "compression", "extension", "torsion" };
        static readonly float[] SpringWires = { 0.5f, 0.8f, 1.0f, 1.5f, 2.0f };
        static readonly float[] SpringCoils = { 4, 6, 8, 10, 14, 20 };
        static readonly float[] SpringLengths = { 10, 15, 20, 30, 45 };
        static readonly string[] BearingTypes = { "ball", "roller", "flanged", "thrust" };
        static readonly float[] BearingBores = { 3, 4, 5, 6, 8, 10, 12, 15, 17, 20 };
        public static readonly float[] BearingWidths = { 0.28f, 0.35f, 0.45f };
        static readonly string[] ShaftTypes = { "shaft", "dowel_pin", "clevis_pin", "threaded_rod", "spring_pin" };
        static readonly float[] ShaftDiameters = { 2, 3, 4, 5, 6, 8, 10, 12, 16 };
        static readonly float[] ShaftLengths = { 10, 12, 16, 20, 25, 30, 40, 50, 60, 80, 100, 120 };
        static readonly string[] ElectronicTypes = { "resistor", "capacitor", "transistor", "ic_chip", "led", "relay", "toggle_switch", "potentiometer" };
        static readonly string[] ElectronicSizes = { "Small", "Medium", "Large" };
        static readonly float[] ElectronicSizeMm = { 6, 9, 13 };
        static readonly Dictionary<string, string[]> ElectronicValues = new()
        {
            ["resistor"] = new[] { "10Ω", "47Ω", "100Ω", "220Ω", "470Ω", "1kΩ", "2.2kΩ", "4.7kΩ", "10kΩ", "47kΩ", "100kΩ", "1MΩ" },
            ["capacitor"] = new[] { "1µF", "2.2µF", "4.7µF", "10µF", "22µF", "47µF", "100µF", "220µF", "470µF", "1000µF", "2200µF", "4700µF" },
            ["transistor"] = new[] { "BC547", "BC557", "2N2222", "2N3904", "2N3906", "BD139", "BD140", "TIP31", "TIP32", "S8050", "S8550", "C945" },
            ["ic_chip"] = new[] { "555", "556", "741", "LM358", "LM386", "4017", "4011", "7400", "7404", "7408", "74595", "ULN2003" },
            ["led"] = new[] { "3V", "3.2V", "2V", "2.1V", "1.8V", "Flash", "Bi-colour", "Diffused", "Clear", "High-bright", "Low-current", "IR" },
            ["relay"] = new[] { "3V", "5V", "6V", "9V", "12V", "24V", "48V", "5V DPDT", "12V DPDT", "24V DPDT", "Reed 5V", "Reed 12V" },
            ["toggle_switch"] = new[] { "SPST", "SPDT", "DPDT", "SPST Mini", "SPDT Mini", "DPDT Mini", "ON-OFF-ON", "Momentary", "Locking", "Guarded", "Panel", "Heavy" },
            ["potentiometer"] = new[] { "1kΩ", "2.2kΩ", "4.7kΩ", "10kΩ", "22kΩ", "47kΩ", "100kΩ", "220kΩ", "470kΩ", "1MΩ", "10k Log", "100k Log" },
        };
        static readonly Dictionary<string, string[]> ElectronicColors = new()
        {
            ["resistor"] = new[] { "#d9c49a", "#7fa7c9", "#c9b07f", "#b7a389" },
            ["capacitor"] = new[] { "#2c4f8a", "#1d1d22", "#5a2d6b", "#2f6e4a" },
            ["transistor"] = new[] { "#1b1b1e", "#26262a", "#1e1e24", "#2a2a2e" },
            ["ic_chip"] = new[] { "#1a1a1d", "#232327", "#1d1f22", "#2b2b30" },
            ["led"] = new[] { "#ff3b2f", "#3cff5a", "#ffd23a", "#3a8bff" },
            ["relay"] = new[] { "#2a56a8", "#1d1d22", "#c9c2b0", "#2b6b47" },
            ["toggle_switch"] = new[] { "#b5b8bd", "#1d1d22", "#c23b2b", "#2a56a8" },
            ["potentiometer"] = new[] { "#2a56a8", "#1d1d22", "#3a7a4a", "#8a6a3a" },
        };
        static readonly string[] SpecialtyTypes = { "cam", "pulley", "coupling", "hinge", "knob", "u_joint", "flywheel" };
        static readonly float[] SpecialtySizes = { 12, 16, 20, 25, 32, 40 };
        static readonly string[] MachineTypes = { "bracket", "motor_housing", "gearbox", "valve", "pump_housing", "camshaft", "crankshaft", "pipe_elbow", "lever_arm", "sprocket_hub" };
        static readonly float[] MachineSizes = { 40, 55, 70, 90, 120, 160 };

        /// <summary>Axis sizes per category (first axis least significant).</summary>
        public static readonly int[][] Axes =
        {
            new[] { 6, 9, 12, 8, 4 }, new[] { 6, 9, 8, 4 }, new[] { 5, 9, 8, 4 }, new[] { 5, 8, 4, 8, 4 },
            new[] { 3, 5, 6, 5, 8, 4 }, new[] { 4, 10, 3, 8, 4 }, new[] { 5, 9, 12, 8, 4 }, new[] { 8, 12, 3, 4 },
            new[] { 7, 6, 8, 4 }, new[] { 10, 6, 8, 4 },
        };
        /// <summary>Index of the material axis per category (-1 = none).</summary>
        public static readonly int[] MaterialAxis = { 3, 2, 2, 3, 4, 3, 3, -1, 2, 2 };

        readonly int[] _offsets = new int[10];
        readonly int[] _sizes = new int[10];
        readonly Dictionary<int, ItemDefinition> _cache = new();
        public int Size { get; }

        public ProceduralCatalog()
        {
            int off = 0;
            for (int c = 0; c < Axes.Length; c++)
            {
                int n = 1;
                foreach (var a in Axes[c]) n *= a;
                _offsets[c] = off;
                _sizes[c] = n;
                off += n;
            }
            Size = off;
        }

        public int CategoryOf(int id)
        {
            for (int c = _offsets.Length - 1; c >= 0; c--) if (id >= _offsets[c]) return c;
            return 0;
        }

        public int[] Decode(int id)
        {
            int c = CategoryOf(id), local = id - _offsets[c];
            var o = new int[Axes[c].Length];
            for (int i = 0; i < o.Length; i++) { o[i] = local % Axes[c][i]; local /= Axes[c][i]; }
            return o;
        }

        public int Encode(int c, int[] idx)
        {
            int local = 0, mul = 1;
            for (int i = 0; i < Axes[c].Length; i++) { local += idx[i] * mul; mul *= Axes[c][i]; }
            return _offsets[c] + local;
        }

        public ItemDefinition Get(int id)
        {
            if (id < 0 || id >= Size) return null;
            if (!_cache.TryGetValue(id, out var d)) _cache[id] = d = Build(id);
            return d;
        }

        public bool IsPlausible(ItemDefinition d)
        {
            if ((d.finish == "zinc" || d.finish == "black_oxide") && d.material != "steel") return false;
            if ((d.material == "oxidized" || d.material == "worn") && d.finish != "plain") return false;
            switch ((Cat)d.category)
            {
                case Cat.Screw: { float a = d.lengthMm / d.diameterMm; return a >= 1.5f && a <= 10f; }
                case Cat.Gear: return d.diameterMm >= 8f && d.diameterMm <= 66f;
                case Cat.Spring:
                {
                    var i = Decode(d.id);
                    float ratio = SpringCoils[i[2]] / SpringWires[i[1]], lr = SpringLengths[i[3]] / SpringCoils[i[2]];
                    return ratio >= 4f && ratio <= 16f && lr >= 0.75f && lr <= 6f;
                }
                case Cat.Shaft: { float a = d.lengthMm / d.diameterMm; return a >= 2f && a <= 15f; }
            }
            return true;
        }

        // ------------------------------------------------------------------ helpers

        static float Nearest(float[] values, float v)
        {
            float best = values[0];
            foreach (var x in values) if (Mathf.Abs(x - v) < Mathf.Abs(best - v)) best = x;
            return best;
        }

        public static string Fmt(float v) => Mathf.Approximately(v, Mathf.Round(v)) ? ((int)Mathf.Round(v)).ToString(CultureInfo.InvariantCulture) : v.ToString("0.0", CultureInfo.InvariantCulture);
        static float Snap(float v, float step) => Mathf.Round(v / step) * step;

        static string Cap(string s)
        {
            var parts = s.Replace('_', ' ').Split(' ');
            for (int i = 0; i < parts.Length; i++) if (parts[i].Length > 0) parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1).ToLowerInvariant();
            return string.Join(" ", parts);
        }

        static void SetMaterial(ItemDefinition d, int m, int f)
        {
            d.material = Materials[m];
            d.finish = d.material == "painted" ? PaintColors[f] : Finishes[f];
        }

        static string MaterialLabel(ItemDefinition d)
        {
            string m = d.material switch
            {
                "steel" => "Steel", "stainless" => "Stainless", "brass" => "Brass", "copper" => "Copper",
                "aluminium" => "Aluminium", "painted" => "Painted", "oxidized" => "Rusty", _ => "Worn",
            };
            if (d.material == "painted") return Cap(d.finish) + " " + m;
            string f = d.finish switch { "zinc" => "Zinc-plated ", "black_oxide" => "Black-oxide ", "polished" => "Polished ", _ => "" };
            return f + m;
        }

        /// <summary>Each painted definition gets its own shade of its paint family (red ->
        /// vermilion, maroon, orange...; blue -> navy, teal...), so painted parts are told
        /// apart by colour, not just by name. Deterministic per id.</summary>
        static Color PaintShade(Color c, int id)
        {
            uint h = (uint)id * 2654435761u;
            float u = (h & 0xff) / 255f, v = ((h >> 8) & 0xff) / 255f, w = ((h >> 16) & 0xff) / 255f;
            Color.RGBToHSV(c, out var hue, out var sat, out var val);
            hue = Mathf.Repeat(hue + (u - 0.5f) * 0.12f, 1f);
            sat = Mathf.Clamp01(sat * (0.75f + 0.45f * v));
            val = Mathf.Clamp(val * (0.7f + 0.5f * w), 0.12f, 0.95f);
            return Color.HSVToRGB(hue, sat, val);
        }

        static void Weight(ItemDefinition d, float vol) => d.weightG = Snap(vol / 1000f * Density[d.material], 0.1f);

        ItemDefinition Build(int id)
        {
            var d = new ItemDefinition { id = id, category = CategoryOf(id), threadSize = "", restMode = RestMode.Flat, craftingPurpose = "" };
            var i = Decode(id);
            d.storageCategory = ItemDefinition.CategoryNames[d.category];
            float vs = DefaultVisualScale;
            switch ((Cat)d.category)
            {
                case Cat.Screw:
                {
                    var t = Threads[i[1]];
                    d.subcategory = ScrewTypes[i[0]];
                    d.shape = d.subcategory switch { "hex_bolt" => "Hex Bolt", "socket_cap" => "Socket Cap Screw", "pan_head" => "Pan Head Screw", "countersunk" => "Countersunk Screw", "carriage" => "Carriage Bolt", _ => "Thumb Screw" };
                    d.threadSize = t.name; d.threadPitchMm = t.pitch; d.diameterMm = t.dia; d.lengthMm = ScrewLengths[i[2]];
                    SetMaterial(d, i[3], i[4]);
                    float a = Nearest(new[] { 1.5f, 2, 2.5f, 3, 4, 5, 6, 8, 10 }, d.lengthMm / d.diameterMm);
                    d.geometryKey = $"screw:{d.subcategory}:a{Fmt(a)}";
                    d.worldScale = d.diameterMm / 1000f * vs;
                    d.restMode = RestMode.Long;
                    d.displayName = $"{d.threadSize}×{Fmt(d.lengthMm)} {MaterialLabel(d)} {d.shape}";
                    Weight(d, Mathf.PI * Mathf.Pow(d.diameterMm * 0.5f, 2) * (d.lengthMm + d.diameterMm * 0.7f));
                    break;
                }
                case Cat.Nut:
                {
                    var t = Threads[i[1]];
                    d.subcategory = NutTypes[i[0]];
                    d.shape = d.subcategory switch { "hex" => "Hex Nut", "nyloc" => "Nyloc Nut", "square" => "Square Nut", "acorn" => "Acorn Nut", "flange" => "Flange Nut", _ => "Wing Nut" };
                    d.threadSize = t.name; d.threadPitchMm = t.pitch; d.diameterMm = t.dia * 1.8f; d.lengthMm = t.dia * 0.8f;
                    SetMaterial(d, i[2], i[3]);
                    d.geometryKey = "nut:" + d.subcategory;
                    d.worldScale = t.dia / 1000f * vs;
                    d.displayName = $"{d.threadSize} {MaterialLabel(d)} {d.shape}";
                    Weight(d, Mathf.PI * (Mathf.Pow(d.diameterMm * 0.5f, 2) - Mathf.Pow(t.dia * 0.5f, 2)) * d.lengthMm);
                    break;
                }
                case Cat.Washer:
                {
                    var t = Threads[i[1]];
                    d.subcategory = WasherTypes[i[0]];
                    d.shape = d.subcategory switch { "flat" => "Flat Washer", "split" => "Split Lock Washer", "fender" => "Fender Washer", "toothed" => "Toothed Lock Washer", _ => "Square Washer" };
                    d.threadSize = t.name;
                    d.diameterMm = t.dia * (d.subcategory == "fender" ? 3.2f : 2f); d.lengthMm = t.dia * 0.2f;
                    SetMaterial(d, i[2], i[3]);
                    d.geometryKey = "washer:" + d.subcategory;
                    d.worldScale = t.dia / 1000f * vs;
                    d.displayName = $"{d.threadSize} {MaterialLabel(d)} {d.shape}";
                    Weight(d, Mathf.PI * (Mathf.Pow(d.diameterMm * 0.5f, 2) - Mathf.Pow(t.dia * 0.55f, 2)) * d.lengthMm);
                    break;
                }
                case Cat.Gear:
                {
                    d.subcategory = GearTypes[i[0]];
                    d.shape = d.subcategory switch { "spur" => "Spur Gear", "pinion" => "Pinion Gear", "sprocket" => "Sprocket", "bevel" => "Bevel Gear", _ => "Ratchet Wheel" };
                    int teeth = GearTeeth[i[1]]; float module = GearModules[i[2]], pd = teeth * module;
                    d.diameterMm = pd + 2 * module; d.lengthMm = Mathf.Clamp(module * 6, 3, 12);
                    SetMaterial(d, i[3], i[4]);
                    d.geometryKey = $"gear:{d.subcategory}:t{teeth}";
                    d.worldScale = d.diameterMm / 1000f * vs;
                    d.displayName = $"{teeth}T {MaterialLabel(d)} {d.shape} (m{Fmt(module)})";
                    Weight(d, Mathf.PI * Mathf.Pow(pd * 0.5f, 2) * d.lengthMm * 0.8f);
                    break;
                }
                case Cat.Spring:
                {
                    d.subcategory = SpringTypes[i[0]];
                    d.shape = Cap(d.subcategory) + " Spring";
                    float wire = SpringWires[i[1]], coil = SpringCoils[i[2]];
                    d.diameterMm = coil; d.lengthMm = SpringLengths[i[3]];
                    SetMaterial(d, i[4], i[5]);
                    float c = Nearest(new[] { 4f, 6, 8, 11, 15 }, coil / wire), l = Nearest(new[] { 0.75f, 1, 1.5f, 2, 3, 4, 6 }, d.lengthMm / coil);
                    d.geometryKey = $"spring:{d.subcategory}:c{Fmt(c)}:l{Fmt(l)}";
                    d.worldScale = coil / 1000f * vs;
                    d.restMode = RestMode.Long;
                    d.displayName = $"{Fmt(coil)}×{Fmt(d.lengthMm)}mm {MaterialLabel(d)} {d.shape} ({Fmt(wire)}mm wire)";
                    Weight(d, Mathf.PI * Mathf.Pow(wire * 0.5f, 2) * Mathf.PI * coil * (d.lengthMm / wire) * 0.5f);
                    break;
                }
                case Cat.Bearing:
                {
                    d.subcategory = BearingTypes[i[0]];
                    d.shape = Cap(d.subcategory) + " Bearing";
                    float bore = BearingBores[i[1]], od = Mathf.Round(bore * 2.2f + 6);
                    d.diameterMm = od; d.lengthMm = Mathf.Round(od * BearingWidths[i[2]]);
                    SetMaterial(d, i[3], i[4]);
                    d.geometryKey = $"bearing:{d.subcategory}:w{i[2]}";
                    d.worldScale = od / 1000f * vs;
                    d.displayName = $"{Fmt(bore)}mm-bore {MaterialLabel(d)} {d.shape} ({Fmt(od)}×{Fmt(d.lengthMm)})";
                    Weight(d, Mathf.PI * (Mathf.Pow(od * 0.5f, 2) - Mathf.Pow(bore * 0.5f, 2)) * d.lengthMm * 0.6f);
                    break;
                }
                case Cat.Shaft:
                {
                    d.subcategory = ShaftTypes[i[0]];
                    d.shape = d.subcategory switch { "shaft" => "Drive Shaft", "dowel_pin" => "Dowel Pin", "clevis_pin" => "Clevis Pin", "threaded_rod" => "Threaded Rod", _ => "Spring Pin" };
                    d.diameterMm = ShaftDiameters[i[1]]; d.lengthMm = ShaftLengths[i[2]];
                    SetMaterial(d, i[3], i[4]);
                    if (d.subcategory == "threaded_rod") { d.threadSize = "M" + Fmt(d.diameterMm); d.threadPitchMm = Snap(0.15f * d.diameterMm + 0.1f, 0.05f); }
                    float a = Nearest(new[] { 2f, 3, 4, 5, 6, 8, 10, 12, 15 }, d.lengthMm / d.diameterMm);
                    d.geometryKey = $"shaft:{d.subcategory}:a{Fmt(a)}";
                    d.worldScale = d.diameterMm / 1000f * vs;
                    d.restMode = RestMode.Long;
                    d.displayName = $"Ø{Fmt(d.diameterMm)}×{Fmt(d.lengthMm)} {MaterialLabel(d)} {d.shape}";
                    Weight(d, Mathf.PI * Mathf.Pow(d.diameterMm * 0.5f, 2) * d.lengthMm);
                    break;
                }
                case Cat.Electronic:
                {
                    d.subcategory = ElectronicTypes[i[0]];
                    d.shape = Cap(d.subcategory).Replace("Ic Chip", "IC Chip").Replace("Led", "LED");
                    float size = ElectronicSizeMm[i[2]];
                    d.material = "electronic"; d.finish = "plain";
                    ColorUtility.TryParseHtmlString(ElectronicColors[d.subcategory][i[3]], out d.variantColor);
                    d.diameterMm = size;
                    d.lengthMm = size * (d.subcategory == "resistor" || d.subcategory == "capacitor" ? 2.2f : 1.2f);
                    d.magnetic = d.subcategory == "relay";
                    d.rarity = d.subcategory is "ic_chip" or "relay" or "potentiometer" ? 1 : 0;
                    d.geometryKey = "elec:" + d.subcategory;
                    d.worldScale = size / 1000f * vs;
                    if (d.subcategory is "resistor" or "capacitor" or "led" or "transistor") d.restMode = RestMode.Long;
                    d.displayName = $"{ElectronicValues[d.subcategory][i[1]]} {d.shape} ({ElectronicSizes[i[2]]})";
                    d.weightG = Snap(size * size * 0.012f, 0.1f);
                    break;
                }
                case Cat.Specialty:
                {
                    d.subcategory = SpecialtyTypes[i[0]];
                    d.shape = d.subcategory switch { "cam" => "Cam Lobe", "pulley" => "V-Pulley", "coupling" => "Shaft Coupling", "hinge" => "Butt Hinge", "knob" => "Knurled Knob", "u_joint" => "Universal Joint", _ => "Flywheel" };
                    float size = SpecialtySizes[i[1]];
                    d.diameterMm = size;
                    d.lengthMm = size * (d.subcategory is "coupling" or "u_joint" or "hinge" ? 1.6f : 0.45f);
                    SetMaterial(d, i[2], i[3]);
                    d.geometryKey = "spec:" + d.subcategory;
                    d.worldScale = size / 1000f * vs;
                    if (d.subcategory is "coupling" or "u_joint") d.restMode = RestMode.Long;
                    d.displayName = $"{Fmt(size)}mm {MaterialLabel(d)} {d.shape}";
                    Weight(d, size * size * d.lengthMm * 0.5f);
                    break;
                }
                case Cat.Machine:
                {
                    d.subcategory = MachineTypes[i[0]];
                    d.shape = d.subcategory switch
                    {
                        "bracket" => "Mounting Bracket", "motor_housing" => "Motor Housing", "gearbox" => "Gearbox", "valve" => "Gate Valve",
                        "pump_housing" => "Pump Housing", "camshaft" => "Camshaft", "crankshaft" => "Crankshaft", "pipe_elbow" => "Flanged Pipe Elbow",
                        "lever_arm" => "Lever Arm", _ => "Sprocket Hub Assembly",
                    };
                    float size = MachineSizes[i[1]];
                    bool longPart = d.subcategory is "camshaft" or "crankshaft" or "lever_arm";
                    d.diameterMm = size; d.lengthMm = size * (longPart ? 1.8f : 1f);
                    SetMaterial(d, i[2], i[3]);
                    d.geometryKey = "mach:" + d.subcategory;
                    d.worldScale = size / 1000f * vs;
                    if (longPart) d.restMode = RestMode.Long;
                    d.displayName = $"{Fmt(size)}mm {MaterialLabel(d)} {d.shape}";
                    Weight(d, size * size * size * 0.25f);
                    break;
                }
            }
            if (d.category != (int)Cat.Electronic)
            {
                d.magnetic = MagneticMaterials.Contains(d.material);
                d.rarity = Mathf.Min(3, MaterialRarity[d.material] + (d.finish == "polished" ? 1 : 0));
                if (d.material == "painted") d.variantColor = PaintShade(Paints[d.finish], d.id);
            }
            d.meshScaleMm = Snap(d.worldScale * 1000f / vs, 0.001f);
            d.visualScale = vs;
            return d;
        }
    }
}
