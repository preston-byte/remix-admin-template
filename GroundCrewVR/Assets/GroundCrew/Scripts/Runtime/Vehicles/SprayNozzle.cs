using GroundCrew.Core;
using GroundCrew.Interaction;
using UnityEngine;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// De-icing spray from the basket nozzle: the SPRAY button toggles it while the truck's
    /// engine (and so the pump) runs, fluid is used at <see cref="litresPerSecond"/>, and the
    /// visible spray is a particle system on the nozzle.
    ///
    /// Contamination, coverage and holdover time belong to the weather system (Phase 8); they
    /// will listen to <see cref="SprayEvent"/> and raycast along the nozzle.
    /// </summary>
    public class SprayNozzle : MonoBehaviour
    {
        [SerializeField] ParticleSystem spray;
        [SerializeField] PressButton toggleButton;
        [SerializeField] VehicleController vehicle;
        [SerializeField] float litres = 6000f;
        [SerializeField] float litresPerSecond = 2.5f;

        public bool Spraying { get; private set; }
        public float Litres => litres;

        void OnEnable() { if (toggleButton != null) toggleButton.Pressed += OnToggle; }
        void OnDisable() { if (toggleButton != null) toggleButton.Pressed -= OnToggle; SetSpraying(false); }

        void OnToggle(PressButton _) => SetSpraying(!Spraying);

        public void SetSpraying(bool on)
        {
            if (on && (litres <= 0f || (vehicle != null && !vehicle.EngineRunning))) on = false;
            if (on == Spraying) return;
            Spraying = on;
            if (spray != null)
            {
                if (on) spray.Play();
                else spray.Stop();
            }
            EventBus.Publish(new SprayEvent(this, on, litres));
        }

        void Update()
        {
            if (!Spraying) return;
            litres = Mathf.Max(0f, litres - litresPerSecond * Time.deltaTime);
            if (litres <= 0f || (vehicle != null && !vehicle.EngineRunning)) SetSpraying(false);
        }
    }
}
