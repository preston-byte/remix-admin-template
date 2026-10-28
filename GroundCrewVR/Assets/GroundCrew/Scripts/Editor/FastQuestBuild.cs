using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace GroundCrew.EditorTools
{
    /// <summary>
    /// Menu: Ground Crew > Quest > Apply Fast Build Settings.
    ///
    /// Cuts Quest build time without changing how the game looks:
    ///  - IL2CPP code generation "Faster (smaller) builds" for Android;
    ///  - shader stripping: no lightmap variants (nothing is baked) and only linear fog (what the
    ///    airport uses), so every lit shader compiles a fraction of its variants;
    ///  - in every URP asset: soft shadows and HDR off (both unused on Quest here).
    /// Run it once; it's safe to run again. The other big saving - converting the aircraft and
    /// vehicle materials to Simple Lit - happens automatically when you rerun the Ground Crew menus.
    /// For code-only updates, a Development Build with "Patch And Run" is much faster still.
    /// </summary>
    public static class FastQuestBuild
    {
        [MenuItem("Ground Crew/Quest/Apply Fast Build Settings", priority = 10)]
        static void Apply()
        {
            var log = new StringBuilder();

            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.Android, Il2CppCodeGeneration.OptimizeSize);
            log.AppendLine("IL2CPP code generation: Faster (smaller) builds");

            var graphics = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphics != null && graphics.Length > 0)
            {
                var so = new SerializedObject(graphics[0]);
                Set(so, "m_LightmapStripping", 1);          // custom
                Set(so, "m_LightmapKeepPlain", 1);
                Set(so, "m_LightmapKeepDirCombined", 0);
                Set(so, "m_LightmapKeepDynamicPlain", 0);
                Set(so, "m_LightmapKeepDynamicDirCombined", 0);
                Set(so, "m_LightmapKeepShadowMask", 0);
                Set(so, "m_LightmapKeepSubtractive", 0);
                Set(so, "m_FogStripping", 1);               // custom
                Set(so, "m_FogKeepLinear", 1);
                Set(so, "m_FogKeepExp", 0);
                Set(so, "m_FogKeepExp2", 0);
                Set(so, "m_InstancingStripping", 0);        // strip unused
                so.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine("Shader stripping: no lightmap variants, linear fog only");
            }

            var urp = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                var so = new SerializedObject(asset);
                Set(so, "m_SoftShadowsSupported", 0);
                Set(so, "m_SupportsHDR", 0);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                urp++;
            }
            log.AppendLine($"URP assets updated (soft shadows, HDR off): {urp}");
            AssetDatabase.SaveAssets();

            Debug.Log("[Fast build] " + log.ToString().Replace("\n", "; "));
            EditorUtility.DisplayDialog("Fast Quest build settings applied", log +
                "\nAlso: rerun the Ground Crew menus once so the aircraft and vehicles use the lighter Quest materials." +
                "\n\nFor updates that only change code: tick Development Build in Build Profiles and use Patch And Run.", "OK");
        }

        static void Set(SerializedObject so, string property, int value)
        {
            var p = so.FindProperty(property);
            if (p == null) return;
            if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value != 0;
            else p.intValue = value;
        }
    }
}
