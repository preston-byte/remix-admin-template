using System.Collections.Generic;
using UnityEngine;

namespace GroundCrew.Vehicles
{
    public enum HitchType { Pintle, Pin }

    /// <summary>
    /// A coupling point on a vehicle: the pintle hook on a tractor or cart, the tow pin on a tug.
    /// </summary>
    public class TowHitch : MonoBehaviour
    {
        [SerializeField] HitchType type = HitchType.Pintle;
        [SerializeField, Tooltip("The vehicle body. Defaults to the Rigidbody in the parents.")] Rigidbody body;

        static readonly List<TowHitch> s_All = new List<TowHitch>();
        public static IReadOnlyList<TowHitch> All => s_All;

        public HitchType Type => type;
        public Rigidbody Body => body != null ? body : (body = GetComponentInParent<Rigidbody>());
        public TowCoupler Coupled { get; internal set; }
        public bool IsFree => Coupled == null;
        public VehicleController Vehicle => GetComponentInParent<VehicleController>();

        /// <summary>Hitch position from the body's physics pose (not the interpolated transform), for FixedUpdate code.</summary>
        public Vector3 PhysicsPosition
        {
            get
            {
                var b = Body;
                if (b == null) return transform.position;
                if (!m_HasLocal) { m_LocalInBody = Quaternion.Inverse(b.rotation) * (transform.position - b.position); m_HasLocal = true; }
                return b.position + b.rotation * m_LocalInBody;
            }
        }

        Vector3 m_LocalInBody;
        bool m_HasLocal;

        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_All.Clear();
    }
}
