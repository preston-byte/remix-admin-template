using UnityEngine;

namespace GroundCrew.Crew
{
    /// <summary>
    /// Gives each spawned person their own skin and hair colour, so a crew of six doesn't look
    /// like clones. Sets _BaseColor (URP Simple Lit / Lit) through a MaterialPropertyBlock on the
    /// sub-meshes that use the "Skin" and "Hair" materials, so no materials are duplicated.
    /// </summary>
    public class CrewAppearance : MonoBehaviour
    {
        static readonly Color[] k_Skin =
        {
            new Color(0.96f, 0.80f, 0.69f), new Color(0.89f, 0.70f, 0.57f), new Color(0.80f, 0.60f, 0.46f),
            new Color(0.66f, 0.46f, 0.33f), new Color(0.50f, 0.34f, 0.24f), new Color(0.36f, 0.24f, 0.17f),
        };
        static readonly Color[] k_Hair =
        {
            new Color(0.08f, 0.06f, 0.05f), new Color(0.20f, 0.13f, 0.08f), new Color(0.40f, 0.27f, 0.15f),
            new Color(0.70f, 0.55f, 0.32f), new Color(0.55f, 0.55f, 0.55f), new Color(0.30f, 0.12f, 0.06f),
        };
        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] bool randomise = true;

        void Start()
        {
            if (!randomise) return;
            var skin = k_Skin[Random.Range(0, k_Skin.Length)];
            var hair = k_Hair[Random.Range(0, k_Hair.Length)];
            var block = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var n = mats[i].name;
                    Color? c = n.StartsWith("Skin") ? skin : n.StartsWith("Hair") ? hair : (Color?)null;
                    if (c == null) continue;
                    r.GetPropertyBlock(block, i);
                    block.SetColor(k_BaseColor, c.Value);
                    r.SetPropertyBlock(block, i);
                }
            }
        }
    }
}
