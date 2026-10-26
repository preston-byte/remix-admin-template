using TMPro;
using UnityEngine;

namespace GroundCrew.Interaction
{
    /// <summary>
    /// Shows a 0..1 value as text, e.g. a lever position, fuel quantity or lift height.
    /// Displayed value = value01 * scale + offset.
    /// </summary>
    public class ValueReadout : MonoBehaviour
    {
        [SerializeField] TMP_Text text;
        [SerializeField] string label = "VALUE";
        [SerializeField] float scale = 100f;
        [SerializeField] float offset;
        [SerializeField] string unit = "%";

        public void SetValue01(float value01)
        {
            if (text != null) text.text = $"{label} {value01 * scale + offset:0}{unit}";
        }
    }
}
