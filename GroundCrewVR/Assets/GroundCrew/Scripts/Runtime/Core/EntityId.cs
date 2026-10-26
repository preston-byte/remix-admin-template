using System;
using UnityEngine;

namespace GroundCrew.Core
{
    /// <summary>
    /// Stable identifier for anything the simulation needs to reference: bags, plugs, doors,
    /// vehicles, aircraft. Events carry this id rather than only object references, so the
    /// same events can later be serialized and sent to other players.
    ///
    /// Scene objects should get a deterministic id (the sandbox builder assigns readable ids
    /// such as "sandbox.bag.heavy"). Runtime-spawned objects fall back to a GUID.
    /// </summary>
    [DisallowMultipleComponent]
    public class EntityId : MonoBehaviour
    {
        [SerializeField] string value;

        public string Value
        {
            get
            {
                if (string.IsNullOrEmpty(value))
                    value = Guid.NewGuid().ToString("N");
                return value;
            }
        }

        public static string Of(Component component)
        {
            if (component == null) return string.Empty;
            return component.TryGetComponent(out EntityId id) ? id.Value : component.name;
        }
    }
}
