using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GSOOffline
{
    [Serializable]
    public class ShopFileItem
    {
        public int typeId;
        public string name;
        public int price;     // 0 = the item's own price
        public int max, stock, restock;   // the original's stock cap, starting stock and restock seconds (0 = its defaults)
    }

    [Serializable]
    public class ShopFileShop
    {
        public int id;
        public string name;
        public int currency;   // item type the shop takes instead of silver (0 = silver)
        public List<ShopFileItem> items = new List<ShopFileItem>();
    }

    [Serializable]
    public class ShopFile
    {
        public List<ShopFileShop> shops = new List<ShopFileShop>();
    }

    public class ShopStock
    {
        public string name;
        public int currency;   // item type paid instead of silver (0 = silver); such shops buy nothing
        public int merchantSilver = 5000;
        public List<ShopEntry> items = new List<ShopEntry>();
    }

    /// <summary>
    /// One item a shop sells. Original shops keep stock as the original server did (Scr_ShopHandler): it starts at
    /// the list's starting stock (else its cap, else 1), comes back one at a time every restock seconds (default 5)
    /// up to the cap (default 1), and resources sold to the shop go back on its shelf. Themed fallback shops have
    /// no stock limit (stock -1).
    /// </summary>
    public class ShopEntry
    {
        public int typeId, price;
        public int stock = -1, max, restock;
        public float nextRestock;

        public bool Limited => stock >= 0;
        public int Shown => Limited ? stock : 99;
    }

    /// <summary>
    /// NPC shop inventories, by shop id (dialogue openshop=). Data/shops.json is the original server's stock
    /// (Shops.xml, sent by the game's developer; built by GSODevTools' gen_shops.py), at each item's price
    /// unless the shop sets one. A shop id it lacks falls back to the themed rules below, which picked
    /// matching items from the item database before the original lists were known.
    /// </summary>
    internal static class ShopData
    {
        private const int MaxItems = 40;

        // The Tool vendor has no dialogue to name a shop; the original's "Tools" shop is his stock.
        public const int ToolVendorShop = 3;

        private static Dictionary<int, ShopFileShop> original;

        private static Dictionary<int, ShopFileShop> Original
        {
            get
            {
                if (original != null) return original;
                original = new Dictionary<int, ShopFileShop>();
                using (var s = typeof(ShopData).Assembly.GetManifestResourceStream("GSOOffline.shops.json"))
                {
                    if (s == null) return original;
                    using (var r = new StreamReader(s))
                        foreach (var shop in Json.Deserialize<ShopFile>(r.ReadToEnd()).shops) original[shop.id] = shop;
                }
                Plugin.Log.LogInfo($"[shop] {original.Count} shops from the original server's stock lists");
                return original;
            }
        }
        private static Dictionary<int, int> cheapest;

        /// <summary>The lowest silver price any original shop sells this item for, or -1 if no shop stocks it.</summary>
        public static int CheapestPrice(int typeId)
        {
            if (cheapest == null)
            {
                cheapest = new Dictionary<int, int>();
                foreach (var shop in Original.Values)
                {
                    if (shop.currency != 0) continue;
                    foreach (var i in shop.items)
                    {
                        int price = i.price > 0 ? i.price : ItemData.Get(i.typeId)?.price ?? 0;
                        if (price > 0 && (!cheapest.TryGetValue(i.typeId, out int p) || price < p)) cheapest[i.typeId] = price;
                    }
                }
            }
            return cheapest.TryGetValue(typeId, out int found) ? found : -1;
        }

        private static readonly Dictionary<int, ShopStock> cache = new Dictionary<int, ShopStock>();
        private static readonly HashSet<int> UnearnableCurrencies = new HashSet<int> { 10725 };   // Hunters Guild token

        private static bool Has(ItemTemplate t, params string[] flags) => flags.Any(f => t.flags.Contains(f));
        private static bool Named(ItemTemplate t, params string[] words) =>
            t.name != null && words.Any(w => t.name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

        private static readonly string[] WeaponFlags =
            { "isBroadsword", "isGreatsword", "isHalberd", "isDagger", "isMace", "isBow", "isCrossBow", "isStaff", "isFireStaff", "isIceStaff", "isWand" };
        private static readonly string[] ToolFlags =
            { "isHatchet", "isPickaxe", "isFishingRod", "isFishingNet", "isSickle", "isShears", "isScythe", "isShovel", "isTorch" };
        private static readonly string[] ArmorSlots = { "Head", "Body", "Legs", "Feet" };

        private static bool Weapon(ItemTemplate t) => Has(t, WeaponFlags);
        // The client hard-codes the basic tools (hatchets 58 and 166, pickaxe 59, fishing rod 90...) rather than
        // flagging them in the item data, so ask it; flags alone left them out of every shop.
        // Basic ones only: not the Dragon torch (a level 56 craft) or named keepsakes such as Owen's hatchet.
        private static bool Tool(ItemTemplate t) =>
            (Has(t, ToolFlags) || Scr_ItemHandler.instance.isTool(t.typeId)) && t.level <= 10 && !t.name.Contains("'s ");
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
            Rule("General store", t => Tool(t) || Named(t, "rope", "needle", "vial", "bucket"), 10, 19, 3, 26, 27, 28, 42);
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
            // The Hunters Guild's shops take its tokens, which only its monster bounties paid out; with no way to earn
            // them offline, those shops keep their silver rules (below) and the token lists wait in shops.json.
            if (Original.TryGetValue(shopId, out var real) && !UnearnableCurrencies.Contains(real.currency))
            {
                stock = new ShopStock { name = real.name, currency = real.currency };
                foreach (var it in real.items)
                {
                    var t = ItemData.Get(it.typeId);
                    if (t == null) continue;
                    int max = it.max > 0 ? it.max : 1;
                    stock.items.Add(new ShopEntry
                    {
                        typeId = it.typeId, price = it.price > 0 ? it.price : t.price,
                        max = max, stock = it.stock > 0 ? it.stock : max, restock = it.restock > 0 ? it.restock : 5,
                    });
                }
                cache[shopId] = stock;
                return stock;
            }
            if (!rules.TryGetValue(shopId, out var rule)) rule = rules[10];
            stock = Build(rule.Key, rule.Value);
            if (stock.items.Count == 0) stock = Build("General store", rules[10].Value);
            cache[shopId] = stock;
            return stock;
        }

        /// <summary>Puts restocked items back on the shelves of every shop opened this session; true if the given one changed.</summary>
        public static bool Restock(ShopStock watched)
        {
            bool changed = false;
            foreach (var shop in cache.Values)
                foreach (var e in shop.items)
                {
                    if (!e.Limited || e.stock >= e.max) { e.nextRestock = 0f; continue; }
                    if (e.nextRestock <= 0f) { e.nextRestock = Time.time + e.restock; continue; }
                    if (Time.time < e.nextRestock) continue;
                    e.stock++;
                    e.nextRestock = e.stock < e.max ? Time.time + e.restock : 0f;
                    if (shop == watched) changed = true;
                }
            return changed;
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
                stock.items.Add(new ShopEntry { typeId = t.typeId, price = t.price });
            return stock;
        }
    }
}
