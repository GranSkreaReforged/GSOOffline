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
            return Mathf.Max(1, max);
        }

        private void SendHealth()
        {
            Send(8, 13, character.name, character.currentHealth, MaxHealth());
        }

        private int MaxMana()
        {
            int max = character.mana;
            foreach (var it in character.items)
                if (it.equipped)
                    foreach (var s in it.stats)
                        if (s.key == "Mana") max += s.value;
            return Mathf.Max(0, max);
        }

        private void SendMana() => Send(8, 15, character.name, character.currentMana, MaxMana());

        private float regenTimer;

        /// <summary>Out of combat: 1 health per 3 s; mana always 2 per 3 s.</summary>
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
                character.currentMana = Mathf.Min(MaxMana(), character.currentMana + 2);
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
