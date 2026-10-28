using Unity.XR.CoreUtils;
using UnityEngine;

namespace GroundCrew.Player
{
    /// <summary>
    /// Safety net for the player rig: if you ever drop through the ground (missing or broken
    /// floor collision), you're put back where you started instead of falling forever into an
    /// empty blue sky. It also writes what happened to the log (Unity Console / Android Logcat),
    /// including whether there was any floor under the start position at all.
    /// Put this on the XR Origin.
    /// </summary>
    public class FallSafety : MonoBehaviour
    {
        [SerializeField, Tooltip("Below this height (m) you count as fallen.")] float killHeight = -10f;

        XROrigin m_Origin;
        Vector3 m_Start;
        Quaternion m_StartRotation;
        int m_Rescues;

        void Start()
        {
            m_Origin = GetComponent<XROrigin>();
            m_Start = transform.position;
            m_StartRotation = transform.rotation;
            RaycastHit hit;
            if (Physics.Raycast(m_Start + Vector3.up * 2f, Vector3.down, out hit, 10f, ~0, QueryTriggerInteraction.Ignore))
                Debug.Log($"[FallSafety] Floor under the start position: '{hit.collider.name}' at y={hit.point.y:0.00}.", this);
            else
                Debug.LogError("[FallSafety] There is NO floor collider under the start position - the player will fall. " +
                               "Rerun Ground Crew > Airport > Build Airport Around Sandbox (or Build Phase 1 Sandbox Scene).", this);
        }

        void LateUpdate()
        {
            if (transform.position.y > killHeight) return;
            if (transform.parent != null) return;      // seated in a vehicle: the vehicle has its own FIX
            m_Rescues++;
            Debug.LogWarning($"[FallSafety] Fell through the ground near ({transform.position.x:0}, {transform.position.z:0}); " +
                             $"put back at the start (rescue {m_Rescues}).", this);
            var character = GetComponent<CharacterController>();
            if (character != null) character.enabled = false;
            transform.SetPositionAndRotation(m_Start + Vector3.up * 0.05f, m_StartRotation);
            if (character != null) character.enabled = true;
        }
    }
}
