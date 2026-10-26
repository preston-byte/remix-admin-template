using System;
using System.Collections.Generic;
using GroundCrew.Interaction;
using GroundCrew.Vehicles;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Object = UnityEngine.Object;

namespace GroundCrew.EditorTools
{
    /// <summary>
    /// Menu: Ground Crew > Vehicles > Add Vehicles to Sandbox.
    ///
    /// Turns the generated vehicle models (Assets/GroundCrew/Art/Vehicles/*.glb, imported with
    /// glTFast) into working vehicles and parks them around Stand 24:
    ///  - baggage tractor towing three baggage carts (coupled drawbars, turntable steering);
    ///  - pushback tug with its tow bar on the front pin, ready to be latched to the aircraft;
    ///  - fuel truck and catering truck (scissor lift and platform);
    ///  - passenger stairs and a belt loader (both report alignment with the aircraft door),
    ///    a ground power unit, potable-water and lavatory trucks with lifting rear platforms,
    ///    a de-icing truck (slewing, lifting, telescoping boom with a level basket and spray)
    ///    and an airside van.
    /// Each drivable vehicle gets a Rigidbody, convex colliders from the generator's Collision_*
    /// hulls, WheelColliders on the wheel nodes, <see cref="VehicleController"/>, lights, synthesised
    /// sound, a seat with a "GRAB TO DRIVE" handle, a grabbable steering wheel and working dash
    /// buttons. Numbers come from each model's JSON sidecar.
    /// </summary>
    public static class VehicleAssembler
    {
        const string k_ArtDir = "Assets/GroundCrew/Art/Vehicles";
        const string k_RootName = "Ground Vehicles";

#pragma warning disable 0649   // filled by JsonUtility
        [Serializable] class Spec
        {
            public string id, label, engine;
            public float massKg, maxSpeedKmh, maxSteerDeg, motorTorque, brakeTorque, suspension;
            public float[] centerOfMass;
            public WheelSpec[] wheels;
            public TowSpec tow;
            public LiftSpec lift;
            public StairsSpec stairs;
            public BeltSpec belt;
            public BoomSpec boom;
            public PlatformSpec servicePlatform;
        }
        [Serializable] class StairsSpec { public float minFloor, maxFloor, flightLength, speed, extensionTravel; }
        [Serializable] class BeltSpec { public float maxAngle, speed, conveyorSpeed; }
        [Serializable] class BoomSpec { public float swingDeg, minPitch, maxPitch, extension, fluidLitres, sprayLitresPerSecond; }
        [Serializable] class PlatformSpec { public float travel, speed; }
        [Serializable] class WheelSpec { public string name; public float radius, width; public bool steer, drive; }
        [Serializable] class TowSpec { public float maxSpeedKmh, torqueScale; }
        [Serializable] class LiftSpec { public float armLength, stowedFloor, maxFloor, speed, platformTravel; }
#pragma warning restore 0649

        [MenuItem("Ground Crew/Vehicles/Add Vehicles to Sandbox", priority = 30)]
        static void AddVehicles()
        {
            if (!Check("BaggageTractor")) return;
            var previous = GameObject.Find(k_RootName);
            if (previous != null) Undo.DestroyObjectImmediate(previous);
            var root = new GameObject(k_RootName).transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Add vehicles");

            // Baggage train left of the stand, facing the aircraft's tail.
            var tractor = Drivable(Spawn("BaggageTractor", root, new Vector3(-13f, 0f, 4f), 0f), "BaggageTractor", 270f);
            var hitch = Find(tractor.transform, "Hitch_Rear");
            for (var k = 0; k < 3; k++)
                hitch = Cart(root, k + 1, hitch);

            // Pushback tug right of the stand with its bar on the front pin.
            var tug = Drivable(Spawn("PushbackTug", root, new Vector3(10f, 0f, 3f), 0f), "PushbackTug", 450f);
            TowBarOnTug(root, Find(tug.transform, "Hitch_Front"));

            Drivable(Spawn("FuelTruck", root, new Vector3(15f, 0f, 24f), 180f), "FuelTruck", 540f);
            var catering = Drivable(Spawn("CateringTruck", root, new Vector3(-16f, 0f, 26f), 180f), "CateringTruck", 540f);
            CateringSetup(catering);

            // Passenger stairs facing the aircraft's left side (door 1L), belt loader facing its
            // right side (forward hold), service trucks, de-icer and a van parked around the stand.
            StairsSetup(Drivable(Spawn("PassengerStairs", root, new Vector3(13f, 0f, 16f), 270f), "PassengerStairs", 450f));
            BeltLoaderSetup(Drivable(Spawn("BeltLoader", root, new Vector3(-12f, 0f, 17f), 90f), "BeltLoader", 450f));
            GroundPowerSetup(root, new Vector3(5f, 0f, -7f), 0f);
            ServicePlatformSetup(Drivable(Spawn("WaterTruck", root, new Vector3(20f, 0f, 36f), 180f), "WaterTruck", 540f));
            ServicePlatformSetup(Drivable(Spawn("LavatoryTruck", root, new Vector3(-21f, 0f, 40f), 180f), "LavatoryTruck", 540f));
            DeicerSetup(Drivable(Spawn("DeicingTruck", root, new Vector3(27f, 0f, 12f), 180f), "DeicingTruck", 540f));
            Drivable(Spawn("AirsideVan", root, new Vector3(-24f, 0f, 4f), 0f), "AirsideVan", 540f);

            Phase1SandboxBuilder.Text3D(root, new Vector3(-13f, 3f, 9f), "<b>BAGGAGE TRAIN</b>", 3f, new Vector2(6f, 1f), Color.white)
                .transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Selection.activeGameObject = root.gameObject;
            Debug.Log("[Vehicles] Added the ground vehicle fleet around Stand 24. Save the scene to keep it.");
        }

        static bool Check(string id)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{k_ArtDir}/{id}.glb") != null) return true;
            EditorUtility.DisplayDialog("Vehicle models not imported",
                $"Could not load {k_ArtDir}/{id}.glb as a model.\n\nInstall glTFast (Window > Package Manager > + > Add package by name > " +
                "com.unity.cloud.gltfast), reimport the Vehicles folder, and run this menu again.", "OK");
            return false;
        }

        // ------------------------------------------------------------------ spawning

        static Spec LoadSpec(string id)
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>($"{k_ArtDir}/{id}.json");
            return json != null ? JsonUtility.FromJson<Spec>(json.text) : new Spec { id = id, label = id };
        }

        static GameObject Spawn(string id, Transform parent, Vector3 position, float yaw)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{k_ArtDir}/{id}.glb");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return go;
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var hit = Find(c, name);
                if (hit != null) return hit;
            }
            return null;
        }

        static IEnumerable<Transform> FindPrefix(Transform root, string prefix)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(prefix, StringComparison.Ordinal)) yield return t;
        }

        // ------------------------------------------------------------------ common vehicle set-up

        static Rigidbody Body(GameObject go, Spec spec)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(10f, spec.massKg);
            foreach (var hull in FindPrefix(go.transform, "Collision_"))
            {
                var filter = hull.GetComponentInChildren<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var mc = filter.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = filter.sharedMesh;
                mc.convex = true;
                foreach (var r in hull.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }
            return rb;
        }

        static GameObject Drivable(GameObject go, string id, float wheelTurn)
        {
            var spec = LoadSpec(id);
            go.name = spec.label;
            var rb = Body(go, spec);
            var vehicle = go.AddComponent<VehicleController>();
            vehicle.SetWheels(Wheels(go, spec, rb.mass));
            var com = spec.centerOfMass != null && spec.centerOfMass.Length == 3
                ? new Vector3(-spec.centerOfMass[0], spec.centerOfMass[1], spec.centerOfMass[2]) : new Vector3(0f, 0.5f, 0f);
            var hasEngine = spec.engine != "none";
            Phase1SandboxBuilder.Props(vehicle, ("displayName", spec.label), ("hasEngine", hasEngine), ("maxSpeedKmh", spec.maxSpeedKmh),
                ("maxSteerAngle", spec.maxSteerDeg), ("motorTorque", spec.motorTorque), ("brakeTorque", spec.brakeTorque), ("centerOfMass", com));
            if (spec.tow != null && spec.tow.maxSpeedKmh > 0f)
                Phase1SandboxBuilder.Props(vehicle, ("towMaxSpeedKmh", spec.tow.maxSpeedKmh), ("towTorqueScale", spec.tow.torqueScale));

            var lights = go.AddComponent<VehicleLights>();
            Phase1SandboxBuilder.Props(lights, ("vehicle", vehicle));
            go.AddComponent<AudioSource>();
            var vehicleAudio = go.AddComponent<VehicleAudio>();
            var sound = spec.engine == "diesel_big" ? EngineSound.BigDiesel : spec.engine == "electric" ? EngineSound.Electric : EngineSound.Diesel;
            Phase1SandboxBuilder.Props(vehicleAudio, ("vehicle", vehicle), ("engine", sound));

            foreach (var name in new[] { "Hitch_Front", "Hitch_Rear" })
            {
                var h = Find(go.transform, name);
                if (h == null || spec.id == "FuelTruck" || spec.id == "CateringTruck") continue;
                var hitch = h.gameObject.AddComponent<TowHitch>();
                Phase1SandboxBuilder.Props(hitch, ("type", spec.id == "PushbackTug" ? HitchType.Pin : HitchType.Pintle), ("body", rb));
            }

            var eye = Find(go.transform, "DriverEye");
            if (eye != null) DriverSeat(go, vehicle, vehicleAudio, lights, eye, wheelTurn);
            return go;
        }

        static List<WheelBinding> Wheels(GameObject go, Spec spec, float mass)
        {
            var bindings = new List<WheelBinding>();
            if (spec.wheels == null) return bindings;
            var travel = Mathf.Max(0.03f, spec.suspension);
            var sprung = mass / Mathf.Max(1, spec.wheels.Length);
            var k = sprung * Mathf.Pow(2f * Mathf.PI * 1.6f, 2f);        // ~1.6 Hz ride
            foreach (var w in spec.wheels)
            {
                var visual = Find(go.transform, w.name);
                if (visual == null) continue;
                var wc = new GameObject("WC_" + w.name).AddComponent<WheelCollider>();
                wc.transform.SetParent(go.transform, false);
                // Hang the wheel so it sits at the modelled height once the springs settle.
                wc.transform.position = visual.position + go.transform.up * (travel * 0.5f);
                wc.radius = w.radius;
                wc.suspensionDistance = travel;
                wc.mass = Mathf.Clamp(w.radius * 120f, 10f, 120f);
                wc.forceAppPointDistance = w.radius * 0.6f;
                wc.suspensionSpring = new JointSpring { spring = k, damper = 2f * 0.6f * Mathf.Sqrt(k * sprung), targetPosition = 0.5f };
                bindings.Add(new WheelBinding { collider = wc, visual = visual, steer = w.steer, drive = w.drive });
            }
            return bindings;
        }

        static void DriverSeat(GameObject go, VehicleController vehicle, VehicleAudio vehicleAudio, VehicleLights lights, Transform eye, float wheelTurn)
        {
            var mats = Phase1SandboxBuilder.Materials;
            var exit = Find(go.transform, "ExitPoint");

            // Steering wheel: a flat grab box in the wheel's plane.
            SteeringWheelInput steering = null;
            var wheelNode = Find(go.transform, "SteeringWheel");
            var axis = wheelNode != null ? Find(wheelNode, "SteerAxis") : null;
            if (wheelNode != null && axis != null)
            {
                var grip = new GameObject("Grip").transform;
                grip.SetParent(wheelNode, false);
                grip.rotation = Quaternion.LookRotation((axis.position - wheelNode.position).normalized, go.transform.forward);
                var box = grip.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.52f, 0.52f, 0.08f);
                wheelNode.gameObject.AddComponent<XRSimpleInteractable>();
                steering = wheelNode.gameObject.AddComponent<SteeringWheelInput>();
                Phase1SandboxBuilder.Props(steering, ("wheel", wheelNode), ("axisHint", axis), ("maxWheelAngle", wheelTurn));
            }

            // "Grab to drive" handle on the driver's side, at hand height.
            var handle = new GameObject("Enter Handle").transform;
            handle.SetParent(go.transform, false);
            var exitPos = exit != null ? exit.position : go.transform.position + go.transform.right * -1.5f;
            var side = Vector3.ProjectOnPlane(exitPos - eye.position, go.transform.up);
            var handlePos = eye.position + side * 0.62f;
            handlePos.y = Mathf.Min(eye.position.y - 0.35f, go.transform.position.y + 1.45f);
            handle.position = handlePos;
            handle.rotation = go.transform.rotation;
            var bar = Phase1SandboxBuilder.Cylinder("Grab Bar", handle, Vector3.zero, new Vector3(0.04f, 0.14f, 0.04f), mats.Yellow);
            bar.transform.localRotation = Quaternion.identity;
            var handleCollider = handle.gameObject.AddComponent<BoxCollider>();
            handleCollider.size = new Vector3(0.14f, 0.32f, 0.14f);
            var enter = handle.gameObject.AddComponent<XRSimpleInteractable>();
            var label = Phase1SandboxBuilder.Text3D(handle, new Vector3(0f, 0.24f, 0f), "GRAB TO DRIVE", 0.5f, new Vector2(0.6f, 0.1f), Color.white);
            label.transform.rotation = Quaternion.LookRotation(-side.normalized, Vector3.up);

            var seat = go.AddComponent<VehicleSeat>();
            var input = go.AddComponent<VehicleDriverInput>();
            Phase1SandboxBuilder.Props(input, ("vehicle", vehicle), ("steeringWheel", steering), ("seat", seat), ("vehicleAudio", vehicleAudio));
            Phase1SandboxBuilder.Props(seat, ("vehicle", vehicle), ("eyePoint", eye), ("exitPoint", exit), ("driverInput", input), ("enterHandle", enter));

            Button(go, "Btn_Start", "ENGINE", vehicle.ToggleEngine);
            Button(go, "Btn_Drive", "D", vehicle.SetGearDrive);
            Button(go, "Btn_Neutral", "N", vehicle.SetGearNeutral);
            Button(go, "Btn_Reverse", "R", vehicle.SetGearReverse);
            Button(go, "Btn_Park", "PARK", vehicle.ToggleParkingBrake);
            Button(go, "Btn_Lights", "LIGHTS", lights.ToggleHeadlights);
            Button(go, "Btn_Horn", "", vehicleAudio.HonkShort);
        }

        static PressButton Button(GameObject go, string node, string label, UnityAction action)
        {
            var t = Find(go.transform, node);
            if (t == null) return null;
            var outNode = Find(t, "Out");
            var outward = outNode != null ? (outNode.position - t.position).normalized : -go.transform.forward;
            var trigger = t.gameObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.022f;
            trigger.center = t.InverseTransformVector(outward * 0.012f);
            var button = t.gameObject.AddComponent<PressButton>();
            Phase1SandboxBuilder.Props(button, ("label", string.IsNullOrEmpty(label) ? "HORN" : label), ("movingCap", t),
                ("pressDirection", t.InverseTransformDirection(-outward)), ("pressDepth", 0.007f));
            if (action != null) UnityEventTools.AddPersistentListener(button.onPressed, action);
            if (!string.IsNullOrEmpty(label))
            {
                var up = Vector3.ProjectOnPlane(go.transform.up, outward).normalized;
                var text = Phase1SandboxBuilder.Text3D(t, Vector3.zero, label, 0.16f, new Vector2(0.08f, 0.02f), Color.white);
                text.transform.position = t.position + up * 0.032f + outward * 0.002f;
                text.transform.rotation = Quaternion.LookRotation(-outward, up);
            }
            return button;
        }

        // ------------------------------------------------------------------ baggage carts

        static Transform Cart(Transform root, int index, Transform hitch)
        {
            var spec = LoadSpec("BaggageCart");
            var group = new GameObject($"Baggage Cart {index}").transform;
            group.SetParent(root, false);
            var go = Spawn("BaggageCart", group, new Vector3(hitch.position.x, root.position.y, hitch.position.z), hitch.rotation.eulerAngles.y);
            go.name = "Cart";
            var drawbar = Find(go.transform, "Drawbar");
            var eye = Find(drawbar, "DrawbarEye");
            // Line the cart up behind the hitch, drawbar straight.
            var shift = hitch.position - eye.position;
            go.transform.position += new Vector3(shift.x, 0f, shift.z);

            var rb = Body(go, spec);
            var cart = go.AddComponent<VehicleController>();
            cart.SetWheels(Wheels(go, spec, rb.mass));
            Phase1SandboxBuilder.Props(cart, ("displayName", "Baggage cart"), ("hasEngine", false), ("maxSpeedKmh", 25f),
                ("maxSteerAngle", spec.maxSteerDeg), ("motorTorque", 0f), ("brakeTorque", spec.brakeTorque), ("centerOfMass", new Vector3(0f, 0.5f, 0f)));
            var lights = go.AddComponent<VehicleLights>();
            Phase1SandboxBuilder.Props(lights, ("vehicle", cart));
            var rear = Find(go.transform, "Hitch_Rear");
            var rearHitch = rear.gameObject.AddComponent<TowHitch>();
            Phase1SandboxBuilder.Props(rearHitch, ("type", HitchType.Pintle), ("body", rb));

            // The drawbar becomes its own body, jointed to the cart at the turntable.
            Drawbar(group, go, rb, cart, hitch.GetComponent<TowHitch>());
            return rear;
        }

        // ------------------------------------------------------------------ tow bar

        static void TowBarOnTug(Transform root, Transform pin)
        {
            var spec = LoadSpec("TowBar");
            var go = Spawn("TowBar", root, pin.position, pin.rotation.eulerAngles.y);
            go.name = "Tow Bar";
            var eye = Find(go.transform, "Eye");
            go.transform.position += pin.position - eye.position;
            var rb = Body(go, spec);
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var headHandle = Find(go.transform, "TowHeadHandle");
            var handleCollider = new GameObject("Head Handle Grip").AddComponent<BoxCollider>();
            handleCollider.transform.SetParent(headHandle, false);
            handleCollider.size = new Vector3(0.3f, 0.12f, 0.12f);
            var grab = go.AddComponent<XRGrabInteractable>();
            grab.colliders.Clear();
            grab.colliders.Add(handleCollider);
            grab.attachTransform = headHandle;
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.throwOnDetach = false;

            var coupler = go.AddComponent<TowCoupler>();
            Phase1SandboxBuilder.Props(coupler, ("body", rb), ("eye", eye), ("accepts", HitchType.Pin), ("startCoupledTo", pin.GetComponent<TowHitch>()));
            var bar = go.AddComponent<TowBar>();
            Phase1SandboxBuilder.Props(bar, ("body", rb), ("eyeCoupler", coupler), ("head", Find(go.transform, "TowHead")), ("headHandle", grab));
            var label = Phase1SandboxBuilder.Text3D(headHandle, new Vector3(0f, 0.25f, 0f), "TOW BAR HEAD\n<size=60%>grip, carry to the nose gear, let go</size>", 0.6f, new Vector2(1.2f, 0.3f), Color.white);
            label.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        }

        // ------------------------------------------------------------------ catering truck

        static void CateringSetup(GameObject go)
        {
            var spec = LoadSpec("CateringTruck");
            var vehicle = go.GetComponent<VehicleController>();
            var box = Find(go.transform, "LiftBox");
            var platform = Find(go.transform, "Platform");
            var lift = go.AddComponent<CateringLift>();
            var l = spec.lift ?? new LiftSpec { armLength = 4.6f, stowedFloor = 1.38f, maxFloor = 5.6f, speed = 0.12f, platformTravel = 0.9f };
            Phase1SandboxBuilder.Props(lift, ("vehicle", vehicle), ("liftBox", box), ("platform", platform),
                ("upButton", Button(go, "Btn_LiftUp", "LIFT UP", null)), ("downButton", Button(go, "Btn_LiftDown", "LIFT DOWN", null)),
                ("platformOutButton", Button(go, "Btn_PlatformOut", "PLATFORM OUT", null)), ("platformInButton", Button(go, "Btn_PlatformIn", "PLATFORM IN", null)),
                ("armAL", Find(go.transform, "ScissorA_L")), ("armBL", Find(go.transform, "ScissorB_L")),
                ("armAR", Find(go.transform, "ScissorA_R")), ("armBR", Find(go.transform, "ScissorB_R")),
                ("armLength", l.armLength), ("maxFloor", l.maxFloor), ("stowedFloor", l.stowedFloor), ("liftSpeed", l.speed), ("platformTravel", l.platformTravel));

            // Solid box and platform floor; a ride volume so the player goes up with the platform.
            AddBoundsCollider(box, false);
            if (platform != null)
            {
                AddBoundsCollider(platform, false);
                var ride = new GameObject("Ride Volume").AddComponent<BoxCollider>();
                ride.transform.SetParent(platform, false);
                var b = LocalBounds(platform);
                ride.center = new Vector3(b.center.x, b.min.y + 1.1f, b.center.z);
                ride.size = new Vector3(b.size.x, 2.2f, b.size.z);
                ride.isTrigger = true;
                ride.enabled = false;   // only its shape is used
                var rider = platform.gameObject.AddComponent<RideablePlatform>();
                Phase1SandboxBuilder.Props(rider, ("volume", ride), ("carrier", platform));
                Docking(platform.gameObject, "Catering truck", Find(platform, "PlatformEdge"), "SP_Sill_Door_", 0.05f, 0.20f, 0.30f, 0.40f);
            }
        }

        static Bounds LocalBounds(Transform t)
        {
            var filter = t.GetComponent<MeshFilter>();
            if (filter == null) filter = t.GetComponentInChildren<MeshFilter>();
            return filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.5f);
        }

        static void AddBoundsCollider(Transform t, bool trigger)
        {
            if (t == null) return;
            var b = LocalBounds(t);
            var c = t.gameObject.AddComponent<BoxCollider>();
            c.center = b.center;
            c.size = b.size;
            c.isTrigger = trigger;
        }

        // ------------------------------------------------------------------ shared helpers for equipment

        static void SetArray(Object target, string property, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(property);
            p.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A BoxCollider on its own child, centred and sized in the parent's local space.</summary>
        static BoxCollider LocalBox(Transform parent, string name, Vector3 centre, Vector3 size, bool trigger = false)
        {
            var c = new GameObject(name).AddComponent<BoxCollider>();
            c.transform.SetParent(parent, false);
            c.center = centre;
            c.size = size;
            c.isTrigger = trigger;
            return c;
        }

        static void Rideable(Transform carrier, Vector3 floorCentre, Vector3 floorSize)
        {
            var ride = LocalBox(carrier, "Ride Volume", floorCentre + Vector3.up * 1.1f, new Vector3(floorSize.x, 2.2f, floorSize.z), true);
            ride.enabled = false;   // only its shape is used
            var rider = carrier.gameObject.AddComponent<RideablePlatform>();
            Phase1SandboxBuilder.Props(rider, ("volume", ride), ("carrier", carrier));
        }

        static DoorDockingSensor Docking(GameObject host, string name, Transform edge, string prefix, float maxAbove, float maxBelow, float maxGap, float maxLateral)
        {
            if (edge == null) return null;
            var sensor = host.AddComponent<DoorDockingSensor>();
            var lamp = FindPrefix(host.transform, "Light_Dock_");
            GameObject lampGo = null;
            foreach (var l in lamp) { lampGo = l.gameObject; break; }
            Phase1SandboxBuilder.Props(sensor, ("equipmentName", name), ("edge", edge), ("sillPrefix", prefix), ("maxAbove", maxAbove),
                ("maxBelow", maxBelow), ("maxGap", maxGap), ("maxLateral", maxLateral), ("alignedLamp", lampGo));
            return sensor;
        }

        static HydraulicAxis Axis(GameObject vehicleGo, string name, Transform target, string hintName, AxisMotion motion, float min, float max,
            float speed, string plusButton, string plusLabel, string minusButton, string minusLabel, bool inhibitDrive)
        {
            if (target == null) return null;
            var axis = target.gameObject.AddComponent<HydraulicAxis>();
            Phase1SandboxBuilder.Props(axis, ("functionName", name), ("target", target), ("axisHint", Find(target, hintName)), ("motion", motion),
                ("min", min), ("max", max), ("speed", speed), ("plusButton", Button(vehicleGo, plusButton, plusLabel, null)),
                ("minusButton", Button(vehicleGo, minusButton, minusLabel, null)), ("vehicle", vehicleGo.GetComponent<VehicleController>()),
                ("inhibitDriveWhenDeployed", inhibitDrive));
            return axis;
        }

        // ------------------------------------------------------------------ passenger stairs

        static void StairsSetup(GameObject go)
        {
            var spec = LoadSpec("PassengerStairs").stairs ?? new StairsSpec { minFloor = 2.2f, maxFloor = 4.1f, flightLength = 6f, speed = 0.1f, extensionTravel = 0.6f };
            var platform = Find(go.transform, "StairPlatform");
            var extension = Find(go.transform, "PlatformExtension");
            var flight = Find(go.transform, "StairFlight");
            var bottom = Find(go.transform, "FlightBottom");
            var stairs = go.AddComponent<StairsMechanism>();
            Phase1SandboxBuilder.Props(stairs, ("vehicle", go.GetComponent<VehicleController>()), ("platform", platform), ("extension", extension),
                ("flight", flight), ("flightBottom", bottom), ("minFloor", spec.minFloor), ("maxFloor", spec.maxFloor),
                ("flightLength", spec.flightLength), ("speed", spec.speed), ("extensionTravel", spec.extensionTravel));
            SetArray(stairs, "upButtons", new Object[] { Button(go, "Btn_StairsUp", "UP", null), Button(go, "Btn_StairsUp_Dash", "STAIRS UP", null) });
            SetArray(stairs, "downButtons", new Object[] { Button(go, "Btn_StairsDown", "DOWN", null), Button(go, "Btn_StairsDown_Dash", "STAIRS DN", null) });
            SetArray(stairs, "outButtons", new Object[] { Button(go, "Btn_StairsOut", "PLATE OUT", null), Button(go, "Btn_StairsOut_Dash", "PLATE OUT", null) });
            SetArray(stairs, "inButtons", new Object[] { Button(go, "Btn_StairsIn", "PLATE IN", null), Button(go, "Btn_StairsIn_Dash", "PLATE IN", null) });

            // Platform floor, bridge plate and a walking slope along the flight (it tilts with the flight).
            LocalBox(platform, "Floor Collider", new Vector3(0f, -0.04f, 0.80f), new Vector3(1.5f, 0.08f, 1.6f));
            LocalBox(extension, "Plate Collider", new Vector3(0f, -0.10f, -0.30f), new Vector3(1.3f, 0.04f, 0.6f));
            if (flight != null && bottom != null)
            {
                var slope = new GameObject("Walk Surface").AddComponent<BoxCollider>();
                slope.transform.SetParent(flight, false);
                var up = flight.position - bottom.position;
                // Runs 1 m past the foot so it meets the ground: no tall first step at any height.
                slope.transform.SetPositionAndRotation(Vector3.Lerp(bottom.position, flight.position, 0.5f) - up.normalized * 0.5f - Vector3.up * 0.04f,
                    Quaternion.LookRotation(up.normalized, Vector3.up));
                slope.size = new Vector3(1.2f, 0.05f, up.magnitude + 1f);
            }
            Rideable(platform, new Vector3(0f, 0f, 0.8f), new Vector3(1.5f, 0f, 1.6f));
            Docking(platform.gameObject, "Passenger stairs", Find(go.transform, "PlatformEdge"), "SP_Sill_Door_", 0.03f, 0.15f, 0.25f, 0.35f);
        }

        // ------------------------------------------------------------------ belt loader

        static void BeltLoaderSetup(GameObject go)
        {
            var spec = LoadSpec("BeltLoader").belt ?? new BeltSpec { maxAngle = 23f, speed = 6f, conveyorSpeed = 0.55f };
            var belt = Find(go.transform, "Belt");
            if (belt == null) return;
            Axis(go, "Belt lift", belt, "BeltAxis", AxisMotion.Rotate, 0f, spec.maxAngle, spec.speed, "Btn_BeltUp", "BELT UP", "Btn_BeltDown", "BELT DN", false);
            // Belt surface and side guards (local: forward along the belt from the rear hinge).
            LocalBox(belt, "Belt Surface", new Vector3(0f, 0.18f, 3.8f), new Vector3(0.78f, 0.04f, 7.6f));
            LocalBox(belt, "Guard L", new Vector3(-0.43f, 0.33f, 3.8f), new Vector3(0.07f, 0.30f, 7.6f));
            LocalBox(belt, "Guard R", new Vector3(0.43f, 0.33f, 3.8f), new Vector3(0.07f, 0.30f, 7.6f));
            var volume = LocalBox(belt, "Conveyor Volume", new Vector3(0f, 0.45f, 3.8f), new Vector3(0.78f, 0.5f, 7.6f), true);
            var conveyor = volume.gameObject.AddComponent<ConveyorBelt>();
            Phase1SandboxBuilder.Props(conveyor, ("belt", belt), ("speed", spec.conveyorSpeed), ("vehicle", go.GetComponent<VehicleController>()),
                ("inButton", Button(go, "Btn_ConveyorIn", "BELT IN", null)), ("outButton", Button(go, "Btn_ConveyorOut", "BELT OUT", null)),
                ("stopButton", Button(go, "Btn_ConveyorStop", "STOP", null)));
            Docking(belt.gameObject, "Belt loader", Find(belt, "BeltEnd"), "SP_Sill_CargoDoor_", 0.10f, 0.20f, 0.35f, 0.50f);
        }

        // ------------------------------------------------------------------ ground power unit

        static void GroundPowerSetup(Transform root, Vector3 position, float yaw)
        {
            var spec = LoadSpec("GroundPowerUnit");
            var group = new GameObject("Ground Power Unit").transform;
            group.SetParent(root, false);
            var go = Spawn("GroundPowerUnit", group, position, yaw);
            go.name = "GPU";
            var rb = Body(go, spec);
            var unit = go.AddComponent<VehicleController>();
            unit.SetWheels(Wheels(go, spec, rb.mass));
            Phase1SandboxBuilder.Props(unit, ("displayName", spec.label), ("hasEngine", true), ("maxSpeedKmh", 25f),
                ("maxSteerAngle", spec.maxSteerDeg), ("motorTorque", 0f), ("brakeTorque", spec.brakeTorque), ("centerOfMass", new Vector3(0f, 0.7f, 0f)));
            go.AddComponent<AudioSource>();
            var sound = go.AddComponent<VehicleAudio>();
            Phase1SandboxBuilder.Props(sound, ("vehicle", unit), ("engine", EngineSound.Diesel), ("reverseAlarm", false));
            var gpu = go.AddComponent<GroundPowerUnit>();
            Transform run = null, output = null;
            foreach (var t in FindPrefix(go.transform, "Light_GpuRun_")) run = t;
            foreach (var t in FindPrefix(go.transform, "Light_GpuOut_")) output = t;
            Phase1SandboxBuilder.Props(gpu, ("unit", unit), ("engineButton", Button(go, "Btn_GpuEngine", "ENGINE", null)),
                ("outputButton", Button(go, "Btn_GpuOutput", "OUTPUT", null)), ("runLamp", run != null ? run.gameObject : null),
                ("outputLamp", output != null ? output.gameObject : null));
            Drawbar(group, go, rb, unit, null);
        }

        /// <summary>Turntable drawbar as its own body, jointed to the trailer; optionally coupled at start.</summary>
        static TowCoupler Drawbar(Transform group, GameObject trailer, Rigidbody rb, VehicleController controller, TowHitch startCoupledTo)
        {
            var drawbar = Find(trailer.transform, "Drawbar");
            var eye = Find(drawbar, "DrawbarEye");
            drawbar.SetParent(group, true);
            var barBody = drawbar.gameObject.AddComponent<Rigidbody>();
            barBody.mass = 35f;
            barBody.interpolation = RigidbodyInterpolation.Interpolate;
            var capsule = drawbar.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.radius = 0.06f;
            capsule.height = Vector3.Distance(drawbar.position, eye.position);
            capsule.center = drawbar.InverseTransformPoint(Vector3.Lerp(drawbar.position, eye.position, 0.5f));
            var pivot = drawbar.gameObject.AddComponent<ConfigurableJoint>();
            pivot.connectedBody = rb;
            pivot.anchor = Vector3.zero;
            pivot.axis = Vector3.right;
            pivot.secondaryAxis = Vector3.up;
            pivot.xMotion = pivot.yMotion = pivot.zMotion = ConfigurableJointMotion.Locked;
            pivot.angularXMotion = ConfigurableJointMotion.Limited;
            pivot.angularYMotion = ConfigurableJointMotion.Limited;
            pivot.angularZMotion = ConfigurableJointMotion.Locked;
            pivot.lowAngularXLimit = new SoftJointLimit { limit = -80f };
            pivot.highAngularXLimit = new SoftJointLimit { limit = 8f };
            pivot.angularYLimit = new SoftJointLimit { limit = 80f };
            var grab = drawbar.gameObject.AddComponent<XRGrabInteractable>();
            grab.attachTransform = Find(drawbar, "DrawbarHandle");
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.throwOnDetach = false;
            var coupler = drawbar.gameObject.AddComponent<TowCoupler>();
            Phase1SandboxBuilder.Props(coupler, ("body", barBody), ("eye", eye), ("accepts", HitchType.Pintle), ("handle", grab),
                ("startCoupledTo", startCoupledTo), ("pivotJoint", pivot));
            var steering = trailer.AddComponent<TrailerSteering>();
            Phase1SandboxBuilder.Props(steering, ("trailer", controller), ("drawbar", drawbar), ("coupler", coupler));
            return coupler;
        }

        // ------------------------------------------------------------------ water / lavatory trucks

        static void ServicePlatformSetup(GameObject go)
        {
            var platform = Find(go.transform, "ServicePlatform");
            if (platform == null) return;
            Axis(go, "Service platform", platform, "LiftAxis", AxisMotion.Translate, 0f, 1.5f, 0.15f, "Btn_PlatformUp", "UP", "Btn_PlatformDown", "DOWN", true);
            LocalBox(platform, "Floor Collider", new Vector3(0f, -0.03f, 0f), new Vector3(1.6f, 0.06f, 0.9f));
            Rideable(platform, Vector3.zero, new Vector3(1.6f, 0f, 0.9f));
        }

        // ------------------------------------------------------------------ de-icing truck

        static void DeicerSetup(GameObject go)
        {
            var spec = LoadSpec("DeicingTruck").boom ?? new BoomSpec { swingDeg = 150f, minPitch = -18f, maxPitch = 70f, extension = 4f, fluidLitres = 6000f, sprayLitresPerSecond = 2.5f };
            var turret = Find(go.transform, "BoomTurret");
            var boom = Find(go.transform, "Boom");
            var ext = Find(go.transform, "BoomExt");
            var basket = Find(go.transform, "Basket");
            Axis(go, "Slew", turret, "TurretAxis", AxisMotion.Rotate, -spec.swingDeg, spec.swingDeg, 12f, "Btn_SwingPlus", "SWING +", "Btn_SwingMinus", "SWING -", true);
            Axis(go, "Boom", boom, "BoomAxis", AxisMotion.Rotate, spec.minPitch, spec.maxPitch, 6f, "Btn_BoomUp", "BOOM UP", "Btn_BoomDown", "BOOM DN", true);
            Axis(go, "Telescope", ext, "ExtAxis", AxisMotion.Translate, 0f, spec.extension, 0.25f, "Btn_BoomOut", "OUT", "Btn_BoomIn", "IN", true);
            if (basket == null) return;
            var leveller = go.AddComponent<BasketLeveller>();
            Phase1SandboxBuilder.Props(leveller, ("basket", basket), ("reference", turret));
            LocalBox(basket, "Floor Collider", new Vector3(0f, -1.15f, -0.55f), new Vector3(1.1f, 0.06f, 1.0f));
            Rideable(basket, new Vector3(0f, -1.15f, -0.55f), new Vector3(1.1f, 0f, 1.0f));

            var nozzle = Find(basket, "Nozzle");
            var dir = Find(basket, "NozzleDir");
            if (nozzle == null || dir == null) return;
            var sprayGo = new GameObject("Spray");
            sprayGo.transform.SetParent(nozzle, false);
            sprayGo.transform.rotation = Quaternion.LookRotation((dir.position - nozzle.position).normalized, Vector3.up);
            var ps = sprayGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startSpeed = 14f;
            main.startLifetime = 1.3f;
            main.startSize = 0.07f;
            main.gravityModifier = 1f;
            main.maxParticles = 1200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(1f, 0.55f, 0.1f, 0.7f);   // Type I fluid is dyed orange
            var emission = ps.emission;
            emission.rateOverTime = 350f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 5f;
            shape.radius = 0.01f;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null) sprayGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = new Material(shader) { color = new Color(1f, 0.55f, 0.1f, 0.7f) };
            ps.Stop();
            var nozzleComp = sprayGo.AddComponent<SprayNozzle>();
            Phase1SandboxBuilder.Props(nozzleComp, ("spray", ps), ("toggleButton", Button(go, "Btn_Spray", "SPRAY", null)),
                ("vehicle", go.GetComponent<VehicleController>()), ("litres", spec.fluidLitres), ("litresPerSecond", spec.sprayLitresPerSecond));
        }
    }
}
