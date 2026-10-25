using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace GSOOffline
{
    /// <summary>
    /// The auction house (issue #88): with no other players, made-up traders post on it. Their buy orders want items
    /// the player can farm, craft or loot, and pay more the rarer the item (and the better the grade, for gear);
    /// their sell offers put gear and materials around the player's level on sale, dearer than any shop. Every order
    /// and offer has its own time limit, counted in playing time: the rarer, the shorter. A new order is posted every
    /// 15 minutes of play (up to 10), a new offer every 20 (up to 6).
    ///
    /// The stand (interactable 40) opens the client's auction window (31), which AuctionWindowPatch redraws as two
    /// lists: the offers for sale on the left, the orders on the right, each row with its price, grade and time left;
    /// a click asks to buy or sell. Trading uses the client's own messages, as on the original server: buying an offer
    /// is the chat command /buyoffer, selling to an order 25/13, and the window then asks for its lists again (3/55,
    /// /requestoffers). The server sends both every time (4/15 per offer, 7/40 per order), as the client's confirm
    /// handlers look the offer up there. Opening the window also lists both in chat.
    ///
    /// Balance: the shops are the reference. An order pays at most 80% of a shop's price for the item, so buying from
    /// a shop to fill orders loses silver; an offer costs at least 125% of it and twice the item's value, so nothing
    /// bought here sells back at a profit. Offers stay within a few levels of the player and below Epic grade.
    /// </summary>
    public partial class OfflineServer
    {
        private const int WindowAuctionHouse = 31, InteractAuctionStand = 40, InteractSilverBooth = 41;
        private const int MaxAuctionOrders = 10, MaxAuctionSales = 6, FirstAuctionOrders = 6, FirstAuctionSales = 4;
        private const float AuctionOrderEvery = 15 * 60f, AuctionSaleEvery = 20 * 60f;

        internal enum AuctionRarity { Common, Uncommon, Rare, VeryRare }

        // How often each rarity is posted, what share of the item's value an order pays, and its time limit (minutes).
        private static readonly float[] AuctionWeight = { 10f, 5f, 1.5f, 0.4f };
        private static readonly float[] OrderPremium = { 0.6f, 0.8f, 1.1f, 1.5f };
        private static readonly int[][] OrderMinutes = { new[] { 180, 240 }, new[] { 120, 180 }, new[] { 60, 90 }, new[] { 30, 60 } };
        // The most of a stacking item in one order or offer, by rarity (LotSize picks the amount by value).
        private static readonly int[] OrderMaxLot = { 40, 12, 4, 2 };
        private static readonly int[] SaleMaxLot = { 30, 10, 3, 3 };
        // What share of the item's value a sell offer asks, for materials by rarity; gear asks GearSaleMarkup.
        private static readonly float[] SaleMarkup = { 2f, 2.5f, 3f, 3f };
        private const float GearSaleMarkup = 2.5f;
        private const float OrderShopCap = 0.8f, SaleShopFloor = 1.25f;
        private const int MinAuctionValue = 5;   // cheaper items (soft clay, carrots) made orders worth a few silver

        // Gear grades: an order's lowest grade taken, and the grade of gear for sale (never Epic or better).
        private static readonly int[] OrderGrades = { 2, 3, 4, 5, 6 };
        private static readonly float[] OrderGradeWeights = { 45f, 30f, 17f, 6f, 2f };
        private static readonly int[] SaleGrades = { 2, 3, 4, 5 };
        private static readonly float[] SaleGradeWeights = { 45f, 30f, 18f, 7f };

        // Items no trader deals in: upgrade and grade stones (worth only their grade, which an order can't say) and
        // the ancient cloth a quest asks for, which only a boss drops.
        private static readonly HashSet<int> NotTraded = new HashSet<int> { 437, 438, 439, 440, 441, 442, 443, 10645 };

        private class AuctionItem
        {
            public ItemTemplate t;
            public int level;              // the easiest way to come by it: recipe, node or creature level
            public AuctionRarity rarity;
            public int shopPrice;          // the cheapest shop's price, or -1
            public bool Gear => t.slot != null && !t.Stacks;
        }

        private static List<AuctionItem> auctionPool;
        private float auctionOpenedAt = -10f;

        private void RegisterAuctionHandlers()
        {
            On(3, 55, c => RefreshAuctionWindow(false));   // the window's refresh after a sale (once "Search buy offers")
            On(3, 54, c => RefreshAuctionWindow(false));   // once "Search sell offers"
            On(25, 13, c => FillAuctionOrder((int)c[1]));   // selling to a buy order
        }

        /// <summary>Chat commands the auction window sends. True if it was one.</summary>
        private bool RunAuctionCommand(string cmd, string[] a)
        {
            switch (cmd)
            {
                case "buyoffer":
                    BuyAuctionSale(int.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture));
                    return true;
                case "requestoffers":     // after buying
                case "requestmyoffers":   // when the window opens
                    RefreshAuctionWindow(cmd == "requestmyoffers" && Time.time - auctionOpenedAt < 3f);
                    return true;
            }
            return false;
        }

        /// <summary>The auction stand opens the window; the silver booth has nothing to pay out. True if it was one.</summary>
        private bool HandleAuctionInteract(int typeId)
        {
            if (character == null) return false;
            if (typeId == InteractSilverBooth)
            {
                Notice("The traders pay on the spot: no silver is waiting here.", "gray");
                return true;
            }
            if (typeId != InteractAuctionStand) return false;
            StockAuction();
            auctionOpenedAt = Time.time;
            Send(3, 33, character.name, WindowAuctionHouse);
            WindowOpened(WindowAuctionHouse);
            return true;
        }

        // ---- the item pool -------------------------------------------------------------------

        /// <summary>
        /// Everything a trader may deal in: items with a way to come by them (a harvest, a recipe or a creature's
        /// drop), tradeable, and not pets or boats. Quest items have none of those, so they never appear.
        /// Rarity: harvests and crafts are Common, or Uncommon from level 40; drops go by their best chance
        /// (1 in 20, 100, 500), and drops only bosses have are Rare at least.
        /// </summary>
        private static List<AuctionItem> AuctionPool
        {
            get
            {
                if (auctionPool != null) return auctionPool;
                GameData.EnsureLoaded();
                var level = new Dictionary<int, int>();
                var made = new HashSet<int>();                       // harvested or crafted
                var bestDrop = new Dictionary<int, float>();
                var creatureDrop = new HashSet<int>();               // dropped by something that isn't a boss
                void Source(int typeId, int lvl)
                {
                    level[typeId] = level.TryGetValue(typeId, out int l) ? Mathf.Min(l, lvl) : lvl;
                }
                foreach (var h in SkillData.Harvestables.Values)
                    foreach (var d in h.drops)
                        if (d.dropRate >= 10) { Source(d.item, h.level); made.Add(d.item); }   // 1% or better
                foreach (var r in SkillData.Recipes.Values) { Source(r.product, r.level); made.Add(r.product); }
                foreach (var npc in GameData.Npcs.Values)
                    foreach (var d in LootData.OriginalDrops(npc.id))
                    {
                        if (d.typeId <= 0) continue;
                        Source(d.typeId, npc.level);
                        float chance = 1f / Mathf.Max(1, d.rate);
                        bestDrop[d.typeId] = bestDrop.TryGetValue(d.typeId, out float c) ? Mathf.Max(c, chance) : chance;
                        if (!npc.boss) creatureDrop.Add(d.typeId);
                    }

                auctionPool = new List<AuctionItem>();
                foreach (var kv in level)
                {
                    var t = ItemData.Get(kv.Key);
                    if (t == null || string.IsNullOrEmpty(t.name) || t.price < MinAuctionValue || t.unique || NotTraded.Contains(t.typeId)) continue;
                    if (t.flags.Contains("untradeable") || t.flags.Contains("untradeableOnAH") || t.flags.Contains("isPet") || t.flags.Contains("isBoat")) continue;
                    AuctionRarity rarity;
                    if (made.Contains(t.typeId)) rarity = kv.Value < 40 ? AuctionRarity.Common : AuctionRarity.Uncommon;
                    else
                    {
                        float best = bestDrop[t.typeId];
                        rarity = best >= 1f / 20 ? AuctionRarity.Common : best >= 1f / 100 ? AuctionRarity.Uncommon : best >= 1f / 500 ? AuctionRarity.Rare : AuctionRarity.VeryRare;
                        if (!creatureDrop.Contains(t.typeId))
                            rarity = (AuctionRarity)Mathf.Max((int)rarity, best >= 1f / 100 ? (int)AuctionRarity.Rare : (int)AuctionRarity.VeryRare);
                    }
                    auctionPool.Add(new AuctionItem { t = t, level = kv.Value, rarity = rarity, shopPrice = ShopData.CheapestPrice(t.typeId) });
                }
                var counts = new int[4];
                foreach (var i in auctionPool) counts[(int)i.rarity]++;
                Plugin.Log.LogInfo($"[auction] {auctionPool.Count} tradeable items: {counts[0]} common, {counts[1]} uncommon, {counts[2]} rare, {counts[3]} very rare");
                return auctionPool;
            }
        }

        /// <summary>The player's best level, combat or any skill: what the traders deal around.</summary>
        private int AuctionLevel()
        {
            int top = CombatLevel();
            foreach (int skill in SkillById.Keys) top = Mathf.Max(top, SkillLevel(skill));
            return top;
        }

        // The client's item price for a grade (Menucontroller.GetItemPriceGradeMultiplier): x1.3 a grade above Normal.
        private static float GradePrice(int grade) => Mathf.Pow(1.3f, grade - 2);

        private static T Weighted<T>(IList<T> items, Func<T, float> weight)
        {
            float total = 0f;
            foreach (var i in items) total += weight(i);
            float roll = Random.value * total;
            foreach (var i in items)
            {
                roll -= weight(i);
                if (roll <= 0f) return i;
            }
            return items[items.Count - 1];
        }

        private static int WeightedGrade(int[] grades, float[] weights)
        {
            var index = new List<int>();
            for (int i = 0; i < grades.Length; i++) index.Add(i);
            return grades[Weighted(index, i => weights[i])];
        }

        private List<AuctionItem> AuctionCandidates(int below, int above, Predicate<AuctionItem> keep)
        {
            int top = AuctionLevel();
            var list = AuctionPool.FindAll(i => i.level <= top + above && i.level >= top - below && keep(i));
            if (list.Count < 10) list = AuctionPool.FindAll(i => i.level <= top + above && keep(i));
            return list;
        }

        // ---- posting -------------------------------------------------------------------------

        /// <summary>The first visit: the boards start with some orders and offers.</summary>
        private void StockAuction()
        {
            if (character == null || character.auctionStocked) return;
            character.auctionStocked = true;
            for (int i = 0; i < FirstAuctionOrders; i++) PostAuctionOrder();
            for (int i = 0; i < FirstAuctionSales; i++) PostAuctionSale();
            character.auctionNextOrder = AuctionOrderEvery;
            character.auctionNextSale = AuctionSaleEvery;
        }

        private void PostAuctionOrder()
        {
            var wanted = new HashSet<int>();
            foreach (var o in character.auctionOrders) wanted.Add(o.typeId);
            var list = AuctionCandidates(40, 10, i => !wanted.Contains(i.t.typeId));
            if (list.Count == 0) return;
            var item = Weighted(list, i => AuctionWeight[(int)i.rarity]);
            var t = item.t;
            var o2 = new AuctionOrderSave { id = character.nextAuctionId++, typeId = t.typeId, rarity = (int)item.rarity, amount = 1 };
            float gradePrice = 1f;
            if (item.Gear)
            {
                o2.grade = WeightedGrade(OrderGrades, OrderGradeWeights);
                gradePrice = GradePrice(o2.grade);
                if (o2.grade >= 5) o2.rarity = Mathf.Min(o2.rarity + 1, (int)AuctionRarity.VeryRare);   // fewer of those around
            }
            float each = t.price * OrderPremium[(int)item.rarity] * Random.Range(0.85f, 1.15f) * gradePrice;
            if (item.shopPrice > 0) each = Mathf.Min(each, item.shopPrice * OrderShopCap * gradePrice);
            if (t.Stacks) o2.amount = LotSize(each, OrderMaxLot[(int)item.rarity]);
            o2.price = Mathf.Max(1, Mathf.RoundToInt(each * o2.amount));
            var minutes = OrderMinutes[o2.rarity];
            o2.secondsLeft = Random.Range(minutes[0], minutes[1] + 1) * 60f;
            character.auctionOrders.Add(o2);
            Plugin.Log.LogInfo($"[auction] order {o2.id}: {o2.amount} x {t.name}{(o2.grade > 0 ? " (" + GradeNames[o2.grade] + "+)" : "")} for {o2.price} silver, {(AuctionRarity)o2.rarity}, {o2.secondsLeft / 60f:F0} min");
        }

        /// <summary>
        /// How many of a stacking item make one lot: about 200 silver plus 50 a level of the player, give or take 40%,
        /// up to <paramref name="most"/>. Counts alone made a level 5 offer 13 Deadwood logs for 6,124 silver.
        /// </summary>
        private int LotSize(float each, int most) =>
            Mathf.Clamp(Mathf.RoundToInt((200f + 50f * AuctionLevel()) * Random.Range(0.6f, 1.4f) / Mathf.Max(1f, each)), 1, most);

        private void PostAuctionSale()
        {
            bool gear = Random.value < 0.5f;
            var list = gear
                ? AuctionCandidates(15, 5, i => i.Gear && i.rarity <= AuctionRarity.Uncommon)
                : AuctionCandidates(20, 10, i => i.t.Stacks && i.rarity <= AuctionRarity.Rare);
            if (list.Count == 0) return;
            var item = Weighted(list, i => AuctionWeight[(int)i.rarity]);
            var t = item.t;
            var s = new AuctionSaleSave { id = character.nextAuctionId++ };
            float each;
            if (gear)
            {
                s.item = NewItem(t, 1, announceGrade: false);   // the very item, stats and abilities rolled now
                s.item.grade = WeightedGrade(SaleGrades, SaleGradeWeights);
                float gradePrice = GradePrice(s.item.grade);
                each = t.price * GearSaleMarkup * gradePrice * Random.Range(0.9f, 1.2f);
                if (item.shopPrice > 0) each = Mathf.Max(each, item.shopPrice * SaleShopFloor * gradePrice);
            }
            else
            {
                each = t.price * SaleMarkup[(int)item.rarity] * Random.Range(0.9f, 1.2f);
                if (item.shopPrice > 0) each = Mathf.Max(each, item.shopPrice * SaleShopFloor);
                s.item = new ItemSave { typeId = t.typeId, amount = LotSize(each, SaleMaxLot[(int)item.rarity]), grade = t.baseGrade };
            }
            s.price = Mathf.Max(1, Mathf.RoundToInt(each * s.item.amount));
            s.secondsLeft = Random.Range(60, 181) * 60f;
            character.auctionSales.Add(s);
            Plugin.Log.LogInfo($"[auction] offer {s.id}: {s.item.amount} x {t.name}{(gear ? " (" + GradeNames[s.item.grade] + ")" : "")} for {s.price} silver, {s.secondsLeft / 60f:F0} min");
        }

        /// <summary>Time limits run down while playing; new orders and offers come in on their own clocks.</summary>
        private void TickAuction()
        {
            if (character == null || !sceneReady || !character.auctionStocked) return;
            float dt = Time.deltaTime;
            for (int i = character.auctionOrders.Count - 1; i >= 0; i--)
            {
                var o = character.auctionOrders[i];
                o.secondsLeft -= dt;
                if (o.secondsLeft > 0f) continue;
                Plugin.Log.LogInfo($"[auction] order {o.id} ({ItemData.Get(o.typeId)?.name}) ran out");
                character.auctionOrders.RemoveAt(i);
            }
            for (int i = character.auctionSales.Count - 1; i >= 0; i--)
            {
                var s = character.auctionSales[i];
                s.secondsLeft -= dt;
                if (s.secondsLeft > 0f) continue;
                Plugin.Log.LogInfo($"[auction] offer {s.id} ({ItemData.Get(s.item.typeId)?.name}) ran out");
                character.auctionSales.RemoveAt(i);
            }
            character.auctionNextOrder -= dt;
            if (character.auctionNextOrder <= 0f)
            {
                character.auctionNextOrder = AuctionOrderEvery;
                if (character.auctionOrders.Count < MaxAuctionOrders) PostAuctionOrder();
            }
            character.auctionNextSale -= dt;
            if (character.auctionNextSale <= 0f)
            {
                character.auctionNextSale = AuctionSaleEvery;
                if (character.auctionSales.Count < MaxAuctionSales) PostAuctionSale();
            }
        }

        // ---- listing -------------------------------------------------------------------------

        internal static string AuctionTimeLeft(float seconds)
        {
            int minutes = Mathf.Max(1, Mathf.CeilToInt(seconds / 60f));
            return minutes >= 60 ? $"{minutes / 60}h {minutes % 60:D2}m" : $"{minutes}m";
        }

        private static string Silver(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        /// <param name="row">The window's row: shorter, "unusual+" and no "left".</param>
        private string OrderLine(AuctionOrderSave o, bool row = false)
        {
            string name = ItemData.Get(o.typeId)?.name ?? "item " + o.typeId;
            string what = o.grade <= 0 ? $"{o.amount} x {name}" : row ? $"{name}, {GradeNames[o.grade]}+" : $"{name}, {GradeNames[o.grade]} or better";
            return $"{what}: {Silver(o.price)} silver, {AuctionTimeLeft(o.secondsLeft)}{(row ? "" : " left")}";
        }

        private string SaleLine(AuctionSaleSave s, bool row = false)
        {
            var t = ItemData.Get(s.item.typeId);
            string name = t?.name ?? "item " + s.item.typeId;
            string what = t != null && !t.Stacks && t.slot != null ? $"{name} ({GradeNames[s.item.grade]})" : $"{s.item.amount} x {name}";
            return $"{what}: {Silver(s.price)} silver, {AuctionTimeLeft(s.secondsLeft)}{(row ? "" : " left")}";
        }

        /// <summary>
        /// Both lists into the client's (its confirm handlers find a sell offer there), and on opening the window, in
        /// chat too.
        /// </summary>
        private void RefreshAuctionWindow(bool announce)
        {
            if (character == null) return;
            StockAuction();
            var ah = Scr_AuctionHouse.instance;
            if (ah != null)
            {
                ah.offers.Clear();
                ah.buyOffers.Clear();
            }
            foreach (var s in character.auctionSales)
                Send(4, 15, ItemDataString(s.item, s.item.amount), s.id, s.item.amount, s.price);
            foreach (var o in character.auctionOrders)
                Send(7, 40, JsonUtility.ToJson(new Scr_Packets.AHBuyOffer { offerId = o.id, itemTypeId = o.typeId, amount = o.amount, price = o.price }));
            if (!announce) return;
            if (character.auctionSales.Count == 0) Notice("No trader is selling anything right now.", "gray");
            else
            {
                Notice($"{character.auctionSales.Count} for sale:");
                foreach (var s in character.auctionSales) Notice("  " + SaleLine(s), "white");
            }
            if (character.auctionOrders.Count == 0) Notice("No trader is buying anything right now.", "gray");
            else
            {
                Notice($"{character.auctionOrders.Count} wanted:");
                foreach (var o in character.auctionOrders) Notice("  " + OrderLine(o), "white");
            }
        }

        // ---- trading -------------------------------------------------------------------------

        /// <summary>
        /// 25/13: the player fills a buy order. Gear goes by grade: the lowest-graded piece that meets the order,
        /// never one that's worn or on the back. The order pays what it said, whatever the grade handed in.
        /// </summary>
        private void FillAuctionOrder(int orderId)
        {
            if (character == null) return;
            var o = character.auctionOrders.Find(x => x.id == orderId);
            var t = o != null ? ItemData.Get(o.typeId) : null;
            if (o == null || t == null)
            {
                Notice("That order is no longer on the board.", "red");
                return;
            }
            if (t.Stacks)
            {
                if (CountItem(o.typeId) < o.amount)
                {
                    Notice("You don't have the items required to complete this offer", "red");
                    return;
                }
                TakeItems(o.typeId, o.amount);
            }
            else
            {
                ItemSave pick = null;
                bool worn = false, lower = false;
                foreach (var it in character.items)
                {
                    if (it.typeId != o.typeId) continue;
                    if (it.grade < o.grade) { lower = true; continue; }
                    if (it.equipped || it.id == character.secondaryWeaponId) { worn = true; continue; }
                    if (pick == null || it.grade < pick.grade) pick = it;
                }
                if (pick == null)
                {
                    Notice(worn ? $"Take off your {t.name} first." :
                           lower ? $"This trader wants {("aeiou".IndexOf(GradeNames[o.grade][0]) >= 0 ? "an" : "a")} {GradeNames[o.grade]} {t.name} or better." :
                           "You don't have the items required to complete this offer", "red");
                    return;
                }
                RemoveFromStack(pick, 1);
            }
            character.auctionOrders.Remove(o);
            SetSilver(character.silver + o.price);
            Notice($"You have successfully sold {o.amount} {t.name} for {Silver(o.price)} silver", "green");
            Plugin.Log.LogInfo($"[auction] filled order {o.id}: {o.amount} x {t.name} for {o.price} silver");
            SaveCurrentCharacter();
        }

        /// <summary>/buyoffer: the player buys a sell offer, the very item listed.</summary>
        private void BuyAuctionSale(int saleId)
        {
            if (character == null) return;
            var s = character.auctionSales.Find(x => x.id == saleId);
            var t = s != null ? ItemData.Get(s.item.typeId) : null;
            if (s == null || t == null)
            {
                Notice("That offer is no longer on the board.", "red");
                return;
            }
            if (character.silver < s.price)
            {
                Notice("You don't have enough silver to do that.", "red");
                return;
            }
            character.auctionSales.Remove(s);
            SetSilver(character.silver - s.price);
            if (t.Stacks) GiveItem(t.typeId, s.item.amount);
            else
            {
                s.item.equipped = false;
                character.items.Add(s.item);
                SendItemAdded(s.item, s.item.amount);
                CheckItemTriggers();
            }
            Notice($"Successfully bought [{s.item.amount} x {t.name} for {Silver(s.price)} silver]", "green");
            Plugin.Log.LogInfo($"[auction] bought offer {s.id}: {s.item.amount} x {t.name} (grade {s.item.grade}) for {s.price} silver");
            SaveCurrentCharacter();
        }

        // ---- the window's rows (AuctionWindowPatch) ----------------------------------------------

        /// <summary>One row of the auction window: an offer for sale, or an order.</summary>
        internal class AuctionRow
        {
            public int id, typeId, amount, price, grade;   // grade: the item's, or the lowest an order takes (0 for none)
            public string name, text;
        }

        internal List<AuctionRow> AuctionSaleRows()
        {
            var rows = new List<AuctionRow>();
            if (character == null) return rows;
            foreach (var s in character.auctionSales)
            {
                var t = ItemData.Get(s.item.typeId);
                rows.Add(new AuctionRow
                {
                    id = s.id, typeId = s.item.typeId, amount = s.item.amount, price = s.price, name = t?.name ?? "item " + s.item.typeId,
                    grade = t != null && !t.Stacks ? s.item.grade : 0, text = SaleLine(s, row: true),
                });
            }
            return rows;
        }

        internal List<AuctionRow> AuctionOrderRows()
        {
            var rows = new List<AuctionRow>();
            if (character == null) return rows;
            foreach (var o in character.auctionOrders)
                rows.Add(new AuctionRow
                {
                    id = o.id, typeId = o.typeId, amount = o.amount, price = o.price, grade = o.grade,
                    name = ItemData.Get(o.typeId)?.name ?? "item " + o.typeId, text = OrderLine(o, row: true),
                });
            return rows;
        }

        internal static string GradeName(int grade) => grade > 0 && grade < GradeNames.Length ? GradeNames[grade] : "";

        // ---- dev ---------------------------------------------------------------------------------

        /// <summary>Dev: auction [orders|sales|order|expire|clear]: post more, post one for an item type, run every time limit down to a minute, or empty the boards.</summary>
        internal void DevAuction(string what, int n)
        {
            if (character == null) return;
            StockAuction();
            switch (what)
            {
                case "orders": for (int i = 0; i < n; i++) PostAuctionOrder(); break;
                case "sales": for (int i = 0; i < n; i++) PostAuctionSale(); break;
                case "order":   // n = the item type: one of it (gear: Unusual or better) for its value
                    var t = ItemData.Get(n);
                    if (t != null)
                        character.auctionOrders.Add(new AuctionOrderSave { id = character.nextAuctionId++, typeId = n, amount = 1, grade = t.Stacks ? 0 : 3, price = t.price, secondsLeft = 3600f });
                    break;
                case "expire":
                    foreach (var o in character.auctionOrders) o.secondsLeft = Mathf.Min(o.secondsLeft, 60f);
                    foreach (var s in character.auctionSales) s.secondsLeft = Mathf.Min(s.secondsLeft, 60f);
                    break;
                case "clear": character.auctionOrders.Clear(); character.auctionSales.Clear(); break;
            }
            foreach (var o in character.auctionOrders) Plugin.Log.LogInfo($"[dev] order {o.id}: {OrderLine(o)} ({(AuctionRarity)o.rarity})");
            foreach (var s in character.auctionSales) Plugin.Log.LogInfo($"[dev] offer {s.id}: {SaleLine(s)}");
            Plugin.Log.LogInfo($"[dev] auction: {character.auctionOrders.Count} orders, {character.auctionSales.Count} offers, level {AuctionLevel()}, next order in {character.auctionNextOrder:F0}s, offer in {character.auctionNextSale:F0}s");
        }
    }
}
