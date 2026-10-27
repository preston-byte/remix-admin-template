using UnityEngine;

namespace GroundCrew.Crew
{
    public enum CrewPose { Idle, Walk, Sit, Drive, Crouch, Wave }

    /// <summary>
    /// Procedural animation for the generated jointed figures (Tools/CharacterGenerator): walk
    /// cycle from the speed, idle breathing and glances, sitting, driving, crouching to place
    /// things, waving, and carrying with both hands. No animation clips or skinning; every joint
    /// is a node whose rest rotation is identity, posed here in LateUpdate.
    /// </summary>
    public class CrewAnimator : MonoBehaviour
    {
        public CrewPose Pose = CrewPose.Idle;
        [Tooltip("Ground speed (m/s), set by CrewAgent.")] public float Speed;
        public bool Carrying;
        [SerializeField] float blend = 10f;

        Transform m_Hips, m_Spine, m_Neck, m_Head;
        readonly Transform[] m_UpperArm = new Transform[2], m_LowerArm = new Transform[2], m_UpperLeg = new Transform[2], m_LowerLeg = new Transform[2], m_Foot = new Transform[2];
        Vector3 m_HipsRest;
        float m_Phase, m_Time, m_LookYaw, m_LookTarget, m_NextLook;

        void Awake()
        {
            m_Hips = Find("Hips");
            m_Spine = Find("Spine");
            m_Neck = Find("Neck");
            m_Head = Find("Head");
            for (var i = 0; i < 2; i++)
            {
                var s = i == 0 ? "_L" : "_R";
                m_UpperArm[i] = Find("UpperArm" + s);
                m_LowerArm[i] = Find("LowerArm" + s);
                m_UpperLeg[i] = Find("UpperLeg" + s);
                m_LowerLeg[i] = Find("LowerLeg" + s);
                m_Foot[i] = Find("Foot" + s);
            }
            if (m_Hips != null) m_HipsRest = m_Hips.localPosition;
            m_Time = Random.value * 10f;
        }

        public Transform HandAttach(bool right) => Find(right ? "HandAttach_R" : "HandAttach_L");

        void LateUpdate()
        {
            if (m_Hips == null) return;
            var dt = Time.deltaTime;
            m_Time += dt;
            var k = 1f - Mathf.Exp(-blend * dt);
            var walking = Pose == CrewPose.Walk && Speed > 0.05f;
            if (walking) m_Phase += Speed / 1.45f * 2f * Mathf.PI * dt;   // ~1.45 m per stride cycle

            // Defaults: standing.
            var hips = m_HipsRest;
            float spineX = 1.5f * Mathf.Sin(m_Time * 1.3f), spineY = 0f, neckX = 0f, headY = 0f;
            var ua = new Vector3[2];
            var la = new Vector3[2];
            var ul = new Vector3[2];
            var ll = new Vector3[2];
            var ft = new Vector3[2];
            for (var i = 0; i < 2; i++)
            {
                var outward = i == 0 ? -1f : 1f;
                ua[i] = new Vector3(2f, 0f, outward * 5f);
                la[i] = new Vector3(-10f, 0f, 0f);
            }

            switch (Pose)
            {
                case CrewPose.Walk:
                {
                    var amp = 26f * Mathf.Clamp01(Speed / 1.3f);
                    for (var i = 0; i < 2; i++)
                    {
                        var ph = m_Phase + (i == 0 ? 0f : Mathf.PI);
                        var swing = Mathf.Sin(ph);
                        ul[i] = new Vector3(-amp * swing, 0f, 0f);
                        ll[i] = new Vector3(6f + amp * 1.5f * Mathf.Max(0f, Mathf.Sin(ph + 1.2f)), 0f, 0f);
                        ft[i] = new Vector3(amp * 0.3f * swing, 0f, 0f);
                        ua[i].x = amp * 0.75f * swing;
                        la[i].x = -15f - amp * 0.4f * Mathf.Max(0f, -swing);
                    }
                    hips.y -= 0.022f * Mathf.Abs(Mathf.Sin(m_Phase));
                    spineY = 4f * Mathf.Sin(m_Phase);
                    spineX = 3f;
                    break;
                }
                case CrewPose.Sit:
                case CrewPose.Drive:
                    for (var i = 0; i < 2; i++)
                    {
                        ul[i] = new Vector3(-88f, 0f, (i == 0 ? -1f : 1f) * 4f);
                        ll[i] = new Vector3(85f, 0f, 0f);
                        ft[i] = new Vector3(0f, 0f, 0f);
                        ua[i] = Pose == CrewPose.Drive ? new Vector3(-48f, 0f, (i == 0 ? 1f : -1f) * 8f) : new Vector3(-22f, 0f, (i == 0 ? -1f : 1f) * 4f);
                        la[i] = new Vector3(Pose == CrewPose.Drive ? -42f : -55f, 0f, 0f);
                    }
                    hips.y = m_HipsRest.y - 0.44f;
                    hips.z = m_HipsRest.z - 0.04f;
                    spineX = -4f + Mathf.Sin(m_Time * 1.3f);
                    break;
                case CrewPose.Crouch:
                    for (var i = 0; i < 2; i++)
                    {
                        ul[i] = new Vector3(-105f, 0f, (i == 0 ? -1f : 1f) * 8f);
                        ll[i] = new Vector3(120f, 0f, 0f);
                        ft[i] = new Vector3(-15f, 0f, 0f);
                        ua[i] = new Vector3(-62f, 0f, (i == 0 ? 1f : -1f) * 6f);
                        la[i] = new Vector3(-18f, 0f, 0f);
                    }
                    hips.y = m_HipsRest.y - 0.54f;
                    hips.z = m_HipsRest.z - 0.12f;
                    spineX = 32f;
                    neckX = -15f;
                    break;
                case CrewPose.Wave:
                    ua[1] = new Vector3(-10f, 0f, 155f);
                    la[1] = new Vector3(0f, 0f, 25f * Mathf.Sin(m_Time * 9f));
                    break;
            }

            if (Carrying && Pose != CrewPose.Crouch && Pose != CrewPose.Sit && Pose != CrewPose.Drive)
                for (var i = 0; i < 2; i++)
                {
                    ua[i] = new Vector3(-28f, 0f, (i == 0 ? 1f : -1f) * 4f);
                    la[i] = new Vector3(-62f, 0f, 0f);
                }

            // Idle glances.
            if (Pose == CrewPose.Idle && m_Time > m_NextLook)
            {
                m_NextLook = m_Time + Random.Range(2f, 5f);
                m_LookTarget = Random.Range(-40f, 40f);
            }
            else if (Pose != CrewPose.Idle) m_LookTarget = 0f;
            m_LookYaw = Mathf.Lerp(m_LookYaw, m_LookTarget, k * 0.5f);
            headY = m_LookYaw;

            m_Hips.localPosition = Vector3.Lerp(m_Hips.localPosition, hips, k);
            Set(m_Spine, new Vector3(spineX, spineY, 0f), k);
            Set(m_Neck, new Vector3(neckX, 0f, 0f), k);
            Set(m_Head, new Vector3(0f, headY, 0f), k);
            for (var i = 0; i < 2; i++)
            {
                Set(m_UpperArm[i], ua[i], k);
                Set(m_LowerArm[i], la[i], k);
                Set(m_UpperLeg[i], ul[i], k);
                Set(m_LowerLeg[i], ll[i], k);
                Set(m_Foot[i], ft[i], k);
            }
        }

        static void Set(Transform t, Vector3 euler, float k)
        {
            if (t != null) t.localRotation = Quaternion.Slerp(t.localRotation, Quaternion.Euler(euler), k);
        }

        Transform Find(string name) => FindDeep(transform, name);

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
