using GroundCrew.Interaction;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace GroundCrew.Player
{
    /// <summary>
    /// Adds a small trigger sphere at each XRI poke point (controller tip / index finger) so
    /// the player can physically press <see cref="PressButton"/>s. Put this on the XR Origin.
    /// </summary>
    public class FingertipSetup : MonoBehaviour
    {
        [SerializeField] float radius = 0.01f;

        void Start()
        {
            var pokes = GetComponentsInChildren<XRPokeInteractor>(true);
            if (pokes.Length == 0)
                Debug.LogWarning("[FingertipSetup] No XRPokeInteractor in rig - physical buttons will not work.", this);

            foreach (var poke in pokes)
                Create(poke.attachTransform != null ? poke.attachTransform : poke.transform, poke.transform);
        }

        void Create(Transform at, Transform hapticSource)
        {
            if (at.GetComponentInChildren<FingertipPresser>() != null) return;

            var go = new GameObject("Fingertip");
            go.layer = at.gameObject.layer;
            go.transform.SetParent(at, false);

            var sphere = go.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = radius;

            // Trigger events need a Rigidbody on one side; kinematic so it just follows the hand.
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            go.AddComponent<FingertipPresser>().HapticSource = hapticSource;
        }
    }
}
