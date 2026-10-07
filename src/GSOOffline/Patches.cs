using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    // Every client->server message funnels through this one method.
    [HarmonyPatch(typeof(Scr_RPCSender), nameof(Scr_RPCSender.RaiseEvent))]
    internal static class RaiseEventPatch
    {
        private static bool Prefix(byte eventType, object[] c)
        {
            OfflineServer.Instance?.Receive(eventType, c);
            return false;
        }
    }

    // Replaces the Photon connect loop and the server-heartbeat timeout.
    [HarmonyPatch(typeof(Scr_ConnectionHandler), "Update")]
    internal static class ConnectionUpdatePatch
    {
        private static bool Prefix(Scr_ConnectionHandler __instance)
        {
            if (!__instance.clientConnected && Time.timeSinceLevelLoad > 0.5f)
                OfflineServer.Instance?.FakeConnect(__instance);
            return false;
        }
    }

    // Polls server status and disconnects if we became Photon master client; both meaningless offline.
    [HarmonyPatch(typeof(Scr_ConnectionHandler), "ConnectionCheck")]
    internal static class ConnectionCheckPatch
    {
        private static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(Scr_ConnectionHandler), "LoadServerStatus")]
    internal static class LoadServerStatusPatch
    {
        private static bool Prefix(Scr_ConnectionHandler __instance, ref IEnumerator __result)
        {
            __instance.serverStatus = "Offline mode";
            __result = Empty();
            return false;
        }

        internal static IEnumerator Empty()
        {
            yield break;
        }
    }

    [HarmonyPatch(typeof(Scr_ConnectionHandler), "LoadIp")]
    internal static class LoadIpPatch
    {
        private static bool Prefix(Scr_ConnectionHandler __instance, ref IEnumerator __result)
        {
            __instance.serverIp = "127.0.0.1";
            __result = LoadServerStatusPatch.Empty();
            return false;
        }
    }

    [HarmonyPatch(typeof(Scr_ConnectionHandler), nameof(Scr_ConnectionHandler.JoinGame))]
    internal static class JoinGamePatch
    {
        private static bool Prefix(Scr_ConnectionHandler __instance)
        {
            OfflineServer.Instance?.FakeJoin(__instance);
            return false;
        }
    }

    // The LUI login window's "Server selection" button returns here; there is only one
    // (offline) server, so go straight back to the login window.
    [HarmonyPatch(typeof(Scr_UI_ServerSelection), nameof(Scr_UI_ServerSelection.Activate))]
    internal static class ServerSelectionActivatePatch
    {
        private static bool Prefix()
        {
            var ch = Scr_ConnectionHandler.instance;
            if (ch != null && ch.clientConnected)
                OfflineServer.Instance?.FakeJoin(ch);
            return false;
        }
    }

    [HarmonyPatch(typeof(Scr_UI_ServerSelection), nameof(Scr_UI_ServerSelection.Close))]
    internal static class ServerSelectionClosePatch
    {
        private static bool Prefix() => false;
    }
}
