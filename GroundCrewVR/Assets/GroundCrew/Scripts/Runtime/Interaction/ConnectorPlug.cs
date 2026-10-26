using System.Collections.Generic;
using GroundCrew.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace GroundCrew.Interaction
{
    /// <summary>Physical connector standards. A plug only fits a socket of the same type.</summary>
    public enum ConnectorType
    {
        GroundPower400Hz,   // GPU cable: 115/200 V AC 400 Hz, 6-pin
        FuelNozzle,         // single-point pressure refuelling (underwing)
        BondingCable,       // static bonding clip, required before fuelling
        PotableWater,
        WasteService,       // lavatory service panel
        PreconditionedAir,  // PCA hose
        AirStart,           // high-pressure start air
        Interphone,         // headset jack at the nose gear (pushback comms)
    }

    /// <summary>
    /// The plug end of a cable or hose: GPU cable, fuel hose, water hose, PCA hose, headset lead.
    ///
    /// Optionally tethered to an origin (the GPU, the truck reel) with a maximum length.
    /// Pull past it while holding and the plug is yanked out of your hand. If the *equipment*
    /// moves away while the plug is connected, it is ripped out of its socket and a
    /// <see cref="CableOverstretchedEvent"/> with WasConnected=true is raised (a safety
    /// violation once the turnaround rules exist).
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class ConnectorPlug : MonoBehaviour
    {
        [SerializeField] ConnectorType connectorType;
        [SerializeField] string displayName = "Plug";
        [SerializeField, Tooltip("Where the cable starts (on the GPU / reel). Optional.")] Transform tetherOrigin;
        [SerializeField, Tooltip("Where the cable leaves the plug. Defaults to this transform.")] Transform cableEnd;
        [SerializeField] float maxCableLength = 5f;

        XRGrabInteractable m_Grab;
        readonly List<IXRSelectInteractor> m_Scratch = new List<IXRSelectInteractor>();

        public ConnectorType Type => connectorType;
        public string DisplayName => displayName;
        public TypedSocketInteractor ConnectedSocket { get; private set; }
        public bool IsConnected => ConnectedSocket != null;
        public Transform CableEnd => cableEnd;
        public float MaxCableLength => maxCableLength;
        public string Id => StableId.Of(this);

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            if (cableEnd == null) cableEnd = transform;
            if (!TryGetComponent<NearOnlyInteraction>(out _)) gameObject.AddComponent<NearOnlyInteraction>();

            m_Grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            m_Grab.throwOnDetach = false; // a dropped cable falls, it doesn't fly
        }

        internal void NotifyConnected(TypedSocketInteractor socket) => ConnectedSocket = socket;

        internal void NotifyDisconnected(TypedSocketInteractor socket)
        {
            if (ConnectedSocket == socket) ConnectedSocket = null;
        }

        void Update()
        {
            if (tetherOrigin == null || !m_Grab.isSelected) return;
            if (Vector3.Distance(tetherOrigin.position, cableEnd.position) <= maxCableLength) return;

            m_Scratch.Clear();
            m_Scratch.AddRange(m_Grab.interactorsSelecting);
            foreach (var interactor in m_Scratch)
            {
                if (interactor is TypedSocketInteractor socket)
                {
                    socket.ForceRelease();
                    EventBus.Publish(new CableOverstretchedEvent(this, true));
                }
                else
                {
                    Haptics.Pulse(interactor, 0.8f, 0.15f);
                    if (m_Grab.interactionManager != null)
                        m_Grab.interactionManager.SelectExit(interactor, m_Grab);
                    EventBus.Publish(new CableOverstretchedEvent(this, false));
                }
            }
        }
    }
}
