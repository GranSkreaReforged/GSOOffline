using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// What dying costs offline (issue #69). The original dropped your gear where you fell, for anyone to take; alone,
    /// that's harsher than it was. Instead each death adds "Death's toll": less experience from everything for a
    /// while, worse with every death before it wears off, and from the 4th a chance that equipped gear falls into a
    /// bag where you died (it stays there, through logouts, until you fetch it). Time spent playing wears it off,
    /// or John Brech in Yorkhill lifts it for silver, more the stronger you are. The numbers are in CombatRules.
    /// </summary>
    public partial class OfflineServer
    {
        private const int DeathBuffIcon = 120;     // an id the client's Buffs.xml doesn't use: registered at runtime
        private const int DeathBuffIconLook = 76;  // drawn with the Weakness icon
        private const int CleanserNpc = 127;       // John Brech, a Yorkhill citizen with no dialogue of his own
        private const float CleanseConfirmSeconds = 30f;

        private float cleanseOfferedUntil = -1f;
        private bool deathBuffRegistered;

        private bool DeathTollActive => character != null && character.deathStacks > 0 && character.deathTollSeconds > 0f;

        /// <summary>The share of experience kept while the toll is on (1 when it isn't).</summary>
        private float DeathTollXpMultiplier() =>
            DeathTollActive ? 1f - CombatRules.DeathTollXpLoss * character.deathStacks : 1f;

        /// <summary>From PlayerDies: the toll grows (or starts), and from the 4th death gear may fall.</summary>
        private void ApplyDeathToll(Vector3 where)
        {
            int stacks = DeathTollActive ? Mathf.Min(character.deathStacks + 1, CombatRules.DeathTollMaxStacks) : 1;
            character.deathStacks = stacks;
            character.deathTollSeconds = CombatRules.DeathTollMinutesPerStack * 60f * stacks;
            Plugin.Log.LogInfo($"[death] toll {stacks}: -{stacks * CombatRules.DeathTollXpLoss * 100:F0}% XP for {character.deathTollSeconds / 60f:F0} min");
            if (stacks >= CombatRules.DeathTollDropFromStack && Random.value < CombatRules.DeathDropChance) DropGearOnDeath(where);
        }

        /// <summary>After the respawn: the client lost its buff icons with the death.</summary>
        private void SendDeathToll()
        {
            if (!DeathTollActive || PlayerDead) return;
            RegisterDeathBuff();
            int tenths = Mathf.RoundToInt(character.deathTollSeconds * 10f);
            Send(4, 9, character.name, DeathBuffIcon, tenths, tenths);
        }

        /// <summary>The toll's icon and tooltip, added to the client's buff list (its text says how bad it is now).</summary>
        private void RegisterDeathBuff()
        {
            var bc = BuffController.instance;
            if (bc == null || character == null) return;
            var info = bc.GetBuffInfo(DeathBuffIcon);
            if (info == null)
            {
                info = new BuffController.BuffInfo { id = DeathBuffIcon, buffName = "Death's toll" };
                bc.infoBuffs.Add(info);
            }
            info.buffDescription = $"You gain {character.deathStacks * CombatRules.DeathTollXpLoss * 100:F0}% less experience. "
                + (character.deathStacks >= CombatRules.DeathTollDropFromStack - 1 ? "Dying again may cost you equipped gear. " : "")
                + $"John Brech in Yorkhill can lift it for {CleanseFee()} silver.";
            if (deathBuffRegistered) return;
            var loader = Scr_TextureLoader.instance;
            if (loader != null)
            {
                Texture2D look = loader.getBuffIcon(DeathBuffIconLook);
                if (look != null && !loader.loadedIcons.buffIcons.Exists(i => i.id == DeathBuffIcon))
                    loader.loadedIcons.buffIcons.Add(new Scr_TextureLoader.itemIcon { id = DeathBuffIcon, icon = look });
            }
            deathBuffRegistered = true;
        }

        /// <summary>Counts down while playing (not while logged out); at 0 the toll is gone.</summary>
        private void TickDeathToll()
        {
            if (character == null || !sceneReady || character.deathStacks <= 0) return;
            character.deathTollSeconds -= Time.deltaTime;
            if (character.deathTollSeconds > 0f) return;
            EndDeathToll();
            Notice("The toll of your deaths has worn off.", "green");
        }

        private void EndDeathToll()
        {
            character.deathStacks = 0;
            character.deathTollSeconds = 0f;
            Send(3, 35, character.name, DeathBuffIcon);
            Plugin.Log.LogInfo("[death] toll ended");
        }

        private int CleanseFee()
        {
            int stacks = Mathf.Clamp(character.deathStacks, 1, CombatRules.DeathTollMaxStacks);
            return Mathf.RoundToInt(CombatRules.DeathCleanseFee[stacks - 1] * (1f + CombatLevel() / 10f));
        }

        /// <summary>
        /// Talking to John Brech: the first time he names his price, talking to him again within 30 seconds pays it.
        /// </summary>
        private bool CleanserInteract(NpcEntity npc)
        {
            if (npc.typeId != CleanserNpc) return false;
            string me = GameData.Npcs.TryGetValue(CleanserNpc, out var info) ? info.name : "John Brech";   // NpcName is lower case
            if (!DeathTollActive)
            {
                Notice($"{me}: You look well enough to me.", "white");
                return true;
            }
            int fee = CleanseFee();
            if (Time.time > cleanseOfferedUntil)
            {
                cleanseOfferedUntil = Time.time + CleanseConfirmSeconds;
                Notice($"{me}: Death clings to you. For {fee} silver I can lift it. Speak to me again if you want it gone.", "white");
                return true;
            }
            cleanseOfferedUntil = -1f;
            if (character.silver < fee)
            {
                Notice($"{me}: That's not enough silver. It'll be {fee}.", "white");
                return true;
            }
            SetSilver(character.silver - fee);
            EndDeathToll();
            PlayPlayerEffect(GfxLevelUp);
            Notice($"{me}: There. The weight of death lifts from you. (-{fee} silver)", "green");
            Plugin.Log.LogInfo($"[death] cleansed for {fee} silver");
            return true;
        }

        // ---- gear dropped on death -----------------------------------------------------------

        /// <summary>
        /// Some equipped pieces fall into a bag at the spot: 1 most often, 2 sometimes, more rarely, all of it very
        /// rarely. The pieces keep their grade and stats.
        /// </summary>
        private void DropGearOnDeath(Vector3 where)
        {
            var worn = character.items.FindAll(i => i.equipped);
            if (worn.Count == 0) return;
            float r = Random.value;
            int n = r < CombatRules.DeathDropOne ? 1 : r < CombatRules.DeathDropOne + CombatRules.DeathDropTwo ? 2 : Random.Range(3, worn.Count + 1);
            n = Mathf.Min(n, worn.Count);
            var bag = new DeathBagSave { scene = character.scene, x = where.x, y = where.y, z = where.z };
            for (int i = 0; i < n; i++)
            {
                var it = worn[Random.Range(0, worn.Count)];
                worn.Remove(it);
                RemoveFromStack(it, it.amount);   // unequips and tells the client
                it.equipped = false;
                bag.items.Add(it);
            }
            GearChanged();
            character.deathBags.Add(bag);
            var loot = AddDeathBagLoot(bag);
            Send(198, 34, LootEntry(loot));
            Notice($"In death you dropped {LootLabel(loot)}. It lies where you fell.", "red");
            Plugin.Log.LogInfo($"[death] dropped {LootLabel(loot)} in scene {bag.scene} at {SceneWorld.Vec(where)}");
            SaveCurrentCharacter();
        }

        private LootBag AddDeathBagLoot(DeathBagSave save)
        {
            var bag = new LootBag { id = nextLootId++, scene = save.scene, pos = new Vector3(save.x, save.y, save.z), despawnAt = float.MaxValue, death = save };
            foreach (var it in save.items) bag.items.Add(new ItemAmount { type = it.typeId, amount = it.amount });
            SceneLoot(save.scene).Add(bag);
            return bag;
        }

        /// <summary>Login: this character's death bags back on the ground, nobody else's.</summary>
        private void LoadDeathBags()
        {
            foreach (var list in lootByScene.Values) list.RemoveAll(b => b.death != null);
            foreach (var save in character.deathBags) AddDeathBagLoot(save);
        }

        /// <summary>Picking up a death bag: the very same items back in the bag.</summary>
        private void CollectDeathBag(LootBag bag)
        {
            foreach (var it in bag.death.items)
            {
                it.equipped = false;
                if (ItemData.Get(it.typeId)?.Stacks == true)
                {
                    GiveItem(it.typeId, it.amount);
                    continue;
                }
                character.items.Add(it);
                SendItemAdded(it, it.amount);
            }
            character.deathBags.Remove(bag.death);
            CheckItemTriggers();
            Notice("You recover " + LootLabel(bag) + ".");
            RemoveLoot(bag);
            SaveCurrentCharacter();
        }
    }
}
