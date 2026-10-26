using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace GroundCrew.Player
{
    /// <summary>
    /// Runtime performance setup for standalone Quest:
    ///  - Fixed foveated rendering (Quest 2 has no eye tracking, so gaze flags stay off).
    ///  - Physics fixed timestep matched to the display refresh rate. With the default 50 Hz
    ///    physics, held objects visibly stutter at 72/90 Hz because XRI velocity tracking moves
    ///    them in FixedUpdate.
    ///
    /// Everything else (MSAA, render scale, shadows, Vulkan, multiview) is project/URP
    /// configuration - see GroundCrewVR/README.md.
    /// </summary>
    public class QuestPerformanceSettings : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f), Tooltip("0 = off, 1 = maximum foveation.")]
        float foveationLevel = 0.6f;

        [SerializeField] bool matchPhysicsToRefreshRate = true;

        readonly List<XRDisplaySubsystem> m_Displays = new List<XRDisplaySubsystem>();

        IEnumerator Start()
        {
            // The XR display can take a few frames to start after the scene loads.
            for (var attempt = 0; attempt < 120; attempt++)
            {
                if (TryApply()) yield break;
                yield return null;
            }
            Debug.Log("[QuestPerformance] No running XR display (editor without headset?). Settings not applied.");
        }

        bool TryApply()
        {
            SubsystemManager.GetSubsystems(m_Displays);
            foreach (var display in m_Displays)
            {
                if (!display.running) continue;

                display.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.None;
                display.foveatedRenderingLevel = foveationLevel;

                if (matchPhysicsToRefreshRate && display.TryGetDisplayRefreshRate(out var hz) && hz > 1f)
                    Time.fixedDeltaTime = 1f / hz;

                Debug.Log($"[QuestPerformance] Foveation {foveationLevel:0.00}, physics {1f / Time.fixedDeltaTime:0} Hz.");
                return true;
            }
            return false;
        }
    }
}
