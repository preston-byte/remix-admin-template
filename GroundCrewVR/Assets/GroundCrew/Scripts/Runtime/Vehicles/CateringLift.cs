using GroundCrew.Interaction;
using GroundCrew.Player;
using UnityEngine;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// High-loader catering truck: scissor lift and front platform.
    ///
    /// Hold LIFT UP / LIFT DOWN (on the platform, by the box door) to raise or lower the box;
    /// PLATFORM OUT / IN slides the bridge plate towards the aircraft door. As on the real truck
    /// the lift only works with the parking brake on, and the truck cannot drive while the box
    /// is raised or the platform is out.
    ///
    /// Scissor arms are posed from the box height: arm A pivots at the chassis front and its top
    /// end slides under the box; arm B crosses it at the middle. Directions are worked out in the
    /// truck's local space from the generator's 'End' helper nodes, so importer axis conventions
    /// don't matter.
    /// </summary>
    public class CateringLift : MonoBehaviour
    {
        [SerializeField] VehicleController vehicle;
        [SerializeField] Transform liftBox;
        [SerializeField] Transform platform;
        [SerializeField] PressButton upButton, downButton, platformOutButton, platformInButton;
        [SerializeField] Transform armAL, armBL, armAR, armBR;
        [SerializeField] float armLength = 4.6f;
        [SerializeField, Tooltip("Highest box floor, metres above the ground (A320 door 2 sill is ~3.5 m).")] float maxFloor = 5.6f;
        [SerializeField] float stowedFloor = 1.38f;
        [SerializeField, Tooltip("m/s")] float liftSpeed = 0.12f;
        [SerializeField] float platformTravel = 0.9f;
        [SerializeField, Tooltip("m/s")] float platformSpeed = 0.2f;

        struct Arm
        {
            public Transform T;
            public Quaternion Rest;
            public Vector3 RestPos, RestDir;
            public bool Valid => T != null;
        }

        Arm m_AL, m_BL, m_AR, m_BR;
        Vector3 m_BoxRest, m_PlatformRest, m_Up, m_Forward;
        float m_SinRest;

        public float Rise { get; private set; }
        public float Extension { get; private set; }
        public float FloorHeight => stowedFloor + Rise;

        void Awake()
        {
            var root = transform;
            m_Up = Vector3.up;
            m_Forward = Vector3.forward;
            if (liftBox != null) m_BoxRest = liftBox.localPosition;
            if (platform != null) m_PlatformRest = platform.localPosition;
            m_AL = Capture(armAL, root);
            m_BL = Capture(armBL, root);
            m_AR = Capture(armAR, root);
            m_BR = Capture(armBR, root);
            m_SinRest = m_AL.Valid ? Mathf.Clamp(Vector3.Dot(m_AL.RestDir, m_Up), -1f, 1f) : 0f;
        }

        static Arm Capture(Transform arm, Transform root)
        {
            var a = new Arm { T = arm };
            if (arm == null) return a;
            a.Rest = arm.localRotation;
            a.RestPos = arm.localPosition;
            var end = arm.Find("End");
            var parent = arm.parent != null ? arm.parent : root;
            a.RestDir = end != null ? parent.InverseTransformDirection(end.position - arm.position).normalized : Vector3.back;
            return a;
        }

        void Update()
        {
            var wants = (upButton != null && upButton.IsHeld) || (downButton != null && downButton.IsHeld);
            if (wants && vehicle != null && !vehicle.ParkingBrake)
            {
                if (ComfortSettings.DrivingAssists && Mathf.Abs(vehicle.SpeedKmh) < 1f) vehicle.SetParkingBrake(true);
                else vehicle.Notify("Set the parking brake to use the lift");
            }
            var parked = vehicle == null || (vehicle.ParkingBrake && Mathf.Abs(vehicle.SpeedKmh) < 0.5f);
            var dir = 0f;
            if (parked && upButton != null && upButton.IsHeld) dir += 1f;
            if (parked && downButton != null && downButton.IsHeld) dir -= 1f;
            var maxRise = maxFloor - stowedFloor;
            if (dir != 0f) Rise = Mathf.Clamp(Rise + dir * liftSpeed * Time.deltaTime, 0f, maxRise);

            var pdir = 0f;
            if (platformOutButton != null && platformOutButton.IsHeld) pdir += 1f;
            if (platformInButton != null && platformInButton.IsHeld) pdir -= 1f;
            if (pdir != 0f) Extension = Mathf.Clamp(Extension + pdir * platformSpeed * Time.deltaTime, 0f, platformTravel);

            if (vehicle != null)
            {
                if (Rise > 0.02f || Extension > 0.02f) vehicle.AddDriveInhibit(this, Rise > 0.02f ? "Box raised" : "Platform out");
                else vehicle.RemoveDriveInhibit(this);
            }
            Apply();
        }

        void Apply()
        {
            if (liftBox != null) liftBox.localPosition = m_BoxRest + m_Up * Rise;
            if (platform != null) platform.localPosition = m_PlatformRest + m_Forward * Extension;
            if (!m_AL.Valid) return;
            var sin = Mathf.Clamp(m_SinRest + Rise / armLength, -0.999f, 0.999f);
            var cos = Mathf.Sqrt(1f - sin * sin);
            PoseScissor(m_AL, m_BL, sin, cos);
            PoseScissor(m_AR, m_BR, sin, cos);
        }

        void PoseScissor(Arm a, Arm b, float sin, float cos)
        {
            if (!a.Valid) return;
            var horizontalA = Vector3.ProjectOnPlane(a.RestDir, m_Up).normalized;
            var dirA = horizontalA * cos + m_Up * sin;
            a.T.localRotation = Quaternion.FromToRotation(a.RestDir, dirA) * a.Rest;
            if (!b.Valid) return;
            // B pivots on A's midpoint (keeping its sideways offset) and leans the other way.
            var restMidA = a.RestPos + a.RestDir * (armLength * 0.5f);
            var offset = b.RestPos - restMidA;
            b.T.localPosition = a.RestPos + dirA * (armLength * 0.5f) + offset;
            var horizontalB = Vector3.ProjectOnPlane(b.RestDir, m_Up).normalized;
            var dirB = horizontalB * cos + m_Up * sin;
            b.T.localRotation = Quaternion.FromToRotation(b.RestDir, dirB) * b.Rest;
        }
    }
}
