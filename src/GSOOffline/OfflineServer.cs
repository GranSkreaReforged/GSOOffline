using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// In-process replacement for the Gran Skrea Online game server.
    /// Client->server traffic arrives via <see cref="Receive"/> (patched Scr_RPCSender.RaiseEvent);
    /// server->client traffic is queued with <see cref="Send"/> and fed into Scr_RPCReceiver.OnEventCall,
    /// exactly as Photon would have delivered it.
    /// </summary>
    public partial class OfflineServer : MonoBehaviour
    {
        public static OfflineServer Instance { get; private set; }

        // Yorkhill Monastery wayshrine, where the first quest begins.
        public const int StartScene = 1;
        public static readonly Vector3 StartPos = new Vector3(829.7f, 20f, 1599.2f);

        private delegate void Handler(object[] c);

        private struct Outgoing
        {
            public byte ev;
            public object[] data;
        }

        private readonly Queue<Outgoing> outbox = new Queue<Outgoing>();
        private readonly Dictionary<int, Handler> handlers = new Dictionary<int, Handler>();
        private readonly HashSet<int> silent = new HashSet<int>();
        private readonly HashSet<int> warned = new HashSet<int>();
        private MethodInfo onEventCall;

        private static int Key(byte ev, int sub) => (ev << 16) | (sub & 0xFFFF);

        private void On(byte ev, int sub, Handler h) => handlers[Key(ev, sub)] = h;

        private void Ignore(byte ev, params int[] subs)
        {
            foreach (int s in subs) silent.Add(Key(ev, s));
        }

        private void Awake()
        {
            Instance = this;
            onEventCall = AccessTools.Method(typeof(Scr_RPCReceiver), "OnEventCall");
            if (onEventCall == null)
                Plugin.Log.LogError("Scr_RPCReceiver.OnEventCall not found; the offline server cannot talk to the client.");
            RegisterAccountHandlers();
            RegisterWorldHandlers();
            RegisterChatHandlers();
            RegisterInventoryHandlers();
            RegisterDialogueHandlers();
            RegisterSkillHandlers();
            RegisterCombatHandlers();
            RegisterTravelHandlers();
        }

        public void Receive(byte ev, object[] c)
        {
            int sub = c != null && c.Length > 0 && c[0] is int s ? s : -1;
            int key = Key(ev, sub);
            if (handlers.TryGetValue(key, out var h))
            {
                try
                {
                    h(c);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Handler {ev}/{sub} failed: {e}");
                }
            }
            else if (!silent.Contains(key) && Plugin.LogUnhandledEvents.Value && warned.Add(key))
            {
                Plugin.Log.LogWarning($"Unhandled client event {ev}/{sub}: {Describe(c)}");
            }
        }

        /// <summary>Queue a server->client event. data[0] must be the int sub-opcode.</summary>
        public void Send(byte ev, params object[] data)
        {
            outbox.Enqueue(new Outgoing { ev = ev, data = data });
        }

        private void Update()
        {
            // Client handlers may call RaiseEvent, which can queue more replies; cap per frame.
            int budget = 512;
            while (outbox.Count > 0 && budget-- > 0)
                Deliver(outbox.Dequeue());

            TickWorld();
            TickSkills();
            TickRespawns();
            TickCombat();
            TickRegen();
            TickAutoLogin();
        }

        private float autoLoginAt = -1f;
        private bool autoCharacterDone;

        private void TickAutoLogin()
        {
            string user = Plugin.AutoLogin.Value.Trim();
            if (user.Length == 0) return;

            if (autoLoginAt > 0f && Time.realtimeSinceStartup >= autoLoginAt)
            {
                autoLoginAt = -1f;
                autoCharacterDone = false;
                Scr_RPCSender.instance.attemptLogin(user, string.Empty, Scr_ConnectionHandler.instance.steamId);
            }

            string charName = Plugin.AutoCharacter.Value.Trim();
            if (charName.Length == 0 || autoCharacterDone || account == null || character != null) return;
            var selection = Scr_CharacterSelection.instance;
            if (selection == null || Time.timeSinceLevelLoad < 1f) return;

            autoCharacterDone = true;
            if (account.characters.Exists(n => string.Equals(n, charName, StringComparison.OrdinalIgnoreCase)))
            {
                Scr_RPCSender.instance.characterLogin(account.characters.Find(n => string.Equals(n, charName, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                selection.justCreated = charName;
                Scr_RPCSender.instance.createNewCharacter(charName);
            }
        }

        private void Deliver(Outgoing o)
        {
            var receiver = Scr_RPCReceiver.instance;
            if (receiver == null || onEventCall == null)
            {
                Plugin.Log.LogWarning($"Dropped event {o.ev}/{o.data[0]}: no receiver.");
                return;
            }
            try
            {
                onEventCall.Invoke(receiver, new object[] { o.ev, o.data, 0 });
            }
            catch (TargetInvocationException e)
            {
                Plugin.Log.LogError($"Client failed handling event {o.ev}/{o.data[0]} ({Describe(o.data)}): {e.InnerException}");
            }
        }

        internal static string Describe(object[] c)
        {
            if (c == null) return "null";
            var sb = new StringBuilder("[");
            for (int i = 0; i < c.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                string v = c[i]?.ToString() ?? "null";
                if (v.Length > 80) v = v.Substring(0, 80) + "...";
                sb.Append(c[i]?.GetType().Name ?? "").Append(':').Append(v);
            }
            return sb.Append(']').ToString();
        }

        public void FakeConnect(Scr_ConnectionHandler ch)
        {
            ch.clientConnected = true;
            ch.connectionStatus = "Connected";
            Menucontroller.instance?.ClearCache();
            FakeJoin(ch);
            Plugin.Log.LogInfo("Connected to offline server.");
        }

        public void FakeJoin(Scr_ConnectionHandler ch)
        {
            ch.joinedGame = true;
            ch.connectionStatus = "Joined server";
            if (Scr_Options.instance != null && Scr_Options.instance.useLUI && Scr_UI_LoginScreen.instance != null)
                Scr_UI_LoginScreen.instance.Activate();
            if (Plugin.AutoLogin.Value.Trim().Length > 0)
                autoLoginAt = Time.realtimeSinceStartup + 1.5f;
        }

        private void OnApplicationQuit()
        {
            SaveCurrentCharacter();
        }
    }
}
