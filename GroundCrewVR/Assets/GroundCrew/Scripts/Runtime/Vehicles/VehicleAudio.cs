using UnityEngine;

namespace GroundCrew.Vehicles
{
    public enum EngineSound { None, Diesel, BigDiesel, Electric }

    /// <summary>
    /// Engine, horn and reverse-beeper sound synthesised on the audio thread, so vehicles are
    /// audible before any recorded sound assets exist. Swap in recorded loops later by giving
    /// the AudioSource a clip and turning <see cref="synthesise"/> off.
    ///
    /// Diesel: a sawtooth at the firing frequency plus a sub-harmonic and filtered noise that
    /// grows with load. Horn: two detuned tones. Reverse alarm: the usual 1.1 kHz beep, 0.5 s on
    /// / 0.5 s off, whenever reverse is selected with the engine running.
    ///
    /// Only plain fields cross from the main thread to the audio thread (no Unity API calls in
    /// <see cref="OnAudioFilterRead"/>).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class VehicleAudio : MonoBehaviour
    {
        [SerializeField] VehicleController vehicle;
        [SerializeField] EngineSound engine = EngineSound.Diesel;
        [SerializeField, Range(0f, 1f)] float volume = 0.5f;
        [SerializeField] bool synthesise = true;
        [SerializeField] bool reverseAlarm = true;

        AudioSource m_Source;
        int m_SampleRate = 48000;
        float m_HornUntil;
        bool m_HornHeld;

        // main thread -> audio thread
        volatile float m_TargetRpm;      // 0 = off, 1 = idle .. 3 = full
        volatile float m_TargetLoad;
        volatile bool m_Horn;
        volatile bool m_Beeper;

        // audio thread state
        float m_Rpm, m_Load, m_HornGain, m_BeepGain;
        double m_Phase, m_SubPhase, m_HornPhaseA, m_HornPhaseB, m_BeepPhase;
        long m_Samples;
        float m_Lp;
        uint m_Noise = 22222;

        void Awake()
        {
            m_Source = GetComponent<AudioSource>();
            m_SampleRate = AudioSettings.outputSampleRate;
            if (!synthesise) return;
            m_Source.clip = AudioClip.Create("vehicle-synth", 1024, 1, m_SampleRate, false);
            m_Source.loop = true;
            m_Source.spatialBlend = 1f;
            m_Source.rolloffMode = AudioRolloffMode.Linear;
            m_Source.minDistance = 3f;
            m_Source.maxDistance = 45f;
            m_Source.dopplerLevel = 0f;
            m_Source.playOnAwake = false;
            m_Source.Play();
        }

        public void Honk(float seconds) => m_HornUntil = Time.time + seconds;
        /// <summary>A short blast (dash horn button).</summary>
        public void HonkShort() => Honk(0.6f);
        public void SetHorn(bool held) => m_HornHeld = held;

        void Update()
        {
            if (vehicle == null) return;
            var running = vehicle.EngineRunning && engine != EngineSound.None;
            var speed01 = Mathf.Clamp01(Mathf.Abs(vehicle.SpeedKmh) / 25f);
            var throttle = Mathf.Clamp01(vehicle.Input.Throttle);
            m_TargetRpm = running ? 1f + 1.6f * Mathf.Max(speed01, throttle * 0.8f) : 0f;
            m_TargetLoad = running ? throttle : 0f;
            m_Horn = m_HornHeld || Time.time < m_HornUntil;
            m_Beeper = reverseAlarm && vehicle.ReverseSelected;
        }

        float Noise()
        {
            m_Noise ^= m_Noise << 13; m_Noise ^= m_Noise >> 17; m_Noise ^= m_Noise << 5;
            return (m_Noise & 0xFFFF) / 32768f - 1f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!synthesise) return;
            var sr = (double)m_SampleRate;
            var big = engine == EngineSound.BigDiesel;
            var electric = engine == EngineSound.Electric;
            var idleHz = big ? 22.0 : 30.0;
            for (var i = 0; i < data.Length; i += channels)
            {
                m_Rpm += (m_TargetRpm - m_Rpm) * 0.00008f;
                m_Load += (m_TargetLoad - m_Load) * 0.0004f;
                m_HornGain += ((m_Horn ? 1f : 0f) - m_HornGain) * 0.004f;
                var beepOn = m_Beeper && (m_Samples % m_SampleRate) < m_SampleRate / 2;
                m_BeepGain += ((beepOn ? 1f : 0f) - m_BeepGain) * 0.01f;
                m_Samples++;

                var s = 0f;
                if (m_Rpm > 0.01f)
                {
                    var on = Mathf.Clamp01(m_Rpm);
                    var hz = idleHz * m_Rpm;
                    if (electric)
                    {
                        m_Phase += 30.0 * m_Rpm * 8.0 / sr;
                        s += Mathf.Sin((float)(m_Phase * 2.0 * Mathf.PI)) * 0.12f * on;
                    }
                    else
                    {
                        m_Phase += hz / sr;
                        m_SubPhase += hz * 0.5 / sr;
                        var saw = (float)(m_Phase - System.Math.Floor(m_Phase)) * 2f - 1f;
                        var sub = Mathf.Sin((float)(m_SubPhase * 2.0 * Mathf.PI));
                        var raw = saw * 0.45f + sub * 0.35f + Noise() * (0.08f + 0.25f * m_Load);
                        m_Lp += (raw - m_Lp) * (big ? 0.05f : 0.08f);     // muffler
                        s += m_Lp * on * (0.55f + 0.45f * m_Load);
                    }
                }
                if (m_HornGain > 0.001f)
                {
                    m_HornPhaseA += 415.0 / sr;
                    m_HornPhaseB += 523.0 / sr;
                    var a = Mathf.Sin((float)(m_HornPhaseA * 2.0 * Mathf.PI));
                    var b = Mathf.Sin((float)(m_HornPhaseB * 2.0 * Mathf.PI));
                    s += Mathf.Clamp((a + b) * 1.6f, -1f, 1f) * 0.35f * m_HornGain;
                }
                if (m_BeepGain > 0.001f)
                {
                    m_BeepPhase += 1100.0 / sr;
                    s += Mathf.Sin((float)(m_BeepPhase * 2.0 * Mathf.PI)) * 0.25f * m_BeepGain;
                }

                s *= volume;
                for (var c = 0; c < channels; c++) data[i + c] = s;
            }
            if (m_Phase > 1e6) m_Phase -= System.Math.Floor(m_Phase);
        }
    }
}
