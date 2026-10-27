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
        public GameObject passengerPrefab;
        [Tooltip("The same passenger as one mesh, already sitting (cheap to render).")] public GameObject seatedPassengerPrefab;
        public GameObject[] bagPrefabs = new GameObject[0];
        [Tooltip("Line material for hoses and the GPU cable.")] public Material hoseMaterial;
        [Range(4, 60)] public int passengersPerFlight = 24;
        [Range(4, 20)] public int bagsPerFlight = 16;
        [Tooltip("How much faster than real time fuel, water and waste flow (so you don't wait 15 minutes).")] public float serviceTimeScale = 4f;
        public VehicleCatalog vehicles;
        [Range(1, 6)] public int maxWorkersPerStand = 3;
    }
}
