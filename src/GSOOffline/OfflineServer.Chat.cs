using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GSOOffline
{
    public partial class OfflineServer
    {
        private static readonly HashSet<string> ClientCommands = new HashSet<string>
        {
            "togglebeautify", "changeappearance", "printevents", "stoprendering", "scalegui", "ping", "loadcinematics",
            "debug", "resetgui", "clearcache", "resetachievements", "showgui", "fps", "dc", "hidecharacter",
            "destroymusic", "loadoptions", "unloadassets", "resetstats", "resetoptions", "admindebug", "cinematic",
        };

        private void RegisterChatHandlers()
        {
            On(2, 5, OnChat);
        }

        private void Notice(string text, string color = "yellow")
        {
            Plugin.Log.LogInfo("[notice] " + text);
            Send(2, 0, string.Empty, "color=" + color + "|" + text.Replace('|', '/'), 0);
        }

        private void OnChat(object[] c)
        {
            if (character == null) return;
            string msg = (c[2] as string ?? string.Empty).Trim();
            if (msg.Length == 0) return;
            if (msg[0] == '/')
            {
                // The client runs these itself (Menucontroller.checkClientCommand) and forwards them anyway.
                if (ClientCommands.Contains(msg.Substring(1).Split(' ')[0].ToLowerInvariant())) return;
                RunCommand(msg.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                return;
            }
            Send(2, 0, string.Empty, character.name + ": " + msg.Replace('|', '/'), 0);
            OnSay(msg);
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        private void RunCommand(string[] a)
        {
            if (a.Length == 0) return;
            var player = LocalPlayer;
            try
            {
                if (RunBoatCommand(a[0].ToLowerInvariant(), a)) return;
                switch (a[0].ToLowerInvariant())
                {
                    case "help":
                        Notice("Offline commands: /pos, /tele x y z, /scene id [x y z], /wayshrine id, /wayshrines, /time 0-2400, /save, /give id [n], /silver n, /quest id [phase]");
                        break;
                    case "pos":
                        if (player != null)
                            Notice($"Scene {character.scene}: {SceneWorld.Vec(player.transform.position)}");
                        break;
                    case "tele":
                        Teleport(character.scene, new Vector3(F(a[1]), F(a[2]), F(a[3])));
                        break;
                    case "scene":
                        int id = int.Parse(a[1], CultureInfo.InvariantCulture);
                        Vector3 spawn = a.Length >= 5 ? new Vector3(F(a[2]), F(a[3]), F(a[4])) : DefaultSpawn(id);
                        Teleport(id, spawn);
                        break;
                    case "wayshrine":
                        TeleportToWayshrine(int.Parse(a[1], CultureInfo.InvariantCulture));
                        break;
                    case "wayshrines":
                        GameData.EnsureLoaded();
                        foreach (var w in GameData.Wayshrines.Values)
                            Notice($"{w.id}: {w.name} (scene {w.scene})", "cyan");
                        break;
                    case "time":
                        Send(16, 1, int.Parse(a[1], CultureInfo.InvariantCulture), 20);
                        break;
                    case "give":
                        GiveItem(int.Parse(a[1], CultureInfo.InvariantCulture), a.Length > 2 ? int.Parse(a[2], CultureInfo.InvariantCulture) : 1);
                        break;
                    case "silver":
                        SetSilver(int.Parse(a[1], CultureInfo.InvariantCulture));
                        break;
                    case "quest":
                        if (a.Length > 2)
                            SetQuestPhase(int.Parse(a[1], CultureInfo.InvariantCulture), int.Parse(a[2], CultureInfo.InvariantCulture));
                        else
                            Notice($"Quest {a[1]} phase: {character.GetQuestPhase(int.Parse(a[1], CultureInfo.InvariantCulture))}");
                        break;
                    case "useportal":   // sent by the client when confirming the snowy portal
                        UsePortal();
                        break;
                    case "save":
                        SaveCurrentCharacter();
                        Notice("Character saved.");
                        break;
                    default:
                        // Social commands (/trade, /follow, /inviteparty...) have no one to target offline.
                        Notice("Unknown or unavailable offline command: /" + a[0], "gray");
                        break;
                }
            }
            catch (Exception e) when (e is FormatException || e is IndexOutOfRangeException)
            {
                Notice("Bad arguments. Type /help.", "red");
            }
        }

        // Prefer a wayshrine in the target scene; otherwise the scene origin lifted above the terrain.
        private static Vector3 DefaultSpawn(int sceneId)
        {
            GameData.EnsureLoaded();
            foreach (var w in GameData.Wayshrines.Values)
                if (w.scene == sceneId) return w.pos;
            return new Vector3(0f, 50f, 0f);
        }
    }
}
