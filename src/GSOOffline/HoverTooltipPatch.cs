using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// An item's tooltip stays up while the cursor moves over the same item (#81). The client hides tooltips until
    /// the mouse has been still for 0.2 s (Menucontroller.mouseStillTimer, reset by any movement), so every small
    /// move over an icon hid the tooltip and brought it back. The delay now applies only when the cursor comes to a
    /// different item, or comes back after leaving it.
    /// </summary>
    [HarmonyPatch(typeof(Menucontroller), nameof(Menucontroller.DrawHoverItem))]
    internal static class HoverTooltipPatch
    {
        // How long the tooltip counts as still showing after the last pass that drew it. The client draws it every
        // GUI pass while hovered, so this only bridges frames; leaving the item for longer brings the delay back.
        private const float Grace = 0.15f;

        private static Item shown;
        private static float shownAt = -1f;

        /// <summary>This pass draws the tooltip, as far as the delay goes (GearCompare draws its panel beside it).</summary>
        internal static bool ShowingNow { get; private set; }

        private static void Prefix(Menucontroller __instance, Item h, out float __state)
        {
            __state = __instance.mouseStillTimer;
            float now = Time.unscaledTime;
            if (h != null && h == shown && now - shownAt < Grace)
                __instance.mouseStillTimer = 0f;
            ShowingNow = __instance.mouseStillTimer <= 0f;
            if (ShowingNow)
            {
                shown = h;
                shownAt = now;
            }
        }

        // The timer also gates the hover texts ("Use x with y"), so it goes back to what it was.
        private static void Postfix(Menucontroller __instance, float __state) => __instance.mouseStillTimer = __state;
    }
}
