using System.Collections.Generic;
using System.Text;
using GroundCrew.Interaction;
using GroundCrew.Player;
using GroundCrew.Vehicles;
using UnityEngine;

namespace GroundCrew.UI
{
    /// <summary>
    /// Wrist-tablet page for vehicles. VEHICLES opens it; then
    ///   PREV / NEXT  pick a vehicle from the catalog
    ///   SPAWN        put it on the ground in front of you, side-on, driver's door towards you
    ///   FIX          set the nearest vehicle back on its wheels (stuck, tipped, wedged)
    ///   CLEAR        remove everything you spawned
    ///   ASSIST       driving assists on / off
    ///   BACK         back to the normal tablet page
    /// </summary>
    public class VehicleSpawnMenu : MonoBehaviour
    {
        [SerializeField] WristTablet tablet;
        [SerializeField] VehicleCatalog catalog;
        [SerializeField] GameObject homeRow;
        [SerializeField] GameObject menuRow;
        [SerializeField] PressButton openButton, prevButton, nextButton, spawnButton, backButton, fixButton, clearButton, assistButton;
        [SerializeField] float fixRadius = 15f;

        readonly List<GameObject> m_Spawned = new List<GameObject>();
        readonly StringBuilder m_Text = new StringBuilder(256);
        int m_Index;
        bool m_Open;
        string m_Message = "";

        void OnEnable()
        {
            Hook(openButton, Open);
            Hook(backButton, Close);
            Hook(prevButton, () => Select(-1));
            Hook(nextButton, () => Select(1));
            Hook(spawnButton, Spawn);
            Hook(fixButton, FixNearest);
            Hook(clearButton, Clear);
            Hook(assistButton, () => { ComfortSettings.ToggleDrivingAssists(); Say("Driving assists " + (ComfortSettings.DrivingAssists ? "ON" : "OFF")); });
            ShowRows();
        }

        void OnDisable()
        {
            foreach (var b in m_Hooks.Keys) if (b != null) b.Pressed -= m_Hooks[b];
            m_Hooks.Clear();
        }

        readonly Dictionary<PressButton, System.Action<PressButton>> m_Hooks = new Dictionary<PressButton, System.Action<PressButton>>();

        void Hook(PressButton button, System.Action action)
        {
            if (button == null) return;
            System.Action<PressButton> handler = _ => action();
            button.Pressed += handler;
            m_Hooks[button] = handler;
        }

        void Open() { m_Open = true; ShowRows(); Redraw(); }

        void Close()
        {
            m_Open = false;
            ShowRows();
            if (tablet != null) tablet.PageText = null;
        }

        void ShowRows()
        {
            if (homeRow != null) homeRow.SetActive(!m_Open);
            if (menuRow != null) menuRow.SetActive(m_Open);
        }

        int Count => catalog != null ? catalog.Entries.Count : 0;

        void Select(int step)
        {
            if (Count == 0) return;
            m_Index = (m_Index + step + Count) % Count;
            m_Message = "";
            Redraw();
        }

        void Say(string message) { m_Message = message; Redraw(); }

        void Redraw()
        {
            if (tablet == null || !m_Open) return;
            m_Text.Clear();
            m_Text.Append("<b>VEHICLES</b>  ").Append(m_Spawned.Count).Append(" spawned   assists ").Append(ComfortSettings.DrivingAssists ? "ON" : "OFF").Append('\n');
            if (Count == 0) m_Text.Append("No vehicle catalog - run Ground Crew > Vehicles > Add Vehicles to Sandbox.\n");
            for (var i = -2; i <= 2 && Count > 0; i++)
            {
                var k = (m_Index + i + Count) % Count;
                if (Count < 5 && (i < -Count / 2 || i > Count / 2)) continue;
                var name = catalog.Entries[k].displayName;
                m_Text.Append(i == 0 ? "<color=#FFD166>> " + name + "</color>" : "<color=#9AA7B8>   " + name + "</color>").Append('\n');
            }
            if (!string.IsNullOrEmpty(m_Message)) m_Text.Append("<color=#7CFC9A>").Append(m_Message).Append("</color>");
            tablet.PageText = m_Text.ToString();
        }

        Transform Head => Camera.main != null ? Camera.main.transform : null;

        void Spawn()
        {
            var head = Head;
            if (head == null || Count == 0) return;
            var entry = catalog.Entries[m_Index];
            if (entry.prefab == null) { Say("Missing prefab"); return; }

            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
            forward.Normalize();
            // Side-on with the driver's (left) side towards you, its middle in your line of sight.
            var rotation = Quaternion.LookRotation(Vector3.Cross(forward, Vector3.up), Vector3.up);
            var half = new Vector3(1.5f, 1.3f, entry.length * 0.5f + 0.3f);
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var p = head.position + forward * (3.5f + attempt * 3f);
                var ground = GroundHeight(p, head.position.y);
                var centre = new Vector3(p.x, ground + 1.8f, p.z);
                if (Physics.CheckBox(centre, half, rotation, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                var go = Instantiate(entry.prefab, new Vector3(p.x, ground + 0.05f, p.z), rotation);
                go.name = entry.displayName;
                m_Spawned.Add(go);
                Say(entry.displayName + " spawned - grab the yellow handle to drive");
                return;
            }
            Say("No room in front of you - turn to open ground");
        }

        static float GroundHeight(Vector3 p, float fallback)
        {
            var hits = Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
            var best = float.NegativeInfinity;
            foreach (var h in hits)
                if (h.collider.attachedRigidbody == null && h.point.y > best) best = h.point.y;
            return float.IsNegativeInfinity(best) ? fallback - 1.6f : best;
        }

        void FixNearest()
        {
            var head = Head;
            if (head == null) return;
            VehicleController best = null;
            var bestDistance = fixRadius;
            foreach (var v in FindObjectsByType<VehicleController>(FindObjectsSortMode.None))
            {
                var d = Vector3.Distance(v.transform.position, head.position);
                if (d < bestDistance) { bestDistance = d; best = v; }
            }
            if (best == null) { Say("No vehicle within " + fixRadius.ToString("0") + " m"); return; }
            var rb = best.Body;
            var yaw = Quaternion.Euler(0f, best.transform.eulerAngles.y, 0f);
            var pos = best.transform.position;
            pos.y = GroundHeight(pos, pos.y) + 0.3f;
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = pos;
                rb.rotation = yaw;
            }
            best.transform.SetPositionAndRotation(pos, yaw);
            best.SetParkingBrake(true);
            Say(best.DisplayName + " reset");
        }

        void Clear()
        {
            foreach (var go in m_Spawned) if (go != null) Destroy(go);
            var n = m_Spawned.Count;
            m_Spawned.Clear();
            Say(n + " removed");
        }
    }
}
