using GroundCrew.Core;
using UnityEngine;

namespace GroundCrew.Vehicles
{
    // Facts published by the vehicle layer. Turnaround rules and scoring listen to these
    // (speeding near the aircraft, leaving a vehicle without the parking brake, hitting the
    // aircraft, over-steering the nose gear during pushback...).

    public readonly struct VehicleSeatEvent : IGameEvent
    {
        public readonly VehicleController Vehicle;
        public readonly bool Entered;
        /// <summary>On exit: was the parking brake set? (Leaving without it is a safety violation.)</summary>
        public readonly bool ParkingBrakeSet;
        public VehicleSeatEvent(VehicleController vehicle, bool entered, bool parkingBrakeSet)
        {
            Vehicle = vehicle; Entered = entered; ParkingBrakeSet = parkingBrakeSet;
        }
    }

    public readonly struct VehicleCollisionEvent : IGameEvent
    {
        public readonly VehicleController Vehicle;
        public readonly Collider Other;
        public readonly float ImpactSpeed;
        /// <summary>The other collider belongs to an aircraft: always a serious incident.</summary>
        public readonly bool HitAircraft;
        public VehicleCollisionEvent(VehicleController vehicle, Collider other, float impactSpeed, bool hitAircraft)
        {
            Vehicle = vehicle; Other = other; ImpactSpeed = impactSpeed; HitAircraft = hitAircraft;
        }
    }

    public readonly struct CouplingEvent : IGameEvent
    {
        public readonly TowCoupler Coupler;
        public readonly TowHitch Hitch;
        public readonly bool Coupled;
        public CouplingEvent(TowCoupler coupler, TowHitch hitch, bool coupled) { Coupler = coupler; Hitch = hitch; Coupled = coupled; }
    }

    public readonly struct AircraftTowEvent : IGameEvent
    {
        public readonly AircraftTowable Aircraft;
        public readonly bool Attached;
        public AircraftTowEvent(AircraftTowable aircraft, bool attached) { Aircraft = aircraft; Attached = attached; }
    }

    /// <summary>
    /// The nose gear was turned past its towing limit, or the tow bar was overloaded: the
    /// shear pin broke and the tow bar let go - exactly what happens on a real ramp.
    /// </summary>
    public readonly struct TowShearPinEvent : IGameEvent
    {
        public readonly AircraftTowable Aircraft;
        public readonly float NoseGearAngle;
        public TowShearPinEvent(AircraftTowable aircraft, float noseGearAngle) { Aircraft = aircraft; NoseGearAngle = noseGearAngle; }
    }
}
