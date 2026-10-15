using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Quest 23, Lakhmu's basement, and the dungeon under Bal Sardan, as the original server ran them
    /// (Scr_LakhmusBasement, Scr_InteractableHandler 5/6, the Lakhmu cases of Scr_NpcHandler and Scr_PlayerHandler).
    /// Dialogue, barrel deliveries (actions 52-54), the wraith kill and the quest's NPC rules are in content.json;
    /// this is what needed code:
    /// - the hatch in Bal Sardan town: locked until phase 6, then a lock to pick by ear. It ticks once a second, and
    ///   clicking on the right tick (3, 5, 3, 2, 3) opens each of five locks; the wrong tick or waiting to 6 resets it.
    ///   Open, it drops the player into the dungeon. The ladder there leads back up.
    /// - Alorus's barrels: picking one up (one at a time, in delivery order) hides it outside his shop.
    /// - Lakhmu calls for help as the player reaches the deep caves; after the wraith he follows them out, and
    ///   reaching the ladder with him moves the quest on.
    /// - Falling rocks in the caves: a warning dust cloud, then rocks 3 seconds later that hurt anyone within 4 m.
    /// </summary>
    public partial class OfflineServer
    {
        private const int QuestLakhmu = 23, SceneBalSardanTown = 31, SceneLakhmuDungeon = 32;
        private const int LakhmuTrapped = 230, LakhmuFollowing = 243;
        private const int ActionFollowLakhmu = 56, ActionNewBackpack = 57;
        private const int ObjectQuest = 43, ObjectLadder = 37;
        private const int CinematicLockedHatch = 2, CinematicLakhmuAttacked = 3;
        private const int SfxGrowl = 379, SfxLockReset = 384, SfxTick = 385, SfxLockOpens = 386, SfxHatchOpens = 387;
        private const int GfxRocksWarning = 102, GfxRocksFall = 101;

        private static readonly Vector3 HatchPos = new Vector3(891.9f, 6f, 589.4f);
        private static readonly Vector3 BarrelsPos = new Vector3(929.9f, 5.7f, 453.6f);
        private static readonly Vector3 LadderPos = new Vector3(42.9f, 199.9f, -32.3f);
        private static readonly Vector3 DungeonArrival = new Vector3(45f, 201f, -32f);

        private static readonly int[] HatchTicks = { 3, 5, 3, 2, 3 };
        private static readonly string[] Taverns = { "Tavern Nyal Mahan", "Tavern Lokha Kahul", "Tavern Elja Bahra" };
        private static readonly string[] Ordinals = { "first", "second", "third" };

        // The caves between the entrance and Lakhmu, where rocks fall (the original's 16 spots).
        private static readonly Vector2[] RockSpots =
        {
            new Vector2(50, -60), new Vector2(28, -67), new Vector2(37, -74), new Vector2(36, -85), new Vector2(23, -84),
            new Vector2(50, -74), new Vector2(62, -73), new Vector2(73, -62), new Vector2(83, -65), new Vector2(92, -77),
            new Vector2(71, -77), new Vector2(61, -90), new Vector2(80, -90), new Vector2(96, -90), new Vector2(88, -96),
            new Vector2(101, -98),
        };

        private bool hatchPicking;
        private int hatchTick, hatchLocksOpen;
        private float hatchTickAt, lakhmuCheckAt, rockDropAt;
        private readonly List<KeyValuePair<Vector3, float>> fallingRocks = new List<KeyValuePair<Vector3, float>>();

        private int LakhmuPhase => character != null ? character.GetQuestPhase(QuestLakhmu) : 0;

        private NpcEntity LakhmuFollower => world?.npcs.Find(n => n.typeId == LakhmuFollowing && !n.dead);

        private static bool Near(Vector3 a, Vector3 b, float r) => (a - b).sqrMagnitude <= r * r;

        /// <summary>A click on an object (3/11): the hatch, the barrels or the ladder. True if handled here.</summary>
        private bool HandleLakhmuInteract(int typeId)
        {
            var player = LocalPlayer;
            if (character == null || player == null) return false;
            Vector3 me = player.transform.position;
            if (character.scene == SceneBalSardanTown && typeId == ObjectQuest && Near(me, HatchPos, 5f))
            {
                ClickHatch(me);
                return true;
            }
            if (character.scene == SceneBalSardanTown && typeId == ObjectQuest && Near(me, BarrelsPos, 5f))
            {
                ClickBarrel();
                return true;
            }
            if (character.scene == SceneLakhmuDungeon && typeId == ObjectLadder && Near(me, LadderPos, 6f))
            {
                TeleportNear(SceneBalSardanTown, HatchPos);
                return true;
            }
            return false;
        }

        private void ClickHatch(Vector3 me)
        {
            int phase = LakhmuPhase;
            if (phase == 1)
            {
                SetQuestPhase(QuestLakhmu, 2);
                Notice("The hatch is locked. You can hear muffled sounds coming from the basement.");
                Send(3, 40, character.name, CinematicLockedHatch);
                PlaySound(SfxGrowl, me);
            }
            else if (phase >= 2 && phase <= 5)
                Notice("The hatch is locked. You can hear muffled sounds coming from the basement.");
            else if (phase >= 6 || phase == -1)
                PickHatchLock();
        }

        private void PickHatchLock()
        {
            if (!hatchPicking)
            {
                Notice("You start picking the lock. The hatch starts making a ticking sound...");
                hatchPicking = true;
                hatchTick = 0;
                hatchLocksOpen = 0;
                hatchTickAt = Time.time + 2f;
                PlaySound(SfxTick, HatchPos, 25);
                return;
            }
            if (hatchTick != HatchTicks[hatchLocksOpen])
            {
                Notice("You turn the lock... You hear a loud click as the lock closes.");
                ResetHatch();
                return;
            }
            hatchLocksOpen++;
            hatchTick = 0;
            hatchTickAt = Time.time + 3f;
            if (hatchLocksOpen < HatchTicks.Length)
            {
                Notice("You turn the lock... You hear a click as a lock opens. The hatch keeps making ticking sounds...");
                PlaySound(SfxLockOpens, HatchPos, 25);
                return;
            }
            hatchPicking = false;
            Notice("You turn the lock... The last lock clicks and the hatch opens!");
            PlaySound(SfxHatchOpens, HatchPos, 25);
            Plugin.Log.LogInfo("[quest] Lakhmu's hatch opened");
            Later(2.5f, () => { if (character != null && character.scene == SceneBalSardanTown) Teleport(SceneLakhmuDungeon, DungeonArrival); });
        }

        private void ResetHatch()
        {
            hatchPicking = false;
            hatchTick = 0;
            hatchLocksOpen = 0;
            PlaySound(SfxLockReset, HatchPos, 25);
        }

        /// <summary>One barrel at a time, in the order the taverns get them (quest var 1 = picked up, var 2 = delivered).</summary>
        private void ClickBarrel()
        {
            if (LakhmuPhase != 4)
            {
                Notice("It's a barrel.");
                return;
            }
            int delivered = GetQuestVar(QuestLakhmu, 2), picked = GetQuestVar(QuestLakhmu, 1);
            if (delivered >= Taverns.Length) return;
            if (picked == delivered)
            {
                SetQuestVar(QuestLakhmu, 1, picked + 1);
                Notice($"You pick up the {Ordinals[delivered]} barrel. It says '{Taverns[delivered]}'.");
            }
            else
                Notice($"You already have a barrel. It says '{Taverns[delivered]}'.");
        }

        /// <summary>Clicking one of the Lakhmus. True if handled here (no conversation).</summary>
        private bool LakhmuInteractNpc(NpcEntity npc)
        {
            if (npc.typeId == LakhmuFollowing)
            {
                NpcSay(npc, "You know the way out, right?");
                return true;
            }
            if (npc.typeId != LakhmuTrapped) return false;
            int phase = LakhmuPhase;
            if (phase == 6)
            {
                LakhmuCallsForHelp(npc);
                return true;
            }
            return phase != 8;   // at 8 the conversation (node 1561, content.json) starts his escort
        }

        private void LakhmuCallsForHelp(NpcEntity npc)
        {
            NpcSay(npc, "Human! Help!");
            SetQuestPhase(QuestLakhmu, 7);
        }

        /// <summary>Dialogue actions with logic. True if handled.</summary>
        private bool RunLakhmuAction(int id)
        {
            if (id == ActionFollowLakhmu)
            {
                SpawnFollowingLakhmu();
                return true;
            }
            if (id != ActionNewBackpack) return false;
            if (CountItem(10387) >= 20 && CountItem(10037) >= 5)
            {
                TakeItems(10387, 20);
                TakeItems(10037, 5);
                GiveItem(381);
                if (dialogueNpc != null) NpcSay(dialogueNpc, "Here you go!");
                Notice("You have received " + (ItemData.Get(381)?.name ?? "a backpack") + ".", "green");
            }
            else if (dialogueNpc != null)
                NpcSay(dialogueNpc, "I'm afraid I don't have the materials, I require 20 Bal Sardan vines and 5 iron ingots.");
            return true;
        }

        private void SpawnFollowingLakhmu()
        {
            var player = LocalPlayer;
            if (player == null || world == null || world.sceneId != SceneLakhmuDungeon || LakhmuPhase != 8 || LakhmuFollower != null) return;
            Vector3 at = BesidePlayer(player.transform.position);
            var lakhmu = new NpcEntity { uid = world.sceneId * 10000 + 9600, typeId = LakhmuFollowing, spawnPos = at, pos = at, health = 30, maxHealth = 30 };
            world.npcs.Add(lakhmu);
            visibilityTimer = 0f;
            Later(1.5f, () => { if (world != null && world.npcs.Contains(lakhmu)) NpcSay(lakhmu, "You know the way out, right?"); });
            Plugin.Log.LogInfo("[quest] Lakhmu follows the player out");
        }

        private void TickLakhmusBasement()
        {
            TickHatch();
            if (character == null || !sceneReady || world == null || world.sceneId != SceneLakhmuDungeon) return;
            var player = LocalPlayer;
            if (player == null) return;
            Vector3 me = player.transform.position;
            TickFallingRocks(me);
            var follower = LakhmuFollower;
            if (follower != null && !(dialogueNpc != null && dialogueNpc.uid == follower.uid && dialogueNode != null)) KeepBesideEvery(follower, me);

            if (Time.time < lakhmuCheckAt) return;
            lakhmuCheckAt = Time.time + 1f;
            int phase = LakhmuPhase;
            if (me.z < -118f && phase == 6)
            {
                // Down in the deep caves: Lakhmu, cornered by the wraith, calls for help.
                Send(3, 40, character.name, CinematicLakhmuAttacked);
                var trapped = world.npcs.Find(n => n.typeId == LakhmuTrapped);
                if (trapped != null) LakhmuCallsForHelp(trapped);
            }
            else if (phase == 8 && follower != null && me.z > -45f && me.x < 58f)
            {
                world.npcs.Remove(follower);
                SetQuestPhase(QuestLakhmu, 9);
                Notice("Lakhmu climbs up the ladder.");
            }
        }

        private float lakhmuFollowAt;

        private void KeepBesideEvery(NpcEntity n, Vector3 me)
        {
            if (Time.time < lakhmuFollowAt) return;
            lakhmuFollowAt = Time.time + 0.5f;
            KeepBeside(n, me);
        }

        /// <summary>The lock ticks once a second while it's being picked; at the sixth tick it resets.</summary>
        private void TickHatch()
        {
            if (!hatchPicking || Time.time < hatchTickAt) return;
            hatchTickAt = Time.time + 1f;
            hatchTick++;
            PlaySound(SfxTick, HatchPos, 25);
            Plugin.Log.LogInfo($"[quest] hatch tick {hatchTick} (lock {hatchLocksOpen + 1} opens on {HatchTicks[hatchLocksOpen]})");
            if (hatchTick == 6) ResetHatch();
        }

        /// <summary>
        /// A rock every 0.4 s at one of the spots (not one already falling), 3 s after its dust cloud. The original's
        /// blows were 1500-3000 against its 100 health per combat level; here they take the same share of the
        /// player's health, less their blunt resistance.
        /// </summary>
        private void TickFallingRocks(Vector3 me)
        {
            if (Time.time >= rockDropAt)
            {
                rockDropAt = Time.time + 0.4f;
                var s = RockSpots[Random.Range(0, RockSpots.Length)];
                var pos = new Vector3(s.x, 200f, s.y);
                if (!fallingRocks.Exists(r => r.Key == pos))
                {
                    fallingRocks.Add(new KeyValuePair<Vector3, float>(pos, Time.time + 3f));
                    PlayEffect(GfxRocksWarning, pos + Vector3.up * 6f);
                }
            }
            for (int i = fallingRocks.Count - 1; i >= 0; i--)
            {
                if (Time.time < fallingRocks[i].Value) continue;
                Vector3 pos = fallingRocks[i].Key;
                fallingRocks.RemoveAt(i);
                PlayEffect(GfxRocksFall, pos + Vector3.up * 6f);
                if (PlayerDead || Vector3.Distance(pos, me) >= 4f) continue;
                int dmg = HurtByOriginal(Random.Range(1500, 3001), DamageKinds.Blunt);
                Plugin.Log.LogInfo($"[quest] falling rocks hit the player for {dmg}");
            }
        }

        /// <summary>Leaving the dungeon: rocks in the air and Lakhmu's escort end (he waits by the wraith again).</summary>
        private void ResetLakhmusBasement() => fallingRocks.Clear();
    }
}
