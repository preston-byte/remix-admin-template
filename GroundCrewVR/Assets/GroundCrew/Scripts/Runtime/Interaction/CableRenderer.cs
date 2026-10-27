using UnityEngine;

namespace GroundCrew.Interaction
{
    /// <summary>
    /// Draws a cable/hose between two points with a cheap sag curve that rests on the ground
    /// when slack. No physics rope: a simulated rope with dozens of rigidbodies per cable is
    /// far too expensive on Quest 2 and jitters when grabbed.
    ///
    /// Sag uses the "V" approximation for a cable of length L spanning distance d
    /// (sag = sqrt(L^2 - d^2) / 2) shaped as a parabola, then clamped to ground height.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class CableRenderer : MonoBehaviour
    {
        [SerializeField] Transform start;
        [SerializeField] Transform end;
        [SerializeField] float cableLength = 5f;
        [SerializeField, Range(2, 48)] int segments = 20;
        [SerializeField, Tooltip("World height the cable cannot sink below (apron surface + radius).")] float groundHeight = 0.02f;
        [SerializeField, Range(0f, 1f), Tooltip("Cables are stiff; 1 = hangs like a perfect rope.")] float slackFactor = 0.85f;

        LineRenderer m_Line;
        Vector3[] m_Points;

        /// <summary>Attach it to new ends (e.g. a hose carried from the truck to the aircraft).</summary>
        public void Connect(Transform from, Transform to, float length)
        {
            start = from;
            end = to;
            cableLength = Mathf.Max(0.1f, length);
        }

        void Awake()
        {
            m_Line = GetComponent<LineRenderer>();
            m_Line.useWorldSpace = true;
            m_Points = new Vector3[segments + 1];
            m_Line.positionCount = m_Points.Length;
        }

        void LateUpdate()
        {
            if (start == null || end == null) return;

            var a = start.position;
            var b = end.position;
            var d = Vector3.Distance(a, b);
            var sag = d < cableLength ? 0.5f * Mathf.Sqrt(cableLength * cableLength - d * d) * slackFactor : 0f;

            for (var i = 0; i < m_Points.Length; i++)
            {
                var t = i / (float)segments;
                var p = Vector3.Lerp(a, b, t);
                p.y -= 4f * sag * t * (1f - t);
                p.y = Mathf.Max(p.y, groundHeight);
                m_Points[i] = p;
            }
            m_Line.SetPositions(m_Points);
        }
    }
}
