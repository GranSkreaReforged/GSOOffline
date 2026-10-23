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
    /// Every bag (a kill's loot, items the player dropped, gear lost on death) is saved with the character and lasts
    /// 20 minutes of playing time (#79); the original server cleared bags after 30 minutes.
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
            public float secondsLeft = LootBagLifetime;   // playing time until it's gone
            public List<ItemSave> kept;   // the very items, given back as they are (death bags, dropped items, bounty gear)
            public bool death;            // gear lost on death
            public bool dropped;          // items the player dropped
        }

        private const float LootBagLifetime = 1200f;  // playing time a bag stays on the ground
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
            if (BountyKilled(npc)) return;   // an outlaw: its bounty and gear instead
            int level = info?.level ?? 1;
            var bag = RollLoot(info);
            SeasonalDrops(bag, info);
            if (bag.items.Count == 0 && bag.silver <= 0)
            {
                Plugin.Log.LogInfo($"[loot] {info?.name} (level {level}) dropped nothing");
                return;
            }
            bag.id = nextLootId++;
            bag.scene = character.scene;
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
            if (info != null && LootData.KnownToOriginal(info.id))
            {
                // The original server's tables, rolled as it rolled them: each drop on its own, 1 in its rate.
                var original = LootData.OriginalDrops(info.id);
                foreach (var d in original)
                {
                    if (!OneIn(d.rate)) continue;
                    if (d.typeId == 0) bag.silver = Random.Range(d.min, d.max + 1);
                    else AddToBag(bag, d.typeId, Random.Range(d.min, d.max + 1));
                }
                // Uniques and pets the patch notes added (updates 137-140) that the tables lack.
                if (table != null)
                    foreach (var d in table.drops)
                        if (LaterPatch(d) && !original.Exists(o => o.typeId == d.typeId) && Random.value < d.chance)
                            AddToBag(bag, d.typeId, Random.Range(d.min, d.max + 1));
            }
            else if (table != null)
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
            LevelDrops(bag, level);
            return bag;
        }

        // Unity's Random.Range(0, n) is 0 for any n <= 1, as on the original server.
        private static bool OneIn(int n) => Random.Range(0, n) == 0;

        // Patch-note drops newer than this go on top of the original's tables when the tables lack them. The tables
        // now come from the original's develop branch, which has most of them; a few never reached Loot.xml (Argus's
        // and George's pets, Serpent's Fang, Roke's ring on his other copies).
        private const int OriginalLootUpdate = 136;

        private static bool LaterPatch(LootDrop d)
        {
            if (d.from == null || !d.from.StartsWith("patch notes")) return false;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(d.from, @"\d+"))
                if (int.Parse(m.Value) > OriginalLootUpdate) return true;
            return false;
        }

        /// <summary>
        /// What every creature could drop by level on the original server (Scr_NpcHandler.dropLoot): an upgrade stone,
        /// 1 in 15 of them a grade stone instead, and a gear crate of the level's metal, 1 in 15 of them the next one.
        /// </summary>
        private static void LevelDrops(LootBag bag, int level)
        {
            if (level >= 1 && level < 25 && OneIn(50 - level)) AddToBag(bag, OneIn(15) ? 440 : 437, 1);
            else if (level >= 25 && level < 50 && OneIn(75 - level)) AddToBag(bag, OneIn(15) ? 441 : 438, 1);
            else if (level >= 50 && OneIn(100 - level)) AddToBag(bag, OneIn(15) ? 442 : 439, 1);

            if (level >= 1 && level < 10 && OneIn(2 * (50 - level))) AddToBag(bag, OneIn(15) ? IronCrate + 1 : IronCrate, 1);
            else if (level >= 10 && level < 20 && OneIn(2 * (60 - level))) AddToBag(bag, OneIn(15) ? IronCrate + 2 : IronCrate + 1, 1);
            else if (level >= 20 && level < 30 && OneIn(2 * (70 - level))) AddToBag(bag, OneIn(15) ? IronCrate + 3 : IronCrate + 2, 1);
            else if (level >= 30 && level < 40 && OneIn(3 * (80 - level))) AddToBag(bag, OneIn(15) ? IronCrate + 4 : IronCrate + 3, 1);
            else if (level >= 40 && level < 50 && OneIn(3 * (90 - level))) AddToBag(bag, OneIn(15) ? IronCrate + 5 : IronCrate + 4, 1);
            else if (level >= 50 && OneIn(3 * (100 - level))) AddToBag(bag, IronCrate + 5, 1);
        }

        // Gear crates: iron, steel, verum, stranite, shadow, iridium (10668-10673).
        private const int IronCrate = 10668;

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
            if (bag.kept != null)
            {
                CollectKeptBag(bag);
                return;
            }
            foreach (var it in bag.items) GiveItem(it.type, it.amount);
            if (bag.silver > 0) SetSilver(character.silver + bag.silver);
            Notice("You loot " + LootLabel(bag) + ".");
            RemoveLoot(bag);
        }

        /// <summary>Bags wear away only while playing, in every scene.</summary>
        private void TickLoot()
        {
            if (character == null || !sceneReady) return;
            foreach (var list in lootByScene.Values)
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    list[i].secondsLeft -= Time.deltaTime;
                    if (list[i].secondsLeft <= 0f)
                    {
                        Plugin.Log.LogInfo($"[loot] bag {list[i].id} in scene {list[i].scene} wore away: {LootLabel(list[i])}");
                        RemoveLoot(list[i]);
                    }
                }
        }

        // ---- saving --------------------------------------------------------------------------

        /// <summary>Before a save: the bags on the ground, as they are now.</summary>
        private void SnapshotLootBags()
        {
            character.lootBags.Clear();
            foreach (var list in lootByScene.Values)
                foreach (var bag in list)
                    character.lootBags.Add(new LootBagSave
                    {
                        scene = bag.scene, x = bag.pos.x, y = bag.pos.y, z = bag.pos.z, secondsLeft = bag.secondsLeft,
                        death = bag.death, dropped = bag.dropped, silver = bag.silver,
                        loot = bag.kept == null ? new List<ItemAmount>(bag.items) : new List<ItemAmount>(), kept = bag.kept,
                    });
        }

        /// <summary>Entering the world: this character's bags back on the ground, nobody else's.</summary>
        private void LoadLootBags()
        {
            lootByScene.Clear();
            foreach (var old in character.deathBags)   // saves from before 1.8
            {
                foreach (var it in old.items)
                    if (it.amount <= 0) it.amount = 1;   // 1.7 bagged gear after emptying its stack
                character.lootBags.Add(new LootBagSave { scene = old.scene, x = old.x, y = old.y, z = old.z, secondsLeft = LootBagLifetime, death = true, kept = old.items });
            }
            character.deathBags.Clear();
            foreach (var s in character.lootBags)
            {
                var bag = new LootBag
                {
                    id = nextLootId++, scene = s.scene, pos = new Vector3(s.x, s.y, s.z), secondsLeft = s.secondsLeft,
                    death = s.death, dropped = s.dropped, silver = s.silver, kept = s.kept,
                };
                if (s.kept != null)
                    foreach (var it in s.kept) bag.items.Add(new ItemAmount { type = it.typeId, amount = it.amount });
                else if (s.loot != null)
                    bag.items.AddRange(s.loot);
                SceneLoot(bag.scene).Add(bag);
            }
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
                Instance?.SeasonalDrops(bag, info);
                if (bag.items.Count == 0 && bag.silver <= 0) empty++;
                silver += bag.silver;
                foreach (var it in bag.items)
                    totals[it.type] = (totals.TryGetValue(it.type, out int t) ? t : 0) + it.amount;
            }
            Plugin.Log.LogInfo($"[dev] {n} kills of {info?.name} (type {npcType}, level {info?.level}, wiki table: {LootData.For(info?.name) != null}): {empty} empty, {silver} silver");
            foreach (var kv in totals)
                Plugin.Log.LogInfo($"[dev]   {ItemData.Get(kv.Key)?.name} x{kv.Value}");
        }

        internal void DevAgeLoot(float seconds)
        {
            foreach (var list in lootByScene.Values)
                foreach (var bag in list) bag.secondsLeft -= seconds;
        }

        /// <summary>Dev: the bags the server holds in the current scene.</summary>
        internal void LogLoot()
        {
            if (character == null) return;
            foreach (var bag in SceneLoot(character.scene))
                Plugin.Log.LogInfo($"[dev] loot bag {bag.id} at {bag.pos} ({Mathf.RoundToInt(bag.secondsLeft)}s left{(bag.dropped ? ", dropped" : bag.death ? ", death" : "")}): {LootLabel(bag)}");
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
