using System.Globalization;
using System.IO;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "gso.offline.server";
        public const string Name = "GSO Offline Server";
        public const string Version = PluginInfo.Version;

        internal static ManualLogSource Log;
        internal static string SaveDir;

        internal static ConfigEntry<float> NpcViewDistance;
        internal static ConfigEntry<float> HarvestableViewDistance;
        internal static ConfigEntry<bool> UnlockAllWayshrines;
        internal static ConfigEntry<bool> LogUnhandledEvents;
        internal static ConfigEntry<float> TimeOfDay;
        internal static ConfigEntry<string> AutoLogin;
        internal static ConfigEntry<string> AutoCharacter;
        internal static ConfigEntry<string> DevCommandFile;

        private void Awake()
        {
            Log = Logger;

            // The client parses server-provided numbers with Convert.ToDouble and splits
            // vectors on ',' so a comma-decimal locale would corrupt every position.
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            NpcViewDistance = Config.Bind("World", "NpcViewDistance", 120f, "Distance (m) at which NPCs are streamed to the client.");
            HarvestableViewDistance = Config.Bind("World", "HarvestableViewDistance", 90f, "Distance (m) at which harvestable nodes are streamed to the client.");
            UnlockAllWayshrines = Config.Bind("World", "UnlockAllWayshrines", true, "Give every character all wayshrines so the whole world is reachable.");
            TimeOfDay = Config.Bind("World", "StartTimeOfDay", 1000f, "Time of day sent on scene load (0-2400). Negative = leave client default.");
            LogUnhandledEvents = Config.Bind("Debug", "LogUnhandledEvents", true, "Log client->server events the offline server does not implement yet.");
            AutoLogin = Config.Bind("Convenience", "AutoLogin", "", "If set, log in automatically with this account name (skips the login screen).");
            DevCommandFile = Config.Bind("Debug", "DevCommandFile", "", "Developer automation: path (relative to the game folder) of a command file to poll. Empty = off. See DevBridge.cs.");
            AutoCharacter = Config.Bind("Convenience", "AutoCharacter", "", "If set together with AutoLogin, enter the world with this character (created if missing).");

            SaveDir = Path.Combine(Paths.GameRootPath, "OfflineSaves");
            Directory.CreateDirectory(SaveDir);

            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

            var host = new GameObject("GSOOfflineServer");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<OfflineServer>();
            if (DevCommandFile.Value.Trim().Length > 0)
                host.AddComponent<DevBridge>();

            Log.LogInfo($"{Name} {Version} loaded. Saves: {SaveDir}");
        }
    }
}
