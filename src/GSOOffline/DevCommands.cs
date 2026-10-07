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
    }
}
