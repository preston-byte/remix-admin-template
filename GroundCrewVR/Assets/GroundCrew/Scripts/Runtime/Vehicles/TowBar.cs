using GroundCrew.Interaction;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// Tow bar between a pushback tug and an aircraft's nose gear.
    ///
    /// The eye end is a <see cref="TowCoupler"/> that couples to the tug's tow pin. Walk the head
    /// end (handle near the head, grip) to the nose-gear tow fitting and let go: it latches and
    /// the aircraft becomes towable (<see cref="AircraftTowable"/>). Hold the handle and pull the
    /// trigger to unlatch it again.
    ///
    /// While attached, the bar's pose is set every physics step from the tug pin to the fitting
    /// (kinematic), its colliders become triggers and its joint to the tug is suspended, so the
    /// aircraft's motion never fights the tug's physics.
    /// </summary>
    public class TowBar : MonoBehaviour
    {
        [SerializeField] Rigidbody body;
        [SerializeField] TowCoupler eyeCoupler;
        [SerializeField] Transform head;
        [SerializeField] XRBaseInteractable headHandle;
        [SerializeField] float captureRadius = 0.45f;

        AircraftTowable m_Aircraft;
        Collider[] m_Colliders;
        Vector3 m_EyeLocal;

        public bool EyeCoupled => eyeCoupler != null && eyeCoupler.IsCoupled;
        public Vector3 PinWorld => EyeCoupled ? eyeCoupler.Hitch.PhysicsPosition : eyeCoupler.EyeWorld;
        public VehicleController Tug => EyeCoupled ? eyeCoupler.Hitch.Vehicle : null;
        public AircraftTowable Aircraft => m_Aircraft;

        void Awake() => m_Colliders = GetComponentsInChildren<Collider>(true);

        void OnEnable()
        {
            if (headHandle == null) return;
            headHandle.selectExited.AddListener(OnHeadReleased);
            headHandle.activated.AddListener(OnHeadTrigger);
        }

        void OnDisable()
        {
            if (headHandle == null) return;
            headHandle.selectExited.RemoveListener(OnHeadReleased);
            headHandle.activated.RemoveListener(OnHeadTrigger);
        }

        void OnHeadReleased(SelectExitEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor || m_Aircraft != null) return;
            if (TryAttach()) Haptics.Pulse(args.interactorObject, 0.7f, 0.1f);
        }

        void OnHeadTrigger(ActivateEventArgs args)
        {
            if (m_Aircraft == null) return;
            m_Aircraft.Detach(false);
            Haptics.Pulse(args.interactorObject, 0.4f, 0.05f);
        }

        public bool TryAttach()
        {
            if (!EyeCoupled || head == null) return false;
            foreach (var aircraft in AircraftTowable.All)
            {
                if (aircraft.IsAttached) continue;
                if (Vector3.Distance(aircraft.TowFittingWorld, head.position) <= captureRadius)
                    return aircraft.Attach(this);
            }
            return false;
        }

        internal void OnAttached(AircraftTowable aircraft)
        {
            m_Aircraft = aircraft;
            m_EyeLocal = Quaternion.Inverse(body.rotation) * (eyeCoupler.EyeWorld - body.position);
            eyeCoupler.SuspendJoint(true);
            body.isKinematic = true;
            foreach (var c in m_Colliders) if (c != null) c.isTrigger = true;
            Pose(PinWorld, aircraft.TowFittingWorld);
        }

        internal void OnDetached()
        {
            m_Aircraft = null;
            foreach (var c in m_Colliders) if (c != null) c.isTrigger = false;
            body.isKinematic = false;
            eyeCoupler.SuspendJoint(false);
        }

        /// <summary>Place the bar with its eye on the pin and its head on the fitting.</summary>
        internal void Pose(Vector3 pin, Vector3 fitting)
        {
            var along = fitting - pin;
            if (along.sqrMagnitude < 1e-6f) return;
            var rotation = Quaternion.LookRotation(along, Vector3.up);
            // Keep the eye on the pin (offset from the body origin measured when it latched on).
            body.MovePosition(pin - rotation * m_EyeLocal);
            body.MoveRotation(rotation);
        }
    }
}
