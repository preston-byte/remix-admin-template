using System;
using System.Collections.Generic;
using GroundCrew.Aircraft;
using GroundCrew.Core;
using UnityEngine;

namespace GroundCrew.Vehicles
{
    public enum Gear { Reverse = -1, Neutral = 0, Drive = 1 }

    /// <summary>
    /// What a driver (player, AI or remote player) asks of a vehicle this frame. Kept as plain
    /// data so it can be sent over the network later; the vehicle with authority simulates it.
    /// </summary>
    [Serializable]
    public struct VehicleInput
    {
        [Range(0f, 1f)] public float Throttle;
        [Range(0f, 1f)] public float Brake;
        [Range(-1f, 1f)] public float Steer;
    }

    [Serializable]
    public class WheelBinding
    {
        public WheelCollider collider;
        public Transform visual;
        public bool steer;
        public bool drive;
        [NonSerialized] public Quaternion visualOffset;
    }

    /// <summary>
    /// Drives an airport ground vehicle with Unity WheelColliders: engine on/off, a simple
    /// automatic gearbox (D / N / R), throttle, service brake, parking brake, steering and an
    /// airside speed governor.
    ///
    /// GSE handling is deliberately slow and heavy: airside limits are ~25 km/h on the apron and
    /// walking pace near aircraft, and tugs pushing an aircraft are limited further
    /// (<see cref="TowLoad"/>). Pressing the throttle against the direction of travel brakes first,
    /// like an automatic, so the vehicle never lurches into reverse.
    ///
    /// Inputs come from <see cref="Input"/>, written by <see cref="VehicleDriverInput"/> for the
    /// seated player (or by AI / network code). Interlocks (a raised catering box, a coupled
    /// hose...) can stop the vehicle moving through <see cref="AddDriveInhibit"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class VehicleController : MonoBehaviour
    {
        [SerializeField] string displayName = "Vehicle";
        [SerializeField] bool hasEngine = true;
        [SerializeField] float maxSpeedKmh = 25f;
        [SerializeField, Tooltip("Front-wheel lock at standstill (degrees).")] float maxSteerAngle = 35f;
        [SerializeField, Tooltip("Steering lock left at max speed, as a fraction (stability).")] float steerAtSpeed = 0.45f;
        [SerializeField, Tooltip("Total drive torque (N m) at standstill.")] float motorTorque = 1000f;
        [SerializeField, Tooltip("Total service-brake torque (N m).")] float brakeTorque = 3000f;
        [SerializeField, Tooltip("Rolling resistance / engine braking with no throttle, fraction of brake torque.")] float coastBrake = 0.04f;
        [SerializeField] Vector3 centerOfMass = new Vector3(0f, 0.5f, 0f);
        [SerializeField] List<WheelBinding> wheels = new List<WheelBinding>();
        [SerializeField] bool startParked = true;

        [Header("Towing (pushback tugs)")]
        [SerializeField] float towMaxSpeedKmh = 10f;
        [SerializeField] float towTorqueScale = 0.6f;

        public VehicleInput Input;

        Rigidbody m_Body;
        readonly HashSet<object> m_DriveInhibits = new HashSet<object>();
        float m_LastImpact = -10f;

        public string DisplayName => displayName;
        public bool HasEngine => hasEngine;
        public bool EngineRunning { get; private set; }
        public bool ParkingBrake { get; private set; }
        public Gear Gear { get; private set; }
        public bool Occupied { get; set; }
        public Rigidbody Body => m_Body;
        public IReadOnlyList<WheelBinding> Wheels => wheels;
        public string Id => EntityId.Of(this);

        /// <summary>Signed speed along the vehicle's forward axis, km/h.</summary>
        public float SpeedKmh => m_Body != null ? Vector3.Dot(m_Body.linearVelocity, transform.forward) * 3.6f : 0f;

        /// <summary>A pushback tug with an aircraft on the bar: lower speed limit and torque.</summary>
        public bool TowLoad { get; set; }

        /// <summary>Extra speed limit from gameplay (e.g. 5 km/h inside the stand). 0 = none.</summary>
        public float ZoneSpeedLimitKmh { get; set; }

        public float SpeedLimitKmh
        {
            get
            {
                var limit = TowLoad ? Mathf.Min(maxSpeedKmh, towMaxSpeedKmh) : maxSpeedKmh;
                return ZoneSpeedLimitKmh > 0f ? Mathf.Min(limit, ZoneSpeedLimitKmh) : limit;
            }
        }

        public bool DriveInhibited => m_DriveInhibits.Count > 0;
        public bool Braking => Input.Brake > 0.05f || ParkingBrake;
        public bool ReverseSelected => EngineRunning && Gear == Gear.Reverse;

        /// <summary>Engine, gear, parking brake or lights changed (dash indicators, audio).</summary>
        public event Action<VehicleController> StateChanged;

        void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
            m_Body.centerOfMass = centerOfMass;
            m_Body.interpolation = RigidbodyInterpolation.Interpolate;
            ParkingBrake = startParked || !hasEngine;
            foreach (var w in wheels)
            {
                if (w.collider == null || w.visual == null) continue;
                w.collider.GetWorldPose(out _, out var rot);
                w.visualOffset = Quaternion.Inverse(rot) * w.visual.rotation;
            }
        }

        // ------------------------------------------------------------------ controls (buttons, AI)

        public void StartEngine() { if (hasEngine && !EngineRunning) { EngineRunning = true; Changed(); } }
        public void StopEngine() { if (EngineRunning) { EngineRunning = false; Gear = Gear.Neutral; Changed(); } }
        public void ToggleEngine() { if (EngineRunning) StopEngine(); else StartEngine(); }
        public void SetGearDrive() => SetGear(Gear.Drive);
        public void SetGearNeutral() => SetGear(Gear.Neutral);
        public void SetGearReverse() => SetGear(Gear.Reverse);
        public void SetParkingBrake(bool on) { if (ParkingBrake != on) { ParkingBrake = on; Changed(); } }
        public void ToggleParkingBrake() => SetParkingBrake(!ParkingBrake);

        public void SetGear(Gear gear)
        {
            // Shifting between D and R needs (almost) a standstill, as on a real automatic.
            if (gear != Gear.Neutral && Gear != Gear.Neutral && gear != Gear && Mathf.Abs(SpeedKmh) > 1.5f) return;
            if (Gear == gear) return;
            Gear = gear;
            Changed();
        }

        /// <summary>Editor set-up (VehicleAssembler): the wheel colliders and their visuals.</summary>
        public void SetWheels(List<WheelBinding> bindings) => wheels = bindings ?? new List<WheelBinding>();

        public void AddDriveInhibit(object source) { if (source != null) m_DriveInhibits.Add(source); }
        public void RemoveDriveInhibit(object source) { if (source != null) m_DriveInhibits.Remove(source); }

        void Changed() => StateChanged?.Invoke(this);

        // ------------------------------------------------------------------ simulation

        void FixedUpdate()
        {
            var speed = SpeedKmh;
            var limit = SpeedLimitKmh;
            var steerScale = Mathf.Lerp(1f, steerAtSpeed, Mathf.InverseLerp(0f, Mathf.Max(1f, maxSpeedKmh), Mathf.Abs(speed)));
            var steer = Mathf.Clamp(Input.Steer, -1f, 1f) * maxSteerAngle * steerScale;

            var direction = (int)Gear;
            var canDrive = hasEngine && EngineRunning && direction != 0 && !ParkingBrake && !DriveInhibited;
            var throttle = canDrive ? Mathf.Clamp01(Input.Throttle) : 0f;
            var brake = Mathf.Clamp01(Input.Brake);

            // Throttle against the direction of travel brakes first.
            if (throttle > 0f && speed * direction < -0.5f)
            {
                brake = Mathf.Max(brake, throttle);
                throttle = 0f;
            }

            // Governor: no drive above the limit, gentle braking well above it.
            var over = Mathf.Abs(speed) - limit;
            if (over > 0f) throttle = 0f;
            if (over > 2f) brake = Mathf.Max(brake, 0.3f);

            var driveWheels = 0;
            foreach (var w in wheels) if (w.drive && w.collider != null) driveWheels++;
            var torqueScale = TowLoad ? towTorqueScale : 1f;
            var fade = Mathf.Clamp01(1f - Mathf.Abs(speed) / Mathf.Max(1f, limit));
            var perWheelDrive = driveWheels > 0 ? motorTorque * torqueScale * throttle * (0.35f + 0.65f * fade) * direction / driveWheels : 0f;
            var perWheelBrake = brakeTorque / Mathf.Max(1, wheels.Count);
            var appliedBrake = ParkingBrake
                ? perWheelBrake * 2f
                : perWheelBrake * Mathf.Max(brake, throttle <= 0f ? coastBrake : 0f);

            foreach (var w in wheels)
            {
                if (w.collider == null) continue;
                if (w.steer) w.collider.steerAngle = steer;
                w.collider.motorTorque = w.drive ? perWheelDrive : 0f;
                w.collider.brakeTorque = appliedBrake;
            }
        }

        /// <summary>For trailers: steer the steering wheels to an angle directly (degrees).</summary>
        public void SetSteerAngleDirect(float degrees)
        {
            Input.Steer = maxSteerAngle > 0f ? degrees / maxSteerAngle : 0f;
            foreach (var w in wheels) if (w.steer && w.collider != null) w.collider.steerAngle = Mathf.Clamp(degrees, -maxSteerAngle, maxSteerAngle);
        }

        void LateUpdate()
        {
            foreach (var w in wheels)
            {
                if (w.collider == null || w.visual == null) continue;
                w.collider.GetWorldPose(out var pos, out var rot);
                w.visual.SetPositionAndRotation(pos, rot * w.visualOffset);
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            var speed = collision.relativeVelocity.magnitude;
            if (speed < 0.8f || Time.time - m_LastImpact < 0.5f) return;
            m_LastImpact = Time.time;
            var hitAircraft = collision.collider != null && collision.collider.GetComponentInParent<AircraftRig>() != null;
            EventBus.Publish(new VehicleCollisionEvent(this, collision.collider, speed, hitAircraft));
        }
    }
}
