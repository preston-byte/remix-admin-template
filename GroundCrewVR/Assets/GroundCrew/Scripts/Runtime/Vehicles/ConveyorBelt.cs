using GroundCrew.Interaction;
using UnityEngine;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// The belt loader's conveyor. Bags (any loose Rigidbody) lying on the belt are carried along
    /// it at <see cref="speed"/>, up into the hold (IN) or down to the ground (OUT). Bags someone is
    /// holding are left alone. The belt only runs with the vehicle's engine running.
    ///
    /// Put this on a trigger box covering the belt surface (a child of the tilting belt).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class ConveyorBelt : MonoBehaviour
    {
        [SerializeField, Tooltip("The belt; its forward axis points to the aircraft end.")] Transform belt;
        [SerializeField, Tooltip("m/s")] float speed = 0.55f;
        [SerializeField] PressButton inButton;
        [SerializeField] PressButton outButton;
        [SerializeField] PressButton stopButton;
        [SerializeField] VehicleController vehicle;

        /// <summary>+1 towards the aircraft, -1 towards the ground, 0 stopped.</summary>
        public int Direction { get; private set; }

        void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            if (belt == null) belt = transform.parent != null ? transform.parent : transform;
        }

        void OnEnable()
        {
            if (inButton != null) inButton.Pressed += OnIn;
            if (outButton != null) outButton.Pressed += OnOut;
            if (stopButton != null) stopButton.Pressed += OnStop;
        }

        void OnDisable()
        {
            if (inButton != null) inButton.Pressed -= OnIn;
            if (outButton != null) outButton.Pressed -= OnOut;
            if (stopButton != null) stopButton.Pressed -= OnStop;
        }

        void OnIn(PressButton _) => Direction = 1;
        void OnOut(PressButton _) => Direction = -1;
        void OnStop(PressButton _) => Direction = 0;
        public void Run(int direction) => Direction = Mathf.Clamp(direction, -1, 1);

        void Update()
        {
            if (vehicle != null && !vehicle.EngineRunning) Direction = 0;
        }

        void OnTriggerStay(Collider other)
        {
            if (Direction == 0) return;
            var rb = other.attachedRigidbody;
            if (rb == null || rb.isKinematic || rb.GetComponent<VehicleController>() != null) return;
            if (rb.TryGetComponent(out GrabbableItem item) && item.IsHeld) return;

            var along = belt.forward;
            var v = rb.linearVelocity;
            var current = Vector3.Dot(v, along);
            var lateral = v - along * current - Vector3.up * Vector3.Dot(v, Vector3.up);
            // Ease towards belt speed (bags don't snap to it) and damp sideways drift.
            v += along * ((speed * Direction - current) * 0.35f) - lateral * 0.3f;
            rb.linearVelocity = v;
        }
    }
}
