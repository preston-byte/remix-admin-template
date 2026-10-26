using System;
using System.Collections.Generic;
using GroundCrew.Aircraft;
using GroundCrew.Core;
using UnityEngine;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// Lets an aircraft be pushed back or towed by a tug through a <see cref="TowBar"/>.
    ///
    /// A 60-80 t airliner is not simulated as a free rigid body (PhysX would make it skate,
    /// bounce and jitter). Instead it moves kinematically the way a real one does on the ground:
    ///  - the tow bar is rigid and pivots on the tug's pin, so the tow fitting stays one bar
    ///    length from the pin;
    ///  - the main gear rolls straight and never slides sideways;
    ///  - the nose gear castors to follow the bar.
    /// Each physics step the fitting is moved along the bar from where the tug's pin now is, and
    /// the aircraft is re-solved around its main gear. The result tracks like the real thing:
    /// push straight and it goes straight; turn the tug and the tail swings the opposite way.
    ///
    /// The nose gear has a towing steering limit. Turn the bar past it and the shear pin breaks:
    /// the bar lets go and the aircraft stops (<see cref="TowShearPinEvent"/>). While the bar is on,
    /// the tug drives with <see cref="VehicleController.TowLoad"/> (lower speed and torque).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class AircraftTowable : MonoBehaviour
    {
        [SerializeField, Tooltip("<AircraftId>.json from the generator: supplies wheelbase and steering limit.")]
        TextAsset definition;
        [SerializeField, Tooltip("Nose gear to main gear, metres.")] float wheelbase = 15.6f;
        [SerializeField, Tooltip("Nose-gear towing steering limit, degrees either side.")] float steerLimit = 78f;
        [SerializeField, Tooltip("Defaults to the 'SP_TowFitting' service point under the model.")] Transform towFitting;

        static readonly List<AircraftTowable> s_All = new List<AircraftTowable>();
        public static IReadOnlyList<AircraftTowable> All => s_All;

        Rigidbody m_Body;
        AircraftRig m_Rig;
        TowBar m_Bar;
        float m_BarLength;
        float m_FittingAhead;

        public TowBar Bar => m_Bar;
        public bool IsAttached => m_Bar != null;
        public float NoseGearAngle { get; private set; }
        public float SteerLimit => steerLimit;
        public Vector3 TowFittingWorld => towFitting != null ? towFitting.position : transform.position;

#pragma warning disable 0649   // filled by JsonUtility
        [Serializable] class Definition { public GearSpec gear; }
        [Serializable] class GearSpec { public float wheelbase; public float towSteerLimitDeg; }
#pragma warning restore 0649

        void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
            m_Body.isKinematic = true;
            m_Body.interpolation = RigidbodyInterpolation.Interpolate;
            m_Rig = GetComponent<AircraftRig>();
            if (definition != null)
            {
                var d = JsonUtility.FromJson<Definition>(definition.text);
                if (d != null && d.gear != null)
                {
                    if (d.gear.wheelbase > 1f) wheelbase = d.gear.wheelbase;
                    if (d.gear.towSteerLimitDeg > 1f) steerLimit = d.gear.towSteerLimitDeg;
                }
            }
            if (towFitting == null)
                foreach (var t in GetComponentsInChildren<Transform>(true))
                    if (t.name == "SP_TowFitting") { towFitting = t; break; }
        }

        void OnEnable() => s_All.Add(this);
        void OnDisable() { s_All.Remove(this); if (IsAttached) Detach(false); }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        public bool Attach(TowBar bar)
        {
            if (bar == null || IsAttached || !bar.EyeCoupled) return false;
            m_Bar = bar;
            var h = Flat(m_Body.rotation * Vector3.forward).normalized;
            m_FittingAhead = Vector3.Dot(Flat(TowFittingWorld - m_Body.position), h);
            m_BarLength = Flat(TowFittingWorld - bar.PinWorld).magnitude;
            bar.OnAttached(this);
            var tug = bar.Tug;
            if (tug != null) tug.TowLoad = true;
            EventBus.Publish(new AircraftTowEvent(this, true));
            return true;
        }

        public void Detach(bool shearPin)
        {
            if (!IsAttached) return;
            var bar = m_Bar;
            m_Bar = null;
            var tug = bar.Tug;
            if (tug != null) tug.TowLoad = false;
            bar.OnDetached();
            if (shearPin) EventBus.Publish(new TowShearPinEvent(this, NoseGearAngle));
            EventBus.Publish(new AircraftTowEvent(this, false));
        }

        void FixedUpdate()
        {
            if (!IsAttached) return;
            if (!m_Bar.EyeCoupled) { Detach(false); return; }

            // Physics poses, not the interpolated transforms.
            var h = Flat(m_Body.rotation * Vector3.forward).normalized;
            var nose = Flat(m_Body.position);
            var main = nose - h * wheelbase;
            var arm = wheelbase + m_FittingAhead;               // main gear -> tow fitting
            var fitting = main + h * arm;
            var pin = Flat(m_Bar.PinWorld);

            var bar = fitting - pin;
            if (bar.sqrMagnitude < 1e-6f) return;
            var newFitting = pin + bar.normalized * m_BarLength;

            // Main gear moves only along its heading: |newFitting - (main + h t)| = arm.
            var d = newFitting - main;
            var dh = Vector3.Dot(d, h);
            var disc = Mathf.Max(0f, dh * dh - (d.sqrMagnitude - arm * arm));
            var root = Mathf.Sqrt(disc);
            var t = Mathf.Abs(dh - root) < Mathf.Abs(dh + root) ? dh - root : dh + root;
            var newMain = main + h * t;
            var newHeading = (newFitting - newMain).normalized;

            NoseGearAngle = Vector3.SignedAngle(newHeading, Flat(pin - newFitting), Vector3.up);
            if (Mathf.Abs(NoseGearAngle) > steerLimit)
            {
                Detach(true);
                return;
            }

            var newNose = newMain + newHeading * wheelbase;
            m_Body.MovePosition(new Vector3(newNose.x, m_Body.position.y, newNose.z));
            m_Body.MoveRotation(Quaternion.LookRotation(newHeading, Vector3.up));
            if (m_Rig != null)
            {
                m_Rig.SetSteering(NoseGearAngle);
                m_Rig.RollWheels(t);
            }
            m_Bar.Pose(m_Bar.PinWorld, new Vector3(newFitting.x, TowFittingWorld.y, newFitting.z));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_All.Clear();
    }
}
