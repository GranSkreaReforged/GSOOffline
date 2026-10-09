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

        /// <summary>Clicking an object (3/11): returns true if a quest trigger handled it.</summary>
        private bool OnObjectClicked(int objectType)
        {
            if (character == null) return false;
            bool hit = false;
            foreach (var t in Content.Triggers.ToArray())
                if (TriggerReady(t, "interact") && t.objectType == objectType && InTriggerArea(t)
                    && (t.item == 0 || CountItem(t.item) >= System.Math.Max(1, t.amount)))
                {
                    FireTrigger(t);
                    hit = true;
                }
            return hit;
        }

        /// <summary>"quest" triggers: run when a quest reaches a phase (e.g. finishing one quest starts the next).</summary>
        private void OnQuestPhaseReached(int quest)
        {
            foreach (var t in Content.Triggers.ToArray())
                if (t.quest == quest && TriggerReady(t, "quest") && InTriggerArea(t)) FireTrigger(t);
        }

        /// <summary>Puts NPCs next to the player for this visit to the scene (summoned ghosts, ambushes).</summary>
        private void SpawnNearPlayer(List<int> types)
        {
            var player = LocalPlayer;
            if (world == null || player == null || types == null) return;
            foreach (int type in types)
            {
                if (world.npcs.Exists(n => n.typeId == type && !n.dead && Vector3.Distance(n.pos, player.transform.position) < 40f))
                    continue;   // already here: saying the name twice doesn't summon two
                Vector2 o = Random.insideUnitCircle.normalized * 4f;
                Vector3 p = player.transform.position + new Vector3(o.x, 0f, o.y);
                if (Physics.Raycast(p + Vector3.up * 10f, Vector3.down, out var hit, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    p = hit.point;
                int hp = GameData.Npcs.TryGetValue(type, out var info) ? info.health : 30;
                int uid = world.sceneId * 10000 + 9500 + world.npcs.Count;
                world.npcs.Add(new NpcEntity { uid = uid, typeId = type, spawnPos = p, pos = p, health = hp, maxHealth = hp });
                Plugin.Log.LogInfo($"[quest] spawned {NpcName(type)} ({type}) as {uid} at {SceneWorld.Vec(p)}");
            }
            visibilityTimer = 0f;   // send the new NPC list right away
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
