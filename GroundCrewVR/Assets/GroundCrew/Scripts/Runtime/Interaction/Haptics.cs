using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace GroundCrew.Interaction
{
    /// <summary>
    /// One place for controller vibration. XRI 3 puts a HapticImpulsePlayer on each controller
    /// in the Starter Assets rig; interactors are children of that controller, so we look it
    /// up from whatever interactor (or fingertip) caused the interaction.
    ///
    /// Sockets and other non-hand interactors simply have no player in their parents, so
    /// calls from them are harmless no-ops.
    /// </summary>
    public static class Haptics
    {
        public static void Pulse(IXRInteractor interactor, float amplitude, float duration)
        {
            if (interactor == null) return;
            Pulse(interactor.transform, amplitude, duration);
        }

        public static void Pulse(Transform source, float amplitude, float duration)
        {
            if (source == null) return;
            var player = source.GetComponentInParent<HapticImpulsePlayer>();
            if (player != null)
                player.SendHapticImpulse(Mathf.Clamp01(amplitude), Mathf.Max(0.01f, duration));
        }
    }
}
