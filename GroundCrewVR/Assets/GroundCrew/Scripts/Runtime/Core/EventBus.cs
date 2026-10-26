using System;
using System.Collections.Generic;
using UnityEngine;

namespace GroundCrew.Core
{
    /// <summary>Marker interface for anything sent through the <see cref="EventBus"/>.</summary>
    public interface IGameEvent { }

    /// <summary>
    /// Typed publish/subscribe bus.
    ///
    /// Interaction components publish *facts* ("plug connected", "bag placed in zone") and
    /// higher-level systems (turnaround rules, scoring, radio, wrist tablet) subscribe to them.
    /// This keeps the reusable interaction layer ignorant of gameplay rules, and gives us a
    /// single choke point to forward events over the network later.
    ///
    /// Gameplay events are low-frequency (a handful per second at most), so robustness wins
    /// over micro-optimisation: every handler is isolated, and an exception in one is logged
    /// without stopping the others. Do NOT publish per-frame data through this bus.
    /// </summary>
    public static class EventBus
    {
        static readonly Dictionary<Type, Delegate> s_Handlers = new Dictionary<Type, Delegate>();

        public static void Subscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            if (handler == null) return;
            var type = typeof(T);
            s_Handlers[type] = s_Handlers.TryGetValue(type, out var existing)
                ? Delegate.Combine(existing, handler)
                : handler;
        }

        public static void Unsubscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            if (handler == null) return;
            var type = typeof(T);
            if (!s_Handlers.TryGetValue(type, out var existing)) return;

            var remaining = Delegate.Remove(existing, handler);
            if (remaining == null) s_Handlers.Remove(type);
            else s_Handlers[type] = remaining;
        }

        public static void Publish<T>(T gameEvent) where T : struct, IGameEvent
        {
            if (!s_Handlers.TryGetValue(typeof(T), out var existing)) return;

            // Snapshot: handlers may subscribe/unsubscribe while we are publishing.
            foreach (var handler in existing.GetInvocationList())
            {
                try { ((Action<T>)handler).Invoke(gameEvent); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        // Supports "Enter Play Mode Options" with domain reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Handlers.Clear();
    }
}
