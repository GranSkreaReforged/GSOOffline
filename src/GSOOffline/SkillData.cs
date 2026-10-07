using System.Collections.Generic;
using System.Xml;
using UnityEngine;

namespace GSOOffline
{
    public class HarvestDrop
    {
        public int item, min, max, dropRate, dropRateBoost, amountBoost;
    }

    public class HarvestableInfo
    {
        public int typeId;
        public string name;
        public int level;
        public string skill;   // Woodcutting, Mining, Fishing, Farming, Alchemy, none
        public string tool;    // hatchet, pickaxe, fishingrod, scythe, sickle, shovel, none
        public int healthMin = 1, healthMax = 1;
        public float harvestTime = 1.5f;
        public float respawnTime = 60f;
        public readonly List<HarvestDrop> drops = new List<HarvestDrop>();
    }

    public class CraftRecipe
    {
        public int id;              // same numbering as Script_Crafting.craftingIdCounter
        public int product;
        public int amount;
        public readonly List<int[]> materials = new List<int[]>();   // typeId, amount
        public readonly List<int> workbenches = new List<int>();
        public int skill;
        public int level;
        public float xpMultiplier;
        public float time;          // seconds
    }

    /// <summary>Harvestable nodes (XMLs/HarvestableInfo) and crafting recipes (items "Crafting" stats).</summary>
    internal static class SkillData
    {
        public static readonly Dictionary<int, HarvestableInfo> Harvestables = new Dictionary<int, HarvestableInfo>();
        public static readonly Dictionary<int, CraftRecipe> Recipes = new Dictionary<int, CraftRecipe>();

        // Neither the harvesting nor the crafting XP formula survived; this level-scaled base keeps early
        // levels at a handful of actions each (level 5 needs 414 XP) and is the single place to rebalance.
        public static int BaseXp(int level) => 10 + 3 * Mathf.Max(1, level);

        private static int[] Range(string s)
        {
            if (string.IsNullOrEmpty(s)) return new[] { 1, 1 };
            var p = s.Split('-');
            int a = int.Parse(p[0].Trim());
            return new[] { a, p.Length > 1 ? int.Parse(p[1].Trim()) : a };
        }

        public static void Load()
        {
            foreach (XmlNode x in GameData.LoadXml("XMLs/HarvestableInfo").DocumentElement.ChildNodes)
            {
                if (x.NodeType != XmlNodeType.Element) continue;
                var hp = Range(GameData.Attr(x, "health"));
                var h = new HarvestableInfo
                {
                    typeId = GameData.IntAttr(x, "typeid"),
                    name = GameData.Attr(x, "name"),
                    level = GameData.IntAttr(x, "level", 1),
                    skill = GameData.Attr(x, "skill") ?? "none",
                    tool = GameData.Attr(x, "tool") ?? "none",
                    healthMin = hp[0],
                    healthMax = hp[1],
                    harvestTime = GameData.IntAttr(x, "harvesttime", 1500) / 1000f,
                    respawnTime = GameData.IntAttr(x, "respawntime", 60),
                };
                foreach (XmlNode d in x.ChildNodes)
                {
                    if (d.NodeType != XmlNodeType.Element) continue;
                    h.drops.Add(new HarvestDrop
                    {
                        item = GameData.IntAttr(d, "id"),
                        min = GameData.IntAttr(d, "minamount", 1),
                        max = GameData.IntAttr(d, "maxamount", 1),
                        dropRate = GameData.IntAttr(d, "droprate"),
                        dropRateBoost = GameData.IntAttr(d, "droprateboost"),
                        amountBoost = GameData.IntAttr(d, "amountboost"),
                    });
                }
                Harvestables[h.typeId] = h;
            }

            // Recipes: walk items in file order exactly like Scr_ItemHandler.LoadItems so ids line up.
            int counter = 0;
            foreach (XmlNode item in GameData.LoadXml("Data/items").DocumentElement.ChildNodes)
            {
                if (item.NodeType != XmlNodeType.Element) continue;
                int typeId = GameData.IntAttr(item, "TypeId");
                int itemLevel = GameData.IntAttr(item, "Level", 0);   // client leaves itemLevel 0 when absent
                foreach (XmlNode s in item.ChildNodes)
                {
                    if (s.NodeType != XmlNodeType.Element) continue;
                    string spec = GameData.Attr(s, "Crafting");
                    if (spec == null) continue;
                    counter++;
                    var r = ParseRecipe(spec, counter, typeId, itemLevel, GameData.IntAttr(s, "CraftingAmount", 1));
                    if (r != null) Recipes[r.id] = r;
                }
            }
        }

        // "m1_a1_m2_a2_m3_a3_m4_a4_m5_a5_workbenches_skill_level_xp%_timeMs"
        private static CraftRecipe ParseRecipe(string spec, int id, int product, int itemLevel, int amount)
        {
            var p = spec.Split('_');
            if (p.Length < 15) return null;
            var r = new CraftRecipe { id = id, product = product, amount = amount };
            for (int i = 0; i < 5; i++)
            {
                int m = int.Parse(p[i * 2]), a = int.Parse(p[i * 2 + 1]);
                if (m > 0 && a > 0) r.materials.Add(new[] { m, a });
            }
            foreach (string wb in p[10].Split(',')) r.workbenches.Add(int.Parse(wb));
            r.skill = int.Parse(p[11]);
            r.level = int.Parse(p[12]);
            if (r.level == 0) r.level = itemLevel;
            r.xpMultiplier = int.Parse(p[13]) / 100f;
            r.time = int.Parse(p[14]) / 1000f;
            return r;
        }
    }
}
