using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Shop and bank windows close when the player walks away or changes zone, as crafting windows do. The client
    /// keeps them open until the player closes them, so a shop or the bank could be used from anywhere.
    /// </summary>
    public partial class OfflineServer
    {
        private const int WindowShop = 7;   // WindowBank (14) is in Travel.cs
        private const float WindowLeaveDistance = 1f;

        private static readonly AccessTools.FieldRef<Script_WindowController, List<Script_Window>> ClientWindows =
            AccessTools.FieldRefAccess<Script_WindowController, List<Script_Window>>("windows");

        private int openWindow;          // the shop or bank window the server opened, or 0
        private Vector3 windowOpenedAt;
        private bool windowSeen;         // the client has shown it (the open message is delivered after this)
        private float windowSentAt;

        private void WindowOpened(int type)
        {
            var player = LocalPlayer;
            openWindow = type;
            windowOpenedAt = player != null ? player.transform.position : Vector3.zero;
            windowSeen = false;
            windowSentAt = Time.time;
        }

        private void TickWindows()
        {
            if (openWindow == 0) return;
            bool open = ClientWindowOpen(openWindow);
            if (!windowSeen)
            {
                windowSeen = open;
                if (!open && Time.time > windowSentAt + 2f) ForgetWindow();   // never showed (the bank toggled shut)
                if (!open) return;
            }
            if (!open)
            {
                ForgetWindow();   // the player closed it
                return;
            }
            var player = LocalPlayer;
            if (player == null || !sceneReady || Vector3.Distance(player.transform.position, windowOpenedAt) > WindowLeaveDistance)
                CloseOpenWindow();
        }

        /// <summary>Closes the shop or bank window, as its close button would.</summary>
        private void CloseOpenWindow()
        {
            if (openWindow == 0) return;
            int type = openWindow;
            ForgetWindow();
            if (ClientWindowOpen(type)) Script_WindowController.instance.closeWindowType(type);
            Plugin.Log.LogInfo($"[travel] closed window {type}: player left");
        }

        private void ForgetWindow()
        {
            if (openWindow == WindowShop) openShop = null;
            openWindow = 0;
        }

        private static bool ClientWindowOpen(int type)
        {
            var wc = Script_WindowController.instance;
            if (wc == null) return false;
            foreach (var w in ClientWindows(wc))
                if (w.windowType == type && w.windowActive) return true;
            return false;
        }
    }
}
