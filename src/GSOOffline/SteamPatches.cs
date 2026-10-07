using HarmonyLib;
using Steamworks;

namespace GSOOffline
{
    /// <summary>
    /// The client assumes Steamworks is up. If the game is started without Steam, these calls throw
    /// and abort whatever Unity callback made them (e.g. the LUI login window never gets its buttons).
    /// Make them inert when Steam is unavailable.
    /// </summary>
    internal static class SteamGuard
    {
        internal static bool Available
        {
            get
            {
                try
                {
                    return SteamManager.Initialized;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    [HarmonyPatch(typeof(SteamFriends), nameof(SteamFriends.GetPersonaName))]
    internal static class GetPersonaNamePatch
    {
        private static bool Prefix(ref string __result)
        {
            if (SteamGuard.Available) return true;
            __result = "Player";
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamUser), nameof(SteamUser.GetSteamID))]
    internal static class GetSteamIdPatch
    {
        private static bool Prefix(ref CSteamID __result)
        {
            if (SteamGuard.Available) return true;
            __result = CSteamID.Nil;
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamUser), nameof(SteamUser.GetAuthSessionTicket))]
    internal static class GetAuthSessionTicketPatch
    {
        private static bool Prefix(ref HAuthTicket __result, ref uint pcbTicket)
        {
            if (SteamGuard.Available) return true;
            pcbTicket = 0;
            __result = HAuthTicket.Invalid;
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamUser), nameof(SteamUser.CancelAuthTicket))]
    internal static class CancelAuthTicketPatch
    {
        private static bool Prefix() => SteamGuard.Available;
    }

    [HarmonyPatch(typeof(SteamUserStats), nameof(SteamUserStats.SetAchievement))]
    internal static class SetAchievementPatch
    {
        private static bool Prefix(ref bool __result)
        {
            __result = false;
            return SteamGuard.Available;
        }
    }

    [HarmonyPatch(typeof(SteamUserStats), nameof(SteamUserStats.StoreStats))]
    internal static class StoreStatsPatch
    {
        private static bool Prefix(ref bool __result)
        {
            __result = false;
            return SteamGuard.Available;
        }
    }
}
