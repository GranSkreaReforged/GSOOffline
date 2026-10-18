using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Items in the inventory and the bank wear their grade on their slot (#80): a frame round the icon in the
    /// grade's colour (the tooltip's name colours). Normal grade (2) has none, so better and worse items stand out.
    /// (The slot's own outline, which the client tints cyan on usable items, is too faint to carry a colour.)
    /// The windows draw their slots in a foreach over the item stacks: a transpiler notes each stack as the loop
    /// takes it, and the item icon's DrawTexture goes through <see cref="DrawIcon"/>, which adds the outline.
    /// </summary>
    internal static class GradeBorder
    {
        private const int NormalGrade = 2;
        private const float Edge = 2f;   // the frame's thickness

        private static Inventory.ItemStack current;

        public static Inventory.ItemStack Track(Inventory.ItemStack stack)
        {
            current = stack;
            return stack;
        }

        public static void DrawIcon(Rect r, Texture icon)
        {
            GUI.DrawTexture(r, icon);
            var item = current?.item;
            var wc = Script_WindowController.instance;
            if (item == null || wc == null || Event.current.type != EventType.Repaint) return;
            if (item.itemGrade <= 0 || item.itemGrade == NormalGrade) return;
            var old = GUI.color;
            GUI.color = Menucontroller.instance.GetGradeColor(item.itemGrade);
            var t = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, Edge), t);
            GUI.DrawTexture(new Rect(r.x, r.yMax - Edge, r.width, Edge), t);
            GUI.DrawTexture(new Rect(r.x, r.y + Edge, Edge, r.height - 2 * Edge), t);
            GUI.DrawTexture(new Rect(r.xMax - Edge, r.y + Edge, Edge, r.height - 2 * Edge), t);
            GUI.color = old;
        }

        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, string window)
        {
            var code = new List<CodeInstruction>(instructions);
            var getCurrent = AccessTools.PropertyGetter(typeof(List<Inventory.ItemStack>.Enumerator), "Current");
            var getIcon = AccessTools.Method(typeof(Inventory), nameof(Inventory.getItemIcon), new[] { typeof(int) });
            var drawTexture = AccessTools.Method(typeof(GUI), nameof(GUI.DrawTexture), new[] { typeof(Rect), typeof(Texture) });
            var track = AccessTools.Method(typeof(GradeBorder), nameof(Track));
            var draw = AccessTools.Method(typeof(GradeBorder), nameof(DrawIcon));
            int loops = 0, icons = 0;
            for (int i = 0; i < code.Count; i++)
            {
                // Only the first loop: the window's own list. The inventory's also has one over the trade window's
                // stacks inside it, which mustn't take over.
                if (loops == 0 && code[i].Calls(getCurrent))
                {
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Call, track));
                    loops++;
                }
                else if (code[i].Calls(drawTexture) && i > 0 && code[i - 1].Calls(getIcon))
                {
                    code[i] = new CodeInstruction(OpCodes.Call, draw).MoveLabelsFrom(code[i]);
                    icons++;
                }
            }
            if (loops == 0 || icons == 0)
                Plugin.Log.LogWarning($"[ui] {window}: {loops} item loops, {icons} item icons found; grade borders may be missing");
            return code;
        }
    }

    [HarmonyPatch(typeof(Script_WindowController), "WindowInventory")]
    internal static class GradeBorderInventoryPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            GradeBorder.Rewrite(instructions, "inventory");
    }

    [HarmonyPatch(typeof(Script_WindowController), "WindowBank")]
    internal static class GradeBorderBankPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            GradeBorder.Rewrite(instructions, "bank");
    }
}
