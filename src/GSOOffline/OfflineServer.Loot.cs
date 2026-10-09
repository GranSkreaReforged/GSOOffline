using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Loot bags. A kill rolls the NPC's drop table (LootData) into one bag on the ground; clicking it (3/17)
    /// hands over everything in it. Bags stay for a while per scene and are re-sent when the scene loads,
    /// because the client only ever adds bags (198/34) or removes them (198/35) and loses them with the scene.
    /// </summary>
    public partial class OfflineServer
    {
        private class LootBag
        {
            public int id;
            public int scene;
            public Vector3 pos;
            public int silver;
            public readonly List<ItemAmount> items = new List<ItemAmount>();
            public float despawnAt;
        }

        // Reconstructions: the old server's numbers are lost.
        private const float LootBagLifetime = 300f;   // seconds a bag stays on the ground
        private const float LootPickupRange = 12f;    // loose: the client already gates the click on distance
        // The client draws a bag's item model from its type id. A plain stackable keeps the default sack,
        // for bags that hold only silver (the game has no silver item).
        private const int PlainSackTypeId = 10017;

        private readonly Dictionary<int, List<LootBag>> lootByScene = new Dictionary<int, List<LootBag>>();
        private int nextLootId = 1;

        private void RegisterLootHandlers()
        {
            On(3, 17, c => CollectLoot((int)c[2]));
        }

        private List<LootBag> SceneLoot(int scene)
        {
            if (!lootByScene.TryGetValue(scene, out var list)) lootByScene[scene] = list = new List<LootBag>();
            return list;
        }

        // ---- rolling -------------------------------------------------------------------------

        /// <summary>Rolls the kill's loot into a bag at the NPC (or at the player, for NPCs flagged droplootatplayer).</summary>
        private void DropLoot(NpcEntity npc, NpcInfo info)
        {
            int level = info?.level ?? 1;
            var bag = RollLoot(info);
            if (bag.items.Count == 0 && bag.silver <= 0)
            {
                Plugin.Log.LogInfo($"[loot] {info?.name} (level {level}) dropped nothing");
                return;
            }
            bag.id = nextLootId++;
            bag.scene = character.scene;
            bag.despawnAt = Time.time + LootBagLifetime;
            bag.pos = LootPosition(npc, info);
            SceneLoot(bag.scene).Add(bag);
            Plugin.Log.LogInfo($"[loot] {info?.name} (level {level}) -> bag {bag.id}: {LootLabel(bag)}");
            Send(198, 34, LootEntry(bag));
        }

        private static LootBag RollLoot(NpcInfo info)
        {
            int level = info?.level ?? 1;
            var bag = new LootBag();
            var table = LootData.For(info?.name);
            if (table != null)
            {
                foreach (var d in table.drops)
                {
                    if (d.levels.Count > 0 && !d.levels.Contains(level)) continue;
                    if (Random.value >= d.chance) continue;
                    AddToBag(bag, d.typeId, Random.Range(d.min, d.max + 1));
                }
                if (table.dropsSilver)
                    bag.silver = table.silver.Count == 2 ? WikiSilver(table.silver[0], table.silver[1]) : CombatRules.SilverDrop(level);
            }
            else
            {
                bag.silver = CombatRules.SilverDrop(level);   // no wiki entry: the old level formula
            }
            foreach (var g in LootData.File.global)
                if (level >= g.minLevel && level <= g.maxLevel && Random.value < g.chance) AddToBag(bag, g.typeId, 1);
            return bag;
        }

        // The wiki gives one observed amount (e.g. "Crab lvl 2: 7"); vary it a little around that.
        private static int WikiSilver(int min, int max)
        {
            if (max > min) return Random.Range(min, max + 1);
            return Mathf.Max(0, Random.Range(Mathf.RoundToInt(min * 0.75f), Mathf.RoundToInt(min * 1.25f) + 1));
        }

        private static void AddToBag(LootBag bag, int typeId, int amount)
        {
            if (amount <= 0 || ItemData.Get(typeId) == null) return;
            foreach (var it in bag.items)
                if (it.type == typeId) { it.amount += amount; return; }
            bag.items.Add(new ItemAmount { type = typeId, amount = amount });
        }

        private Vector3 LootPosition(NpcEntity npc, NpcInfo info)
        {
            var player = LocalPlayer;
            if (info != null && info.dropLootAtPlayer && player != null) return player.transform.position;
            // The client's NPC sits where it actually died; the server only knows its last waypoint.
            var shown = Scr_NpcHandler.instance != null ? Scr_NpcHandler.instance.getNpc(npc.uid) : null;
            return shown != null ? shown.transform.position : npc.pos;
        }

        // ---- client messages -----------------------------------------------------------------

        /// <summary>"Wool x3, Sheep bones, 12 silver": shown when hovering the bag.</summary>
        private static string LootLabel(LootBag bag)
        {
            var parts = new List<string>();
            foreach (var it in bag.items)
            {
                string name = ItemData.Get(it.type)?.name ?? ("item " + it.type);
                parts.Add(it.amount > 1 ? name + " x" + it.amount : name);
            }
            if (bag.silver > 0) parts.Add(bag.silver + " silver");
            return string.Join(", ", parts.ToArray());
        }

        // The item whose model the client puts on the ground: equipment first (weapons, shields, helmets and
        // backs have models), otherwise the first item, otherwise a plain sack.
        private static int DisplayType(LootBag bag)
        {
            foreach (var it in bag.items)
                if (it.type < 10000) return it.type;
            return bag.items.Count > 0 ? bag.items[0].type : PlainSackTypeId;
        }

        /// <summary>One entry of the client's visible-loot list: id_x,y,z_name_typeId ('_' and '>' are separators).</summary>
        private static string LootEntry(LootBag bag)
        {
            var inv = CultureInfo.InvariantCulture;
            string label = LootLabel(bag).Replace('_', ' ').Replace('>', ' ');
            return string.Format(inv, "{0}_{1},{2},{3}_{4}_{5}", bag.id, bag.pos.x, bag.pos.y, bag.pos.z, label, DisplayType(bag));
        }

        /// <summary>After a scene load: the client lost its bags with the old scene, so send this scene's again.</summary>
        private void SendSceneLoot()
        {
            if (character == null) return;
            var list = SceneLoot(character.scene);
            if (list.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var bag in list)
            {
                if (sb.Length > 0) sb.Append('>');
                sb.Append(LootEntry(bag));
            }
            Send(198, 34, sb.ToString());
        }

        private void RemoveLoot(LootBag bag)
        {
            SceneLoot(bag.scene).Remove(bag);
            if (character != null && bag.scene == character.scene) Send(198, 35, bag.id);
        }

        private void CollectLoot(int id)
        {
            if (character == null) return;
            var bag = SceneLoot(character.scene).Find(b => b.id == id);
            if (bag == null)
            {
                Plugin.Log.LogInfo($"[loot] bag {id} not found in scene {character.scene}");
                Send(198, 35, id);   // the client shows a bag we don't have: drop it there too
                return;
            }
            var player = LocalPlayer;
            float dist = player != null ? Vector3.Distance(player.transform.position, bag.pos) : 0f;
            if (dist > LootPickupRange)
            {
                Plugin.Log.LogInfo($"[loot] bag {id} too far ({dist:F1}m)");
                Notice("That's too far away.", "red");
                return;
            }
            foreach (var it in bag.items) GiveItem(it.type, it.amount);
            if (bag.silver > 0) SetSilver(character.silver + bag.silver);
            Notice("You loot " + LootLabel(bag) + ".");
            RemoveLoot(bag);
        }

        private void TickLoot()
        {
            foreach (var list in lootByScene.Values)
                for (int i = list.Count - 1; i >= 0; i--)
                    if (Time.time >= list[i].despawnAt) RemoveLoot(list[i]);
        }

        /// <summary>Dev: rolls an NPC type's loot n times without dropping anything, and logs the totals.</summary>
        internal static void LogLootRolls(int npcType, int n)
        {
            GameData.Npcs.TryGetValue(npcType, out var info);
            var totals = new Dictionary<int, int>();
            int silver = 0, empty = 0;
            for (int i = 0; i < n; i++)
            {
                var bag = RollLoot(info);
                if (bag.items.Count == 0 && bag.silver <= 0) empty++;
                silver += bag.silver;
                foreach (var it in bag.items)
                    totals[it.type] = (totals.TryGetValue(it.type, out int t) ? t : 0) + it.amount;
            }
            Plugin.Log.LogInfo($"[dev] {n} kills of {info?.name} (type {npcType}, level {info?.level}, wiki table: {LootData.For(info?.name) != null}): {empty} empty, {silver} silver");
            foreach (var kv in totals)
                Plugin.Log.LogInfo($"[dev]   {ItemData.Get(kv.Key)?.name} x{kv.Value}");
        }

        /// <summary>Dev: the bags the server holds in the current scene.</summary>
        internal void LogLoot()
        {
            if (character == null) return;
            foreach (var bag in SceneLoot(character.scene))
                Plugin.Log.LogInfo($"[dev] loot bag {bag.id} at {bag.pos} ({Mathf.RoundToInt(bag.despawnAt - Time.time)}s left): {LootLabel(bag)}");
            int live = 0, gone = 0;
            if (Scr_LootHandler.instance != null)
                foreach (var lb in Scr_LootHandler.instance.lootList)
                {
                    if (lb != null) { live++; Plugin.Log.LogInfo($"[dev] client bag {lb.id} at {lb.transform.position} '{lb.lootName}'"); }
                    else gone++;   // destroyed with an earlier scene; the client never prunes its list
                }
            Plugin.Log.LogInfo($"[dev] loot: {SceneLoot(character.scene).Count} bags in scene {character.scene}, client shows {live} (+{gone} destroyed entries)");
        }
    }
}
