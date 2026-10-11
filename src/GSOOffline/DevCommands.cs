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

        // npchp <uid> <hp>: sets an NPC's health and max health (a training dummy for combat tests).
        private static void NpcHp(string[] args) => OfflineServer.Instance.DevSetNpcHealth(int.Parse(args[1]), int.Parse(args[2]));

        // swingprofile [seconds]: logs each attack animation's right-hand speed curve (when the blow strikes).
        private static void SwingProfile(string[] args) =>
            OfflineServer.Instance.DevProfileSwings(args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 10f);

        // invorder: server vs client inventory and bank row by row. Swaps and drags are sent as list indexes,
        // so the two orders must match; stacks must be id 0 on the client or their counts aren't drawn.
        private static void InvOrder(string[] args) => OfflineServer.Instance.CompareInventories();

        // sortinv: the inventory window's "By name" button (sorts the client list, then sends 198/16).
        private static void SortInv(string[] args)
        {
            var inv = Inventory.instance;
            inv.items.Sort((a, b) => string.Compare(a.item.itemName, b.item.itemName));
            Scr_RPCSender.instance.IssueSortInventory(2, true);
        }

        // loot: the loot bags the server holds in this scene, and how many the client shows.
        private static void Loot(string[] args) => OfflineServer.Instance.LogLoot();

        // lootroll <npcType> [n]: rolls that NPC type's drop table n times (default 1000) and logs the totals.
        private static void LootRoll(string[] args) =>
            OfflineServer.LogLootRolls(int.Parse(args[1]), args.Length > 2 ? int.Parse(args[2]) : 1000);

        // killnpc <uid>: kills a visible NPC as if the player had (xp, loot bag, quest triggers).
        private static void KillNpc(string[] args) => OfflineServer.Instance.DevKill(int.Parse(args[1]));

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
