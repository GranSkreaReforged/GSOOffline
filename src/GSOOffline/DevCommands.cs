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
