using System.Collections;
using System.Collections.Generic;
using GroundCrew.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace GroundCrew.Player
{
    public enum HandSide { Left, Right }

    /// <summary>
    /// Gloved hands in place of the controller models, posed from what your fingers are doing on
    /// the Touch controller:
    ///   grip      curls the middle, ring and little fingers;
    ///   trigger   curls the index finger (resting on it = half curled, not touching = pointing);
    ///   thumb     rests when it touches the stick or A/B (X/Y), sticks up when it doesn't.
    /// So grip without the trigger makes a pointing hand - use that to press buttons: the poke
    /// point (and the fingertip that presses <see cref="Interaction.PressButton"/>s) is moved to the
    /// tip of the index finger.
    ///
    /// Put this on the "Left Controller" / "Right Controller" object; the glove model (generated
    /// Hand_R.glb, mirrored for the left hand) is a child. Each joint has a "Bend" helper whose
    /// local position is its curl axis, so this doesn't depend on importer axis conventions.
    /// </summary>
    public class ControllerHands : MonoBehaviour
    {
        [SerializeField] HandSide side = HandSide.Right;
        [SerializeField] Transform handRoot;
        [SerializeField, Tooltip("Hide the controller's own model (keeps the wrist tablet and lines).")] bool hideControllerModel = true;
        [SerializeField, Tooltip("Move the XRI poke point to the index fingertip.")] bool pokeFromIndexTip = true;
        [SerializeField, Tooltip("How fast fingers follow the controls (1/s).")] float speed = 20f;

        // Joint angles (degrees) for proximal, middle, distal joints.
        static readonly Vector3 k_Open = new Vector3(6f, 8f, 4f);
        static readonly Vector3 k_Fist = new Vector3(80f, 98f, 62f);
        static readonly Vector3 k_IndexPoint = new Vector3(2f, 3f, 2f);
        static readonly Vector3 k_IndexRest = new Vector3(28f, 30f, 16f);
        static readonly Vector3 k_IndexPull = new Vector3(66f, 80f, 48f);
        static readonly Vector3 k_ThumbUp = new Vector3(-6f, 0f, 0f);
        static readonly Vector3 k_ThumbOpen = new Vector3(4f, 6f, 2f);
        static readonly Vector3 k_ThumbRest = new Vector3(20f, 18f, 14f);

        struct Joint
        {
            public Transform T;
            public Quaternion Rest;
            public Vector3 Axis;
        }

        class Finger
        {
            public readonly Joint[] Joints = new Joint[3];
            public Vector3 Current;
            public bool Valid;
        }

        readonly Finger m_Thumb = new Finger(), m_Index = new Finger(), m_Middle = new Finger(), m_Ring = new Finger(), m_Pinky = new Finger();
        readonly List<Renderer> m_Hidden = new List<Renderer>();
        InputAction m_Grip, m_Trigger, m_TriggerTouch, m_ThumbTouch;

        /// <summary>Tip of the index finger (poke point), or null if the model has none.</summary>
        public Transform IndexTip { get; private set; }

        void Awake()
        {
            if (handRoot == null) return;
            Bind(m_Thumb, "Thumb");
            Bind(m_Index, "Index");
            Bind(m_Middle, "Middle");
            Bind(m_Ring, "Ring");
            Bind(m_Pinky, "Pinky");
            IndexTip = FindDeep(handRoot, "IndexTip");
            // Before FingertipSetup.Start adds its trigger sphere at the poke point.
            if (pokeFromIndexTip && IndexTip != null)
            {
                foreach (var poke in GetComponentsInChildren<XRPokeInteractor>(true))
                {
                    var point = new GameObject("Poke Point (index tip)").transform;
                    point.SetParent(IndexTip, false);
                    poke.attachTransform = point;
                }
            }
        }

        void Bind(Finger finger, string name)
        {
            finger.Valid = true;
            for (var k = 0; k < 3; k++)
            {
                var t = FindDeep(handRoot, name + (k + 1));
                var bend = t != null ? t.Find("Bend") : null;
                if (t == null || bend == null) { finger.Valid = false; return; }
                finger.Joints[k] = new Joint { T = t, Rest = t.localRotation, Axis = bend.localPosition.normalized };
            }
        }

        void OnEnable()
        {
            var hand = side == HandSide.Left ? "{LeftHand}" : "{RightHand}";
            m_Grip = Make(hand, "grip");
            m_Trigger = Make(hand, "trigger");
            m_TriggerTouch = Make(hand, "triggerTouched");
            m_ThumbTouch = Make(hand, "thumbstickTouched", "primaryTouched", "secondaryTouched", "thumbrestTouched");
        }

        void OnDisable()
        {
            foreach (var a in new[] { m_Grip, m_Trigger, m_TriggerTouch, m_ThumbTouch })
                if (a != null) { a.Disable(); a.Dispose(); }
            m_Grip = m_Trigger = m_TriggerTouch = m_ThumbTouch = null;
        }

        static InputAction Make(string hand, params string[] controls)
        {
            var action = new InputAction(type: InputActionType.PassThrough);
            foreach (var c in controls) action.AddBinding($"<XRController>{hand}/{c}");
            action.Enable();
            return action;
        }

        static float Read(InputAction a) => a != null ? a.ReadValue<float>() : 0f;

        IEnumerator Start()
        {
            if (!hideControllerModel) yield break;
            HideControllerModel();
            // Some rigs spawn the controller model a moment after start.
            yield return new WaitForSeconds(1f);
            HideControllerModel();
        }

        void HideControllerModel()
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (handRoot != null && r.transform.IsChildOf(handRoot)) continue;
                if (r.GetComponentInParent<WristTablet>(true) != null) continue;
                if (!r.enabled) continue;
                r.enabled = false;
                m_Hidden.Add(r);
            }
        }

        void LateUpdate()
        {
            if (handRoot == null) return;
            var grip = Read(m_Grip);
            var trigger = Read(m_Trigger);
            var triggerTouch = Read(m_TriggerTouch) > 0.5f || trigger > 0.05f;
            var thumbTouch = Read(m_ThumbTouch) > 0.5f;

            var fist = Vector3.Lerp(k_Open, k_Fist, grip);
            // Index: pointing when the finger is off the trigger, otherwise follows the pull.
            var index = triggerTouch ? Vector3.Lerp(k_IndexRest, k_IndexPull, trigger) : Vector3.Lerp(k_Open, k_IndexPoint, grip);
            // Thumb: resting on the face when touching it; up when gripping without touching (thumbs-up).
            var thumb = thumbTouch ? k_ThumbRest : Vector3.Lerp(k_ThumbOpen, k_ThumbUp, grip);

            var k = 1f - Mathf.Exp(-speed * Time.deltaTime);
            Pose(m_Index, index, k);
            Pose(m_Middle, fist, k);
            Pose(m_Ring, fist * 1.03f, k);
            Pose(m_Pinky, fist * 1.06f, k);
            Pose(m_Thumb, thumb, k);
        }

        static void Pose(Finger finger, Vector3 target, float k)
        {
            if (!finger.Valid) return;
            finger.Current = Vector3.Lerp(finger.Current, target, k);
            for (var j = 0; j < 3; j++)
            {
                var joint = finger.Joints[j];
                joint.T.localRotation = joint.Rest * Quaternion.AngleAxis(finger.Current[j], joint.Axis);
            }
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var hit = FindDeep(c, name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
