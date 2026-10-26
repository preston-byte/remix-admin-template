using System.Collections.Generic;
using GroundCrew.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace GroundCrew.Interaction
{
    /// <summary>
    /// A trigger volume that knows which items have been *placed* in it: inside, released by
    /// every hand, and at rest. Not a snap socket: items keep full physics, so bags really
    /// stack in a hold and chocks really sit against a tyre.
    ///
    /// Used for cargo holds (weight limit), baggage carts, chock positions (one chock, chocks
    /// only), cone positions, catering cart bays...
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PlacementZone : MonoBehaviour
    {
        [SerializeField] string zoneName = "Zone";

        [SerializeField, Tooltip("Categories that belong here. Others are still tracked (they weigh something!) but reported as rejected.")]
        ItemCategory acceptedCategories = (ItemCategory)~0;

        [SerializeField, Min(0f), Tooltip("0 = no mass limit.")]
        float maxMassKg = 500f;

        [SerializeField, Min(0), Tooltip("0 = no item-count limit.")]
        int maxItems;

        [SerializeField, Tooltip("Below this speed (m/s) an item counts as resting.")]
        float settleSpeed = 0.15f;

        [SerializeField, Tooltip("How long an item must rest before it counts as placed (s).")]
        float settleTime = 0.35f;

        [SerializeField, Tooltip("Optional world-space label showing load / limit.")]
        TMP_Text label;

        public UnityEvent<float> onMassChanged = new UnityEvent<float>();
        public UnityEvent<bool> onOverCapacityChanged = new UnityEvent<bool>();

        readonly Dictionary<GrabbableItem, int> m_Overlaps = new Dictionary<GrabbableItem, int>();
        readonly Dictionary<GrabbableItem, float> m_Settling = new Dictionary<GrabbableItem, float>();
        readonly HashSet<GrabbableItem> m_Placed = new HashSet<GrabbableItem>();
        readonly List<GrabbableItem> m_Scratch = new List<GrabbableItem>();

        public string ZoneName => zoneName;
        public float MaxMassKg => maxMassKg;
        public float PlacedMassKg { get; private set; }
        public int PlacedCount => m_Placed.Count;
        public int RejectedCount { get; private set; }
        public bool OverCapacity { get; private set; }
        public IReadOnlyCollection<GrabbableItem> PlacedItems => m_Placed;
        public string Id => EntityId.Of(this);

        void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        void Start() => Recalculate();

        public bool Accepts(GrabbableItem item) => item != null && (acceptedCategories & item.Category) != 0;

        void OnTriggerEnter(Collider other)
        {
            var item = ItemFrom(other);
            if (item == null) return;
            m_Overlaps[item] = m_Overlaps.TryGetValue(item, out var count) ? count + 1 : 1;
        }

        void OnTriggerExit(Collider other)
        {
            var item = ItemFrom(other);
            if (item == null || !m_Overlaps.TryGetValue(item, out var count)) return;

            if (count > 1)
            {
                m_Overlaps[item] = count - 1;
                return;
            }

            m_Overlaps.Remove(item);
            m_Settling.Remove(item);
            if (m_Placed.Remove(item)) OnRemoved(item);
        }

        static GrabbableItem ItemFrom(Collider other)
        {
            var body = other.attachedRigidbody;
            return body != null ? body.GetComponent<GrabbableItem>() : null;
        }

        void Update()
        {
            if (m_Overlaps.Count == 0) return;

            // Destroyed/disabled items never send OnTriggerExit.
            m_Scratch.Clear();
            foreach (var item in m_Overlaps.Keys)
                if (item == null || !item.isActiveAndEnabled) m_Scratch.Add(item);
            foreach (var gone in m_Scratch)
            {
                m_Overlaps.Remove(gone);
                m_Settling.Remove(gone);
                if (m_Placed.Remove(gone)) OnRemoved(gone);
            }

            m_Scratch.Clear();
            m_Scratch.AddRange(m_Overlaps.Keys);
            foreach (var item in m_Scratch)
            {
                if (m_Placed.Contains(item))
                {
                    // Picked back up out of the zone.
                    if (item.IsHeld)
                    {
                        m_Placed.Remove(item);
                        OnRemoved(item);
                    }
                    continue;
                }

                var body = item.Body;
                var resting = !item.IsHeld &&
                              (body.IsSleeping() || body.linearVelocity.sqrMagnitude < settleSpeed * settleSpeed);
                if (!resting)
                {
                    m_Settling.Remove(item);
                    continue;
                }

                var t = (m_Settling.TryGetValue(item, out var soFar) ? soFar : 0f) + Time.deltaTime;
                if (t < settleTime)
                {
                    m_Settling[item] = t;
                    continue;
                }

                m_Settling.Remove(item);
                m_Placed.Add(item);
                OnPlaced(item);
            }
        }

        void OnPlaced(GrabbableItem item)
        {
            Recalculate();
            EventBus.Publish(new ZoneItemPlacedEvent(this, item, Accepts(item)));
        }

        void OnRemoved(GrabbableItem item)
        {
            Recalculate();
            EventBus.Publish(new ZoneItemRemovedEvent(this, item));
        }

        void Recalculate()
        {
            var mass = 0f;
            var rejected = 0;
            foreach (var item in m_Placed)
            {
                if (item == null) continue;
                mass += item.MassKg;
                if (!Accepts(item)) rejected++;
            }

            PlacedMassKg = mass;
            RejectedCount = rejected;
            onMassChanged.Invoke(mass);

            var over = (maxMassKg > 0f && mass > maxMassKg) || (maxItems > 0 && m_Placed.Count > maxItems);
            if (over != OverCapacity)
            {
                OverCapacity = over;
                onOverCapacityChanged.Invoke(over);
                EventBus.Publish(new ZoneCapacityEvent(this, over));
            }

            if (label != null)
            {
                var limit = maxMassKg > 0f ? $" / {maxMassKg:0} kg" : " kg";
                label.text = $"{zoneName}\n{mass:0.0}{limit}" + (maxItems > 0 ? $"  ({m_Placed.Count}/{maxItems})" : "");
                label.color = OverCapacity ? new Color(1f, 0.3f, 0.25f) : Color.white;
            }
        }
    }
}
