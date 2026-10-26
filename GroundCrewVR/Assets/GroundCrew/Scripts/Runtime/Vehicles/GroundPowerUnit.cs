using GroundCrew.Core;
using GroundCrew.Interaction;
using UnityEngine;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// Towed diesel ground power unit: ENGINE starts / stops the diesel, OUTPUT closes the
    /// 115 V 400 Hz contactor (only with the engine running). Lamps on the control panel show
    /// RUN and OUTPUT. The unit is a <see cref="VehicleController"/> trailer (with an engine for
    /// the sound) so it's towed by the baggage tractor like a cart.
    ///
    /// Plugging the cable into the aircraft's <c>SP_GPU</c> receptacle arrives with the Phase 6
    /// services; <see cref="GroundPowerEvent"/> already tells the aircraft when power is on.
    /// </summary>
    public class GroundPowerUnit : MonoBehaviour
    {
        [SerializeField] VehicleController unit;
        [SerializeField] PressButton engineButton;
        [SerializeField] PressButton outputButton;
        [SerializeField] GameObject runLamp;
        [SerializeField] GameObject outputLamp;

        public bool OutputOn { get; private set; }
        public bool Running => unit != null && unit.EngineRunning;

        void OnEnable()
        {
            if (engineButton != null) engineButton.Pressed += OnEngine;
            if (outputButton != null) outputButton.Pressed += OnOutput;
        }

        void OnDisable()
        {
            if (engineButton != null) engineButton.Pressed -= OnEngine;
            if (outputButton != null) outputButton.Pressed -= OnOutput;
        }

        void OnEngine(PressButton _) { if (unit != null) unit.ToggleEngine(); }

        void OnOutput(PressButton _) => SetOutput(!OutputOn);

        public void SetOutput(bool on)
        {
            if (on && !Running) return;
            if (OutputOn == on) return;
            OutputOn = on;
            EventBus.Publish(new GroundPowerEvent(this, on));
        }

        void Update()
        {
            if (OutputOn && !Running) SetOutput(false);
            if (runLamp != null && runLamp.activeSelf != Running) runLamp.SetActive(Running);
            if (outputLamp != null && outputLamp.activeSelf != OutputOn) outputLamp.SetActive(OutputOn);
        }
    }
}
