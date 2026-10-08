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

        // animclips: the client's shared animation table (the ids NPC animation messages refer to).
        private static void AnimClips(string[] args)
        {
            var clips = EasyAnimationHandler.instance.animations;
            for (int i = 0; i < clips.Length; i++)
                Plugin.Log.LogInfo($"[dev] anim {i}: {(clips[i] != null ? clips[i].name + " " + clips[i].length.ToString("F2") + "s" : "null")}");
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
