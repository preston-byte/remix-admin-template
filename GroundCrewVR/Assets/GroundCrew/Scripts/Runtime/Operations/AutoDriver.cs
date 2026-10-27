using System.Collections;
using System.Collections.Generic;
using GroundCrew.Vehicles;
using UnityEngine;

namespace GroundCrew.Operations
{
    /// <summary>
    /// How AI ground crew drive. Positioning equipment at an aircraft door needs centimetre
    /// accuracy, which a physics-driven vehicle with a simple AI can't give reliably, so the AI
    /// drives the vehicle kinematically along a smooth path (a curve that ends in a straight run
    /// square to the target), easing in and out, and hands it back to physics, parked, at the end.
    /// It gives up (and hands back) if the player takes the driver's seat.
    /// </summary>
    public static class AutoDriver
    {
        const float k_Accel = 0.8f;

        /// <summary>Drive forwards to `target` (root position), arriving heading along `heading`, with a straight final `approach`.</summary>
        public static IEnumerator DriveTo(VehicleController vehicle, Vector3 target, Vector3 heading, float speed = 3f, float approach = 8f)
        {
            if (vehicle == null || vehicle.Body == null) yield break;
            heading = Vector3.ProjectOnPlane(heading, Vector3.up).normalized;
            var start = vehicle.Body.position;
            target.y = start.y;
            var forward = Vector3.ProjectOnPlane(vehicle.transform.forward, Vector3.up).normalized;
            var pre = target - heading * approach;
            var points = new List<Vector3>();
            var startDir = forward;
            var from = start;
            if (Vector3.Dot(forward, pre - start) < 3f)
            {
                // Target behind us: swing out in a loop first instead of spinning on the spot.
                var right = Vector3.Cross(Vector3.up, forward);
                var side = Vector3.Dot(right, pre - start) >= 0f ? 1f : -1f;
                var swing = start + forward * 5f + right * side * 9f;
                AddCurve(points, start, forward, swing, right * side);
                from = swing;
                startDir = right * side;
            }
            AddCurve(points, from, startDir, pre, heading);
            points.Add(target);
            yield return Follow(vehicle, points, speed, false);
        }

        static void AddCurve(List<Vector3> points, Vector3 a, Vector3 aDir, Vector3 b, Vector3 bDir)
        {
            var k = Mathf.Clamp(Vector3.Distance(a, b) * 0.4f, 2f, 18f);
            for (var i = points.Count == 0 ? 0 : 1; i <= 40; i++)
            {
                var t = i / 40f;
                var u = 1f - t;
                points.Add(u * u * u * a + 3f * u * u * t * (a + aDir * k) + 3f * u * t * t * (b - bDir * k) + t * t * t * b);
            }
        }

        /// <summary>Move straight along the vehicle's own heading by `distance` metres (negative = reverse).</summary>
        public static IEnumerator Creep(VehicleController vehicle, float distance, float speed = 0.4f)
        {
            if (vehicle == null || vehicle.Body == null || Mathf.Abs(distance) < 0.005f) yield break;
            var start = vehicle.Body.position;
            var forward = Vector3.ProjectOnPlane(vehicle.transform.forward, Vector3.up).normalized;
            yield return Follow(vehicle, new List<Vector3> { start, start + forward * distance }, speed, distance < 0f);
        }

        static IEnumerator Follow(VehicleController vehicle, List<Vector3> points, float speed, bool reverse)
        {
            var body = vehicle.Body;
            var wasKinematic = body.isKinematic;
            body.isKinematic = true;
            vehicle.SetParkingBrake(false);
            var lengths = new List<float> { 0f };
            for (var i = 1; i < points.Count; i++) lengths.Add(lengths[i - 1] + Vector3.Distance(points[i - 1], points[i]));
            var total = lengths[lengths.Count - 1];
            var s = 0f;
            var v = 0f;
            var y = body.position.y;
            var yaw = body.rotation;
            while (s < total)
            {
                if (VehicleSeat.Current != null && VehicleSeat.Current.Vehicle == vehicle) break;   // the player took over
                var stopping = Mathf.Sqrt(2f * k_Accel * Mathf.Max(0f, total - s)) + 0.15f;
                v = Mathf.Min(speed, Mathf.Min(v + k_Accel * Time.fixedDeltaTime, stopping));
                s = Mathf.Min(total, s + v * Time.fixedDeltaTime);
                var i = 1;
                while (i < lengths.Count - 1 && lengths[i] < s) i++;
                var seg = Mathf.Max(1e-4f, lengths[i] - lengths[i - 1]);
                var p = Vector3.Lerp(points[i - 1], points[i], (s - lengths[i - 1]) / seg);
                p.y = y;
                var tangent = points[i] - points[i - 1];
                tangent.y = 0f;
                if (reverse) tangent = -tangent;
                if (tangent.sqrMagnitude > 1e-6f) yaw = Quaternion.Slerp(yaw, Quaternion.LookRotation(tangent.normalized, Vector3.up), 0.25f);
                body.MovePosition(p);
                body.MoveRotation(yaw);
                yield return new WaitForFixedUpdate();
            }
            body.isKinematic = wasKinematic;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            vehicle.SetParkingBrake(true);
        }
    }
}
