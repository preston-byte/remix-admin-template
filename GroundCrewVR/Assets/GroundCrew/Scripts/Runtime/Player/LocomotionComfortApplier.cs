using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace GroundCrew.Player
{
    /// <summary>
    /// Applies <see cref="ComfortSettings"/> to the XRI Starter Assets rig.
    ///
    /// The Starter Assets "XR Origin (XR Rig)" already implements teleport, smooth move,
    /// snap/smooth turn and the comfort vignette well; we reuse it rather than re-implement
    /// locomotion. Its per-hand ControllerInputActionManager decides whether a thumbstick
    /// teleports or moves via the smoothMotionEnabled / smoothTurnEnabled properties.
    ///
    /// That class lives in the *sample* assembly, which our code must not hard-reference
    /// (samples can be re-imported at a different path/version), so it is driven by name via
    /// reflection. If XRI renames those properties, a warning is logged and the rig keeps
    /// its defaults - nothing breaks.
    /// </summary>
    public class LocomotionComfortApplier : MonoBehaviour
    {
        const string k_ManagerTypeName = "ControllerInputActionManager";

        void OnEnable() => ComfortSettings.Changed += Apply;
        void OnDisable() => ComfortSettings.Changed -= Apply;

        IEnumerator Start()
        {
            // Let the Starter Assets components initialise first, then override.
            yield return null;
            Apply();
        }

        public void Apply()
        {
            var found = false;
            foreach (var behaviour in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name != k_ManagerTypeName) continue;
                found = true;

                var isLeft = IsLeftHand(behaviour.transform);
                // Convention: left stick walks, right stick teleports (forward) and turns (sideways).
                SetBool(behaviour, "smoothMotionEnabled", isLeft && ComfortSettings.Move == MoveMode.SmoothAndTeleport);
                SetBool(behaviour, "smoothTurnEnabled", !isLeft && ComfortSettings.Turn == TurnMode.Smooth);
            }

            if (!found)
                Debug.LogWarning("[Comfort] No ControllerInputActionManager found under the rig. Is this the XRI Starter Assets 'XR Origin (XR Rig)'?", this);

            foreach (var snap in GetComponentsInChildren<SnapTurnProvider>(true))
                snap.turnAmount = ComfortSettings.SnapAngle;
        }

        static bool IsLeftHand(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                var n = t.name.ToLowerInvariant();
                if (n.Contains("left")) return true;
                if (n.Contains("right")) return false;
            }
            return false;
        }

        static void SetBool(object target, string member, bool value)
        {
            var type = target.GetType();
            var property = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
            {
                property.SetValue(target, value);
                return;
            }

            Debug.LogWarning($"[Comfort] {type.Name}.{member} not found - XRI version mismatch? Comfort option ignored.");
        }
    }
}
