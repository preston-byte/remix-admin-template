using GroundCrew.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace GroundCrew.Interaction
{
    /// <summary>
    /// A physical, hand-held item: suitcase, cone, wheel chock, catering box...
    ///
    /// Builds on XRI's XRGrabInteractable and adds what a ground-ops sim needs:
    ///  - mass from <see cref="ItemDefinition"/>, which also makes heavy items lag behind the hand
    ///  - heavy items need two hands, or they crawl
    ///  - near-only grabbing (no force-grabbing bags from across the apron)
    ///  - impact damage (throwing a fragile box into the hold is penalised later)
    ///  - grab/release/damage events for gameplay systems
    ///
    /// Uses VelocityTracking so held items collide with the world instead of clipping
    /// through hold walls and aircraft skin.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(XRGrabInteractable))]
    public class GrabbableItem : MonoBehaviour
    {
        [SerializeField] ItemDefinition definition;

        [SerializeField, Tooltip("Allow grabbing with the far ray. Off for almost everything physical.")]
        bool allowDistanceGrab;

        [SerializeField, Tooltip("Velocity scale while a two-hand item is held with only one hand.")]
        float oneHandedHeavyScale = 0.2f;

        Rigidbody m_Body;
        XRGrabInteractable m_Grab;
        float m_Heaviness;
        float m_LastImpactTime = -10f;

        public ItemDefinition Definition => definition;
        public string DisplayName => definition != null ? definition.displayName : name;
        public ItemCategory Category => definition != null ? definition.category : ItemCategory.None;
        public float MassKg => definition != null ? definition.massKg : (m_Body != null ? m_Body.mass : 0f);
        public bool RequiresTwoHands => definition != null && definition.requiresTwoHands;
        public float Damage01 { get; private set; }

        /// <summary>Number of hands (not sockets) currently holding the item.</summary>
        public int HandCount { get; private set; }
        public bool IsHeld => HandCount > 0;

        public Rigidbody Body => m_Body;
        public XRGrabInteractable Grab => m_Grab;
        public string Id => StableId.Of(this);

        void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
            m_Grab = GetComponent<XRGrabInteractable>();

            if (!allowDistanceGrab && !TryGetComponent<NearOnlyInteraction>(out _))
                gameObject.AddComponent<NearOnlyInteraction>();

            ApplyDefinition();
        }

        void OnEnable()
        {
            m_Grab.selectEntered.AddListener(OnSelectEntered);
            m_Grab.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            m_Grab.selectEntered.RemoveListener(OnSelectEntered);
            m_Grab.selectExited.RemoveListener(OnSelectExited);
        }

        void ApplyDefinition()
        {
            var mass = Mathf.Max(0.1f, definition != null ? definition.massKg : m_Body.mass);
            m_Body.mass = mass;
            m_Body.interpolation = RigidbodyInterpolation.Interpolate;
            m_Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // 0 = feather-light, 1 = 32 kg (max checked-bag weight with a HEAVY tag).
            m_Heaviness = Mathf.InverseLerp(2f, 32f, mass);

            m_Grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            m_Grab.smoothPosition = true;
            m_Grab.smoothPositionAmount = Mathf.Lerp(20f, 6f, m_Heaviness);
            m_Grab.smoothRotation = true;
            m_Grab.smoothRotationAmount = Mathf.Lerp(20f, 5f, m_Heaviness);
            m_Grab.throwOnDetach = true;
            m_Grab.throwVelocityScale = Mathf.Lerp(1.2f, 0.4f, m_Heaviness);
            m_Grab.throwAngularVelocityScale = Mathf.Lerp(1f, 0.3f, m_Heaviness);

            if (RequiresTwoHands)
                m_Grab.selectMode = InteractableSelectMode.Multiple;

            UpdateHandling();
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor) return;

            HandCount++;
            UpdateHandling();
            Haptics.Pulse(args.interactorObject, Mathf.Lerp(0.2f, 0.7f, m_Heaviness), 0.06f);
            EventBus.Publish(new ItemGrabbedEvent(this, HandCount));
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor) return;

            HandCount = Mathf.Max(0, HandCount - 1);
            UpdateHandling();
            if (HandCount == 0)
                EventBus.Publish(new ItemReleasedEvent(this));
            else
                EventBus.Publish(new ItemGrabbedEvent(this, HandCount));
        }

        void UpdateHandling()
        {
            if (!RequiresTwoHands) return;
            // One hand on a 32 kg bag: it follows, but very reluctantly. Two hands: normal.
            m_Grab.velocityScale = HandCount >= 2 ? 1f : oneHandedHeavyScale;
            m_Grab.angularVelocityScale = HandCount >= 2 ? 1f : oneHandedHeavyScale;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (definition == null) return;

            var speed = collision.relativeVelocity.magnitude;
            if (speed < definition.impactDamageSpeed) return;
            if (Time.time - m_LastImpactTime < 0.25f) return; // one bounce = one impact
            m_LastImpactTime = Time.time;

            var damage = (speed - definition.impactDamageSpeed) * (definition.fragile ? 0.35f : 0.12f);
            Damage01 = Mathf.Clamp01(Damage01 + damage);
            EventBus.Publish(new ItemDamagedEvent(this, speed, Damage01));
        }
    }
}
