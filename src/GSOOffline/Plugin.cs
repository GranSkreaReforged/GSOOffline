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
        internal static ConfigEntry<string> SeasonalEvents;
        internal static ConfigEntry<string> AutoLogin;
        internal static ConfigEntry<string> AutoCharacter;
        internal static ConfigEntry<bool> ShowVersion;
        internal static ConfigEntry<bool> CheckForUpdates;
        internal static ConfigEntry<bool> GearEffects;

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
            TimeOfDay = Config.Bind("World", "StartTimeOfDay", 1000f, "Time of day when you enter the world (0-2400); the day/night cycle runs on from there. Negative = leave client default.");
            SeasonalEvents = Config.Bind("World", "SeasonalEvents", "Auto", new ConfigDescription(
                "Holiday events (creatures drop Halloween or Christmas crates). Auto: Halloween from 15 October to 7 November, Christmas from 1 December to 6 January, by this computer's date.",
                new AcceptableValueList<string>("Auto", "Off", "Halloween", "Christmas", "Both")));
            GearEffects = Config.Bind("World", "GearEffects", true, "Worn gear of Rare grade and better shimmers in its grade's colour, more the better the grade.");
            LogUnhandledEvents = Config.Bind("Debug", "LogUnhandledEvents", true, "Log client->server events the offline server does not implement yet.");
            AutoLogin = Config.Bind("Convenience", "AutoLogin", "", "If set, log in automatically with this account name (skips the login screen).");
            AutoCharacter = Config.Bind("Convenience", "AutoCharacter", "", "If set together with AutoLogin, enter the world with this character (created if missing).");
            ShowVersion = Config.Bind("Updates", "ShowVersion", true, "Show the mod's version in the top-left corner.");
            CheckForUpdates = Config.Bind("Updates", "CheckForUpdates", true, "At startup, ask GitHub for the latest release and add \"NEW UPDATE AVAILABLE\" to the version line if there is a newer one. Only the version is read; nothing is downloaded.");

            SaveDir = Path.Combine(Paths.GameRootPath, "OfflineSaves");
            Directory.CreateDirectory(SaveDir);

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            OpenUrlPatch.Apply(harmony);

            var host = new GameObject("GSOOfflineServer");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<OfflineServer>();
            if (ShowVersion.Value)
                UpdateOverlay.Create(host, Name, Version, PluginInfo.ReleaseBuild, "GSOOffline", Guid, CheckForUpdates.Value);

            Log.LogInfo($"{Name} {PluginInfo.BuildVersion} loaded{(PluginInfo.ReleaseBuild ? "" : " (dev build)")}. Saves: {SaveDir}");
        }
    }
}
