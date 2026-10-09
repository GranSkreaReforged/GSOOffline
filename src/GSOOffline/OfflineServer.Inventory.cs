using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GSOOffline
{
    public partial class OfflineServer
    {
        // Scr_SkillsHandler.addXp skill ids -> addPlayer "s_<name>" keys.
        internal static readonly Dictionary<int, string> SkillById = new Dictionary<int, string>
        {
            { 3, "Woodcutting" }, { 4, "Blacksmithy" }, { 5, "Mining" }, { 6, "Fishing" }, { 7, "Farming" },
            { 8, "Alchemy" }, { 9, "Cooking" }, { 10, "Carpentry" }, { 11, "Tailoring" }, { 12, "Cartography" },
            { 13, "Sailing" }, { 14, "Thieving" }, { 15, "Swordsmanship" }, { 16, "Fencing" }, { 17, "Archery" },
            { 18, "Macefighting" }, { 20, "Elementalgrim" }, { 21, "Mentalgrim" }, { 22, "Arcanegrim" },
            { 23, "Wrestling" }, { 24, "Healing" }, { 25, "Lightarmor" }, { 26, "Heavyarmor" },
            { 27, "Jewellery" }, { 28, "Animaltaming" },
        };

        // Starting kits as listed on the character creation screen (Scr_CharacterCreation).
        private static readonly Dictionary<int, int> StyleWeapon = new Dictionary<int, int>
        {
            { 1, 183 }, { 2, 199 }, { 3, 179 }, { 4, 46 }, { 5, 79 }, { 7, 83 }, { 8, 81 }, { 9, 50 }, { 10, 80 },
        };

        private static readonly Dictionary<int, int[][]> ProfessionKit = new Dictionary<int, int[][]>
        {
            { 1, new[] { new[] { 10037, 8 } } },
            { 2, new[] { new[] { 59, 1 } } },
            { 3, new[] { new[] { 58, 1 } } },
            { 4, new[] { new[] { 10319, 10 }, new[] { 10094, 5 }, new[] { 10333, 1 }, new[] { 40, 1 } } },
            { 5, new[] { new[] { 10078, 10 }, new[] { 10080, 10 } } },
            { 6, new[] { new[] { 10360, 10 } } },
            { 7, new[] { new[] { 90, 1 } } },
            { 8, new[] { new[] { 10062, 8 } } },
            { 10, new[] { new[] { 10367, 8 } } },
        };

        private static readonly Dictionary<int, int> ProfessionSkill = new Dictionary<int, int>
        {
            { 1, 4 }, { 2, 5 }, { 3, 3 }, { 4, 8 }, { 5, 9 }, { 6, 11 }, { 7, 6 }, { 8, 10 }, { 10, 24 },
        };

        private static readonly int[] StarterClothes = { 243, 163, 19 };

        private void RegisterInventoryHandlers()
        {
            On(3, 4, c => Equip((int)c[2]));
            On(3, 5, c => Unequip((int)c[2]));
            On(4, 4, c => DestroyItem((int)c[1], (int)c[2], (int)c[3]));
            On(4, 14, c => { if (!OnQuestItemUsed((int)c[3])) DestroyItem((int)c[2], (int)c[3], (int)c[4]); });   // no ground loot yet: dropping destroys
            On(4, 1, OnChangeTab);
            On(198, 15, OnSwapItems);
            On(198, 17, OnInsertItem);
            On(198, 16, c => MirrorClientOrder());
            On(3, 18, c => { if (!OnQuestItemUsed((int)c[2])) UseItem((int)c[2]); });
            On(16, 3, c => UseExperienceScroll((int)c[1], (int)c[2]));
            On(6, 5, c => SwitchWeapon());
            On(6, 6, c => SetSecondaryWeapon(null));
        }

        // ---- queries -------------------------------------------------------------------------

        private ItemSave FindItem(int itemId)
        {
            if (character == null) return null;
            foreach (var it in character.items) if (it.id == itemId) return it;
            return null;
        }

        private ItemSave FindStack(int typeId)
        {
            if (character == null) return null;
            foreach (var it in character.items) if (it.typeId == typeId) return it;
            return null;
        }

        public int CountItem(int typeId)
        {
            int n = 0;
            if (character != null)
                foreach (var it in character.items) if (it.typeId == typeId) n += it.amount;
            return n;
        }

        // ---- client sync ---------------------------------------------------------------------

        /// <summary>
        /// The id the client knows an item by. Stacks are id 0 on the wire: the inventory and bank windows
        /// only draw a stack's count when its id is 0, and the client matches bank removals on it.
        /// </summary>
        private static int WireId(ItemSave it) => ItemData.Get(it.typeId)?.Stacks == true ? 0 : it.id;

        private string ItemDataString(ItemSave it, int amount)
        {
            var sb = new StringBuilder();
            sb.Append("Id=").Append(WireId(it)).Append('\n');
            sb.Append("Typeid=").Append(it.typeId).Append('\n');
            sb.Append("Amount=").Append(amount).Append('\n');
            sb.Append("Tab=").Append(it.tab).Append('\n');
            sb.Append("Grade=").Append(it.grade).Append('\n');
            var t = ItemData.Get(it.typeId);
            if (t?.slot != null) sb.Append("Slot=").Append(t.slot).Append('\n');
            foreach (var s in it.stats)
                sb.Append(s.key).Append('=').Append(s.value).Append('\n');
            return sb.ToString();
        }

        private void SendItemAdded(ItemSave it, int amount) => Send(2, 1, ItemDataString(it, amount), character.name);

        private bool equipSyncPending;

        /// <summary>Full inventory push after the player object exists (on entering the world).</summary>
        private void SendInventory()
        {
            if (character == null) return;
            foreach (var it in character.items)
                SendItemAdded(it, it.amount);
            // Equip confirmations sent before the world scene loads don't stick in the client's
            // equipment slots (the gear renders but the slots stay empty), so they wait for the scene.
            equipSyncPending = true;
        }

        private void SendPendingEquips()
        {
            if (!equipSyncPending || character == null) return;
            equipSyncPending = false;
            foreach (var it in character.items)
                if (it.equipped) Send(8, 0, character.name, it.id, 0);
            var back = SecondaryWeapon();
            if (back != null) SetSecondaryWeapon(back);
        }

        private List<int> EquippedTypeIds(CharacterSave ch)
        {
            var list = new List<int>();
            foreach (var it in ch.items) if (it.equipped) list.Add(it.typeId);
            return list;
        }

        // ---- mutations -----------------------------------------------------------------------

        private ItemSave NewItem(ItemTemplate t, int amount)
        {
            var it = new ItemSave { id = character.nextItemId++, typeId = t.typeId, amount = amount, grade = t.baseGrade };
            foreach (var r in t.stats)
                it.stats.Add(new ItemStat { key = r.key, value = Random.Range(Mathf.Min(r.min, r.max), Mathf.Max(r.min, r.max) + 1) });
            return it;
        }

        /// <summary>Give items to the current character and tell the client. Returns the last stack/instance created.</summary>
        public ItemSave GiveItem(int typeId, int amount = 1)
        {
            if (character == null || amount <= 0) return null;
            var t = ItemData.Get(typeId);
            if (t == null)
            {
                Plugin.Log.LogWarning($"GiveItem: unknown item type {typeId}");
                return null;
            }
            if (t.Stacks)
            {
                var stack = FindStack(typeId);
                if (stack == null)
                {
                    stack = NewItem(t, 0);
                    character.items.Add(stack);
                }
                stack.amount += amount;
                SendItemAdded(stack, amount);
                CheckItemTriggers();
                return stack;
            }
            ItemSave last = null;
            for (int i = 0; i < amount; i++)
            {
                last = NewItem(t, 1);
                character.items.Add(last);
                SendItemAdded(last, 1);
            }
            CheckItemTriggers();
            return last;
        }

        /// <summary>Remove up to <paramref name="amount"/> of a type (any instances). Returns how many were removed.</summary>
        public int TakeItems(int typeId, int amount)
        {
            int taken = 0;
            for (int i = character.items.Count - 1; i >= 0 && taken < amount; i--)
            {
                var it = character.items[i];
                if (it.typeId != typeId) continue;
                int n = Mathf.Min(it.amount, amount - taken);
                RemoveFromStack(it, n);
                taken += n;
            }
            return taken;
        }

        /// <summary>Takes an item out of its equipment or back slot before it leaves the inventory.</summary>
        private void ReleaseItem(ItemSave it)
        {
            if (it.equipped)
            {
                it.equipped = false;
                Send(3, 6, character.name, it.id);
            }
            if (it.id == character.secondaryWeaponId) SetSecondaryWeapon(null);
        }

        private void RemoveFromStack(ItemSave it, int amount)
        {
            ReleaseItem(it);
            it.amount -= amount;
            if (it.amount <= 0) character.items.Remove(it);
            Send(4, 3, character.name, WireId(it), it.typeId, amount);
        }

        private void DestroyItem(int itemId, int typeId, int amount)
        {
            if (character == null) return;
            var it = ItemData.Get(typeId)?.Stacks == true ? FindStack(typeId) : FindItem(itemId);
            if (it == null || it.typeId != typeId) return;
            RemoveFromStack(it, Mathf.Clamp(amount, 1, it.amount));
        }

        private void Equip(int itemId)
        {
            var it = FindItem(itemId);
            var t = it != null ? ItemData.Get(it.typeId) : null;
            if (t?.slot == null) return;

            ItemSave replaced = null;
            foreach (var other in character.items)
            {
                if (other == it || !other.equipped) continue;
                if (ItemData.Get(other.typeId)?.slot == t.slot)
                {
                    other.equipped = false;
                    replaced = other;
                }
            }
            it.equipped = true;
            Send(8, 0, character.name, it.id, replaced != null ? replaced.id : 0);

            // A weapon replaced in hand goes on the back (the client's secondary weapon, swapped with the
            // "Switch weapon" key). The client has no other way to fill that slot, so the old server did this.
            if (t.slot != "Weapon") return;
            if (replaced != null) SetSecondaryWeapon(replaced);
            else if (it.id == character.secondaryWeaponId) SetSecondaryWeapon(null);
        }

        private ItemSave SecondaryWeapon()
        {
            var it = character.secondaryWeaponId != 0 ? FindItem(character.secondaryWeaponId) : null;
            return it != null && !it.equipped ? it : null;
        }

        /// <summary>8/19: the weapon on the player's back. The inventory hides that item while it's there.</summary>
        private void SetSecondaryWeapon(ItemSave it)
        {
            if (character == null) return;
            character.secondaryWeaponId = it != null ? it.id : 0;
            Send(8, 19, character.name, character.secondaryWeaponId, it != null ? it.typeId : 0);
        }

        /// <summary>6/5 "Switch weapon": the back weapon goes in hand, the one in hand (if any) on the back.</summary>
        private void SwitchWeapon()
        {
            var back = character != null ? SecondaryWeapon() : null;
            if (back == null) return;
            Equip(back.id);
        }

        private void Unequip(int itemId)
        {
            var it = FindItem(itemId);
            if (it == null || !it.equipped) return;
            it.equipped = false;
            Send(3, 6, character.name, it.id);
        }

        private void OnChangeTab(object[] c)
        {
            int itemId = (int)c[1], typeId = (int)c[2], tab = (int)c[3];
            var it = ItemData.Get(typeId)?.Stacks == true ? FindStack(typeId) : FindItem(itemId);
            if (it == null) return;
            it.tab = tab;
            Send(4, 2, character.name, itemId, typeId, tab);
        }

        private void OnSwapItems(object[] c)
        {
            int a = (int)c[1], b = (int)c[2];
            var items = character?.items;
            if (items == null || a < 0 || b < 0 || a >= items.Count || b >= items.Count) return;
            var tmp = items[a];
            items[a] = items[b];
            items[b] = tmp;
            Send(198, 37, a, b);
        }

        private void OnInsertItem(object[] c)
        {
            int from = (int)c[1], to = (int)c[2];
            var items = character?.items;
            if (items == null || from < 0 || from >= items.Count || to < 0 || to >= items.Count) return;
            var it = items[from];
            items.RemoveAt(from);
            items.Insert(to, it);
            Send(198, 38, from, to);
        }

        /// <summary>
        /// Sort (198/16 type, ascending): the client has already reordered its list when it sends this. Swaps
        /// and inserts are sent as list indexes, so adopt the client's order rather than re-sorting here
        /// (its name sort uses the client's string comparison, its price and weight come from its templates).
        /// </summary>
        private void MirrorClientOrder()
        {
            var items = character?.items;
            var client = Inventory.instance?.items;
            if (items == null || client == null) return;

            var sorted = new List<ItemSave>(items.Count);
            foreach (var stack in client)
            {
                int typeId = stack.item.itemTypeID, id = stack.item.itemID;
                var match = items.Find(it => it.typeId == typeId && (WireId(it) == 0 || it.id == id) && !sorted.Contains(it));
                if (match != null) sorted.Add(match);
            }
            int unmatched = 0;
            foreach (var it in items)
                if (!sorted.Contains(it)) { sorted.Add(it); unmatched++; }
            if (unmatched > 0 || client.Count != items.Count)
                Plugin.Log.LogWarning($"[inventory] sort: client has {client.Count} entries, server {items.Count}, {unmatched} unmatched kept at the end");
            items.Clear();
            items.AddRange(sorted);
        }

        /// <summary>Dev check: server vs client inventory and bank, row by row (order, wire id, amount).</summary>
        internal void CompareInventories()
        {
            if (character == null || Inventory.instance == null) return;
            Compare("inventory", character.items, Inventory.instance.items);
            Compare("bank", character.bank, Inventory.instance.bankitems);
        }

        private static void Compare(string what, List<ItemSave> server, List<Inventory.ItemStack> client)
        {
            int bad = 0;
            for (int i = 0; i < Mathf.Max(server.Count, client.Count); i++)
            {
                string s = i < server.Count ? $"{WireId(server[i])}:{server[i].typeId}x{server[i].amount}" : "-";
                string c = i < client.Count ? $"{client[i].item.itemID}:{client[i].item.itemTypeID}x{client[i].amount}" : "-";
                if (s != c) bad++;
                Plugin.Log.LogInfo($"[dev] {what} {i}: server {s} client {c}{(s != c ? "  MISMATCH" : "")}");
            }
            Plugin.Log.LogInfo($"[dev] {what}: {server.Count} server, {client.Count} client, {bad} mismatched rows");
        }

        public void SetSilver(int silver)
        {
            character.silver = Mathf.Max(0, silver);
            Send(3, 16, character.name, character.silver);
        }

        public void AddXp(int skillId, int amount)
        {
            if (character == null || amount <= 0 || !SkillById.TryGetValue(skillId, out var key)) return;
            int before = LevelFromXp(character.GetXp(key));
            character.SetXp(key, character.GetXp(key) + amount);
            Send(8, 1, character.name, skillId, amount);
            int after = LevelFromXp(character.GetXp(key));
            if (after > before && sceneReady) OnLevelUp(key, after);   // not for the starter kit's levels at creation
        }

        // Same curve as Scr_LevelsHandler; evaluated in float like the client so rounding agrees.
        internal static int LevelFromXp(int xp) => xp < 10 ? 1 : (int)(Mathf.Pow(xp / 0.5f, 0.26666668f) - 1f);

        internal static int XpForLevel(int level)
        {
            int xp = Mathf.RoundToInt(0.5f * Mathf.Pow(level + 1f, 3.75f));
            while (LevelFromXp(xp) < level) xp++;
            return xp;
        }

        private void GrantStarterKit()
        {
            foreach (int typeId in StarterClothes)
            {
                var it = GiveItem(typeId);
                if (it != null) Equip(it.id);
            }
            if (StyleWeapon.TryGetValue(character.fightingStyle, out int weapon))
            {
                var it = GiveItem(weapon);
                if (it != null) Equip(it.id);
            }
            if (ProfessionKit.TryGetValue(character.profession, out var kit))
                foreach (var entry in kit) GiveItem(entry[0], entry[1]);
            if (ProfessionSkill.TryGetValue(character.profession, out int skill))
            {
                string key = SkillById[skill];
                AddXp(skill, XpForLevel(5) - character.GetXp(key));
            }
        }
    }
}
