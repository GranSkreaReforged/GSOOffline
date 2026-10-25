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
        private string lastNpcList, lastHarvestableList, lastQuestObjects;
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
            Ignore(198, 2, 3);
        }

        private void ResetWorldState()
        {
            world = null;
            sceneReady = false;
            pendingSpawn = null;
            arrivalDeadline = -1f;
            pendingSpawnNeedsClearing = false;
            borderArrival = false;
            pendingCrossing = null;
            ResetOysters();
            ResetLakhmusBasement();
            ResetUlansDungeon();
            ResetPoacherDungeon();
            ResetSewersAndCoral();
            ResetHousing();
            ResetBounties();
            lastNpcList = lastHarvestableList = lastQuestObjects = null;
            // Tell the client, or it keeps playing the gathering animation in the next zone.
            CancelJob("changing zone");
            StandUp();
            CloseWorkbench();
            CloseOpenWindow();
            // A conversation left open stays on screen in the next zone, hiding the HUD and holding the camera.
            if (dialogueNode != null && character != null)
            {
                dialogueNode = null;
                Send(2, 3, character.name, "-");
            }
            EndDifficultyTalk();
            ResetCombat();
        }

        /// <summary>
        /// Closes the crafting window. The client only closes it on a movement key, its Close button or a
        /// disconnect, so after a teleport the previous zone's workbench stayed open; Escape doesn't close it, and
        /// under the cursor it blocks mouse-look. Same reset as the client's disconnect handler.
        /// </summary>
        private static void CloseWorkbench()
        {
            var mc = Menucontroller.instance;
            if (mc == null || mc.showWorkbench == 0) return;
            Plugin.Log.LogInfo($"[skill] closing workbench {mc.showWorkbench}: changing zone");
            mc.clearWorkbench();
            mc.showWorkbench = 0;
            mc.craftingAmount = 1;
            mc.craftingInProgress = false;
        }

        // The start time goes out once per login: resending it on every zone change would turn the clock back
        // (the client's day/night cycle runs on its own, and GSO HD Textures' weather keeps it going across zones).
        private bool startTimeSent, eventsAnnounced;

        private Scr_Player LocalPlayer => Scr_PlayerHandler.instance != null ? Scr_PlayerHandler.instance.player : null;

        private void ChangeScene(int sceneId, Vector3 spawn, bool keepBoat = false)
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
            if (!keepBoat) StowBoat("zone change");   // sailing over a zone border keeps it (ZoneBorders.cs)
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
            var manager = Script_sceneManager.instance;
            // The client sends this from Script_sceneManager.OnNewSceneLoaded and clears its `async` only when that
            // method returns. If anything after this message throws (Scr_UI_Minimap.UpdateMap does once the minimap's
            // image has been destroyed), it runs again every frame: the black fade-in, the camera setup and this
            // message, which rebuilt the world each frame (bosses reset, the player killed again on arrival). Clearing
            // `async` here ends it after one pass, and a repeat for the zone we already have is ignored.
            manager.async = null;
            var current = manager.currentScene;
            if (sceneReady && world != null && world.sceneId == current.id && character.scene == current.id)
            {
                Plugin.Log.LogWarning($"Scene {current.id} reported loaded again; ignored (the client's scene setup threw?)");
                return;
            }
            character.scene = current.id;
            KeepPersistentAnimationHandler();

            GameData.EnsureLoaded();
            ApplyDifficulty();
            MarkCleanser();
            world = SceneWorld.Build(current.id, current.sceneName);
            AddHarpyToWorld();
            SyncBountyActors();
            LoadFerries(current.id);
            HideStrayDoors(current.id);
            HideFerryProps(current.id);

            // Door arrivals wait (behind the loading screen) until the floor at the door exists; see Arrival.cs.
            if (pendingSpawnNeedsClearing && pendingSpawn.HasValue)
            {
                arrivalDeadline = Time.time + ArrivalWait;
                return;
            }
            FinishSceneLoad();
        }

        /// <summary>
        /// The Mages Guild scene (21) carries its own copy of the client's AnimationHandler, whose Awake takes over the
        /// static EasyAnimationHandler.instance every NPC gets its animator from. Once that scene unloads the instance
        /// is destroyed, and every NPC set up afterwards throws (no animations, broken bears) until the game restarts.
        /// Point it back at the persistent one (DontDestroyOnLoad) on every scene load.
        /// </summary>
        private static void KeepPersistentAnimationHandler()
        {
            var current = EasyAnimationHandler.instance;
            if (current != null && current.gameObject.scene.name == "DontDestroyOnLoad") return;
            foreach (var h in Resources.FindObjectsOfTypeAll<EasyAnimationHandler>())
            {
                if (h == null || h.gameObject.scene.name != "DontDestroyOnLoad") continue;
                EasyAnimationHandler.instance = h;
                Plugin.Log.LogInfo($"Animation handler: restored the persistent one (the scene's copy {(current == null ? "was destroyed" : "had taken over")}).");
                return;
            }
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
            if (!eventsAnnounced)
            {
                eventsAnnounced = true;
                AnnounceSeasonalEvents();
                AnnounceActiveBounties();
            }
            SendWayshrines();
            SendPendingEquips();
            SendWeaponAbilities();
            SendHealth();
            SendMana();
            SendGearStats();
            SendDeathToll();
            SendCowardMark();
            RefreshAlignment(true);
            ResendBuffIcons();
            UpdateWhiskerSickness(QuestRatsTail);
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
            if (TickZoneBorders()) return;   // zone change: this world is gone
            TickQuestTriggers();
            visibilityTimer -= Time.deltaTime;
            if (visibilityTimer > 0f) return;
            visibilityTimer = 1f;

            var player = LocalPlayer;
            if (player == null) return;
            Vector3 p = player.transform.position;

            SendQuestObjects();
            SendVisibleHouses();
            RenameBrokers();
            string npcs = world.BuildVisibleNpcList(p, Plugin.NpcViewDistance.Value, n => NpcShown(n) && !bountyByUid.ContainsKey(n.uid));
            SendVisibleBounties(p);
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

        /// <summary>Dev: how far each of this scene's original-server spawns (Data/spawns.json) is from the ground (spawnground).</summary>
        internal void LogSpawnGround()
        {
            if (world == null) return;
            int ok = 0;
            var rows = new List<SpawnRow>(SpawnData.Npcs(world.sceneId));
            rows.AddRange(SpawnData.Harvestables(world.sceneId));
            foreach (var r in rows)
            {
                Vector3 p = r.Position;
                bool found = GroundAt(p, p.y + 3f, 40f, out var g);
                if (found && Mathf.Abs(p.y - g.y) < 1.5f) { ok++; continue; }
                Plugin.Log.LogInfo($"[dev] spawn type {r.type} at {SceneWorld.Vec(p)}: " + (found ? $"ground {p.y - g.y:0.00} m below" : "no ground within 40 m"));
            }
            Plugin.Log.LogInfo($"[dev] spawnground: {ok} of {rows.Count} original spawns in scene {world.sceneId} stand on the ground");
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
            if (HandleBountyBoard(typeId)) return;   // a wanted board
            if (HandleSewerInteract(typeId)) return;     // the sewers' pillar
            if (HandlePoacherInteract(typeId)) return;   // the Poacher dungeon's doors, chests, vault and dynamite crate
            if (HandleUlanInteract(typeId)) return;     // Ulan's dungeon: the gate, its key, the levers, the deep door
            if (HandleLakhmuInteract(typeId)) return;   // the hatch, Alorus's barrels, the ladder out of the dungeon
            if (HandleTravelInteract(typeId)) return;
            if (HandleItemObjectInteract(typeId)) return;   // Kombra's book, the Argi blueprint chest
            if (HandleFurnitureInteract(typeId)) return;    // house doors and furniture
            if (typeId == InteractChair) SitDown();
            HandleOysterInteract(typeId);   // harpoon guns (the client then waits for the confirm below)
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

        // ---- chairs --------------------------------------------------------------------------

        // A chair click: the client puts the player on the seat itself, and the server plays the sitting animation
        // (8/5 action type 4 sets the animator's ActionID; AC_Player state 12 is "Sit"). The client never ends it,
        // so the server stands the player up when they leave the seat, attack, start a job or change zone.
        private const int InteractChair = 54, ActionAnimation = 4, AnimSit = 12;
        private Vector3? sittingAt;

        private void SitDown()
        {
            var player = LocalPlayer;
            if (player == null) return;
            CancelJob("sitting down");
            StopAttacking();
            sittingAt = player.transform.position;
            Send(8, 5, character.name, ActionAnimation, AnimSit);
        }

        private void TickSitting()
        {
            if (!sittingAt.HasValue) return;
            var player = LocalPlayer;
            if (player == null || !sceneReady || job != null || autoAttacking || LeftSeat(player.transform.position - sittingAt.Value))
                StandUp();
        }

        // Walking off. Height doesn't count: after the client puts the player on a seat point, physics drops them onto
        // some benches (Yorkhill's tavern) or briefly pops them up (Grimwall) by about 0.5 m, which stood them straight back up.
        private static bool LeftSeat(Vector3 moved) => new Vector2(moved.x, moved.z).magnitude > 0.5f;

        private void StandUp()
        {
            if (!sittingAt.HasValue) return;
            sittingAt = null;
            // A job started from the seat already replaced the animation; only end ours.
            if (character != null && job == null) Send(3, 14, character.name, ActionAnimation);
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
