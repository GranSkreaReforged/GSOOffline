using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// The wanted board's button reads "Abandon bounty" on an outlaw being hunted, and "Confirm abandon" once pressed
    /// (#86). The client draws one button per red "Outlaw" line, always "Add bounty", and sends the line's name with
    /// it (7/32); the server tells starting a hunt from giving one up. The button's row comes from its rect: the
    /// client puts row n's button at y = 60 + 90 n.
    /// </summary>
    internal static class BountyBoard
    {
        public static bool Button(Rect r, string text)
        {
            var list = Script_WindowController.instance?.window_wantedCriminals?.criminalsList;
            int row = Mathf.RoundToInt((r.y - 60f) / 90f);
            if (list != null && row >= 0 && row < list.Count)
            {
                string line = list[row].playerName ?? string.Empty;
                if (line.Contains("(" + OfflineServer.BountyHuntingTag + ","))
                {
                    string name = line.Substring(0, Mathf.Max(0, line.IndexOf(',')));
                    text = OfflineServer.AbandonArmedName == name ? "Confirm abandon" : "Abandon bounty";
                }
            }
            return GUI.Button(r, text);
        }

        internal static IEnumerable<CodeInstruction> Relabel(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var button = AccessTools.Method(typeof(GUI), nameof(GUI.Button), new[] { typeof(Rect), typeof(string) });
            int replaced = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (!code[i].Calls(button)) continue;
                code[i] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BountyBoard), nameof(Button))).MoveLabelsFrom(code[i]);
                replaced++;
            }
            if (replaced != 1)
                Plugin.Log.LogWarning($"[bounty] wanted board: {replaced} buttons found, expected 1; hunted outlaws may show \"Add bounty\"");
            return code;
        }
    }

    [HarmonyPatch(typeof(Script_WindowController), "WindowWantedBoard")]
    internal static class BountyBoardPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            BountyBoard.Relabel(instructions);
    }
}
