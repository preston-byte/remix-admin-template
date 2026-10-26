using UnityEngine;

namespace GroundCrew.Interaction
{
    /// <summary>
    /// Marks a small trigger sphere on a fingertip (the XRI poke point) that physically presses
    /// <see cref="PressButton"/>s. Created at runtime by <see cref="GroundCrew.Player.FingertipSetup"/>.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class FingertipPresser : MonoBehaviour
    {
        /// <summary>Any transform under the controller, used to find its haptics player.</summary>
        public Transform HapticSource { get; set; }
    }
}
