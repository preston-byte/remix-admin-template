using System;
using System.Collections.Generic;
using System.Text;
using GroundCrew.Aircraft;
using GroundCrew.Airport;
using GroundCrew.Core;
using GroundCrew.Interaction;
using GroundCrew.Operations;
using GroundCrew.Vehicles;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace GroundCrew.UI
{
    /// <summary>
    /// The ramp agent's iPad. It rides at your waist (front-left, tilted up) and you press its
    /// buttons with a fingertip; grab it to bring it closer, let go and it goes back to the holster.
    ///
    /// Tabs along the top: STAND (pick the stand or gate you're working, set it all up or clear
    /// it, call the crew), BRIDGE (dock / retract the jet bridge), CREW (pilots and cabin crew),
    /// GROUND (AI ground crew: chocks, cones, stairs or bridge, belt loader), AIRCRAFT (doors,
    /// beacon, flaps, flight-deck light). Eight keys underneath change with the page.
    /// </summary>
    public class OpsTablet : MonoBehaviour
    {
        enum Page { Stand, Bridge, Crew, Ground, Aircraft }

        [SerializeField] TMP_Text headerText, bodyText, footerText;
        [SerializeField] PressButton[] tabs = new PressButton[5];
        [SerializeField] PressButton[] keys = new PressButton[8];
        [SerializeField] XRGrabInteractable grab;
        [SerializeField, Tooltip("The solid body you grab it by.")] Collider bodyCollider;
        [SerializeField, Tooltip("Holster offset from the head: right, down, forward (m).")] Vector3 holsterOffset = new Vector3(-0.2f, -0.5f, 0.3f);
        [SerializeField, Tooltip("Tilt of the screen towards your face in the holster (deg).")] float holsterTilt = 50f;

        readonly StringBuilder m_Text = new StringBuilder(1024);
        readonly Action[] m_Actions = new Action[8];
        readonly Dictionary<PressButton, Action<PressButton>> m_Hooks = new Dictionary<PressButton, Action<PressButton>>();
        Page m_Page;
        int m_Stand = -1;
        float m_NextRedraw;
        string m_LastMessage = "";
        Transform m_Head;

        StandInfo Stand
        {
            get
            {
                var all = StandRegistry.All;
                if (m_Stand < 0 || m_Stand >= all.Count) m_Stand = Camera.main != null ? StandRegistry.Nearest(Camera.main.transform.position) : 0;
                return all[Mathf.Clamp(m_Stand, 0, all.Count - 1)];
            }
        }

        TurnaroundOps Ops => TurnaroundOps.Instance;

        void OnEnable()
        {
            for (var i = 0; i < tabs.Length; i++)
            {
                var page = (Page)i;
                Hook(tabs[i], () => { m_Page = page; Redraw(); });
            }
            for (var i = 0; i < keys.Length; i++)
            {
                var k = i;
                Hook(keys[i], () => { m_Actions[k]?.Invoke(); Redraw(); });
            }
            EventBus.Subscribe<JetBridgeEvent>(OnBridge);
            EventBus.Subscribe<OpsEvent>(OnOps);
            EventBus.Subscribe<DockingEvent>(OnDocking);
        }

        void Start()
        {
            // It floats at your waist: never let it push you, or anything you're driving.
            var origin = FindAnyObjectByType<XROrigin>();
            var body = origin != null ? origin.GetComponent<CharacterController>() : null;
            if (body != null && bodyCollider != null) Physics.IgnoreCollision(body, bodyCollider);
        }

        void OnDisable()
        {
            foreach (var pair in m_Hooks) if (pair.Key != null) pair.Key.Pressed -= pair.Value;
            m_Hooks.Clear();
            EventBus.Unsubscribe<JetBridgeEvent>(OnBridge);
            EventBus.Unsubscribe<OpsEvent>(OnOps);
            EventBus.Unsubscribe<DockingEvent>(OnDocking);
        }

        void Hook(PressButton b, Action a)
        {
            if (b == null) return;
            Action<PressButton> h = _ => a();
            b.Pressed += h;
            m_Hooks[b] = h;
        }

        void OnBridge(JetBridgeEvent e) => m_LastMessage = e.Message;
        void OnOps(OpsEvent e) { if (e.State != TaskState.Working) m_LastMessage = $"{e.Stand}: {e.Note}"; }
        void OnDocking(DockingEvent e) { if (e.State == DockingState.Aligned) m_LastMessage = $"{e.Sensor.EquipmentName} aligned at {e.Door}"; }

        // ------------------------------------------------------------------ holster

        void LateUpdate()
        {
            if (bodyCollider != null)
            {
                var held = grab != null && grab.isSelected;
                var on = held || VehicleSeat.Current == null;
                if (bodyCollider.enabled != on) bodyCollider.enabled = on;
            }
            if (m_Head == null && Camera.main != null) m_Head = Camera.main.transform;
            if (m_Head != null && (grab == null || !grab.isSelected))
            {
                var flat = Vector3.ProjectOnPlane(m_Head.forward, Vector3.up);
                if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(m_Head.up, Vector3.up);
                var yaw = Quaternion.LookRotation(flat.normalized, Vector3.up);
                var target = m_Head.position + yaw * holsterOffset;
                // Screen (local -z) faces back towards you, tilted up.
                var rot = yaw * Quaternion.Euler(holsterTilt, 0f, 0f);
                var k = 1f - Mathf.Exp(-10f * Time.deltaTime);
                transform.SetPositionAndRotation(Vector3.Lerp(transform.position, target, k), Quaternion.Slerp(transform.rotation, rot, k));
            }
            if (Time.time >= m_NextRedraw) Redraw();
        }

        // ------------------------------------------------------------------ pages

        void SetKey(int i, string label, Action action)
        {
            m_Actions[i] = action;
            if (i >= keys.Length || keys[i] == null) return;
            var show = !string.IsNullOrEmpty(label);
            if (keys[i].gameObject.activeSelf != show) keys[i].gameObject.SetActive(show);
            var text = keys[i].GetComponentInChildren<TMP_Text>(true);
            if (text != null && text.text != label) text.text = label;
        }

        void Redraw()
        {
            m_NextRedraw = Time.time + 0.5f;
            for (var i = 0; i < 8; i++) SetKey(i, "", null);
            var stand = Stand;
            var rig = StandRegistry.AircraftAt(stand);
            var o = Ops != null ? Ops.For(stand) : null;
            m_Text.Clear();
            switch (m_Page)
            {
                case Page.Stand: StandPage(stand, rig, o); break;
                case Page.Bridge: BridgePage(stand, rig); break;
                case Page.Crew: CrewPage(stand, o); break;
                case Page.Ground: GroundPage(stand, o); break;
                case Page.Aircraft: AircraftPage(rig); break;
            }
            if (bodyText != null) bodyText.text = m_Text.ToString();
            if (headerText != null)
                headerText.text = $"<b>{stand.name.ToUpperInvariant()}</b>  <color=#9AA7B8>{(stand.IsGate ? "contact gate" : "remote stand")}</color>" +
                                  $"<pos=78%>{DateTime.Now:HH:mm}";
            if (footerText != null) footerText.text = string.IsNullOrEmpty(m_LastMessage) ? "<color=#9AA7B8>Ready</color>" : m_LastMessage;
            for (var i = 0; i < tabs.Length; i++)
            {
                var t = tabs[i] != null ? tabs[i].GetComponentInChildren<TMP_Text>(true) : null;
                if (t != null) t.color = i == (int)m_Page ? new Color(1f, 0.82f, 0.3f) : Color.white;
            }
        }

        void StandPage(StandInfo stand, AircraftRig rig, StandOps o)
        {
            var all = StandRegistry.All;
            m_Text.Append("Aircraft: ").Append(rig != null ? Pretty(rig.name) : "<color=#FF8A80>none on stand</color>").Append('\n');
            if (rig != null) m_Text.Append("Door 1L ").Append(DoorState(rig, "Door_1L")).Append("   Fwd hold ").Append(DoorState(rig, "CargoDoor_Fwd")).Append('\n');
            if (stand.IsGate) m_Text.Append("Jet bridge: ").Append(stand.bridge.Message).Append('\n');
            if (o != null)
            {
                m_Text.Append("Crew: ").Append(o.CrewStatus).Append('\n');
                m_Text.Append("Ground: ");
                foreach (OpsTask t in Enum.GetValues(typeof(OpsTask)))
                {
                    if (t == OpsTask.Bridge && !stand.IsGate || t == OpsTask.Stairs && stand.IsGate) continue;
                    m_Text.Append(Short(t)).Append(' ').Append(Mark(o.Get(t))).Append("  ");
                }
                m_Text.Append('\n');
            }
            else m_Text.Append("<color=#FF8A80>No operations manager - run Ground Crew > Operations > Set Up</color>\n");
            m_Text.Append($"\n<color=#9AA7B8>Stand {m_Stand + 1} of {all.Count}. SETUP ALL sends the ground crew to chock, cone and position the equipment.</color>");
            SetKey(0, "< PREV", () => { m_Stand = (m_Stand - 1 + all.Count) % all.Count; });
            SetKey(1, "NEXT >", () => { m_Stand = (m_Stand + 1) % all.Count; });
            SetKey(2, "NEAREST", () => { if (Camera.main != null) m_Stand = StandRegistry.Nearest(Camera.main.transform.position); });
            SetKey(3, "GO THERE", () => GoTo(stand));
            if (Ops == null) return;
            SetKey(4, "SETUP ALL", () => Ops.SetupAll(stand));
            SetKey(5, "CLEAR ALL", () => Ops.ClearAll(stand));
            SetKey(6, "CALL CREW", () => Ops.CallCrew(stand));
            SetKey(7, "CREW OFF", () => Ops.CrewOff(stand));
        }

        void BridgePage(StandInfo stand, AircraftRig rig)
        {
            if (!stand.IsGate)
            {
                m_Text.Append("This is a remote stand - no jet bridge.\n\nUse GROUND > STAIRS to have the passenger stairs brought to door 1L, or drive them yourself.");
                return;
            }
            var b = stand.bridge;
            m_Text.Append("State: <b>").Append(b.State).Append("</b>\n").Append(b.Message).Append('\n');
            m_Text.Append("Aircraft: ").Append(rig != null ? Pretty(rig.name) : "none").Append('\n');
            if (rig != null) m_Text.Append("Door 1L ").Append(DoorState(rig, "Door_1L")).Append('\n');
            m_Text.Append("\n<color=#9AA7B8>DOCK drives the bridge to door 1L: it lines up, creeps in, closes the canopy and opens the door. The same buttons are on the console in the cab.</color>");
            SetKey(0, "DOCK", b.Dock);
            SetKey(1, "RETRACT", b.Retract);
            SetKey(2, "STOP", b.Stop);
            if (rig != null) SetKey(3, "DOOR 1L", () => rig.SetOpen01("Door_1L", rig.GetOpen01("Door_1L") > 0.5f ? 0f : 1f));
        }

        void CrewPage(StandInfo stand, StandOps o)
        {
            if (o == null) { m_Text.Append("No operations manager in this scene."); return; }
            m_Text.Append("<b>").Append(o.CrewStatus).Append("</b>\n");
            foreach (var a in o.FlightCrew)
                if (a != null) m_Text.Append(a.DisplayName).Append(" <color=#9AA7B8>").Append(a.Activity).Append("</color>\n");
            if (o.FlightCrew.Count == 0)
                m_Text.Append("\n<color=#9AA7B8>CALL CREW brings two pilots and four cabin crew. They board by the jet bridge, or by the stairs on a remote stand (the stairs are brought first if needed).</color>");
            SetKey(0, "CALL CREW", () => Ops.CallCrew(stand));
            SetKey(1, "CREW OFF", () => Ops.CrewOff(stand));
        }

        void GroundPage(StandInfo stand, StandOps o)
        {
            if (o == null) { m_Text.Append("No operations manager in this scene."); return; }
            var tasks = new[] { OpsTask.Chocks, OpsTask.Cones, stand.IsGate ? OpsTask.Bridge : OpsTask.Stairs, OpsTask.BeltLoader };
            for (var i = 0; i < tasks.Length; i++)
            {
                var t = tasks[i];
                var state = o.Get(t);
                m_Text.Append(Mark(state)).Append(' ').Append(Short(t)).Append(" <color=#9AA7B8>").Append(o.NoteFor(t)).Append("</color>\n");
                var done = state == TaskState.Done;
                SetKey(i, (done ? "REMOVE " : "") + Short(t).ToUpperInvariant(), () => { if (done) Ops.Undo(stand, t); else Ops.Run(stand, t); });
            }
            m_Text.Append("\nWorkers: ");
            var any = false;
            foreach (var w in o.Workers)
                if (w != null) { m_Text.Append(w.DisplayName).Append(" (").Append(w.Busy ? w.Activity : "free").Append(")  "); any = true; }
            if (!any) m_Text.Append("<color=#9AA7B8>none yet - they come when you give them a job</color>");
            SetKey(4, "SETUP ALL", () => Ops.SetupAll(stand));
            SetKey(5, "CLEAR ALL", () => Ops.ClearAll(stand));
        }

        void AircraftPage(AircraftRig rig)
        {
            if (rig == null) { m_Text.Append("No aircraft on this stand."); return; }
            m_Text.Append(Pretty(rig.name)).Append('\n');
            m_Text.Append("Doors: 1L ").Append(DoorState(rig, "Door_1L")).Append("  2L ").Append(DoorState(rig, "Door_2L"))
                .Append("\nCargo: fwd ").Append(DoorState(rig, "CargoDoor_Fwd")).Append("  aft ").Append(DoorState(rig, "CargoDoor_Aft")).Append('\n');
            m_Text.Append("Beacon ").Append(rig.BeaconOn ? "ON" : "off").Append("   Flight deck light ").Append(rig.FlightDeckLightOn ? "ON" : "off").Append('\n');
            SetKey(0, "DOOR 1L", () => Toggle(rig, "Door_1L"));
            SetKey(1, "DOOR 2L", () => Toggle(rig, "Door_2L"));
            SetKey(2, "FWD HOLD", () => Toggle(rig, "CargoDoor_Fwd"));
            SetKey(3, "AFT HOLD", () => Toggle(rig, "CargoDoor_Aft"));
            SetKey(4, "ALL DOORS", rig.ToggleAllDoors);
            SetKey(5, "BEACON", rig.ToggleBeacon);
            SetKey(6, "FLAPS", rig.ToggleFlaps);
            SetKey(7, "CKPT LT", rig.ToggleFlightDeckLight);
        }

        // ------------------------------------------------------------------ helpers

        static void Toggle(AircraftRig rig, string door) => rig.SetOpen01(door, rig.GetOpen01(door) > 0.5f ? 0f : 1f);

        static string DoorState(AircraftRig rig, string door)
        {
            if (!rig.HasPart(door)) return "-";
            var t = rig.GetOpen01(door);
            return t > 0.95f ? "<color=#7CFC9A>open</color>" : t < 0.05f ? "closed" : "moving";
        }

        static string Mark(TaskState s) =>
            s == TaskState.Done ? "<color=#7CFC9A>[OK]</color>" : s == TaskState.Working ? "<color=#FFD166>[..]</color>" : s == TaskState.Failed ? "<color=#FF8A80>[!!]</color>" : "<color=#9AA7B8>[  ]</color>";

        static string Short(OpsTask t) => t == OpsTask.BeltLoader ? "Belt" : t.ToString();

        static string Pretty(string modelName) => modelName.Contains("320") ? "A320-class" : modelName.Contains("737") ? "737-class" : modelName;

        void GoTo(StandInfo stand)
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin == null || VehicleSeat.Current != null) return;
            var face = stand.stopPosition - stand.staging;
            face.y = 0f;
            origin.MatchOriginUpCameraForward(Vector3.up, face.sqrMagnitude > 0.01f ? face.normalized : Vector3.forward);
            var cam = origin.Camera.transform.position;
            var floor = origin.transform.position.y;
            var target = stand.staging + new Vector3(0f, cam.y - floor, 0f);
            origin.MoveCameraToWorldLocation(target);
            m_LastMessage = "Moved to " + stand.name;
        }
    }
}
