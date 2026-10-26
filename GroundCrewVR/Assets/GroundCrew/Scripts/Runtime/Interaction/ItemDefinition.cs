using System;
using UnityEngine;

namespace GroundCrew.Interaction
{
    /// <summary>
    /// What kind of physical object something is. Flags, so a zone can accept several
    /// categories (a cargo hold accepts Suitcase | Duffel | Box | Fragile ...) and an item can be
    /// more than one thing (a fragile box is Box | Fragile).
    /// </summary>
    [Flags]
    public enum ItemCategory
    {
        None = 0,
        Suitcase = 1 << 0,
        Duffel = 1 << 1,
        Box = 1 << 2,
        Fragile = 1 << 3,
        Oversize = 1 << 4,
        Cargo = 1 << 5,
        Catering = 1 << 6,
        SafetyCone = 1 << 7,
        WheelChock = 1 << 8,
        Equipment = 1 << 9,
    }

    /// <summary>
    /// Data for a class of physical item (not one specific bag). Keeping this data-driven means
    /// designers can add bag types, cargo and equipment without code changes, and the
    /// turnaround/loading rules can reason about weight and category.
    /// </summary>
    [CreateAssetMenu(menuName = "Ground Crew/Item Definition", fileName = "Item_New")]
    public class ItemDefinition : ScriptableObject
    {
        [Tooltip("Stable id used by save data, loading manifests and networking.")]
        public string itemId = "item.new";

        public string displayName = "Item";

        public ItemCategory category = ItemCategory.Suitcase;

        [Min(0.1f)]
        [Tooltip("Real-world mass. Drives the Rigidbody, how sluggish it feels in the hand, and hold weight totals.")]
        public float massKg = 15f;

        [Tooltip("Heavy items (IATA 'heavy' tag is >23 kg) move very slowly unless held with both hands.")]
        public bool requiresTwoHands;

        public bool fragile;

        [Min(0.5f)]
        [Tooltip("Impact speed (m/s) above which the item takes damage. Fragile items should be lower.")]
        public float impactDamageSpeed = 4f;

        [TextArea] public string notes;
    }
}
