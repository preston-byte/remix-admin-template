using GroundCrew.Player;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GroundCrew.EditorTools
{
    /// <summary>
    /// Menu: Ground Crew > Player > Add Gloved Hands to Rig (also run by Build Phase 1 Sandbox).
    ///
    /// Puts the generated glove (Assets/GroundCrew/Art/Hands/Hand_R.glb, mirrored for the left
    /// hand) on each controller of the XR rig and adds <see cref="ControllerHands"/>, which hides
    /// the controller model, poses the fingers from grip / trigger / thumb touch and moves the
    /// poke point to the index fingertip.
    ///
    /// The hand sits on the controller's grip pose: palm towards the grip, thumb on top. If it
    /// sits wrong for you, move the "Glove Hand" child of the controller (position and rotation).
    /// </summary>
    public static class HandsSetup
    {
        const string k_HandModel = "Assets/GroundCrew/Art/Hands/Hand_R.glb";
        const string k_ChildName = "Glove Hand";

        // Wrist behind and to the outside of the grip, palm facing the grip.
        static readonly Vector3 k_RightPosition = new Vector3(0.034f, -0.012f, -0.078f);
        static readonly Vector3 k_RightEuler = new Vector3(0f, 0f, -90f);

        [MenuItem("Ground Crew/Player/Add Gloved Hands to Rig", priority = 20)]
        static void AddToSceneRig()
        {
            var origin = Object.FindAnyObjectByType<XROrigin>();
            if (origin == null)
            {
                EditorUtility.DisplayDialog("No XR rig", "Open a scene with an XR Origin (e.g. build the Phase 1 sandbox) and run this again.", "OK");
                return;
            }
            if (Install(origin.transform) > 0)
            {
                EditorSceneManager.MarkSceneDirty(origin.gameObject.scene);
                Debug.Log("[Hands] Gloved hands added to both controllers. Save the scene to keep them.");
            }
        }

        /// <summary>Adds (or replaces) the glove on both controllers; returns how many were set up.</summary>
        internal static int Install(Transform rig)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(k_HandModel);
            if (model == null)
            {
                Debug.LogWarning($"[Hands] {k_HandModel} is not imported as a model (glTFast missing?) - keeping controller models.");
                return 0;
            }
            var count = 0;
            foreach (var side in new[] { HandSide.Left, HandSide.Right })
            {
                var name = side == HandSide.Left ? "Left Controller" : "Right Controller";
                var controller = FindDeep(rig, name);
                if (controller == null) { Debug.LogWarning($"[Hands] '{name}' not found in the rig."); continue; }

                var old = controller.Find(k_ChildName);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
                var oldHands = controller.GetComponent<ControllerHands>();
                if (oldHands != null) Undo.DestroyObjectImmediate(oldHands);

                var hand = (GameObject)PrefabUtility.InstantiatePrefab(model, controller);
                PrefabUtility.UnpackPrefabInstance(hand, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                Undo.RegisterCreatedObjectUndo(hand, "Add gloved hands");
                hand.name = k_ChildName;
                var left = side == HandSide.Left;
                // The model is a right hand; the left one is its mirror image.
                hand.transform.localPosition = left ? new Vector3(-k_RightPosition.x, k_RightPosition.y, k_RightPosition.z) : k_RightPosition;
                hand.transform.localRotation = Quaternion.Euler(left ? -k_RightEuler : k_RightEuler);
                hand.transform.localScale = left ? new Vector3(-1f, 1f, 1f) : Vector3.one;
                GltfToQuest.Convert(hand);
                foreach (var r in hand.GetComponentsInChildren<Renderer>(true))
                    r.shadowCastingMode = ShadowCastingMode.Off;   // cheap on Quest; hands are close to the eye anyway

                var driver = Undo.AddComponent<ControllerHands>(controller.gameObject);
                Phase1SandboxBuilder.Props(driver, ("side", side), ("handRoot", hand.transform));
                count++;
            }
            return count;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var hit = FindDeep(c, name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
