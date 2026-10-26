using GroundCrew.Core;

namespace GroundCrew.Interaction
{
    // Facts published by the interaction layer. Gameplay systems (turnaround rules, scoring,
    // radio, wrist tablet) listen to these; interaction components never call gameplay code.
    // Each event carries the component reference (for local use) plus EntityIds (for save
    // data / networking later).

    public readonly struct ItemGrabbedEvent : IGameEvent
    {
        public readonly GrabbableItem Item;
        public readonly int HandCount;
        public ItemGrabbedEvent(GrabbableItem item, int handCount) { Item = item; HandCount = handCount; }
    }

    public readonly struct ItemReleasedEvent : IGameEvent
    {
        public readonly GrabbableItem Item;
        public ItemReleasedEvent(GrabbableItem item) { Item = item; }
    }

    public readonly struct ItemDamagedEvent : IGameEvent
    {
        public readonly GrabbableItem Item;
        public readonly float ImpactSpeed;
        public readonly float TotalDamage01;
        public ItemDamagedEvent(GrabbableItem item, float impactSpeed, float totalDamage01)
        {
            Item = item; ImpactSpeed = impactSpeed; TotalDamage01 = totalDamage01;
        }
    }

    public readonly struct ZoneItemPlacedEvent : IGameEvent
    {
        public readonly PlacementZone Zone;
        public readonly GrabbableItem Item;
        public readonly bool Accepted;
        public ZoneItemPlacedEvent(PlacementZone zone, GrabbableItem item, bool accepted)
        {
            Zone = zone; Item = item; Accepted = accepted;
        }
    }

    public readonly struct ZoneItemRemovedEvent : IGameEvent
    {
        public readonly PlacementZone Zone;
        public readonly GrabbableItem Item;
        public ZoneItemRemovedEvent(PlacementZone zone, GrabbableItem item) { Zone = zone; Item = item; }
    }

    public readonly struct ZoneCapacityEvent : IGameEvent
    {
        public readonly PlacementZone Zone;
        public readonly bool OverCapacity;
        public ZoneCapacityEvent(PlacementZone zone, bool overCapacity) { Zone = zone; OverCapacity = overCapacity; }
    }

    public readonly struct ConnectorEvent : IGameEvent
    {
        public readonly ConnectorPlug Plug;
        public readonly TypedSocketInteractor Socket;
        public readonly bool Connected;
        public ConnectorEvent(ConnectorPlug plug, TypedSocketInteractor socket, bool connected)
        {
            Plug = plug; Socket = socket; Connected = connected;
        }
    }

    /// <summary>
    /// A tethered plug was pulled past its cable length. <see cref="WasConnected"/> means the
    /// equipment (or aircraft) moved while still connected - a safety violation in gameplay.
    /// </summary>
    public readonly struct CableOverstretchedEvent : IGameEvent
    {
        public readonly ConnectorPlug Plug;
        public readonly bool WasConnected;
        public CableOverstretchedEvent(ConnectorPlug plug, bool wasConnected) { Plug = plug; WasConnected = wasConnected; }
    }

    public readonly struct ButtonPressedEvent : IGameEvent
    {
        public readonly PressButton Button;
        public readonly bool IsOn;
        public ButtonPressedEvent(PressButton button, bool isOn) { Button = button; IsOn = isOn; }
    }

    /// <summary>A door/lever/panel reached its fully-open (AtMax) or fully-closed (min) limit.</summary>
    public readonly struct HandleLimitEvent : IGameEvent
    {
        public readonly RotaryHandle Handle;
        public readonly bool AtMax;
        public HandleLimitEvent(RotaryHandle handle, bool atMax) { Handle = handle; AtMax = atMax; }
    }
}
