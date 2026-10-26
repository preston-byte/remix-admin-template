using System.Collections.Generic;
using GroundCrew.Interaction;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// A steering wheel you grab (grip) with one or both hands and turn. The wheel follows the
    /// hands' rotation around the column; several turns lock to lock, like the real vehicle.
    ///
    /// The hands don't attach to the wheel (XRSimpleInteractable): the wheel turns under them,
    /// which avoids the "hand glued to a rotating object" feeling. Letting go leaves the wheel
    /// where it is; <see cref="VehicleDriverInput"/> uses the thumbstick instead while no hand
    /// is on it, and turns this wheel to match.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class SteeringWheelInput : MonoBehaviour
    {
        [SerializeField, Tooltip("The rotating wheel. Defaults to this transform.")] Transform wheel;
        [SerializeField, Tooltip("Point on the column axis on the driver's side of the hub (generator node 'SteerAxis').")] Transform axisHint;
        [SerializeField, Tooltip("Wheel rotation at full lock, degrees (450 = 1.25 turns each way).")] float maxWheelAngle = 450f;
        [SerializeField] float breakDistance = 0.35f;

        XRSimpleInteractable m_Interactable;
        readonly List<IXRSelectInteractor> m_Hands = new List<IXRSelectInteractor>();
        readonly Dictionary<IXRSelectInteractor, Vector3> m_Previous = new Dictionary<IXRSelectInteractor, Vector3>();
        Quaternion m_RestLocal;
        Vector3 m_AxisLocal;
        int m_LastTick;

        /// <summary>Wheel angle in degrees; positive = clockwise as seen by the driver (steer right).</summary>
        public float Angle { get; private set; }
        public float Steer => maxWheelAngle > 0f ? Angle / maxWheelAngle : 0f;
        public bool IsHeld => m_Hands.Count > 0;

        Vector3 Axis => wheel.parent != null ? wheel.parent.TransformDirection(m_AxisLocal) : m_AxisLocal;

        void Awake()
        {
            m_Interactable = GetComponent<XRSimpleInteractable>();
            if (wheel == null) wheel = transform;
            m_RestLocal = wheel.localRotation;
            var axisWorld = axisHint != null ? (axisHint.position - wheel.position).normalized : -wheel.forward;
            m_AxisLocal = wheel.parent != null ? wheel.parent.InverseTransformDirection(axisWorld) : axisWorld;
            if (!TryGetComponent<NearOnlyInteraction>(out _)) gameObject.AddComponent<NearOnlyInteraction>();
        }

        void OnEnable()
        {
            m_Interactable.selectEntered.AddListener(OnGrab);
            m_Interactable.selectExited.AddListener(OnRelease);
        }

        void OnDisable()
        {
            m_Interactable.selectEntered.RemoveListener(OnGrab);
            m_Interactable.selectExited.RemoveListener(OnRelease);
            m_Hands.Clear();
            m_Previous.Clear();
        }

        void OnGrab(SelectEnterEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor) return;
            var hand = args.interactorObject;
            if (!m_Hands.Contains(hand)) m_Hands.Add(hand);
            m_Previous[hand] = HandVector(hand);
            Haptics.Pulse(hand, 0.2f, 0.03f);
        }

        void OnRelease(SelectExitEventArgs args)
        {
            m_Hands.Remove(args.interactorObject);
            m_Previous.Remove(args.interactorObject);
        }

        Vector3 HandVector(IXRSelectInteractor hand) =>
            Vector3.ProjectOnPlane(hand.transform.position - wheel.position, Axis);

        void Update()
        {
            if (m_Hands.Count == 0) return;
            var axis = Axis;
            var sum = 0f;
            var count = 0;
            for (var i = m_Hands.Count - 1; i >= 0; i--)
            {
                var hand = m_Hands[i];
                if (Vector3.Distance(hand.transform.position, wheel.position) > breakDistance)
                {
                    var manager = m_Interactable.interactionManager;
                    m_Hands.RemoveAt(i);
                    m_Previous.Remove(hand);
                    if (manager != null) manager.SelectExit(hand, m_Interactable);
                    continue;
                }
                var current = HandVector(hand);
                if (current.sqrMagnitude > 1e-5f && m_Previous[hand].sqrMagnitude > 1e-5f)
                {
                    // Positive SignedAngle about an axis pointing at the driver looks anticlockwise to them.
                    sum += -Vector3.SignedAngle(m_Previous[hand], current, axis);
                    count++;
                }
                m_Previous[hand] = current;
            }
            if (count > 0) SetAngle(Angle + sum / count);
        }

        /// <summary>Turn the wheel to show a steering value set by the thumbstick (only while nobody holds it).</summary>
        public void ShowSteer(float steer)
        {
            if (IsHeld) return;
            SetAngle(Mathf.MoveTowards(Angle, Mathf.Clamp(steer, -1f, 1f) * maxWheelAngle, 900f * Time.deltaTime));
        }

        void SetAngle(float angle)
        {
            Angle = Mathf.Clamp(angle, -maxWheelAngle, maxWheelAngle);
            wheel.localRotation = Quaternion.AngleAxis(-Angle, m_AxisLocal) * m_RestLocal;
            var tick = Mathf.FloorToInt(Angle / 30f);
            if (tick != m_LastTick)
            {
                m_LastTick = tick;
                foreach (var hand in m_Hands) Haptics.Pulse(hand, 0.06f, 0.01f);
            }
        }
    }
}
