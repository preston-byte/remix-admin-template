using UnityEngine;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// Keeps a boom basket level whatever the boom's pitch, as the hydraulic levelling on a
    /// de-icer or cherry picker does: the basket keeps its starting orientation relative to the
    /// slewing turret (which only yaws).
    /// </summary>
    public class BasketLeveller : MonoBehaviour
    {
        [SerializeField] Transform basket;
        [SerializeField, Tooltip("The slewing turret (rotates about the vertical only).")] Transform reference;

        Quaternion m_Offset;

        void Awake()
        {
            if (basket == null || reference == null) { enabled = false; return; }
            m_Offset = Quaternion.Inverse(reference.rotation) * basket.rotation;
        }

        void LateUpdate() => basket.rotation = reference.rotation * m_Offset;
    }
}
