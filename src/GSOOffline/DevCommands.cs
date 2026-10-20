namespace GSOOffline
{
    /// <summary>
    /// Extra DevBridge commands for the GSODevTools plugin. It finds this class by name at runtime
    /// (any static void Name(string[] args) becomes the command "name"), so there's no reference to it
    /// and nothing here runs unless the bridge is enabled.
    /// </summary>
    internal static class DevCommands
    {
        // Server recipe ids must match the client's Script_Crafting numbering or crafts produce the wrong item.
        private static void CheckRecipes(string[] args)
        {
            int ok = 0, bad = 0;
            foreach (var c in Script_Crafting.instance.craftingRecipes)
            {
                if (SkillData.Recipes.TryGetValue(c.recipeId, out var r) && r.product == c.product1 && r.skill == c.skill && r.level == c.level)
                    ok++;
                else if (bad++ < 5)
                    Plugin.Log.LogWarning($"[dev] recipe {c.recipeId} mismatch: client product {c.product1}, server {(r != null ? r.product.ToString() : "missing")}");
            }
            Plugin.Log.LogInfo($"[dev] recipes: {ok} match, {bad} mismatch, client {Script_Crafting.instance.craftingRecipes.Count}, server {SkillData.Recipes.Count}");
        }

        // boatprefabs: which ab_PlayerShip_<n> prefabs the client ships, with their names, seats and speeds.
        private static void BoatPrefabs(string[] args)
        {
            for (int i = 0; i <= 30; i++)
            {
                var go = UnityEngine.Resources.Load("1LoadingAssets/PlayerShips/ab_PlayerShip_" + i, typeof(UnityEngine.GameObject)) as UnityEngine.GameObject;
                var ship = go != null ? go.GetComponent<Scr_PlayerShip>() : null;
                if (ship != null)
                {
                    var inter = new System.Collections.Generic.List<string>();
                    foreach (var it in go.GetComponentsInChildren<Scr_Interactable>(true)) inter.Add(it.typeId + " '" + it.interactableName + "' at " + it.transform.localPosition);
                    var crane = go.GetComponent<Scr_FishingBoatCrane>();
                    Plugin.Log.LogInfo($"[dev] ship {i}: '{ship.boatName}' seats={ship.positions?.Length} speed={ship.movementSpeed} turn={ship.rotationSpeed} crane={(crane != null && crane.cranePosition != null ? crane.cranePosition.localPosition.ToString() : "none")} interactables [{string.Join("; ", inter.ToArray())}]");
                }
                else if (go != null)
                    Plugin.Log.LogInfo($"[dev] ship {i}: prefab without Scr_PlayerShip");
            }
            Plugin.Log.LogInfo("[dev] boatprefabs done");
        }

        // sail <seconds> [heading]: drives the player's boat forward like holding "move forward" (optionally
        // turning to a compass heading first), so the client's own sync and shore check run as in play.
        private static void Sail(string[] args)
        {
            var p = Scr_PlayerHandler.instance.player;
            if (!p.inShip || p.ship == null)
            {
                Plugin.Log.LogInfo("[dev] sail: not in a boat");
                return;
            }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            float seconds = float.Parse(args[1], inv);
            if (args.Length > 2) p.ship.targetRotation = float.Parse(args[2], inv);
            OfflineServer.Instance.StartCoroutine(SailFor(p.ship, seconds));
        }

        private static System.Collections.IEnumerator SailFor(Scr_PlayerShip ship, float seconds)
        {
            var cc = ship.GetComponent<UnityEngine.CharacterController>();
            var start = ship.transform.position;
            for (float t = 0f; t < seconds && ship != null; t += UnityEngine.Time.deltaTime)
            {
                cc.Move(ship.transform.forward * ship.movementSpeed * UnityEngine.Time.deltaTime);
                yield return null;
            }
            Plugin.Log.LogInfo(ship != null
                ? $"[dev] sailed {UnityEngine.Vector3.Distance(start, ship.transform.position):F1} m to {ship.transform.position}"
                : "[dev] sail: the boat is gone");
        }

        // spawnprefabs [max]: which 1LoadingAssets/SpawnedObjects/ab_spawnedObject_<n> prefabs exist, with their
        // components and the interactables inside them.
        private static void SpawnPrefabs(string[] args)
        {
            int max = args.Length > 1 ? int.Parse(args[1]) : 200;
            for (int i = 0; i <= max; i++)
            {
                var go = UnityEngine.Resources.Load("1LoadingAssets/SpawnedObjects/ab_spawnedObject_" + i, typeof(UnityEngine.GameObject)) as UnityEngine.GameObject;
                if (go == null) continue;
                var comps = new System.Collections.Generic.List<string>();
                foreach (var c in go.GetComponentsInChildren<UnityEngine.Component>(true))
                    if (c != null && !(c is UnityEngine.Transform) && !comps.Contains(c.GetType().Name)) comps.Add(c.GetType().Name);
                var inter = new System.Collections.Generic.List<string>();
                foreach (var it in go.GetComponentsInChildren<Scr_Interactable>(true)) inter.Add(it.typeId + " '" + it.interactableName + "'");
                var so = go.GetComponentInChildren<Scr_SpawnedObject>(true);
                var size = UnityEngine.Vector3.zero;
                foreach (var mf in go.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
                    if (mf.sharedMesh != null) size = UnityEngine.Vector3.Max(size, UnityEngine.Vector3.Scale(mf.sharedMesh.bounds.size, mf.transform.lossyScale));
                foreach (var sm in go.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true))
                    if (sm.sharedMesh != null) size = UnityEngine.Vector3.Max(size, UnityEngine.Vector3.Scale(sm.sharedMesh.bounds.size, sm.transform.lossyScale));
                if (args.Length > 2 && size.magnitude < 0.01f) continue;
                Plugin.Log.LogInfo($"[dev] spawned {i} '{go.name}' size {size}: interactables [{string.Join(", ", inter.ToArray())}] {(so != null ? $"speed={so.movementSpeed} rotSpeed={so.rotationSpeed} slerp={so.slerpPos} " : "")}components: {string.Join(", ", comps.ToArray())}");
            }
            Plugin.Log.LogInfo("[dev] spawnprefabs done");
        }

        // profile x1 z1 x2 z2 [step]: the highest solid surface along a line (water is y 3.15; - = nothing solid).
        private static void Profile(string[] args)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var a = new UnityEngine.Vector3(float.Parse(args[1], inv), 0f, float.Parse(args[2], inv));
            var b = new UnityEngine.Vector3(float.Parse(args[3], inv), 0f, float.Parse(args[4], inv));
            float step = args.Length > 5 ? float.Parse(args[5], inv) : 2f;
            var sb = new System.Text.StringBuilder();
            float len = UnityEngine.Vector3.Distance(a, b);
            for (float d = 0f; d <= len; d += step)
            {
                var c = UnityEngine.Vector3.Lerp(a, b, d / len);
                float top = FerryRoutePlanner.SolidTop(c);
                sb.Append($"{c.x:F0},{c.z:F0}={(top < -1000f ? "-" : top.ToString("F1", inv))} ");
            }
            Plugin.Log.LogInfo("[dev] profile " + sb);
        }

        // oysters: the harbour oysters in Bal Sardan, where they are and whether they're stunned.
        private static void Oysters(string[] args) => OfflineServer.Instance.LogOysters();

        // netoyster [stun] [offset]: drop the vessel's net on the first oyster (needs the fishing vessel boarded).
        private static void NetOyster(string[] args) => OfflineServer.Instance.DevNetOyster(
            args.Length > 1 && args[1] == "stun", args.Length > 2 ? float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0f);

        // spawnobj <type> [dx dz] / despawnobj <uid>: show a spawned-object prefab next to the player (client only).
        private static int devObjectUid = 900000;
        private static void SpawnObj(string[] args)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var p = Scr_PlayerHandler.instance.player.transform.position;
            var pl = Scr_PlayerHandler.instance.player.transform;
            if (args.Length > 2 && args[2] == "f") p += pl.forward * float.Parse(args[3], inv) + UnityEngine.Vector3.up * (args.Length > 4 ? float.Parse(args[4], inv) : 0f);   // f <metres ahead> [up]
            else if (args.Length > 3) p += new UnityEngine.Vector3(float.Parse(args[2], inv), 0f, float.Parse(args[3], inv));
            int uid = ++devObjectUid;
            Scr_SpawnedObjectHandler.instance.UpdateVisibleObjects(uid + "_" + args[1] + "_" + SceneWorld.Vec(p) + "_0");
            Plugin.Log.LogInfo($"[dev] spawned object {uid} (type {args[1]}) at {SceneWorld.Vec(p)}");
        }

        private static void DespawnObj(string[] args) => Scr_SpawnedObjectHandler.instance.RemoveSpawnedObject(int.Parse(args[1]));

        // houses: the house objects the client holds (Scr_HouseHandler.houses), with their doors.
        private static void Houses(string[] args)
        {
            foreach (var h in Scr_HouseHandler.instance.houses)
            {
                if (h == null) continue;
                Plugin.Log.LogInfo($"[dev] house {h.id} type {h.typeid} size {h.size} '{h.houseName}' at {SceneWorld.Vec(h.transform.position)}"
                    + $" door {(h.door != null ? SceneWorld.Vec(h.door.transform.position) : "none")} furniture {h.furniture.Count}");
            }
            Plugin.Log.LogInfo($"[dev] {Scr_HouseHandler.instance.houses.Count} house objects");
        }

        // fakeplayer spawn|move dx dz|attack id|stop|hit n|die|anim json|gone: a client-side "other player" next to
        // you, driven straight through the client's receivers (spike for the bounty board, #67).
        private const string FakeName = "Garrick";
        private static int fakeHp = 300;
        private static void FakePlayer(string[] args)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var ph = Scr_PlayerHandler.instance;
            var me = ph.player.transform.position;
            switch (args[1])
            {
                case "spawn":
                    fakeHp = 300;
                    var p = me + ph.player.transform.forward * 4f;
                    // name_hp_max_equips_x,y,z_gender_hair_body_eyebrow_r_g_b_size_pvp_criminal_title_bounty_guild_secondary_emblem_bg_color
                    string entry = string.Join("_", new[] { FakeName, "300", "300", args.Length > 2 ? args[2] : "183,60,61,62",
                        ((int)p.x) + "," + ((int)p.y) + "," + ((int)p.z), "0", "15", "0", "0", "60", "40", "20", "100", "0", "0", "", "0", "{}", "", "0", "0", "0" });
                    ph.updateVisiblePlayers(entry);
                    ph.updatePlayerLocRot(FakeName, p, 0);
                    break;
                case "move":
                    var o = ph.getOtherPlayer(FakeName);
                    var to = o.transform.position + new UnityEngine.Vector3(float.Parse(args[2], inv), 0f, float.Parse(args[3], inv));
                    int rot = (int)(UnityEngine.Mathf.Atan2(to.x - o.transform.position.x, to.z - o.transform.position.z) * UnityEngine.Mathf.Rad2Deg);
                    ph.updatePlayerLocRot(FakeName, to, rot);
                    break;
                case "attack": ph.setPlayerAttackId(FakeName, int.Parse(args[2])); break;
                case "stop": ph.stopPlayerAttack(FakeName); break;
                case "hit":
                    int n = int.Parse(args[2]);
                    fakeHp -= n;
                    ph.takeDamage(FakeName, -n, 0);
                    ph.updatePlayerHealth(FakeName, fakeHp, 300);
                    break;
                case "die": ph.setPlayerDead(FakeName, true); break;
                case "anim": ph.UpdateAnimations(FakeName + "," + string.Join(" ", args, 2, args.Length - 2)); break;
                case "myanim":   // the local player's animation packet (Scr_Animator.SendAnimations), as a template
                    var an = ph.player.anim;
                    var a = an.animator;
                    string json = UnityEngine.JsonUtility.ToJson(new Scr_Animator.AnimationPacket
                    {
                        f1 = a.GetFloat("ForwardSpeed"), i1 = a.GetInteger("ActionID"), b1 = a.GetBool("Combat"), f8 = a.GetFloat("MovingDirection"),
                        b2 = a.GetBool("Jumping"), b3 = a.GetBool("Dead"), i5 = a.GetInteger("CustomMoveAnim"),
                        f3 = a.GetLayerWeight(0), f4 = a.GetLayerWeight(2), f5 = a.GetLayerWeight(3), f6 = a.GetLayerWeight(4), f9 = a.GetLayerWeight(1), f10 = a.GetLayerWeight(5),
                        i3 = (int)ph.player.characterModel.transform.localEulerAngles.y, i4 = an.attackId, f7 = a.GetFloat("HorizontalSpeed"),
                    });
                    Plugin.Log.LogInfo("[dev] myanim " + json);
                    break;
                case "gone": ph.updateVisiblePlayers(string.Empty); break;
            }
            var f = ph.getOtherPlayer(FakeName);
            Plugin.Log.LogInfo($"[dev] fakeplayer {args[1]}: {(f != null ? SceneWorld.Vec(f.transform.position) + " hp " + f.currentHealth + "/" + f.maxHealth + " dead " + f.dead : "none")}");
        }

        // bountyreset: a fresh bounty board. bountyhere <style 0-4> <level>: an outlaw 8 m away (0 swordsman,
        // 1 two-hander, 2 archer, 3 crossbowman, 4 fire mage).
        private static void BountyReset(string[] args) => OfflineServer.Instance.DevBountyReset();
        private static void Outlaws(string[] args) => OfflineServer.Instance.DevOutlaws();

        // riskinfo: what the risk-zone "?" button does (Menucontroller): the scroll notification with the risk zones
        // text, which RiskZoneInfoPatch turns into the active bounties.
        private static void RiskInfo(string[] args) =>
            Script_WindowController.instance.createScrollNotification("Information", "Risk zones:\n\nBlue\n- No PvP", 600);
        private static void BountyHere(string[] args) => OfflineServer.Instance.DevBountyHere(int.Parse(args[1]), int.Parse(args[2]));

        // interactablesnear [radius]: every scene interactable within radius of the player (default 150 m), active or not.
        private static void InteractablesNear(string[] args)
        {
            float r = args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 150f;
            var p = Scr_PlayerHandler.instance.player.transform.position;
            foreach (var i in UnityEngine.Resources.FindObjectsOfTypeAll<Scr_Interactable>())
                if (i.gameObject.scene.IsValid() && UnityEngine.Vector3.Distance(i.transform.position, p) <= r)
                    Plugin.Log.LogInfo($"[dev] interactable {i.typeId} '{i.interactableName}' ({i.name}) at {i.transform.position} active={i.gameObject.activeInHierarchy}");
            Plugin.Log.LogInfo("[dev] interactablesnear done");
        }

        // questobjects: the scene's quest objects (hidden until the server lists their id in event 2/16), with the
        // interactable on each, active or not.
        private static void QuestObjects(string[] args)
        {
            foreach (var q in UnityEngine.Resources.FindObjectsOfTypeAll<Scr_QuestObject>())
            {
                if (!q.gameObject.scene.IsValid()) continue;
                var i = q.GetComponentInChildren<Scr_Interactable>(true);
                Plugin.Log.LogInfo($"[dev] questobject {q.id} '{q.name}' at {q.transform.position} active={q.gameObject.activeSelf} interactable={(i != null ? i.typeId + " '" + i.interactableName + "'" : "-")} scene={q.gameObject.scene.name}");
            }
            Plugin.Log.LogInfo("[dev] questobjects done");
        }

        // interactables <typeId>: positions of the scene's client-side interactables of one type.
        private static void Interactables(string[] args)
        {
            int type = int.Parse(args[1]);
            foreach (var i in UnityEngine.Resources.FindObjectsOfTypeAll<Scr_Interactable>())
                if (i.typeId == type && i.gameObject.scene.IsValid())
                    Plugin.Log.LogInfo($"[dev] interactable {type} '{i.name}' at {i.transform.position} active={i.gameObject.activeInHierarchy} scene={i.gameObject.scene.name}");
            Plugin.Log.LogInfo("[dev] interactables done");
        }

        // boatstate: the client's view of boats: whether the player swims or sits in one, and every boat object.
        private static void BoatState(string[] args)
        {
            var p = Scr_PlayerHandler.instance.player;
            Plugin.Log.LogInfo($"[dev] player at {p.transform.position} swimming={p.swimming} inShip={p.inShip} seat={p.shipPosition} ship={(p.ship != null ? p.ship.ownername : "-")}");
            foreach (var b in Scr_PlayerBoatHandler.instance.boats)
                if (b != null)
                    Plugin.Log.LogInfo($"[dev] boat '{b.boatName}' type {b.typeId} owner {b.ownername} at {b.transform.position} rot {b.transform.eulerAngles.y:F0} local={b.controlledLocally}");
            Plugin.Log.LogInfo($"[dev] boatstate done ({Scr_PlayerBoatHandler.instance.boats.Count} boats)");
        }

        // ferries: the server's ferry schedule next to the client's ferry objects, and whether the player is on a deck.
        private static void Ferries(string[] args) => OfflineServer.Instance.LogFerries();

        // watermap x1 z1 x2 z2 [cell]: water/land map of that rectangle -> <game>\GSODevTools\watermap.png (north up).
        private static void WaterMap(string[] args)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            FerryRoutePlanner.WaterMap(new UnityEngine.Vector3(float.Parse(args[1], inv), 0f, float.Parse(args[2], inv)),
                new UnityEngine.Vector3(float.Parse(args[3], inv), 0f, float.Parse(args[4], inv)),
                args.Length > 5 ? float.Parse(args[5], inv) : 4f, DevImage("watermap"));
        }

        // routeplan <name> <margin> <cell> x,z x,z ...: shortest water route through the points, printed as waypoints,
        // with a map -> <game>\GSODevTools\<name>.png.
        private static void RoutePlan(string[] args)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var pts = new System.Collections.Generic.List<UnityEngine.Vector3>();
            for (int k = 4; k < args.Length; k++)
            {
                var xz = args[k].Split(',');
                pts.Add(new UnityEngine.Vector3(float.Parse(xz[0], inv), 3.15f, float.Parse(xz[1], inv)));
            }
            var route = FerryRoutePlanner.Plan(pts, float.Parse(args[2], inv), float.Parse(args[3], inv), DevImage(args[1]));
            if (route == null) return;
            var parts = new System.Collections.Generic.List<string>();
            foreach (var p in route) parts.Add(p.x.ToString("F1", inv) + "," + p.z.ToString("F1", inv));
            Plugin.Log.LogInfo($"[dev] route {args[1]} (scene {Script_sceneManager.instance.currentScene.id}): {route.Count} waypoints: {string.Join(" ", parts.ToArray())}");
        }

        private static string DevImage(string name) =>
            System.IO.Path.Combine(System.IO.Path.Combine(BepInEx.Paths.GameRootPath, "GSODevTools"), name + ".png");

        // routecheck: where this zone's ferry routes run over land or rocks.
        private static void RouteCheck(string[] args) => OfflineServer.Instance.DevRouteCheck();

        // ferryboard <id> [height]: drops the player onto a ferry's deck from that height above its origin (default 4 m).
        private static void FerryBoard(string[] args) =>
            OfflineServer.Instance.DevBoardFerry(int.Parse(args[1]), args.Length > 2 ? float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 4f);

        // door <n>: goes through doors.json entry n exactly as clicking it would (arrival included). "door" alone lists them.
        private static void Door(string[] args)
        {
            var doors = OfflineServer.Instance.DoorList;
            if (args.Length < 2)
            {
                for (int i = 0; i < doors.Count; i++)
                    Plugin.Log.LogInfo($"[dev] door {i}: type {doors[i].type} scene {doors[i].scene} -> {doors[i].toScene} ({doors[i].note})");
                return;
            }
            OfflineServer.Instance.UseDoor(doors[int.Parse(args[1])]);
        }

        // building <n>: goes through doors.json building entrance n (arrival search included), recording the way back
        // as a click would. "building" alone lists them.
        private static void Building(string[] args)
        {
            var list = OfflineServer.Instance.BuildingList;
            if (args.Length < 2)
            {
                for (int i = 0; i < list.Count; i++)
                    Plugin.Log.LogInfo($"[dev] building {i}: scene {list[i].scene} -> {list[i].toScene} ({list[i].note})");
                return;
            }
            OfflineServer.Instance.DevEnterBuilding(list[int.Parse(args[1])]);
        }

        // hover <npc uid> | hover obj <typeId> | hover off: acts as if the mouse cursor were over that NPC or the nearest
        // object of that type, for the Interact key (F). With interactprompt, tests what F uses without a mouse.
        private static void Hover(string[] args)
        {
            UnityEngine.Component c = null;
            if (args.Length > 2 && args[1] == "obj")
            {
                int type = int.Parse(args[2]);
                var p = Scr_PlayerHandler.instance.player.transform.position;
                float best = float.MaxValue;
                foreach (var i in Scr_Interactable.list)
                    if (i != null && i.typeId == type && UnityEngine.Vector3.Distance(i.transform.position, p) < best)
                    {
                        best = UnityEngine.Vector3.Distance(i.transform.position, p);
                        c = i;
                    }
            }
            else if (args.Length > 1 && args[1] != "off")
                c = Scr_NpcHandler.instance.npcList.Find(n => n != null && n.npcUniqueId == int.Parse(args[1]));
            QuickInteract.DevHover = c;
            Plugin.Log.LogInfo($"[dev] hover {(c != null ? c.name + " at " + c.transform.position : "off")}");
        }

        // chairs [n]: the nearest n seats (type 54 interactables and Scr_Chair components) with their seat offsets.
        private static void Chairs(string[] args)
        {
            int n = args.Length > 1 ? int.Parse(args[1]) : 10;
            var p = Scr_PlayerHandler.instance.player.transform.position;
            var seen = new System.Collections.Generic.List<UnityEngine.Component>();
            foreach (var i in Scr_Interactable.list) if (i != null && i.typeId == 54) seen.Add(i);
            foreach (var c in UnityEngine.Object.FindObjectsOfType<Scr_Chair>()) if (c.GetComponent<Scr_Interactable>() == null) seen.Add(c);
            seen.Sort((a, b) => UnityEngine.Vector3.Distance(a.transform.position, p).CompareTo(UnityEngine.Vector3.Distance(b.transform.position, p)));
            Plugin.Log.LogInfo($"[dev] chairs: {seen.Count} in scene");
            for (int k = 0; k < seen.Count && k < n; k++)
            {
                var c = seen[k];
                var chair = c.GetComponent<Scr_Chair>();
                var inter = c.GetComponent<Scr_Interactable>();
                string seat = chair == null ? "no Scr_Chair" : chair.positionObject == null ? "no positionObject" : (chair.positionObject.position - c.transform.position).ToString();
                Plugin.Log.LogInfo($"[dev] chair '{c.name}' root='{c.transform.root.name}' at {c.transform.position} {UnityEngine.Vector3.Distance(c.transform.position, p):F1} m interactable={(inter != null ? inter.typeId.ToString() : "none")} seat {seat} childChairs={c.GetComponentsInChildren<Scr_Chair>().Length}");
            }
        }

        // sitall [from]: sits on every seat in the scene in turn, as a click does, and logs whether the sit held.
        private static void SitAll(string[] args) =>
            OfflineServer.Instance.StartCoroutine(SitAllRoutine(args.Length > 1 ? int.Parse(args[1]) : 0));

        private static System.Collections.IEnumerator SitAllRoutine(int from)
        {
            var seats = new System.Collections.Generic.List<Scr_Interactable>();
            foreach (var i in Scr_Interactable.list) if (i != null && i.typeId == 54) seats.Add(i);
            seats.Sort((a, b) => a.transform.position.x != b.transform.position.x ? a.transform.position.x.CompareTo(b.transform.position.x) : a.transform.position.z.CompareTo(b.transform.position.z));
            var player = Scr_PlayerHandler.instance.player;
            var server = HarmonyLib.Traverse.Create(OfflineServer.Instance).Field("sittingAt");
            int bad = 0;
            for (int k = from; k < seats.Count; k++)
            {
                var s = seats[k];
                var chair = s.GetComponent<Scr_Chair>();
                if (chair == null || chair.positionObject == null) { Plugin.Log.LogWarning($"[dev] sit {k} '{s.transform.root.name}' at {s.transform.position}: no seat point"); bad++; continue; }
                var seat = chair.positionObject.position;
                HarmonyLib.Traverse.Create(OfflineServer.Instance).Method("StandUp").GetValue();
                player.transform.position = seat + UnityEngine.Vector3.up * 0.2f;   // within chair reach
                yield return new UnityEngine.WaitForSeconds(0.6f);
                player.connecter.interactableHandler.interact(s);   // the client moves the player onto the seat, as a click does
                yield return new UnityEngine.WaitForSeconds(0.2f);
                Plugin.Log.LogInfo($"[dev] sit {k} after 0.2 s: server sitting={server.GetValue<UnityEngine.Vector3?>().HasValue} actionId={player.anim.actionId} player {player.transform.position}");
                yield return new UnityEngine.WaitForSeconds(2f);
                bool held = server.GetValue<UnityEngine.Vector3?>().HasValue;
                int action = player.anim.actionId;
                float drift = UnityEngine.Vector3.Distance(player.transform.position, seat);
                bool ok = held && action == 12 && drift < 0.5f;
                if (ok && k == from)
                {
                    player.transform.position += UnityEngine.Vector3.forward * 1f;   // stepping off must still stand up
                    yield return new UnityEngine.WaitForSeconds(0.5f);
                    bool stood = !server.GetValue<UnityEngine.Vector3?>().HasValue;
                    Plugin.Log.LogInfo($"[dev] sit {k}: stepping off {(stood ? "stood up" : "FAIL: still sitting")}");
                    if (!stood) bad++;
                }
                if (!ok) bad++;
                var log = $"[dev] sit {k} {(ok ? "ok" : "FAIL")} '{s.transform.root.name}/{s.name}' at {s.transform.position}: server sitting={held} actionId={action} drift={drift:F2} m (player {player.transform.position}, seat {seat})";
                if (ok) Plugin.Log.LogInfo(log); else Plugin.Log.LogWarning(log);
            }
            Plugin.Log.LogInfo($"[dev] sitall done: {seats.Count - from} seats, {bad} failed");
        }

        // useobj <typeId>: uses the nearest object of that type the way a mouse click does (the client's interact, with its
        // reach checks and the chair's move onto the seat).
        private static void UseObj(string[] args)
        {
            int type = int.Parse(args[1]);
            var player = Scr_PlayerHandler.instance.player;
            Scr_Interactable best = null;
            foreach (var i in Scr_Interactable.list)
                if (i != null && i.typeId == type && (best == null || UnityEngine.Vector3.Distance(i.transform.position, player.transform.position) < UnityEngine.Vector3.Distance(best.transform.position, player.transform.position)))
                    best = i;
            if (best == null) return;
            Plugin.Log.LogInfo($"[dev] useobj {type} at {UnityEngine.Vector3.Distance(best.transform.position, player.transform.position):F1} m");
            player.connecter.interactableHandler.interact(best);
        }

        // updatecheck <version>: reruns the GitHub update check as if that version were installed (e.g. 1.0.0).
        private static void UpdateCheck(string[] args) => UpdateOverlay.Recheck(args[1]);

        // weight: carried weight against capacity (server and client counts), and the player's run speed.
        private static void Weight(string[] args)
        {
            var server = OfflineServer.Instance;
            var m = Scr_PlayerHandler.instance.player.characterMovement.movement;
            Plugin.Log.LogInfo($"[dev] weight server {server.CarriedWeight()}/{server.CarryingCapacity()} client {Inventory.instance.GetInventoryWeight()}/{Scr_StatsHandler.instance.getStat(Scr_StatsHandler.StatType.CarryingCap)} speed fwd={m.maxForwardSpeed:0.00} back={m.maxBackwardsSpeed:0.00} side={m.maxSidewaysSpeed:0.00}");
        }

        // runtest [seconds]: autoruns forward (the game's Autorun) and logs the distance covered, to measure run speed.
        private static void RunTest(string[] args) =>
            OfflineServer.Instance.StartCoroutine(RunTestRoutine(args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 1f));

        private static System.Collections.IEnumerator RunTestRoutine(float seconds)
        {
            var p = Scr_PlayerHandler.instance.player;
            var start = p.transform.position;
            p.autoMoving = true;
            yield return new UnityEngine.WaitForSeconds(seconds);
            p.autoMoving = false;
            var moved = p.transform.position - start;
            float d = new UnityEngine.Vector2(moved.x, moved.z).magnitude;
            Plugin.Log.LogInfo($"[dev] runtest {d:0.00} m in {seconds:0.0} s = {d / seconds:0.00} m/s (max {p.characterMovement.movement.maxForwardSpeed:0.00})");
        }

        // die: kills the player as a fatal hit would (wakes at the home wayshrine after the respawn delay).
        private static void Die(string[] args)
        {
            var server = HarmonyLib.Traverse.Create(OfflineServer.Instance);
            server.Field("character").Field("currentHealth").SetValue(0);
            server.Method("PlayerDies").GetValue();
        }

        // canmove: whether the client lets the player move, and where they are.
        private static void CanMove(string[] args)
        {
            var p = Scr_PlayerHandler.instance.player;
            Plugin.Log.LogInfo($"[dev] canmove dead={p.dead} canControl={p.characterMovement.canControl} at {p.transform.position}");
        }

        // workbench: the crafting window the client shows (0 = none) and whether a craft is in progress.
        private static void Workbench(string[] args) =>
            Plugin.Log.LogInfo($"[dev] workbench {Menucontroller.instance.showWorkbench} crafting={Menucontroller.instance.craftingInProgress}");

        // windows: the client's open windows (types as in Script_WindowController: 2 inventory, 7 shop, 14 bank...).
        private static void Windows(string[] args)
        {
            var open = new System.Collections.Generic.List<string>();
            foreach (var w in HarmonyLib.Traverse.Create(Script_WindowController.instance).Field("windows").GetValue<System.Collections.Generic.List<Script_Window>>())
                if (w.windowActive) open.Add(w.windowType.ToString());
            Plugin.Log.LogInfo($"[dev] windows open: {string.Join(" ", open.ToArray())}");
        }

        // interactprompt [press]: the "Press [F] to ..." text, i.e. what the Interact key would use now; "press" uses it.
        private static void InteractPrompt(string[] args)
        {
            Plugin.Log.LogInfo($"[dev] interact prompt '{Menucontroller.instance.quickInteractText}'");
            var p = Scr_PlayerHandler.instance.player.transform.position;
            foreach (var i in Scr_Interactable.list)
                if (i != null && UnityEngine.Vector3.Distance(i.transform.position, p) < 8f)
                    Plugin.Log.LogInfo($"[dev]   object {i.typeId} '{i.interactableName}' {UnityEngine.Vector3.Distance(i.transform.position, p):F1} m noQuickInteract={i.noQuickInteract} extra={i.customDistance}");
            foreach (var n in Scr_NpcHandler.instance.npcList)
                if (n != null && UnityEngine.Vector3.Distance(n.transform.position, p) < 8f)
                    Plugin.Log.LogInfo($"[dev]   npc '{n.npcName}' {UnityEngine.Vector3.Distance(n.transform.position, p):F1} m");
            if (args.Length > 1 && args[1] == "press") QuickInteract.DevPress();
        }

        // doorsigns: every shop sign (Prefab_Object_Sign_*) in the scene with the three nearest doors and gates, to tell
        // which building a door belongs to.
        private static void DoorSigns(string[] args)
        {
            foreach (var t in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Transform>())
            {
                if (!t.name.StartsWith("Prefab_Object_Sign_") || !t.gameObject.scene.IsValid()) continue;   // culled signs are inactive
                var near = new System.Collections.Generic.List<Scr_Interactable>(UnityEngine.Resources.FindObjectsOfTypeAll<Scr_Interactable>());
                near.RemoveAll(i => !i.gameObject.scene.IsValid() || (i.typeId != 37 && i.typeId != 29));
                near.Sort((a, b) => UnityEngine.Vector3.Distance(a.transform.position, t.position).CompareTo(UnityEngine.Vector3.Distance(b.transform.position, t.position)));
                var parts = new System.Collections.Generic.List<string>();
                for (int i = 0; i < near.Count && i < 3; i++)
                    parts.Add($"{near[i].typeId} '{near[i].name}' {near[i].transform.position} {UnityEngine.Vector3.Distance(near[i].transform.position, t.position):F1} m");
                Plugin.Log.LogInfo($"[dev] sign '{t.name.Substring("Prefab_Object_Sign_".Length)}' at {t.position}: {string.Join("; ", parts.ToArray())}");
            }
        }

        // probe x y z: every collider on a vertical line through the point (60 m up and down) and within 4 m of it.
        private static void Probe(string[] args)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var p = new UnityEngine.Vector3(float.Parse(args[1], inv), float.Parse(args[2], inv), float.Parse(args[3], inv));
            var hits = UnityEngine.Physics.RaycastAll(p + UnityEngine.Vector3.up * 60f, UnityEngine.Vector3.down, 120f, ~0, UnityEngine.QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
                Plugin.Log.LogInfo($"[dev] hit y={h.point.y:F2} normal={h.normal} '{h.collider.name}' trigger={h.collider.isTrigger} active={h.collider.gameObject.activeInHierarchy} scene={h.collider.gameObject.scene.name}");
            foreach (var c in UnityEngine.Physics.OverlapSphere(p, 4f, ~0, UnityEngine.QueryTriggerInteraction.Collide))
                Plugin.Log.LogInfo($"[dev] near '{c.name}' at {c.transform.position} trigger={c.isTrigger} bounds={c.bounds.min}..{c.bounds.max}");
            Plugin.Log.LogInfo($"[dev] probe done ({hits.Length} hits)");
        }

        // clearspot x y z: runs the door arrival search at a point, logging why each candidate is rejected.
        private static void ClearSpot(string[] args)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var p = new UnityEngine.Vector3(float.Parse(args[1], inv), float.Parse(args[2], inv), float.Parse(args[3], inv));
            Plugin.Log.LogInfo($"[dev] clear spot -> {OfflineServer.Instance.ClearSpotNear(p, true)}");
        }

        // playerstate: the client's attack state for the local player (stuck-in-combat checks).
        // hitcheck <npc type> [ability id]: how much of your hit gets through that NPC's resistances.
        private static void HitCheck(string[] args) =>
            OfflineServer.Instance.DevHitCheck(int.Parse(args[1]), args.Length > 2 ? int.Parse(args[2]) : 0);

        // clientstats: base + bonus of the client's combat stats, as its stats window shows them.
        private static void ClientStats(string[] args)
        {
            var h = Scr_StatsHandler.instance;
            var types = new[] { Scr_StatsHandler.StatType.Health, Scr_StatsHandler.StatType.Mana, Scr_StatsHandler.StatType.Manaregen,
                Scr_StatsHandler.StatType.Damage, Scr_StatsHandler.StatType.DamageBlock, Scr_StatsHandler.StatType.ResistanceFire,
                Scr_StatsHandler.StatType.DamageSlash, Scr_StatsHandler.StatType.Movementspeed };
            var parts = new System.Collections.Generic.List<string>();
            foreach (var t in types) parts.Add(t + "=" + h.getStat(t));
            Plugin.Log.LogInfo("[dev] client stats: " + string.Join(" ", parts.ToArray()));
        }

        // devhealth <n>: the test character's base health (only for throwaway characters: it's saved).
        private static void DevHealth(string[] args) => OfflineServer.Instance.DevSetHealth(int.Parse(args[1]));

        // blowlog on|off: log every creature blow aimed at the player ([blow] lines), to check who attacks.
        private static void BlowLog(string[] args) => OfflineServer.LogBlows = args.Length < 2 || args[1] != "off";

        // flyers: the client's flying followers (parrot, bats): prefab type, model, and distance from the player.
        private static void Flyers(string[] args)
        {
            var me = Scr_PlayerHandler.instance.player.transform.position;
            foreach (var f in Scr_FlyingFollowers.instance.list)
            {
                if (f == null || f.gameObject == null) continue;
                var model = f.gameObject.transform.childCount > 0 ? f.gameObject.transform.GetChild(0).name : "-";
                Plugin.Log.LogInfo($"[dev] flyer {f.uniqueId} type {f.typeId} '{f.petname}' of {f.playerName} model {model} at {UnityEngine.Vector3.Distance(f.gameObject.transform.position, me):F1} m, found={f.playerFound}");
                foreach (var r in f.gameObject.GetComponentsInChildren<UnityEngine.Renderer>(true))
                    Plugin.Log.LogInfo($"[dev]   renderer {r.name} enabled={r.enabled} active={r.gameObject.activeInHierarchy} size={r.bounds.size} mat={(r.sharedMaterial != null ? r.sharedMaterial.name : "-")}");
            }
            Plugin.Log.LogInfo($"[dev] {Scr_FlyingFollowers.instance.list.Count} flying followers");
        }

        // spawnedobjects [from] [to]: what each spawned-object prefab (Resources/1LoadingAssets/SpawnedObjects/
        // ab_spawnedObject_N) contains: its objects, materials and particle systems, to identify them by name.
        private static void SpawnedObjects(string[] args)
        {
            int from = args.Length > 1 ? int.Parse(args[1]) : 1, to = args.Length > 2 ? int.Parse(args[2]) : 60;
            for (int i = from; i <= to; i++)
            {
                var go = UnityEngine.Resources.Load("1LoadingAssets/SpawnedObjects/ab_spawnedObject_" + i, typeof(UnityEngine.GameObject)) as UnityEngine.GameObject;
                if (go == null) continue;
                var names = new System.Collections.Generic.List<string>();
                foreach (var t in go.GetComponentsInChildren<UnityEngine.Transform>(true))
                    if (names.Count < 12) names.Add(t.name);
                var mats = new System.Collections.Generic.List<string>();
                foreach (var r in go.GetComponentsInChildren<UnityEngine.Renderer>(true))
                    if (r.sharedMaterial != null && !mats.Contains(r.GetType().Name + ":" + r.sharedMaterial.name)) mats.Add(r.GetType().Name + ":" + r.sharedMaterial.name);
                var scripts = new System.Collections.Generic.List<string>();
                foreach (var m in go.GetComponentsInChildren<UnityEngine.MonoBehaviour>(true))
                    if (m != null && !scripts.Contains(m.GetType().Name)) scripts.Add(m.GetType().Name);
                Plugin.Log.LogInfo($"[dev] spawned object {i}: {string.Join("/", names.ToArray())} | materials {string.Join(",", mats.ToArray())} | scripts {string.Join(",", scripts.ToArray())}");
            }
        }

        // npcparts <uid>: a client NPC's child objects with renderers: type, material, active and enabled, to find
        // an aura or effect that's part of its model.
        private static void NpcParts(string[] args)
        {
            var npc = Scr_NpcHandler.instance.getNpc(int.Parse(args[1]));
            if (npc == null) { Plugin.Log.LogInfo("[dev] npcparts: no such NPC"); return; }
            foreach (var r in npc.GetComponentsInChildren<UnityEngine.Renderer>(true))
                Plugin.Log.LogInfo($"[dev]   {r.transform.parent?.name}/{r.name} {r.GetType().Name} mat={(r.sharedMaterial != null ? r.sharedMaterial.name : "-")} active={r.gameObject.activeInHierarchy} enabled={r.enabled}");
            foreach (var l in npc.GetComponentsInChildren<UnityEngine.Light>(true))
                Plugin.Log.LogInfo($"[dev]   light {l.transform.parent?.name}/{l.name} {l.type} color={l.color} range={l.range:F1} intensity={l.intensity:F1} active={l.gameObject.activeInHierarchy} enabled={l.enabled}");
        }

        // npcpart <uid> <child name> on|off: shows or hides a named part of a client NPC's model (an aura, an orb).
        private static void NpcPart(string[] args)
        {
            var npc = Scr_NpcHandler.instance.getNpc(int.Parse(args[1]));
            string name = args[2].Replace('_', ' ');
            foreach (var t in npc.GetComponentsInChildren<UnityEngine.Transform>(true))
                if (t.name == name) { t.gameObject.SetActive(args[3] == "on"); Plugin.Log.LogInfo($"[dev] {name} {args[3]}"); }
        }

        // combatstats: combat level, armor alignment and effective resistances, server against client.
        private static void CombatStats(string[] args) => OfflineServer.Instance.DevCombatStats();

        private static void PlayerState(string[] args)
        {
            var pl = Scr_PlayerHandler.instance.player;
            Plugin.Log.LogInfo($"[dev] player attacking={pl.attacking} attackId={pl.attackId} anim.combat={pl.anim.combat} anim.attackId={pl.anim.attackId} dead={pl.dead}");
            var hand = Inventory.instance.equipSlots.Find(s => s.slot == Item.ItemSlot.Weapon)?.item;
            Plugin.Log.LogInfo($"[dev] player actionId={pl.actionId} anim.actionId={pl.anim.actionId} hand={(hand != null ? hand.itemTypeID + " " + hand.itemName : "empty")} back={pl.secondaryWeaponId}/{pl.secondaryWeaponTypeId}");
        }

        // playeranim: the player's animator layers (weight, state length, progress) to time attack animations.
        private static void PlayerAnim(string[] args)
        {
            var a = Scr_PlayerHandler.instance.player.anim.animator;
            var sb = new System.Text.StringBuilder();
            for (int l = 0; l < a.layerCount; l++)
            {
                var s = a.GetCurrentAnimatorStateInfo(l);
                var clips = a.GetCurrentAnimatorClipInfo(l);
                sb.Append($" L{l} w={a.GetLayerWeight(l):F2} len={s.length:F2} t={s.normalizedTime:F2} loop={s.loop} clip={(clips.Length > 0 ? clips[0].clip.name : "-")};");
            }
            Plugin.Log.LogInfo($"[dev] anim at {UnityEngine.Time.time:F2} attackId={a.GetInteger("AttackID")}{sb}");
        }

        // actionanim <id>: sets the player's animator ActionID (what 8/5 with action type 3 or 4 does), 0 to stop.
        // With playeranim, finds which id plays which clip.
        private static void ActionAnim(string[] args)
        {
            var p = Scr_PlayerHandler.instance.player;
            p.anim.actionId = int.Parse(args[1]);
            Plugin.Log.LogInfo($"[dev] actionId {p.anim.actionId}");
        }

        // npcpercent <uid> <percent>: sets an NPC's current health to that share of its maximum (boss health thresholds).
        private static void NpcPercent(string[] args) => OfflineServer.Instance.DevNpcPercent(int.Parse(args[1]), int.Parse(args[2]));

        // npchp <uid> <hp>: sets an NPC's health and max health (a training dummy for combat tests).
        private static void NpcHp(string[] args) => OfflineServer.Instance.DevSetNpcHealth(int.Parse(args[1]), int.Parse(args[2]));

        // swingprofile [seconds]: logs each attack animation's right-hand speed curve (when the blow strikes).
        private static void SwingProfile(string[] args) =>
            OfflineServer.Instance.DevProfileSwings(args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 10f);

        // invorder: server vs client inventory and bank row by row. Swaps and drags are sent as list indexes,
        // so the two orders must match; stacks must be id 0 on the client or their counts aren't drawn.
        private static void InvOrder(string[] args) => OfflineServer.Instance.CompareInventories();

        // devbank: opens the bank window as a banker does.
        private static void DevBank(string[] args) => OfflineServer.Instance.DevOpenBank();

        // sortinv: the inventory window's "By name" button (sorts the client list, then sends 198/16).
        private static void SortInv(string[] args)
        {
            var inv = Inventory.instance;
            inv.items.Sort((a, b) => string.Compare(a.item.itemName, b.item.itemName));
            Scr_RPCSender.instance.IssueSortInventory(2, true);
        }

        // bufficons [max]: which buff icons the client's resources hold (Tex_BuffIcon_<id>), and the size of each.
        private static void BuffIcons(string[] args)
        {
            int max = args.Length > 1 ? int.Parse(args[1]) : 200;
            var found = new System.Collections.Generic.List<string>();
            for (int i = 0; i <= max; i++)
            {
                var t = UnityEngine.Resources.Load<UnityEngine.Texture2D>("1LoadingAssets/Tex/BuffIcons/Tex_BuffIcon_" + i);
                if (t != null) found.Add(i + ":" + t.width + "x" + t.height);
            }
            Plugin.Log.LogInfo($"[dev] buff icons ({found.Count}): {string.Join(" ", found.ToArray())}");
        }

        // showbuff <id...>: puts those buffs' icons on the player's buff bar for a minute (to look at them).
        private static void ShowBuff(string[] args)
        {
            for (int i = 1; i < args.Length; i++) OfflineServer.Instance.DevShowBuff(int.Parse(args[i]));
        }

        // bountypress <name start>: the wanted board's button on that outlaw's line (start the hunt, or abandon it).
        private static void BountyPress(string[] args) => OfflineServer.Instance.DevBoardPress(string.Join(" ", args, 1, args.Length - 1));

        // gfxprobe: which particle shaders the build has, the particle materials in the scene, and what the local
        // player's worn models are made of (for effects on gear).
        private static void GfxProbe(string[] args)
        {
            foreach (var name in new[] { "Particles/Additive", "Legacy Shaders/Particles/Additive", "Particles/Alpha Blended", "Mobile/Particles/Additive", "Particles/Additive (Soft)", "Sprites/Default", "Unlit/Transparent" })
                Plugin.Log.LogInfo($"[dev] shader '{name}': {(UnityEngine.Shader.Find(name) != null ? "yes" : "no")}");
            var mats = new System.Collections.Generic.HashSet<string>();
            foreach (var r in UnityEngine.Object.FindObjectsOfType<UnityEngine.ParticleSystemRenderer>())
                if (r.sharedMaterial != null && mats.Add(r.sharedMaterial.name + " / " + r.sharedMaterial.shader.name) && mats.Count <= 25)
                    Plugin.Log.LogInfo($"[dev] particle material '{r.sharedMaterial.name}' shader '{r.sharedMaterial.shader.name}' on {r.gameObject.name}");
            var loader = Inventory.instance != null ? Inventory.instance.eqLoader : null;
            if (loader == null) return;
            foreach (var s in Inventory.instance.items)
                if (s.item.currentlyEquipped)
                    Plugin.Log.LogInfo($"[dev] worn {s.item.itemName} ({s.item.itemTypeID}, {s.item.itemSlot}): model {(UnityEngine.Resources.Load("1LoadingAssets/ab_eq_" + s.item.itemTypeID) != null ? "yes" : "none")}");
            foreach (var s in loader.eqSlots)
            {
                if (s.instancedObject == null) continue;
                foreach (var r in s.instancedObject.GetComponentsInChildren<UnityEngine.Renderer>(true))
                    Plugin.Log.LogInfo($"[dev] slot {s.slot} item {s.itemTypeId}: {r.GetType().Name} '{r.name}' shader '{r.sharedMaterial?.shader.name}' bounds {r.bounds.size}");
            }
        }

        // gearfx <grade>: shows that grade's shimmer on everything the player wears (0 = the real grades again).
        private static void GearFx(string[] args)
        {
            int grade = int.Parse(args[1]);
            var inv = Inventory.instance;
            foreach (var s in inv.eqLoader.eqSlots)
            {
                if (s.instancedObject == null) continue;
                var item = inv.getEquippedItem(s.slot);
                GradeAura.Apply(s.instancedObject, grade > 0 ? grade : item != null ? item.itemGrade : 0);
                foreach (var ps in s.instancedObject.GetComponentsInChildren<UnityEngine.ParticleSystem>())
                    Plugin.Log.LogInfo($"[dev] gearfx {s.slot}: {ps.transform.parent.name} lossy {ps.transform.lossyScale} rate {ps.emission.rateOverTime.constant} size {ps.main.startSize.constantMax}");
            }
        }

        // gearfxinfo: a few live sparkles of each worn piece: where they are (from the player) and how big.
        private static void GearFxInfo(string[] args)
        {
            var me = Scr_PlayerHandler.instance.player.transform.position;
            var buf = new UnityEngine.ParticleSystem.Particle[3];
            foreach (var ps in UnityEngine.Object.FindObjectsOfType<UnityEngine.ParticleSystem>())
            {
                if (ps.name != "GSO grade aura") continue;
                int n = ps.GetParticles(buf);
                var r = ps.GetComponent<UnityEngine.ParticleSystemRenderer>();
                Plugin.Log.LogInfo($"[dev] gearfx: {ps.particleCount} live, playing {ps.isPlaying}, at {ps.transform.position - me}, box {ps.shape.scale}, layer {ps.gameObject.layer}, rate {ps.emission.rateOverTime.constant}, renderer {r.enabled} material {r.sharedMaterial?.shader.name}");
                for (int i = 0; i < n; i++)
                    Plugin.Log.LogInfo($"[dev]   at {buf[i].position - me} size {buf[i].GetCurrentSize(ps)} colour {buf[i].GetCurrentColor(ps)}");
            }
        }

        // huntage <seconds>: takes that much playing time off every active bounty's hour.
        private static void HuntAge(string[] args) => OfflineServer.Instance.DevHuntAge(float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture));

        // loot: the loot bags the server holds in this scene, and how many the client shows.
        private static void Loot(string[] args) => OfflineServer.Instance.LogLoot();

        // lootage <seconds>: ages every bag on the ground by that much playing time (bags go at 0).
        private static void LootAge(string[] args) => OfflineServer.Instance.DevAgeLoot(float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture));

        // lootroll <npcType> [n]: rolls that NPC type's drop table n times (default 1000) and logs the totals.
        private static void LootRoll(string[] args) =>
            OfflineServer.LogLootRolls(int.Parse(args[1]), args.Length > 2 ? int.Parse(args[2]) : 1000);

        // killnpc <uid>: kills a visible NPC as if the player had (xp, loot bag, quest triggers).
        private static void KillNpc(string[] args) => OfflineServer.Instance.DevKill(int.Parse(args[1]));

        // spawnground: checks the original server's spawns in this scene (Data/spawns.json) against the ground.
        private static void SpawnGround(string[] args) => OfflineServer.Instance.LogSpawnGround();

        // setgrade <itemId> <grade>: sets an item's grade (1 damaged - 8 mythic), for testing upgrades.
        private static void SetGrade(string[] args) => OfflineServer.Instance.DevSetGrade(int.Parse(args[1]), int.Parse(args[2]));

        // where <name>: every loaded object whose name contains the word, with its position and renderer bounds.
        private static void Where(string[] args)
        {
            foreach (var t in UnityEngine.Object.FindObjectsOfType<UnityEngine.Transform>())
            {
                if (t.name.IndexOf(args[1], System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var r = t.GetComponent<UnityEngine.Renderer>();
                Plugin.Log.LogInfo($"[dev] where '{t.name}' at {t.position}" + (r != null ? $" bounds {r.bounds.min} - {r.bounds.max}" : ""));
            }
            Plugin.Log.LogInfo("[dev] where done");
        }

        // objs: the spawned objects the client shows (gates, lifts, oysters...), with their positions.
        private static void Objs(string[] args)
        {
            foreach (var so in Scr_SpawnedObjectHandler.instance.objectList)
                if (so != null) Plugin.Log.LogInfo($"[dev] object {so.uniqueId} type {so.typeId} at {so.transform.position} rot {so.transform.eulerAngles.y:0}");
            Plugin.Log.LogInfo("[dev] objs done");
        }

        // silver <amount>: sets the test character's silver (boat rides, shops).
        private static void Silver(string[] args) => OfflineServer.Instance.SetSilver(int.Parse(args[1]));

        // harpy [spawn [route 0-4] | hit [damage] | takeoff | back]: where Aellope is and what she's doing; spawn puts her on a
        // route now, hit deals her damage (default 1) as the player (in the air: she dives at you).
        private static void Harpy(string[] args) => OfflineServer.Instance.DevHarpy(args);

        // fatelock <seconds>: the fate brokers' lock ends in that many seconds (negative: already over, told at the next check).
        private static void FateLock(string[] args) => OfflineServer.Instance.DevFateLock(int.Parse(args[1]));

        // setlevel <skillId> <level>: raises a skill to that level (Scr_SkillsHandler ids), for testing abilities.
        private static void SetLevel(string[] args) => OfflineServer.Instance.DevSetLevel(int.Parse(args[1]), int.Parse(args[2]));

        // worldnpcs [type...]: the server's NPCs of those types in this scene (all if none), including hidden quest copies.
        private static void WorldNpcs(string[] args)
        {
            var types = new int[args.Length - 1];
            for (int i = 1; i < args.Length; i++) types[i - 1] = int.Parse(args[i]);
            OfflineServer.Instance.LogWorldNpcs(types);
        }

        // buffs: the player's active buffs and what they do.
        private static void Buffs(string[] args) => OfflineServer.Instance.LogBuffs();

        // projectiles: the client's projectiles in flight (id, position, target).
        private static void Projectiles(string[] args)
        {
            var list = Scr_ProjectileHandler.instance.projectiles;
            int live = 0;
            foreach (var p in list)
            {
                if (p == null) continue;
                live++;
                Plugin.Log.LogInfo($"[dev] projectile {p.id} at {p.transform.position} speed {p.speed} model={(p.projectileObject != null ? p.projectileObject.name : "none")}");
            }
            Plugin.Log.LogInfo($"[dev] projectiles in flight: {live}");
        }

        // fx <id> / sfx <id>: play an effect 3 m in front of the player ("_GFX IDs"), or a sound at the player.
        private static void Fx(string[] args) => OfflineServer.Instance.DevEffect(int.Parse(args[1]), false);
        private static void Sfx(string[] args) => OfflineServer.Instance.DevEffect(int.Parse(args[1]), true);

        // openurl <url>: Application.OpenURL as a menu link would call it (checks OpenUrlPatch).
        private static void OpenUrl(string[] args)
        {
            UnityEngine.Application.OpenURL(args[1]);
            Plugin.Log.LogInfo($"[dev] openurl {args[1]} returned");
        }

        // animclips: the client's shared animation table (the ids NPC animation messages refer to).
        private static void AnimClips(string[] args)
        {
            var clips = EasyAnimationHandler.instance.animations;
            for (int i = 0; i < clips.Length; i++)
                Plugin.Log.LogInfo($"[dev] anim {i}: {(clips[i] != null ? clips[i].name + " " + clips[i].length.ToString("F2") + "s" : "null")}");
        }

        // sounds: the client's sounds playing right now (ids as in Audio_Sound_<id>.wav).
        private static void Sounds(string[] args)
        {
            var ids = new System.Collections.Generic.List<string>();
            foreach (var s in Scr_AudioHandler.instance.sounds)
                if (s != null) ids.Add(s.id.ToString());
            Plugin.Log.LogInfo($"[dev] sounds playing: {string.Join(" ", ids.ToArray())}");
        }

        // npcground [n]: the nearest n client NPCs' height above the ground under them (sunken NPCs read negative).
        private static void NpcGround(string[] args) => OfflineServer.Instance.LogNpcGround(args.Length > 1 ? int.Parse(args[1]) : 15);

        // npcobstacles on|off: whether NPCs steer around rocks and walls (off = the old walk-through, to compare).
        private static void NpcObstacles(string[] args)
        {
            OfflineServer.NpcObstaclesOff = args.Length > 1 && args[1] == "off";
            Plugin.Log.LogInfo($"[dev] NPC obstacle checks {(OfflineServer.NpcObstaclesOff ? "off" : "on")}");
        }

        // npcbounds [n]: the nearest n client NPCs with their rendered size, how far the model's feet are
        // from the ground under them, and the animation playing. Finds giant, sunken or unanimated NPCs.
        private static void NpcBounds(string[] args)
        {
            int n = args.Length > 1 ? int.Parse(args[1]) : 15;
            var me = Scr_PlayerHandler.instance.player.transform.position;
            var list = new System.Collections.Generic.List<Scr_Npc>(Scr_NpcHandler.instance.npcList);
            list.RemoveAll(x => x == null);
            list.Sort((a, b) => (a.transform.position - me).sqrMagnitude.CompareTo((b.transform.position - me).sqrMagnitude));
            for (int i = 0; i < list.Count && i < n; i++)
            {
                var npc = list[i];
                var p = npc.transform.position;
                var b = new UnityEngine.Bounds(p, UnityEngine.Vector3.zero);
                foreach (var r in npc.GetComponentsInChildren<UnityEngine.Renderer>())
                    if (r.enabled && (r is UnityEngine.SkinnedMeshRenderer || r is UnityEngine.MeshRenderer)) b.Encapsulate(r.bounds);
                string ground = UnityEngine.Physics.Raycast(p + UnityEngine.Vector3.up * 5f, UnityEngine.Vector3.down, out var hit, 50f, ~0, UnityEngine.QueryTriggerInteraction.Ignore)
                    ? (b.min.y - hit.point.y).ToString("F2") + " (" + hit.collider.name + ")" : "none";
                string anim = npc.legacyAnimations ? "legacy " + npc.eAnimatorLegacy.currentAnimationId
                    : npc.eAnimator != null ? "easy " + npc.eAnimator.currentAnimationId : "no animator";
                Plugin.Log.LogInfo($"[dev] {npc.npcUniqueId} type={npc.npcTypeId} '{npc.npcName}' d={UnityEngine.Vector3.Distance(p, me):F1} size={b.size.x:F1}x{b.size.y:F1}x{b.size.z:F1} feet-above-ground={ground} anim={anim}");
            }
        }
    }
}
