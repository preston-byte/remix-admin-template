using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GroundCrew.EditorTools
{
    /// <summary>
    /// Swaps glTFast's PBR materials for URP Simple Lit (or the ground-paint shader) from a list
    /// of material specs in a generator's JSON sidecar: cheaper on Quest, and colours / textures /
    /// emission / double-sidedness come straight from the generator.
    /// </summary>
    internal static class QuestMaterials
    {
#pragma warning disable 0649   // filled by JsonUtility
        [Serializable] internal class Spec
        {
            public string name, texture, shader;
            public float[] color, emission;
            public float metallic, smoothness, tileMetres;
            public bool doubleSided;
        }
#pragma warning restore 0649


        /// <summary>Replace every material on `root` whose name matches a spec with a Simple Lit one saved in `materialDir`.</summary>
        internal static int Swap(GameObject root, Spec[] specs, string materialDir, string textureRoot)
        {
            EnsureFolder(materialDir);
            var map = new Dictionary<string, Material>();
            foreach (var s in specs ?? new Spec[0]) map[s.name] = SimpleLit(s, materialDir, textureRoot);
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

        static Material SimpleLit(Spec s, string materialDir, string textureRoot)
        {
            var path = $"{materialDir}/{s.name}.mat";
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
                var texPath = $"{textureRoot}/{s.texture}";
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
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", s.doubleSided ? 0f : 2f);
            material.doubleSidedGI = s.doubleSided;
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


        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
