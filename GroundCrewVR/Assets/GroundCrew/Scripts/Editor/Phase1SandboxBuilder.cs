using System;
using System.IO;
using GroundCrew.Core;
using GroundCrew.Interaction;
using GroundCrew.Player;
using GroundCrew.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using Object = UnityEngine.Object;

namespace GroundCrew.EditorTools
{
    /// <summary>
    /// Menu: Ground Crew > Build Phase 1 Sandbox Scene.
    ///
    /// Generates Assets/GroundCrew/Scenes/Phase1_Sandbox.unity: an apron "training area" with
    /// the XRI Starter Assets rig, a wrist tablet, and five stations that exercise every
    /// Phase 1 interaction system:
    ///   A  Baggage     - grab bags of different weights, load a hold with a 60 kg limit
    ///   B  Safety      - cones and wheel chocks, chock positions at a tyre
    ///   C  Controls    - latching push button driving a beacon, spring-return lever
    ///   D  Cargo door  - 737-style inward/upward-opening hold door, mock hold behind it
    ///   E  GPU         - tethered ground-power plug, receptacle behind an access panel
    ///
    /// Re-running the menu rebuilds the scene from scratch (materials/meshes/item data are
    /// reused). Props are deliberately simple geometry: Phase 1 is about how things *work*
    /// in VR; modelled assets arrive with Phase 2.
    /// </summary>
    public static class Phase1SandboxBuilder
    {
        const string k_Root = "Assets/GroundCrew";
        const string k_ScenePath = k_Root + "/Scenes/Phase1_Sandbox.unity";
        const string k_MaterialDir = k_Root + "/Generated/Materials";
        const string k_MeshDir = k_Root + "/Generated/Meshes";
        const string k_ItemDir = k_Root + "/Data/Items";
        const string k_RigPrefabName = "XR Origin (XR Rig).prefab";

        static Palette s_Mat;

        /// <summary>Shared generated materials (also used by other Ground Crew editor tools).</summary>
        internal static Palette Materials => s_Mat ?? (s_Mat = new Palette());

        [MenuItem("Ground Crew/Build Phase 1 Sandbox Scene", priority = 0)]
        public static void Build()
        {
            if (Resources.Load<TMP_Settings>("TMP Settings") == null)
            {
                EditorUtility.DisplayDialog("TextMesh Pro resources missing",
                    "Import them first: Window > TextMeshPro > Import TMP Essential Resources.\nThen run Ground Crew > Build Phase 1 Sandbox Scene again.",
                    "OK");
                return;
            }

            var rigPrefab = FindRigPrefab();
            if (rigPrefab == null)
            {
                EditorUtility.DisplayDialog("XR Interaction Toolkit Starter Assets missing",
                    "Could not find '" + k_RigPrefabName + "'.\n\nWindow > Package Manager > XR Interaction Toolkit > Samples > Starter Assets > Import.\nThen run this menu again.",
                    "OK");
                return;
            }

            if (GraphicsSettings.defaultRenderPipeline == null)
                Debug.LogWarning("[Sandbox] No render pipeline asset assigned. Use the Unity 'VR' (URP) template - see README.");

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(k_MaterialDir);
            EnsureFolder(k_MeshDir);
            EnsureFolder(k_ItemDir);
            EnsureFolder(Path.GetDirectoryName(k_ScenePath)?.Replace('\\', '/'));

            s_Mat = new Palette();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLightingAndGround();
            new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            new GameObject("Systems").AddComponent<QuestPerformanceSettings>();

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rig.AddComponent<LocomotionComfortApplier>();
            rig.AddComponent<FingertipSetup>();
            HandsSetup.Install(rig.transform);

            var leftController = FindDeep(rig.transform, "Left Controller");
            if (leftController != null) BuildWristTablet(leftController);
            else Debug.LogWarning("[Sandbox] 'Left Controller' not found in rig - wrist tablet skipped.");

            BuildBaggageStation(Station("A_Baggage", new Vector3(-2.3f, 0f, 2.3f), "A  BAGGAGE", "Grab bags (grip). 32 kg bag needs both hands. Hold limit 60 kg."));
            BuildSafetyStation(Station("B_Safety", new Vector3(2.3f, 0f, 2.3f), "B  SAFETY EQUIPMENT", "Chock the wheel fore and aft. Place cones."));
            BuildControlsStation(Station("C_Controls", new Vector3(0f, 0f, 3.1f), "C  CONTROLS", "Press the button with your fingertip. Pull the lever."));
            BuildCargoDoorStation(Station("D_CargoDoor", new Vector3(-3.4f, 0f, -0.3f), "D  CARGO DOOR", "Lift the handle - struts take it from halfway. Load bags inside.", 3.05f, 0.6f, false));
            BuildGpuStation(Station("E_GPU", new Vector3(3.4f, 0f, -0.3f), "E  GROUND POWER", "Open the panel, plug in the GPU cable. 4 m cable.", 2.85f, 0.6f, false));

            VehicleAssembler.InstallSpawnMenu();   // no-op until Add Vehicles has built the catalog
            OperationsSetup.Install(false);        // Ops iPad, flight crew and AI ground crew

            EditorSceneManager.SaveScene(scene, k_ScenePath);
            AddSceneToBuildSettings(k_ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Sandbox] Built {k_ScenePath}. Build And Run to your Quest 2, or press Play with Quest Link / XR Device Simulator.");
        }

        // ------------------------------------------------------------------ environment

        static void BuildLightingAndGround()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Hard; // single hard-shadow cascade; bake for the real airport
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.63f, 0.75f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.47f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.25f, 0.26f);
            RenderSettings.sun = sun;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Apron";
            ground.transform.localScale = new Vector3(10f, 1f, 10f); // 100 x 100 m
            ground.GetComponent<Renderer>().sharedMaterial = s_Mat.Concrete;
            MarkStatic(ground);

            var teleport = ground.AddComponent<TeleportationArea>();
            var teleportLayer = InteractionLayerMask.GetMask("Teleport");
            if (teleportLayer != 0) teleport.interactionLayers = teleportLayer;

            // Stand markings: lead-in line, stop bar and an equipment restraint line (decor for now;
            // Phase 2 parks the aircraft on this stand).
            var markings = new GameObject("Stand Markings").transform;
            // Nose-gear stop bar at z = 12 leaves room for the ~4-5 m nose overhang in front of the
            // training stations (see AircraftPlacer).
            Box("Lead-in line", markings, new Vector3(0f, 0.004f, 30f), new Vector3(0.15f, 0.005f, 36f), s_Mat.Yellow, false, true);
            Box("Stop bar", markings, new Vector3(0f, 0.004f, 12f), new Vector3(3f, 0.005f, 0.2f), s_Mat.Yellow, false, true);
            Box("Restraint line L", markings, new Vector3(-6.5f, 0.004f, 18f), new Vector3(0.12f, 0.005f, 14f), s_Mat.Red, false, true);
            Box("Restraint line R", markings, new Vector3(6.5f, 0.004f, 18f), new Vector3(0.12f, 0.005f, 14f), s_Mat.Red, false, true);
            var standSign = Text3D(markings, new Vector3(-9f, 3.2f, 12f), "<b>STAND 24</b>\n<size=50%>TRAINING AREA</size>", 6f, new Vector2(8f, 3f), Color.white);
            standSign.transform.rotation = Quaternion.LookRotation(new Vector3(-9f, 0f, 12f).normalized, Vector3.up);
            standSign.gameObject.name = "Stand sign";
        }

        /// <summary>A station root whose local -Z faces the player spawn at the origin.</summary>
        static Transform Station(string name, Vector3 position, string title, string help,
            float signHeight = 2.2f, float signZ = 0.75f, bool signPost = true)
        {
            var root = new GameObject(name).transform;
            var flat = new Vector3(position.x, 0f, position.z);
            root.SetPositionAndRotation(position, Quaternion.LookRotation(flat.normalized, Vector3.up));

            Box("Floor pad", root, new Vector3(0f, 0.002f, 0f), new Vector3(2.6f, 0.004f, 2.2f), s_Mat.Pad, false, true);
            Box("Sign board", root, new Vector3(0f, signHeight, signZ), new Vector3(2.1f, 0.5f, 0.04f), s_Mat.SignBoard, true, true);
            if (signPost)
                Box("Sign post", root, new Vector3(0f, (signHeight - 0.25f) / 2f, signZ + 0.03f), new Vector3(0.06f, signHeight - 0.25f, 0.06f), s_Mat.DarkMetal, true, true);
            var text = Text3D(root, new Vector3(0f, signHeight, signZ - 0.025f), $"<b>{title}</b>\n<size=55%>{help}</size>", 1.6f, new Vector2(2f, 0.46f), Color.white);
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.4f;
            text.fontSizeMax = 1.6f;
            return root;
        }

        // ------------------------------------------------------------------ A: baggage

        static void BuildBaggageStation(Transform s)
        {
            var cabin = Def("Bag_Cabin", "bag.cabin", "Cabin Bag", ItemCategory.Suitcase, 8f);
            var standard = Def("Bag_Checked", "bag.checked", "Checked Suitcase", ItemCategory.Suitcase, 18f);
            var heavy = Def("Bag_Heavy", "bag.heavy", "Heavy Suitcase", ItemCategory.Suitcase, 32f, twoHands: true);
            var fragile = Def("Box_Fragile", "box.fragile", "Fragile Box", ItemCategory.Box | ItemCategory.Fragile, 6f, fragile: true, impactSpeed: 2.5f);

            // Baggage cart bed (cart height ~0.45 m).
            Box("Cart bed", s, new Vector3(-0.78f, 0.225f, 0f), new Vector3(1.45f, 0.45f, 0.85f), s_Mat.CartBlue, true, true);

            Suitcase("Cabin Bag", s, new Vector3(-1.28f, 0.45f, -0.1f), new Vector3(0.36f, 0.5f, 0.22f), s_Mat.BagBlack, cabin, "sandbox.bag.cabin");
            Suitcase("Checked Suitcase", s, new Vector3(-0.86f, 0.45f, -0.1f), new Vector3(0.42f, 0.66f, 0.26f), s_Mat.BagRed, standard, "sandbox.bag.checked");
            Suitcase("Heavy Suitcase", s, new Vector3(-0.36f, 0.45f, -0.1f), new Vector3(0.5f, 0.75f, 0.3f), s_Mat.BagGreen, heavy, "sandbox.bag.heavy", dynamicAttach: true);
            CardboardBox("Fragile Box", s, new Vector3(-0.8f, 0f, -0.75f), new Vector3(0.4f, 0.3f, 0.4f), fragile, "sandbox.box.fragile");

            // Open-top bin standing in for a cargo hold section.
            var bin = new GameObject("Test Hold").transform;
            bin.SetParent(s, false);
            bin.localPosition = new Vector3(0.85f, 0f, 0f);
            Box("Plinth", bin, new Vector3(0f, 0.275f, 0f), new Vector3(1.2f, 0.55f, 0.95f), s_Mat.HoldLining, true, true);
            Box("Wall back", bin, new Vector3(0f, 0.8f, 0.45f), new Vector3(1.2f, 0.5f, 0.05f), s_Mat.HoldLining, true, true);
            Box("Wall left", bin, new Vector3(-0.575f, 0.8f, 0f), new Vector3(0.05f, 0.5f, 0.95f), s_Mat.HoldLining, true, true);
            Box("Wall right", bin, new Vector3(0.575f, 0.8f, 0f), new Vector3(0.05f, 0.5f, 0.95f), s_Mat.HoldLining, true, true);
            Box("Lip front", bin, new Vector3(0f, 0.6f, -0.45f), new Vector3(1.2f, 0.1f, 0.05f), s_Mat.Yellow, true, true);

            var label = Text3D(bin, new Vector3(0f, 1.35f, 0.42f), "TEST HOLD", 0.9f, new Vector2(1.2f, 0.3f), Color.white);
            Zone("Test Hold", bin, new Vector3(0f, 0.82f, 0f), new Vector3(1.05f, 0.5f, 0.82f),
                ItemCategory.Suitcase | ItemCategory.Duffel | ItemCategory.Box | ItemCategory.Fragile, 60f, 0, label);
        }

        // ------------------------------------------------------------------ B: safety equipment

        static void BuildSafetyStation(Transform s)
        {
            var coneDef = Def("Safety_Cone", "equip.cone", "Safety Cone", ItemCategory.SafetyCone, 3f, impactSpeed: 8f);
            var chockDef = Def("Wheel_Chock", "equip.chock", "Wheel Chock", ItemCategory.WheelChock, 6f, impactSpeed: 8f);

            Cone("Cone 1", s, new Vector3(-0.95f, 0f, -0.55f), coneDef, "sandbox.cone.1");
            Cone("Cone 2", s, new Vector3(-0.55f, 0f, -0.8f), coneDef, "sandbox.cone.2");
            Cone("Cone 3", s, new Vector3(0.95f, 0f, -0.55f), coneDef, "sandbox.cone.3");
            Chock("Chock 1", s, new Vector3(0.35f, 0f, -0.75f), chockDef, "sandbox.chock.1");
            Chock("Chock 2", s, new Vector3(0.7f, 0f, -0.95f), chockDef, "sandbox.chock.2");

            // Practice tyre (737-800 main gear tyre is ~1.1 m diameter, ~0.4 m wide), axle along X.
            var tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tyre.name = "Practice tyre";
            tyre.transform.SetParent(s, false);
            tyre.transform.localPosition = new Vector3(0f, 0.55f, 0.5f);
            tyre.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            tyre.transform.localScale = new Vector3(1.1f, 0.21f, 1.1f);
            tyre.GetComponent<Renderer>().sharedMaterial = s_Mat.Rubber;
            Object.DestroyImmediate(tyre.GetComponent<Collider>()); // capsule would be a 1.1 m sphere
            tyre.AddComponent<MeshCollider>();
            MarkStatic(tyre);

            // Chock positions: directly in front of and behind the tyre tread.
            // The tread is at z = 0.18 / 0.82 at chock height, so the zones hug it.
            Zone("Chock FWD", s, new Vector3(0f, 0.12f, 0.02f), new Vector3(0.5f, 0.25f, 0.34f), ItemCategory.WheelChock, 0f, 1, null);
            Zone("Chock AFT", s, new Vector3(0f, 0.12f, 0.98f), new Vector3(0.5f, 0.25f, 0.34f), ItemCategory.WheelChock, 0f, 1, null);
            Box("Chock mark FWD", s, new Vector3(0f, 0.004f, 0.02f), new Vector3(0.5f, 0.004f, 0.34f), s_Mat.White, false, true);
            Box("Chock mark AFT", s, new Vector3(0f, 0.004f, 0.98f), new Vector3(0.5f, 0.004f, 0.34f), s_Mat.White, false, true);
        }

        // ------------------------------------------------------------------ C: controls

        static void BuildControlsStation(Transform s)
        {
            Box("Pedestal", s, new Vector3(0f, 0.5f, 0.1f), new Vector3(0.9f, 1f, 0.5f), s_Mat.DarkMetal, true, true);

            // Latching push button (pressed from above).
            Box("Button bezel", s, new Vector3(-0.22f, 1.005f, 0f), new Vector3(0.11f, 0.01f, 0.11f), s_Mat.Black, false, true);
            var button = new GameObject("Beacon Button");
            button.transform.SetParent(s, false);
            button.transform.localPosition = new Vector3(-0.22f, 1.01f, 0f);
            var trigger = button.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0.03f, 0f);
            trigger.size = new Vector3(0.075f, 0.05f, 0.075f);
            var cap = Cylinder("Cap", button.transform, new Vector3(0f, 0.012f, 0f), new Vector3(0.065f, 0.012f, 0.065f), s_Mat.Green);
            var press = button.AddComponent<PressButton>();
            Props(press, ("label", "Beacon"), ("movingCap", cap.transform), ("pressDirection", Vector3.down), ("pressDepth", 0.01f), ("toggle", true));

            // Rotating-beacon style lamp driven by the button.
            Box("Beacon pole", s, new Vector3(0.36f, 1.3f, 0.25f), new Vector3(0.04f, 0.6f, 0.04f), s_Mat.DarkMetal, true, true);
            var lamp = Sphere("Beacon lamp", s, new Vector3(0.36f, 1.65f, 0.25f), 0.1f, s_Mat.AmberOff);
            var beacon = lamp.AddComponent<IndicatorLight>();
            Props(beacon, ("onMaterial", s_Mat.AmberOn), ("offMaterial", s_Mat.AmberOff), ("blink", true), ("blinkHz", 1.2f));
            UnityEventTools.AddPersistentListener(press.onToggled, beacon.SetOn);

            // Spring-return lever (like a deadman / throttle), -35..+35 degrees about X.
            Box("Lever base", s, new Vector3(0.15f, 1.005f, 0f), new Vector3(0.12f, 0.01f, 0.12f), s_Mat.Black, false, true);
            var pivot = new GameObject("Lever pivot").transform;
            pivot.SetParent(s, false);
            pivot.localPosition = new Vector3(0.15f, 1.01f, 0f);
            Box("Lever stick", pivot, new Vector3(0f, 0.14f, 0f), new Vector3(0.025f, 0.28f, 0.025f), s_Mat.Steel, false, false);
            var knob = Sphere("Lever knob", pivot, new Vector3(0f, 0.3f, 0f), 0.06f, s_Mat.Red);
            knob.AddComponent<XRSimpleInteractable>();
            var lever = knob.AddComponent<RotaryHandle>();
            Props(lever, ("pivot", pivot), ("localAxis", Vector3.right), ("minAngle", -35f), ("maxAngle", 35f),
                ("startAngle", 0f), ("returnToRest", true), ("restAngle", 0f), ("returnSpeed", 140f), ("detentStep", 7f));

            var readoutText = Text3D(s, new Vector3(0f, 1.42f, 0.36f), "THROTTLE 0%", 0.8f, new Vector2(0.9f, 0.2f), Color.white);
            var readout = readoutText.gameObject.AddComponent<ValueReadout>();
            Props(readout, ("text", readoutText), ("label", "THROTTLE"), ("scale", 200f), ("offset", -100f), ("unit", "%"));
            UnityEventTools.AddPersistentListener(lever.onValueChanged, readout.SetValue01);
        }

        // ------------------------------------------------------------------ D: cargo door

        static void BuildCargoDoorStation(Transform s)
        {
            // Fuselage wall section with a 1.22 x 1.1 m opening, sill at 1.1 m.
            const float sill = 1.1f, top = 2.2f, halfW = 0.61f, wallZ = 0.6f, wallTop = 2.8f;
            var wall = new GameObject("Fuselage section").transform;
            wall.SetParent(s, false);
            Box("Below sill", wall, new Vector3(0f, sill / 2f, wallZ), new Vector3(2.6f, sill, 0.1f), s_Mat.Fuselage, true, true);
            Box("Above door", wall, new Vector3(0f, (top + wallTop) / 2f, wallZ), new Vector3(2.6f, wallTop - top, 0.1f), s_Mat.Fuselage, true, true);
            Box("Left of door", wall, new Vector3(-(1.3f + halfW) / 2f, (sill + top) / 2f, wallZ), new Vector3(1.3f - halfW, top - sill, 0.1f), s_Mat.Fuselage, true, true);
            Box("Right of door", wall, new Vector3((1.3f + halfW) / 2f, (sill + top) / 2f, wallZ), new Vector3(1.3f - halfW, top - sill, 0.1f), s_Mat.Fuselage, true, true);

            // Hold behind the opening.
            var hold = new GameObject("Hold").transform;
            hold.SetParent(s, false);
            Box("Hold base", hold, new Vector3(0f, (sill - 0.05f) / 2f, 1.2f), new Vector3(1.3f, sill - 0.05f, 1.1f), s_Mat.Fuselage, true, true);
            Box("Hold floor", hold, new Vector3(0f, sill - 0.025f, 1.2f), new Vector3(1.3f, 0.05f, 1.1f), s_Mat.HoldLining, true, true);
            Box("Hold ceiling", hold, new Vector3(0f, top + 0.025f, 1.2f), new Vector3(1.3f, 0.05f, 1.1f), s_Mat.HoldLining, true, true);
            Box("Hold back", hold, new Vector3(0f, (sill + top) / 2f, 1.75f), new Vector3(1.3f, top - sill, 0.05f), s_Mat.HoldLining, true, true);
            Box("Hold side L", hold, new Vector3(-0.65f, (sill + top) / 2f, 1.2f), new Vector3(0.05f, top - sill, 1.1f), s_Mat.HoldLining, true, true);
            Box("Hold side R", hold, new Vector3(0.65f, (sill + top) / 2f, 1.2f), new Vector3(0.05f, top - sill, 1.1f), s_Mat.HoldLining, true, true);
            var holdLabel = Text3D(s, new Vector3(0f, wallTop - 0.25f, wallZ - 0.055f), "BULK HOLD", 0.8f, new Vector2(1.4f, 0.3f), Color.black);
            Zone("Bulk Hold", hold, new Vector3(0f, sill + 0.4f, 1.2f), new Vector3(1.15f, 0.75f, 0.95f),
                ItemCategory.Suitcase | ItemCategory.Duffel | ItemCategory.Box | ItemCategory.Fragile | ItemCategory.Cargo, 250f, 0, holdLabel);

            // Door: hinged on its top edge, swings OUT and up so it can be reached from the apron.
            // This rig tests the hinge interaction only. The aircraft-specific doors come with the
            // aircraft: the 737's inward-opening, spring-counterbalanced plug door (Phase 2) and the
            // A320's outward-opening hydraulic door with hand pump (Phase 5).
            var pivot = new GameObject("Door hinge").transform;
            pivot.SetParent(s, false);
            pivot.localPosition = new Vector3(0f, top, wallZ - 0.07f);
            var hingeBody = pivot.gameObject.AddComponent<Rigidbody>();
            hingeBody.isKinematic = true; // moving door collider pushes bags instead of tunnelling
            Box("Door panel", pivot, new Vector3(0f, -(top - sill) / 2f, 0f), new Vector3(halfW * 2f - 0.02f, top - sill - 0.02f, 0.05f), s_Mat.Fuselage, true, false);
            Box("Door seal", pivot, new Vector3(0f, -(top - sill) / 2f, -0.026f), new Vector3(halfW * 2f - 0.1f, top - sill - 0.1f, 0.002f), s_Mat.FuselageTrim, false, false);
            var handle = Box("Door handle", pivot, new Vector3(0f, -(top - sill) + 0.12f, -0.06f), new Vector3(0.3f, 0.04f, 0.05f), s_Mat.DarkMetal, true, false);
            handle.AddComponent<XRSimpleInteractable>();
            var door = handle.AddComponent<RotaryHandle>();
            // Max 80 degrees keeps the handle reachable (~2.1 m) when open; gas struts finish the swing.
            Props(door, ("handleName", "Cargo door"), ("pivot", pivot), ("localAxis", Vector3.right), ("minAngle", 0f), ("maxAngle", 80f),
                ("startAngle", 0f), ("maxSpeed", 110f), ("returnSpeed", 45f), ("assistOpenAbove", 45f), ("detentStep", 15f), ("breakDistance", 0.5f));
            handle.AddComponent<StableId>();
            Props(handle.GetComponent<StableId>(), ("value", "sandbox.door.cargo"));
        }

        // ------------------------------------------------------------------ E: ground power

        static void BuildGpuStation(Transform s)
        {
            const float wallZ = 0.6f, face = wallZ - 0.05f;
            Box("Fuselage section", s, new Vector3(0f, 1.3f, wallZ), new Vector3(2.0f, 2.6f, 0.1f), s_Mat.Fuselage, true, true);
            Box("Receptacle recess", s, new Vector3(0.4f, 1.2f, face - 0.005f), new Vector3(0.34f, 0.34f, 0.01f), s_Mat.Black, false, true);

            // Receptacle (socket). Attach +Z points into the fuselage.
            var socketGo = new GameObject("AC Receptacle");
            socketGo.transform.SetParent(s, false);
            socketGo.transform.localPosition = new Vector3(0.4f, 1.2f, face - 0.03f);
            var socketCollider = socketGo.AddComponent<SphereCollider>();
            socketCollider.isTrigger = true;
            socketCollider.radius = 0.09f;
            var socketAttach = new GameObject("Attach").transform;
            socketAttach.SetParent(socketGo.transform, false);
            var socket = socketGo.AddComponent<TypedSocketInteractor>();
            socket.attachTransform = socketAttach;
            Props(socket, ("acceptedType", ConnectorType.GroundPower400Hz), ("socketName", "EXT PWR receptacle"));
            socketGo.AddComponent<StableId>();
            Props(socketGo.GetComponent<StableId>(), ("value", "sandbox.socket.extpower"));

            // Access panel hinged on its left edge, opens outward.
            var panelPivot = new GameObject("Panel hinge").transform;
            panelPivot.SetParent(s, false);
            panelPivot.localPosition = new Vector3(0.23f, 1.2f, face - 0.012f);
            Box("Panel", panelPivot, new Vector3(0.17f, 0f, 0f), new Vector3(0.32f, 0.32f, 0.015f), s_Mat.FuselageTrim, false, false);
            var panelHandle = Box("Panel latch", panelPivot, new Vector3(0.3f, 0f, -0.02f), new Vector3(0.04f, 0.09f, 0.03f), s_Mat.DarkMetal, true, false);
            panelHandle.AddComponent<XRSimpleInteractable>();
            var panel = panelHandle.AddComponent<RotaryHandle>();
            Props(panel, ("handleName", "EXT PWR panel"), ("pivot", panelPivot), ("localAxis", Vector3.up), ("minAngle", 0f), ("maxAngle", 110f), ("detentStep", 20f));

            var interlock = s.gameObject.AddComponent<PanelInterlock>();
            Props(interlock, ("panel", panel), ("socket", socket), ("openThreshold", 0.75f));

            // Status lamp.
            var lamp = Sphere("EXT PWR lamp", s, new Vector3(-0.25f, 1.55f, face - 0.02f), 0.06f, s_Mat.GreenOff);
            var indicator = lamp.AddComponent<IndicatorLight>();
            Props(indicator, ("onMaterial", s_Mat.GreenOn), ("offMaterial", s_Mat.GreenOff));
            UnityEventTools.AddPersistentListener(socket.onConnectionChanged, indicator.SetOn);
            Text3D(s, new Vector3(-0.25f, 1.42f, face - 0.01f), "EXT PWR\nAVAIL", 0.5f, new Vector2(0.4f, 0.15f), Color.black);

            // GPU unit (becomes a drivable/towable unit in Phase 4).
            Box("GPU unit", s, new Vector3(-0.7f, 0.45f, -0.5f), new Vector3(0.9f, 0.9f, 0.7f), s_Mat.Yellow, true, true);
            var anchor = new GameObject("Cable outlet").transform;
            anchor.SetParent(s, false);
            anchor.localPosition = new Vector3(-0.7f, 0.85f, -0.14f);

            // Plug: body along +Z, nose at +Z.
            var plugGo = new GameObject("GPU Plug");
            plugGo.transform.SetParent(s, false);
            plugGo.transform.localPosition = new Vector3(-0.55f, 0.95f, -0.5f);
            var capsule = plugGo.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.radius = 0.045f;
            capsule.height = 0.24f;
            var plugBody = plugGo.AddComponent<Rigidbody>();
            plugBody.mass = 2.5f;
            plugBody.interpolation = RigidbodyInterpolation.Interpolate;
            var body = Cylinder("Body", plugGo.transform, Vector3.zero, new Vector3(0.09f, 0.1f, 0.09f), s_Mat.Orange);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var nose = Cylinder("Nose", plugGo.transform, new Vector3(0f, 0f, 0.11f), new Vector3(0.06f, 0.015f, 0.06f), s_Mat.Black);
            nose.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var plugAttach = new GameObject("Attach").transform;
            plugAttach.SetParent(plugGo.transform, false);
            var cableEnd = new GameObject("Cable end").transform;
            cableEnd.SetParent(plugGo.transform, false);
            cableEnd.localPosition = new Vector3(0f, 0f, -0.12f);

            var plug = plugGo.AddComponent<ConnectorPlug>(); // adds XRGrabInteractable
            plugGo.GetComponent<XRGrabInteractable>().attachTransform = plugAttach;
            Props(plug, ("connectorType", ConnectorType.GroundPower400Hz), ("displayName", "GPU cable"),
                ("tetherOrigin", anchor), ("cableEnd", cableEnd), ("maxCableLength", 4f));
            plugGo.AddComponent<StableId>();
            Props(plugGo.GetComponent<StableId>(), ("value", "sandbox.plug.gpu"));

            var line = anchor.gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = s_Mat.Cable;
            line.widthMultiplier = 0.03f;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            var cable = anchor.gameObject.AddComponent<CableRenderer>();
            Props(cable, ("start", anchor), ("end", cableEnd), ("cableLength", 4f));
        }

        // ------------------------------------------------------------------ wrist tablet

        static void BuildWristTablet(Transform leftController)
        {
            var root = new GameObject("Wrist Tablet").transform;
            root.SetParent(leftController, false);
            // On top of the forearm, just behind the controller; readable when you look at your
            // wrist like a watch. Tweak these two values on the prefab if it sits wrong for you.
            root.localPosition = new Vector3(0f, 0.04f, -0.13f);
            root.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.left);

            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            canvasGo.transform.SetParent(root, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = new Vector2(360f, 220f);
            canvasRect.localScale = Vector3.one * 0.0005f; // 18 x 11 cm
            canvasRect.localPosition = new Vector3(0f, 0.012f, 0f);
            var group = canvasGo.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            var background = UIChild("Background", canvasRect, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            background.color = new Color(0.05f, 0.07f, 0.1f, 0.92f);
            var header = UIText("Header", canvasRect, new Vector2(0f, 0.82f), new Vector2(1f, 1f), 24f, TextAlignmentOptions.Left);
            var body = UIText("Body", canvasRect, new Vector2(0f, 0.18f), new Vector2(1f, 0.82f), 17f, TextAlignmentOptions.TopLeft);
            var footer = UIText("Footer", canvasRect, new Vector2(0f, 0f), new Vector2(1f, 0.18f), 15f, TextAlignmentOptions.Left);

            var buttons = new GameObject("Buttons").transform;
            buttons.SetParent(root, false);
            // Home row; other pages (Ground Crew > Vehicles adds VEHICLES) swap their own row in.
            var home = new GameObject("Home Row").transform;
            home.SetParent(buttons, false);
            var move = TabletButton(home, "MOVE", new Vector3(-0.045f, -0.072f, 0f));
            var turn = TabletButton(home, "TURN", new Vector3(0.045f, -0.072f, 0f));

            var tablet = root.gameObject.AddComponent<WristTablet>();
            Props(tablet, ("group", group), ("headerText", header), ("bodyText", body), ("footerText", footer),
                ("moveButton", move), ("turnButton", turn), ("buttonsRoot", buttons.gameObject));
        }

        internal static PressButton TabletButton(Transform parent, string label, Vector3 position, float width = 0.032f)
        {
            s_Mat = s_Mat ?? new Palette();
            var go = new GameObject(label + " Button");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0f, -0.01f); // sticks out toward the viewer (-Z)
            trigger.size = new Vector3(width + 0.002f, 0.026f, 0.025f);
            var cap = Box("Cap", go.transform, new Vector3(0f, 0f, -0.004f), new Vector3(width, 0.024f, 0.008f), s_Mat.TabletButton, false, false);
            Text3D(go.transform, new Vector3(0f, 0f, -0.0085f), label, 0.09f, new Vector2(width, 0.024f), Color.white);
            var button = go.AddComponent<PressButton>();
            Props(button, ("label", label), ("movingCap", cap.transform), ("pressDirection", Vector3.forward), ("pressDepth", 0.004f));
            return button;
        }

        // ------------------------------------------------------------------ props

        static void Suitcase(string name, Transform parent, Vector3 position, Vector3 size, Material shell, ItemDefinition def, string id, bool dynamicAttach = false)
        {
            var root = ItemRoot(name, parent, position);
            var collider = root.AddComponent<BoxCollider>();
            collider.size = size;
            collider.center = new Vector3(0f, size.y / 2f, 0f);

            Box("Shell", root.transform, collider.center, size, shell, false, false);
            Box("Band", root.transform, collider.center, new Vector3(size.x + 0.004f, 0.04f, size.z + 0.004f), s_Mat.BagTrim, false, false);
            Box("Handle bar", root.transform, new Vector3(0f, size.y + 0.035f, 0f), new Vector3(0.14f, 0.02f, 0.025f), s_Mat.BagTrim, false, false);
            Box("Handle post L", root.transform, new Vector3(-0.06f, size.y + 0.015f, 0f), new Vector3(0.015f, 0.03f, 0.02f), s_Mat.BagTrim, false, false);
            Box("Handle post R", root.transform, new Vector3(0.06f, size.y + 0.015f, 0f), new Vector3(0.015f, 0.03f, 0.02f), s_Mat.BagTrim, false, false);
            Box("Wheel L", root.transform, new Vector3(-size.x / 2f + 0.04f, 0.02f, size.z / 2f - 0.03f), new Vector3(0.04f, 0.04f, 0.04f), s_Mat.Black, false, false);
            Box("Wheel R", root.transform, new Vector3(size.x / 2f - 0.04f, 0.02f, size.z / 2f - 0.03f), new Vector3(0.04f, 0.04f, 0.04f), s_Mat.Black, false, false);

            var attach = new GameObject("Grab Attach").transform;
            attach.SetParent(root.transform, false);
            attach.localPosition = new Vector3(0f, size.y + 0.035f, 0f);

            MakeItem(root, def, id, attach, dynamicAttach);
        }

        static void CardboardBox(string name, Transform parent, Vector3 position, Vector3 size, ItemDefinition def, string id)
        {
            var root = ItemRoot(name, parent, position);
            var collider = root.AddComponent<BoxCollider>();
            collider.size = size;
            collider.center = new Vector3(0f, size.y / 2f, 0f);
            Box("Box", root.transform, collider.center, size, s_Mat.Cardboard, false, false);
            Box("Tape", root.transform, collider.center, new Vector3(0.06f, size.y + 0.003f, size.z + 0.003f), s_Mat.FragileTape, false, false);
            MakeItem(root, def, id, null, true);
        }

        static GameObject Cone(string name, Transform parent, Vector3 position, ItemDefinition def, string id)
        {
            const float baseSize = 0.36f, baseHeight = 0.03f, coneHeight = 0.67f, radius = 0.15f;
            var root = ItemRoot(name, parent, position);
            var baseCollider = root.AddComponent<BoxCollider>();
            baseCollider.size = new Vector3(baseSize, baseHeight, baseSize);
            baseCollider.center = new Vector3(0f, baseHeight / 2f, 0f);
            Box("Base", root.transform, baseCollider.center, baseCollider.size, s_Mat.Black, false, false);

            var coneMesh = ProceduralMeshes.Cone(k_MeshDir, radius, coneHeight, 20);
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, baseHeight, 0f);
            body.AddComponent<MeshFilter>().sharedMesh = coneMesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = s_Mat.Orange;
            var meshCollider = body.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = coneMesh;
            meshCollider.convex = true;

            // Reflective band where the cone radius is ~0.08 m.
            Cylinder("Reflective band", root.transform, new Vector3(0f, 0.33f, 0f), new Vector3(0.165f, 0.03f, 0.165f), s_Mat.Reflective);

            MakeItem(root, def, id, null, true);
            return root;
        }

        static GameObject Chock(string name, Transform parent, Vector3 position, ItemDefinition def, string id)
        {
            var root = ItemRoot(name, parent, position);
            var wedge = ProceduralMeshes.Wedge(k_MeshDir, 0.45f, 0.17f, 0.22f);
            root.AddComponent<MeshFilter>().sharedMesh = wedge;
            root.AddComponent<MeshRenderer>().sharedMaterial = s_Mat.Yellow;
            var meshCollider = root.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = wedge;
            meshCollider.convex = true;
            Box("Rope handle", root.transform, new Vector3(0.24f, 0.12f, 0f), new Vector3(0.04f, 0.015f, 0.12f), s_Mat.Black, false, false);
            MakeItem(root, def, id, null, true);
            return root;
        }

        /// <summary>A cone or chock built at the origin (no stable id) for saving as a prefab.</summary>
        internal static GameObject SafetyItem(bool chock)
        {
            s_Mat = s_Mat ?? new Palette();
            EnsureFolder(k_MeshDir);
            EnsureFolder(k_ItemDir);
            return chock
                ? Chock("Wheel Chock", null, Vector3.zero, Def("Wheel_Chock", "equip.chock", "Wheel Chock", ItemCategory.WheelChock, 6f, impactSpeed: 8f), "")
                : Cone("Safety Cone", null, Vector3.zero, Def("Safety_Cone", "equip.cone", "Safety Cone", ItemCategory.SafetyCone, 3f, impactSpeed: 8f), "");
        }

        static GameObject ItemRoot(string name, Transform parent, Vector3 localPosition)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            return root;
        }

        static void MakeItem(GameObject root, ItemDefinition def, string id, Transform attach, bool dynamicAttach)
        {
            var item = root.AddComponent<GrabbableItem>(); // RequireComponent adds Rigidbody + XRGrabInteractable
            Props(item, ("definition", def));
            var grab = root.GetComponent<XRGrabInteractable>();
            grab.attachTransform = attach;
            grab.useDynamicAttach = dynamicAttach || attach == null;
            var body = root.GetComponent<Rigidbody>();
            body.mass = def.massKg;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            var entity = root.AddComponent<StableId>();
            Props(entity, ("value", id));
        }

        static void Zone(string name, Transform parent, Vector3 center, Vector3 size, ItemCategory accepted, float maxMass, int maxItems, TMP_Text label)
        {
            var go = new GameObject(name + " Zone");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            var trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = size;
            var zone = go.AddComponent<PlacementZone>();
            Props(zone, ("zoneName", name), ("acceptedCategories", accepted), ("maxMassKg", maxMass), ("maxItems", maxItems), ("label", label));
        }

        static ItemDefinition Def(string file, string id, string display, ItemCategory category, float massKg,
            bool twoHands = false, bool fragile = false, float impactSpeed = 4f)
        {
            var path = $"{k_ItemDir}/{file}.asset";
            var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }
            def.itemId = id;
            def.displayName = display;
            def.category = category;
            def.massKg = massKg;
            def.requiresTwoHands = twoHands;
            def.fragile = fragile;
            def.impactDamageSpeed = impactSpeed;
            EditorUtility.SetDirty(def);
            return def;
        }

        // ------------------------------------------------------------------ primitive helpers

        internal static GameObject Box(string name, Transform parent, Vector3 localPosition, Vector3 size, Material material, bool collider, bool isStatic)
        {
            return Primitive(PrimitiveType.Cube, name, parent, localPosition, size, material, collider, isStatic);
        }

        internal static GameObject Cylinder(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
        {
            return Primitive(PrimitiveType.Cylinder, name, parent, localPosition, scale, material, false, false);
        }

        internal static GameObject Sphere(string name, Transform parent, Vector3 localPosition, float diameter, Material material)
        {
            return Primitive(PrimitiveType.Sphere, name, parent, localPosition, Vector3.one * diameter, material, true, false);
        }

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale,
            Material material, bool collider, bool isStatic)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            if (isStatic) MarkStatic(go);
            else renderer.shadowCastingMode = scale.magnitude < 0.1f ? ShadowCastingMode.Off : ShadowCastingMode.On;
            return go;
        }

        internal static void MarkStatic(GameObject go)
        {
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
        }

        internal static TextMeshPro Text3D(Transform parent, Vector3 localPosition, string text, float fontSize, Vector2 box, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = box;
            return tmp;
        }

        internal static RectTransform UIChild(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(10f, 4f);
            rect.offsetMax = new Vector2(-10f, -4f);
            return rect;
        }

        internal static TextMeshProUGUI UIText(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, float size, TextAlignmentOptions alignment)
        {
            var text = UIChild(name, parent, anchorMin, anchorMax).gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        // ------------------------------------------------------------------ editor utilities

        /// <summary>Set serialized fields by name, e.g. Props(c, ("maxAngle", 90f)).</summary>
        internal static void Props(Object target, params (string name, object value)[] values)
        {
            var so = new SerializedObject(target);
            foreach (var entry in values)
            {
                var propertyName = entry.name;
                var value = entry.value;
                var p = so.FindProperty(propertyName);
                if (p == null)
                {
                    Debug.LogError($"[Sandbox] {target.GetType().Name} has no serialized field '{propertyName}'.", target);
                    continue;
                }

                if (value == null) p.objectReferenceValue = null;
                else if (value is Object o) p.objectReferenceValue = o;
                else if (value is float f) p.floatValue = f;
                else if (value is int i) p.intValue = i;
                else if (value is bool b) p.boolValue = b;
                else if (value is string str) p.stringValue = str;
                else if (value is Vector3 v) p.vector3Value = v;
                else if (value is Enum e) p.intValue = Convert.ToInt32(e);
                else Debug.LogError($"[Sandbox] Unsupported value type {value.GetType()} for '{propertyName}'.");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject FindRigPrefab()
        {
            GameObject fallback = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab XR Origin"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) != k_RigPrefabName) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (path.Contains("Starter Assets")) return prefab;
                if (fallback == null) fallback = prefab;
            }
            return fallback;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void AddSceneToBuildSettings(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Generated materials, saved under Assets/GroundCrew/Generated/Materials.</summary>
        internal sealed class Palette
        {
            public readonly Material Concrete = Lit("Concrete", new Color(0.46f, 0.46f, 0.47f), 0.1f);
            public readonly Material Pad = Lit("Pad", new Color(0.36f, 0.37f, 0.39f), 0.1f);
            public readonly Material Yellow = Lit("Yellow", new Color(0.98f, 0.78f, 0.1f), 0.3f);
            public readonly Material Red = Lit("Red", new Color(0.8f, 0.12f, 0.1f), 0.3f);
            public readonly Material White = Lit("White", new Color(0.92f, 0.92f, 0.92f), 0.2f);
            public readonly Material Black = Lit("Black", new Color(0.05f, 0.05f, 0.055f), 0.3f);
            public readonly Material Green = Lit("Green", new Color(0.1f, 0.65f, 0.2f), 0.4f);
            public readonly Material Orange = Lit("Orange", new Color(1f, 0.38f, 0.05f), 0.3f);
            public readonly Material Reflective = Lit("Reflective", new Color(0.95f, 0.95f, 0.95f), 0.8f);
            public readonly Material Rubber = Lit("Rubber", new Color(0.08f, 0.08f, 0.08f), 0.15f);
            public readonly Material Steel = Lit("Steel", new Color(0.7f, 0.72f, 0.75f), 0.6f);
            public readonly Material DarkMetal = Lit("DarkMetal", new Color(0.18f, 0.19f, 0.21f), 0.4f);
            public readonly Material SignBoard = Lit("SignBoard", new Color(0.05f, 0.12f, 0.3f), 0.3f);
            public readonly Material CartBlue = Lit("CartBlue", new Color(0.15f, 0.3f, 0.55f), 0.3f);
            public readonly Material HoldLining = Lit("HoldLining", new Color(0.55f, 0.6f, 0.62f), 0.2f);
            public readonly Material Fuselage = Lit("Fuselage", new Color(0.93f, 0.94f, 0.96f), 0.55f);
            public readonly Material FuselageTrim = Lit("FuselageTrim", new Color(0.8f, 0.82f, 0.85f), 0.5f);
            public readonly Material BagBlack = Lit("BagBlack", new Color(0.1f, 0.1f, 0.12f), 0.5f);
            public readonly Material BagRed = Lit("BagRed", new Color(0.6f, 0.08f, 0.1f), 0.5f);
            public readonly Material BagGreen = Lit("BagGreen", new Color(0.12f, 0.35f, 0.25f), 0.5f);
            public readonly Material BagTrim = Lit("BagTrim", new Color(0.3f, 0.3f, 0.32f), 0.5f);
            public readonly Material Cardboard = Lit("Cardboard", new Color(0.66f, 0.5f, 0.32f), 0.1f);
            public readonly Material FragileTape = Lit("FragileTape", new Color(0.9f, 0.2f, 0.15f), 0.3f);
            public readonly Material TabletButton = Lit("TabletButton", new Color(0.2f, 0.4f, 0.75f), 0.4f);
            public readonly Material AmberOn = Lit("AmberOn", new Color(1f, 0.55f, 0.05f), 0.5f, emission: 3f);
            public readonly Material AmberOff = Lit("AmberOff", new Color(0.35f, 0.2f, 0.05f), 0.5f);
            public readonly Material GreenOn = Lit("GreenOn", new Color(0.2f, 1f, 0.3f), 0.5f, emission: 3f);
            public readonly Material GreenOff = Lit("GreenOff", new Color(0.06f, 0.2f, 0.08f), 0.5f);
            public readonly Material Cable = Unlit("Cable", new Color(0.08f, 0.08f, 0.08f));

            static Material Lit(string name, Color color, float smoothness, float emission = 0f)
            {
                // Simple Lit (Blinn-Phong) is noticeably cheaper than Lit (PBR) on Quest.
                var material = LoadOrCreate(name, "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Lit", "Standard");
                material.color = color;
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
                if (emission > 0f)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", color * emission);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                else
                {
                    material.DisableKeyword("_EMISSION");
                    if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Color.black);
                }
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                return material;
            }

            static Material Unlit(string name, Color color)
            {
                var material = LoadOrCreate(name, "Universal Render Pipeline/Unlit", "Unlit/Color");
                material.color = color;
                EditorUtility.SetDirty(material);
                return material;
            }

            static Material LoadOrCreate(string name, params string[] shaderNames)
            {
                var path = $"{k_MaterialDir}/{name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null) return material;

                Shader shader = null;
                foreach (var shaderName in shaderNames)
                {
                    shader = Shader.Find(shaderName);
                    if (shader != null) break;
                }
                material = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
                AssetDatabase.CreateAsset(material, path);
                return material;
            }
        }
    }
}
