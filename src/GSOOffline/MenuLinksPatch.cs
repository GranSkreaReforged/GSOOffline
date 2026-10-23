using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Links to the shut-down game's services. The item windows' "Wiki" button opened wiki.granskreaonline.com,
    /// which is gone, so it isn't drawn: here in the shop/recipe item window, and in the bag item window
    /// ItemDropPatch draws Destroy in its place. The menu's "Report a bug or give feedback" button opened a form
    /// that sent to the old server; it opens GSO Offline's GitHub issue page instead.
    /// </summary>
    internal static class MenuLinks
    {
        internal const string BugReportUrl = "https://github.com/GranSkreaReforged/GSOOffline/issues/new?labels=bug";

        private const int WindowFeedback = 22;

        // Stands in for GUILayout.Button("Wiki"): nothing drawn, never clicked.
        public static bool NoButton(string text, GUILayoutOption[] options) => false;

        internal static IEnumerable<CodeInstruction> HideWiki(IEnumerable<CodeInstruction> instructions, string window)
        {
            var code = new List<CodeInstruction>(instructions);
            var button = AccessTools.Method(typeof(GUILayout), nameof(GUILayout.Button), new[] { typeof(string), typeof(GUILayoutOption[]) });
            int replaced = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldstr || (code[i].operand as string) != "Wiki") continue;
                // ldstr "Wiki"; ldc.i4.0; newarr GUILayoutOption; call GUILayout.Button
                for (int j = i + 1; j < code.Count && j <= i + 4; j++)
                {
                    if (!code[j].Calls(button)) continue;
                    code[j] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MenuLinks), nameof(NoButton))).MoveLabelsFrom(code[j]);
                    replaced++;
                    break;
                }
            }
            if (replaced == 0) Plugin.Log.LogWarning($"[ui] {window}: Wiki button not found, it still shows");
            return code;
        }

        internal static bool OpenBugReport(int type)
        {
            if (type != WindowFeedback) return true;
            Application.OpenURL(BugReportUrl);
            return false;
        }
    }

    [HarmonyPatch(typeof(Script_WindowController), "WindowInfoBasicItem")]
    internal static class WikiButtonBasicItemPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            MenuLinks.HideWiki(instructions, "basic item info");
    }

    [HarmonyPatch(typeof(Script_WindowController), nameof(Script_WindowController.ToggleWindowType))]
    internal static class BugReportLinkPatch
    {
        private static bool Prefix(int type) => MenuLinks.OpenBugReport(type);
    }
}
