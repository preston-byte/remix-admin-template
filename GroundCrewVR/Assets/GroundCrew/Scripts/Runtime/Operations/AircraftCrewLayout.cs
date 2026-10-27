using GroundCrew.Aircraft;
using UnityEngine;

namespace GroundCrew.Operations
{
    /// <summary>
    /// Where the flight crew go inside each aircraft type, in the aircraft's local frame (Unity:
    /// +z forward, +x right, origin at the nose-gear contact point). Derived from the same
    /// manufacturer figures the aircraft generator uses (pilot design eye point, flight-deck
    /// bulkhead, galley stations).
    /// </summary>
    public struct AircraftCrewLayout
    {
        public float SeatX, SeatZ, BulkheadZ, FwdGalleyZ;

        public static AircraftCrewLayout For(AircraftRig rig)
        {
            var a320 = rig != null && rig.name.Contains("320");
            // z = nose-gear station - station from the nose.
            return a320
                ? new AircraftCrewLayout { SeatX = 0.53f, SeatZ = 5.07f - 2.43f, BulkheadZ = 5.07f - 4.62f, FwdGalleyZ = 5.07f - 6.4f }
                : new AircraftCrewLayout { SeatX = 0.51f, SeatZ = 4.09f - 2.51f, BulkheadZ = 4.09f - 4.35f, FwdGalleyZ = 4.09f - 6.0f };
        }
    }
}
