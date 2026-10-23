using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Weapon abilities, as the original server did them. Bar slots 1-4 (keys 2-5) are the server's to fill (8/6);
    /// the client only lets the player drag abilities onto slots 5 and up. Every item rolls its own Ability1..4 once,
    /// when it's made (Scr_ItemHandler.getDefaultAbility1..4: some kinds pick at random, so two broadswords can
    /// differ), and lists them in its data string. Scr_PlayerInventory.setItemAbilities puts a two-handed weapon's
    /// four on slots 1-4; anything else gives slots 1-2 from the weapon and 3-4 from the shield slot's item (a
    /// shield, a torch, an off-hand pistol or the rat fang). Two-handed weapons and the shield slot exclude each other.
    /// </summary>
    public partial class OfflineServer
    {
        public const int WeaponAbilitySlots = 4;   // slots 1-4; slot 0 is the basic attack

        // ---- item kinds, as the server's Scr_ItemHandler (item flags plus its hard-coded early items) ------------

        private static bool IsKind(int typeId, string flag, params int[] ids)
        {
            var t = ItemData.Get(typeId);
            return t != null && t.flags.Contains(flag) || System.Array.IndexOf(ids, typeId) >= 0;
        }

        private static bool IsInstrument(int id) => IsKind(id, "isInstrument");
        private static bool IsGreatsword(int id) => IsKind(id, "isGreatsword", 71, 83, 97, 184);
        private static bool IsHalberd(int id) => IsKind(id, "isHalberd", 81, 82, 150, 190);
        private static bool IsMace(int id) => IsKind(id, "isMace", 51, 84, 179, 180, 181);
        private static bool IsBroadsword(int id) => IsKind(id, "isBroadsword", 183, 187, 188, 189);
        private static bool IsIceStaff(int id) => IsKind(id, "isIceStaff");
        private static bool IsFireStaff(int id) => IsKind(id, "isFireStaff");
        private static bool IsWand(int id) => IsKind(id, "isWand", 111);
        private static bool IsDagger(int id) => IsKind(id, "isDagger", 79, 99, 100, 185);
        private static bool IsShield(int id) => IsKind(id, "isShield", 98, 105, 52, 68, 69, 191, 192);
        private static bool IsTorch(int id) => IsKind(id, "isTorch");
        private static bool IsCrossBow(int id) => IsKind(id, "isCrossBow", 50, 75, 121);
        private static bool IsBow(int id) => IsKind(id, "isBow", 46, 74, 114);

        /// <summary>The server's isTwoHanded. Unlike the client's, staffs are one-handed.</summary>
        internal static bool IsTwoHanded(int id) =>
            IsGreatsword(id) || IsHalberd(id) || IsKind(id, "isScythe", 126) || IsKind(id, "isHatchet", 58, 166)
            || IsKind(id, "isFishingRod", 90) || IsKind(id, "isPickaxe", 59) || IsBow(id) || IsCrossBow(id)
            || IsKind(id, "isTwoHanded");

        // ---- rolling an item's abilities ----------------------------------------------------------------------

        private const int ZalranazCleaver = 463, SerpentsFang = 468, LydiasStaff = 473, RokesStaff = 274, StorageGuardsCrossbow = 459;

        // Unique weapons with fixed abilities (the original's getDefaultAbility1..4 special cases). Saved copies are
        // brought in line when loaded: these slots used to be reconstructions.
        private static readonly Dictionary<int, int[]> FixedUniqueAbilities = new Dictionary<int, int[]>
        {
            { ZalranazCleaver, new[] { 52, 173, 0, 0 } },          // Broadsword electric attack, Condensed psyche
            { SerpentsFang, new[] { 175, 174, 88, 26 } },          // Halberd ice attack, Condensed ice, Cripple, Heavy shield block
            { StorageGuardsCrossbow, new[] { 65, 90, 61, 172 } },  // Explosive arrow, Hunting net, Barrage, Landmine
        };

        /// <summary>A new item's Ability1..4 (0 for none).</summary>
        internal static int[] RollAbilities(int typeId)
        {
            if (FixedUniqueAbilities.TryGetValue(typeId, out var fixedList)) return (int[])fixedList.Clone();
            var list = new[] { DefaultAbility1(typeId), DefaultAbility2(typeId), DefaultAbility3(typeId), DefaultAbility4(typeId) };
            if (typeId == LydiasStaff) list[1] = 176;   // Summon skeletal berserk
            if (typeId == RokesStaff) list[1] = 177;    // Summon skeleton
            return list;
        }

        private static readonly int[] IceStaffPool = { 123, 124, 125, 126, 127 };   // Flashfreeze, Waterwalk, Ice barrier, Cryostasis, Icy touch
        private static readonly int[] FireStaffPool = { 1, 119, 115, 121, 122 };     // Fire ball, Fire wall, Flame barrier, Firestorm, Landmine

        private static int DefaultAbility1(int id)
        {
            if (IsInstrument(id)) return 101;            // Tritone
            if (IsGreatsword(id)) return 86;             // Brutal slash
            if (IsHalberd(id)) return 86;
            if (IsMace(id)) return 92;                   // Swing
            if (IsBroadsword(id)) return Pick(new[] { 50, 51, 52 });   // Broadsword fire, ice or electric attack
            if (IsIceStaff(id)) return Pick(IceStaffPool);
            if (IsFireStaff(id)) return Pick(FireStaffPool);
            if (IsWand(id)) return 43;
            if (IsDagger(id)) return 84;                 // Frenzy
            if (IsShield(id)) return 26;                 // Heavy shield block
            if (IsTorch(id)) return id == 413 ? 166 : Pick(new[] { 106, 148 });   // Dragon torch: Dragon flames; else Burn or Torch throw
            if (IsCrossBow(id)) return 91;               // Piercing shot
            if (IsBow(id)) return Pick(new[] { 62, 63, 64 });      // Flame, ice or electric arrow
            if (id == 250) return 76;                    // off-hand pistol: load
            if (id == 408) return 159;                   // rat fang: Stab
            if (id == 426) return 164;                   // main-hand pistol: shoot
            return 0;
        }

        private static int DefaultAbility2(int id)
        {
            if (IsInstrument(id)) return 102;            // Peace
            if (IsGreatsword(id)) return 87;             // Slashing
            if (IsHalberd(id)) return 89;                // Stun
            if (IsMace(id)) return 93;                   // Cripple
            if (IsBroadsword(id)) return 48;             // Sword throw
            if (IsIceStaff(id)) return Pick(IceStaffPool);
            if (IsFireStaff(id)) return Pick(FireStaffPool);
            if (IsDagger(id)) return 85;                 // Slashing
            if (IsWand(id)) return 34;
            if (IsShield(id)) return id == 396 ? 149 : 15;   // Dragonbone shield: Psychic glyph; else Thick skin
            if (IsTorch(id)) return 107;                 // Fire wisp
            if (IsCrossBow(id)) return 90;               // Hunting net
            if (IsBow(id)) return id == 432 ? 90 : 65;   // Hunter's bow: Hunting net; else Explosive arrow
            if (id == 250) return 77;                    // off-hand pistol: shoot
            if (id == 426) return 163;                   // main-hand pistol: load
            if (id == 408) return 160;                   // rat fang: Poisonous stab
            return 0;
        }

        private static int DefaultAbility3(int id)
        {
            if (IsGreatsword(id)) return 88;             // Cripple
            if (IsHalberd(id)) return 88;
            if (IsCrossBow(id)) return 61;               // Focus
            if (IsBow(id)) return id == 402 ? 154 : id == 432 ? 165 : 66;   // Venom arrow, Wounding shot, Poison arrow
            return 0;
        }

        private static int DefaultAbility4(int id)
        {
            if (id == 394 || id == 395 || id == 410) return 147;   // carried boulder, pillar, rum crate: Drop
            if (IsGreatsword(id) || IsHalberd(id) || IsCrossBow(id)) return 26;   // Heavy shield block
            if (IsBow(id)) return id == 402 ? 155 : 67;  // Venomous bow: Thorny vines; else Weakness arrow
            return 0;
        }

        /// <summary>The item's own abilities, rolled now if it predates them (saves from before this).</summary>
        private static int[] ItemAbilities(ItemSave it)
        {
            if (it.abilities == null || it.abilities.Length != WeaponAbilitySlots
                || FixedUniqueAbilities.TryGetValue(it.typeId, out var fixedList) && !SameAbilities(it.abilities, fixedList))
                it.abilities = RollAbilities(it.typeId);
            return it.abilities;
        }

        private static bool SameAbilities(int[] a, int[] b)
        {
            for (int i = 0; i < WeaponAbilitySlots; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Ability1..4 lines for an item's data string; the item window lists them.</summary>
        private static void AppendWeaponAbilities(StringBuilder sb, ItemSave it)
        {
            if (ItemData.Get(it.typeId)?.Stacks != false) return;
            var list = ItemAbilities(it);
            for (int i = 0; i < WeaponAbilitySlots; i++)
                sb.Append("Ability").Append(i + 1).Append('=').Append(list[i]).Append('\n');
        }

        // ---- the bar: slots 1-4 ------------------------------------------------------------------------------

        /// <summary>The ability in weapon slot 1-4 from what's in hand, or 0. Abilities the game's data lacks
        /// (the mace's Swing and Cripple and the wand's two were cut) leave the slot empty.</summary>
        private int WeaponAbilityInSlot(int slot)
        {
            if (character == null || slot < 1 || slot > WeaponAbilitySlots) return 0;
            var w = EquippedWeapon();
            int ability;
            if (w != null && IsTwoHanded(w.typeId)) ability = ItemAbilities(w)[slot - 1];
            else
            {
                var source = slot <= 2 ? w : EquippedInSlot("Shield");
                ability = source != null ? ItemAbilities(source)[(slot - 1) % 2] : 0;
            }
            return SkillData.Abilities.ContainsKey(ability) ? ability : 0;
        }

        private readonly int[] weaponSlotAbility = new int[WeaponAbilitySlots + 1];   // as last sent, by slot

        // The weapon on the back keeps its slot abilities and their cooldowns (seconds left) for when it comes back
        // (Scr_PlayerInventory.secondaryWeaponAbility/Cooldown1..4).
        private readonly int[] backSlotAbility = new int[WeaponAbilitySlots + 1];
        private readonly float[] backSlotCooldown = new float[WeaponAbilitySlots + 1];

        private int shieldOffForTool;   // the shield a gathering tool pushed off, put back on with a one-handed weapon

        private void ResetWeaponSlots()
        {
            shieldOffForTool = 0;
            for (int s = 0; s <= WeaponAbilitySlots; s++)
            {
                weaponSlotAbility[s] = backSlotAbility[s] = 0;
                backSlotCooldown[s] = 0f;
            }
        }

        private float SlotCooldownLeft(int slot) =>
            slotReadyAt.TryGetValue(slot, out float ready) ? Mathf.Max(0f, ready - Time.time) : 0f;

        private void SetSlotCooldown(int slot, float seconds)
        {
            slotReadyAt[slot] = Time.time + seconds;
            int cs = Mathf.RoundToInt(seconds * 100f);
            if (cs > 0) Send(4, 0, character.name, slot, cs, cs);
        }

        /// <summary>
        /// 8/6 for slots 1-4: the weapon in hand's abilities on keys 2-5. An ability that moves keeps its cooldown,
        /// from the slot it was on or from the back weapon (Scr_AbilitiesHandler.setAbilityToSlot).
        /// </summary>
        private void SendWeaponAbilities()
        {
            if (character == null) return;
            var before = (int[])weaponSlotAbility.Clone();
            var left = new float[WeaponAbilitySlots + 1];
            for (int s = 1; s <= WeaponAbilitySlots; s++) left[s] = SlotCooldownLeft(s);

            for (int slot = 1; slot <= WeaponAbilitySlots; slot++)
            {
                int ability = WeaponAbilityInSlot(slot);
                if (ability != 0 && ability != before[slot])
                {
                    for (int s = 1; s <= WeaponAbilitySlots; s++)
                        if (before[s] == ability) { SetSlotCooldown(slot, left[s]); break; }
                    for (int s = 1; s <= WeaponAbilitySlots; s++)
                        if (backSlotAbility[s] == ability) SetSlotCooldown(slot, backSlotCooldown[s]);
                }
                weaponSlotAbility[slot] = ability;
                Send(8, 6, character.name, slot, ability);
            }
        }

        /// <summary>
        /// 6/5 "Switch weapon" (Scr_PlayerInventory.ChangeToSecondaryWeapon): the back weapon goes in hand, the one
        /// in hand on the back, each taking its slot cooldowns along. Slots 3-4 are the hand weapon's own only when
        /// it's two-handed or a shield is on; otherwise they stay with the shield.
        /// </summary>
        private void SwitchWeapon()
        {
            var back = character != null ? SecondaryWeapon() : null;
            if (back == null) return;
            var hand = EquippedWeapon();
            bool handOwnsAll = hand != null && IsTwoHanded(hand.typeId) || EquippedInSlot("Shield") != null;

            var restore = (float[])backSlotCooldown.Clone();
            for (int s = 1; s <= WeaponAbilitySlots; s++)
            {
                if (s > 2 && !handOwnsAll) continue;
                backSlotAbility[s] = weaponSlotAbility[s];
                backSlotCooldown[s] = SlotCooldownLeft(s);
            }
            Equip(back.id);
            // The original restored slots 3-4 even when the shield fills them, so swapping reset its cooldowns.
            bool backOwnsAll = IsTwoHanded(back.typeId);
            for (int s = 1; s <= WeaponAbilitySlots; s++)
                if (s <= 2 || backOwnsAll) SetSlotCooldown(s, restore[s]);
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

        /// <summary>Dev: log the weapon bar (bridge command weaponbar).</summary>
        internal void DevWeaponBar()
        {
            if (character == null) return;
            string Describe(ItemSave it) => it == null ? "none"
                : $"{ItemData.Get(it.typeId)?.name} ({it.typeId}{(IsTwoHanded(it.typeId) ? ", two-handed" : "")}) abilities {string.Join(",", System.Array.ConvertAll(ItemAbilities(it), a => a.ToString()))}";
            Plugin.Log.LogInfo($"[dev] weaponbar hand: {Describe(EquippedWeapon())}");
            Plugin.Log.LogInfo($"[dev] weaponbar shield slot: {Describe(EquippedInSlot("Shield"))}");
            Plugin.Log.LogInfo($"[dev] weaponbar back: {Describe(SecondaryWeapon())}");
            for (int s = 1; s <= WeaponAbilitySlots; s++)
            {
                int a = weaponSlotAbility[s];
                string name = SkillData.Abilities.TryGetValue(a, out var info) ? info.name : "-";
                Plugin.Log.LogInfo($"[dev] weaponbar slot {s}: {a} {name}, cooldown {SlotCooldownLeft(s):0.0} s");
            }
        }

        /// <summary>
        /// Login: a save from before two-handed weapons and the shield slot excluded each other may have both on;
        /// the shield comes off, as equipping the weapon would have done.
        /// </summary>
        private static void FixTwoHandedWithShield(CharacterSave ch)
        {
            ItemSave weapon = null, shield = null;
            foreach (var it in ch.items)
            {
                if (!it.equipped) continue;
                string slot = ItemData.Get(it.typeId)?.slot;
                if (slot == "Weapon") weapon = it;
                else if (slot == "Shield") shield = it;
            }
            if (weapon == null || shield == null || !IsTwoHanded(weapon.typeId)) return;
            shield.equipped = false;
            Plugin.Log.LogInfo($"[inventory] {ItemData.Get(shield.typeId)?.name} unequipped: {ItemData.Get(weapon.typeId)?.name} is two-handed");
        }
    }
}
