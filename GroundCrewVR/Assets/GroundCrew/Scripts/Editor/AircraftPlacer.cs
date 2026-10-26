using GroundCrew.Aircraft;
using GroundCrew.Interaction;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GroundCrew.EditorTools
{
    /// <summary>
    /// Menu: Ground Crew > Place Aircraft on Stand 24 > 737-class / A320-class.
    ///
    /// Parks a generated aircraft (Assets/GroundCrew/Art/Aircraft/*.glb, imported with glTFast)
    /// on the sandbox stand: nose gear on the stop bar, nose towards the training stations.
    /// Adds an <see cref="AircraftRig"/>, static mesh colliders on the airframe (not on moving
    /// parts) and a small control pedestal with DOORS / BEACON / FLAPS push buttons so the model
    /// can be tried on the Quest before the Phase 2/3 gameplay exists.
    /// </summary>
    public static class AircraftPlacer
    {
        const string k_ArtDir = "Assets/GroundCrew/Art/Aircraft";
        const string k_RootName = "Stand 24 Aircraft";
        static readonly Vector3 k_StopBar = new Vector3(0f, 0f, 12f);

        [MenuItem("Ground Crew/Place Aircraft on Stand 24/737-class", priority = 20)]
        static void Place737() => Place("Narrowbody737Class");

        [MenuItem("Ground Crew/Place Aircraft on Stand 24/A320-class", priority = 21)]
        static void Place320() => Place("Narrowbody320Class");

        static void Place(string id)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{k_ArtDir}/{id}.glb");
            if (model == null)
            {
                EditorUtility.DisplayDialog("Aircraft model not imported",
                    $"Could not load {k_ArtDir}/{id}.glb as a model.\n\n" +
                    "Unity needs the glTFast package to import .glb files:\n" +
                    "Window > Package Manager > + > Add package by name > com.unity.cloud.gltfast\n\n" +
                    "Then right-click the Aircraft folder > Reimport, and run this menu again.", "OK");
                return;
            }

            var previous = GameObject.Find(k_RootName);
            if (previous != null) Undo.DestroyObjectImmediate(previous);

            var root = new GameObject(k_RootName);
            Undo.RegisterCreatedObjectUndo(root, "Place aircraft");
            // Generated aircraft: origin = nose-gear ground contact, nose along +Z.
            // Park on the stop bar facing back down the lead-in line (towards the training area).
            root.transform.SetPositionAndRotation(k_StopBar, Quaternion.Euler(0f, 180f, 0f));

            var aircraft = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            aircraft.transform.localPosition = Vector3.zero;
            aircraft.transform.localRotation = Quaternion.identity;

            var rig = aircraft.AddComponent<AircraftRig>();
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>($"{k_ArtDir}/{id}.json");
            if (json != null) Phase1SandboxBuilder.Props(rig, ("definition", json));

            var colliders = AddStaticColliders(aircraft.transform);
            BuildControlPedestal(root.transform, rig);

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = aircraft;
            Debug.Log($"[Aircraft] Placed {id} on Stand 24 with {colliders} static colliders. Save the scene to keep it.");
        }

        /// <summary>Mesh colliders on the LOD0 airframe, wings, tail and gear - never on moving parts.</summary>
        static int AddStaticColliders(Transform aircraft)
        {
            Transform lod0 = null;
            foreach (Transform child in aircraft)
                if (child.name.StartsWith("LOD0")) lod0 = child;
            if (lod0 == null) return 0;

            var count = 0;
            foreach (var filter in lod0.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || IsUnderMovingPart(filter.transform, lod0)) continue;
                if (filter.GetComponent<Collider>() != null) continue;
                var mc = filter.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = filter.sharedMesh;
                count++;
            }
            return count;
        }

        static bool IsUnderMovingPart(Transform t, Transform stopAt)
        {
            for (; t != null && t != stopAt; t = t.parent)
            {
                if (t.name.StartsWith("Beacon_") || t.name.StartsWith("NavLight_") || t.name.StartsWith("Strobe_")) return true;
                foreach (Transform c in t)
                    if (c.name.StartsWith("HingeEnd")) return true;
            }
            return false;
        }

        static void BuildControlPedestal(Transform parent, AircraftRig rig)
        {
            var mats = Phase1SandboxBuilder.Materials;
            var panel = new GameObject("Aircraft Demo Panel").transform;
            panel.SetParent(parent, false);
            // Beside the nose, facing the player spawn.
            var position = new Vector3(-3.8f, 0f, 6.4f);
            var flat = new Vector3(position.x, 0f, position.z);
            panel.SetPositionAndRotation(position, Quaternion.LookRotation(flat.normalized, Vector3.up));

            Phase1SandboxBuilder.Box("Pedestal", panel, new Vector3(0f, 0.5f, 0f), new Vector3(0.9f, 1f, 0.4f), mats.DarkMetal, true, true);
            Phase1SandboxBuilder.Text3D(panel, new Vector3(0f, 1.45f, 0.15f), "<b>AIRCRAFT TEST</b>\n<size=60%>touch a button</size>", 0.9f, new Vector2(1.2f, 0.4f), Color.white);

            Button(panel, "DOORS", new Vector3(-0.33f, 1f, -0.02f), mats.Green, rig.ToggleAllDoors);
            Button(panel, "BEACON", new Vector3(-0.11f, 1f, -0.02f), mats.Red, rig.ToggleBeacon);
            Button(panel, "FLAPS", new Vector3(0.11f, 1f, -0.02f), mats.TabletButton, rig.ToggleFlaps);
            Button(panel, "CKPT LT", new Vector3(0.33f, 1f, -0.02f), mats.TabletButton, rig.ToggleFlightDeckLight);
        }

        static void Button(Transform parent, string label, Vector3 position, Material material, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(label + " Button");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0.03f, 0f);
            trigger.size = new Vector3(0.12f, 0.05f, 0.12f);
            var cap = Phase1SandboxBuilder.Cylinder("Cap", go.transform, new Vector3(0f, 0.012f, 0f), new Vector3(0.1f, 0.012f, 0.1f), material);
            var text = Phase1SandboxBuilder.Text3D(go.transform, new Vector3(0f, 0.002f, -0.1f), label, 0.35f, new Vector2(0.24f, 0.06f), Color.white);
            text.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var button = go.AddComponent<PressButton>();
            Phase1SandboxBuilder.Props(button, ("label", label), ("movingCap", cap.transform), ("pressDirection", Vector3.down), ("pressDepth", 0.01f));
            UnityEventTools.AddPersistentListener(button.onPressed, action);
        }
    }
}
