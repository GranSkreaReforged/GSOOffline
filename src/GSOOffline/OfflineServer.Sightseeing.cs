using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Quest 30, "Sightseeing" (update 133): Nora, staying at the Salty Wound inn in Grimwall, wants a tour of
    /// West Athagos. While it runs her travelling copy (type 290) walks after the player, and "Yes, we're here"
    /// (dialogue action 70) moves the tour on when the player has brought her to the stop the quest log names.
    /// Conversations, the golem and her NPC copies are in content.json; the walking and the stops are here.
    /// </summary>
    public partial class OfflineServer
    {
        private const int QuestSightseeing = 30, NoraAtInn = 289, NoraFollowing = 290;
        private const int ActionWereHere = 70, ActionGolemSighted = 1020;
        private const int NodeBackAtInn = 2170;
        private float followTimer;

        // Each phase's stop in West Athagos (scene 1): centre and how close counts as "here". Phase 4's is the
        // golem's forest, where content.json's enter trigger has Nora spot it on her own.
        private static readonly Dictionary<int, KeyValuePair<Vector3, float>> SightseeingStops =
            new Dictionary<int, KeyValuePair<Vector3, float>>
            {
                { 1, Stop(829.7f, 20f, 1599.2f, 45f) },   // the Yorkhill Monastery (its wayshrine)
                { 2, Stop(840f, 5f, 1192f, 40f) },        // the farm's cows
                { 3, Stop(482.8f, 4f, 1628.6f, 45f) },    // the beach at the Coral Cave entrance
                { 4, Stop(153f, 17f, 1262f, 35f) },       // the golem, west of Grimwall
                { 5, Stop(714f, 4.8f, 1383f, 12f) },      // back at the inn
            };

        private static KeyValuePair<Vector3, float> Stop(float x, float y, float z, float r) =>
            new KeyValuePair<Vector3, float>(new Vector3(x, y, z), r);

        private int SightseeingPhase => character != null ? character.GetQuestPhase(QuestSightseeing) : 0;
        private bool NoraFollows => SightseeingPhase >= 1 && SightseeingPhase <= 5 && GetQuestVar(QuestSightseeing, 1) == 1;

        /// <summary>Dialogue actions done in code; true if <paramref name="id"/> was one.</summary>
        private bool RunCodedAction(int id)
        {
            if (id != ActionWereHere) return false;
            SightseeingArrived();
            return true;
        }

        /// <summary>"Yes, we're here": the tour moves on if the player is at this phase's stop.</summary>
        private void SightseeingArrived()
        {
            int phase = SightseeingPhase;
            var player = LocalPlayer;
            if (player == null || character.scene != 1 || !SightseeingStops.TryGetValue(phase, out var stop)
                || Vector3.Distance(player.transform.position, stop.Key) > stop.Value)
            {
                Notice("Nora: Hmm, this doesn't look like it.", "gray");
                return;
            }
            Plugin.Log.LogInfo($"[quest] Sightseeing: arrived at stop {phase}");
            if (phase == 4) RunAction(ActionGolemSighted, 0);      // the golem: "Woah! Now kill it!"
            else if (phase == 5) StartDialogue(NodeBackAtInn, NoraFollowing);   // thanks and her reward
            else SetQuestPhase(QuestSightseeing, phase + 1);
        }

        /// <summary>
        /// Talking to Nora at the inn while the tour is paused ("I have to go, can we continue later?") picks it
        /// up again. The game has no line for that, so she answers with a short notice. True if it handled the click.
        /// </summary>
        private bool ResumeSightseeing(NpcEntity npc)
        {
            if (npc.typeId != NoraAtInn || SightseeingPhase < 1 || SightseeingPhase > 5 || NoraFollows) return false;
            SetQuestVar(QuestSightseeing, 1, 1);
            Notice("Nora: Ready when you are! Lead the way.");
            visibilityTimer = 0f;   // swap the inn copy for the travelling one now
            return true;
        }

        /// <summary>
        /// A spot on the floor 2.5 m from the player. The ground is looked for from just above the player's own
        /// height, so indoors it's the floor, not the roof.
        /// </summary>
        private static Vector3 BesidePlayer(Vector3 me)
        {
            Vector2 o = Random.insideUnitCircle.normalized * 2.5f;
            Vector3 p = me + new Vector3(o.x, 0f, o.y);
            if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point;
            return new Vector3(p.x, me.y, p.z);
        }

        /// <summary>Twice a second: keep the travelling Nora beside the player while the tour is on.</summary>
        private void TickSightseeing()
        {
            if (!sceneReady || world == null || !NoraFollows) return;
            followTimer -= Time.deltaTime;
            if (followTimer > 0f) return;
            followTimer = 0.5f;
            var player = LocalPlayer;
            if (player == null) return;

            var nora = world.npcs.Find(n => n.typeId == NoraFollowing && !n.dead);
            if (nora == null)
            {
                Vector3 at = BesidePlayer(player.transform.position);
                int uid = world.sceneId * 10000 + 9500 + world.npcs.Count;
                world.npcs.Add(new NpcEntity { uid = uid, typeId = NoraFollowing, spawnPos = at, pos = at, health = 30, maxHealth = 30 });
                Plugin.Log.LogInfo($"[quest] Sightseeing: Nora joins as {uid} at {SceneWorld.Vec(at)}");
                visibilityTimer = 0f;
                return;
            }
            if (dialogueNpc != null && dialogueNpc.uid == nora.uid && dialogueNode != null) return;   // she's talking

            KeepBeside(nora, player.transform.position);
        }

        /// <summary>
        /// Walks a companion (Nora, a pet) after the player: it catches up at once if left far behind (a teleport,
        /// a cart, a run it couldn't keep up with) and otherwise walks to stay about 2 m away.
        /// </summary>
        private void KeepBeside(NpcEntity n, Vector3 me)
        {
            Vector3 at = NpcPosition(n);
            float dist = Vector3.Distance(me, at);
            if (dist > 25f)
            {
                Vector3 p = BesidePlayer(me);
                n.pos = n.spawnPos = p;
                n.hasDest = false;
                Send(9, 1, n.uid, p);
            }
            else if (dist > 3.5f)
            {
                Vector3 away = at - me;
                away.y = 0f;
                MoveNpc(n, me + away.normalized * 2f);
                n.spawnPos = me;
            }
        }
    }
}
