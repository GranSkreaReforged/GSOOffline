using System.Collections.Generic;
using System.Text;

namespace GSOOffline
{
    /// <summary>
    /// Weapon abilities: bar slots 1-4 (keys 2-5) hold the equipped weapon's abilities. The client only lets the
    /// player drag abilities onto slots 5 and up (Q, E, R, T); slots 1-4 are the server's to fill (8/6), and items
    /// list theirs as Ability1..4 in their data string (the item window shows them). The item data names none:
    /// the old server handed them out. This table rebuilds them per weapon kind and damage element from the
    /// abilities' descriptions and the community wiki, so which weapon gets which abilities is a reconstruction.
    /// </summary>
    public partial class OfflineServer
    {
        public const int WeaponAbilitySlots = 4;   // slots 1-4; slot 0 is the basic attack

        // Weapons the wiki names abilities for.
        private static readonly Dictionary<int, int[]> WeaponAbilitiesByItem = new Dictionary<int, int[]>
        {
            { 367, new[] { 50, 48 } },   // Marcus's cutlass: Broadsword fire attack, Sword throw
        };

        private static readonly int[] None = new int[0];

        /// <summary>The abilities a weapon gives, in bar order (slot 1 first).</summary>
        internal static int[] WeaponAbilities(int typeId)
        {
            if (WeaponAbilitiesByItem.TryGetValue(typeId, out var fixedList)) return fixedList;
            var t = ItemData.Get(typeId);
            if (t == null || t.slot != "Weapon") return None;
            var f = t.flags;
            bool fire = HasStat(t, "FireDamage"), ice = HasStat(t, "IceDamage"), electric = HasStat(t, "ElectricDamage");
            bool earth = HasStat(t, "EarthDamage"), wind = HasStat(t, "WindDamage"), psychic = HasStat(t, "PsychicDamage");

            if (f.Contains("isDagger")) return new[] { 159, 160, 84, 85 };   // Stab, Poisonous stab, Frenzy, Slashing
            if (f.Contains("isBroadsword"))   // Brutal slash, Slashing, Cripple, then Sword throw or the element's attack
                return new[] { 86, 87, 88, fire ? 50 : ice ? 51 : electric ? 52 : 48 };
            if (f.Contains("isGreatsword")) return new[] { 86, 87, 89, 88 };   // Brutal slash, Slashing, Stun, Cripple
            if (f.Contains("isHalberd")) return new[] { ice ? 175 : 86, 87, 89, 88 };   // Halberd ice attack on icy ones
            if (f.Contains("isMace")) return new[] { 89, 88, 171 };   // Stun, Cripple, Thoracic trauma (Mace fighting 20)
            if (f.Contains("isBow")) return new[] { 62, 66, 65, 90 };   // Flame, Poison, Explosive arrow, Hunting net
            if (f.Contains("isCrossBow")) return new[] { 61, 91, 165, 67 };   // Focus, Piercing, Wounding shot, Weakness arrow
            if (f.Contains("isIceStaff")) return new[] { 127, 55, 123, 125 };   // Icy touch, Ice bash, Flashfreeze, Ice barrier
            if (f.Contains("isFireStaff"))   // every staff, whatever its element
            {
                if (earth) return new[] { 58, 53, 122, 115 };      // Rockslide, Staff bash, Landmine, Flame barrier
                if (wind) return new[] { 53, 121, 115 };           // Staff bash, Firestorm, Flame barrier
                if (psychic) return new[] { 1, 53, 149, 115 };     // Fire ball, Staff bash, Psychic glyph, Flame barrier
                return new[] { 1, 54, 119, 115 };                  // Fire ball, Flame bash, Fire wall, Flame barrier
            }
            if (f.Contains("isWand")) return ice ? new[] { 174, 125 } : new[] { 173, 149 };   // Condensed ice/psyche, a barrier/glyph
            if (f.Contains("isInstrument")) return new[] { 101, 102 };   // Tritone, Peace
            if (f.Contains("isTorch")) return new[] { 148, 106 };        // Torch throw, Burn
            return None;
        }

        private static bool HasStat(ItemTemplate t, string key) => t.stats.Exists(s => s.key == key);

        /// <summary>Ability1..4 lines for an item's data string, so the item window lists the weapon's abilities.</summary>
        private static void AppendWeaponAbilities(StringBuilder sb, ItemSave it)
        {
            var list = WeaponAbilities(it.typeId);
            for (int i = 0; i < list.Length && i < WeaponAbilitySlots; i++)
                sb.Append("Ability").Append(i + 1).Append('=').Append(list[i]).Append('\n');
        }

        /// <summary>The ability in weapon slot 1-4 for the weapon in hand, or 0.</summary>
        private int WeaponAbilityInSlot(int slot)
        {
            var w = EquippedWeapon();
            var list = w != null ? WeaponAbilities(w.typeId) : None;
            return slot >= 1 && slot <= list.Length ? list[slot - 1] : 0;
        }

        /// <summary>8/6 for slots 1-4: the weapon in hand's abilities on keys 2-5 (empty without one).</summary>
        private void SendWeaponAbilities()
        {
            if (character == null) return;
            for (int slot = 1; slot <= WeaponAbilitySlots; slot++)
                Send(8, 6, character.name, slot, WeaponAbilityInSlot(slot));
        }

        /// <summary>Equipment changed: max health and mana and the weapon's abilities may have too.</summary>
        private void GearChanged()
        {
            HealthStatsChanged();
            ManaStatsChanged();
            SendGearStats();
            SendWeaponAbilities();
            RefreshAlignment();
        }
    }
}
