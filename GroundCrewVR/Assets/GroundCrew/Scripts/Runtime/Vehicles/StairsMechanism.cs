using System.Collections.Generic;
using GroundCrew.Interaction;
using GroundCrew.Player;
using UnityEngine;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// Passenger stairs truck: platform height and bridge-plate extension.
    ///
    /// Hold STAIRS UP / DOWN (on the dash, or on the platform's control column) to set the
    /// platform height between <see cref="minFloor"/> and <see cref="maxFloor"/>; STAIRS OUT / IN
    /// slides the bridge plate towards the door. The flight is hinged to the platform and its
    /// foot rolls along rails behind the rear axle, so raising the platform steepens the flight;
    /// each tread is counter-rotated to stay level. Height and plate only move with the parking
    /// brake on, and the truck can't drive with the plate out.
    /// </summary>
    public class StairsMechanism : MonoBehaviour
    {
        [SerializeField] VehicleController vehicle;
        [SerializeField] Transform platform;
        [SerializeField] Transform extension;
        [SerializeField] Transform flight;
        [SerializeField, Tooltip("Helper at the foot of the flight (generator node 'FlightBottom').")] Transform flightBottom;
        [SerializeField] PressButton[] upButtons = new PressButton[0];
        [SerializeField] PressButton[] downButtons = new PressButton[0];
        [SerializeField] PressButton[] outButtons = new PressButton[0];
        [SerializeField] PressButton[] inButtons = new PressButton[0];
        [SerializeField] float minFloor = 2.2f;
        [SerializeField] float maxFloor = 4.1f;
        [SerializeField] float flightLength = 6f;
        [SerializeField, Tooltip("m/s")] float speed = 0.1f;
        [SerializeField] float extensionTravel = 0.6f;
        [SerializeField, Tooltip("m/s")] float extensionSpeed = 0.2f;

        Vector3 m_PlatformRest, m_ExtensionRest, m_FlightRest, m_RestDir, m_Back;
        Quaternion m_FlightRestRot;
        float m_TopHeight, m_FootHeight;
        readonly List<Transform> m_Treads = new List<Transform>();
        readonly List<Quaternion> m_TreadRest = new List<Quaternion>();

        public float Rise { get; private set; }
        public float Extension { get; private set; }
        public float FloorHeight => minFloor + Rise;

        void Awake()
        {
            if (platform != null) m_PlatformRest = platform.localPosition;
            if (extension != null) m_ExtensionRest = extension.localPosition;
            if (flight == null) return;
            m_FlightRest = flight.localPosition;
            m_FlightRestRot = flight.localRotation;
            var parent = flight.parent;
            var bottomLocal = flightBottom != null ? parent.InverseTransformPoint(flightBottom.position) : flight.localPosition - Vector3.forward * flightLength;
            m_RestDir = (bottomLocal - flight.localPosition).normalized;
            m_Back = Vector3.ProjectOnPlane(m_RestDir, Vector3.up).normalized;
            m_TopHeight = flight.localPosition.y;
            m_FootHeight = bottomLocal.y;
            foreach (Transform child in flight)
            {
                if (!child.name.StartsWith("Tread_")) continue;
                m_Treads.Add(child);
                m_TreadRest.Add(child.localRotation);
            }
        }

        static bool AnyHeld(PressButton[] buttons)
        {
            foreach (var b in buttons) if (b != null && b.IsHeld) return true;
            return false;
        }

        void Update()
        {
            var wants = AnyHeld(upButtons) || AnyHeld(downButtons) || AnyHeld(outButtons) || AnyHeld(inButtons);
            if (wants && vehicle != null && !vehicle.ParkingBrake)
            {
                if (ComfortSettings.DrivingAssists && Mathf.Abs(vehicle.SpeedKmh) < 1f) vehicle.SetParkingBrake(true);
                else vehicle.Notify("Set the parking brake to move the stairs");
            }
            var parked = vehicle == null || vehicle.ParkingBrake;
            if (parked)
            {
                var dir = (AnyHeld(upButtons) ? 1f : 0f) - (AnyHeld(downButtons) ? 1f : 0f);
                var ext = (AnyHeld(outButtons) ? 1f : 0f) - (AnyHeld(inButtons) ? 1f : 0f);
                Rise = Mathf.Clamp(Rise + dir * speed * Time.deltaTime, 0f, maxFloor - minFloor);
                Extension = Mathf.Clamp(Extension + ext * extensionSpeed * Time.deltaTime, 0f, extensionTravel);
            }
            if (vehicle != null)
            {
                if (Extension > 0.02f) vehicle.AddDriveInhibit(this, "Bridge plate out");
                else vehicle.RemoveDriveInhibit(this);
            }
            Apply();
        }

        void Apply()
        {
            if (platform != null) platform.localPosition = m_PlatformRest + Vector3.up * Rise;
            if (extension != null) extension.localPosition = m_ExtensionRest + Vector3.forward * Extension;
            if (flight == null) return;
            flight.localPosition = m_FlightRest + Vector3.up * Rise;
            var sin = Mathf.Clamp((m_TopHeight + Rise - m_FootHeight) / flightLength, 0f, 0.95f);
            var cos = Mathf.Sqrt(1f - sin * sin);
            var dir = m_Back * cos - Vector3.up * sin;
            var q = Quaternion.FromToRotation(m_RestDir, dir);
            flight.localRotation = q * m_FlightRestRot;
            // Same rotation seen from inside the flight; undo it on each tread so they stay level.
            var qLocal = Quaternion.Inverse(m_FlightRestRot) * q * m_FlightRestRot;
            var undo = Quaternion.Inverse(qLocal);
            for (var i = 0; i < m_Treads.Count; i++)
                m_Treads[i].localRotation = undo * m_TreadRest[i];
        }
    }
}
