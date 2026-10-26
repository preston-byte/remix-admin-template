using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace GroundCrew.Interaction
{
    /// <summary>
    /// Prevents "Force grab" from a distance. The Starter Assets Near-Far Interactor can grab
    /// anything its ray touches, which is great for menus but wrong for a ground-ops sim: you
    /// should have to walk to the bag, reach the door handle, stand at the receptacle.
    ///
    /// Works as an XRI select + hover filter: the interaction is only allowed when the hand
    /// is within <see cref="maxDistance"/> of one of the interactable's colliders.
    /// Sockets (and poke) are always allowed.
    /// </summary>
    [DisallowMultipleComponent]
    public class NearOnlyInteraction : MonoBehaviour, IXRSelectFilter, IXRHoverFilter
    {
        [SerializeField, Tooltip("Max distance from the controller to the object's surface (m).")]
        float maxDistance = 0.3f;

        XRBaseInteractable m_Interactable;

        public bool canProcess => isActiveAndEnabled;

        void Awake()
        {
            m_Interactable = GetComponent<XRBaseInteractable>();
            if (m_Interactable == null)
                Debug.LogWarning($"[NearOnlyInteraction] {name} has no XR interactable.", this);
        }

        void OnEnable()
        {
            if (m_Interactable == null) return;
            m_Interactable.selectFilters.Add(this);
            m_Interactable.hoverFilters.Add(this);
        }

        void OnDisable()
        {
            if (m_Interactable == null) return;
            m_Interactable.selectFilters.Remove(this);
            m_Interactable.hoverFilters.Remove(this);
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) => IsAllowed(interactor);

        public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable interactable) => IsAllowed(interactor);

        bool IsAllowed(IXRInteractor interactor)
        {
            if (interactor is XRSocketInteractor || interactor is XRPokeInteractor) return true;

            var handPosition = interactor.transform.position;
            var bestSqr = float.MaxValue;
            foreach (var col in m_Interactable.colliders)
            {
                if (col == null || !col.enabled) continue;
                // ClosestPoint is undefined for non-convex mesh colliders; bounds is a safe fallback.
                var closest = col is MeshCollider mesh && !mesh.convex
                    ? col.bounds.ClosestPoint(handPosition)
                    : col.ClosestPoint(handPosition);
                bestSqr = Mathf.Min(bestSqr, (closest - handPosition).sqrMagnitude);
            }
            return bestSqr <= maxDistance * maxDistance;
        }
    }
}
