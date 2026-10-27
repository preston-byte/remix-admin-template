using System;
using System.Collections.Generic;
using UnityEngine;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// The vehicles the player can spawn from the wrist tablet. Written by the editor menu
    /// Ground Crew > Vehicles > Add Vehicles to Sandbox (one prefab per vehicle or vehicle set).
    /// </summary>
    [CreateAssetMenu(menuName = "Ground Crew/Vehicle Catalog", fileName = "VehicleCatalog")]
    public class VehicleCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string displayName;
            public GameObject prefab;
            [Tooltip("Overall length (m): how much clear ground it needs to appear side-on in front of you.")]
            public float length = 6f;
        }

        [SerializeField] List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        public void SetEntries(List<Entry> list) => entries = list ?? new List<Entry>();
    }
}
