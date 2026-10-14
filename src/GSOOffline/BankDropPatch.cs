using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Dragging an inventory item onto the bank window deposits it, as double-clicking it does: a single item goes
    /// straight in, a stack asks how many, and Ctrl deposits the whole stack. The client only let items be dragged
    /// within the inventory and onto the item hotbar.
    /// Script_WindowController.Update ends the drag on mouse release, before the windows are drawn that frame, so
    /// the released item is noted first and the bank window (drawing in its own, scaled, coordinates) checks
    /// whether the mouse let go over it.
    /// </summary>
    internal static class BankDrop
    {
        private const int WindowBank = 14;

        private static readonly AccessTools.FieldRef<Script_WindowController, List<Script_Window>> Windows =
            AccessTools.FieldRefAccess<Script_WindowController, List<Script_Window>>("windows");

        private static Inventory.ItemStack released;
        private static int releasedFrame = -1;

        internal static void NoteRelease(Script_WindowController wc)
        {
            var d = wc.draggingItem;
            if (d == null || !d.active || !d.isItem || d.stack == null || !Input.GetMouseButtonUp(0)) return;
            released = d.stack;
            releasedFrame = Time.frameCount;
        }

        internal static void TryDeposit(Script_WindowController wc)
        {
            if (released == null || releasedFrame != Time.frameCount) return;
            Script_Window bank = Windows(wc).Find(w => w.windowType == WindowBank && w.windowActive);
            if (bank == null || !new Rect(0f, 0f, bank.windowRect.width, bank.windowRect.height).Contains(Event.current.mousePosition)) return;
            var stack = released;
            released = null;
            if (stack.item == null || !Inventory.instance.items.Contains(stack)) return;   // not from the bag

            var item = stack.item;
            var sender = Scr_ConnectionHandler.instance.rpcSender;
            if (stack.amount == 1)
                sender.addBankItem(item.itemID, item.itemTypeID, 1);
            else if (Input.GetKey(KeyCode.LeftControl))
                sender.addBankItem(item.itemID, item.itemTypeID, stack.amount);
            else
            {
                wc.window_amountConfirmation.item = item;
                wc.createAmountConfirmation(9, "Amount confirmation", "How many would you like to deposit?", 1, stack.amount, stack.amount);
            }
            Plugin.Log.LogInfo($"[bank] dropped {item.itemName} x{stack.amount} on the bank");
        }
    }

    [HarmonyPatch(typeof(Script_WindowController), "Update")]
    internal static class BankDropReleasePatch
    {
        private static void Prefix(Script_WindowController __instance) => BankDrop.NoteRelease(__instance);
    }

    [HarmonyPatch(typeof(Script_WindowController), "WindowBank")]
    internal static class BankDropWindowPatch
    {
        private static void Prefix(Script_WindowController __instance) => BankDrop.TryDeposit(__instance);
    }
}
