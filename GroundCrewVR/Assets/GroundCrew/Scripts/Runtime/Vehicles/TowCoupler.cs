using System;
using System.Collections.Generic;
using GroundCrew.Core;
using GroundCrew.Interaction;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// The towing eye of a drawbar or tow bar. Carry the bar's handle (grip) so the eye is over a
    /// free hitch and let go: it drops on the pin and couples. To uncouple, hold the handle and
    /// pull the trigger (pulling the pin).
    ///
    /// Coupled, the bar's Rigidbody is held to the hitch by a ConfigurableJoint with its position
    /// locked and rotation free. While uncoupled, an optional spring stands a cart drawbar up
    /// (as real ones do) so it's easy to grab and the cart's automatic brake comes on.
    /// </summary>
    public class TowCoupler : MonoBehaviour
    {
        [SerializeField] Rigidbody body;
        [SerializeField] Transform eye;
        [SerializeField] HitchType accepts = HitchType.Pintle;
        [SerializeField] float captureRadius = 0.18f;
        [SerializeField] XRBaseInteractable handle;
        [SerializeField] TowHitch startCoupledTo;

        [Header("Drawbar pivot (carts only)")]
        [SerializeField, Tooltip("Joint between the drawbar and its cart; its pitch spring lifts the bar when uncoupled.")]
        ConfigurableJoint pivotJoint;
        [SerializeField] float standUpAngle = 70f;
        [SerializeField] float standUpSpring = 400f;

        ConfigurableJoint m_Joint;

        public TowHitch Hitch { get; private set; }
        public bool IsCoupled => Hitch != null;
        public Vector3 EyeWorld => eye != null ? eye.position : transform.position;
        public Rigidbody Body => body;
        public event Action<TowCoupler> Changed;

        void Start()
        {
            if (startCoupledTo != null) CoupleTo(startCoupledTo);
            else ApplyPivotSpring();
        }

        void OnEnable()
        {
            if (handle == null) return;
            handle.selectExited.AddListener(OnHandleReleased);
            handle.activated.AddListener(OnHandleTrigger);
        }

        void OnDisable()
        {
            if (handle == null) return;
            handle.selectExited.RemoveListener(OnHandleReleased);
            handle.activated.RemoveListener(OnHandleTrigger);
        }

        void OnHandleReleased(SelectExitEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor) return;
            if (!IsCoupled && TryCouple()) Haptics.Pulse(args.interactorObject, 0.6f, 0.08f);
        }

        void OnHandleTrigger(ActivateEventArgs args)
        {
            if (!IsCoupled) return;
            Uncouple();
            Haptics.Pulse(args.interactorObject, 0.4f, 0.05f);
        }

        /// <summary>Couple to the nearest free, compatible hitch within reach of the eye.</summary>
        public bool TryCouple()
        {
            TowHitch best = null;
            var bestDistance = captureRadius;
            var ownVehicle = GetComponentInParent<VehicleController>();
            foreach (var h in TowHitch.All)
            {
                if (!h.IsFree || h.Type != accepts) continue;
                if (ownVehicle != null && h.Vehicle == ownVehicle) continue;
                if (pivotJoint != null && h.Body == pivotJoint.connectedBody) continue;   // own cart
                var d = Vector3.Distance(h.transform.position, EyeWorld);
                if (d < bestDistance) { bestDistance = d; best = h; }
            }
            if (best == null) return false;
            CoupleTo(best);
            return true;
        }

        public void CoupleTo(TowHitch hitch)
        {
            if (hitch == null || body == null || hitch.Body == null) return;
            if (IsCoupled) Uncouple();
            Hitch = hitch;
            hitch.Coupled = this;
            CreateJoint();
            ApplyPivotSpring();
            EventBus.Publish(new CouplingEvent(this, hitch, true));
            Changed?.Invoke(this);
        }

        public void Uncouple()
        {
            if (!IsCoupled) return;
            var hitch = Hitch;
            DestroyJoint();
            hitch.Coupled = null;
            Hitch = null;
            ApplyPivotSpring();
            EventBus.Publish(new CouplingEvent(this, hitch, false));
            Changed?.Invoke(this);
        }

        /// <summary>
        /// Keep the logical coupling but remove the physical joint, for a tow bar whose pose is
        /// driven by the towed aircraft (see <see cref="AircraftTowable"/>).
        /// </summary>
        public void SuspendJoint(bool suspend)
        {
            if (!IsCoupled) return;
            if (suspend) DestroyJoint();
            else if (m_Joint == null) CreateJoint();
        }

        void CreateJoint()
        {
            m_Joint = body.gameObject.AddComponent<ConfigurableJoint>();
            m_Joint.autoConfigureConnectedAnchor = false;
            m_Joint.anchor = body.transform.InverseTransformPoint(EyeWorld);
            m_Joint.connectedBody = Hitch.Body;
            m_Joint.connectedAnchor = Hitch.Body.transform.InverseTransformPoint(Hitch.transform.position);
            m_Joint.xMotion = m_Joint.yMotion = m_Joint.zMotion = ConfigurableJointMotion.Locked;
            m_Joint.angularXMotion = m_Joint.angularYMotion = m_Joint.angularZMotion = ConfigurableJointMotion.Free;
            m_Joint.enableCollision = false;
        }

        void DestroyJoint()
        {
            if (m_Joint != null) Destroy(m_Joint);
            m_Joint = null;
        }

        void ApplyPivotSpring()
        {
            if (pivotJoint == null) return;
            var drive = pivotJoint.angularXDrive;
            drive.positionSpring = IsCoupled ? 0f : standUpSpring;
            drive.positionDamper = IsCoupled ? 0f : standUpSpring * 0.1f;
            drive.maximumForce = float.MaxValue;
            pivotJoint.angularXDrive = drive;
            // Joint X axis = the drawbar's lateral axis; negative rotation lifts the eye.
            pivotJoint.targetRotation = IsCoupled ? Quaternion.identity : Quaternion.Euler(-standUpAngle, 0f, 0f);
        }
    }
}
