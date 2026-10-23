using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// A "Quick deposit" button in the bank window (#114): every stack in the bag whose item the bank already holds
    /// a stack of goes into the bank whole, through the normal deposit message. Items on the item hotbar and equipped
    /// items stay in the bag, and nothing new is added to the bank, so the bank's item count never changes.
    /// </summary>
    internal static class QuickDeposit
    {
        private const int WindowBank = 14;

        private static readonly AccessTools.FieldRef<Script_WindowController, List<Script_Window>> Windows =
            AccessTools.FieldRefAccess<Script_WindowController, List<Script_Window>>("windows");

        internal static void DrawButton(Script_WindowController wc)
        {
            Script_Window bank = Windows(wc).Find(w => w.windowType == WindowBank && w.windowActive);
            if (bank == null) return;
            // Bottom row, right of the "Items: n/150" label and clear of the resize corner.
            var r = new Rect(bank.windowRect.width - 170f, bank.windowRect.height - 88f, 110f, 25f);
            if (GUI.Button(r, "Quick deposit", wc.skin.GetStyle("ShortButton"))) Deposit(wc);
        }

        internal static void Deposit(Script_WindowController wc)
        {
            var inv = Inventory.instance;
            var kept = new HashSet<int>();
            foreach (var item in wc.window_itemHotkeys.items)
                if (item != null) kept.Add(item.itemTypeID);
            foreach (var slot in inv.equipSlots)
                if (slot?.item != null) kept.Add(slot.item.itemTypeID);

            var banked = new HashSet<int>();
            foreach (var s in inv.bankitems)
                if (s.item != null && s.item.itemID == 0) banked.Add(s.item.itemTypeID);

            var sender = Scr_ConnectionHandler.instance.rpcSender;
            int stacks = 0;
            foreach (var s in new List<Inventory.ItemStack>(inv.items))
            {
                // Stacks are id 0 on the wire; single items never merge with a bank stack.
                if (s.item == null || s.item.itemID != 0 || s.amount <= 0) continue;
                int type = s.item.itemTypeID;
                if (!banked.Contains(type) || kept.Contains(type)) continue;
                sender.addBankItem(0, type, s.amount);
                stacks++;
            }
            Plugin.Log.LogInfo($"[bank] quick deposit: {stacks} stacks");
        }
    }

    [HarmonyPatch(typeof(Script_WindowController), "WindowBank")]
    internal static class QuickDepositWindowPatch
    {
        private static void Prefix(Script_WindowController __instance) => QuickDeposit.DrawButton(__instance);
    }
}
