using System;
using System.Collections.Generic;
using GroundCrew.Aircraft;
using GroundCrew.Core;
using UnityEngine;

namespace GroundCrew.Vehicles
{
    public enum DockingState { None, Approaching, Aligned, TooHigh, TooLow, Offset, Contact }

    /// <summary>
    /// Tells the operator (and the turnaround rules) whether a piece of equipment is correctly
    /// placed at an aircraft door: passenger stairs or the catering platform at a passenger
    /// door, the belt loader's belt end at a hold door.
    ///
    /// It looks for the nearest door-sill service point (<c>SP_Sill_...</c> nodes on generated
    /// aircraft) and measures, from the equipment's edge point:
    ///  - height error: edge above or below the sill (a platform must sit just below a door that
    ///    swings out over it);
    ///  - gap: horizontal distance out from the fuselage (negative = pushing into the skin);
    ///  - lateral: offset along the fuselage from the door centre.
    /// A green lamp on the equipment lights when everything is in tolerance.
    /// </summary>
    public class DoorDockingSensor : MonoBehaviour
    {
        [SerializeField] string equipmentName = "Stairs";
        [SerializeField, Tooltip("Front edge of the platform / belt end.")] Transform edge;
        [SerializeField, Tooltip("Which sills count: SP_Sill_Door_ (passenger doors) or SP_Sill_CargoDoor_.")] string sillPrefix = "SP_Sill_Door_";
        [SerializeField] float searchRadius = 3f;
        [SerializeField, Tooltip("Max height of the edge above the sill (m).")] float maxAbove = 0.03f;
        [SerializeField, Tooltip("Max height of the edge below the sill (m).")] float maxBelow = 0.15f;
        [SerializeField, Tooltip("Max gap out from the fuselage (m).")] float maxGap = 0.25f;
        [SerializeField, Tooltip("Max offset along the fuselage from the door centre (m).")] float maxLateral = 0.35f;
        [SerializeField] GameObject alignedLamp;

        static readonly List<Transform> s_Sills = new List<Transform>();
        static readonly List<AircraftRig> s_Rigs = new List<AircraftRig>();
        static float s_NextRefresh;

        float m_NextCheck;

        public DockingState State { get; private set; } = DockingState.None;
        public string Door { get; private set; } = "";
        public float Gap { get; private set; }
        public float HeightError { get; private set; }
        public float Lateral { get; private set; }
        public string EquipmentName => equipmentName;

        void Awake() { if (alignedLamp != null) alignedLamp.SetActive(false); }

        static void Refresh()
        {
            s_Sills.Clear();
            s_Rigs.Clear();
            foreach (var rig in FindObjectsByType<AircraftRig>(FindObjectsSortMode.None))
            {
                s_Rigs.Add(rig);
                foreach (var t in rig.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("SP_Sill_", StringComparison.Ordinal)) s_Sills.Add(t);
            }
            s_NextRefresh = Time.time + 5f;
        }

        void Update()
        {
            if (edge == null || Time.time < m_NextCheck) return;
            m_NextCheck = Time.time + 0.1f;
            if (Time.time >= s_NextRefresh) Refresh();

            Transform best = null;
            var bestDistance = searchRadius;
            foreach (var sill in s_Sills)
            {
                if (sill == null || !sill.name.StartsWith(sillPrefix, StringComparison.Ordinal)) continue;
                var d = Vector3.Distance(sill.position, edge.position);
                if (d < bestDistance) { bestDistance = d; best = sill; }
            }

            var state = DockingState.None;
            if (best != null)
            {
                var rig = best.GetComponentInParent<AircraftRig>();
                var heading = Vector3.ProjectOnPlane(rig != null ? rig.transform.forward : Vector3.forward, Vector3.up).normalized;
                var centre = rig != null ? rig.transform.position : best.position;
                var outward = Vector3.ProjectOnPlane(best.position - centre, Vector3.up);
                outward = (outward - heading * Vector3.Dot(outward, heading)).normalized;
                var d = edge.position - best.position;
                Gap = Vector3.Dot(d, outward);
                Lateral = Vector3.Dot(d, heading);
                HeightError = d.y;
                Door = best.name.Substring("SP_Sill_".Length);

                if (Gap < -0.02f) state = DockingState.Contact;
                else if (Gap > maxGap) state = DockingState.Approaching;
                else if (Mathf.Abs(Lateral) > maxLateral) state = DockingState.Offset;
                else if (HeightError > maxAbove) state = DockingState.TooHigh;
                else if (HeightError < -maxBelow) state = DockingState.TooLow;
                else state = DockingState.Aligned;
            }

            if (state == State) return;
            State = state;
            if (alignedLamp != null) alignedLamp.SetActive(state == DockingState.Aligned);
            EventBus.Publish(new DockingEvent(this, state, Door, Gap, HeightError, Lateral));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_Sills.Clear(); s_Rigs.Clear(); s_NextRefresh = 0f; }
    }
}
