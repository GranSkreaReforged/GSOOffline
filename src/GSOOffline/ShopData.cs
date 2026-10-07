using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GSOOffline
{
    public class ShopStock
    {
        public string name;
        public int merchantSilver = 5000;
        public List<int[]> items = new List<int[]>();   // typeId, price
    }

    /// <summary>
    /// NPC shop inventories. The real stock lists lived on the server and are lost, so each shop id
    /// (dialogue openshop=) gets thematically matching items from the item database, chosen by
    /// item flags / slot / name, at their listed price. Owners were identified by walking each NPC's
    /// dialogue tree to its openshop node.
    /// </summary>
    internal static class ShopData
    {
        private const int MaxItems = 40;
        private static readonly Dictionary<int, ShopStock> cache = new Dictionary<int, ShopStock>();

        private static bool Has(ItemTemplate t, params string[] flags) => flags.Any(f => t.flags.Contains(f));
        private static bool Named(ItemTemplate t, params string[] words) =>
            t.name != null && words.Any(w => t.name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

        private static readonly string[] WeaponFlags =
            { "isBroadsword", "isGreatsword", "isHalberd", "isDagger", "isMace", "isBow", "isCrossBow", "isStaff", "isFireStaff", "isIceStaff", "isWand" };
        private static readonly string[] ToolFlags =
            { "isHatchet", "isPickaxe", "isFishingRod", "isFishingNet", "isSickle", "isShears", "isScythe", "isShovel", "isTorch" };
        private static readonly string[] ArmorSlots = { "Head", "Body", "Legs", "Feet" };

        private static bool Weapon(ItemTemplate t) => Has(t, WeaponFlags);
        private static bool Armor(ItemTemplate t) => ArmorSlots.Contains(t.slot) && Has(t, "isLightArmor", "isHeavyArmor");
        private static bool Food(ItemTemplate t) => t.food > 0;
        private static bool Drink(ItemTemplate t) => Named(t, "beer", "ale", "wine", "mead", "cider", "milk", "tea");

        // shop id -> (display name, item filter, max item level)
        private static readonly Dictionary<int, KeyValuePair<string, Func<ItemTemplate, bool>>> rules =
            new Dictionary<int, KeyValuePair<string, Func<ItemTemplate, bool>>>();

        private static void Rule(string name, Func<ItemTemplate, bool> filter, params int[] shops)
        {
            foreach (int s in shops) rules[s] = new KeyValuePair<string, Func<ItemTemplate, bool>>(name, filter);
        }

        static ShopData()
        {
            Rule("Weapons", t => Weapon(t) && t.level <= 10, 1, 9, 18, 37);
            Rule("Armor", t => (Armor(t) || Has(t, "isShield")) && t.level <= 10, 2);
            Rule("Bakery", t => Food(t) && Named(t, "bread", "bun", "pie", "cake", "roll", "pastry", "cookie"), 4);
            Rule("Inn", t => (Food(t) && t.level <= 10) || Drink(t), 5, 30, 43);
            Rule("Arcane goods", t => (Has(t, "isStaff", "isFireStaff", "isIceStaff", "isWand") || Named(t, "robe", "rune")) && t.level <= 15, 6, 24, 39);
            Rule("Mining supplies", t => Has(t, "isPickaxe") || Named(t, " ore", "coal"), 7);
            Rule("Healing supplies", t => Named(t, "bandage", "potion", "salve") || Has(t, "isInstrument"), 8);
            Rule("General store", t => Has(t, ToolFlags) || Named(t, "rope", "needle", "vial", "bucket"), 10, 19, 3, 26, 27, 28, 42);
            Rule("Fishing supplies", t => Has(t, "isFishingRod", "isFishingNet") || Named(t, "bait"), 11);
            Rule("Farming supplies", t => Has(t, "FarmingSeed", "isSickle", "isScythe", "isShovel") || Named(t, "seed"), 12, 15);
            Rule("Alchemy supplies", t => Named(t, "vial", "flask", "mortar", "berries", "shroom", "herb", "root") && t.level <= 10, 13);
            Rule("Lumber", t => Has(t, "isHatchet") || Named(t, "plank", "log"), 14);
            Rule("Food", t => Food(t) && t.level <= 15, 16, 33, 40);
            Rule("Gems", t => Named(t, "ruby", "sapphire", "emerald", "diamond", "amethyst", "topaz", "opal", "gem"), 17);
            Rule("Jewellery", t => t.slot == "Ring" || t.slot == "Neck" || t.slot == "Bracelet", 20);
            Rule("Stamps", t => Named(t, "stamp"), 21);
            Rule("Heavy armor", t => (Has(t, "isHeavyArmor") || Has(t, "isShield")) && t.level <= 15, 22);
            Rule("Light armor", t => Has(t, "isLightArmor") && t.level <= 15, 23);
            Rule("Bows", t => Has(t, "isBow", "isCrossBow") || Named(t, "arrow", "bolt"), 25);
            Rule("Boats", t => Has(t, "isBoat"), 29, 41);
            Rule("Vegetables", t => Named(t, "potato", "carrot", "cabbage", "onion", "tomato", "lettuce", "turnip", "pumpkin", "corn"), 31);
            Rule("Oysters", t => Named(t, "oyster", "pearl"), 32);
            Rule("Furniture", t => Has(t, "isFurniture"), 34);
            Rule("Tailor", t => ArmorSlots.Contains(t.slot) && !Has(t, "isHeavyArmor") && t.level <= 5 || Named(t, "cloth", "yarn", "needle"), 36);
            Rule("Odds and ends", t => Named(t, "lockpick", "poison", "mask"), 38);
            Rule("Hunter's rewards", t => Has(t, "isPet") || Named(t, "trophy"), 44, 45, 46);
            Rule("Pets", t => Has(t, "isPet"), 47);
        }

        public static ShopStock Get(int shopId)
        {
            if (cache.TryGetValue(shopId, out var stock)) return stock;
            if (!rules.TryGetValue(shopId, out var rule)) rule = rules[10];
            stock = Build(rule.Key, rule.Value);
            if (stock.items.Count == 0) stock = Build("General store", rules[10].Value);
            cache[shopId] = stock;
            return stock;
        }

        // Items without an icon file are unused/test entries; the client would show a "?" for them.
        private static bool HasIcon(int typeId) =>
            File.Exists(Path.Combine(Application.dataPath, "Data/Textures/ItemIcons/Tex_ItemIcon_" + typeId + ".png"));

        private static ShopStock Build(string name, Func<ItemTemplate, bool> filter)
        {
            var stock = new ShopStock { name = name };
            foreach (var t in ItemData.Templates.Values
                         .Where(t => t.price > 0 && !string.IsNullOrEmpty(t.name) && !t.flags.Contains("untradeable") && HasIcon(t.typeId) && filter(t))
                         .OrderBy(t => t.level).ThenBy(t => t.price).Take(MaxItems))
                stock.items.Add(new[] { t.typeId, t.price });
            return stock;
        }
    }
}
