using System;
using System.Collections.Generic;
using UnityEngine;

namespace GroundCrew.Core
{
    /// <summary>
    /// Minimal service registry for long-lived systems (turnaround manager, radio, audio,
    /// career/save). Systems register themselves in OnEnable and unregister in OnDisable.
    ///
    /// Deliberately tiny: no reflection, no lifetime magic. Prefer passing references
    /// explicitly; use this only for genuinely global systems.
    /// </summary>
    public static class Services
    {
        static readonly Dictionary<Type, object> s_Services = new Dictionary<Type, object>();

        public static void Register<T>(T service) where T : class
        {
            if (service == null) return;
            if (s_Services.TryGetValue(typeof(T), out var existing) && !ReferenceEquals(existing, service))
                Debug.LogWarning($"[Services] Replacing registered {typeof(T).Name}.");
            s_Services[typeof(T)] = service;
        }

        public static void Unregister<T>(T service) where T : class
        {
            if (s_Services.TryGetValue(typeof(T), out var existing) && ReferenceEquals(existing, service))
                s_Services.Remove(typeof(T));
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (s_Services.TryGetValue(typeof(T), out var obj) && obj is T typed)
            {
                service = typed;
                return true;
            }
            service = null;
            return false;
        }

        public static T Get<T>() where T : class
        {
            if (TryGet(out T service)) return service;
            throw new InvalidOperationException($"[Services] No {typeof(T).Name} registered.");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Services.Clear();
    }
}
