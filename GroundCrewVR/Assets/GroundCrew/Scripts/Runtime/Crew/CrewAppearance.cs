using System.Collections.Generic;
using UnityEngine;

namespace GroundCrew.Crew
{
    /// <summary>
    /// Gives each spawned person their own skin and hair colour (and, for passengers, clothes and
    /// whether they carry a backpack), so a crowd doesn't look like clones. Colours are applied by
    /// swapping in tinted copies of the shared materials (one copy per colour, cached), which keeps
    /// the SRP Batcher working; materials are matched by name: Skin, Hair, Top, Bottom.
    /// </summary>
    public class CrewAppearance : MonoBehaviour
    {
        [System.Serializable]
        public struct Choice
        {
            public int Skin, Hair, Top, Bottom;
            public bool Backpack;
        }

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
        static readonly Color[] k_Top =
        {
            new Color(0.55f, 0.15f, 0.15f), new Color(0.15f, 0.30f, 0.55f), new Color(0.90f, 0.90f, 0.88f), new Color(0.12f, 0.12f, 0.13f),
            new Color(0.25f, 0.45f, 0.25f), new Color(0.85f, 0.65f, 0.20f), new Color(0.50f, 0.30f, 0.55f), new Color(0.45f, 0.45f, 0.48f),
        };
        static readonly Color[] k_Bottom =
        {
            new Color(0.15f, 0.20f, 0.35f), new Color(0.10f, 0.10f, 0.11f), new Color(0.45f, 0.38f, 0.28f), new Color(0.30f, 0.32f, 0.35f),
            new Color(0.22f, 0.30f, 0.45f),
        };
        static readonly Dictionary<(Material, Color), Material> s_Tinted = new Dictionary<(Material, Color), Material>();

        [SerializeField] bool randomise = true;
        bool m_Set;

        public Choice Current { get; private set; }

        void Start()
        {
            if (!m_Set && randomise) Apply(Random());
        }

        public static Choice Random() => new Choice
        {
            Skin = UnityEngine.Random.Range(0, k_Skin.Length), Hair = UnityEngine.Random.Range(0, k_Hair.Length),
            Top = UnityEngine.Random.Range(0, k_Top.Length), Bottom = UnityEngine.Random.Range(0, k_Bottom.Length),
            Backpack = UnityEngine.Random.value < 0.55f,
        };

        /// <summary>Dress this person (call before or after Start; a later call wins).</summary>
        public void Apply(Choice choice)
        {
            m_Set = true;
            Current = choice;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                var changed = false;
                for (var i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var baseMat = Original(mats[i]);
                    var n = baseMat.name;
                    Color? c = n.StartsWith("Skin") ? k_Skin[choice.Skin % k_Skin.Length]
                        : n.StartsWith("Hair") ? k_Hair[choice.Hair % k_Hair.Length]
                        : n.StartsWith("Top") ? k_Top[choice.Top % k_Top.Length]
                        : n.StartsWith("Bottom") ? k_Bottom[choice.Bottom % k_Bottom.Length]
                        : (Color?)null;
                    if (c == null) continue;
                    mats[i] = Tinted(baseMat, c.Value);
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "Backpack") t.gameObject.SetActive(choice.Backpack);
        }

        static readonly Dictionary<Material, Material> s_Originals = new Dictionary<Material, Material>();

        static Material Original(Material m) => s_Originals.TryGetValue(m, out var o) ? o : m;

        static Material Tinted(Material original, Color color)
        {
            if (s_Tinted.TryGetValue((original, color), out var m) && m != null) return m;
            m = new Material(original) { name = original.name + " (tint)", color = color, enableInstancing = true };
            s_Tinted[(original, color)] = m;
            s_Originals[m] = original;
            return m;
        }
    }
}
