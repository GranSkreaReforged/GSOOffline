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

        private int MaxMana() => Mathf.Max(0, character.mana + GearStat("Mana"));

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

        /// <summary>Out of combat: 1 health per 3 s; mana always ManaRegen() per 3 s.</summary>
        private void TickRegen()
        {
            if (character == null || !sceneReady || PlayerDead) return;
            regenTimer -= Time.deltaTime;
            if (regenTimer > 0f) return;
            regenTimer = 3f;
            bool inCombat = autoAttacking || (world != null && world.npcs.Exists(n => n.aggro));
            if (!inCombat && character.currentHealth < MaxHealth()) Heal(1);
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
