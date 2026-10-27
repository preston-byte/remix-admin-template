using System;
using System.Collections.Generic;
using GroundCrew.Aircraft;
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
            public MaterialSpec[] materials;
            public SpawnPoint[] spawnPoints;
            public string[] groundNodes, solidNodes;
        }
        [Serializable] class MaterialSpec { public string name, texture, shader; public float[] color, emission; public float metallic, smoothness, tileMetres; }
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

            var swapped = SwapMaterials(airport, spec.materials);
            var colliders = 0;
            var teleportMask = InteractionLayerMask.GetMask("Teleport");
            foreach (var name in spec.groundNodes ?? new string[0])
            {
                var node = Find(airport.transform, name);
                if (node == null) continue;
                colliders += AddMeshColliders(node);
                var area = node.gameObject.AddComponent<TeleportationArea>();
                if (teleportMask != 0) area.interactionLayers = teleportMask;
            }
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
            EditorSceneManager.MarkSceneDirty(airport.scene);
            Selection.activeGameObject = airport;
            Debug.Log($"[Airport] Built the airport: {swapped} materials converted, {colliders} colliders, {parked} parked aircraft. " +
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

        // ------------------------------------------------------------------ materials

        static int SwapMaterials(GameObject root, MaterialSpec[] specs)
        {
            EnsureFolder(k_MaterialDir);
            var map = new Dictionary<string, Material>();
            foreach (var s in specs ?? new MaterialSpec[0]) map[s.name] = SimpleLit(s);
            var count = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                var changed = false;
                for (var i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var key = mats[i].name.Replace(" (Instance)", "");
                    if (!map.TryGetValue(key, out var m) || m == null) continue;
                    mats[i] = m;
                    changed = true;
                }
                if (!changed) continue;
                r.sharedMaterials = mats;
                count++;
            }
            AssetDatabase.SaveAssets();
            return count;
        }

        static Material SimpleLit(MaterialSpec s)
        {
            var path = $"{k_MaterialDir}/{s.name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (s.shader == "paint")
            {
                // Ground paint: depth-offset shader, or Simple Lit if it didn't compile on this setup.
                var paint = Shader.Find("GroundCrew/Ground Paint");
                if (paint != null && !ShaderUtil.ShaderHasError(paint))
                {
                    if (material == null)
                    {
                        material = new Material(paint);
                        AssetDatabase.CreateAsset(material, path);
                    }
                    material.shader = paint;
                    material.color = s.color != null && s.color.Length >= 3 ? new Color(s.color[0], s.color[1], s.color[2]) : Color.white;
                    material.enableInstancing = true;
                    EditorUtility.SetDirty(material);
                    return material;
                }
                Debug.LogWarning("[Airport] Shader 'GroundCrew/Ground Paint' is missing or has errors - markings use Simple Lit and may flicker in the distance.");
            }
            // Simple Lit (Blinn-Phong) is noticeably cheaper than Lit (PBR) on Quest.
            var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader) material.shader = shader;
            var c = s.color != null && s.color.Length >= 3 ? new Color(s.color[0], s.color[1], s.color[2]) : Color.white;
            material.color = c;
            if (!string.IsNullOrEmpty(s.texture))
            {
                var texPath = $"{k_ArtDir}/{s.texture}";
                PrepareTexture(texPath);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex != null) material.mainTexture = tex;
            }
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", Mathf.Clamp01(s.smoothness));
            var shiny = s.smoothness > 0.45f || s.metallic > 0.5f;
            if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", shiny ? 1f : 0f);
            if (shiny)
            {
                material.EnableKeyword("_SPECULAR_COLOR");
                if (material.HasProperty("_SpecColor")) material.SetColor("_SpecColor", Color.white * (0.25f + 0.5f * s.metallic));
            }
            else material.DisableKeyword("_SPECULAR_COLOR");
            if (s.emission != null && s.emission.Length >= 3)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(s.emission[0], s.emission[1], s.emission[2]) * 2.5f);
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

        static void PrepareTexture(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            if (importer.anisoLevel == 4 && importer.wrapMode == TextureWrapMode.Repeat && importer.mipmapEnabled) return;
            importer.anisoLevel = 4;     // ground seen at a grazing angle stays sharp
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
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
