using System;
using GroundCrew.Core;
using UnityEngine;
using UnityEngine.Events;

namespace GroundCrew.Interaction
{
    /// <summary>
    /// A physical push button pressed by touching it with a fingertip: jet-bridge controls,
    /// GPU start/stop, fuel deadman, catering-truck lift, wrist-tablet buttons...
    ///
    /// Deliberately not an XRI interactable: a trigger collider + the fingertip sphere is
    /// simpler, cheap and works the same for controllers and (later) tracked hands.
    /// The trigger volume should stick out a little in front of the cap.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PressButton : MonoBehaviour
    {
        [SerializeField] string label = "Button";
        [SerializeField, Tooltip("Visual part that moves when pressed.")] Transform movingCap;
        [SerializeField, Tooltip("Local direction the cap travels when pressed.")] Vector3 pressDirection = Vector3.forward;
        [SerializeField] float pressDepth = 0.006f;
        [SerializeField, Tooltip("Ignore repeat touches for this long (s).")] float cooldown = 0.35f;
        [SerializeField, Tooltip("Latching on/off button instead of momentary.")] bool toggle;
        [SerializeField] bool startOn;

        public UnityEvent onPressed = new UnityEvent();
        public UnityEvent<bool> onToggled = new UnityEvent<bool>();

        /// <summary>C# event for code-wired listeners (e.g. the wrist tablet).</summary>
        public event Action<PressButton> Pressed;

        Vector3 m_CapRest;
        float m_LastPress = -10f;
        float m_Anim;

        public string Label => label;
        public bool IsOn { get; private set; }

        void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        void Awake()
        {
            if (movingCap != null) m_CapRest = movingCap.localPosition;
            IsOn = startOn;
        }

        void Start()
        {
            // Push the initial state to whatever this button drives (beacon, indicator...).
            if (toggle) onToggled.Invoke(IsOn);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent(out FingertipPresser fingertip))
                Press(fingertip.HapticSource);
        }

        public void Press(Transform hapticSource = null)
        {
            if (Time.time - m_LastPress < cooldown) return;
            m_LastPress = Time.time;

            if (toggle) IsOn = !IsOn;
            m_Anim = 1f;
            Haptics.Pulse(hapticSource, 0.45f, 0.03f);

            onPressed.Invoke();
            if (toggle) onToggled.Invoke(IsOn);
            Pressed?.Invoke(this);
            EventBus.Publish(new ButtonPressedEvent(this, IsOn));
        }

        void Update()
        {
            if (movingCap == null) return;
            m_Anim = Mathf.MoveTowards(m_Anim, 0f, Time.deltaTime / 0.15f);
            // A latched toggle button stays half-way in, like a real push-on/push-off switch.
            var depth = Mathf.Max(m_Anim, toggle && IsOn ? 0.5f : 0f);
            movingCap.localPosition = m_CapRest + pressDirection.normalized * (pressDepth * depth);
        }
    }
}
