using System.Xml;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// The game's damage kinds. NPCInfo's damagetype numbers them in the order of the client's stats window
    /// (poison, arcane, fire ... piercing) from 1: spiders and vipers are 1, Infernal hounds 3, Ents 4, warriors
    /// and skeletons 9, trolls and livestock 10, crabs, rats and birds 11. Items name each kind's damage and
    /// resistance stats, NPCs a resistance percentage per kind.
    /// </summary>
    internal static class DamageKinds
    {
        public const int None = 0, Poison = 1, Arcane = 2, Fire = 3, Earth = 4, Wind = 5, Electric = 6, Psychic = 7,
            Ice = 8, Slash = 9, Blunt = 10, Piercing = 11, Count = 12;

        private static readonly string[] Names =
            { "", "Poison", "Arcane", "Fire", "Earth", "Wind", "Electric", "Psychic", "Ice", "Slash", "Blunt", "Piercing" };

        public static string Name(int kind) => kind > 0 && kind < Count ? Names[kind] : "None";

        /// <summary>Item data key of a kind's damage stat (FireDamage...; poison is DmgPoison).</summary>
        public static string DamageStat(int kind) => kind == Poison ? "DmgPoison" : Names[kind] + "Damage";

        /// <summary>Item data key of a kind's resistance stat (FireResistance...; poison is ResPoison).</summary>
        public static string ResistanceStat(int kind) => kind == Poison ? "ResPoison" : Names[kind] + "Resistance";

        /// <summary>An NPC's resistanceFire, resistanceSlash... attributes, by kind.</summary>
        public static int[] ReadResistances(XmlNode n)
        {
            var r = new int[Count];
            for (int k = 1; k < Count; k++) r[k] = GameData.IntAttr(n, "resistance" + Names[k]);
            return r;
        }

        /// <summary>
        /// How much of a blow of this kind gets through a resistance: 100% minus the percentage, so a weakness
        /// (negative) adds damage and 100 or more (Snowmen, Phraustie: 1000) stops it.
        /// </summary>
        public static float Through(int resistancePercent) => Mathf.Max(0f, 1f - resistancePercent / 100f);

        /// <summary>
        /// The element an ability deals, from its name, or None for a plain blow with the weapon. The data names
        /// no ability elements; the names do (Fire ball, Ice bash, Rockslide...).
        /// </summary>
        public static int OfAbility(string name)
        {
            string n = (name ?? string.Empty).ToLowerInvariant();
            if (Has(n, "poison", "venom")) return Poison;
            if (Has(n, "psych")) return Psychic;
            if (Has(n, "electric", "lightning")) return Electric;
            if (Has(n, "ice", "frost", "freeze", "glacial", "shatter", "cryo", "hail", "frozen", "icy")) return Ice;
            if (Has(n, "rock", "landmine", "stranglevine", "thorny")) return Earth;
            if (Has(n, "fire", "flame", "blaze", "combust", "immolat", "erupt", "burn", "torch", "blast")) return Fire;
            return None;
        }

        private static bool Has(string s, params string[] parts)
        {
            foreach (var p in parts) if (s.Contains(p)) return true;
            return false;
        }
    }
}
