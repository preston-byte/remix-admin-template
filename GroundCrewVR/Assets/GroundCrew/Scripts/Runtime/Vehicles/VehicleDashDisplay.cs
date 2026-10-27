using TMPro;
using UnityEngine;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// A small display on the dash, facing the driver: gear and speed, or exactly why the
    /// vehicle won't move ("PARKING BRAKE ON - press B"), and what the driving assists just did.
    /// </summary>
    public class VehicleDashDisplay : MonoBehaviour
    {
        [SerializeField] VehicleController vehicle;
        [SerializeField] TMP_Text text;

        float m_Next;
        string m_Last;

        void Update()
        {
            if (vehicle == null || text == null || Time.time < m_Next) return;
            m_Next = Time.time + 0.2f;
            var line = vehicle.StatusLine;
            if (line == m_Last) return;
            m_Last = line;
            text.text = line;
        }
    }
}
