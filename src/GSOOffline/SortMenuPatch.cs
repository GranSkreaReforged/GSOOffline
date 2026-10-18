using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// The inventory's and the bank's Sort menu take their own clicks (#78). The menu is drawn last in its window,
    /// over the silver button (deposit in the inventory while the bank is open, withdraw in the bank) and the bank's
    /// search field, and in the IMGUI the control drawn first takes the click: "By name" opened the silver dialog,
    /// and the bank's weight buttons focused the search field. While the menu is open, the controls drawn before it
    /// are moved off-screen for mouse events over the menu, so they still take their control ids (keeping the ids of
    /// the controls after them) but no longer see the click. They draw as before.
    /// </summary>
    internal static class SortMenuGuard
    {
        private static readonly AccessTools.FieldRef<Script_WindowController, List<Script_Window>> Windows =
            AccessTools.FieldRefAccess<Script_WindowController, List<Script_Window>>("windows");

        private static readonly Rect Nowhere = new Rect(-10000f, -10000f, 0f, 0f);

        private static Script_WindowController controller;
        private static Script_Window window;
        private static bool bank;

        internal static void Begin(Script_WindowController wc, int id, bool isBank)
        {
            controller = wc;
            // `id` is the window's place among the open windows (windowUniqueId), not its index in the list.
            window = null;
            foreach (var w in Windows(wc))
                if (w.windowActive && w.windowUniqueId == id) { window = w; break; }
            bank = isBank;
        }

        private static Rect Shield(Rect r)
        {
            if (controller == null || window == null || !Event.current.isMouse) return r;
            bool open = bank ? controller.window_Bank.showSortMenu : controller.window_Inventory.showSortMenu;
            if (!open) return r;
            // Where the client draws the menu (the Sort button's corner, 200 x 100).
            var menu = new Rect(50f, window.windowRect.height - 100f, 200f, 100f);
            return menu.Contains(Event.current.mousePosition) ? Nowhere : r;
        }

        public static bool Button(Rect r, string text, GUIStyle style) => GUI.Button(Shield(r), text, style);

        public static string TextField(Rect r, string text) => GUI.TextField(Shield(r), text);

        /// <summary>Points the buttons and text fields drawn before the menu's block at the guarded versions.</summary>
        internal static IEnumerable<CodeInstruction> Guard(IEnumerable<CodeInstruction> instructions, string window)
        {
            var code = new List<CodeInstruction>(instructions);
            var button = AccessTools.Method(typeof(GUI), nameof(GUI.Button), new[] { typeof(Rect), typeof(string), typeof(GUIStyle) });
            var field = AccessTools.Method(typeof(GUI), nameof(GUI.TextField), new[] { typeof(Rect), typeof(string) });

            // The menu's block: `if (window_X.showSortMenu)`, the first test of the flag that skips ahead when false
            // (the Sort button's `!showSortMenu && ...` branches the other way).
            int menu = -1;
            for (int i = 0; i + 1 < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo f && f.Name == "showSortMenu"
                    && (code[i + 1].opcode == OpCodes.Brfalse || code[i + 1].opcode == OpCodes.Brfalse_S))
                {
                    menu = i;
                    break;
                }
            }
            int replaced = 0;
            for (int i = 0; i < menu; i++)
            {
                if (code[i].Calls(button))
                {
                    code[i] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SortMenuGuard), nameof(Button))).MoveLabelsFrom(code[i]);
                    replaced++;
                }
                else if (code[i].Calls(field))
                {
                    code[i] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SortMenuGuard), nameof(TextField))).MoveLabelsFrom(code[i]);
                    replaced++;
                }
            }
            if (menu < 0 || replaced == 0)
                Plugin.Log.LogWarning($"[ui] {window}: sort menu not found, its clicks can still reach the controls under it");
            return code;
        }
    }

    [HarmonyPatch(typeof(Script_WindowController), "WindowInventory")]
    internal static class SortMenuInventoryPatch
    {
        private static void Prefix(Script_WindowController __instance, int id) => SortMenuGuard.Begin(__instance, id, false);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            SortMenuGuard.Guard(instructions, "inventory");
    }

    [HarmonyPatch(typeof(Script_WindowController), "WindowBank")]
    internal static class SortMenuBankPatch
    {
        private static void Prefix(Script_WindowController __instance, int id) => SortMenuGuard.Begin(__instance, id, true);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            SortMenuGuard.Guard(instructions, "bank");
    }
}
