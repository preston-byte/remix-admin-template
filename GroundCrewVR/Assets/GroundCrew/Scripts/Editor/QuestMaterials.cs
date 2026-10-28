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

namespace GroundCrew.EditorTools
{
    /// <summary>
    /// Converts glTFast's own materials (shader-graph PBR, which carries a very large number of
    /// shader variants and makes Quest builds slow) on the aircraft, vehicles and gloves into URP
    /// Simple Lit materials with the same colour, texture, emission, transparency and culling.
    /// One converted material per source material, shared by every instance.
    /// </summary>
    internal static class GltfToQuest
    {
        const string k_Dir = "Assets/GroundCrew/Generated/QuestMaterials";

        static readonly System.Collections.Generic.Dictionary<UnityEngine.Material, UnityEngine.Material> s_Cache =
            new System.Collections.Generic.Dictionary<UnityEngine.Material, UnityEngine.Material>();

        /// <summary>Swap every glTFast material under `root`; returns how many renderers changed.</summary>
        internal static int Convert(UnityEngine.GameObject root)
        {
            if (root == null) return 0;
            var simpleLit = UnityEngine.Shader.Find("Universal Render Pipeline/Simple Lit");
            if (simpleLit == null) return 0;
            EnsureFolder(k_Dir);
            var count = 0;
            foreach (var r in root.GetComponentsInChildren<UnityEngine.Renderer>(true))
            {
                if (r is UnityEngine.LineRenderer || r is UnityEngine.ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                var changed = false;
                for (var i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null || src.shader == null || !IsGltf(src.shader.name)) continue;
                    mats[i] = Converted(src, simpleLit);
                    changed = true;
                }
                if (!changed) continue;
                r.sharedMaterials = mats;
                count++;
            }
            UnityEditor.AssetDatabase.SaveAssets();
            return count;
        }

        static bool IsGltf(string shaderName) => shaderName.IndexOf("glTF", System.StringComparison.OrdinalIgnoreCase) >= 0;

        static UnityEngine.Material Converted(UnityEngine.Material src, UnityEngine.Shader simpleLit)
        {
            if (s_Cache.TryGetValue(src, out var cached) && cached != null) return cached;
            var owner = System.IO.Path.GetFileNameWithoutExtension(UnityEditor.AssetDatabase.GetAssetPath(src));
            var path = $"{k_Dir}/{Safe(owner)}_{Safe(src.name)}.mat";
            var m = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
            if (m == null)
            {
                m = new UnityEngine.Material(simpleLit);
                UnityEditor.AssetDatabase.CreateAsset(m, path);
            }
            m.shader = simpleLit;

            var color = Color(src, "baseColorFactor", "_BaseColor", "_Color");
            m.color = color;
            var tex = Texture(src, "baseColorTexture", "_BaseMap", "_MainTex");
            m.mainTexture = tex;
            if (tex != null && src.HasProperty("baseColorTexture_ST"))
            {
                var st = src.GetVector("baseColorTexture_ST");
                m.mainTextureScale = new UnityEngine.Vector2(st.x, st.y);
                m.mainTextureOffset = new UnityEngine.Vector2(st.z, st.w);
            }

            // Shininess: glTF roughness -> Simple Lit smoothness; specular highlights only on shiny things.
            var roughness = Float(src, 0.5f, "roughnessFactor", "_Roughness");
            var metallic = Float(src, 0f, "metallicFactor", "_Metallic");
            var smooth = UnityEngine.Mathf.Clamp01(1f - roughness);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            var shiny = smooth > 0.45f || metallic > 0.5f;
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", shiny ? 1f : 0f);
            if (shiny)
            {
                m.EnableKeyword("_SPECULAR_COLOR");
                if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", UnityEngine.Color.white * (0.25f + 0.5f * metallic));
            }
            else m.DisableKeyword("_SPECULAR_COLOR");

            // Emission (lamps, screens).
            var emission = Color(src, "emissiveFactor", "_EmissionColor");
            var emissionTex = Texture(src, "emissiveTexture", "_EmissionMap");
            var emissive = emission.maxColorComponent > 0.001f;
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
                if (emissionTex != null && m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", emissionTex);
                m.globalIlluminationFlags = UnityEngine.MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", UnityEngine.Color.black);
            }

            // Transparency (windows, lenses).
            var transparent = src.renderQueue >= 2900 || color.a < 0.99f;
            if (transparent)
            {
                SetF(m, "_Surface", 1f);
                SetF(m, "_Blend", 0f);
                SetF(m, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                SetF(m, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                SetF(m, "_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                SetF(m, "_Surface", 0f);
                SetF(m, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                SetF(m, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                SetF(m, "_ZWrite", 1f);
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = -1;
            }

            // Culling: glTF double-sided -> off.
            var doubleSided = src.doubleSidedGI || Float(src, 2f, "_CullMode", "_Cull") < 0.5f;
            SetF(m, "_Cull", doubleSided ? 0f : 2f);
            m.doubleSidedGI = doubleSided;
            m.enableInstancing = true;
            UnityEditor.EditorUtility.SetDirty(m);
            s_Cache[src] = m;
            return m;
        }

        static UnityEngine.Color Color(UnityEngine.Material m, params string[] names)
        {
            foreach (var n in names) if (m.HasProperty(n)) return m.GetColor(n);
            return UnityEngine.Color.white;
        }

        static UnityEngine.Texture Texture(UnityEngine.Material m, params string[] names)
        {
            foreach (var n in names) if (m.HasProperty(n) && m.GetTexture(n) != null) return m.GetTexture(n);
            return null;
        }

        static float Float(UnityEngine.Material m, float fallback, params string[] names)
        {
            foreach (var n in names) if (m.HasProperty(n)) return m.GetFloat(n);
            return fallback;
        }

        static void SetF(UnityEngine.Material m, string name, float value)
        {
            if (m.HasProperty(name)) m.SetFloat(name, value);
        }

        static string Safe(string s)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        static void EnsureFolder(string path)
        {
            if (UnityEditor.AssetDatabase.IsValidFolder(path)) return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            UnityEditor.AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
