using System.Collections.Generic;
using System.Linq;
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
            for (int k = 0; k < l.fixtures.Count; k++) BuildFixture(l.fixtures[k], k);
            BuildLighting();
            BuildShafts();
            BuildTrayLight();
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
            b.Box(new Vector3(c.x, -0.05f, c.z), new Vector3(size.x + 1, 0.1f, size.z + 1), EnvMaterials.Concrete(), true);
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
            b.Box(new Vector3(c.x, hi.y + 0.05f, c.z), new Vector3(size.x + 1, 0.1f, size.z + 1), EnvMaterials.Plain(new Color(0.72f, 0.72f, 0.7f), 0, 0.15f), true);   // ceiling painted light grey
            for (int k = 0; k < 7; k++)
            {
                float bz = lo.z + 0.6f + k * (size.z - 1.2f) / 6f;
                b.Box(new Vector3(c.x, hi.y - 0.18f, bz), new Vector3(size.x, 0.32f, 0.22f), EnvMaterials.Plain(new Color(0.66f, 0.66f, 0.64f), 0, 0.15f));
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
                    var m = EnvMaterials.CrateWood(new Color(1f, 0.95f, 0.9f));
                    var tilt = R(Vector3.right, 1.75f);
                    b.Box(new Vector3(0, size.z / 2, 0), new Vector3(size.x, size.y, size.z), m, true, tilt);
                    break;
                }
                case "tipped_bin":
                    b.Box(new Vector3(0, size.y / 2, 0), new Vector3(size.x, size.y, size.z), EnvMaterials.Metal(new Color(0.18f, 0.35f, 0.62f), 0.3f), true, R(Vector3.forward, 1.5f));
                    break;
                case "tipped_box":
                    b.Box(new Vector3(0, size.x / 2, 0), new Vector3(size.x, size.y, size.z), EnvMaterials.Cardboard(new Color(0.62f, 0.45f, 0.28f)), true, R(Vector3.forward, 1.57f));
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
                case "sorting_tray":
                {
                    var wood = EnvMaterials.Wood(new Color(0.55f, 0.4f, 0.24f));
                    var dark = EnvMaterials.Wood(new Color(0.35f, 0.24f, 0.14f));
                    b.Box(new Vector3(0, WorkshopLayout.TrayFloor / 2, 0), new Vector3(size.x, WorkshopLayout.TrayFloor, size.z), dark, true);
                    foreach (var sx in new[] { -1f, 1f }) b.Box(new Vector3(sx * size.x / 2, size.y / 2, 0), new Vector3(0.04f, size.y, size.z + 0.04f), wood, true);
                    foreach (var sz in new[] { -1f, 1f }) b.Box(new Vector3(0, size.y / 2, sz * size.z / 2), new Vector3(size.x, size.y, 0.04f), wood, true);
                    b.Box(new Vector3(-size.x / 2 + 0.05f, 0.45f, size.z / 2 + 0.03f), new Vector3(0.04f, 0.9f, 0.04f), dark);   // sign post
                    var sign = TextAtlas.RenderSingle("SORTING TRAY", 512, 128, new Color(0.3f, 0.2f, 0.13f), new Color(0.95f, 0.8f, 0.4f), 60, true);
                    b.Box(new Vector3(-size.x / 2 + 0.3f, 0.82f, size.z / 2 + 0.03f), new Vector3(0.52f, 0.14f, 0.02f), dark);
                    b.Quad(new Vector3(-size.x / 2 + 0.3f, 0.82f, size.z / 2 + 0.015f), new Vector2(0.5f, 0.125f), EnvMaterials.Atlas(sign), new Rect(0, 0, 1, 1), R(Vector3.up, Mathf.PI));
                    break;
                }
                case "door": Door(g, b, size); break;
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
            // a warm sign light from above: the workshop's name always reads, even in the dim upper wall
            var lgo = new GameObject("SignLight");
            lgo.transform.SetParent(g, false);
            lgo.transform.localPosition = G(0, size.y * 0.5f + 0.55f, 0.9f);
            lgo.transform.localRotation = Quaternion.LookRotation(G(0, -0.3f, 0) - lgo.transform.localPosition);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Spot;
            l.spotAngle = 95; l.innerSpotAngle = 60; l.range = 3.5f; l.intensity = 2.6f;
            l.color = new Color(1f, 0.8f, 0.55f);
            l.shadows = LightShadows.None;
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
            lgo.transform.localPosition = new Vector3(0, -0.12f, 0);
            lgo.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var light = lgo.AddComponent<Light>();
            // an enamel shade only throws light down: a warm pool below, nothing on the ceiling
            light.type = LightType.Spot;
            light.spotAngle = 112;
            light.innerSpotAngle = 58;
            light.color = new Color(1f, 0.76f, 0.5f);
            light.intensity = 7f;
            light.range = 7f;
            light.shadows = lamps.Count < 3 ? LightShadows.Soft : LightShadows.None;
            lamps.Add(light);
        }

        /// <summary>The entry door (south wall): a battered green steel door in a dark frame
        /// with a wired-glass window, push bar and kick plate, and a glowing EXIT sign above.
        /// Aiming at it offers to leave the workshop (quit, with a prompt).</summary>
        void Door(Transform g, Batch b, Vector3 size)
        {
            var frame = EnvMaterials.Metal(new Color(0.16f, 0.17f, 0.17f), 0.25f);
            var leaf = EnvMaterials.Metal(new Color(0.22f, 0.36f, 0.28f), 0.55f);
            var steel = EnvMaterials.Plain(new Color(0.6f, 0.6f, 0.58f), 1, 0.6f);
            var glass = EnvMaterials.Plain(new Color(0.55f, 0.62f, 0.6f), 0.2f, 0.92f, 0.25f);
            float w = size.x, h = size.y;
            b.Box(new Vector3(-w / 2 - 0.05f, h / 2 + 0.03f, 0), new Vector3(0.1f, h + 0.06f, 0.16f), frame);
            b.Box(new Vector3(w / 2 + 0.05f, h / 2 + 0.03f, 0), new Vector3(0.1f, h + 0.06f, 0.16f), frame);
            b.Box(new Vector3(0, h + 0.06f, 0), new Vector3(w + 0.2f, 0.12f, 0.16f), frame);
            b.Box(new Vector3(0, h / 2, 0.01f), new Vector3(w, h, 0.05f), leaf);
            foreach (var y in new[] { h * 0.28f, h * 0.72f })   // pressed panels
                b.Box(new Vector3(0, y, -0.018f), new Vector3(w * 0.78f, h * 0.36f, 0.01f), leaf);
            b.Box(new Vector3(0, h * 0.74f, -0.024f), new Vector3(0.36f, 0.5f, 0.01f), frame);
            b.Box(new Vector3(0, h * 0.74f, -0.03f), new Vector3(0.3f, 0.44f, 0.004f), glass);
            for (int k = 0; k < 5; k++) b.Box(new Vector3(0, h * 0.74f - 0.2f + k * 0.1f, -0.033f), new Vector3(0.3f, 0.003f, 0.002f), steel);   // wire mesh
            b.Box(new Vector3(0, 0.12f, -0.02f), new Vector3(w * 0.92f, 0.22f, 0.006f), steel);   // kick plate
            b.Box(new Vector3(0, 1.0f, -0.07f), new Vector3(w * 0.8f, 0.05f, 0.05f), steel);       // push bar
            foreach (var sx in new[] { -1f, 1f }) b.Box(new Vector3(sx * w * 0.38f, 1.0f, -0.045f), new Vector3(0.06f, 0.09f, 0.05f), frame);
            foreach (var y in new[] { 0.3f, h - 0.3f }) b.Box(new Vector3(-w / 2 + 0.02f, y, -0.02f), new Vector3(0.05f, 0.14f, 0.03f), steel);   // hinges
            // EXIT sign
            var sign = TextAtlas.RenderSingle("EXIT", 256, 96, new Color(0.05f, 0.35f, 0.12f), new Color(0.8f, 1f, 0.85f), 64, true);
            b.Box(new Vector3(0, h + 0.32f, -0.06f), new Vector3(0.46f, 0.2f, 0.08f), frame);
            var sm = EnvMaterials.Atlas(sign);
            sm.SetFloat("_Emission", 0.8f);
            b.Quad(new Vector3(0, h + 0.32f, -0.101f), new Vector2(0.42f, 0.16f), sm, new Rect(0, 0, 1, 1));
            var lgo = new GameObject("ExitLight");
            lgo.transform.SetParent(g, false);
            lgo.transform.localPosition = G(0, h + 0.2f, -0.3f);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(0.35f, 1f, 0.5f); l.intensity = 0.6f; l.range = 2.2f;
            var t = new GameObject("Target_Exit") { layer = Layers.Target };
            t.transform.SetParent(g, false);
            t.transform.localPosition = G(0, h / 2, -0.1f);
            t.AddComponent<BoxCollider>().size = new Vector3(w, h, 0.2f);
            t.AddComponent<StorageTarget>().destId = "EXIT";
        }

        /// <summary>An industrial twin-tube fluorescent fixture on two chains: emissive tubes,
        /// white reflector wings, and a wide downward spot light.</summary>
        void BuildFixture(Vector3 pG, int k)
        {
            var g = Group("Fixture", pG);
            var b = new Batch();
            var housing = EnvMaterials.Metal(new Color(0.3f, 0.34f, 0.31f), 0.2f);
            var white = EnvMaterials.Plain(new Color(0.86f, 0.86f, 0.82f), 0.1f, 0.55f);
            var tube = EnvMaterials.Plain(new Color(1f, 0.97f, 0.9f), 0, 0.5f, 6f);
            var chain = EnvMaterials.Plain(new Color(0.2f, 0.2f, 0.2f), 0.9f, 0.5f);
            const float L = 1.5f;
            b.Box(new Vector3(0, 0.06f, 0), new Vector3(L, 0.06f, 0.24f), housing);
            foreach (var s in new[] { -1f, 1f })
            {
                b.Box(new Vector3(0, 0.01f, s * 0.15f), new Vector3(L, 0.006f, 0.12f), white, false, R(Vector3.right, s * 0.55f));
                b.Box(new Vector3(s * (L / 2 - 0.02f), 0.01f, 0), new Vector3(0.03f, 0.07f, 0.2f), housing);   // end caps
            }
            foreach (var z in new[] { -0.045f, 0.045f }) b.Cylinder(new Vector3(0, -0.005f, z), 0.016f, L - 0.08f, tube, R(Vector3.forward, Mathf.PI / 2), 12);
            float hang = layout.roomMax.y - pG.y - 0.09f;
            foreach (var x in new[] { -0.55f, 0.55f }) b.Cylinder(new Vector3(x, 0.09f + hang / 2, 0), 0.006f, hang, chain, null, 5);
            b.Build(g, "Fixture");
            var lgo = new GameObject("Light");
            lgo.transform.SetParent(g, false);
            lgo.transform.localPosition = new Vector3(0, -0.06f, 0);
            lgo.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var light = lgo.AddComponent<Light>();
            light.type = LightType.Spot;
            // look: warm tungsten pools of light (not a flat white flood), cooler daylight between
            light.spotAngle = 140;
            light.innerSpotAngle = 56;
            light.range = 10f;
            light.intensity = 10f;
            light.color = new Color(1f, 0.84f, 0.64f);
            // only a few fixtures cast shadows (performance: shadowed spot lights are the expensive part)
            light.shadows = k < 3 ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = 0.7f;
            lamps.Add(light);
        }

        /// <summary>A wall-mounted floodlight on the south wall shining down onto the sorting tray.</summary>
        void BuildTrayLight()
        {
            var t = layout.trayRect;
            var pG = new Vector3(t.center.x, 2.7f, layout.roomMax.z - 0.12f);
            var g = Group("TrayFlood", pG);
            var b = new Batch();
            var housing = EnvMaterials.Metal(new Color(0.18f, 0.19f, 0.2f), 0.2f);
            b.Box(new Vector3(0, 0, 0.06f), new Vector3(0.08f, 0.08f, 0.12f), housing);                         // wall bracket
            b.Box(new Vector3(0, -0.05f, -0.08f), new Vector3(0.34f, 0.24f, 0.12f), housing, false, R(Vector3.right, -0.9f));
            b.Box(new Vector3(0, -0.1f, -0.13f), new Vector3(0.28f, 0.18f, 0.01f), EnvMaterials.Plain(new Color(1f, 0.96f, 0.88f), 0, 0.6f, 5f), false, R(Vector3.right, -0.9f));
            b.Build(g, "TrayFlood");
            var lgo = new GameObject("Light");
            lgo.transform.SetParent(g, false);
            lgo.transform.localPosition = G(0, -0.12f, -0.18f);
            var target = Shared.Pos(t.center.x, 0, t.center.y);
            var l = lgo.AddComponent<Light>();
            lgo.transform.rotation = Quaternion.LookRotation(target - lgo.transform.position);
            l.type = LightType.Spot;
            l.spotAngle = 75;
            l.innerSpotAngle = 40;
            l.range = 6f;
            l.intensity = 7f;
            l.color = new Color(1f, 0.95f, 0.86f);
            l.shadows = LightShadows.None;
        }

        /// <summary>Sunlight shafts through the west windows (additive prisms along the sun
        /// direction down to the floor) with dust motes drifting in them.</summary>
        void BuildShafts()
        {
            var dir = sun.transform.forward;
            if (dir.x < 0.05f || dir.y > -0.05f) return;   // the sun must shine in through the west wall
            var mat = new Material(Resources.Load<Shader>("WWShaft"));
            mat.SetColor("_Color", new Color(1f, 0.82f, 0.58f));
            mat.SetFloat("_Intensity", 0.16f);
            var dustMat = new Material(Resources.Load<Shader>("WWDust"));
            float x = layout.roomMin.x;
            foreach (var w in layout.windows)
            {
                float wz = (float)Json.Num(w, "z"), ww = (float)Json.Num(w, "width"), y0 = (float)Json.Num(w, "y0"), y1 = (float)Json.Num(w, "y1");
                var c = new[] { Shared.Pos(x, y0, wz - ww / 2), Shared.Pos(x, y0, wz + ww / 2), Shared.Pos(x, y1, wz + ww / 2), Shared.Pos(x, y1, wz - ww / 2) };
                var far = c.Select(p => p + dir * Mathf.Min(p.y / -dir.y, 11f)).ToArray();
                var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
                for (int e = 0; e < 4; e++)
                {
                    int a = e, bI = (e + 1) % 4, v0 = verts.Count;
                    verts.AddRange(new[] { c[a], c[bI], far[bI], far[a] });
                    uvs.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                    tris.AddRange(new[] { v0, v0 + 1, v0 + 2, v0, v0 + 2, v0 + 3 });
                }
                var mesh = new Mesh { name = "Shaft" };
                mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var go = new GameObject("Shaft");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                // dust motes inside the shaft
                float len = Mathf.Min(((y0 + y1) / 2) / -dir.y, 11f);
                var mid = Shared.Pos(x, (y0 + y1) / 2, wz) + dir * len * 0.45f;
                var dgo = new GameObject("Dust");
                dgo.transform.SetParent(transform, false);
                dgo.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(dir, Vector3.up));
                var ps = dgo.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true; main.prewarm = true; main.playOnAwake = true;
                main.duration = 10;
                main.startLifetime = 14;
                main.startSpeed = 0.01f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.011f);
                main.startColor = new Color(1f, 0.88f, 0.7f, 0.9f);
                main.maxParticles = 220;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = ps.emission; em.rateOverTime = 15;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(ww * 0.8f, (y1 - y0) * 0.7f, len * 0.8f);
                var nz = ps.noise; nz.enabled = true; nz.strength = 0.03f; nz.frequency = 0.25f; nz.scrollSpeed = 0.05f;
                var col = ps.colorOverLifetime; col.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                             new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(1, 0.8f), new GradientAlphaKey(0, 1) });
                col.color = grad;
                var pr = dgo.GetComponent<ParticleSystemRenderer>();
                pr.sharedMaterial = dustMat;
                pr.renderMode = ParticleSystemRenderMode.Billboard;
                pr.shadowCastingMode = ShadowCastingMode.Off;
                pr.receiveShadows = false;
                ps.Play();
            }
        }

        // ---------------------------------------------------------------- lighting

        void BuildLighting()
        {
            foreach (var l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional) Destroy(l.gameObject);
            var go = new GameObject("Sun");
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.74f, 0.46f);   // late-afternoon gold through the windows
            sun.intensity = 3.8f;
            sun.shadows = LightShadows.Soft;
            // Godot: Basis.from_euler(-32°, -75°) (YXZ); the light shines along -Z.
            var gdir = Quaternion.AngleAxis(-75f, Vector3.up) * Quaternion.AngleAxis(-32f, Vector3.right) * Vector3.back;
            go.transform.rotation = Quaternion.LookRotation(new Vector3(gdir.x, gdir.y, -gdir.z));
            RenderSettings.sun = sun;

            // light comes from above (fixtures, windows): up-facing surfaces get the most ambient
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // cool bounce light from the windows against the warm lamps: the look's warm / cool contrast
            RenderSettings.ambientSkyColor = new Color(0.5f, 0.56f, 0.62f) * 0.9f;
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.4f, 0.38f) * 0.42f;
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.25f, 0.22f) * 0.18f;
            // dusty air: a warm haze gives the long room depth
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.46f, 0.39f, 0.31f);
            RenderSettings.fogDensity = 0.018f;
            // Godot adds screen-space indirect light (SSIL); URP has none, so ambient is a bit stronger.
            // (trilight ambient set above; the fixtures carry the room)
            RenderSettings.reflectionIntensity = 0.3f;
            if (RenderSettings.skybox != null)
            {
                var sky = RenderSettings.skybox = new Material(RenderSettings.skybox);
                if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", new Color(0.55f, 0.65f, 0.85f));
                if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", new Color(0.35f, 0.28f, 0.2f));
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", 1.4f);
            }
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            foreach (var p in new Vector3[0])   // (fill lights replaced by the overhead fixtures)
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
            bloom.intensity.Override(0.7f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.75f);
            bloom.tint.Override(new Color(1f, 0.86f, 0.7f));
            // the look: worn industrial film. Slightly desaturated, firm contrast, teal shadows and
            // amber highlights (rust against blueprint), fine grain, a soft vignette
            var ca = profile.Add<ColorAdjustments>(true);
            ca.saturation.Override(-6f);
            ca.contrast.Override(10f);
            ca.postExposure.Override(2.0f);
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(2f);
            var split = profile.Add<SplitToning>(true);
            split.shadows.Override(new Color(0.32f, 0.5f, 0.56f));
            split.highlights.Override(new Color(0.96f, 0.74f, 0.48f));
            split.balance.Override(-12f);
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.16f);
            grain.response.Override(0.85f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.3f);
            vig.smoothness.Override(0.5f);
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.96f, 0.98f, 1.04f, -0.04f));   // slightly cool, deep shadows
            smh.highlights.Override(new Vector4(1.04f, 1.0f, 0.95f, 0f));   // warm highlights
            vol.profile = profile;

            // no environment reflections: no realtime probe (cheaper), and metals only pick up a
            // faint, even, light reflection of the sky — never the dark room
            QualitySettings.realtimeReflectionProbes = false;
        }

        public void RefreshProbe()
        {
            if (probe != null) probe.RenderProbe();
        }
    }
}
