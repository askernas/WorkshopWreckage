using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage
{
    /// <summary>Robot-themed salvage meshes built in code (Robot Workshop redesign,
    /// docs/ROBOT_WORKSHOP_ASSET_AUDIT.md). Same conventions as the exported item meshes:
    /// unit space (about 1 unit = the part's nominal size), centred, long parts along Y;
    /// submesh slots 0 = part material, 2 = accents coloured by vertex colour (alpha 0 = no
    /// thread shading). LOD 0 for inspection, LOD 1 (fewer segments) in the world.</summary>
    public static class SalvageMeshes
    {
        static readonly Dictionary<string, Action<B>> Builders = new()
        {
            ["mach:hydraulic_actuator"] = HydraulicActuator,
            ["mach:servo_joint"] = ServoJoint,
            ["mach:drive_motor"] = DriveMotor,
            ["mach:limb_housing"] = LimbHousing,
            ["mach:chassis_bracket"] = ChassisBracket,
            ["mach:reduction_cartridge"] = ReductionCartridge,
            ["mach:shoulder_yoke"] = ShoulderYoke,
            ["spec:wheel_hub"] = WheelHub,
            ["spec:track_idler"] = TrackIdler,
            ["spec:sensor_lens"] = SensorLens,
            ["elec:servo_controller"] = ServoController,
            ["elec:motor_driver"] = MotorDriver,
            ["elec:sensor_board"] = SensorBoard,
            ["elec:power_connector"] = PowerConnector,
            // Phase 4 robot-workshop spare parts (docs/component_catalogue/phase4/PHASE4_STAGE_A_PROPOSAL.md)
            ["mach:claw_jaw"] = ClawJaw,
            ["mach:leg_knee"] = LegKnee,
            ["spec:track_link"] = TrackLink,
            ["spec:drag_chain"] = DragChain,
            ["spec:rotor_hub"] = RotorHub,
            ["spec:antenna_mount"] = AntennaMount,
            ["elec:battery_cell"] = BatteryCell,
            ["elec:gyro_module"] = GyroModule,
            ["shaft:joint_coupling"] = JointCoupling,
            ["shaft:telescopic_tube"] = TelescopicTube,
            ["gear:servo_horn"] = ServoHorn,
            ["gear:encoder_disc"] = EncoderDisc,
            // Part Radar components and battery (shared/data/tools/part_radar.json; the assembled device is PartRadarModel)
            ["robot:radar_housing"] = RadarHousing,
            ["robot:radar_left_handle"] = b => RadarHandle(b, true),
            ["robot:radar_right_handle"] = b => RadarHandle(b, false),
            ["robot:radar_pulse_button"] = RadarButton,
            ["robot:radar_selector_dial"] = RadarDial,
            ["robot:radar_led_module"] = RadarLed,
            ["robot:radar_left_antenna"] = RadarAntennaL,
            ["robot:radar_right_antenna"] = RadarAntennaR,
            ["robot:radar_battery"] = RadarBattery,
            // Sorting Arm unique parts (robot_parts.json, yellow enamel with red joints)
            ["robot:sa_coupler"] = b => { b.Box(new Vector3(0, -0.3f, 0), new Vector3(1f, 0.16f, 0.7f), 0, Part); b.Box(new Vector3(0, -0.14f, -0.3f), new Vector3(0.7f, 0.2f, 0.12f), 2, DarkSteel); b.Cyl(new Vector3(0, -0.22f, 0.05f), new Vector3(0, 0.2f, 0.05f), 0.24f, 2, ArmRed); foreach (float x in new[] { -0.4f, 0.4f }) b.Cyl(new Vector3(x, -0.38f, 0.2f), new Vector3(x, -0.2f, 0.2f), 0.05f, 2, Hole); b.Box(new Vector3(0.3f, -0.21f, 0.25f), new Vector3(0.1f, 0.02f, 0.06f), 2, Lens); },
            ["robot:sa_shoulder"] = b => { b.Cyl(new Vector3(0, -0.4f, 0), new Vector3(0, 0.05f, 0), 0.45f, 2, ArmRed); b.Cyl(new Vector3(0, 0.05f, 0), new Vector3(0, 0.15f, 0), 0.38f, 2, DarkSteel); b.Box(new Vector3(0, 0.3f, 0), new Vector3(0.7f, 0.3f, 0.5f), 0, Part); b.Cyl(new Vector3(-0.38f, 0.32f, 0), new Vector3(0.38f, 0.32f, 0), 0.16f, 2, Chrome); },
            ["robot:sa_upper"] = b => ArmBeam(b, 1.7f, 0.28f),
            ["robot:sa_forearm"] = b => ArmBeam(b, 1.6f, 0.22f),
            ["robot:sa_elbow"] = b => { b.Cyl(new Vector3(-0.42f, 0, 0), new Vector3(0.42f, 0, 0), 0.4f, 2, ArmRed); b.Cyl(new Vector3(-0.5f, 0, 0), new Vector3(-0.42f, 0, 0), 0.22f, 2, DarkSteel); b.Box(new Vector3(0, -0.35f, 0), new Vector3(0.5f, 0.3f, 0.4f), 0, Part); b.Box(new Vector3(0.2f, 0.25f, 0.33f), new Vector3(0.18f, 0.08f, 0.01f), 2, Warn); },
            ["robot:sa_wrist"] = b => { b.Cyl(new Vector3(0, -0.45f, 0), new Vector3(0, 0.1f, 0), 0.32f, 0, Part); b.Cyl(new Vector3(0, 0.1f, 0), new Vector3(0, 0.25f, 0), 0.4f, 2, ArmRed); b.Cyl(new Vector3(0, 0.25f, 0), new Vector3(0, 0.45f, 0), 0.25f, 2, DarkSteel); },
            ["robot:sa_finger"] = b => { b.Box(new Vector3(0, 0, 0), new Vector3(0.22f, 1.3f, 0.3f), 0, Part); b.Box(new Vector3(0.1f, 0.55f, 0), new Vector3(0.22f, 0.25f, 0.28f), 2, Rubber); b.Cyl(new Vector3(0, -0.6f, -0.17f), new Vector3(0, -0.6f, 0.17f), 0.14f, 2, ArmRed); },
            ["robot:sa_motor"] = b => { b.Cyl(new Vector3(0, -0.7f, 0), new Vector3(0, 0.5f, 0), 0.3f, 2, DarkSteel); for (int i = 0; i < 5; i++) b.Cyl(new Vector3(0, -0.5f + i * 0.2f, 0), new Vector3(0, -0.45f + i * 0.2f, 0), 0.34f, 0, Part); b.Cyl(new Vector3(0, 0.5f, 0), new Vector3(0, 0.75f, 0), 0.07f, 2, Chrome); b.Box(new Vector3(0.3f, -0.2f, 0), new Vector3(0.16f, 0.3f, 0.2f), 2, Rubber); },
            ["robot:sa_control"] = b => { b.Box(Vector3.zero, new Vector3(1f, 0.6f, 0.7f), 0, Part); b.Box(new Vector3(0, 0.05f, 0.355f), new Vector3(0.7f, 0.3f, 0.01f), 2, DarkSteel); for (int i = 0; i < 3; i++) b.Box(new Vector3(-0.2f + i * 0.2f, 0.05f, 0.362f), new Vector3(0.08f, 0.08f, 0.01f), 2, i == 1 ? Lens : ArmRed); for (int i = 0; i < 5; i++) b.Box(new Vector3(-0.4f + i * 0.2f, 0.31f, 0), new Vector3(0.06f, 0.02f, 0.6f), 2, DarkSteel); },
            ["robot:sa_sensor"] = b => { b.Box(new Vector3(0, 0, -0.1f), new Vector3(0.9f, 0.55f, 0.6f), 0, Part); foreach (float x in new[] { -0.22f, 0.22f }) { b.Cyl(new Vector3(x, 0, 0.2f), new Vector3(x, 0, 0.35f), 0.17f, 2, DarkSteel); b.Cyl(new Vector3(x, 0, 0.35f), new Vector3(x, 0, 0.38f), 0.12f, 2, Lens); } b.Box(new Vector3(0, 0.3f, -0.1f), new Vector3(0.2f, 0.08f, 0.2f), 2, ArmRed); },
            ["robot:sa_harness"] = b => { for (int i = 0; i < 9; i++) { float a0 = i * 0.7f, a1 = (i + 1) * 0.7f; var p0 = new Vector3(Mathf.Cos(a0) * 0.35f, -0.4f + i * 0.09f, Mathf.Sin(a0) * 0.35f); var p1 = new Vector3(Mathf.Cos(a1) * 0.35f, -0.4f + (i + 1) * 0.09f, Mathf.Sin(a1) * 0.35f); b.Cyl(p0, p1, 0.06f, 2, i % 3 == 0 ? ArmRed : Rubber, 8); } b.Box(new Vector3(0.35f, -0.45f, 0), new Vector3(0.2f, 0.14f, 0.14f), 0, Part); b.Box(new Vector3(0.3f, 0.45f, 0.2f), new Vector3(0.2f, 0.14f, 0.14f), 0, Part); },
        };

        static readonly Color ArmRed = new(0.7f, 0.12f, 0.08f, 0);
        static readonly Color FadedBlue = new(0.24f, 0.35f, 0.5f, 0), Brass = new(0.66f, 0.5f, 0.22f, 0), CopperC = new(0.62f, 0.32f, 0.17f, 0),
            RustC = new(0.36f, 0.2f, 0.11f, 0), TapeC = new(0.6f, 0.58f, 0.52f, 0), Amber = new(0.95f, 0.6f, 0.12f, 0), LedGreen = new(0.2f, 0.45f, 0.15f, 0);

        // ------------------------------------------------------------------ Part Radar (resources/part radar, concept sheet)

        /// <summary>The chipped cream controller box: faded blue patch, speaker grille, empty sockets for the
        /// button, dial and LED, rusty corner screws, the battery hatch on the back.</summary>
        static void RadarHousing(B b)
        {
            b.Box(Vector3.zero, new Vector3(1f, 0.69f, 0.35f), 0, Part);
            b.Box(new Vector3(0.24f, 0.16f, 0.176f), new Vector3(0.34f, 0.24f, 0.01f), 2, FadedBlue);
            for (int k = 0; k < 4; k++) b.Box(new Vector3(-0.26f, 0.2f - k * 0.07f, 0.177f), new Vector3(0.3f, 0.025f, 0.01f), 2, Hole);   // grille
            b.Cyl(new Vector3(0.28f, -0.08f, 0.17f), new Vector3(0.28f, -0.08f, 0.18f), 0.1f, 2, Hole);    // button socket
            b.Cyl(new Vector3(-0.18f, -0.06f, 0.17f), new Vector3(-0.18f, -0.06f, 0.18f), 0.09f, 2, Hole);   // dial socket
            b.Box(new Vector3(0, 0.35f, 0), new Vector3(0.3f, 0.02f, 0.2f), 2, Hole);                          // LED cut-out on top
            foreach (var x in new[] { -0.44f, 0.44f }) foreach (var y in new[] { -0.29f, 0.29f })
                b.Cyl(new Vector3(x, y, 0.17f), new Vector3(x, y, 0.185f), 0.025f, 2, RustC, 6);
            b.Box(new Vector3(0, -0.08f, -0.176f), new Vector3(0.5f, 0.32f, 0.01f), 2, DarkSteel);              // battery hatch
            foreach (var x in new[] { -0.39f, 0.39f }) b.Cyl(new Vector3(x, 0.345f, 0), new Vector3(x, 0.4f, 0), 0.045f, 2, Brass, 8);   // antenna sockets
        }

        /// <summary>A rubber-ribbed grip on a pipe bracket (brass on the left, gunmetal on the right), taped.</summary>
        static void RadarHandle(B b, bool left)
        {
            b.Box(new Vector3(0, 0.42f, 0), new Vector3(0.34f, 0.12f, 0.16f), 0, Part);                         // bracket
            b.Cyl(new Vector3(0, -0.36f, 0), new Vector3(0, 0.38f, 0), 0.07f, 0, Part, 10);                     // pipe
            b.Cyl(new Vector3(0, -0.3f, 0), new Vector3(0, 0.28f, 0), 0.15f, 2, Rubber);                        // grip
            for (int k = 0; k < 6; k++) b.Cyl(new Vector3(0, -0.24f + k * 0.09f, 0), new Vector3(0, -0.21f + k * 0.09f, 0), 0.165f, 2, Rubber);   // ribs
            b.Cyl(new Vector3(0, left ? 0.1f : -0.15f, 0), new Vector3(0, left ? 0.2f : -0.05f, 0), 0.168f, 2, TapeC);   // tape
            b.Cyl(new Vector3(0, -0.4f, 0), new Vector3(0, -0.36f, 0), 0.11f, 2, left ? Brass : DarkSteel);       // end cap
        }

        static void RadarButton(B b)
        {
            b.Box(new Vector3(0, -0.3f, 0), new Vector3(1f, 0.16f, 1f), 2, DarkSteel);
            b.Cyl(new Vector3(0, -0.22f, 0), new Vector3(0, -0.08f, 0), 0.42f, 2, Chrome);                      // collar
            b.Cyl(new Vector3(0, -0.08f, 0), new Vector3(0, 0.3f, 0), 0.34f, 0, Part);                          // red cap
            foreach (var x in new[] { -0.4f, 0.4f }) b.Cyl(new Vector3(x, -0.4f, 0.3f), new Vector3(x, -0.6f, 0.3f), 0.04f, 2, x < 0 ? WireRed : Rubber, 6);   // leads
        }

        static void RadarDial(B b)
        {
            b.Cyl(new Vector3(0, -0.25f, 0), new Vector3(0, -0.02f, 0), 0.5f, 0, Part);                         // cream face plate
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4;
                b.Box(new Vector3(Mathf.Cos(a) * 0.42f, -0.01f, Mathf.Sin(a) * 0.42f), new Vector3(0.05f, 0.02f, 0.05f), 2, Hole);   // tick marks
            }
            b.Cyl(new Vector3(0, -0.02f, 0), new Vector3(0, 0.3f, 0), 0.22f, 2, FadedBlue);                    // knob
            b.Box(new Vector3(0.1f, 0.31f, 0), new Vector3(0.22f, 0.02f, 0.05f), 2, TapeC);                    // pointer
        }

        static void RadarLed(B b)
        {
            b.Box(new Vector3(0, -0.3f, 0), new Vector3(1f, 0.12f, 0.6f), 0, Part);                              // board
            b.Cyl(new Vector3(-0.15f, -0.24f, 0), new Vector3(-0.15f, -0.05f, 0), 0.3f, 2, Chrome);            // bezel
            b.Cyl(new Vector3(-0.15f, -0.05f, 0), new Vector3(-0.15f, 0.2f, 0), 0.26f, 2, Amber);              // big amber dome
            for (int k = 0; k < 4; k++) b.Box(new Vector3(0.22f + k * 0.07f, -0.2f, 0.1f), new Vector3(0.045f, 0.08f, 0.045f), 2, LedGreen);   // charge LEDs
            b.Cyl(new Vector3(0.4f, -0.3f, -0.2f), new Vector3(0.62f, -0.36f, -0.25f), 0.03f, 2, WireRed, 6);
        }

        /// <summary>An old telescopic rod: three sections, brass joints, blue insulating tape near the foot.</summary>
        static void RadarAntennaL(B b)
        {
            b.Cyl(new Vector3(0, -0.5f, 0), new Vector3(0, -0.44f, 0), 0.06f, 2, DarkSteel, 8);
            b.Cyl(new Vector3(0, -0.44f, 0), new Vector3(0, -0.1f, 0), 0.034f, 0, Part, 8);
            b.Cyl(new Vector3(0, -0.1f, 0), new Vector3(0, 0.22f, 0), 0.026f, 0, Part, 8);
            b.Cyl(new Vector3(0, 0.22f, 0), new Vector3(0, 0.48f, 0), 0.019f, 0, Part, 8);
            foreach (var y in new[] { -0.1f, 0.22f }) b.Cyl(new Vector3(0, y - 0.02f, 0), new Vector3(0, y + 0.02f, 0), 0.045f, 2, Brass, 8);
            b.Cyl(new Vector3(0, -0.38f, 0), new Vector3(0, -0.28f, 0), 0.042f, 2, FadedBlue, 8);
            b.Cyl(new Vector3(0, 0.48f, 0), new Vector3(0, 0.5f, 0), 0.03f, 2, Brass, 8);                      // tip
        }

        /// <summary>A mismatched rod wound with copper wire, an exposed red lead and a clamped bracket.</summary>
        static void RadarAntennaR(B b)
        {
            b.Box(new Vector3(0, -0.44f, 0), new Vector3(0.16f, 0.08f, 0.12f), 2, DarkSteel);                  // clamp
            b.Cyl(new Vector3(0, -0.5f, 0), new Vector3(0, 0.5f, 0), 0.03f, 0, Part, 8);
            for (int k = 0; k < 12; k++) b.Cyl(new Vector3(0, -0.2f + k * 0.026f, 0), new Vector3(0, -0.187f + k * 0.026f, 0), 0.045f, 2, CopperC, 8);   // winding
            b.Cyl(new Vector3(0.05f, -0.42f, 0.02f), new Vector3(0.06f, -0.18f, 0.03f), 0.012f, 2, WireRed, 5);
        }

        /// <summary>A worn cylindrical cell (18650 style): faded blue wrap, scuffed label band, bare terminals.</summary>
        static void RadarBattery(B b)
        {
            b.Cyl(new Vector3(0, -0.46f, 0), new Vector3(0, 0.46f, 0), 0.135f, 0, Part);
            b.Cyl(new Vector3(0, -0.12f, 0), new Vector3(0, 0.18f, 0), 0.138f, 2, TapeC);
            b.Cyl(new Vector3(0, 0.46f, 0), new Vector3(0, 0.5f, 0), 0.05f, 2, Chrome, 10);
            b.Cyl(new Vector3(0, -0.5f, 0), new Vector3(0, -0.46f, 0), 0.115f, 2, Chrome);
        }
        static readonly Color Pcb = new(0.1f, 0.32f, 0.16f, 0), Chip = new(0.07f, 0.07f, 0.08f, 0), Ivory = new(0.88f, 0.86f, 0.78f, 0), Alu = new(0.72f, 0.74f, 0.76f, 0);

        // ------------------------------------------------------------------ Phase D families

        /// <summary>A gearbox housing with one side open on a big exposed reduction gear.</summary>
        static void ReductionCartridge(B b)
        {
            b.Box(new Vector3(-0.08f, 0, 0), new Vector3(0.8f, 0.62f, 0.6f), 0, Part);
            b.Box(new Vector3(-0.08f, -0.34f, 0), new Vector3(0.95f, 0.06f, 0.7f), 0, Part);          // flange
            b.Cyl(new Vector3(0.34f, 0.04f, 0), new Vector3(0.42f, 0.04f, 0), 0.3f, 2, DarkSteel);   // exposed gear
            for (int k = 0; k < 14; k++)
            {
                float a = k * Mathf.PI * 2 / 14;
                b.Box(new Vector3(0.38f, 0.04f + Mathf.Sin(a) * 0.32f, Mathf.Cos(a) * 0.32f), new Vector3(0.08f, 0.06f, 0.06f), 2, DarkSteel);
            }
            b.Cyl(new Vector3(0.42f, 0.04f, 0), new Vector3(0.56f, 0.04f, 0), 0.07f, 2, Chrome);    // output shaft
            b.Cyl(new Vector3(-0.48f, 0.12f, 0), new Vector3(-0.58f, 0.12f, 0), 0.12f, 2, DarkSteel);   // input
            b.Box(new Vector3(-0.1f, 0.18f, 0.305f), new Vector3(0.22f, 0.1f, 0.01f), 2, Warn);       // ratio plate
        }

        /// <summary>U-shaped shoulder yoke: a base block with two arms and a through pivot.</summary>
        static void ShoulderYoke(B b)
        {
            b.Box(new Vector3(0, -0.3f, 0), new Vector3(0.9f, 0.26f, 0.55f), 0, Part);
            foreach (float x in new[] { -0.36f, 0.36f })
            {
                b.Box(new Vector3(x, 0.05f, 0), new Vector3(0.17f, 0.55f, 0.5f), 0, Part);
                b.Cyl(new Vector3(x - 0.1f, 0.22f, 0), new Vector3(x + 0.1f, 0.22f, 0), 0.2f, 2, ArmRed);
            }
            b.Cyl(new Vector3(-0.52f, 0.22f, 0), new Vector3(0.52f, 0.22f, 0), 0.06f, 2, Chrome);   // pivot pin
            b.Cyl(new Vector3(0, -0.44f, 0), new Vector3(0, -0.5f, 0), 0.22f, 2, DarkSteel);        // turntable stub
            foreach (float x in new[] { -0.3f, 0.3f }) b.Cyl(new Vector3(x, -0.17f, 0.18f), new Vector3(x, -0.15f, 0.18f), 0.04f, 2, DarkSteel, 6);
        }

        static void WheelHub(B b)
        {
            b.Cyl(new Vector3(0, -0.4f, 0), new Vector3(0, -0.15f, 0), 0.5f, 0, Part);              // flange
            b.Cyl(new Vector3(0, -0.15f, 0), new Vector3(0, 0.4f, 0), 0.26f, 0, Part);               // hub
            b.Cyl(new Vector3(0, 0.4f, 0), new Vector3(0, 0.41f, 0), 0.12f, 2, Hole);
            for (int k = 0; k < 5; k++)
            {
                float a = k * Mathf.PI * 2 / 5;
                var p = new Vector3(Mathf.Cos(a) * 0.36f, -0.15f, Mathf.Sin(a) * 0.36f);
                b.Cyl(p, p + Vector3.up * 0.14f, 0.04f, 2, Chrome, 8);                                 // wheel studs
            }
            b.Cyl(new Vector3(0, -0.15f, 0), new Vector3(0, -0.12f, 0), 0.3f, 2, DarkSteel);
        }

        static void TrackIdler(B b)
        {
            foreach (float y in new[] { -0.18f, 0.18f }) b.Cyl(new Vector3(0, y - 0.04f, 0), new Vector3(0, y + 0.04f, 0), 0.5f, 0, Part);   // flanges
            b.Cyl(new Vector3(0, -0.14f, 0), new Vector3(0, 0.14f, 0), 0.4f, 0, Part);               // running rim
            b.Cyl(new Vector3(0, -0.26f, 0), new Vector3(0, 0.26f, 0), 0.13f, 2, Chrome);            // axle hub, standing proud of the flanges (flush caps z-fought: playtest 2026-10-26)
            foreach (float y in new[] { -0.265f, 0.26f }) b.Cyl(new Vector3(0, y, 0), new Vector3(0, y + 0.005f, 0), 0.06f, 2, Hole, 10);   // axle bore
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI / 2 + 0.4f;
                var p = new Vector3(Mathf.Cos(a) * 0.27f, 0.222f, Mathf.Sin(a) * 0.27f);
                b.Cyl(p, p + Vector3.up * 0.005f, 0.07f, 2, Hole, 10);                                // lightening holes
            }
        }

        static void SensorLens(B b)
        {
            b.Cyl(new Vector3(0, -0.22f, 0), new Vector3(0, 0.1f, 0), 0.48f, 0, Part);               // housing
            b.Cyl(new Vector3(0, 0.1f, 0), new Vector3(0, 0.16f, 0), 0.36f, 2, DarkSteel);          // bezel
            b.Cyl(new Vector3(0, 0.16f, 0), new Vector3(0, 0.2f, 0), 0.28f, 2, Lens);                // lens
            b.Cyl(new Vector3(0.1f, 0.2f, 0.1f), new Vector3(0.1f, 0.205f, 0.1f), 0.06f, 2, Ivory, 8); // glint
            for (int k = 0; k < 3; k++) b.Box(new Vector3(-0.3f + k * 0.08f, 0.11f, -0.3f), new Vector3(0.04f, 0.02f, 0.04f), 2, k == 0 ? ArmRed : Lens);
            b.Cyl(new Vector3(0.45f, -0.1f, 0), new Vector3(0.6f, -0.1f, 0), 0.06f, 2, Rubber);     // cable gland
        }

        static void ServoController(B b)
        {
            b.Box(new Vector3(0, -0.2f, 0), new Vector3(1.6f, 0.08f, 1.0f), 0, Part);                // board (variant colour)
            b.Box(new Vector3(-0.2f, -0.12f, 0.05f), new Vector3(0.42f, 0.08f, 0.42f), 2, Chip);     // MCU
            b.Box(new Vector3(0.4f, -0.12f, -0.2f), new Vector3(0.22f, 0.07f, 0.22f), 2, Chip);
            for (int k = 0; k < 8; k++) b.Box(new Vector3(-0.7f + k * 0.2f, 0.0f, 0.42f), new Vector3(0.06f, 0.36f, 0.06f), 1, Part);   // servo header pins
            b.Box(new Vector3(0, -0.1f, 0.42f), new Vector3(1.6f, 0.12f, 0.12f), 2, Chip);           // header strip
            foreach (float x in new[] { 0.55f, 0.7f }) b.Cyl(new Vector3(x, -0.16f, 0.15f), new Vector3(x, 0.08f, 0.15f), 0.07f, 2, Alu, 10);   // caps
            b.Box(new Vector3(-0.68f, -0.06f, -0.3f), new Vector3(0.22f, 0.2f, 0.3f), 2, Ivory);    // power plug
        }

        static void MotorDriver(B b)
        {
            b.Box(new Vector3(0, -0.45f, 0), new Vector3(1.0f, 0.08f, 0.8f), 0, Part);               // board
            b.Box(new Vector3(0, -0.3f, 0), new Vector3(0.7f, 0.22f, 0.6f), 2, Alu);                 // heatsink base
            for (int k = 0; k < 7; k++) b.Box(new Vector3(-0.3f + k * 0.1f, 0.05f, 0), new Vector3(0.035f, 0.5f, 0.6f), 2, Alu);   // fins
            b.Box(new Vector3(0, -0.36f, 0.36f), new Vector3(0.6f, 0.14f, 0.12f), 2, new Color(0.2f, 0.5f, 0.25f, 0));   // screw terminal
            for (int k = 0; k < 4; k++) b.Cyl(new Vector3(-0.22f + k * 0.15f, -0.28f, 0.36f), new Vector3(-0.22f + k * 0.15f, -0.27f, 0.36f), 0.035f, 1, Part, 6);
        }

        static void SensorBoard(B b)
        {
            b.Box(new Vector3(0, -0.5f, 0), new Vector3(1.2f, 0.08f, 0.9f), 0, Part);
            foreach (float x in new[] { -0.3f, 0.3f })
            {
                b.Cyl(new Vector3(x, -0.46f, -0.1f), new Vector3(x, 0.15f, -0.1f), 0.24f, 1, Part, 16);   // transducer cans
                b.Cyl(new Vector3(x, 0.15f, -0.1f), new Vector3(x, 0.17f, -0.1f), 0.2f, 2, Chip, 16);
            }
            b.Box(new Vector3(0, -0.42f, 0.3f), new Vector3(0.3f, 0.08f, 0.16f), 2, Chip);
            for (int k = 0; k < 4; k++) b.Box(new Vector3(-0.15f + k * 0.1f, -0.6f, 0.42f), new Vector3(0.03f, 0.25f, 0.03f), 1, Part);
        }

        static void PowerConnector(B b)
        {
            b.Box(new Vector3(0, -0.15f, 0), new Vector3(0.7f, 1.0f, 0.6f), 0, Part);               // housing
            b.Box(new Vector3(0, 0.38f, 0), new Vector3(0.5f, 0.06f, 0.4f), 2, Chip);               // mating face
            for (int k = 0; k < 3; k++) b.Cyl(new Vector3(-0.15f + k * 0.15f, 0.38f, 0), new Vector3(-0.15f + k * 0.15f, 0.55f, 0), 0.04f, 1, Part, 8);   // pins
            b.Box(new Vector3(0.36f, 0, 0), new Vector3(0.04f, 0.4f, 0.2f), 2, Chip);               // latch
            foreach (var (x, c) in new[] { (-0.12f, WireRed), (0.0f, Rubber), (0.12f, Warn) })
                b.Cyl(new Vector3(x, -0.65f, 0), new Vector3(x * 1.4f, -0.95f, 0.05f), 0.045f, 2, c, 8);   // wires
        }

        /// <summary>Servo output shaft, aspect a (length / diameter): splined top, keyed flat,
        /// a collar and a red bearing cap at the base.</summary>
        static void ServoShaft(B b, float a)
        {
            float h = a / 2;
            b.Cyl(new Vector3(0, -h + 0.3f, 0), new Vector3(0, h - 0.35f, 0), 0.5f, 0, Part);
            b.Cyl(new Vector3(0, h - 0.35f, 0), new Vector3(0, h, 0), 0.42f, 2, Chrome);            // splined end
            for (int k = 0; k < 8; k++) { float t = k * Mathf.PI / 4; b.Box(new Vector3(Mathf.Cos(t) * 0.42f, h - 0.17f, Mathf.Sin(t) * 0.42f), new Vector3(0.06f, 0.3f, 0.06f), 2, Chrome); }
            if (a >= 3) b.Box(new Vector3(0.47f, h - 0.9f, 0), new Vector3(0.06f, Mathf.Min(0.9f, a * 0.25f), 0.3f), 2, DarkSteel);   // key flat
            b.Cyl(new Vector3(0, -h + 0.3f, 0), new Vector3(0, -h + 0.65f, 0), 0.75f, 2, ArmRed);      // bearing cap
            b.Cyl(new Vector3(0, -h, 0), new Vector3(0, -h + 0.3f, 0), 0.45f, 0, Part);
            if (a >= 4) b.Cyl(new Vector3(0, h - 1.2f, 0), new Vector3(0, h - 1.0f, 0), 0.65f, 2, DarkSteel);   // collar
        }

        /// <summary>A tapered box-section arm segment with joint bosses at both ends.</summary>
        static void ArmBeam(B b, float len, float w)
        {
            b.Box(Vector3.zero, new Vector3(w * 1.6f, len * 0.78f, w * 1.2f), 0, Part);
            foreach (float y in new[] { -len * 0.42f, len * 0.42f })
                b.Cyl(new Vector3(-w * 1.1f, y, 0), new Vector3(w * 1.1f, y, 0), w * 0.95f, 2, ArmRed);
            b.Box(new Vector3(0, 0, w * 0.61f), new Vector3(w * 0.7f, len * 0.5f, 0.01f), 2, DarkSteel);   // inspection plate
            b.Cyl(new Vector3(w * 0.9f, -len * 0.3f, 0), new Vector3(w * 0.9f, len * 0.3f, 0), 0.035f, 2, Rubber);   // cable
        }

        const string ServoShaftPrefix = "shaft:servo_shaft:a";
        public static bool Has(string key) => Builders.ContainsKey(key) || key.StartsWith(ServoShaftPrefix);
        public static IEnumerable<string> Keys => Builders.Keys;

        public static MeshLibrary.Entry Build(string key, int lod)
        {
            var b = new B { seg = lod == MeshLibrary.LodInspect ? 24 : 12 };
            if (key.StartsWith(ServoShaftPrefix)) ServoShaft(b, float.Parse(key.Substring(ServoShaftPrefix.Length), System.Globalization.CultureInfo.InvariantCulture));
            else Builders[key](b);
            return b.Finish(key + "_lod" + lod);
        }

        // accent colours
        static readonly Color Chrome = new(0.82f, 0.83f, 0.85f, 0), Rubber = new(0.07f, 0.07f, 0.08f, 0), DarkSteel = new(0.25f, 0.26f, 0.28f, 0),
            Hole = new(0.04f, 0.04f, 0.05f, 0), Warn = new(0.92f, 0.66f, 0.1f, 0), WireRed = new(0.7f, 0.1f, 0.08f, 0), Lens = new(0.15f, 0.55f, 0.75f, 0);
        static readonly Color Part = new(1, 1, 1, 0);

        // ------------------------------------------------------------------ parts

        /// <summary>Cylinder barrel, chrome piston rod, clevis eyes at both ends, hose port.</summary>
        static void HydraulicActuator(B b)
        {
            b.Cyl(new Vector3(0, -0.62f, 0), new Vector3(0, 0.22f, 0), 0.17f, 0, Part);
            b.Cyl(new Vector3(0, -0.66f, 0), new Vector3(0, -0.56f, 0), 0.2f, 0, Part);          // end cap
            b.Cyl(new Vector3(0, 0.16f, 0), new Vector3(0, 0.26f, 0), 0.2f, 0, Part);            // gland
            b.Cyl(new Vector3(0, 0.26f, 0), new Vector3(0, 0.72f, 0), 0.065f, 2, Chrome);        // piston rod
            b.Box(new Vector3(0, -0.78f, 0), new Vector3(0.16f, 0.2f, 0.14f), 0, Part);          // rear clevis
            b.Cyl(new Vector3(-0.09f, -0.82f, 0), new Vector3(0.09f, -0.82f, 0), 0.045f, 2, Hole);
            b.Box(new Vector3(0, 0.8f, 0), new Vector3(0.12f, 0.16f, 0.12f), 2, Chrome);         // rod eye
            b.Cyl(new Vector3(-0.07f, 0.83f, 0), new Vector3(0.07f, 0.83f, 0), 0.04f, 2, Hole);
            b.Cyl(new Vector3(0, -0.45f, 0.15f), new Vector3(0, -0.45f, 0.27f), 0.05f, 2, DarkSteel);   // hose ports
            b.Cyl(new Vector3(0, 0.08f, 0.15f), new Vector3(0, 0.08f, 0.27f), 0.05f, 2, DarkSteel);
            b.Cyl(new Vector3(0, -0.45f, 0.25f), new Vector3(0, 0.08f, 0.25f), 0.03f, 2, Rubber); // hose
            for (int i = 0; i < 3; i++) b.Cyl(new Vector3(0, -0.4f + i * 0.2f, 0), new Vector3(0, -0.38f + i * 0.2f, 0), 0.178f, 2, Warn);   // stripes
        }

        /// <summary>Round servo housing with mounting ears, white output horn, cable stub.</summary>
        static void ServoJoint(B b)
        {
            b.Cyl(new Vector3(0, 0, -0.22f), new Vector3(0, 0, 0.18f), 0.36f, 0, Part);
            b.Box(new Vector3(0, -0.02f, -0.02f), new Vector3(0.98f, 0.12f, 0.3f), 0, Part);     // ears
            foreach (float x in new[] { -0.42f, 0.42f })
                b.Cyl(new Vector3(x, -0.09f, -0.02f), new Vector3(x, 0.09f, -0.02f), 0.035f, 2, Hole);
            b.Cyl(new Vector3(0, 0, 0.18f), new Vector3(0, 0, 0.24f), 0.13f, 2, DarkSteel);      // output spline
            b.Box(new Vector3(0, 0, 0.27f), new Vector3(0.62f, 0.12f, 0.05f), 2, new Color(0.9f, 0.9f, 0.86f, 0));   // horn
            b.Box(new Vector3(0, 0, 0.27f), new Vector3(0.12f, 0.5f, 0.05f), 2, new Color(0.9f, 0.9f, 0.86f, 0));
            b.Cyl(new Vector3(0, 0.3f, -0.1f), new Vector3(0, 0.46f, -0.1f), 0.06f, 2, Rubber);  // cable stub
            for (int i = 0; i < 3; i++)
                b.Cyl(new Vector3(-0.03f + i * 0.03f, 0.46f, -0.1f), new Vector3(-0.05f + i * 0.05f, 0.56f, -0.12f), 0.014f, 2, i == 0 ? WireRed : i == 1 ? Rubber : Warn);
            b.Box(new Vector3(0.2f, 0.2f, 0.185f), new Vector3(0.1f, 0.06f, 0.01f), 2, Warn);   // label
        }

        /// <summary>Finned motor can on a foot plate, dark end bells, output shaft, terminal box.</summary>
        static void DriveMotor(B b)
        {
            b.Cyl(new Vector3(-0.32f, 0, 0), new Vector3(0.3f, 0, 0), 0.32f, 0, Part);
            for (int i = 0; i < 6; i++)
            {
                float x = -0.26f + i * 0.1f;
                b.Cyl(new Vector3(x, 0, 0), new Vector3(x + 0.035f, 0, 0), 0.36f, 0, Part);   // cooling fins
            }
            b.Cyl(new Vector3(-0.42f, 0, 0), new Vector3(-0.32f, 0, 0), 0.28f, 2, DarkSteel);   // rear bell
            b.Cyl(new Vector3(0.3f, 0, 0), new Vector3(0.38f, 0, 0), 0.26f, 2, DarkSteel);     // front bell
            b.Cyl(new Vector3(0.38f, 0, 0), new Vector3(0.56f, 0, 0), 0.055f, 2, Chrome);      // shaft
            b.Box(new Vector3(0.53f, 0.05f, 0), new Vector3(0.1f, 0.02f, 0.02f), 2, DarkSteel);   // key
            b.Box(new Vector3(0.02f, -0.36f, 0), new Vector3(0.62f, 0.06f, 0.56f), 0, Part);   // foot
            b.Box(new Vector3(0.02f, 0.38f, 0), new Vector3(0.22f, 0.14f, 0.2f), 0, Part);     // terminal box
            b.Cyl(new Vector3(0.02f, 0.38f, 0.1f), new Vector3(0.02f, 0.38f, 0.2f), 0.04f, 2, Rubber);   // gland
            b.Box(new Vector3(-0.1f, 0.0f, 0.34f), new Vector3(0.16f, 0.1f, 0.01f), 2, Warn);  // rating plate
        }

        /// <summary>Pressed-steel limb shell: flat body between two joint bosses, lightening holes, bearings.</summary>
        static void LimbHousing(B b)
        {
            b.Box(new Vector3(0, 0, 0), new Vector3(0.3f, 1.3f, 0.16f), 0, Part);
            b.Box(new Vector3(0, 0, -0.06f), new Vector3(0.36f, 1.2f, 0.04f), 0, Part);        // flange
            foreach (float y in new[] { -0.7f, 0.7f })
            {
                b.Cyl(new Vector3(0, y, -0.1f), new Vector3(0, y, 0.1f), 0.2f, 0, Part);     // joint boss
                b.Cyl(new Vector3(0, y, 0.1f), new Vector3(0, y, 0.125f), 0.12f, 2, Chrome);   // bearing
                b.Cyl(new Vector3(0, y, 0.125f), new Vector3(0, y, 0.13f), 0.05f, 2, Hole);
            }
            for (int i = -1; i <= 1; i++)
                b.Cyl(new Vector3(0, i * 0.3f, 0.08f), new Vector3(0, i * 0.3f, 0.085f), 0.08f, 2, Hole);   // lightening holes
            b.Box(new Vector3(0.155f, 0, 0.04f), new Vector3(0.02f, 0.9f, 0.04f), 2, Rubber);  // cable channel
            for (int i = 0; i < 5; i++)
                foreach (float x in new[] { -0.167f, 0.167f })
                {
                    var p = new Vector3(x, -0.4f + i * 0.2f, -0.04f);
                    b.Cyl(p, p + Vector3.forward * 0.03f, 0.022f, 2, DarkSteel, 6);              // flange bolts
                }
            b.Box(new Vector3(-0.06f, 0.15f, 0.081f), new Vector3(0.12f, 0.07f, 0.004f), 2, Warn);   // service tag
        }

        /// <summary>L-shaped chassis bracket with gussets, slotted holes and bolts.</summary>
        static void ChassisBracket(B b)
        {
            b.Box(new Vector3(0, -0.39f, 0), new Vector3(1f, 0.08f, 0.6f), 0, Part);           // base plate
            b.Box(new Vector3(0, 0, -0.26f), new Vector3(1f, 0.86f, 0.08f), 0, Part);          // upright
            foreach (float x in new[] { -0.36f, 0.36f })
                b.Prism(new[] { new Vector2(-0.22f, -0.35f), new Vector2(0.28f, -0.35f), new Vector2(-0.22f, 0.3f) }, x - 0.03f, x + 0.03f, 0, Part);   // gussets (profile in ZY)
            foreach (float x in new[] { -0.15f, 0.15f })
            {
                b.Box(new Vector3(x, -0.346f, 0.1f), new Vector3(0.06f, 0.01f, 0.2f), 2, Hole);   // slots in the base
                b.Box(new Vector3(x, 0.1f, -0.216f), new Vector3(0.06f, 0.3f, 0.01f), 2, Hole);   // slots in the upright
            }
            foreach (var p in new[] { new Vector3(-0.15f, -0.35f, 0.22f), new Vector3(0.15f, -0.35f, -0.02f) })
                b.Cyl(p, p + Vector3.up * 0.05f, 0.04f, 2, DarkSteel, 6);                      // hex bolts
            b.Box(new Vector3(0.38f, 0.3f, -0.217f), new Vector3(0.14f, 0.07f, 0.01f), 2, Warn);   // stencil tag
        }

        // ------------------------------------------------------------------ Phase 4 spare parts
        // Flat parts lie with Y up (rest pose flat); long parts run along Y. Slot 0 = body, slot 2 = accents only,
        // so every family stays at two submeshes.

        /// <summary>Hooked gripper jaw: a three-section plate bent inwards, a rubber grip pad on the inner
        /// edge, a pivot boss with a bushing and two mounting bolts.</summary>
        static void ClawJaw(B b)
        {
            const float t = 0.13f;
            b.Slab(new[] { new Vector2(-0.5f, -0.2f), new Vector2(-0.05f, -0.2f), new Vector2(-0.05f, 0.14f), new Vector2(-0.5f, 0.14f) }, -t, t, 0, Part);   // root
            b.Slab(new[] { new Vector2(-0.05f, -0.2f), new Vector2(0.3f, -0.12f), new Vector2(0.3f, 0.16f), new Vector2(-0.05f, 0.14f) }, -t, t, 0, Part);    // reach
            b.Slab(new[] { new Vector2(0.3f, -0.12f), new Vector2(0.5f, 0.02f), new Vector2(0.44f, 0.24f), new Vector2(0.3f, 0.16f) }, -t * 0.85f, t * 0.85f, 0, Part);   // hooked tip
            b.Box(new Vector3(0.12f, 0, -0.17f), new Vector3(0.42f, t * 1.7f, 0.06f), 2, Rubber);                    // grip pad
            b.Cyl(new Vector3(-0.38f, -t - 0.04f, -0.03f), new Vector3(-0.38f, t + 0.04f, -0.03f), 0.16f, 0, Part);   // pivot boss
            b.Cyl(new Vector3(-0.38f, t + 0.04f, -0.03f), new Vector3(-0.38f, t + 0.06f, -0.03f), 0.1f, 2, Chrome);  // bushing
            b.Cyl(new Vector3(-0.38f, t + 0.06f, -0.03f), new Vector3(-0.38f, t + 0.065f, -0.03f), 0.05f, 2, Hole, 8);
            foreach (float x in new[] { -0.2f, -0.1f })
                b.Cyl(new Vector3(x, t, 0.06f), new Vector3(x, t + 0.03f, 0.06f), 0.035f, 2, DarkSteel, 6);          // bolts
            b.Box(new Vector3(0.12f, t + 0.003f, 0.04f), new Vector3(0.16f, 0.005f, 0.09f), 2, Warn);               // part tag
        }

        /// <summary>Leg knee: upper and lower link plates meeting at about 120 degrees on a large pivot drum
        /// with a red joint cap; joint bosses at the link ends.</summary>
        static void LegKnee(B b)
        {
            const float t = 0.1f;
            var up = new Vector2(-0.48f, 0.26f); var lo = new Vector2(0.5f, 0.3f);   // link ends (x, z), drum at the origin
            foreach (var e in new[] { up, lo })
            {
                var d = e.normalized; var n = new Vector2(-d.y, d.x) * 0.11f;
                b.Slab(new[] { -n, e - n * 0.8f, e + n * 0.8f, n }, -t, t, 0, Part);
                b.Cyl(new Vector3(e.x, -t - 0.02f, e.y), new Vector3(e.x, t + 0.02f, e.y), 0.13f, 0, Part);             // end boss
                b.Cyl(new Vector3(e.x, t + 0.02f, e.y), new Vector3(e.x, t + 0.03f, e.y), 0.06f, 2, Hole, 8);
            }
            b.Cyl(new Vector3(0, -t - 0.06f, 0), new Vector3(0, t + 0.06f, 0), 0.25f, 0, Part);                    // pivot drum
            b.Cyl(new Vector3(0, t + 0.06f, 0), new Vector3(0, t + 0.11f, 0), 0.19f, 2, ArmRed);                   // joint cap
            b.Cyl(new Vector3(0, t + 0.11f, 0), new Vector3(0, t + 0.14f, 0), 0.07f, 2, Chrome, 8);                // axle
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI / 2 + 0.6f;
                var p = new Vector3(Mathf.Cos(a) * 0.15f, t + 0.11f, Mathf.Sin(a) * 0.15f);
                b.Cyl(p, p + Vector3.up * 0.025f, 0.025f, 2, DarkSteel, 6);                                       // cap screws
            }
            b.Box(new Vector3(-0.24f, t + 0.003f, 0.11f), new Vector3(0.14f, 0.005f, 0.07f), 2, Warn);           // part tag
        }

        /// <summary>Track shoe: a flat plate with a raised grouser cleat, three hinge knuckles on the pin edge
        /// and two shoe bolts.</summary>
        static void TrackLink(B b)
        {
            b.Box(new Vector3(0, -0.05f, 0.04f), new Vector3(1f, 0.12f, 0.5f), 0, Part);                          // shoe plate
            b.Box(new Vector3(0, 0.06f, 0.1f), new Vector3(1f, 0.12f, 0.1f), 0, Part);                             // grouser
            foreach (var (x0, x1) in new[] { (-0.5f, -0.24f), (-0.08f, 0.08f), (0.24f, 0.5f) })
                b.Cyl(new Vector3(x0, -0.03f, -0.27f), new Vector3(x1, -0.03f, -0.27f), 0.09f, 0, Part);          // hinge knuckles
            b.Cyl(new Vector3(-0.52f, -0.03f, -0.27f), new Vector3(0.52f, -0.03f, -0.27f), 0.04f, 2, Chrome, 8);   // track pin
            foreach (float x in new[] { -0.3f, 0.3f })
                b.Cyl(new Vector3(x, 0.01f, 0.2f), new Vector3(x, 0.04f, 0.2f), 0.05f, 2, DarkSteel, 6);           // shoe bolts
        }

        /// <summary>Cable-carrier link: two side plates with pin bosses, a top crossbar and a bottom shelf,
        /// open in the middle for the cables.</summary>
        static void DragChain(B b)
        {
            foreach (float x in new[] { -0.4f, 0.4f })
            {
                b.Box(new Vector3(x, 0, 0), new Vector3(0.08f, 0.56f, 0.9f), 0, Part);                              // side plate
                foreach (float z in new[] { -0.32f, 0.32f })
                    b.Cyl(new Vector3(x * 1.1f, 0, z), new Vector3(x * 1.2f, 0, z), 0.13f, 2, DarkSteel, 10);       // pin bosses
            }
            b.Box(new Vector3(0, 0.24f, 0), new Vector3(0.8f, 0.08f, 0.3f), 0, Part);                               // crossbar
            b.Box(new Vector3(0, -0.25f, 0), new Vector3(0.8f, 0.06f, 0.7f), 0, Part);                              // bottom shelf
        }

        /// <summary>Rotor hub (no blades): a stepped hub, two blade-root clamp blocks with clamp bolts,
        /// a centre lock nut on the drive shaft.</summary>
        static void RotorHub(B b)
        {
            b.Cyl(new Vector3(0, -0.2f, 0), new Vector3(0, -0.08f, 0), 0.34f, 0, Part);                            // flange
            b.Cyl(new Vector3(0, -0.08f, 0), new Vector3(0, 0.12f, 0), 0.24f, 0, Part);                            // hub body
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(new Vector3(s * 0.36f, 0.02f, 0), new Vector3(0.28f, 0.18f, 0.22f), 0, Part);                // clamp block
                b.Box(new Vector3(s * 0.47f, 0.02f, 0), new Vector3(0.05f, 0.12f, 0.15f), 2, DarkSteel);           // blade-root slot face
                foreach (float x in new[] { 0.3f, 0.42f })
                    b.Cyl(new Vector3(s * x, 0.11f, 0), new Vector3(s * x, 0.14f, 0), 0.035f, 2, DarkSteel, 6);    // clamp bolts
            }
            b.Cyl(new Vector3(0, 0.12f, 0), new Vector3(0, 0.2f, 0), 0.12f, 2, DarkSteel, 6);                      // lock nut
            b.Cyl(new Vector3(0, 0.2f, 0), new Vector3(0, 0.26f, 0), 0.06f, 2, Chrome, 8);                         // shaft end
        }

        /// <summary>Antenna mount: a round flange with screw holes, a boss with a coax socket and a short
        /// stub mast with a knob tip.</summary>
        static void AntennaMount(B b)
        {
            b.Cyl(new Vector3(0, -0.4f, 0), new Vector3(0, -0.3f, 0), 0.42f, 0, Part);                             // flange
            b.Cyl(new Vector3(0, -0.3f, 0), new Vector3(0, -0.08f, 0), 0.2f, 0, Part);                             // boss
            b.Cyl(new Vector3(0, -0.08f, 0), new Vector3(0, 0.3f, 0), 0.07f, 2, Chrome, 8);                        // stub mast
            b.Cyl(new Vector3(0, 0.3f, 0), new Vector3(0, 0.4f, 0), 0.11f, 2, Rubber, 8);                          // knob tip
            b.Cyl(new Vector3(0.18f, -0.2f, 0), new Vector3(0.32f, -0.2f, 0), 0.07f, 2, Warn, 8);                 // gold coax socket
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI * 2 / 3 + 1.1f;
                var p = new Vector3(Mathf.Cos(a) * 0.32f, -0.3f, Mathf.Sin(a) * 0.32f);
                b.Cyl(p, p + Vector3.up * 0.005f, 0.04f, 2, Hole, 6);                                              // screw holes
            }
        }

        /// <summary>Battery pack: three cells side by side in a shrink-wrap band (the label plate), nickel
        /// terminal tabs and a red / black lead.</summary>
        static void BatteryCell(B b)
        {
            foreach (float x in new[] { -0.3f, 0f, 0.3f })
                b.Cyl(new Vector3(x, 0, -0.45f), new Vector3(x, 0, 0.45f), 0.155f, 0, Part);                       // cells
            b.Box(new Vector3(0, 0, 0), new Vector3(0.94f, 0.33f, 0.36f), 2, Warn);                               // wrap band (label)
            foreach (float x in new[] { -0.3f, 0.3f })
                b.Box(new Vector3(x, 0, 0.465f), new Vector3(0.22f, 0.2f, 0.03f), 2, Chrome);                      // terminal tabs
            b.Cyl(new Vector3(-0.08f, 0.05f, 0.47f), new Vector3(-0.12f, 0.08f, 0.62f), 0.035f, 2, WireRed, 6);   // lead
            b.Cyl(new Vector3(0.08f, 0.05f, 0.47f), new Vector3(0.12f, 0.08f, 0.62f), 0.035f, 2, Rubber, 6);
        }

        /// <summary>Gyro module: a small board carrying two uprights with an axle and a dark flywheel rotor,
        /// the sensor chip and a header strip.</summary>
        static void GyroModule(B b)
        {
            b.Box(new Vector3(0, -0.4f, 0), new Vector3(1.0f, 0.08f, 0.8f), 0, Part);                              // board (variant colour)
            foreach (float x in new[] { -0.3f, 0.3f })
                b.Box(new Vector3(x, -0.08f, -0.05f), new Vector3(0.08f, 0.56f, 0.16f), 2, Alu);                   // uprights
            b.Cyl(new Vector3(-0.34f, 0.05f, -0.05f), new Vector3(0.34f, 0.05f, -0.05f), 0.045f, 2, Chrome, 8);    // axle
            b.Cyl(new Vector3(-0.07f, 0.05f, -0.05f), new Vector3(0.07f, 0.05f, -0.05f), 0.27f, 2, DarkSteel);     // rotor
            b.Cyl(new Vector3(-0.09f, 0.05f, -0.05f), new Vector3(0.09f, 0.05f, -0.05f), 0.09f, 2, ArmRed, 8);    // rotor hub
            b.Box(new Vector3(0.28f, -0.33f, 0.27f), new Vector3(0.22f, 0.06f, 0.18f), 2, Chip);                  // sensor chip
            b.Box(new Vector3(0, -0.32f, -0.34f), new Vector3(0.7f, 0.08f, 0.1f), 2, Chip);                       // header strip
        }

        /// <summary>Clamp shaft coupling: a short cylinder with slit clamps and a cap screw at each end,
        /// bores at both ends.</summary>
        static void JointCoupling(B b)
        {
            const float h = 0.75f;
            b.Cyl(new Vector3(0, -h, 0), new Vector3(0, h, 0), 0.5f, 0, Part);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(new Vector3(0, s * (h - 0.22f), 0.42f), new Vector3(0.06f, 0.44f, 0.2f), 2, Hole);           // clamp slit
                b.Cyl(new Vector3(-0.3f, s * (h - 0.22f), 0.32f), new Vector3(0.3f, s * (h - 0.22f), 0.32f), 0.08f, 2, DarkSteel, 8);   // cap screw
                b.Cyl(new Vector3(0, s * h, 0), new Vector3(0, s * (h + 0.005f), 0), 0.22f, 2, Hole, 10);          // bore
            }
        }

        /// <summary>Telescopic arm tube: an outer tube with a red collar lock and lever, a chrome inner tube
        /// drawn out, clevis eyes at both ends.</summary>
        static void TelescopicTube(B b)
        {
            const float h = 2.5f;
            b.Cyl(new Vector3(0, -h + 0.3f, 0), new Vector3(0, 0.3f, 0), 0.5f, 0, Part);                          // outer tube
            b.Cyl(new Vector3(0, 0.3f, 0), new Vector3(0, h - 0.3f, 0), 0.36f, 2, Chrome);                        // inner tube
            b.Cyl(new Vector3(0, 0.15f, 0), new Vector3(0, 0.45f, 0), 0.58f, 2, ArmRed);                          // collar lock
            b.Box(new Vector3(0.62f, 0.3f, 0), new Vector3(0.18f, 0.24f, 0.2f), 2, DarkSteel);                    // lock lever
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(new Vector3(0, s * (h - 0.15f), 0), new Vector3(0.6f, 0.3f, 0.5f), s < 0 ? 0 : 2, s < 0 ? Part : Chrome);   // clevis eye
                b.Cyl(new Vector3(-0.31f, s * (h - 0.15f), 0), new Vector3(0.31f, s * (h - 0.15f), 0), 0.12f, 2, Hole, 8);
            }
        }

        /// <summary>Servo horn: a splined hub with a cross arm, a row of linkage holes and a centre screw.</summary>
        static void ServoHorn(B b)
        {
            b.Box(new Vector3(0, 0, 0), new Vector3(1f, 0.12f, 0.2f), 0, Part);                                   // long arm
            b.Box(new Vector3(0, 0, 0), new Vector3(0.18f, 0.12f, 0.66f), 0, Part);                               // short arm
            b.Cyl(new Vector3(0, -0.12f, 0), new Vector3(0, 0.1f, 0), 0.2f, 0, Part);                             // hub
            b.Cyl(new Vector3(0, 0.1f, 0), new Vector3(0, 0.12f, 0), 0.12f, 2, Ivory, 10);                        // spline insert
            b.Cyl(new Vector3(0, 0.12f, 0), new Vector3(0, 0.15f, 0), 0.05f, 2, Chrome, 6);                       // centre screw
            foreach (float x in new[] { -0.42f, -0.3f, 0.3f, 0.42f })
                b.Cyl(new Vector3(x, 0.06f, 0), new Vector3(x, 0.065f, 0), 0.035f, 2, Hole, 6);                   // linkage holes
        }

        /// <summary>Encoder disc: a thick disc with a ring of dark slots, a hub with a set screw and bore.</summary>
        static void EncoderDisc(B b)
        {
            b.Cyl(new Vector3(0, -0.07f, 0), new Vector3(0, 0.05f, 0), 0.5f, 0, Part);                            // disc
            for (int k = 0; k < 10; k++)
            {
                float a = k * Mathf.PI * 2 / 10;
                b.Box(new Vector3(Mathf.Cos(a) * 0.38f, 0.053f, Mathf.Sin(a) * 0.38f), new Vector3(0.07f, 0.008f, 0.07f), 2, Hole);   // slots
            }
            b.Cyl(new Vector3(0, -0.07f, 0), new Vector3(0, 0.2f, 0), 0.18f, 2, DarkSteel);                       // hub
            b.Cyl(new Vector3(0, 0.2f, 0), new Vector3(0, 0.205f, 0), 0.08f, 2, Hole, 8);                         // bore
            b.Cyl(new Vector3(0.17f, 0.1f, 0), new Vector3(0.23f, 0.1f, 0), 0.035f, 2, Chrome, 6);                // set screw
        }

        // ------------------------------------------------------------------ builder

        public class B
        {
            public int seg = 16;
            readonly List<Vector3> _v = new(), _n = new();
            readonly List<Color> _c = new();
            readonly List<Vector2> _uv = new();
            readonly SortedDictionary<int, List<int>> _tris = new();

            int V(Vector3 p, Vector3 n, Color c)
            {
                _v.Add(p); _n.Add(n.normalized); _c.Add(c); _uv.Add(Vector2.zero);
                return _v.Count - 1;
            }

            /// <summary>Triangle facing along n (winding fixed so Unity's front face points out).</summary>
            void Tri(int slot, int a, int b, int c, Vector3 n)
            {
                if (!_tris.TryGetValue(slot, out var t)) _tris[slot] = t = new List<int>();
                if (Vector3.Dot(Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]), n) < 0) (b, c) = (c, b);
                t.Add(a); t.Add(b); t.Add(c);
            }

            void Quad(int slot, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n, Color c)
            {
                int a = V(p0, n, c), b1 = V(p1, n, c), c1 = V(p2, n, c), d = V(p3, n, c);
                Tri(slot, a, b1, c1, n); Tri(slot, a, c1, d, n);
            }

            public void Box(Vector3 c, Vector3 s, int slot, Color col)
            {
                var h = s * 0.5f;
                foreach (var ax in new[] { Vector3.right, Vector3.up, Vector3.forward })
                    foreach (float sg in new[] { -1f, 1f })
                    {
                        var n = ax * sg;
                        var u = ax == Vector3.up ? Vector3.right : Vector3.up;
                        var w = Vector3.Cross(n, u);
                        Vector3 F(float a, float b2) => c + Vector3.Scale(n, h) + Vector3.Scale(u * a + w * b2, h);
                        Quad(slot, F(-1, -1), F(1, -1), F(1, 1), F(-1, 1), n, col);
                    }
            }

            /// <summary>Capped cylinder from a to b (smooth sides, flat caps).</summary>
            public void Cyl(Vector3 a, Vector3 b, float r, int slot, Color col, int segOverride = 0)
            {
                int n = segOverride > 0 ? segOverride : seg;
                var ax = (b - a).normalized;
                var u = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                var w = Vector3.Cross(ax, u);
                for (int i = 0; i < n; i++)
                {
                    float t0 = i * Mathf.PI * 2 / n, t1 = (i + 1) * Mathf.PI * 2 / n;
                    var d0 = u * Mathf.Cos(t0) + w * Mathf.Sin(t0);
                    var d1 = u * Mathf.Cos(t1) + w * Mathf.Sin(t1);
                    int p0 = V(a + d0 * r, d0, col), p1 = V(a + d1 * r, d1, col), p2 = V(b + d1 * r, d1, col), p3 = V(b + d0 * r, d0, col);
                    var mid = (d0 + d1).normalized;
                    Tri(slot, p0, p1, p2, mid); Tri(slot, p0, p2, p3, mid);
                    int ca = V(a, -ax, col), c0 = V(a + d0 * r, -ax, col), c1 = V(a + d1 * r, -ax, col);
                    Tri(slot, ca, c0, c1, -ax);
                    int cb = V(b, ax, col), e0 = V(b + d0 * r, ax, col), e1 = V(b + d1 * r, ax, col);
                    Tri(slot, cb, e0, e1, ax);
                }
            }

            /// <summary>Convex polygon in the (z, y) plane extruded along X from x0 to x1.</summary>
            public void Prism(Vector2[] zy, float x0, float x1, int slot, Color col) => Extrude(zy, Vector3.forward, Vector3.up, Vector3.right, x0, x1, slot, col);

            /// <summary>Convex polygon in the (x, z) plane extruded along Y from y0 to y1 (flat plates).</summary>
            public void Slab(Vector2[] xz, float y0, float y1, int slot, Color col) => Extrude(xz, Vector3.right, Vector3.forward, Vector3.up, y0, y1, slot, col);

            void Extrude(Vector2[] poly, Vector3 e1, Vector3 e2, Vector3 ax, float t0, float t1, int slot, Color col)
            {
                Vector3 P(Vector2 q, float t) => e1 * q.x + e2 * q.y + ax * t;
                for (int i = 1; i + 1 < poly.Length; i++)
                {
                    Tri(slot, V(P(poly[0], t0), -ax, col), V(P(poly[i], t0), -ax, col), V(P(poly[i + 1], t0), -ax, col), -ax);
                    Tri(slot, V(P(poly[0], t1), ax, col), V(P(poly[i], t1), ax, col), V(P(poly[i + 1], t1), ax, col), ax);
                }
                var centre = Vector2.zero;
                foreach (var q in poly) centre += q / poly.Length;
                for (int i = 0; i < poly.Length; i++)
                {
                    var q0 = poly[i]; var q1 = poly[(i + 1) % poly.Length];
                    var e = q1 - q0;
                    var nn = new Vector2(e.y, -e.x).normalized;
                    if (Vector2.Dot(nn, (q0 + q1) * 0.5f - centre) < 0) nn = -nn;
                    var n3 = e1 * nn.x + e2 * nn.y;
                    Quad(slot, P(q0, t0), P(q1, t0), P(q1, t1), P(q0, t1), n3, col);
                }
            }

            public MeshLibrary.Entry Finish(string name)
            {
                var mesh = new Mesh { name = name };
                if (_v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetUVs(0, _uv);
                mesh.SetColors(_c);
                mesh.subMeshCount = _tris.Count;
                var slots = new int[_tris.Count];
                int sub = 0;
                foreach (var kv in _tris)
                {
                    mesh.SetTriangles(kv.Value, sub, false);
                    slots[sub++] = kv.Key;
                }
                mesh.RecalculateBounds();
                // centre like the exported meshes
                var off = -mesh.bounds.center;
                if (off.sqrMagnitude > 1e-8f)
                {
                    for (int i = 0; i < _v.Count; i++) _v[i] += off;
                    mesh.SetVertices(_v);
                    mesh.RecalculateBounds();
                }
                SkinUV.Apply(mesh, slots);   // skin coordinates (extra channels only)
                mesh.UploadMeshData(false);
                return new MeshLibrary.Entry { mesh = mesh, slots = slots, bounds = mesh.bounds };
            }
        }
    }
}
