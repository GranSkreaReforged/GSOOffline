using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    public partial class OfflineServer
    {
        // Base "Health" stat plus Health rolled on equipped items, as the client's stats window computes it.
        private int MaxHealth()
        {
            int max = character.health;
            foreach (var it in character.items)
            {
                if (!it.equipped) continue;
                foreach (var s in it.stats)
                    if (s.key == "Health") max += s.value;
            }
            return Mathf.Max(1, max + BonusHealth());
        }

        // StatType 36: the client's bonus health (gear and Survivor) on top of the base stat. The client caps health at
        // base + bonus and shows any change above the cap as a heal number, so it must hear the bonus before the health.
        private const int StatBonusHealth = 36;

        private void SendHealth()
        {
            Send(16, 5, StatBonusHealth, MaxHealth() - character.health);
            Send(8, 13, character.name, character.currentHealth, MaxHealth());
        }

        /// <summary>Gear changed: max health may have moved, so health is capped to it and both are sent.</summary>
        private void HealthStatsChanged()
        {
            character.currentHealth = Mathf.Min(character.currentHealth, MaxHealth());
            SendHealth();
        }

        private int MaxMana() => Mathf.Max(0, character.mana + ManaPerGrimoireLevel * SkillLevel(SkillElementalGrim) + GearStat("Mana"));

        // Reconstructed: the pool grows with Elemental grimoire, or its dearest spells (Fire wall 185, Flame barrier
        // 120) could never be cast on the starting 100. The original server's rule is lost.
        private const int ManaPerGrimoireLevel = 5, SkillElementalGrim = 20;

        // Mana regained per regeneration tick: 2, plus Manaregen rolled on equipped items.
        private int ManaRegen() => Mathf.Max(0, BaseManaRegen + GearStat("Manaregen"));
        private const int BaseManaRegen = 2;

        private int GearStat(string key)
        {
            int sum = 0;
            foreach (var it in character.items)
                if (it.equipped)
                    foreach (var s in it.stats)
                        if (s.key == key) sum += s.value;
            return sum;
        }

        // StatTypes 43 and 44: the client's bonus mana and mana regeneration, as 36 is for health. The HUD's mana bar
        // and the stats window use base + bonus, so without them gear mana never showed.
        private const int StatBonusMana = 43, StatBonusManaRegen = 44;

        private void SendMana()
        {
            Send(16, 5, StatBonusMana, MaxMana() - character.mana);
            Send(16, 5, StatBonusManaRegen, GearStat("Manaregen"));
            Send(8, 15, character.name, character.currentMana, MaxMana());
        }

        /// <summary>Gear changed: max mana may have moved, so mana is capped to it and both are sent.</summary>
        private void ManaStatsChanged()
        {
            character.currentMana = Mathf.Min(character.currentMana, MaxMana());
            SendMana();
        }

        private float regenTimer;

        // Health regained per out-of-combat tick: 1, plus Healthregen rolled on equipped items.
        private int HealthRegen() => Mathf.Max(0, 1 + GearStat("Healthregen"));

        // Gear stat key -> the client's bonus StatType (base type + 35), for the stats window. Health and mana go with
        // their own updates. Movementspeed is left out: the client sets your run speed to base + bonus, and the
        // server never sends a base, so any bonus would become your whole speed.
        private static readonly KeyValuePair<string, int>[] GearBonusStats =
        {
            Pair("Healthregen", 37), Pair("Damage", 38), Pair("Damageblock", 39), Pair("StunResistance", 41),
            Pair("Cooldown", 42), Pair("Healing", 45), Pair("DmgPoison", 46), Pair("ResPoison", 47), Pair("Range", 48),
            Pair("Warmth", 49), Pair("Carryingcap", 50), Pair("ArcaneDamage", 51), Pair("ArcaneResistance", 52),
            Pair("FireDamage", 53), Pair("EarthDamage", 54), Pair("WindDamage", 55), Pair("ElectricDamage", 56),
            Pair("PsychicDamage", 57), Pair("IceDamage", 58), Pair("FireResistance", 59), Pair("EarthResistance", 60),
            Pair("WindResistance", 61), Pair("ElectricResistance", 62), Pair("PsychicResistance", 63),
            Pair("IceResistance", 64), Pair("SlashDamage", 65), Pair("BluntDamage", 66), Pair("PiercingDamage", 67),
            Pair("SlashResistance", 68), Pair("BluntResistance", 69), Pair("PiercingResistance", 70),
        };

        private static KeyValuePair<string, int> Pair(string key, int stat) => new KeyValuePair<string, int>(key, stat);

        /// <summary>16/5 for every gear bonus the stats window shows; it only knew health and mana before.</summary>
        private void SendGearStats()
        {
            foreach (var kv in GearBonusStats) Send(16, 5, kv.Value, GearStat(kv.Key));
            SendEffectiveResistances();
            SendCombatLevel();
        }

        /// <summary>Out of combat: HealthRegen() per 3 s; mana always ManaRegen() per 3 s.</summary>
        private void TickRegen()
        {
            if (character == null || !sceneReady || PlayerDead) return;
            regenTimer -= Time.deltaTime;
            if (regenTimer > 0f) return;
            regenTimer = 3f;
            bool inCombat = autoAttacking || (world != null && world.npcs.Exists(n => n.aggro));
            if (!inCombat && character.currentHealth < MaxHealth()) Heal(HealthRegen());
            if (character.currentMana < MaxMana())
            {
                character.currentMana = Mathf.Min(MaxMana(), character.currentMana + ManaRegen());
                SendMana();
            }
        }

        public void Heal(int amount)
        {
            if (character == null) return;
            character.currentHealth = Mathf.Min(MaxHealth(), character.currentHealth + amount);
            SendHealth();
        }
    }
}
