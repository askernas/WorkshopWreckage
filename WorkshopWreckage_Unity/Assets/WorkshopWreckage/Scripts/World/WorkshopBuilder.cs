using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static WorkshopWreckage.Batch;

namespace WorkshopWreckage
{
    /// <summary>Builds the static workshop (port of Godot WorkshopBuilder +
    /// WorkshopLighting): room, furniture, lamps, sun, sky, post-processing.</summary>
    public class WorkshopBuilder : MonoBehaviour
    {
        public WorkshopLayout layout;
        public readonly List<Light> lamps = new();
        public Light sun;
        public ReflectionProbe probe;

        public void Build(WorkshopLayout l)
        {
            layout = l;
            BuildRoom();
            foreach (var f in l.furniture) BuildFurniture(f);
            foreach (var p in l.lamps) BuildLamp(p);
            BuildLighting();
        }

        Transform Group(string name, Vector3 posG, float rotG = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Shared.Pos(posG.x, posG.y, posG.z);
            go.transform.localRotation = Shared.YRot(rotG);
            return go.transform;
        }

        static Vector3 V(object o) => WorkshopLayout.V(o);

        // ---------------------------------------------------------------- room

        void BuildRoom()
        {
            var lo = layout.roomMin;
            var hi = layout.roomMax;
            var size = hi - lo;
            var c = (lo + hi) * 0.5f;
            var room = Group("Room", Vector3.zero);
            var b = new Batch();
            b.Box(new Vector3(c.x, -0.05f, c.z), new Vector3(size.x + 1, 0.1f, size.z + 1), EnvMaterials.Planks(), true);
            var brick = EnvMaterials.Brick();
            float t = 0.3f;
            b.Box(new Vector3(c.x, size.y / 2, lo.z - t / 2), new Vector3(size.x + 2 * t, size.y, t), brick, true);
            b.Box(new Vector3(c.x, size.y / 2, hi.z + t / 2), new Vector3(size.x + 2 * t, size.y, t), brick, true);
            b.Box(new Vector3(hi.x + t / 2, size.y / 2, c.z), new Vector3(t, size.y, size.z), brick, true);
            float x = lo.x - t / 2;
            var zs = new List<float> { lo.z };
            foreach (var w in layout.windows)
            {
                float wz = (float)Json.Num(w, "z"), ww = (float)Json.Num(w, "width");
                zs.Add(wz - ww / 2);
                zs.Add(wz + ww / 2);
            }
            zs.Add(hi.z);
            for (int i = 0; i < zs.Count; i += 2)
                b.Box(new Vector3(x, size.y / 2, (zs[i] + zs[i + 1]) / 2), new Vector3(t, size.y, zs[i + 1] - zs[i]), brick, true);
            var frame = EnvMaterials.Wood(new Color(0.25f, 0.16f, 0.1f));
            foreach (var w in layout.windows)
            {
                float wz = (float)Json.Num(w, "z"), ww = (float)Json.Num(w, "width"), y0 = (float)Json.Num(w, "y0"), y1 = (float)Json.Num(w, "y1");
                b.Box(new Vector3(x, y0 / 2, wz), new Vector3(t, y0, ww), brick, true);
                b.Box(new Vector3(x, (y1 + size.y) / 2, wz), new Vector3(t, size.y - y1, ww), brick, true);
                b.Box(new Vector3(x + 0.12f, y0 - 0.03f, wz), new Vector3(0.36f, 0.06f, ww + 0.1f), EnvMaterials.Wood(new Color(0.5f, 0.36f, 0.22f)));
                for (int k = 0; k < 5; k++)
                    b.Box(new Vector3(x, (y0 + y1) / 2, wz - ww / 2 + ww * k / 4f), new Vector3(0.08f, y1 - y0, 0.05f), frame);
                for (int k = 0; k < 7; k++)
                    b.Box(new Vector3(x, y0 + (y1 - y0) * k / 6f, wz), new Vector3(0.08f, 0.05f, ww), frame);
            }
            b.Box(new Vector3(c.x, hi.y + 0.05f, c.z), new Vector3(size.x + 1, 0.1f, size.z + 1), EnvMaterials.Wood(new Color(0.3f, 0.2f, 0.13f)), true);
            for (int k = 0; k < 7; k++)
            {
                float bz = lo.z + 0.6f + k * (size.z - 1.2f) / 6f;
                b.Box(new Vector3(c.x, hi.y - 0.18f, bz), new Vector3(size.x, 0.32f, 0.22f), EnvMaterials.Wood(new Color(0.33f, 0.21f, 0.12f)));
            }
            var skirt = EnvMaterials.Wood(new Color(0.22f, 0.14f, 0.09f));
            b.Box(new Vector3(c.x, 0.08f, lo.z + 0.015f), new Vector3(size.x, 0.16f, 0.03f), skirt);
            b.Box(new Vector3(c.x, 0.08f, hi.z - 0.015f), new Vector3(size.x, 0.16f, 0.03f), skirt);
            b.Box(new Vector3(hi.x - 0.015f, 0.08f, c.z), new Vector3(0.03f, 0.16f, size.z), skirt);
            b.Build(room, "Room");
        }

        // ---------------------------------------------------------------- furniture

        void BuildFurniture(Dictionary<string, object> f)
        {
            var pos = V(f["pos"]);
            var size = V(f["size"]);
            float rot = (float)Json.Num(f, "rot");
            string type = Json.Str(f, "type");
            var g = Group(type, pos, rot);
            var b = new Batch();
            switch (type)
            {
                case "bench": Bench(b, size, Json.Bool(f, "shelf")); break;
                case "pegboard": Pegboard(b, size); break;
                case "overturned_chest":
                    b.Box(new Vector3(0, size.z / 2, 0), new Vector3(size.y, size.z, size.x), EnvMaterials.Metal(new Color(0.55f, 0.15f, 0.1f), 0.45f), true);
                    for (int k = 0; k < 3; k++)
                        b.Box(new Vector3(size.y / 2 + 0.06f + k * 0.03f, size.z / 2, -size.x * 0.33f + k * size.x * 0.33f), new Vector3(0.12f, size.z * 0.7f, size.x * 0.28f), EnvMaterials.Metal(new Color(0.62f, 0.2f, 0.12f), 0.5f));
                    break;
                case "tipped_crate":
                {
                    var m = EnvMaterials.Wood(new Color(0.55f, 0.4f, 0.24f));
                    var tilt = R(Vector3.right, 1.75f);
                    b.Box(new Vector3(0, size.z / 2, 0), new Vector3(size.x, size.y, size.z), m, true, tilt);
                    break;
                }
                case "tipped_bin":
                    b.Box(new Vector3(0, size.y / 2, 0), new Vector3(size.x, size.y, size.z), EnvMaterials.Metal(new Color(0.18f, 0.35f, 0.62f), 0.3f), true, R(Vector3.forward, 1.5f));
                    break;
                case "tipped_box":
                    b.Box(new Vector3(0, size.x / 2, 0), new Vector3(size.x, size.y, size.z), EnvMaterials.Plain(new Color(0.62f, 0.45f, 0.28f), 0, 0.1f), true, R(Vector3.forward, 1.57f));
                    break;
                case "fallen_drawer":
                {
                    bool upside = Json.Bool(f, "upside_down");
                    var tilt = R(Vector3.right, upside ? Mathf.PI : 0.18f);
                    var m = EnvMaterials.Wood(new Color(0.4f, 0.26f, 0.15f));
                    float h = size.y;
                    var o = new Vector3(0, h / 2 + 0.02f, 0);
                    void Part(Vector3 c, Vector3 s, Material mat) => b.Box(o + Unmirror(tilt * G(c)), s, mat, false, tilt);
                    Part(new Vector3(0, -h / 2 + 0.01f, 0), new Vector3(size.x, 0.02f, size.z), m);
                    Part(new Vector3(-size.x / 2, 0, 0), new Vector3(0.02f, h, size.z), m);
                    Part(new Vector3(size.x / 2, 0, 0), new Vector3(0.02f, h, size.z), m);
                    Part(new Vector3(0, 0, -size.z / 2), new Vector3(size.x, h, 0.02f), m);
                    Part(new Vector3(0, 0, size.z / 2), new Vector3(size.x + 0.04f, h + 0.04f, 0.025f), EnvMaterials.Metal(new Color(0.25f, 0.38f, 0.58f), 0.5f));
                    break;
                }
                case "stool":
                {
                    bool fallen = Json.Bool(f, "fallen");
                    var m = EnvMaterials.Wood(new Color(0.5f, 0.33f, 0.2f));
                    var tilt = fallen ? R(Vector3.right, Mathf.PI / 2) : Quaternion.identity;
                    var o = fallen ? new Vector3(0, size.x / 2, 0) : Vector3.zero;
                    b.Cylinder(o + Unmirror(tilt * G(0, size.y - 0.025f, 0)), size.x / 2, 0.05f, m, tilt, 20);
                    for (int k = 0; k < 3; k++)
                    {
                        float a = Mathf.PI * 2 * k / 3;
                        var lean = R(new Vector3(Mathf.Cos(a + Mathf.PI / 2), 0, Mathf.Sin(a + Mathf.PI / 2)), 0.12f);
                        var lp = new Vector3(Mathf.Cos(a) * size.x * 0.33f, (size.y - 0.05f) / 2, Mathf.Sin(a) * size.x * 0.33f);
                        b.Cylinder(o + Unmirror(tilt * G(lp)), 0.02f, size.y - 0.05f, m, tilt * lean, 8);
                    }
                    break;
                }
                case "barrel":
                    b.Cylinder(new Vector3(0, size.y / 2, 0), size.x / 2, size.y, EnvMaterials.Metal(new Color(0.2f, 0.32f, 0.48f), 0.55f), null, 24);
                    foreach (var yy in new[] { 0.15f, 0.5f, 0.85f })
                        b.Cylinder(new Vector3(0, size.y * yy, 0), size.x * 0.51f, 0.03f, EnvMaterials.Plain(new Color(0.15f, 0.15f, 0.16f), 0.8f, 0.5f), null, 24);
                    b.colliders.Add((G(0, size.y / 2, 0), new Vector3(size.x, size.y, size.x), Quaternion.identity));
                    break;
                case "machine": Machine(g, b, size); break;
                case "door":
                    b.Box(new Vector3(0, size.y / 2, 0), new Vector3(size.x, size.y, 0.06f), EnvMaterials.Wood(new Color(0.35f, 0.22f, 0.13f)));
                    b.Box(new Vector3(0, size.y + 0.05f, 0), new Vector3(size.x + 0.2f, 0.1f, 0.1f), EnvMaterials.Wood(new Color(0.25f, 0.16f, 0.1f)));
                    break;
                case "sign": Sign(g, b, size); break;
                case "clock": Clock(b, size); break;
                case "blueprint":
                    b.Quad(Vector3.zero, new Vector2(Mathf.Max(size.x, size.z), size.y), EnvMaterials.Blueprint(((int)Mathf.Abs(pos.x)) % 2), new Rect(0, 0, 1, 1));
                    break;
                case "plant": Plant(b, pos); break;
            }
            b.Build(g, type);
        }

        /// <summary>Unity-local point back to the Godot-local convention Batch expects.</summary>
        static Vector3 Unmirror(Vector3 u) => new Vector3(u.x, u.y, -u.z);

        void Bench(Batch b, Vector3 size, bool shelf)
        {
            var top = EnvMaterials.Wood(new Color(0.47f, 0.31f, 0.18f));
            var frame = EnvMaterials.Wood(new Color(0.32f, 0.2f, 0.12f));
            float th = 0.07f;
            b.Box(new Vector3(0, size.y - th / 2, 0), new Vector3(size.x, th, size.z), top, true);
            b.Box(new Vector3(0, size.y - th - 0.06f, size.z / 2 - 0.05f), new Vector3(size.x - 0.1f, 0.12f, 0.04f), frame);
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    b.Box(new Vector3(sx * (size.x / 2 - 0.07f), (size.y - th) / 2, sz * (size.z / 2 - 0.07f)), new Vector3(0.09f, size.y - th, 0.09f), frame, true);
            if (shelf) b.Box(new Vector3(0, 0.22f, 0), new Vector3(size.x - 0.12f, 0.04f, size.z - 0.12f), frame);
            var steel = EnvMaterials.Metal(new Color(0.2f, 0.3f, 0.42f), 0.5f);
            b.Box(new Vector3(size.x / 2 - 0.35f, size.y + 0.07f, size.z / 2 - 0.15f), new Vector3(0.24f, 0.14f, 0.18f), steel);
            b.Box(new Vector3(size.x / 2 - 0.35f, size.y + 0.07f, size.z / 2), new Vector3(0.22f, 0.12f, 0.04f), steel);
        }

        void Pegboard(Batch b, Vector3 size)
        {
            b.Box(Vector3.zero, size, EnvMaterials.Wood(new Color(0.55f, 0.42f, 0.28f)));
            var steel = EnvMaterials.Plain(new Color(0.55f, 0.56f, 0.58f), 1, 0.65f);
            var red = EnvMaterials.Plain(new Color(0.6f, 0.1f, 0.08f), 0.1f, 0.5f);
            var handle = EnvMaterials.Wood(new Color(0.4f, 0.25f, 0.12f));
            var rng = new System.Random(42);
            float R01() => (float)rng.NextDouble();
            float x = -size.x / 2 + 0.25f;
            while (x < size.x / 2 - 0.2f)
            {
                int kind = rng.Next(0, 4);
                float y = -0.2f + R01() * 0.45f;
                switch (kind)
                {
                    case 0:
                        float l = 0.18f + R01() * 0.16f;
                        b.Box(new Vector3(x, y, 0.04f), new Vector3(0.025f, l, 0.008f), steel);
                        b.Cylinder(new Vector3(x, y + l / 2, 0.04f), 0.03f, 0.01f, steel, R(Vector3.right, Mathf.PI / 2), 8);
                        break;
                    case 1:
                        b.Cylinder(new Vector3(x, y - 0.06f, 0.05f), 0.016f, 0.12f, R01() < 0.5f ? red : handle, null, 8);
                        b.Cylinder(new Vector3(x, y + 0.07f, 0.05f), 0.004f, 0.16f, steel, null, 6);
                        break;
                    case 2:
                        b.Cylinder(new Vector3(x, y - 0.05f, 0.05f), 0.014f, 0.3f, handle, null, 8);
                        b.Box(new Vector3(x, y + 0.11f, 0.05f), new Vector3(0.12f, 0.035f, 0.035f), steel);
                        break;
                    default:
                        b.Box(new Vector3(x - 0.012f, y, 0.04f), new Vector3(0.012f, 0.2f, 0.01f), red, false, R(Vector3.forward, 0.08f));
                        b.Box(new Vector3(x + 0.012f, y, 0.04f), new Vector3(0.012f, 0.2f, 0.01f), red, false, R(Vector3.forward, -0.08f));
                        break;
                }
                x += 0.12f + R01() * 0.1f;
            }
        }

        public Transform machineSpindle, machineHandwheel;
        public Light machineLamp;
        public AudioSource machineHum;

        void Machine(Transform g, Batch b, Vector3 size)
        {
            var iron = EnvMaterials.Metal(new Color(0.2f, 0.28f, 0.24f), 0.4f);
            var steel = EnvMaterials.Plain(new Color(0.6f, 0.6f, 0.62f), 1, 0.7f);
            b.Box(new Vector3(-size.x * 0.4f, 0.4f, 0), new Vector3(0.3f, 0.8f, size.z * 0.8f), iron, true);
            b.Box(new Vector3(size.x * 0.4f, 0.4f, 0), new Vector3(0.3f, 0.8f, size.z * 0.8f), iron, true);
            b.Box(new Vector3(0, 0.86f, 0), new Vector3(size.x, 0.12f, size.z * 0.5f), iron, true);
            b.Box(new Vector3(-size.x * 0.35f, 1.08f, 0), new Vector3(0.45f, 0.34f, 0.4f), iron);
            b.Box(new Vector3(size.x * 0.32f, 1.02f, 0), new Vector3(0.3f, 0.22f, 0.3f), iron);
            // spindle + chuck and the handwheel turn once the machine is repaired (restoration)
            machineSpindle = new GameObject("Spindle").transform;
            machineSpindle.SetParent(g, false);
            machineSpindle.localPosition = G(0, 1.08f, 0);
            var sb = new Batch();
            sb.Cylinder(Vector3.zero, 0.03f, size.x * 0.5f, steel, R(Vector3.forward, Mathf.PI / 2), 12);
            sb.Cylinder(new Vector3(-size.x * 0.12f, 0, 0), 0.08f, 0.06f, steel, R(Vector3.forward, Mathf.PI / 2), 6);
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI * 2 / 3;
                sb.Box(new Vector3(-size.x * 0.12f - 0.035f, Mathf.Sin(a) * 0.06f, Mathf.Cos(a) * 0.06f), new Vector3(0.03f, 0.025f, 0.025f), EnvMaterials.Plain(new Color(0.3f, 0.3f, 0.32f), 1, 0.5f));   // chuck jaws
            }
            sb.Build(machineSpindle, "spindle");
            machineHandwheel = new GameObject("Handwheel").transform;
            machineHandwheel.SetParent(g, false);
            machineHandwheel.localPosition = G(-size.x * 0.58f, 1.2f, 0);
            var hb = new Batch();
            hb.Cylinder(Vector3.zero, 0.2f, 0.06f, EnvMaterials.Plain(new Color(0.55f, 0.12f, 0.08f), 0.2f, 0.5f), R(Vector3.forward, Mathf.PI / 2), 24);
            hb.Box(new Vector3(-0.04f, 0.14f, 0), new Vector3(0.03f, 0.03f, 0.03f), EnvMaterials.Plain(new Color(0.2f, 0.2f, 0.2f), 0.8f, 0.5f));   // grip
            hb.Build(machineHandwheel, "handwheel");
            var lg = new GameObject("WorkLamp");
            lg.transform.SetParent(g, false);
            lg.transform.localPosition = G(size.x * 0.1f, 1.6f, 0.1f);
            machineLamp = lg.AddComponent<Light>();
            machineLamp.type = LightType.Point;
            machineLamp.color = new Color(1f, 0.85f, 0.6f);
            machineLamp.range = 2.5f;
            machineLamp.intensity = 0;
            machineHum = g.gameObject.AddComponent<AudioSource>();
            machineHum.spatialBlend = 1; machineHum.minDistance = 1; machineHum.maxDistance = 12; machineHum.loop = true; machineHum.volume = 0;
        }

        void Sign(Transform g, Batch b, Vector3 size)
        {
            b.Box(Vector3.zero, size, EnvMaterials.Wood(new Color(0.22f, 0.14f, 0.09f)));
            var trim = EnvMaterials.Plain(new Color(0.7f, 0.52f, 0.25f), 1, 0.65f);
            b.Box(new Vector3(0, size.y / 2, 0.06f), new Vector3(size.x, 0.05f, 0.03f), trim);
            b.Box(new Vector3(0, -size.y / 2, 0.06f), new Vector3(size.x, 0.05f, 0.03f), trim);
            var tex = TextAtlas.RenderSingle("WORKSHOP\nWRECKAGE", 1024, 300, new Color(0.22f, 0.14f, 0.09f), new Color(0.93f, 0.68f, 0.3f), 120, true);
            b.Quad(new Vector3(0, 0, 0.056f), new Vector2(size.x * 0.95f, size.y * 0.85f), EnvMaterials.Atlas(tex), new Rect(0, 0, 1, 1));
        }

        void Clock(Batch b, Vector3 size)
        {
            float r = size.x / 2;
            b.Cylinder(new Vector3(0, 0, 0.02f), r, 0.06f, EnvMaterials.Plain(new Color(0.6f, 0.45f, 0.22f), 1, 0.65f), R(Vector3.right, Mathf.PI / 2), 32);
            b.Cylinder(new Vector3(0, 0, 0.055f), r * 0.88f, 0.01f, EnvMaterials.Plain(new Color(0.92f, 0.86f, 0.7f), 0, 0.4f), R(Vector3.right, Mathf.PI / 2), 32);
            var ink = EnvMaterials.Plain(new Color(0.08f, 0.06f, 0.05f));
            for (int k = 0; k < 12; k++)
            {
                float a = Mathf.PI * 2 * k / 12;
                b.Box(new Vector3(Mathf.Sin(a) * r * 0.75f, Mathf.Cos(a) * r * 0.75f, 0.065f), new Vector3(0.02f, 0.05f, 0.005f), ink, false, R(Vector3.forward, -a));
            }
            b.Box(new Vector3(0.06f, 0.08f, 0.07f), new Vector3(0.02f, 0.22f, 0.005f), ink, false, R(Vector3.forward, -0.6f));
            b.Box(new Vector3(0.1f, -0.02f, 0.072f), new Vector3(0.015f, 0.3f, 0.005f), ink, false, R(Vector3.forward, -1.9f));
        }

        void Plant(Batch b, Vector3 pos)
        {
            b.Cylinder(new Vector3(0, 0.1f, 0), 0.12f, 0.2f, EnvMaterials.Plain(new Color(0.6f, 0.3f, 0.18f), 0, 0.2f), null, 16);
            var leaf = EnvMaterials.Plain(new Color(0.22f, 0.42f, 0.18f), 0, 0.3f);
            var rng = new System.Random((int)(pos.z * 100));
            for (int k = 0; k < 9; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2;
                var tilt = R(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)), 0.3f + (float)rng.NextDouble() * 0.6f);
                b.Box(new Vector3(Mathf.Cos(a) * 0.05f, 0.3f, Mathf.Sin(a) * 0.05f), new Vector3(0.06f, 0.28f, 0.01f), leaf, false, tilt * R(Vector3.up, a));
            }
        }

        void BuildLamp(Vector3 pG)
        {
            var g = Group("Lamp", pG);
            var b = new Batch();
            float cord = layout.roomMax.y - pG.y;
            b.Cylinder(new Vector3(0, cord / 2, 0), 0.008f, cord, EnvMaterials.Plain(new Color(0.1f, 0.1f, 0.1f)), null, 6);
            var shade = EnvMaterials.Plain(new Color(0.18f, 0.3f, 0.22f), 0.6f, 0.6f);
            // conical shade approximated by stacked rings
            for (int k = 0; k < 4; k++)
            {
                float f = k / 3f;
                b.Cylinder(new Vector3(0, 0.1f - f * 0.2f, 0), Mathf.Lerp(0.08f, 0.27f, f), 0.06f, shade, null, 24);
            }
            b.Build(g, "Lamp");
            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(bulb.GetComponent<Collider>());
            bulb.transform.SetParent(g, false);
            bulb.transform.localPosition = new Vector3(0, -0.1f, 0);
            bulb.transform.localScale = Vector3.one * 0.12f;
            var bm = bulb.GetComponent<MeshRenderer>();
            bm.sharedMaterial = EnvMaterials.Plain(new Color(1f, 0.85f, 0.6f), 0, 0.5f, 6f);
            bm.shadowCastingMode = ShadowCastingMode.Off;
            var lgo = new GameObject("Light");
            lgo.transform.SetParent(g, false);
            lgo.transform.localPosition = new Vector3(0, -0.16f, 0);
            var light = lgo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.78f, 0.52f);
            light.intensity = 3.2f;
            light.range = 7f;
            light.shadows = lamps.Count < 3 ? LightShadows.Soft : LightShadows.None;
            lamps.Add(light);
        }

        // ---------------------------------------------------------------- lighting

        void BuildLighting()
        {
            foreach (var l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional) Destroy(l.gameObject);
            var go = new GameObject("Sun");
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.82f, 0.6f);
            sun.intensity = 3.2f;
            sun.shadows = LightShadows.Soft;
            // Godot: Basis.from_euler(-32°, -75°) (YXZ); the light shines along -Z.
            var gdir = Quaternion.AngleAxis(-75f, Vector3.up) * Quaternion.AngleAxis(-32f, Vector3.right) * Vector3.back;
            go.transform.rotation = Quaternion.LookRotation(new Vector3(gdir.x, gdir.y, -gdir.z));
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Flat;
            // Godot adds screen-space indirect light (SSIL); URP has none, so ambient is a bit stronger.
            RenderSettings.ambientLight = new Color(0.55f, 0.42f, 0.3f) * 0.5f;
            RenderSettings.reflectionIntensity = 0.7f;
            if (RenderSettings.skybox != null)
            {
                var sky = RenderSettings.skybox = new Material(RenderSettings.skybox);
                if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", new Color(0.55f, 0.65f, 0.85f));
                if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", new Color(0.35f, 0.28f, 0.2f));
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", 1.4f);
            }
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            foreach (var p in new[] { new Vector3(-5, 2.5f, 3.5f), new Vector3(5, 2.5f, 3.5f), new Vector3(5, 2.5f, -3) })
            {
                var fill = new GameObject("Fill").AddComponent<Light>();
                fill.transform.SetParent(transform, false);
                fill.transform.position = Shared.Pos(p.x, p.y, p.z);
                fill.type = LightType.Point;
                fill.color = new Color(1, 0.82f, 0.62f);
                fill.intensity = 1.2f;
                fill.range = 7;
            }

            var vgo = new GameObject("PostProcess");
            vgo.transform.SetParent(transform, false);
            var vol = vgo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.35f);
            bloom.threshold.Override(1.3f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.saturation.Override(8f);
            ca.contrast.Override(5f);
            ca.postExposure.Override(1.0f);   // matches Godot AgX + SSIL brightness at the reference poses
            vol.profile = profile;

            var size = layout.roomMax - layout.roomMin;
            var center = (layout.roomMin + layout.roomMax) * 0.5f;
            var pgo = new GameObject("ReflectionProbe");
            pgo.transform.SetParent(transform, false);
            pgo.transform.position = Shared.Pos(center.x, 1.4f, center.z + 1.0f);
            QualitySettings.realtimeReflectionProbes = true;   // off in the URP template's quality levels
            probe = pgo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = size + Vector3.one * 0.2f;
            probe.center = Shared.Pos(center.x, center.y, center.z) - pgo.transform.position;
            probe.boxProjection = true;
            probe.resolution = 256;
            probe.hdr = true;
        }

        public void RefreshProbe()
        {
            if (probe != null) probe.RenderProbe();
        }
    }
}
