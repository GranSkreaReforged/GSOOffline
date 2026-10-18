using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Comparing gear (#77):
    /// - Hovering gear that isn't worn shows a second panel beside the tooltip: what changes against the piece worn
    ///   in that slot, stat by stat (with grades applied, as the tooltip shows them), green where the hovered piece
    ///   is better and red where it's worse, and the weight.
    /// - The character window's equipped slots show the item tooltip on hover. The client only drew tooltips over
    ///   the inventory, bank, trade and two shop windows, and the slots never set the hovered item.
    /// </summary>
    internal static class GearCompare
    {
        private const int WindowCharacter = 6, WindowInventory = 2;
        private const int SecondaryWeaponSlot = 12;
        private const float Width = 200f, Line = 25f;

        // The tooltip's stats, in its order (the index is the stat's icon in smallStatIcons).
        private static readonly KeyValuePair<string, Func<Item, int>>[] Stats =
        {
            S("Health", i => i.itemStatHealth), S("Health regen", i => i.itemStatHealthregen), S("Damage", i => i.itemStatDamage),
            S("Movement speed", i => i.itemStatMovementspeed), S("Cooldowns", i => i.itemStatCooldowns), S("Stability", i => i.itemStatStability),
            S("Mana", i => i.itemStatMana), S("Mana regen", i => i.itemStatManaregen), S("Damage block", i => i.itemStatDamageBlock),
            S("Healing", i => i.itemStatHealing), S("Warmth", i => i.itemStatWarmth), S("Carrying capacity", i => i.itemStatCarryingCap),
            S("Poison damage", i => i.itemStatPoisonDamage), S("% Poison resistance", i => i.itemStatPoisonResistance), S("Range", i => i.itemStatRange),
            S("Arcane damage", i => i.itemStatDamageArcane), S("% Arcane resistance", i => i.itemStatResistancArcane), S("Fire damage", i => i.itemStatDamageFire),
            S("Earth damage", i => i.itemStatDamageEarth), S("Wind damage", i => i.itemStatDamageWind), S("Electric damage", i => i.itemStatDamageElectric),
            S("Psychic damage", i => i.itemStatDamagePsychic), S("Ice damage", i => i.itemStatDamageIce), S("% Fire resistance", i => i.itemStatResistanceFire),
            S("% Earth resistance", i => i.itemStatResistanceEarth), S("% Wind resistance", i => i.itemStatResistanceWind), S("% Electric resistance", i => i.itemStatResistanceElectric),
            S("% Psychic resistance", i => i.itemStatResistancePsychic), S("% Ice resistance", i => i.itemStatResistanceIce), S("Slash damage", i => i.itemStatDamageSlash),
            S("Blunt damage", i => i.itemStatDamageBlunt), S("Piercing damage", i => i.itemStatDamagePiercing), S("% Slash resistance", i => i.itemStatResistanceSlash),
            S("% Blunt resistance", i => i.itemStatResistanceBlunt), S("% Piercing resistance", i => i.itemStatResistancePiercing),
        };

        private static KeyValuePair<string, Func<Item, int>> S(string name, Func<Item, int> get) => new KeyValuePair<string, Func<Item, int>>(name, get);

        /// <summary>The tooltip's window check: the character window counts as well.</summary>
        public static bool OverTooltipWindow(Script_WindowController wc, int type) =>
            wc.isHoveringOverWindow(type) || (type == WindowInventory && wc.isHoveringOverWindow(WindowCharacter));

        internal static IEnumerable<CodeInstruction> AllowCharacterWindow(IEnumerable<CodeInstruction> instructions)
        {
            var hovering = AccessTools.Method(typeof(Script_WindowController), nameof(Script_WindowController.isHoveringOverWindow), new[] { typeof(int) });
            var wrapper = AccessTools.Method(typeof(GearCompare), nameof(OverTooltipWindow));
            int replaced = 0;
            foreach (var ci in instructions)
            {
                if (ci.Calls(hovering))
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call, wrapper).MoveLabelsFrom(ci);
                }
                else
                    yield return ci;
            }
            if (replaced == 0)
                Plugin.Log.LogWarning("[ui] item tooltip: window check not found; equipped gear shows no tooltip");
        }

        /// <summary>After the character window: the equipped slot under the mouse is the hovered item.</summary>
        internal static void HoverEquipped(Script_WindowController wc, int id)
        {
            var mc = Menucontroller.instance;
            var inv = Inventory.instance;
            Script_Window w = null;
            foreach (var o in Traverse.Create(wc).Field("windows").GetValue<List<Script_Window>>())
                if (o.windowActive && o.windowUniqueId == id) { w = o; break; }
            if (mc == null || inv == null || w == null) return;
            // The slots sit in the right-hand scroll view (Script_WindowController.WindowCharacter).
            var view = new Rect(50f + w.windowRect.width / 2f - 95f, 100f, 345f, 340f);
            Vector2 mouse = Event.current.mousePosition;
            if (!view.Contains(mouse)) return;
            Vector2 local = mouse - view.position + wc.window_Character.scrollPos_rightMenu;
            for (int n = 0; n < inv.equipSlots.Count; n++)
            {
                Vector2 at = n < 5 ? new Vector2(10f, 40 + n * 41) : n < 10 ? new Vector2(295f, 40 + (n - 5) * 41) : new Vector2(113 + (n - 10) * 41, 255f);
                if (!new Rect(at.x, at.y, 40f, 40f).Contains(local)) continue;
                var item = n == SecondaryWeaponSlot ? inv.GetItem(Scr_PlayerHandler.instance.player.secondaryWeaponId) : inv.getEquippedItem(n);
                if (item != null && item.itemTypeID != 0) mc.hoverItem = item;
                return;
            }
        }

        /// <summary>After the tooltip: the comparison panel beside it, for gear not being worn.</summary>
        internal static void DrawComparison(Menucontroller mc)
        {
            var h = mc.hoverItem;
            var wc = Script_WindowController.instance;
            if (h == null || wc == null || h.itemTypeID == 0 || h.itemTypeID >= 10000 || h.currentlyEquipped) return;
            if (!HoverTooltipPatch.ShowingNow || Inventory.instance.usingItem) return;   // the tooltip itself is up
            if (!(OverTooltipWindow(wc, 2) || wc.isHoveringOverWindow(31) || wc.isHoveringOverWindow(14) || wc.isHoveringOverWindow(10) || wc.isHoveringOverWindow(41))) return;
            var worn = Inventory.instance.getEquippedItem(h.itemSlot);
            if (worn == h) return;

            // Where the client put the tooltip (Menucontroller.DrawHoverItem), in GUI coordinates.
            float scale = GUI.matrix.m00 > 0f ? GUI.matrix.m00 : 1f;
            float guiW = Screen.width / scale, guiH = Screen.height / (GUI.matrix.m11 > 0f ? GUI.matrix.m11 : 1f);
            Vector2 mouse = Event.current.mousePosition;
            int statLines = 0;
            foreach (var s in Stats) if (s.Value(h) != 0 && statLines < 10) statLines++;
            float need = statLines * 25f + 210f;
            float tipX = mouse.x > guiW - 200f ? guiW - 200f : mouse.x + 30f;
            float tipY = guiH - mouse.y < need ? mouse.y - (need - (guiH - mouse.y)) : mouse.y;
            float x = tipX + Width + 5f + Width <= guiW ? tipX + Width + 5f : tipX - Width - 5f;
            float y = tipY + 5f;

            GUI.DrawTexture(new Rect(x, y, Width, Line), mc.getOtherIcon(42));
            mc.drawGUITextWhite(x + 10f, y + 6f, "Compared with what you wear:");
            y += Line;
            if (worn == null || worn.itemTypeID == 0)
            {
                Row(mc, x, ref y, "Nothing worn there", Color.white);
                return;
            }
            Row(mc, x, ref y, worn.itemName, mc.GetGradeColor(worn.itemGrade));
            float hm = mc.GetGradeMultiplier(h.itemGrade), wm = mc.GetGradeMultiplier(worn.itemGrade);
            int shown = 0;
            for (int j = 0; j < Stats.Length; j++)
            {
                int diff = (int)(Stats[j].Value(h) * hm) - (int)(Stats[j].Value(worn) * wm);
                if (diff == 0) continue;
                GUI.DrawTexture(new Rect(x, y, Width, Line), mc.getOtherIcon(43));
                var icons = mc.icons.stats.smallStatIcons;
                if (icons != null && j < icons.Length) GUI.DrawTexture(new Rect(x + 8f, y, 16f, 16f), icons[j]);
                mc.drawGUITextWhite(x + 31f, y, (diff > 0 ? "+" : string.Empty) + diff + " " + Stats[j].Key, diff > 0 ? Color.green : Color.red);
                y += Line;
                shown++;
            }
            float weight = h.itemWeight - worn.itemWeight;
            if (Mathf.Abs(weight) >= 0.05f)
            {
                Row(mc, x, ref y, (weight > 0 ? "+" : string.Empty) + weight.ToString("0.#") + " Weight", weight < 0 ? Color.green : Color.red);
                shown++;
            }
            if (shown == 0) Row(mc, x, ref y, "No difference", Color.white);
        }

        private static void Row(Menucontroller mc, float x, ref float y, string text, Color color)
        {
            GUI.DrawTexture(new Rect(x, y, Width, Line), mc.getOtherIcon(43));
            mc.drawGUITextWhite(x + 10f, y + 3f, text, color);
            y += Line;
        }
    }

    [HarmonyPatch(typeof(Menucontroller), nameof(Menucontroller.DrawHoverItem))]
    internal static class GearCompareTooltipPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            GearCompare.AllowCharacterWindow(instructions);

        private static void Postfix(Menucontroller __instance) => GearCompare.DrawComparison(__instance);
    }

    [HarmonyPatch(typeof(Script_WindowController), "WindowCharacter")]
    internal static class GearCompareCharacterPatch
    {
        private static void Postfix(Script_WindowController __instance, int id) => GearCompare.HoverEquipped(__instance, id);
    }
}
