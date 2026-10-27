using System;
using GroundCrew.Crew;
using GroundCrew.Interaction;
using GroundCrew.Operations;
using GroundCrew.UI;
using GroundCrew.Vehicles;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Object = UnityEngine.Object;

namespace GroundCrew.EditorTools
{
    /// <summary>
    /// Menu: Ground Crew > Operations > Set Up iPad, Crew and AI Ground Crew. Also run by the
    /// sandbox builder, Add Vehicles and Build Airport, so you rarely need it by hand.
    ///
    ///  - builds prefabs in Assets/GroundCrew/Generated/Operations: Pilot, CabinCrew, GroundCrew
    ///    (generated figures + CrewAnimator / CrewAgent / CrewAppearance, Simple Lit materials),
    ///    WheelChock and SafetyCone (the sandbox's grabbable items);
    ///  - writes OpsConfig.asset pointing at them and at the vehicle catalog;
    ///  - adds the "Operations" object (TurnaroundOps) and the Ops iPad to the open scene.
    /// </summary>
    public static class OperationsSetup
    {
        const string k_Dir = "Assets/GroundCrew/Generated/Operations";
        const string k_CharDir = "Assets/GroundCrew/Art/Characters";
        const string k_CatalogPath = "Assets/GroundCrew/Generated/Vehicles/VehicleCatalog.asset";

        [Serializable] class CharacterSidecar { public QuestMaterials.Spec[] materials; }

        [MenuItem("Ground Crew/Operations/Set Up iPad, Crew and AI Ground Crew", priority = 50)]
        static void Menu()
        {
            Install(true);
            Debug.Log("[Ops] Added the Ops iPad (it floats at your waist, front-left) and the operations manager. Save the scene to keep them.");
        }

        internal static void Install(bool verbose)
        {
            var config = BuildConfig(verbose);
            foreach (var name in new[] { "Operations", "Ops iPad" })
            {
                var old = GameObject.Find(name);
                if (old != null) Undo.DestroyObjectImmediate(old);
            }
            var ops = new GameObject("Operations");
            Undo.RegisterCreatedObjectUndo(ops, "Operations");
            Phase1SandboxBuilder.Props(ops.AddComponent<TurnaroundOps>(), ("config", config));
            var tablet = BuildTablet();
            Undo.RegisterCreatedObjectUndo(tablet, "Operations");
            EditorSceneManager.MarkSceneDirty(ops.scene);
        }

        // ------------------------------------------------------------------ prefabs + config

        static OpsConfig BuildConfig(bool verbose)
        {
            EnsureFolder(k_Dir);
            var path = k_Dir + "/OpsConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<OpsConfig>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<OpsConfig>();
                AssetDatabase.CreateAsset(config, path);
            }
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(k_CharDir + "/Characters.json");
            var specs = json != null ? JsonUtility.FromJson<CharacterSidecar>(json.text).materials : null;
            config.pilotPrefab = CharacterPrefab("Pilot", CrewRole.Captain, specs, verbose);
            config.cabinCrewPrefab = CharacterPrefab("CabinCrew", CrewRole.CabinCrew, specs, verbose);
            config.groundCrewPrefab = CharacterPrefab("GroundCrew", CrewRole.GroundCrew, specs, verbose);
            config.passengerPrefab = CharacterPrefab("Passenger", CrewRole.Passenger, specs, verbose);
            config.seatedPassengerPrefab = SeatedPrefab(specs, verbose);
            config.bagPrefabs = new[]
            {
                Save(Phase1SandboxBuilder.BagItem(0), k_Dir + "/Bag_Black.prefab"),
                Save(Phase1SandboxBuilder.BagItem(1), k_Dir + "/Bag_Red.prefab"),
                Save(Phase1SandboxBuilder.BagItem(2), k_Dir + "/Bag_Green.prefab"),
            };
            config.hoseMaterial = Phase1SandboxBuilder.Materials.Cable;
            config.chockPrefab = Save(Phase1SandboxBuilder.SafetyItem(true), k_Dir + "/WheelChock.prefab");
            config.conePrefab = Save(Phase1SandboxBuilder.SafetyItem(false), k_Dir + "/SafetyCone.prefab");
            config.vehicles = AssetDatabase.LoadAssetAtPath<VehicleCatalog>(k_CatalogPath);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return config;
        }

        static GameObject CharacterPrefab(string id, CrewRole role, QuestMaterials.Spec[] specs, bool verbose)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{k_CharDir}/{id}.glb");
            if (model == null)
            {
                if (verbose) Debug.LogWarning($"[Ops] {k_CharDir}/{id}.glb isn't imported as a model (glTFast missing?) - no {id} crew.");
                return AssetDatabase.LoadAssetAtPath<GameObject>($"{k_Dir}/{id}.prefab");
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = id;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (specs != null) QuestMaterials.Swap(go, specs, k_Dir + "/Materials", k_CharDir);
            go.AddComponent<CrewAnimator>();
            var agent = go.AddComponent<CrewAgent>();
            Phase1SandboxBuilder.Props(agent, ("role", role));
            go.AddComponent<CrewAppearance>();
            return Save(go, $"{k_Dir}/{id}.prefab");
        }

        /// <summary>The passenger as one already-sitting mesh: no joints, no animator, just their colours.</summary>
        static GameObject SeatedPrefab(QuestMaterials.Spec[] specs, bool verbose)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{k_CharDir}/PassengerSeated.glb");
            if (model == null)
            {
                if (verbose) Debug.LogWarning($"[Ops] {k_CharDir}/PassengerSeated.glb isn't imported - seated passengers will be skipped.");
                return AssetDatabase.LoadAssetAtPath<GameObject>($"{k_Dir}/PassengerSeated.prefab");
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "PassengerSeated";
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (specs != null) QuestMaterials.Swap(go, specs, k_Dir + "/Materials", k_CharDir);
            var look = go.AddComponent<CrewAppearance>();
            Phase1SandboxBuilder.Props(look, ("randomise", false));
            return Save(go, $"{k_Dir}/PassengerSeated.prefab");
        }

        static GameObject Save(GameObject go, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ------------------------------------------------------------------ the iPad

        static readonly string[] k_Tabs = { "STAND", "BRIDGE", "CREW", "GROUND", "SERVICE", "PLANE" };

        static GameObject BuildTablet()
        {
            var mats = Phase1SandboxBuilder.Materials;
            var root = new GameObject("Ops iPad");
            root.transform.position = new Vector3(-0.2f, 1.1f, 0.3f);
            var body = Phase1SandboxBuilder.Box("Body", root.transform, new Vector3(0f, 0f, 0.006f), new Vector3(0.27f, 0.205f, 0.012f), mats.DarkMetal, true, false);
            Phase1SandboxBuilder.Box("Bezel", root.transform, new Vector3(0f, 0f, -0.0002f), new Vector3(0.262f, 0.197f, 0.001f), mats.Black, false, false);

            // Screen: a world-space canvas 25 x 18.5 cm (500 x 370 at 0.5 mm per unit), facing -z.
            var canvasGo = new GameObject("Screen", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(root.transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)canvasGo.transform;
            rect.sizeDelta = new Vector2(500f, 370f);
            rect.localScale = Vector3.one * 0.0005f;
            rect.localPosition = new Vector3(0f, 0f, -0.0012f);
            var background = Phase1SandboxBuilder.UIChild("Background", rect, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            background.rectTransform.offsetMin = background.rectTransform.offsetMax = Vector2.zero;
            background.color = new Color(0.04f, 0.06f, 0.09f, 1f);
            background.raycastTarget = false;
            // Rows (fraction of the screen height): tabs 0.86-0.99, header 0.75-0.85, body 0.40-0.74, footer 0.32-0.40, keys below.
            var header = Phase1SandboxBuilder.UIText("Header", rect, new Vector2(0f, 0.75f), new Vector2(1f, 0.85f), 17f, TextAlignmentOptions.Left);
            var bodyText = Phase1SandboxBuilder.UIText("Body", rect, new Vector2(0f, 0.40f), new Vector2(1f, 0.745f), 12.5f, TextAlignmentOptions.TopLeft);
            var footer = Phase1SandboxBuilder.UIText("Footer", rect, new Vector2(0f, 0.32f), new Vector2(1f, 0.40f), 11f, TextAlignmentOptions.Left);
            footer.color = new Color(0.55f, 0.85f, 1f);

            var tabs = new PressButton[k_Tabs.Length];
            for (var i = 0; i < k_Tabs.Length; i++)
                tabs[i] = Phase1SandboxBuilder.TabletButton(root.transform, k_Tabs[i], new Vector3(-0.105f + i * 0.042f, 0.078f, 0f), 0.039f);
            var keys = new PressButton[8];
            for (var i = 0; i < 8; i++)
                keys[i] = Phase1SandboxBuilder.TabletButton(root.transform, "-", new Vector3(-0.093f + (i % 4) * 0.062f, i < 4 ? -0.047f : -0.075f, 0f), 0.058f);
            // Long labels ("REMOVE CHOCKS", "ALL SERVICES") shrink to fit their key.
            foreach (var b in tabs) AutoSize(b);
            foreach (var b in keys) AutoSize(b);

            var grab = root.AddComponent<XRGrabInteractable>();
            grab.colliders.Clear();
            grab.colliders.Add(body.GetComponent<Collider>());
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;
            grab.useDynamicAttach = true;
            var rb = root.GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var tablet = root.AddComponent<OpsTablet>();
            Phase1SandboxBuilder.Props(tablet, ("headerText", header), ("bodyText", bodyText), ("footerText", footer), ("grab", grab), ("bodyCollider", body.GetComponent<Collider>()));
            SetArray(tablet, "tabs", tabs);
            SetArray(tablet, "keys", keys);
            return root;
        }

        static void AutoSize(PressButton b)
        {
            var text = b != null ? b.GetComponentInChildren<TMP_Text>(true) : null;
            if (text == null) return;
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.04f;
            text.fontSizeMax = 0.09f;
        }

        static void SetArray(Object target, string property, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(property);
            p.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
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
