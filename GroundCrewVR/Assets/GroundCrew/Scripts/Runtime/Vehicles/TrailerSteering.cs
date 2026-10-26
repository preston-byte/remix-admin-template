using UnityEngine;

namespace GroundCrew.Vehicles
{
    /// <summary>
    /// Baggage cart (or any turntable trailer): the front wheels steer with the drawbar, and the
    /// automatic parking brake holds the cart whenever the drawbar is not coupled.
    /// </summary>
    public class TrailerSteering : MonoBehaviour
    {
        [SerializeField] VehicleController trailer;
        [SerializeField] Transform drawbar;
        [SerializeField] TowCoupler coupler;

        void FixedUpdate()
        {
            if (trailer == null || drawbar == null) return;
            var up = trailer.transform.up;
            var bar = Vector3.ProjectOnPlane(drawbar.forward, up);
            var yaw = bar.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(trailer.transform.forward, bar, up) : 0f;
            trailer.SetSteerAngleDirect(yaw);
            var coupled = coupler != null && coupler.IsCoupled;
            trailer.SetParkingBrake(!coupled);
        }
    }
}
