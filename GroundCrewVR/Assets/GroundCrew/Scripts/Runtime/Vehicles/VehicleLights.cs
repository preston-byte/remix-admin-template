using System;
using System.Collections.Generic;
using UnityEngine;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// Vehicle lamps from the generator's emissive nodes (Light_Head_*, Light_Brake_*,
    /// Light_Reverse_*, Light_Beacon_*, Light_Work_*): a lamp is lit by enabling its node.
    ///
    /// Brake and reverse lamps follow the vehicle. The amber beacon flashes whenever the engine
    /// runs, as airside rules require of moving vehicles. Headlights and work lights are
    /// switched by the driver.
    /// </summary>
    public class VehicleLights : MonoBehaviour
    {
        [SerializeField] VehicleController vehicle;
        [SerializeField] float beaconHz = 1.4f;
        [SerializeField, Range(0.05f, 0.9f)] float beaconOnFraction = 0.35f;

        readonly List<GameObject> m_Head = new List<GameObject>();
        readonly List<GameObject> m_Brake = new List<GameObject>();
        readonly List<GameObject> m_Reverse = new List<GameObject>();
        readonly List<GameObject> m_Beacon = new List<GameObject>();
        readonly List<GameObject> m_Work = new List<GameObject>();
        float m_Phase;

        public bool Headlights { get; private set; }
        public bool WorkLights { get; private set; }

        /// <summary>Force the beacon on with the engine off (e.g. a broken-down vehicle).</summary>
        public bool BeaconOverride { get; set; }

        void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                var n = t.name;
                if (n.StartsWith("Light_Head_", StringComparison.Ordinal)) m_Head.Add(t.gameObject);
                else if (n.StartsWith("Light_Brake_", StringComparison.Ordinal)) m_Brake.Add(t.gameObject);
                else if (n.StartsWith("Light_Reverse_", StringComparison.Ordinal)) m_Reverse.Add(t.gameObject);
                else if (n.StartsWith("Light_Beacon_", StringComparison.Ordinal)) m_Beacon.Add(t.gameObject);
                else if (n.StartsWith("Light_Work_", StringComparison.Ordinal)) m_Work.Add(t.gameObject);
            }
            m_Phase = UnityEngine.Random.value;   // vehicles don't flash in sync
            Set(m_Head, false);
            Set(m_Work, false);
        }

        public void ToggleHeadlights() => SetHeadlights(!Headlights);
        public void SetHeadlights(bool on) { Headlights = on; Set(m_Head, on); }
        public void SetWorkLights(bool on) { WorkLights = on; Set(m_Work, on); }

        void Update()
        {
            var running = vehicle != null && vehicle.EngineRunning;
            var trailer = vehicle != null && !vehicle.HasEngine;
            Set(m_Brake, vehicle != null && (running || trailer) && vehicle.Input.Brake > 0.05f);
            Set(m_Reverse, vehicle != null && vehicle.ReverseSelected);
            if (m_Beacon.Count > 0)
            {
                m_Phase = Mathf.Repeat(m_Phase + Time.deltaTime * beaconHz, 1f);
                Set(m_Beacon, (running || BeaconOverride) && m_Phase < beaconOnFraction);
            }
        }

        static void Set(List<GameObject> lamps, bool on)
        {
            foreach (var l in lamps) if (l != null && l.activeSelf != on) l.SetActive(on);
        }
    }
}
