using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Dropping and destroying items from the inventory (#79):
    /// - Dragging an inventory item out of the windows and letting go over the world drops it, after the client's
    ///   own "Are you sure you want to drop ...? How many?" confirmation (the same as Shift+click).
    /// - The item window gets a Destroy button next to Drop, in place of the dead Wiki link (see MenuLinksPatch),
    ///   with the same kind of confirmation. Confirming sends
    ///   the client's own destroy message (4/4), which the server always handled; nothing in the client used it.
    /// </summary>
    internal static class ItemDrop
    {
        // A window id (Script_Window.windowId) the client's amountConfirmation doesn't use.
        internal const int DestroyConfirmation = 90;

        private static readonly AccessTools.FieldRef<Script_WindowController, List<Script_Window>> Windows =
            AccessTools.FieldRefAccess<Script_WindowController, List<Script_Window>>("windows");
        private static readonly System.Action<Script_WindowController, Item> CreateDropConfirmation =
            AccessTools.MethodDelegate<System.Action<Script_WindowController, Item>>(AccessTools.Method(typeof(Script_WindowController), "createDestroyConfirmation"));
        private static readonly System.Action<Script_WindowController, int> CloseWindow =
            AccessTools.MethodDelegate<System.Action<Script_WindowController, int>>(AccessTools.Method(typeof(Script_WindowController), "closeWindowId"));

        // The item window being drawn (WindowInfoItem's id, as the client itself looks it up).
        private static Script_Window infoWindow;

        /// <summary>Before Script_WindowController.Update ends the drag: an inventory item let go outside every window.</summary>
        internal static void CheckDragOut(Script_WindowController wc)
        {
            var d = wc.draggingItem;
            if (d == null || !d.active || !d.isItem || d.stack == null || d.item == null || !Input.GetMouseButtonUp(0)) return;
            if (!Inventory.instance.items.Contains(d.stack) || wc.isHoveringOverWindow()) return;
            if (d.item.currentlyEquipped) return;
            CreateDropConfirmation(wc, d.item);
        }

        internal static void BeginInfoWindow(Script_WindowController wc, int id)
        {
            var list = Windows(wc);
            infoWindow = id >= 0 && id < list.Count ? list[id] : null;
        }

        /// <summary>Stands in for the item window's Wiki button: Destroy, for an item in the bag; the Wiki button is gone.</summary>
        public static bool DestroyInsteadOfWiki(string text, GUILayoutOption[] options)
        {
            var wc = Script_WindowController.instance;
            var item = infoWindow?.windowItem;
            if (wc != null && item != null && InBag(item, out int amount) && GUILayout.Button("Destroy", options))
            {
                CloseWindow(wc, infoWindow.windowUniqueId);
                wc.window_amountConfirmation.item = item;
                wc.createAmountConfirmation(DestroyConfirmation, "Confirmation",
                    "Are you sure you want to destroy " + item.itemName + "? It will be gone for good.\nHow many?", 1, amount, amount);
            }
            return false;
        }

        private static bool InBag(Item item, out int amount)
        {
            amount = 0;
            foreach (var s in Inventory.instance.items)
                if (s.item.itemTypeID == item.itemTypeID && (item.itemID == 0 || item.itemID == s.item.itemID))
                    amount = s.amount;
            return amount > 0;
        }

        /// <summary>The Destroy confirmation's Confirm: true if it was ours.</summary>
        internal static bool Confirm(Script_WindowController wc, int id)
        {
            if (id != DestroyConfirmation) return false;
            var c = wc.window_amountConfirmation;
            if (c.item != null && c.currentAmount > 0)
                Scr_ConnectionHandler.instance.rpcSender.removeItem(c.item.itemID, c.item.itemTypeID, c.currentAmount, Scr_PlayerHandler.instance.player.playerName);
            return true;
        }

        internal static IEnumerable<CodeInstruction> AddDestroyButton(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var button = AccessTools.Method(typeof(GUILayout), nameof(GUILayout.Button), new[] { typeof(string), typeof(GUILayoutOption[]) });
            int replaced = 0;
            for (int i = 0; i + 2 < code.Count; i++)
            {
                // ldstr "Wiki"; (the options array); call GUILayout.Button
                if (code[i].opcode != OpCodes.Ldstr || (code[i].operand as string) != "Wiki") continue;
                for (int j = i + 1; j < code.Count && j <= i + 6; j++)
                {
                    if (!code[j].Calls(button)) continue;
                    code[j] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ItemDrop), nameof(DestroyInsteadOfWiki))).MoveLabelsFrom(code[j]);
                    replaced++;
                    break;
                }
            }
            if (replaced != 1)
                Plugin.Log.LogWarning($"[inventory] item window: {replaced} Wiki buttons found, expected 1; the Destroy button may be missing");
            return code;
        }
    }

    [HarmonyPatch(typeof(Script_WindowController), "Update")]
    internal static class ItemDragOutPatch
    {
        private static void Prefix(Script_WindowController __instance) => ItemDrop.CheckDragOut(__instance);
    }

    [HarmonyPatch(typeof(Script_WindowController), "WindowInfoItem")]
    internal static class ItemDestroyButtonPatch
    {
        private static void Prefix(Script_WindowController __instance, int id) => ItemDrop.BeginInfoWindow(__instance, id);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            ItemDrop.AddDestroyButton(instructions);
    }

    [HarmonyPatch(typeof(Script_WindowController), "amountConfirmation")]
    internal static class ItemDestroyConfirmPatch
    {
        private static bool Prefix(Script_WindowController __instance, int id) => !ItemDrop.Confirm(__instance, id);
    }
}
