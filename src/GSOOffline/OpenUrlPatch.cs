using System;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// The menu's links (leaderboards, Discord, wiki...) use Application.OpenURL, which starts the browser as a
    /// child of GSO.exe. If no browser was open yet, that browser inherits the game's Steam environment and
    /// overlay, and Steam counts it as part of the game: after quitting, Steam still shows the game running and
    /// "Stop" hangs until the browser is closed. explorer.exe hands the link to the shell over COM and exits, so
    /// the browser starts outside the game's process tree.
    /// </summary>
    internal static class OpenUrlPatch
    {
        // Applied on its own: OpenURL is an internal call, and a failure must not stop the other patches.
        internal static void Apply(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(Application), nameof(Application.OpenURL)),
                    prefix: new HarmonyMethod(typeof(OpenUrlPatch), nameof(Prefix)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not patch Application.OpenURL; links will open as children of the game: {e.Message}");
            }
        }

        private static bool Prefix(string url)
        {
            if (url == null || url.IndexOf('"') >= 0) return true;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                using (Process.Start(new ProcessStartInfo("explorer.exe", "\"" + url + "\"") { UseShellExecute = false })) { }
                Plugin.Log.LogInfo($"Opened {url} through the shell.");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Opening {url} through the shell failed, using Unity: {e.Message}");
                return true;
            }
        }
    }
}
