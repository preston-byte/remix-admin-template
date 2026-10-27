using System;
using GroundCrew.Interaction;
using GroundCrew.Player;
using UnityEngine;

namespace GroundCrew.Vehicles
{
    public enum AxisMotion { Rotate, Translate }

    /// <summary>
    /// One hydraulic function on a piece of equipment, run by hold-to-run push buttons: the belt
    /// loader's belt lift, a service platform, the de-icer's slew / boom / telescope.
    ///
    /// The axis comes from a helper node placed by the generator (<see cref="axisHint"/> =
    /// pivot + axis direction), captured in the parent's space at start, so it doesn't depend
    /// on importer axis conventions. Rotation follows Unity's rule (positive = clockwise looking
    /// along the axis); the generator places each hint so that "plus" means up / out / raise.
    ///
    /// As on real equipment, functions can require the parking brake, and can stop the vehicle
    /// driving while they're away from their stowed position.
    /// </summary>
    public class HydraulicAxis : MonoBehaviour
    {
        [SerializeField] string functionName = "Lift";
        [SerializeField] Transform target;
        [SerializeField] Transform axisHint;
        [SerializeField] AxisMotion motion = AxisMotion.Rotate;
        [SerializeField, Tooltip("Degrees or metres.")] float min;
        [SerializeField] float max = 1f;
        [SerializeField, Tooltip("Degrees or metres per second.")] float speed = 0.2f;
        [SerializeField] PressButton plusButton;
        [SerializeField] PressButton minusButton;
        [SerializeField] VehicleController vehicle;
        [SerializeField] bool requireParkingBrake = true;
        [SerializeField, Tooltip("Stop the vehicle driving while this isn't at its stowed value.")] bool inhibitDriveWhenDeployed = true;
        [SerializeField] float stowedValue;

        Vector3 m_RestPos;
        Quaternion m_RestRot;
        Vector3 m_AxisLocal;
        float m_Command;

        public string FunctionName => functionName;
        public float Value { get; private set; }
        public bool IsStowed => Mathf.Abs(Value - stowedValue) < (motion == AxisMotion.Rotate ? 0.5f : 0.02f);
        public event Action<HydraulicAxis> Moved;

        void Awake()
        {
            if (target == null) target = transform;
            m_RestPos = target.localPosition;
            m_RestRot = target.localRotation;
            var parent = target.parent;
            var axisWorld = axisHint != null ? (axisHint.position - target.position).normalized : Vector3.up;
            m_AxisLocal = parent != null ? parent.InverseTransformDirection(axisWorld) : axisWorld;
            Value = Mathf.Clamp(stowedValue, min, max);
            Apply();
        }

        /// <summary>Run the function from code (AI, tablet): -1, 0 or +1 for this frame.</summary>
        public void Command(float direction) => m_Command = Mathf.Clamp(direction, -1f, 1f);

        void Update()
        {
            var dir = m_Command;
            m_Command = 0f;
            if (plusButton != null && plusButton.IsHeld) dir += 1f;
            if (minusButton != null && minusButton.IsHeld) dir -= 1f;
            if (dir != 0f && requireParkingBrake && vehicle != null && !vehicle.ParkingBrake)
            {
                if (ComfortSettings.DrivingAssists && Mathf.Abs(vehicle.SpeedKmh) < 1f) vehicle.SetParkingBrake(true);
                else vehicle.Notify("Set the parking brake to use " + functionName.ToLowerInvariant());
            }
            var allowed = !requireParkingBrake || vehicle == null || vehicle.ParkingBrake;
            if (dir != 0f && allowed)
            {
                var next = Mathf.Clamp(Value + dir * speed * Time.deltaTime, min, max);
                if (!Mathf.Approximately(next, Value))
                {
                    Value = next;
                    Apply();
                    Moved?.Invoke(this);
                }
            }
            if (vehicle != null && inhibitDriveWhenDeployed)
            {
                if (IsStowed) vehicle.RemoveDriveInhibit(this);
                else vehicle.AddDriveInhibit(this, functionName + " not stowed");
            }
        }

        void Apply()
        {
            if (motion == AxisMotion.Rotate)
                target.localRotation = Quaternion.AngleAxis(Value, m_AxisLocal) * m_RestRot;
            else
                target.localPosition = m_RestPos + m_AxisLocal * Value;
        }
    }
}
