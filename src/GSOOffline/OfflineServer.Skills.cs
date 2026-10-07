using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    public partial class OfflineServer
    {
        private const int ActionHarvest = 1;
        private const int ActionCraft = 2;

        private class SkillJob
        {
            public int type;            // ActionHarvest / ActionCraft
            public int target;          // harvestable uid / recipe id
            public float due;           // Time.time of the next completion
            public Vector3 startPos;
        }

        private SkillJob job;

        private static readonly Dictionary<string, string> ToolFlag = new Dictionary<string, string>
        {
            { "hatchet", "isHatchet" }, { "pickaxe", "isPickaxe" }, { "fishingrod", "isFishingRod" },
            { "scythe", "isScythe" }, { "sickle", "isSickle" }, { "shovel", "isShovel" },
        };

        private void RegisterSkillHandlers()
        {
            On(8, 4, c => StartAction((int)c[2], (int)c[3]));
            On(6, 25, c => { CancelJob(); autoAttacking = false; });
        }

        private static int SkillIdByName(string name)
        {
            foreach (var kv in SkillById)
                if (string.Equals(kv.Value, name, System.StringComparison.OrdinalIgnoreCase)) return kv.Key;
            return 0;
        }

        private int SkillLevel(int skillId) =>
            SkillById.TryGetValue(skillId, out var key) ? LevelFromXp(character.GetXp(key)) : 1;

        // The client hard-codes the basic tools (e.g. the starter pickaxe has no isPickaxe flag), so ask it.
        private static bool IsTool(string tool, int typeId)
        {
            var ih = Scr_ItemHandler.instance;
            switch (tool)
            {
                case "pickaxe": return ih.isPickaxe(typeId);
                case "hatchet": return ih.isHatchet(typeId);
                case "fishingrod": return ih.isFishingRod(typeId);
                case "sickle": return ih.isSickle(typeId);
                case "scythe": return ih.isScythe(typeId);
            }
            return ToolFlag.TryGetValue(tool, out var flag) && ItemData.Get(typeId)?.flags.Contains(flag) == true;
        }

        private bool HasTool(string tool)
        {
            if (string.IsNullOrEmpty(tool) || tool == "none") return true;
            foreach (var it in character.items)
                if (IsTool(tool, it.typeId)) return true;
            return false;
        }

        private void StartAction(int type, int target)
        {
            if (character == null || !sceneReady) return;
            var player = LocalPlayer;
            if (player == null) return;

            float time;
            bool ok;
            if (type == ActionHarvest) ok = CanHarvest(target, out time);
            else if (type == ActionCraft) ok = CanCraft(target, out time);
            else
            {
                Plugin.Log.LogWarning($"Unhandled action type {type} (target {target})");
                return;
            }
            Plugin.Log.LogInfo($"[skill] action {type} on {target}: {(ok ? "started, " + time + "s" : "refused")}");
            if (!ok)
            {
                Send(3, 14, character.name, type);
                return;
            }

            job = new SkillJob { type = type, target = target, due = Time.time + time, startPos = player.transform.position };
            Send(8, 5, character.name, type, target);
        }

        private void CancelJob(string reason = "cancelled")
        {
            if (job == null || character == null) return;
            Plugin.Log.LogInfo($"[skill] action {job.type} on {job.target} stopped: {reason}");
            Send(3, 14, character.name, job.type);
            job = null;
        }

        private void TickSkills()
        {
            if (job == null) return;
            var player = LocalPlayer;
            // Walking away (or a scene change) interrupts the action, as clicking elsewhere would.
            if (player == null || !sceneReady || Vector3.Distance(player.transform.position, job.startPos) > 3f)
            {
                CancelJob("player moved");
                return;
            }
            if (Time.time < job.due) return;
            if (job.type == ActionHarvest) HarvestTick();
            else CraftTick();
        }

        // ---- harvesting ----------------------------------------------------------------------

        private bool CanHarvest(int uid, out float time)
        {
            time = 0f;
            var h = world?.GetHarvestable(uid);
            if (h == null || h.dead || !SkillData.Harvestables.TryGetValue(h.typeId, out var info))
            {
                Plugin.Log.LogInfo($"[skill] harvestable {uid}: {(h == null ? "unknown" : h.dead ? "depleted" : "no info for type " + h.typeId)}");
                return false;
            }
            time = info.harvestTime;
            float dist = Vector3.Distance(LocalPlayer.transform.position, h.pos);
            // Loose: the client already gates harvesting on its own proximity prompt, and its node
            // transforms sit a few metres off the exported marker positions.
            if (dist > 20f)
            {
                Plugin.Log.LogInfo($"[skill] harvestable {uid} too far ({dist:F1}m)");
                return false;
            }
            if (!HasTool(info.tool))
            {
                Notice($"You need a {info.tool.Replace("fishingrod", "fishing rod")} to do that.", "red");
                return false;
            }
            int skill = SkillIdByName(info.skill);
            if (skill != 0 && SkillLevel(skill) < info.level)
            {
                Notice($"You need level {info.level} {info.skill} to harvest {info.name}.", "red");
                return false;
            }
            return true;
        }

        private void HarvestTick()
        {
            var h = world?.GetHarvestable(job.target);
            if (h == null || h.dead || !SkillData.Harvestables.TryGetValue(h.typeId, out var info))
            {
                CancelJob();
                return;
            }
            job.due = Time.time + info.harvestTime;

            int skill = SkillIdByName(info.skill);
            int level = skill != 0 ? SkillLevel(skill) : 1;
            bool gotMain = false;
            for (int i = 0; i < info.drops.Count; i++)
            {
                var d = info.drops[i];
                if (Random.Range(0, 1000) >= d.dropRate + d.dropRateBoost * level) continue;
                int amount = Random.Range(d.min, d.max + 1) + d.amountBoost * (level / 10);
                GiveItem(d.item, Mathf.Max(1, amount));
                if (i == 0) gotMain = true;
            }
            if (!gotMain) return;   // keep swinging

            if (skill != 0) AddXp(skill, SkillData.BaseXp(info.level));
            if (--h.health > 0) return;

            h.dead = true;
            h.respawnAt = Time.time + info.respawnTime;
            Send(13, 1, h.uid, true);
            Send(3, 13, character.name, ActionHarvest);
            job = null;
        }

        private void TickRespawns()
        {
            if (world == null) return;
            foreach (var h in world.harvestables)
            {
                if (!h.dead || Time.time < h.respawnAt) continue;
                h.dead = false;
                h.health = world.RollHarvestableHealth(h.typeId);
                Send(13, 1, h.uid, false);
            }
        }

        // ---- crafting ------------------------------------------------------------------------

        private bool CanCraft(int recipeId, out float time)
        {
            time = 0f;
            if (!SkillData.Recipes.TryGetValue(recipeId, out var r)) return false;
            time = Mathf.Max(0.5f, r.time);
            if (r.skill != 0 && SkillLevel(r.skill) < r.level)
            {
                Notice($"You need level {r.level} {SkillById[r.skill]} to make that.", "red");
                return false;
            }
            foreach (var m in r.materials)
            {
                if (CountItem(m[0]) < m[1])
                {
                    Notice("You don't have the materials for that.", "red");
                    return false;
                }
            }
            return true;
        }

        private void CraftTick()
        {
            int recipeId = job.target;
            job = null;
            if (!CanCraft(recipeId, out _))
            {
                Send(3, 14, character.name, ActionCraft);
                return;
            }
            var r = SkillData.Recipes[recipeId];
            foreach (var m in r.materials) TakeItems(m[0], m[1]);
            GiveItem(r.product, r.amount);
            if (r.skill != 0) AddXp(r.skill, Mathf.RoundToInt(SkillData.BaseXp(r.level) * r.xpMultiplier));
            // The client re-issues startAction for the next item in a batch when it sees completion.
            Send(3, 13, character.name, ActionCraft);
        }
    }
}
