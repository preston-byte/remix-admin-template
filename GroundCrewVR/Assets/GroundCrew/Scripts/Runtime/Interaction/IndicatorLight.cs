using UnityEngine;

namespace GroundCrew.Interaction
{
    /// <summary>
    /// A lamp that swaps between an "off" and an emissive "on" material: beacons, connection
    /// lights, jet-bridge status lamps. Material swapping (not a real Light) keeps it free on
    /// Quest and friendly to the SRP batcher.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class IndicatorLight : MonoBehaviour
    {
        [SerializeField] Material onMaterial;
        [SerializeField] Material offMaterial;
        [SerializeField] bool startOn;
        [SerializeField] bool blink;
        [SerializeField] float blinkHz = 1.5f;

        Renderer m_Renderer;
        bool m_On;
        int m_Lit = -1;

        public bool IsOn => m_On;

        void Awake()
        {
            m_Renderer = GetComponent<Renderer>();
            SetOn(startOn);
        }

        public void SetOn(bool on)
        {
            m_On = on;
            if (!blink) Apply(on);
        }

        public void Toggle() => SetOn(!m_On);

        void Update()
        {
            if (blink) Apply(m_On && Mathf.Repeat(Time.time * blinkHz, 1f) < 0.5f);
        }

        void Apply(bool lit)
        {
            var state = lit ? 1 : 0;
            if (state == m_Lit || m_Renderer == null) return;
            m_Lit = state;
            m_Renderer.sharedMaterial = lit ? onMaterial : offMaterial;
        }
    }
}
