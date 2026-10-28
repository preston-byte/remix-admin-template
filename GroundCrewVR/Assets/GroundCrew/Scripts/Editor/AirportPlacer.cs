using System;
using System.Collections.Generic;
using GroundCrew.Aircraft;
using GroundCrew.Airport;
using GroundCrew.Player;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using Object = UnityEngine.Object;

namespace GroundCrew.EditorTools
{
    /// <summary>
    /// Menu: Ground Crew > Airport > Build Airport Around Sandbox.
    ///
    /// Places the generated airport (Assets/GroundCrew/Art/Airport/Airport.glb, from
    /// Tools/AirportGenerator) around the training stand, which becomes Stand 24 of a remote apron:
    ///  - swaps the glTF materials for URP Simple Lit ones (cheaper on Quest) using the tiling
    ///    textures and the colours in Airport.json; ground textures get anisotropic filtering;
    ///  - adds mesh colliders to the ground and buildings, teleport areas on all paved / grass
    ///    ground, and marks everything static for batching;
    ///  - hides the sandbox's own 100 m apron plane and stand markings (the airport has its own);
    ///  - pushes the camera's far clip out to 3 km with light distance fog;
    ///  - parks aircraft at a few gates and remote stands (Ground Crew > Airport > Park Aircraft).
    /// Run it after Build Phase 1 Sandbox (and Place Aircraft / Add Vehicles in any order).
    /// </summary>
    public static class AirportPlacer
    {
        const string k_ArtDir = "Assets/GroundCrew/Art/Airport";
        const string k_AircraftDir = "Assets/GroundCrew/Art/Aircraft";
        const string k_MaterialDir = "Assets/GroundCrew/Generated/Airport";
        const string k_RootName = "Airport";
        const string k_ParkedName = "Parked Aircraft";

        // Gates / stands that get a parked aircraft (Stand 24 is the training stand).
        static readonly string[,] k_Parked =
        {
            { "Gate_2", "Narrowbody320Class" }, { "Gate_4", "Narrowbody737Class" }, { "Gate_5", "Narrowbody320Class" }, { "Stand_22", "Narrowbody737Class" },
        };

#pragma warning disable 0649   // filled by JsonUtility
        [Serializable] class Sidecar
        {
            public QuestMaterials.Spec[] materials;
            public SpawnPoint[] spawnPoints;
            public string[] groundNodes, solidNodes;
        }
        [Serializable] class SpawnPoint { public string name; public float[] position; public float yaw; }
#pragma warning restore 0649

        [MenuItem("Ground Crew/Airport/Build Airport Around Sandbox", priority = 40)]
        static void BuildAirport()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{k_ArtDir}/Airport.glb");
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>($"{k_ArtDir}/Airport.json");
            if (model == null || json == null)
            {
                EditorUtility.DisplayDialog("Airport model not imported",
                    $"Could not load {k_ArtDir}/Airport.glb as a model.\n\nInstall glTFast (Window > Package Manager > + > Add package by name > " +
                    "com.unity.cloud.gltfast), reimport the Airport folder, and run this menu again.", "OK");
                return;
            }
            var spec = JsonUtility.FromJson<Sidecar>(json.text);

            var previous = GameObject.Find(k_RootName);
            if (previous != null) Undo.DestroyObjectImmediate(previous);
            var airport = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(airport, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Undo.RegisterCreatedObjectUndo(airport, "Build airport");
            airport.name = k_RootName;
            airport.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var swapped = QuestMaterials.Swap(airport, spec.materials, k_MaterialDir, k_ArtDir);
            var colliders = 0;
            var teleportMask = InteractionLayerMask.GetMask("Teleport");
            // The ground is flat: one solid box under the whole airport (top at y = 0) is the most
            // reliable floor there is - no mesh-collider cooking of huge triangles, works in every build.
            var groundBounds = new Bounds(airport.transform.position, Vector3.zero);
            var anyGround = false;
            foreach (var name in spec.groundNodes ?? new string[0])
            {
                var node = Find(airport.transform, name);
                if (node == null) continue;
                foreach (var r in node.GetComponentsInChildren<Renderer>(true))
                {
                    if (!anyGround) { groundBounds = r.bounds; anyGround = true; }
                    else groundBounds.Encapsulate(r.bounds);
                }
            }
            if (!anyGround) groundBounds = new Bounds(new Vector3(0f, 0f, 135f), new Vector3(3500f, 0f, 930f));
            var floor = new GameObject("Airport Ground Collider");
            floor.transform.SetParent(airport.transform, false);
            var floorBox = floor.AddComponent<BoxCollider>();
            floorBox.center = new Vector3(groundBounds.center.x, -1f, groundBounds.center.z);
            floorBox.size = new Vector3(groundBounds.size.x + 20f, 2f, groundBounds.size.z + 20f);
            var floorArea = floor.AddComponent<TeleportationArea>();
            if (teleportMask != 0) floorArea.interactionLayers = teleportMask;
            colliders++;
            foreach (var name in spec.solidNodes ?? new string[0])
            {
                var node = Find(airport.transform, name);
                if (node != null) colliders += AddMeshColliders(node);
            }
            foreach (var t in airport.GetComponentsInChildren<Transform>(true))
                Phase1SandboxBuilder.MarkStatic(t.gameObject);

            // The sandbox's own ground and stand markings would overlap the airport's.
            foreach (var name in new[] { "Apron", "Stand Markings" })
            {
                var go = GameObject.Find(name);
                if (go == null || go.transform.IsChildOf(airport.transform)) continue;
                Undo.RecordObject(go, "Build airport");
                go.SetActive(false);
            }

            var origin = Object.FindAnyObjectByType<XROrigin>();
            if (origin != null && origin.GetComponent<FallSafety>() == null) Undo.AddComponent<FallSafety>(origin.gameObject);
            if (origin != null && origin.Camera != null)
            {
                Undo.RecordObject(origin.Camera, "Build airport");
                origin.Camera.farClipPlane = 3000f;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.66f, 0.73f, 0.82f);
            RenderSettings.fogStartDistance = 500f;
            RenderSettings.fogEndDistance = 2800f;

            var parked = ParkAircraft(spec);
            var bridges = BuildJetBridges(airport.transform, spec);
            BuildStandRegistry(airport, spec, bridges);
            OperationsSetup.Install(false);
            EditorSceneManager.MarkSceneDirty(airport.scene);
            Selection.activeGameObject = airport;
            Debug.Log($"[Airport] Built the airport: {swapped} materials converted, {colliders} colliders, {parked} parked aircraft, {bridges.Count} jet bridges. " +
                      "Save the scene to keep it.");
        }

        [MenuItem("Ground Crew/Airport/Park Aircraft at Gates", priority = 41)]
        static void ParkAircraftMenu()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>($"{k_ArtDir}/Airport.json");
            if (json == null) return;
            var n = ParkAircraft(JsonUtility.FromJson<Sidecar>(json.text));
            Debug.Log($"[Airport] Parked {n} aircraft.");
        }

        [MenuItem("Ground Crew/Airport/Remove Parked Aircraft (faster)", priority = 42)]
        static void RemoveParked()
        {
            var previous = GameObject.Find(k_ParkedName);
            if (previous != null) Undo.DestroyObjectImmediate(previous);
        }

        // ------------------------------------------------------------------ jet bridges

        // Tunnel sections and cab, in each node's local space (see Tools/AirportGenerator JetBridge model):
        // name, floor centre z, length, width, wall height.
        static readonly (string node, float z, float length, float width, float height)[] k_BridgeFloors =
        {
            ("Tunnel", 6.5f, 9.0f, 2.9f, 3.0f), ("TunnelB", 4.0f, 8.0f, 2.75f, 2.85f), ("TunnelC", 3.75f, 7.5f, 2.6f, 2.7f), ("Cab", 1.15f, 2.9f, 3.4f, 2.8f),
        };

        static List<JetBridge> BuildJetBridges(Transform airport, Sidecar spec)
        {
            var list = new List<JetBridge>();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{k_ArtDir}/JetBridge.glb");
            if (model == null || spec.spawnPoints == null) return list;
            var root = new GameObject("Jet Bridges").transform;
            root.SetParent(airport, false);
            var teleportMask = InteractionLayerMask.GetMask("Teleport");
            foreach (var p in spec.spawnPoints)
            {
                if (!p.name.StartsWith("Bridge_", StringComparison.Ordinal) || p.position == null || p.position.Length < 3) continue;
                var gate = p.name.Substring("Bridge_".Length);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = "Jet Bridge " + gate;
                go.transform.SetPositionAndRotation(new Vector3(p.position[0], p.position[1], p.position[2]), Quaternion.identity);
                QuestMaterials.Swap(go, spec.materials, k_MaterialDir, k_ArtDir);

                var floors = new List<Collider>();
                foreach (var f in k_BridgeFloors)
                {
                    var t = Find(go.transform, f.node);
                    if (t == null) continue;
                    float z = f.z, length = f.length, width = f.width, height = f.height;
                    floors.Add(Box(t, "Floor", new Vector3(0f, -0.1f, z), new Vector3(width - 0.1f, 0.2f, length)));
                    Box(t, "Wall L", new Vector3(-width / 2f + 0.04f, height / 2f, z), new Vector3(0.08f, height, length));
                    Box(t, "Wall R", new Vector3(width / 2f - 0.04f, height / 2f, z), new Vector3(0.08f, height, length));
                }
                var cab = Find(go.transform, "Cab");
                var barrier = cab != null ? Box(cab, "Doorway Barrier", new Vector3(0f, 1.1f, 2.62f), new Vector3(2.6f, 2.2f, 0.08f)) : null;

                var stop = SpawnPosition(spec, "Gate_" + gate);
                var bridge = go.AddComponent<JetBridge>();
                Phase1SandboxBuilder.Props(bridge, ("gateName", "Gate " + gate), ("aircraftStop", stop),
                    ("slew", Find(go.transform, "Slew")), ("tunnel", Find(go.transform, "Tunnel")), ("tunnelB", Find(go.transform, "TunnelB")),
                    ("tunnelC", Find(go.transform, "TunnelC")), ("cabMount", Find(go.transform, "CabMount")), ("cab", cab),
                    ("canopy", Find(go.transform, "Canopy")), ("cabFront", Find(go.transform, "CabFront")),
                    ("columnMount", Find(go.transform, "ColumnMount")), ("columnLegs", Find(go.transform, "ColumnLegs")),
                    ("bogie", Find(go.transform, "Bogie")), ("wheelL", Find(go.transform, "Wheel_L")), ("wheelR", Find(go.transform, "Wheel_R")),
                    ("frontBarrier", barrier), ("stowYaw", p.yaw));
                VehicleAssembler.Button(go, "Btn_BridgeDock", "DOCK", bridge.Dock);
                VehicleAssembler.Button(go, "Btn_BridgeRetract", "RETRACT", bridge.Retract);

                // Walk on it / teleport onto it: floors only, not the walls.
                var area = go.AddComponent<TeleportationArea>();
                area.colliders.Clear();
                area.colliders.AddRange(floors);
                if (teleportMask != 0) area.interactionLayers = teleportMask;

                // Floors of the static fixed link and rotunda (in the airport model).
                var fixedFloor = Box(root, $"Gate {gate} link floor", new Vector3(p.position[0], p.position[1] - 0.25f, -149.25f), new Vector3(2.5f, 0.2f, 2.8f));
                var rotundaFloor = Box(root, $"Gate {gate} rotunda floor", new Vector3(p.position[0], p.position[1] - 0.25f, p.position[2]), new Vector3(4.0f, 0.2f, 4.0f));
                Box(root, $"Gate {gate} link wall L", new Vector3(p.position[0] - 1.25f, p.position[1] + 1.3f, -149.25f), new Vector3(0.1f, 2.8f, 2.8f));
                Box(root, $"Gate {gate} link wall R", new Vector3(p.position[0] + 1.25f, p.position[1] + 1.3f, -149.25f), new Vector3(0.1f, 2.8f, 2.8f));
                var fixedArea = fixedFloor.gameObject.AddComponent<TeleportationArea>();
                if (teleportMask != 0) fixedArea.interactionLayers = teleportMask;
                var rotundaArea = rotundaFloor.gameObject.AddComponent<TeleportationArea>();
                if (teleportMask != 0) rotundaArea.interactionLayers = teleportMask;
                list.Add(bridge);
            }
            return list;
        }

        static BoxCollider Box(Transform parent, string name, Vector3 centre, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<BoxCollider>();
            c.center = centre;
            c.size = size;
            return c;
        }

        static Vector3 SpawnPosition(Sidecar spec, string name)
        {
            foreach (var p in spec.spawnPoints ?? new SpawnPoint[0])
                if (p.name == name && p.position != null && p.position.Length >= 3) return new Vector3(p.position[0], p.position[1], p.position[2]);
            return Vector3.zero;
        }

        /// <summary>The stands and gates, for the Ops iPad and the AI crews.</summary>
        static void BuildStandRegistry(GameObject airport, Sidecar spec, List<JetBridge> bridges)
        {
            var stands = new List<StandInfo>();
            foreach (var p in spec.spawnPoints ?? new SpawnPoint[0])
            {
                if (p.position == null || p.position.Length < 3) continue;
                var pos = new Vector3(p.position[0], p.position[1], p.position[2]);
                if (p.name.StartsWith("Stand_", StringComparison.Ordinal))
                {
                    stands.Add(new StandInfo
                    {
                        name = "Stand " + p.name.Substring(6), stopPosition = pos, yaw = p.yaw,
                        staging = pos + new Vector3(11f, 0f, -36f), crewEntry = pos + new Vector3(-11f, 0f, -36f),
                        equipmentPark = pos + new Vector3(11f, 0f, -43f), beltPark = pos + new Vector3(-11f, 0f, -43f),
                    });
                }
                else if (p.name.StartsWith("Gate_", StringComparison.Ordinal))
                {
                    var gate = "Gate " + p.name.Substring(5);
                    JetBridge bridge = null;
                    foreach (var b in bridges) if (b.GateName == gate) bridge = b;
                    stands.Add(new StandInfo
                    {
                        name = gate, stopPosition = pos, yaw = p.yaw, bridge = bridge,
                        staging = pos + new Vector3(-15f, 0f, -9.5f),
                        crewEntry = bridge != null ? bridge.transform.position + new Vector3(0f, -0.15f, -4.2f) : pos + new Vector3(10f, 0f, -9f),
                        equipmentPark = pos + new Vector3(-11f, 0f, -7f), beltPark = pos + new Vector3(-17f, 0f, -7f),
                    });
                }
            }
            // Stand 24 (the training stand) first, so the iPad starts there.
            stands.Sort((a, b) => a.name == "Stand 24" ? -1 : b.name == "Stand 24" ? 1 : string.CompareOrdinal(a.name, b.name));
            var registry = airport.AddComponent<StandRegistry>();
            registry.SetStands(stands);
            EditorUtility.SetDirty(registry);
        }

        // ------------------------------------------------------------------ colliders, aircraft

        static int AddMeshColliders(Transform node)
        {
            var n = 0;
            foreach (var filter in node.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                n++;
            }
            return n;
        }

        static int ParkAircraft(Sidecar spec)
        {
            var previous = GameObject.Find(k_ParkedName);
            if (previous != null) Undo.DestroyObjectImmediate(previous);
            var root = new GameObject(k_ParkedName).transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Park aircraft");
            var count = 0;
            for (var k = 0; k < k_Parked.GetLength(0); k++)
            {
                var spot = k_Parked[k, 0];
                var id = k_Parked[k, 1];
                SpawnPoint point = null;
                foreach (var p in spec.spawnPoints ?? new SpawnPoint[0]) if (p.name == spot) point = p;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{k_AircraftDir}/{id}.glb");
                if (point == null || model == null || point.position == null || point.position.Length < 3) continue;
                var holder = new GameObject($"{spot.Replace('_', ' ')} - {id}").transform;
                holder.SetParent(root, false);
                holder.SetPositionAndRotation(new Vector3(point.position[0], point.position[1], point.position[2]), Quaternion.Euler(0f, point.yaw, 0f));
                var aircraft = (GameObject)PrefabUtility.InstantiatePrefab(model, holder);
                aircraft.transform.localPosition = Vector3.zero;
                aircraft.transform.localRotation = Quaternion.identity;
                GltfToQuest.Convert(aircraft);
                var rig = aircraft.AddComponent<AircraftRig>();
                var json = AssetDatabase.LoadAssetAtPath<TextAsset>($"{k_AircraftDir}/{id}.json");
                if (json != null) Phase1SandboxBuilder.Props(rig, ("definition", json));
                AircraftPlacer.AddStaticColliders(aircraft.transform);
                count++;
            }
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            return count;
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

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
