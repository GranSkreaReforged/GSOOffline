using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    public partial class OfflineServer
    {
        // Base "Health" stat (100 per combat level, as the original: BaseHealth) plus Health rolled on equipped items,
        // as the client's stats window computes it.
        private int MaxHealth()
        {
            int max = character.health;
            foreach (var it in character.items)
            {
                if (!it.equipped) continue;
                foreach (var s in it.stats)
                    if (s.key == "Health") max += Graded(it, s.value);
            }
            return Mathf.Max(1, max + BuffStat("Health") + BonusHealth());
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

        // The original's mana: the base 100 and the gear's, less what a Fire wisp burns.
        private int MaxMana() => Mathf.Max(0, character.mana + GearStat("Mana") - BuffAmount(AbilityFireWisp) - BuffAmount(AbilityFireWisp2) - SummonManaHeld());

        private const int SkillElementalGrim = 20;

        // Mana regained each second (Scr_PlayerHandler.HealthManaRegen): 1, plus Manaregen on your gear and Mana surge's.
        private int ManaRegen() => Mathf.Max(0, BaseManaRegen + GearStat("Manaregen") + IconAmount(BuffManaSurge));
        private const int BaseManaRegen = 1;

        /// <summary>
        /// The base health follows the combat level (update 136): set at login and on every level up, and sent as the
        /// client's base Health stat (16/5 type 1), as the original's levelUp did.
        /// </summary>
        private void UpdateBaseHealth()
        {
            if (character == null || devBaseHealth > 0) return;
            int baseHealth = CombatRules.BaseHealth(CombatLevel());
            if (baseHealth == character.health) return;
            character.health = baseHealth;
            Send(16, 5, StatHealth, character.health);
            HealthStatsChanged();
        }

        private const int StatHealth = 1;
        private int devBaseHealth;   // dev: devhealth pins the base health

        private int GearStat(string key)
        {
            int sum = 0;
            foreach (var it in character.items)
                if (it.equipped)
                    foreach (var s in it.stats)
                        if (s.key == key) sum += Graded(it, s.value);
            return sum + BuffStat(key);
        }

        // What buffs add to a gear stat (BuffEffect.stats), counted as if worn.
        private int BuffStat(string key)
        {
            int sum = 0;
            foreach (var b in buffs)
                if (b.effect.stats != null && b.effect.stats.TryGetValue(key, out int v)) sum += v;
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

        // Health regained each second: 1, plus Healthregen rolled on equipped items.
        private int HealthRegen() => Mathf.Max(0, 1 + GearStat("Healthregen") + IconAmount(BuffLifeSurge));

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
            foreach (var kv in GearBonusStats) Send(16, 5, kv.Value, kv.Key == "Damage" ? StatDamage() : GearStat(kv.Key));
            SendEffectiveResistances();
            SendCombatLevel();
        }

        /// <summary>Every second, in combat or not, as the original: HealthRegen() (and food's) and ManaRegen().</summary>
        private void TickRegen()
        {
            if (character == null || !sceneReady || PlayerDead) return;
            regenTimer -= Time.deltaTime;
            if (regenTimer > 0f) return;
            regenTimer = 1f;
            // Food: 1-1.5% of your health on top while it lasts.
            int food = HasBuff(BuffFood) ? (int)(MaxHealth() * Random.Range(0.01f, 0.015f)) : 0;
            if (character.currentHealth < MaxHealth()) Heal(HealthRegen() + food, regen: true);
            if (character.currentMana < MaxMana())
            {
                character.currentMana = Mathf.Min(MaxMana(), character.currentMana + ManaRegen());
                SendMana();
            }
        }

        /// <summary>Heals the player. Heals (not <paramref name="regen"/>eration) are lowered by Nature's blessing and turned by the plague.</summary>
        public void Heal(int amount, bool regen = false)
        {
            if (character == null) return;
            if (!regen) amount = (int)(amount * NaturesBlessingHealing() * BuffProduct(e => e.healingTaken));
            // Sepitus's plague, in his dungeon: a real heal (over 2% of your health) burns you for 2-3 times as much.
            if (!regen && character.scene == ScenePoacher && amount > MaxHealth() * 0.02f && buffs.Exists(b => b.effect.plague))
            {
                PlayPlayerEffect(7);
                float res = CombatRules.PlayerResistance(GearStat(DamageKinds.ResistanceStat(DamageKinds.Fire)));
                HurtPlayer(Mathf.RoundToInt(amount * Random.Range(2f, 3f) * (1f - res)), SplashPlain);
                return;
            }
            character.currentHealth = Mathf.Min(MaxHealth(), character.currentHealth + amount);
            SendHealth();
        }
    }
}
