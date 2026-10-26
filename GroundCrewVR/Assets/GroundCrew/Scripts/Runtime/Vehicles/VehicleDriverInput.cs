using UnityEngine;
using UnityEngine.InputSystem;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// Controller (and keyboard, for testing without a headset) mapping while the player is in
    /// the driver's seat. Enabled by <see cref="VehicleSeat"/> only while seated.
    ///
    ///   Right trigger       accelerate             (W)
    ///   Left trigger        brake                  (S)
    ///   Grab the wheel      steer; or left stick   (A / D)
    ///   Right stick up/down Drive / Reverse        (R = reverse, T = drive)
    ///   Right stick click   Neutral                (N)
    ///   A                   horn (hold)            (H)
    ///   B                   parking brake on/off   (P)
    ///   X                   engine start / stop    (E)
    ///   Y                   get out                (F)
    ///   Left stick click    re-centre the view     (C)
    ///
    /// The dash push buttons do the same things; both are kept because some players prefer
    /// reaching for switches and others want everything on the controllers.
    /// </summary>
    public class VehicleDriverInput : MonoBehaviour
    {
        [SerializeField] VehicleController vehicle;
        [SerializeField] SteeringWheelInput steeringWheel;
        [SerializeField] VehicleSeat seat;
        [SerializeField] VehicleAudio vehicleAudio;

        InputAction m_Throttle, m_Brake, m_Steer, m_GearAxis, m_Neutral, m_Horn, m_Park, m_Engine, m_Exit, m_Recenter;
        bool m_GearArmed = true;

        void Awake()
        {
            m_Throttle = Value("Throttle", "<XRController>{RightHand}/trigger", "<Keyboard>/w", "<Gamepad>/rightTrigger");
            m_Brake = Value("Brake", "<XRController>{LeftHand}/trigger", "<Keyboard>/s", "<Gamepad>/leftTrigger");
            m_Steer = new InputAction("Steer", InputActionType.Value);
            m_Steer.AddBinding("<XRController>{LeftHand}/thumbstick/x");
            m_Steer.AddBinding("<Gamepad>/leftStick/x");
            m_Steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            m_GearAxis = new InputAction("Gear", InputActionType.Value);
            m_GearAxis.AddBinding("<XRController>{RightHand}/thumbstick/y");
            m_GearAxis.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/r").With("Positive", "<Keyboard>/t");
            m_Neutral = Button("Neutral", "<XRController>{RightHand}/thumbstickClicked", "<Keyboard>/n");
            m_Horn = Button("Horn", "<XRController>{RightHand}/primaryButton", "<Keyboard>/h");
            m_Park = Button("ParkingBrake", "<XRController>{RightHand}/secondaryButton", "<Keyboard>/p");
            m_Engine = Button("Engine", "<XRController>{LeftHand}/primaryButton", "<Keyboard>/e");
            m_Exit = Button("Exit", "<XRController>{LeftHand}/secondaryButton", "<Keyboard>/f");
            m_Recenter = Button("Recenter", "<XRController>{LeftHand}/thumbstickClicked", "<Keyboard>/c");
        }

        static InputAction Value(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Value);
            foreach (var b in bindings) action.AddBinding(b);
            return action;
        }

        static InputAction Button(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (var b in bindings) action.AddBinding(b);
            return action;
        }

        InputAction[] All => new[] { m_Throttle, m_Brake, m_Steer, m_GearAxis, m_Neutral, m_Horn, m_Park, m_Engine, m_Exit, m_Recenter };

        void OnEnable() { foreach (var a in All) a.Enable(); }

        void OnDisable()
        {
            foreach (var a in All) a.Disable();
            if (vehicle != null) vehicle.Input = default;
            if (vehicleAudio != null) vehicleAudio.SetHorn(false);
        }

        void OnDestroy() { foreach (var a in All) a.Dispose(); }

        void Update()
        {
            if (vehicle == null) return;
            var stick = m_Steer.ReadValue<float>();
            var wheelHeld = steeringWheel != null && steeringWheel.IsHeld;
            if (steeringWheel != null && !wheelHeld) steeringWheel.ShowSteer(stick);
            vehicle.Input = new VehicleInput
            {
                Throttle = m_Throttle.ReadValue<float>(),
                Brake = m_Brake.ReadValue<float>(),
                Steer = wheelHeld ? steeringWheel.Steer : stick,
            };

            var gear = m_GearAxis.ReadValue<float>();
            if (m_GearArmed && gear > 0.7f) { vehicle.SetGearDrive(); m_GearArmed = false; }
            else if (m_GearArmed && gear < -0.7f) { vehicle.SetGearReverse(); m_GearArmed = false; }
            else if (Mathf.Abs(gear) < 0.3f) m_GearArmed = true;

            if (m_Neutral.WasPressedThisFrame()) vehicle.SetGearNeutral();
            if (m_Park.WasPressedThisFrame()) vehicle.ToggleParkingBrake();
            if (m_Engine.WasPressedThisFrame()) vehicle.ToggleEngine();
            if (vehicleAudio != null)
            {
                if (m_Horn.WasPressedThisFrame()) vehicleAudio.SetHorn(true);
                if (m_Horn.WasReleasedThisFrame()) vehicleAudio.SetHorn(false);
            }
            if (seat != null)
            {
                if (m_Recenter.WasPressedThisFrame()) seat.Recenter();
                if (m_Exit.WasPressedThisFrame()) seat.Exit();
            }
        }
    }
}
