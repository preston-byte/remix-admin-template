using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GroundCrew.Crew
{
    public enum CrewRole { Captain, FirstOfficer, Purser, CabinCrew, GroundCrew }

    /// <summary>
    /// A computer-controlled person: walks along routes (following the ground, stairs, bridge
    /// and cabin floors by raycasting down), turns, sits, crouches, carries things in its hands
    /// and rides in vehicles. Higher-level jobs (TurnaroundOps) are coroutines built from these.
    /// No NavMesh: routes are explicit waypoints, which suits an apron where everything moves.
    /// </summary>
    [RequireComponent(typeof(CrewAnimator))]
    public class CrewAgent : MonoBehaviour
    {
        [SerializeField] CrewRole role = CrewRole.GroundCrew;
        [SerializeField] float walkSpeed = 1.35f;
        [SerializeField, Tooltip("Highest step it walks up (stairs treads, door sills).")] float stepHeight = 0.55f;

        CrewAnimator m_Animator;
        readonly List<GameObject> m_Carried = new List<GameObject>();

        public CrewRole Role { get => role; set => role = value; }
        public string DisplayName { get; set; } = "";
        public string Activity { get; set; } = "";
        public bool Busy { get; set; }
        public CrewAnimator Animator => m_Animator != null ? m_Animator : (m_Animator = GetComponent<CrewAnimator>());
        public int CarriedCount => m_Carried.Count;

        void Awake() => m_Animator = GetComponent<CrewAnimator>();

        // ------------------------------------------------------------------ moving

        public void SnapTo(Vector3 position, float yaw)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>Walk through the points in order, then stop facing the last direction.</summary>
        public IEnumerator WalkPath(IList<Vector3> points, float speedScale = 1f)
        {
            for (var i = 0; i < points.Count; i++)
                yield return WalkTo(points[i], speedScale);
            Animator.Speed = 0f;
            Animator.Pose = CrewPose.Idle;
        }

        public IEnumerator WalkTo(Vector3 target, float speedScale = 1f)
        {
            Animator.Pose = CrewPose.Walk;
            var speed = walkSpeed * speedScale;
            var stuck = 0f;
            while (true)
            {
                var p = transform.position;
                var flat = new Vector3(target.x - p.x, 0f, target.z - p.z);
                var distance = flat.magnitude;
                if (distance < 0.08f) break;
                var dir = flat / distance;
                var yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                var current = transform.eulerAngles.y;
                var turn = Mathf.Abs(Mathf.DeltaAngle(current, yaw));
                transform.rotation = Quaternion.Euler(0f, Mathf.MoveTowardsAngle(current, yaw, 300f * Time.deltaTime), 0f);
                // Slow down for sharp turns, so it doesn't moonwalk round corners.
                var v = speed * Mathf.Clamp01(1.2f - turn / 90f);
                var step = Mathf.Min(distance, v * Time.deltaTime);
                var next = p + dir * step;
                next.y = Ground(next, p.y, target.y);
                transform.position = next;
                Animator.Speed = v;
                stuck += Time.deltaTime;
                if (stuck > 120f) break;     // give up on an impossible leg rather than walk forever
                yield return null;
            }
            transform.position = new Vector3(target.x, Ground(target, transform.position.y, target.y), target.z);
        }

        /// <summary>Height of the floor under a point: the highest surface no more than a step above the current height.</summary>
        float Ground(Vector3 at, float currentY, float fallbackY)
        {
            var origin = new Vector3(at.x, currentY + stepHeight + 0.15f, at.z);
            var hits = Physics.RaycastAll(origin, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
            var best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                var rb = h.collider.attachedRigidbody;
                if (rb != null && !rb.isKinematic && rb.mass < 100f) continue;   // bags, cones, chocks: step over, don't climb
                if (h.point.y > best && h.point.y <= currentY + stepHeight) best = h.point.y;
            }
            if (float.IsNegativeInfinity(best)) return Mathf.MoveTowards(currentY, fallbackY, 2f * Time.deltaTime);
            return Mathf.MoveTowards(currentY, best, 4f * Time.deltaTime);
        }

        public IEnumerator TurnTo(float yaw)
        {
            while (Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, yaw)) > 1f)
            {
                transform.rotation = Quaternion.Euler(0f, Mathf.MoveTowardsAngle(transform.eulerAngles.y, yaw, 240f * Time.deltaTime), 0f);
                yield return null;
            }
        }

        public IEnumerator Face(Vector3 point)
        {
            var d = point - transform.position;
            if (new Vector2(d.x, d.z).sqrMagnitude > 1e-4f) yield return TurnTo(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
        }

        public IEnumerator Crouch(float seconds)
        {
            Animator.Pose = CrewPose.Crouch;
            yield return new WaitForSeconds(seconds);
            Animator.Pose = CrewPose.Idle;
        }

        /// <summary>Sit (seat height ~0.48 m above `floor`) facing `yaw`, parented to `parent` so it rides along.</summary>
        public void SitAt(Vector3 floor, float yaw, Transform parent, bool driving = false)
        {
            transform.SetParent(parent, true);
            transform.SetPositionAndRotation(floor, Quaternion.Euler(0f, yaw, 0f));
            Animator.Speed = 0f;
            Animator.Pose = driving ? CrewPose.Drive : CrewPose.Sit;
        }

        public void StandUp(Transform parent = null)
        {
            transform.SetParent(parent, true);
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            Animator.Pose = CrewPose.Idle;
        }

        // ------------------------------------------------------------------ carrying

        /// <summary>Take an object into a hand: physics off, colliders off, parented to the hand.</summary>
        public void Hold(GameObject item, bool right)
        {
            if (item == null) return;
            var attach = Animator.HandAttach(right);
            foreach (var c in item.GetComponentsInChildren<Collider>()) c.enabled = false;
            var rb = item.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            item.transform.SetParent(attach != null ? attach : transform, false);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
            m_Carried.Add(item);
            Animator.Carrying = true;
        }

        /// <summary>Put the most recently taken object down at a pose; it becomes a normal physics object again.</summary>
        public GameObject PutDown(Vector3 position, Quaternion rotation)
        {
            if (m_Carried.Count == 0) return null;
            var item = m_Carried[m_Carried.Count - 1];
            m_Carried.RemoveAt(m_Carried.Count - 1);
            Animator.Carrying = m_Carried.Count > 0;
            if (item == null) return null;
            item.transform.SetParent(null, true);
            item.transform.SetPositionAndRotation(position, rotation);
            foreach (var c in item.GetComponentsInChildren<Collider>()) c.enabled = true;
            var rb = item.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            return item;
        }

        /// <summary>Remove whatever it's carrying (for tidying up).</summary>
        public void DropAllAndDestroy()
        {
            foreach (var item in m_Carried) if (item != null) Destroy(item);
            m_Carried.Clear();
            Animator.Carrying = false;
        }
    }
}
