using GroundCrew.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace GroundCrew.Interaction
{
    /// <summary>
    /// Anything that rotates about a hinge when you grab and move it: cargo doors, access
    /// panels, passenger doors, levers, valve handles, tow-bar locks.
    ///
    /// Put this on the grabbable handle (with an XRSimpleInteractable and a collider) and
    /// point <see cref="pivot"/> at the hinge transform the handle is parented under. The hand's
    /// motion around the hinge axis drives the angle 1:1, clamped to [min, max] and to
    /// <see cref="maxSpeed"/> so heavy doors feel heavy.
    ///
    /// The angle is not physics-driven on purpose: hinged physics joints jitter badly on
    /// Quest at 72 Hz and are hard to make deterministic for networking. The door carries a
    /// kinematic Rigidbody so it still pushes bags out of the way.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class RotaryHandle : MonoBehaviour
    {
        [SerializeField, Tooltip("Shown in messages, e.g. 'FWD cargo door'. Leave empty for silent levers.")]
        string handleName;

        [SerializeField, Tooltip("The hinge. Defaults to this transform's parent.")]
        Transform pivot;

        [SerializeField, Tooltip("Hinge axis in the pivot's local space.")]
        Vector3 localAxis = Vector3.right;

        [SerializeField] float minAngle;
        [SerializeField] float maxAngle = 90f;
        [SerializeField] float startAngle;

        [SerializeField, Tooltip("Degrees per second the handle can follow the hand.")]
        float maxSpeed = 360f;

        [SerializeField, Tooltip("Spring back to the rest angle when released (deadman levers).")]
        bool returnToRest;
        [SerializeField] float restAngle;
        [SerializeField, Tooltip("Speed (deg/s) of spring return and strut assist.")] float returnSpeed = 120f;

        [SerializeField, Tooltip("Gas-strut / counterbalance assist: released above this angle, it swings fully open by itself. Negative = off.")]
        float assistOpenAbove = -1f;

        [SerializeField, Tooltip("Haptic tick every N degrees while moving. 0 = off.")]
        float detentStep = 10f;

        [SerializeField, Tooltip("Hand further than this from the handle (m) lets go.")]
        float breakDistance = 0.45f;

        public UnityEvent<float> onValueChanged = new UnityEvent<float>();
        public UnityEvent onReachedMax = new UnityEvent();
        public UnityEvent onReachedMin = new UnityEvent();

        const float k_EndTolerance = 1.5f;

        XRSimpleInteractable m_Interactable;
        IXRSelectInteractor m_Interactor;
        Quaternion m_BaseRotation;
        float m_GrabStartAngle;
        Vector3 m_GrabStartVector;
        int m_LastDetent;
        bool m_AtMax, m_AtMin;

        public string HandleName => handleName;
        public float Angle { get; private set; }
        public float Value01 => Mathf.InverseLerp(minAngle, maxAngle, Angle);
        public bool IsHeld => m_Interactor != null;
        public bool IsFullyOpen => m_AtMax;
        public bool IsFullyClosed => m_AtMin;
        public string Id => EntityId.Of(this);

        /// <summary>Locked handles cannot be moved by hand (e.g. panel with a cable plugged in).</summary>
        public bool Locked { get; set; }

        Vector3 WorldAxis => pivot.TransformDirection(localAxis.normalized);

        void Awake()
        {
            m_Interactable = GetComponent<XRSimpleInteractable>();
            if (pivot == null) pivot = transform.parent != null ? transform.parent : transform;
            if (!TryGetComponent<NearOnlyInteraction>(out _)) gameObject.AddComponent<NearOnlyInteraction>();

            m_BaseRotation = pivot.localRotation;
            Angle = Mathf.Clamp(startAngle, minAngle, maxAngle);
            ApplyRotation();
            m_AtMax = Angle >= maxAngle - k_EndTolerance;
            m_AtMin = Angle <= minAngle + k_EndTolerance;
        }

        void OnEnable()
        {
            m_Interactable.selectEntered.AddListener(OnSelectEntered);
            m_Interactable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
            m_Interactable.selectExited.RemoveListener(OnSelectExited);
            m_Interactor = null;
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor) return;
            m_Interactor = args.interactorObject;
            m_GrabStartAngle = Angle;
            m_GrabStartVector = HandVector();
            m_LastDetent = Detent(Angle);
            Haptics.Pulse(m_Interactor, 0.25f, 0.03f);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (args.interactorObject == m_Interactor) m_Interactor = null;
        }

        Vector3 HandVector() =>
            Vector3.ProjectOnPlane(m_Interactor.transform.position - pivot.position, WorldAxis);

        void Update()
        {
            var target = Angle;
            if (m_Interactor != null)
            {
                if (Vector3.Distance(m_Interactor.transform.position, transform.position) > breakDistance)
                {
                    // Hand slipped off the handle.
                    var manager = m_Interactable.interactionManager;
                    var interactor = m_Interactor;
                    m_Interactor = null;
                    if (manager != null) manager.SelectExit(interactor, m_Interactable);
                    return;
                }

                if (!Locked)
                {
                    var current = HandVector();
                    if (current.sqrMagnitude > 1e-6f && m_GrabStartVector.sqrMagnitude > 1e-6f)
                        target = m_GrabStartAngle + Vector3.SignedAngle(m_GrabStartVector, current, WorldAxis);
                }
            }
            else if (returnToRest)
            {
                target = restAngle;
            }
            else if (assistOpenAbove >= 0f && Angle >= assistOpenAbove)
            {
                target = maxAngle;
            }

            target = Mathf.Clamp(target, minAngle, maxAngle);
            var speed = m_Interactor != null ? maxSpeed : returnSpeed;
            var next = Mathf.MoveTowards(Angle, target, speed * Time.deltaTime);
            if (!Mathf.Approximately(next, Angle)) SetAngleInternal(next);
        }

        /// <summary>Drive the handle from code (e.g. hydraulic A320 cargo door, AI crew closing a door).</summary>
        public void SetAngle(float angle) => SetAngleInternal(Mathf.Clamp(angle, minAngle, maxAngle));

        void SetAngleInternal(float angle)
        {
            Angle = angle;
            ApplyRotation();
            onValueChanged.Invoke(Value01);

            if (m_Interactor != null && detentStep > 0f)
            {
                var detent = Detent(angle);
                if (detent != m_LastDetent)
                {
                    m_LastDetent = detent;
                    Haptics.Pulse(m_Interactor, 0.08f, 0.015f);
                }
            }

            var atMax = angle >= maxAngle - k_EndTolerance;
            var atMin = angle <= minAngle + k_EndTolerance;
            if (atMax && !m_AtMax) OnLimit(true);
            if (atMin && !m_AtMin) OnLimit(false);
            m_AtMax = atMax;
            m_AtMin = atMin;
        }

        void OnLimit(bool max)
        {
            Haptics.Pulse(m_Interactor, 0.5f, 0.05f);
            if (max) onReachedMax.Invoke();
            else onReachedMin.Invoke();
            if (!string.IsNullOrEmpty(handleName))
                EventBus.Publish(new HandleLimitEvent(this, max));
        }

        int Detent(float angle) => detentStep > 0f ? Mathf.FloorToInt(angle / detentStep) : 0;

        void ApplyRotation()
        {
            pivot.localRotation = m_BaseRotation * Quaternion.AngleAxis(Angle, localAxis.normalized);
        }
    }
}
