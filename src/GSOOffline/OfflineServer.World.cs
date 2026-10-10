using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    public partial class OfflineServer
    {
        private const int InteractWayshrine = 21;

        private SceneWorld world;
        private bool sceneReady;
        private Vector3? pendingSpawn;
        private string lastNpcList, lastHarvestableList;
        private float visibilityTimer, autosaveTimer;

        private void RegisterWorldHandlers()
        {
            On(6, 10, OnNewSceneLoaded);
            On(17, 1, OnSyncPosRot);
            On(3, 39, c => TeleportToWayshrine((int)c[2]));
            On(3, 11, OnObjectInteract);
            On(7, 11, OnSetHomeWayshrine);
            On(7, 16, c => Send(7, 17, character?.name ?? string.Empty));

            // Heartbeats, telemetry and cosmetic sync that need no reply offline.
            Ignore(7, 0, 18, 21);
            Ignore(3, 47);
            Ignore(6, 27);
            Ignore(27, 0);
            Ignore(23, 1);
            Ignore(198, 2, 3);
        }

        private void ResetWorldState()
        {
            world = null;
            sceneReady = false;
            pendingSpawn = null;
            arrivalDeadline = -1f;
            pendingSpawnNeedsClearing = false;
            pendingCrossing = null;
            lastNpcList = lastHarvestableList = null;
            job = null;
            ResetCombat();
        }

        // The start time goes out once per login: resending it on every zone change would turn the clock back
        // (the client's day/night cycle runs on its own, and GSO HD Textures' weather keeps it going across zones).
        private bool startTimeSent;

        private Scr_Player LocalPlayer => Scr_PlayerHandler.instance != null ? Scr_PlayerHandler.instance.player : null;

        private void ChangeScene(int sceneId, Vector3 spawn)
        {
            if (character == null) return;
            bool known = false;
            foreach (var s in Script_sceneManager.instance.scenes)
                if (s.id == sceneId) known = true;
            if (!known)
            {
                Plugin.Log.LogWarning($"Unknown scene id {sceneId}; sending player to the start location.");
                sceneId = StartScene;
                spawn = StartPos;
            }
            StowBoat("zone change");
            ResetWorldState();
            pendingSpawn = spawn;
            character.scene = sceneId;
            character.Position = spawn;
            Send(3, 28, character.name, sceneId);
        }

        public void Teleport(int sceneId, Vector3 pos)
        {
            if (character == null) return;
            ridingId = 0;   // off any ferry
            if (sceneReady && sceneId == character.scene)
            {
                DisembarkForTeleport();
                character.Position = pos;
                Send(5, 3, character.name, 1, pos);
                PlayArrivalGfx(pos);
            }
            else
            {
                ChangeScene(sceneId, pos);
            }
        }

        private void OnNewSceneLoaded(object[] c)
        {
            if (character == null) return;
            var current = Script_sceneManager.instance.currentScene;
            character.scene = current.id;

            GameData.EnsureLoaded();
            world = SceneWorld.Build(current.id, current.sceneName);
            LoadFerries(current.id);

            // Door arrivals wait (behind the loading screen) until the floor at the door exists; see Arrival.cs.
            if (pendingSpawnNeedsClearing && pendingSpawn.HasValue)
            {
                arrivalDeadline = Time.time + ArrivalWait;
                return;
            }
            FinishSceneLoad();
        }

        private void FinishSceneLoad()
        {
            Send(198, 10);   // clears Script_sceneManager.loadingInProgress
            SendSceneFerries();   // before the spawn: a ferry crossing lands the player on a deck
            var deck = TakeFerryArrival();
            if (deck.HasValue) pendingSpawn = deck;
            if (pendingSpawn.HasValue)
            {
                Send(5, 3, character.name, 0, pendingSpawn.Value);
                PlayArrivalGfx(pendingSpawn.Value);
                pendingSpawn = null;
            }
            if (Plugin.TimeOfDay.Value >= 0f && !startTimeSent)
            {
                Send(16, 1, (int)Plugin.TimeOfDay.Value, 20);
                startTimeSent = true;
            }
            SendWayshrines();
            SendPendingEquips();
            SendHealth();
            SendSceneLoot();
            SendBoats();

            sceneReady = true;
            visibilityTimer = 0.5f;
        }

        private void OnSyncPosRot(object[] c)
        {
            if (character == null || !sceneReady || pendingSpawn.HasValue) return;
            if ((int)c[3] != character.scene) return;
            character.rot = (int)c[2];
            character.Position = (Vector3)c[4];
        }

        private void TickWorld()
        {
            if (character == null) return;
            TickArrival();

            autosaveTimer -= Time.deltaTime;
            if (autosaveTimer <= 0f)
            {
                autosaveTimer = 60f;
                SaveCurrentCharacter();
            }

            if (!sceneReady || world == null) return;
            TickQuestTriggers();
            visibilityTimer -= Time.deltaTime;
            if (visibilityTimer > 0f) return;
            visibilityTimer = 1f;

            var player = LocalPlayer;
            if (player == null) return;
            Vector3 p = player.transform.position;

            string npcs = world.BuildVisibleNpcList(p, Plugin.NpcViewDistance.Value, NpcShown);
            if (npcs != lastNpcList)
            {
                lastNpcList = npcs;
                Send(2, 2, character.name, npcs);
            }
            string harvestables = world.BuildVisibleHarvestableList(p, Plugin.HarvestableViewDistance.Value);
            if (harvestables != lastHarvestableList)
            {
                lastHarvestableList = harvestables;
                Send(2, 4, character.name, harvestables);
                // Newly visible nodes spawn alive client-side; mark the depleted ones.
                float r2 = Plugin.HarvestableViewDistance.Value * Plugin.HarvestableViewDistance.Value;
                foreach (var h in world.harvestables)
                    if (h.dead && (h.pos - p).sqrMagnitude <= r2) Send(13, 1, h.uid, true);
            }
        }

        private List<int> UnlockedWayshrines(CharacterSave ch)
        {
            if (!Plugin.UnlockAllWayshrines.Value) return new List<int>(ch.wayshrines);
            GameData.EnsureLoaded();
            return new List<int>(GameData.Wayshrines.Keys);
        }

        private void SendWayshrines()
        {
            if (character == null) return;
            var ids = UnlockedWayshrines(character).ConvertAll(i => i.ToString()).ToArray();
            Send(2, 17, character.name, string.Join(",", ids) + "_" + character.homeWayshrine);
        }

        private void TeleportToWayshrine(int id)
        {
            GameData.EnsureLoaded();
            if (character == null || !GameData.Wayshrines.TryGetValue(id, out var w)) return;
            if (!UnlockedWayshrines(character).Contains(id))
            {
                Send(2, 41, "You have not discovered that wayshrine yet.");
                return;
            }
            var player = LocalPlayer;
            if (player != null) PlayEffect(GfxTeleport, player.transform.position);
            arrivalGfx = GfxTeleportLand;
            Teleport(w.scene, w.pos);
        }

        private WayshrineInfo NearestWayshrine(float maxDistance)
        {
            var player = LocalPlayer;
            if (player == null || character == null) return null;
            GameData.EnsureLoaded();
            WayshrineInfo best = null;
            float bestDist = maxDistance;
            foreach (var w in GameData.Wayshrines.Values)
            {
                if (w.scene != character.scene) continue;
                float d = Vector3.Distance(w.pos, player.transform.position);
                if (d < bestDist) { bestDist = d; best = w; }
            }
            return best;
        }

        private void OnObjectInteract(object[] c)
        {
            if (character == null) return;
            int typeId = (int)c[2];
            if (HandleTravelInteract(typeId)) return;
            OnObjectClicked(typeId);
            if (typeId == InteractWayshrine)
            {
                var w = NearestWayshrine(30f);
                if (w != null && !character.wayshrines.Contains(w.id))
                {
                    character.wayshrines.Add(w.id);
                    Send(2, 0, string.Empty, "Wayshrine discovered: " + w.name, 4);
                    SendWayshrines();
                }
            }
            // The client implements most interactables (workbenches, wayshrine window, banks...) once confirmed.
            Send(3, 12, character.name, typeId);
        }

        private void OnSetHomeWayshrine(object[] c)
        {
            var w = NearestWayshrine(30f);
            if (character == null || w == null) return;
            character.homeWayshrine = w.id;
            Send(2, 0, string.Empty, "Home wayshrine set to " + w.name + ".", 4);
            SendWayshrines();
        }
    }
}
