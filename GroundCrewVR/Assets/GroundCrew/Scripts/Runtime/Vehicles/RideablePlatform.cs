using Unity.XR.CoreUtils;
using UnityEngine;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// Carries the player on something that moves under their feet: the catering truck's
    /// platform, later the stairs truck or the jet bridge.
    ///
    /// While the player's head is inside <see cref="volume"/> (a trigger box above the floor),
    /// the XR Origin is parented to <see cref="carrier"/> so it rises and falls with it; leaving
    /// the volume puts it back. A CharacterController does not ride moving colliders on its own.
    /// </summary>
    public class RideablePlatform : MonoBehaviour
    {
        [SerializeField] BoxCollider volume;
        [SerializeField] Transform carrier;

        XROrigin m_Origin;
        Transform m_PreviousParent;
        bool m_Riding;

        void Update()
        {
            if (volume == null) return;
            if (m_Origin == null)
            {
                m_Origin = FindFirstObjectByType<XROrigin>();
                if (m_Origin == null) return;
            }
            if (VehicleSeat.Current != null) { StopRiding(); return; }   // seated players ride their vehicle

            var head = m_Origin.Camera.transform.position;
            var local = volume.transform.InverseTransformPoint(head) - volume.center;
            var half = volume.size * 0.5f;
            var inside = Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
            if (inside && !m_Riding)
            {
                m_PreviousParent = m_Origin.transform.parent;
                m_Origin.transform.SetParent(carrier != null ? carrier : transform, true);
                m_Riding = true;
            }
            else if (!inside && m_Riding) StopRiding();
        }

        void StopRiding()
        {
            if (!m_Riding || m_Origin == null) return;
            m_Origin.transform.SetParent(m_PreviousParent, true);
            m_Riding = false;
        }

        void OnDisable() => StopRiding();
    }
}
