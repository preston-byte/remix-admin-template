using UnityEngine;

namespace GroundCrew.Interaction
{
    /// <summary>
    /// The simplest interlock: a receptacle behind an access panel.
    ///  - The socket only accepts a plug while the panel is open past a threshold.
    ///  - The panel cannot be closed while something is plugged in.
    ///
    /// This is the pattern every service point will use (GPU receptacle, refuel panel, water
    /// and waste panels). Turnaround-level interlocks (no fuelling before chocks + bonding)
    /// live in the simulation layer and also drive <see cref="TypedSocketInteractor.Locked"/>.
    /// </summary>
    public class PanelInterlock : MonoBehaviour
    {
        [SerializeField] RotaryHandle panel;
        [SerializeField] TypedSocketInteractor socket;
        [SerializeField, Range(0f, 1f)] float openThreshold = 0.75f;

        void Update()
        {
            if (panel == null || socket == null) return;
            socket.Locked = panel.Value01 < openThreshold;
            panel.Locked = socket.IsConnected;
        }
    }
}
