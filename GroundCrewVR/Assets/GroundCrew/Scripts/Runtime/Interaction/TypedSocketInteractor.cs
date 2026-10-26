using GroundCrew.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace GroundCrew.Interaction
{
    /// <summary>
    /// A receptacle on the aircraft or equipment (GPU receptacle, refuel coupling, bonding
    /// point, water/waste panel fitting, headset jack) that only accepts a matching
    /// <see cref="ConnectorPlug"/>.
    ///
    /// <see cref="Locked"/> blocks new connections without dropping an existing one; interlocks
    /// (access panel closed, aircraft not chocked, fuel bonding missing...) drive it.
    ///
    /// Orientation: the socket's attach transform +Z must point INTO the receptacle; the
    /// plug's attach transform +Z points out of its nose.
    /// </summary>
    public class TypedSocketInteractor : XRSocketInteractor
    {
        [Header("Ground Crew")]
        [SerializeField] ConnectorType acceptedType;
        [SerializeField] string socketName = "Receptacle";
        [SerializeField] bool startLocked;

        public UnityEvent<bool> onConnectionChanged = new UnityEvent<bool>();

        float m_BlockedUntil;

        public ConnectorType AcceptedType => acceptedType;
        public string SocketName => socketName;
        public ConnectorPlug ConnectedPlug { get; private set; }
        public bool IsConnected => ConnectedPlug != null;
        public bool Locked { get; set; }
        public string Id => StableId.Of(this);

        protected override void Awake()
        {
            base.Awake();
            Locked = startLocked;
        }

        bool Accepts(IXRInteractable interactable) =>
            interactable != null &&
            interactable.transform.TryGetComponent(out ConnectorPlug plug) &&
            plug.Type == acceptedType;

        // Never block the plug we already hold: XRI drops a selection as soon as CanSelect
        // returns false for it, which would make every lock eject the cable.
        bool AllowsNew => !Locked && Time.time >= m_BlockedUntil;

        public override bool CanHover(IXRHoverInteractable interactable)
        {
            if (!base.CanHover(interactable) || !Accepts(interactable)) return false;
            return AllowsNew || (interactable is IXRSelectInteractable selectable && IsSelecting(selectable));
        }

        public override bool CanSelect(IXRSelectInteractable interactable)
        {
            if (!base.CanSelect(interactable) || !Accepts(interactable)) return false;
            return AllowsNew || IsSelecting(interactable);
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            ConnectedPlug = args.interactableObject.transform.GetComponent<ConnectorPlug>();
            if (ConnectedPlug != null) ConnectedPlug.NotifyConnected(this);
            onConnectionChanged.Invoke(true);
            EventBus.Publish(new ConnectorEvent(ConnectedPlug, this, true));
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            var plug = ConnectedPlug;
            ConnectedPlug = null;
            if (plug != null) plug.NotifyDisconnected(this);
            onConnectionChanged.Invoke(false);
            EventBus.Publish(new ConnectorEvent(plug, this, false));
        }

        /// <summary>Eject the current plug and refuse new ones for a moment (so it can fall clear).</summary>
        public void ForceRelease(float blockSeconds = 1.5f)
        {
            m_BlockedUntil = Time.time + blockSeconds;
            if (hasSelection && interactionManager != null)
                interactionManager.SelectExit(this, interactablesSelected[0]);
        }
    }
}
