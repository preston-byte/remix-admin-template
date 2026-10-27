using System;
using System.Collections.Generic;
using GroundCrew.Aircraft;
using UnityEngine;

namespace GroundCrew.Airport
{
    /// <summary>One aircraft parking position: a remote stand or a contact gate.</summary>
    [Serializable]
    public class StandInfo
    {
        public string name = "Stand 24";
        [Tooltip("Nose-gear stop position (world).")] public Vector3 stopPosition = new Vector3(0f, 0f, 12f);
        [Tooltip("Aircraft heading on the stand (deg).")] public float yaw = 180f;
        [Tooltip("Jet bridge, for a contact gate; empty on a remote stand.")] public JetBridge bridge;
        [Tooltip("Where AI ground crew come from (equipment area, baggage hall door).")] public Vector3 staging = new Vector3(11f, 0f, -24f);
        [Tooltip("Where the flight crew arrive (crew bus drop-off, or the terminal end of the bridge).")] public Vector3 crewEntry = new Vector3(-11f, 0f, -24f);
        [Tooltip("Where the AI parks the stairs when it's finished with them.")] public Vector3 equipmentPark = new Vector3(11f, 0f, -31f);
        [Tooltip("Where the AI parks the belt loader.")] public Vector3 beltPark = new Vector3(-11f, 0f, -31f);

        public bool IsGate => bridge != null;
    }

    /// <summary>
    /// The stands and gates of the airport, written by Ground Crew > Airport > Build Airport.
    /// Without an airport (the bare sandbox) there is one stand: Stand 24 at the origin.
    /// </summary>
    public class StandRegistry : MonoBehaviour
    {
        [SerializeField] List<StandInfo> stands = new List<StandInfo>();

        static StandRegistry s_Instance;
        static readonly List<StandInfo> s_Fallback = new List<StandInfo> { new StandInfo() };

        public static IReadOnlyList<StandInfo> All => s_Instance != null && s_Instance.stands.Count > 0 ? s_Instance.stands : s_Fallback;

        public void SetStands(List<StandInfo> list) => stands = list ?? new List<StandInfo>();

        void OnEnable() => s_Instance = this;
        void OnDisable() { if (s_Instance == this) s_Instance = null; }

        /// <summary>The aircraft parked on a stand (nose gear within 12 m of the stop), or null.</summary>
        public static AircraftRig AircraftAt(StandInfo stand)
        {
            if (stand == null) return null;
            AircraftRig best = null;
            var bestDistance = 12f;
            foreach (var rig in AircraftRig.All)
            {
                var d = Vector3.Distance(rig.transform.position, stand.stopPosition);
                if (d < bestDistance) { bestDistance = d; best = rig; }
            }
            return best;
        }

        /// <summary>The stand nearest a point (e.g. the player), or the first one.</summary>
        public static int Nearest(Vector3 position)
        {
            var best = 0;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < All.Count; i++)
            {
                var d = Vector3.Distance(All[i].stopPosition, position);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }
    }
}
