using System.Collections.Generic;
using GroundCrew.Core;
using GroundCrew.Interaction;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// Getting in and out of a vehicle.
    ///
    /// Grab the "GRAB TO DRIVE" handle by the driver's door: the XR Origin is parented to the
    /// vehicle with your eyes on the seat's eye point, facing forward, and walking / teleporting
    /// / turning are switched off so the sticks are free for driving. Press Y (or F) to get out:
    /// you are put down beside the driver's door, upright, and locomotion comes back.
    ///
    /// Only the local player is handled here; a remote player would instead show an avatar in
    /// the seat while the vehicle with authority replicates its <see cref="VehicleInput"/>.
    /// </summary>
    public class VehicleSeat : MonoBehaviour
    {
        [SerializeField] VehicleController vehicle;
        [SerializeField] Transform eyePoint;
        [SerializeField] Transform exitPoint;
        [SerializeField] VehicleDriverInput driverInput;
        [SerializeField, Tooltip("Grab this to get in.")] XRBaseInteractable enterHandle;

        XROrigin m_Origin;
        Transform m_PreviousParent;
        readonly List<Behaviour> m_Disabled = new List<Behaviour>();
        CharacterController m_Character;

        /// <summary>The seat the local player is sitting in, if any.</summary>
        public static VehicleSeat Current { get; private set; }

        public VehicleController Vehicle => vehicle;
        public bool IsOccupied => Current == this;

        void Awake()
        {
            if (driverInput != null) driverInput.enabled = false;
        }

        void OnEnable()
        {
            if (enterHandle != null) enterHandle.selectEntered.AddListener(OnHandleGrabbed);
        }

        void OnDisable()
        {
            if (enterHandle != null) enterHandle.selectEntered.RemoveListener(OnHandleGrabbed);
            if (Current == this) Exit();
        }

        void OnHandleGrabbed(SelectEnterEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor || Current == this) return;
            // Let go of the handle first, otherwise the hand stays "selecting" it inside the cab.
            var manager = enterHandle.interactionManager;
            if (manager != null) manager.SelectExit(args.interactorObject, enterHandle);
            Haptics.Pulse(args.interactorObject, 0.4f, 0.05f);
            Enter();
        }

        public void Enter()
        {
            if (Current != null) Current.Exit();
            m_Origin = FindAnyObjectByType<XROrigin>();
            if (m_Origin == null || eyePoint == null)
            {
                Debug.LogWarning("[Vehicle] No XR Origin in the scene (or no eye point) - cannot sit down.", this);
                return;
            }

            m_Disabled.Clear();
            foreach (var provider in m_Origin.GetComponentsInChildren<LocomotionProvider>(true))
            {
                if (!provider.enabled) continue;
                provider.enabled = false;
                m_Disabled.Add(provider);
            }
            m_Character = m_Origin.GetComponent<CharacterController>();
            if (m_Character != null) m_Character.enabled = false;

            m_PreviousParent = m_Origin.transform.parent;
            m_Origin.transform.SetParent(transform, true);
            Recenter();

            Current = this;
            vehicle.Occupied = true;
            if (driverInput != null) driverInput.enabled = true;
            EventBus.Publish(new VehicleSeatEvent(vehicle, true, vehicle.ParkingBrake));
        }

        /// <summary>Put the player's eyes back on the seat's eye point, facing forward.</summary>
        public void Recenter()
        {
            if (m_Origin == null || eyePoint == null) return;
            m_Origin.MatchOriginUpCameraForward(eyePoint.up, eyePoint.forward);
            m_Origin.MoveCameraToWorldLocation(eyePoint.position);
        }

        public void Exit()
        {
            if (Current != this || m_Origin == null) return;
            Current = null;
            if (driverInput != null) driverInput.enabled = false;
            vehicle.Occupied = false;

            var origin = m_Origin.transform;
            origin.SetParent(m_PreviousParent, true);
            origin.rotation = Quaternion.Euler(0f, origin.eulerAngles.y, 0f);
            var exit = exitPoint != null ? exitPoint.position : transform.position + transform.right * 2f;
            var camera = m_Origin.Camera.transform.position;
            origin.position += new Vector3(exit.x - camera.x, 0f, exit.z - camera.z);
            origin.position = new Vector3(origin.position.x, exit.y, origin.position.z);

            if (m_Character != null) m_Character.enabled = true;
            foreach (var b in m_Disabled) if (b != null) b.enabled = true;
            m_Disabled.Clear();
            EventBus.Publish(new VehicleSeatEvent(vehicle, false, vehicle.ParkingBrake));
        }
    }
}
