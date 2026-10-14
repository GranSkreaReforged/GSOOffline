using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Escape closes the crafting window (any workbench), as its Close button does. The crafting window isn't one of
    /// Script_WindowController's windows, so Escape skipped it and opened the game menu on top instead (the original
    /// game did the same). Like the Close button, it does nothing while a craft is in progress.
    /// </summary>
    [HarmonyPatch(typeof(Scr_KeyboardInputs), nameof(Scr_KeyboardInputs.GetKeyDown))]
    internal static class WorkbenchEscapePatch
    {
        // Escape is read more than once a frame (the window controller, the LUI handler); the press that closed the
        // workbench mustn't also open the menu.
        private static int usedFrame = -1;

        private static void Postfix(string key, ref bool __result)
        {
            if (!__result || key != "Escape") return;
            if (Time.frameCount == usedFrame)
            {
                __result = false;
                return;
            }
            var mc = Menucontroller.instance;
            if (mc == null || mc.showWorkbench == 0 || mc.craftingInProgress) return;
            mc.clearWorkbench();
            mc.showWorkbench = 0;
            usedFrame = Time.frameCount;
            __result = false;
        }
    }
}
