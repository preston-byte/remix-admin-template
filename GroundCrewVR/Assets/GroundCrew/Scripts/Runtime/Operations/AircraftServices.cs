using System.Collections.Generic;
using GroundCrew.Aircraft;
using UnityEngine;

namespace GroundCrew.Operations
{
    /// <summary>
    /// What has been done to one aircraft on the ground: power, fuel, water, waste, catering,
    /// bags in the hold, passengers aboard. Added to the aircraft on first use; shown on the iPad.
    /// Quantities are approximate for the class (APPROX).
    /// </summary>
    public class AircraftServices : MonoBehaviour
    {
        public bool ExternalPower;
        public float FuelKg;
        public float FuelCapacityKg;
        public float FuelTargetKg;
        [Range(0f, 1f)] public float Water01 = 0.2f;
        [Range(0f, 1f)] public float Waste01 = 0.7f;
        public bool CateringDone;
        public readonly List<GameObject> HoldBags = new List<GameObject>();
        public int Passengers;

        public static AircraftServices For(AircraftRig rig)
        {
            if (rig == null) return null;
            var s = rig.GetComponent<AircraftServices>();
            if (s != null) return s;
            s = rig.gameObject.AddComponent<AircraftServices>();
            var a320 = rig.name.Contains("320");
            s.FuelCapacityKg = a320 ? 18700f : 20800f;              // usable, Jet A-1 at 0.8 kg/l (APPROX)
            s.FuelKg = Mathf.Round(Random.Range(0.10f, 0.18f) * s.FuelCapacityKg / 10f) * 10f;
            s.FuelTargetKg = Mathf.Round(0.55f * s.FuelCapacityKg / 100f) * 100f;
            return s;
        }

        public int BagCount
        {
            get
            {
                HoldBags.RemoveAll(b => b == null);
                return HoldBags.Count;
            }
        }
    }
}
