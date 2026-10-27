using GroundCrew.Vehicles;
using UnityEngine;

namespace GroundCrew.Operations
{
    /// <summary>Prefabs the operations system spawns (written by Ground Crew > Operations > Set Up).</summary>
    [CreateAssetMenu(menuName = "Ground Crew/Ops Config", fileName = "OpsConfig")]
    public class OpsConfig : ScriptableObject
    {
        public GameObject pilotPrefab;
        public GameObject cabinCrewPrefab;
        public GameObject groundCrewPrefab;
        public GameObject chockPrefab;
        public GameObject conePrefab;
        public VehicleCatalog vehicles;
        [Range(1, 6)] public int maxWorkersPerStand = 3;
    }
}
