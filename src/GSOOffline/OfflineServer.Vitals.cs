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

        public void Heal(int amount)
        {
            if (character == null) return;
            character.currentHealth = Mathf.Min(MaxHealth(), character.currentHealth + amount);
            SendHealth();
        }
    }
}
