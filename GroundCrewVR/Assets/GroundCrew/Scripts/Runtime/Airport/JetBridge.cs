using System.Collections.Generic;
using GroundCrew.Aircraft;
using GroundCrew.Core;
using UnityEngine;

namespace GroundCrew.Airport
{
    public enum BridgeState { Stowed, Docking, Docked, Retracting, Stopped }

    public readonly struct JetBridgeEvent : IGameEvent
    {
        public readonly JetBridge Bridge;
        public readonly BridgeState State;
        public readonly string Message;
        public JetBridgeEvent(JetBridge bridge, BridgeState state, string message) { Bridge = bridge; State = state; Message = message; }
    }

    /// <summary>
    /// Apron-drive passenger boarding bridge that docks itself to the aircraft on its gate.
    ///
    /// Joints (from the generated JetBridge.glb): the tunnel slews about the rotunda and pitches,
    /// two inner tunnels telescope, the cab turns at the tunnel end and stays level, the drive
    /// column stays vertical with its legs stretched to the ground and its bogie wheels steering
    /// and rolling with the motion, and the canopy (bellows) closes onto the fuselage.
    ///
    /// <see cref="Dock"/> solves the joints for the cab floor's front edge to sit just below and
    /// outside the door sill, facing the fuselage: first 1.2 m out at normal speed, then the
    /// final approach at a crawl, then the canopy. Docked, it keeps following the sill (auto
    /// level). If the aircraft moves away (pushback) it retracts on its own. <see cref="Retract"/>
    /// backs off and parks it in its stowed position. Buttons in the cab and the Ops iPad call these.
    /// </summary>
    public class JetBridge : MonoBehaviour
    {
        [SerializeField] string gateName = "Gate";
        [SerializeField] Transform slew, tunnel, tunnelB, tunnelC, cabMount, cab, canopy, cabFront;
        [SerializeField] Transform columnMount, columnLegs, bogie, wheelL, wheelR;
        [SerializeField, Tooltip("Collider across the cab doorway while not docked, so nobody walks out into the air.")] Collider frontBarrier;
        [SerializeField, Tooltip("Where the aircraft's nose gear stops on this gate (world).")] Vector3 aircraftStop;
        [SerializeField] string door = "Door_1L";
        [SerializeField] bool openDoorWhenDocked = true;

        [Header("Geometry (from the generator)")]
        [SerializeField] float floorHeight = 5f;
        [SerializeField] float cabMountBase = 11.1f;
        [SerializeField] float cabMountY = 0.14f;
        [SerializeField] float maxExtension = 13f;
        [SerializeField] float legRestLength = 3.99f;
        [SerializeField] float wheelRadius = 0.45f;
        [SerializeField] float canopyTravel = 0.15f;

        [Header("Stowed pose")]
        [SerializeField] float stowYaw = -48f;
        [SerializeField] float stowPitch = 7f;
        [SerializeField] float stowCabYaw = -42f;

        [Header("Speeds")]
        [SerializeField, Tooltip("deg/s")] float slewSpeed = 6f;
        [SerializeField, Tooltip("deg/s")] float pitchSpeed = 2f;
        [SerializeField, Tooltip("m/s")] float extendSpeed = 0.5f;
        [SerializeField, Tooltip("deg/s")] float cabSpeed = 9f;
        [SerializeField, Tooltip("Speed factor for the last 1.2 m.")] float finalApproachFactor = 0.2f;

        struct Axes
        {
            public float Yaw, Pitch, Ext, CabYaw;
        }

        static readonly List<JetBridge> s_All = new List<JetBridge>();
        public static IReadOnlyList<JetBridge> All => s_All;

        Axes m_Now, m_Stow;
        float m_Canopy;
        int m_Phase;
        Vector3 m_RestB, m_RestC, m_RestCanopy, m_LastBogie;
        float m_WheelAngle, m_BogieYaw;
        AircraftRig m_Rig;
        Transform m_Sill;
        string m_Message = "";

        public string GateName => gateName;
        public BridgeState State { get; private set; } = BridgeState.Stowed;
        public string Message => m_Message;
        public AircraftRig DockedAircraft => State == BridgeState.Docked ? m_Rig : null;
        public bool IsDocked => State == BridgeState.Docked;
        public Vector3 AircraftStop => aircraftStop;
        public string DoorName => door;

        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_All.Clear();

        void Awake()
        {
            if (tunnelB != null) m_RestB = tunnelB.localPosition;
            if (tunnelC != null) m_RestC = tunnelC.localPosition;
            if (canopy != null) m_RestCanopy = canopy.localPosition;
            m_Stow = new Axes { Yaw = stowYaw, Pitch = stowPitch, Ext = 0f, CabYaw = stowCabYaw };
            m_Now = m_Stow;
            m_BogieYaw = stowYaw;
            Apply(0f);
            if (bogie != null) m_LastBogie = bogie.position;
            m_Message = "Stowed";
        }

        // ------------------------------------------------------------------ commands

        /// <summary>Dock to the aircraft on this gate (its <see cref="door"/>).</summary>
        public void Dock()
        {
            m_Rig = FindAircraft();
            if (m_Rig == null) { Say(BridgeState.Stopped, "No aircraft on " + gateName); return; }
            m_Sill = FindDeep(m_Rig.transform, "SP_Sill_" + door);
            if (m_Sill == null) { Say(BridgeState.Stopped, "Aircraft has no " + door); return; }
            if (State == BridgeState.Docked) return;
            m_Phase = 0;
            Say(BridgeState.Docking, "Docking to " + Pretty(door));
        }

        /// <summary>Back off and park in the stowed position.</summary>
        public void Retract()
        {
            if (State == BridgeState.Stowed) return;
            if (State == BridgeState.Docked && openDoorWhenDocked && m_Rig != null) m_Rig.SetOpen01(door, 0f);
            m_Phase = 0;
            Say(BridgeState.Retracting, "Retracting");
        }

        public void Stop() => Say(BridgeState.Stopped, "Stopped");

        public void ToggleDock()
        {
            if (State == BridgeState.Docked || State == BridgeState.Docking) Retract();
            else Dock();
        }

        // ------------------------------------------------------------------ motion

        void Update()
        {
            var dt = Time.deltaTime;
            switch (State)
            {
                case BridgeState.Docking: UpdateDocking(dt); break;
                case BridgeState.Docked: UpdateDocked(dt); break;
                case BridgeState.Retracting: UpdateRetracting(dt); break;
            }
            if (frontBarrier != null) frontBarrier.enabled = State != BridgeState.Docked;
        }

        void UpdateDocking(float dt)
        {
            if (m_Rig == null || m_Sill == null) { Say(BridgeState.Stopped, "Aircraft gone"); return; }
            if (m_Phase < 2)
            {
                var gap = m_Phase == 0 ? 1.2f : 0.03f;
                if (!Solve(gap, out var target)) { Say(BridgeState.Stopped, Pretty(door) + " is out of reach"); return; }
                var factor = m_Phase == 0 ? 1f : finalApproachFactor;
                var remaining = Vector3.Distance(cabFront != null ? cabFront.position : transform.position, SillFront(gap));
                m_Message = m_Phase == 0 ? $"Driving to {Pretty(door)} ({remaining:0.0} m)" : $"Final approach ({remaining * 100f:0} cm)";
                if (MoveTowards(target, factor, dt)) m_Phase++;
            }
            else
            {
                m_Canopy = Mathf.MoveTowards(m_Canopy, 1f, dt * 0.5f);
                m_Message = "Canopy closing";
                Apply(dt);
                if (m_Canopy >= 1f)
                {
                    if (openDoorWhenDocked) m_Rig.SetOpen01(door, 1f);
                    Say(BridgeState.Docked, "Docked at " + Pretty(door));
                }
            }
        }

        void UpdateDocked(float dt)
        {
            if (m_Rig == null || m_Sill == null || !Solve(0.03f, out var target) || Error(target) > 0.5f)
            {
                Say(BridgeState.Retracting, "Aircraft moved - retracting");
                m_Phase = 0;
                return;
            }
            MoveTowards(target, finalApproachFactor, dt);   // auto level: follow the sill as the aircraft settles
        }

        void UpdateRetracting(float dt)
        {
            if (m_Canopy > 0f)
            {
                m_Canopy = Mathf.MoveTowards(m_Canopy, 0f, dt * 0.5f);
                Apply(dt);
                return;
            }
            if (m_Phase == 0)
            {
                if (m_Rig != null && m_Sill != null && Solve(1.2f, out var backOff) && Error(backOff) < 2f)
                {
                    m_Message = "Backing off";
                    if (MoveTowards(backOff, finalApproachFactor * 2f, dt)) m_Phase = 1;
                    return;
                }
                m_Phase = 1;
            }
            m_Message = "Returning to stowed position";
            if (MoveTowards(m_Stow, 1f, dt)) Say(BridgeState.Stowed, "Stowed");
        }

        float Error(Axes t) =>
            Mathf.Abs(Mathf.DeltaAngle(m_Now.Yaw, t.Yaw)) * 0.15f + Mathf.Abs(t.Pitch - m_Now.Pitch) * 0.2f + Mathf.Abs(t.Ext - m_Now.Ext)
            + Mathf.Abs(Mathf.DeltaAngle(m_Now.CabYaw, t.CabYaw)) * 0.03f;

        bool MoveTowards(Axes t, float factor, float dt)
        {
            m_Now.Yaw = Mathf.MoveTowardsAngle(m_Now.Yaw, t.Yaw, slewSpeed * factor * dt);
            m_Now.Pitch = Mathf.MoveTowards(m_Now.Pitch, t.Pitch, pitchSpeed * Mathf.Max(factor, 0.5f) * dt);
            m_Now.Ext = Mathf.MoveTowards(m_Now.Ext, t.Ext, extendSpeed * factor * dt);
            m_Now.CabYaw = Mathf.MoveTowardsAngle(m_Now.CabYaw, t.CabYaw, cabSpeed * factor * dt);
            Apply(dt);
            return Mathf.Abs(Mathf.DeltaAngle(m_Now.Yaw, t.Yaw)) < 0.05f && Mathf.Abs(t.Pitch - m_Now.Pitch) < 0.05f
                   && Mathf.Abs(t.Ext - m_Now.Ext) < 0.005f && Mathf.Abs(Mathf.DeltaAngle(m_Now.CabYaw, t.CabYaw)) < 0.1f;
        }

        // ------------------------------------------------------------------ solving

        Vector3 Outward()
        {
            var heading = Vector3.ProjectOnPlane(m_Rig.transform.forward, Vector3.up).normalized;
            var outward = Vector3.ProjectOnPlane(m_Sill.position - m_Rig.transform.position, Vector3.up);
            return (outward - heading * Vector3.Dot(outward, heading)).normalized;
        }

        Vector3 SillFront(float gap)
        {
            var p = m_Sill.position + Outward() * gap;
            p.y = m_Sill.position.y - 0.03f;
            return p;
        }

        /// <summary>Joint values that put the cab floor's front edge `gap` metres out from the sill.</summary>
        bool Solve(float gap, out Axes axes)
        {
            axes = m_Now;
            var outward = Outward();
            var cabDir = -outward;
            var front = SillFront(gap);
            var cabLength = cabFront != null && cabMount != null
                ? Vector3.Dot(cabFront.position - cabMount.position, cab != null ? cab.forward : cabDir) : 2.7f;
            var mount = front - cabDir * cabLength;
            var d = mount - transform.position;
            var horizontal = new Vector2(d.x, d.z).magnitude;
            var length = Mathf.Sqrt(Mathf.Max(0f, d.y * d.y + horizontal * horizontal - cabMountY * cabMountY));
            axes.Yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            axes.Pitch = (Mathf.Atan2(cabMountY, length) - Mathf.Atan2(d.y, horizontal)) * Mathf.Rad2Deg;
            axes.Ext = length - cabMountBase;
            var tunnelDir = Quaternion.Euler(0f, axes.Yaw, 0f) * Vector3.forward;
            axes.CabYaw = Vector3.SignedAngle(tunnelDir, cabDir, Vector3.up);
            return axes.Ext >= -0.05f && axes.Ext <= maxExtension && axes.Pitch > -9f && axes.Pitch < 15f && Mathf.Abs(axes.CabYaw) < 115f;
        }

        void Apply(float dt)
        {
            if (slew != null) slew.rotation = Quaternion.Euler(0f, m_Now.Yaw, 0f);
            if (tunnel != null) tunnel.localRotation = Quaternion.Euler(m_Now.Pitch, 0f, 0f);
            var half = Mathf.Max(0f, m_Now.Ext) * 0.5f;
            if (tunnelB != null) tunnelB.localPosition = m_RestB + Vector3.forward * half;
            if (tunnelC != null) tunnelC.localPosition = m_RestC + Vector3.forward * half;
            if (cabMount != null) cabMount.localRotation = Quaternion.Euler(-m_Now.Pitch, 0f, 0f);
            if (cab != null) cab.localRotation = Quaternion.Euler(0f, m_Now.CabYaw, 0f);
            if (canopy != null) canopy.localPosition = m_RestCanopy + Vector3.forward * (m_Canopy * canopyTravel);
            if (columnMount == null) return;
            columnMount.localRotation = Quaternion.Euler(-m_Now.Pitch, 0f, 0f);
            var groundY = transform.position.y - floorHeight;
            var axleY = groundY + wheelRadius;
            var legLength = columnMount.position.y - (axleY + 0.6f);
            if (columnLegs != null) columnLegs.localScale = new Vector3(1f, Mathf.Max(0.05f, legLength / legRestLength), 1f);
            if (bogie == null) return;
            var p = new Vector3(columnMount.position.x, axleY, columnMount.position.z);
            var move = p - m_LastBogie;
            move.y = 0f;
            if (move.sqrMagnitude > 1e-8f && dt > 0f)
            {
                // Steer the bogie along the motion (either way round), roll the wheels.
                var heading = Mathf.Atan2(move.x, move.z) * Mathf.Rad2Deg;
                var forwards = Mathf.Abs(Mathf.DeltaAngle(m_BogieYaw, heading)) <= 90f;
                if (!forwards) heading += 180f;
                m_BogieYaw = Mathf.MoveTowardsAngle(m_BogieYaw, heading, 90f * dt);
                m_WheelAngle += (forwards ? 1f : -1f) * move.magnitude / wheelRadius * Mathf.Rad2Deg;
            }
            m_LastBogie = p;
            bogie.SetPositionAndRotation(p, Quaternion.Euler(0f, m_BogieYaw, 0f));
            var spin = Quaternion.Euler(m_WheelAngle, 0f, 0f);
            if (wheelL != null) wheelL.localRotation = spin;
            if (wheelR != null) wheelR.localRotation = spin;
        }

        // ------------------------------------------------------------------ helpers

        AircraftRig FindAircraft()
        {
            AircraftRig best = null;
            var bestDistance = 12f;
            foreach (var rig in AircraftRig.All)
            {
                var d = Vector3.Distance(rig.transform.position, aircraftStop);
                if (d < bestDistance) { bestDistance = d; best = rig; }
            }
            return best;
        }

        /// <summary>The aircraft parked on this gate, if any (docked or not).</summary>
        public AircraftRig AircraftOnGate() => FindAircraft();

        /// <summary>Walking route from the terminal end of the fixed link to the cab doorway (world points).</summary>
        public List<Vector3> WalkPath(Vector3 terminalEntry)
        {
            var path = new List<Vector3> { terminalEntry, transform.position };
            if (tunnel != null) path.Add(tunnel.TransformPoint(new Vector3(0f, 0.1f, 2.6f)));
            if (cabMount != null) path.Add(cabMount.position + Vector3.up * 0.05f);
            if (cabFront != null && cab != null) path.Add(cabFront.position - cab.forward * 0.4f + Vector3.up * 0.05f);
            return path;
        }

        void Say(BridgeState state, string message)
        {
            State = state;
            m_Message = message;
            EventBus.Publish(new JetBridgeEvent(this, state, gateName + ": " + message));
        }

        static string Pretty(string doorName) => doorName.Replace("Door_", "door ").Replace('_', ' ');

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var hit = FindDeep(c, name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
