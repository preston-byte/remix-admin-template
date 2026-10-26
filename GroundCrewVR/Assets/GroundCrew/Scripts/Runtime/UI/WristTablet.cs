using System;
using System.Collections.Generic;
using System.Text;
using GroundCrew.Core;
using GroundCrew.Interaction;
using GroundCrew.Player;
using TMPro;
using UnityEngine;

namespace GroundCrew.UI
{
    /// <summary>
    /// Wrist-mounted tablet on the left controller. It appears when you turn your wrist to look
    /// at it (like checking a watch) and fades out otherwise, so there is no HUD in your face.
    ///
    /// Phase 1 shows: clock, what you are holding (with weight), a short feed of interaction
    /// events, frame rate and comfort settings, plus two physical buttons for comfort options.
    /// Later phases add the assignment, task list, deadline, radio log and earnings pages.
    /// </summary>
    public class WristTablet : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [SerializeField] TMP_Text headerText;
        [SerializeField] TMP_Text bodyText;
        [SerializeField] TMP_Text footerText;
        [SerializeField] PressButton moveButton;
        [SerializeField] PressButton turnButton;
        [SerializeField, Tooltip("Hidden while the tablet is faded out, so you can't press invisible buttons.")]
        GameObject buttonsRoot;

        [SerializeField, Range(0f, 1f), Tooltip("How directly the tablet must face your eyes to show.")]
        float showWhenFacing = 0.55f;
        [SerializeField] float maxViewDistance = 0.8f;
        [SerializeField] float fadeSpeed = 6f;
        [SerializeField] string callsign = "GROUND CREW 12";

        const int k_FeedLines = 4;

        readonly Queue<string> m_Feed = new Queue<string>();
        readonly StringBuilder m_Builder = new StringBuilder(256);
        Transform m_Head;
        string m_Holding = "Hands free";
        bool m_Dirty = true;
        float m_FpsTime;
        int m_FpsFrames;
        float m_Fps;
        int m_LastMinute = -1;

        void OnEnable()
        {
            EventBus.Subscribe<ItemGrabbedEvent>(OnItemGrabbed);
            EventBus.Subscribe<ItemReleasedEvent>(OnItemReleased);
            EventBus.Subscribe<ItemDamagedEvent>(OnItemDamaged);
            EventBus.Subscribe<ZoneItemPlacedEvent>(OnZonePlaced);
            EventBus.Subscribe<ZoneItemRemovedEvent>(OnZoneRemoved);
            EventBus.Subscribe<ZoneCapacityEvent>(OnZoneCapacity);
            EventBus.Subscribe<ConnectorEvent>(OnConnector);
            EventBus.Subscribe<CableOverstretchedEvent>(OnCableOverstretched);
            EventBus.Subscribe<HandleLimitEvent>(OnHandleLimit);
            ComfortSettings.Changed += MarkDirty;
            if (moveButton != null) moveButton.Pressed += OnMovePressed;
            if (turnButton != null) turnButton.Pressed += OnTurnPressed;
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<ItemGrabbedEvent>(OnItemGrabbed);
            EventBus.Unsubscribe<ItemReleasedEvent>(OnItemReleased);
            EventBus.Unsubscribe<ItemDamagedEvent>(OnItemDamaged);
            EventBus.Unsubscribe<ZoneItemPlacedEvent>(OnZonePlaced);
            EventBus.Unsubscribe<ZoneItemRemovedEvent>(OnZoneRemoved);
            EventBus.Unsubscribe<ZoneCapacityEvent>(OnZoneCapacity);
            EventBus.Unsubscribe<ConnectorEvent>(OnConnector);
            EventBus.Unsubscribe<CableOverstretchedEvent>(OnCableOverstretched);
            EventBus.Unsubscribe<HandleLimitEvent>(OnHandleLimit);
            ComfortSettings.Changed -= MarkDirty;
            if (moveButton != null) moveButton.Pressed -= OnMovePressed;
            if (turnButton != null) turnButton.Pressed -= OnTurnPressed;
        }

        void Start()
        {
            Push("Shift started - training sandbox.");
            if (group != null) group.alpha = 0f;
            if (buttonsRoot != null) buttonsRoot.SetActive(false);
        }

        void Update()
        {
            UpdateVisibility();
            UpdateFps();

            var minute = DateTime.Now.Minute;
            if (minute != m_LastMinute)
            {
                m_LastMinute = minute;
                m_Dirty = true;
            }

            if (m_Dirty) Redraw();
        }

        void UpdateVisibility()
        {
            if (m_Head == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                m_Head = cam.transform;
            }

            var toTablet = transform.position - m_Head.position;
            var distance = toTablet.magnitude;
            // A world-space canvas is readable when the viewer looks along its forward axis.
            var facing = distance > 1e-4f ? Vector3.Dot(transform.forward, toTablet / distance) : 0f;
            var visible = facing >= showWhenFacing && distance <= maxViewDistance;

            if (group == null) return;
            group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, fadeSpeed * Time.deltaTime);
            if (buttonsRoot != null)
            {
                var active = group.alpha > 0.6f;
                if (buttonsRoot.activeSelf != active) buttonsRoot.SetActive(active);
            }
        }

        void UpdateFps()
        {
            m_FpsFrames++;
            m_FpsTime += Time.unscaledDeltaTime;
            if (m_FpsTime < 0.5f) return;
            m_Fps = m_FpsFrames / m_FpsTime;
            m_FpsFrames = 0;
            m_FpsTime = 0f;
            m_Dirty = true;
        }

        void Redraw()
        {
            m_Dirty = false;

            if (headerText != null)
                headerText.text = $"<b>{callsign}</b>   {DateTime.Now:HH:mm}";

            if (bodyText != null)
            {
                m_Builder.Clear();
                m_Builder.Append("<b>").Append(m_Holding).Append("</b>\n");
                foreach (var line in m_Feed) m_Builder.Append(line).Append('\n');
                bodyText.text = m_Builder.ToString();
            }

            if (footerText != null)
            {
                var fpsColor = m_Fps >= 70f ? "#7CFC9A" : m_Fps >= 60f ? "#FFD166" : "#FF6B6B";
                footerText.text = $"<color={fpsColor}>{m_Fps:0} fps</color>   {ComfortSettings.MoveLabel} | {ComfortSettings.TurnLabel}";
            }
        }

        void MarkDirty() => m_Dirty = true;

        /// <summary>Add a line to the feed. Later the radio system will also push here.</summary>
        public void Push(string message)
        {
            m_Feed.Enqueue($"<color=#9AA7B8>{DateTime.Now:HH:mm}</color> {message}");
            while (m_Feed.Count > k_FeedLines) m_Feed.Dequeue();
            m_Dirty = true;
        }

        void OnMovePressed(PressButton _) => ComfortSettings.CycleMove();
        void OnTurnPressed(PressButton _) => ComfortSettings.CycleTurn();

        void OnItemGrabbed(ItemGrabbedEvent e)
        {
            var hint = e.Item.RequiresTwoHands && e.HandCount < 2 ? " - use both hands!" : "";
            m_Holding = $"Holding: {e.Item.DisplayName} ({e.Item.MassKg:0.#} kg){hint}";
            m_Dirty = true;
        }

        void OnItemReleased(ItemReleasedEvent e)
        {
            m_Holding = "Hands free";
            m_Dirty = true;
        }

        void OnItemDamaged(ItemDamagedEvent e) =>
            Push($"<color=#FF9A3C>{e.Item.DisplayName} damaged ({e.TotalDamage01:P0})</color>");

        void OnZonePlaced(ZoneItemPlacedEvent e) => Push(e.Accepted
            ? $"{e.Item.DisplayName} in {e.Zone.ZoneName} ({e.Zone.PlacedMassKg:0.#} kg)"
            : $"<color=#FF6B6B>{e.Item.DisplayName} doesn't belong in {e.Zone.ZoneName}</color>");

        void OnZoneRemoved(ZoneItemRemovedEvent e) => Push($"{e.Item.DisplayName} out of {e.Zone.ZoneName}");

        void OnZoneCapacity(ZoneCapacityEvent e) => Push(e.OverCapacity
            ? $"<color=#FF6B6B>{e.Zone.ZoneName} OVER LIMIT {e.Zone.PlacedMassKg:0}/{e.Zone.MaxMassKg:0} kg</color>"
            : $"{e.Zone.ZoneName} back within limit");

        void OnConnector(ConnectorEvent e)
        {
            var plug = e.Plug != null ? e.Plug.DisplayName : "Plug";
            Push(e.Connected ? $"<color=#7CFC9A>{plug} connected</color> - {e.Socket.SocketName}" : $"{plug} disconnected");
        }

        void OnCableOverstretched(CableOverstretchedEvent e) => Push(e.WasConnected
            ? "<color=#FF6B6B>SAFETY: equipment moved while connected!</color>"
            : "Cable at full length");

        void OnHandleLimit(HandleLimitEvent e) => Push($"{e.Handle.HandleName} {(e.AtMax ? "open" : "closed")}");
    }
}
