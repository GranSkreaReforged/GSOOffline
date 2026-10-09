using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Quest glue from content.json beyond conversations: which copy of a quest NPC exists in each phase,
    /// quest-only NPCs spawned for a phase, and triggers that advance quests on game events.
    /// </summary>
    public partial class OfflineServer
    {
        private float enterTriggerTimer;

        // ---- NPC copies per phase ------------------------------------------------------------

        /// <summary>
        /// Whether an NPC exists for the player right now. NPC types without rules always do; a type with rules
        /// exists while any of its rules holds.
        /// </summary>
        private bool NpcShown(NpcEntity npc)
        {
            if (character == null || !Content.NpcRules.TryGetValue(npc.typeId, out var rules)) return true;
            foreach (var r in rules)
                if (RuleHolds(r)) return true;
            return false;
        }

        private bool RuleHolds(NpcRule r)
        {
            if (r.hidden) return false;
            if (r.quest != 0 && r.phases != null && r.phases.Count > 0 && !r.phases.Contains(character.GetQuestPhase(r.quest)))
                return false;
            return r.Holds(character, GetQuestVar);
        }

        /// <summary>Quest NPCs with no marker in the scene, created where content.json says.</summary>
        internal static IEnumerable<KeyValuePair<int, Vector3>> SpawnsFor(int sceneId)
        {
            foreach (var list in Content.NpcRules.Values)
                foreach (var r in list)
                    if (r.scene == sceneId && r.spawn != null)
                        foreach (string p in r.spawn)
                            yield return new KeyValuePair<int, Vector3>(r.type, GameData.ParseVec(p));
        }

        // ---- triggers ------------------------------------------------------------------------

        private bool TriggerReady(TriggerDef t, string on)
        {
            if (t.on != on || character.GetQuestPhase(t.quest) != t.phase) return false;
            return t.Holds(character, GetQuestVar);
        }

        private bool InTriggerArea(TriggerDef t)
        {
            if (string.IsNullOrEmpty(t.pos)) return t.scene == 0 || t.scene == character.scene;
            var player = LocalPlayer;
            if (player == null || (t.scene != 0 && t.scene != character.scene)) return false;
            return Vector3.Distance(player.transform.position, GameData.ParseVec(t.pos)) <= t.radius;
        }

        private void FireTrigger(TriggerDef t)
        {
            Plugin.Log.LogInfo($"[quest] trigger {t.on} for quest {t.quest} phase {t.phase}{(t.note != null ? ": " + t.note : string.Empty)}");
            if (t.action > 0) RunAction(t.action, 0);
            if (t.setPhase != 0) SetQuestPhase(t.quest, t.setPhase);
        }

        /// <summary>Area triggers, checked once a second.</summary>
        private void TickQuestTriggers()
        {
            if (character == null || !sceneReady) return;
            enterTriggerTimer -= Time.deltaTime;
            if (enterTriggerTimer > 0f) return;
            enterTriggerTimer = 1f;
            foreach (var t in Content.Triggers.ToArray())
                if (TriggerReady(t, "enter") && InTriggerArea(t)) FireTrigger(t);
        }

        /// <summary>A chat line: "say" triggers whose text it contains.</summary>
        private void OnSay(string message)
        {
            foreach (var t in Content.Triggers.ToArray())
                if (TriggerReady(t, "say") && !string.IsNullOrEmpty(t.text)
                    && message.ToLowerInvariant().Contains(t.text.ToLowerInvariant()) && InTriggerArea(t))
                    FireTrigger(t);
        }

        /// <summary>Using or dropping an item: returns true if a quest took it (the normal use is skipped).</summary>
        private bool OnQuestItemUsed(int typeId)
        {
            if (character == null) return false;
            foreach (var t in Content.Triggers.ToArray())
            {
                if (!TriggerReady(t, "use") || t.item != typeId) continue;
                if (!InTriggerArea(t))
                {
                    Plugin.Log.LogInfo($"[quest] {ItemData.Get(typeId)?.name} used outside the trigger area of quest {t.quest}");
                    continue;
                }
                if (t.consume) TakeItems(typeId, 1);
                FireTrigger(t);
                return true;
            }
            return false;
        }

        /// <summary>Dev: the server's NPCs of some types in this scene, shown or not (bridge command worldnpcs).</summary>
        internal void LogWorldNpcs(int[] types)
        {
            if (world == null) return;
            foreach (var n in world.npcs)
                if (types.Length == 0 || System.Array.IndexOf(types, n.typeId) >= 0)
                    Plugin.Log.LogInfo($"[dev] scene {world.sceneId} npc {n.uid} type {n.typeId} '{NpcName(n.typeId)}' at {SceneWorld.Vec(n.pos)} shown={NpcShown(n)} dead={n.dead} hp={n.health}/{n.maxHealth}");
        }

        // ---- action helpers ------------------------------------------------------------------

        private void SetQuestVar(int quest, int var, int value)
        {
            foreach (var v in character.questVars)
                if (v.quest == quest && v.var == var)
                {
                    v.value = value;
                    return;
                }
            character.questVars.Add(new QuestVar { quest = quest, var = var, value = value });
        }

        /// <summary>Opens a conversation from the server side, as if the player had clicked the speaker.</summary>
        private void StartDialogue(int node, int npcType)
        {
            NpcEntity speaker = null;
            var player = LocalPlayer;
            if (world != null && player != null && npcType > 0)
            {
                float best = float.MaxValue;
                foreach (var n in world.npcs)
                {
                    if (n.typeId != npcType || n.dead || !NpcShown(n)) continue;
                    float d = Vector3.Distance(NpcPosition(n), player.transform.position);
                    if (d < best) { best = d; speaker = n; }
                }
            }
            if (speaker != null)
            {
                dialogueNpc = speaker;
                Send(3, 10, character.name, speaker.uid);
            }
            EnterNode(node);
        }
    }
}
