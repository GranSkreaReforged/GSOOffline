using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Aellope, Goldon's flying world boss ("flying around the island of Goldon", update 135), as the original server's
    /// Scr_GoldonHarpy ran her: she patrols one of five routes high over the island, back and forth at half her speed.
    /// A hit while she flies makes her dive at the spot the attacker stood on; there she lands and fights like any
    /// boss. 20 seconds without taking damage and she takes off again, healed, and goes back to her route. While she's
    /// down, a dead player nearby is food: she flies to the body and heals 30% (every 45 seconds at most). Killed, she
    /// comes back an hour later on a new route.
    ///
    /// The server simulates her flight even when the player is elsewhere; inside Goldon she is an ordinary NpcEntity
    /// (targeting, damage, loot) with <see cref="NpcEntity.flying"/> keeping the ground AI off her while airborne.
    /// The state is per session, not saved: after a restart she is out flying again.
    /// </summary>
    public partial class OfflineServer
    {
        private const int HarpyType = 307, HarpyScene = 18, HarpyUidBase = HarpyScene * 10000 + 8900;
        private const int AnimHarpyFly = 35, GfxHarpyFeed = 133;
        private const int HarpySpeed = 10;   // NPCInfo movementspeed; she patrols and dives at half of it
        private const float HarpyRespawn = 3600f, HarpyResetAfter = 20f, HarpyFeedCooldown = 45f, HarpyFeedTime = 10f, HarpyFeedShare = 0.3f;
        private const float HarpyTickEvery = 0.5f;

        // The original's five routes. Its code picked points[route] and points[route + 1] instead of a pair, which
        // only ever flew the west and centre routes and the legs between them; these are the pairs it meant (its
        // "where's the harpy" gossip names them: west, over the centre, north, north-east, south).
        private static readonly Vector3[][] HarpyRoutes =
        {
            new[] { new Vector3(312, 44, 236), new Vector3(263, 41, 1095) },     // west
            new[] { new Vector3(972, 54, 573), new Vector3(572, 53, 821) },      // centre
            new[] { new Vector3(877, 36, 1060), new Vector3(670, 36, 1216) },    // north
            new[] { new Vector3(1485, 66, 978), new Vector3(1185, 54, 960) },    // north-east
            new[] { new Vector3(829, 47, 434), new Vector3(782, 30, 248) },      // south
        };
        private static readonly string[] HarpyRouteNames = { "west", "centre", "north", "north-east", "south" };

        private enum HarpyState { Away, Flying, Diving, Landed, Feeding }

        private HarpyState harpyState = HarpyState.Away;
        private int harpyRoute, harpySpawns;
        private bool harpyHeadingBack;          // flying from the route's second point to its first
        private Vector3 harpyPos, harpyTarget;  // where she is (simulated while the client doesn't show her) and is heading
        private float harpyBackAt;              // Away: when she comes back (0 = at once, as the original's server start)
        private float harpyResetAt, harpyFeedReadyAt, harpyFeedUntil, harpyTick;
        private NpcEntity harpyNpc;             // her entity while the player is in Goldon

        private bool HarpyInWorld => harpyNpc != null && world != null && world.sceneId == HarpyScene && world.npcs.Contains(harpyNpc);

        private void TickHarpy()
        {
            if (character == null) return;
            harpyTick -= Time.deltaTime;
            if (harpyTick > 0f) return;
            harpyTick = HarpyTickEvery;

            if (harpyState == HarpyState.Away)
            {
                // The corpse stays for her usual body time, then the client lets it go.
                if (harpyNpc != null && Time.time >= harpyNpc.respawnAt)
                {
                    world?.npcs.Remove(harpyNpc);
                    harpyNpc = null;
                }
                if (Time.time >= harpyBackAt) SpawnHarpy(Random.Range(0, HarpyRoutes.Length));
                return;
            }
            bool shown = HarpyInWorld && sceneReady;
            if (!shown && harpyState != HarpyState.Flying)
            {
                // Down somewhere the player can't see (they left Goldon): nothing will hit her for 20 seconds.
                TakeOffHarpy();
                return;
            }
            if (shown) harpyPos = NpcPosition(harpyNpc);

            switch (harpyState)
            {
                case HarpyState.Flying:
                    if (FlyHarpy(HarpySpeed * 0.5f, 3f))
                    {
                        harpyHeadingBack = !harpyHeadingBack;
                        harpyTarget = HarpyRoutePoint();
                    }
                    break;
                case HarpyState.Diving:
                    if (FlyHarpy(HarpySpeed * 0.5f, 3f)) LandHarpy("dive");
                    break;
                case HarpyState.Feeding:
                    if (FlyHarpy(HarpySpeed, 5f))
                    {
                        harpyNpc.health = Mathf.Min(harpyNpc.maxHealth, harpyNpc.health + Mathf.RoundToInt(harpyNpc.maxHealth * HarpyFeedShare));
                        Send(12, 0, harpyNpc.uid, harpyNpc.health, harpyNpc.maxHealth, 0);
                        PlayEffect(GfxHarpyFeed, harpyPos + Vector3.up);
                        LandHarpy($"fed on the body, health {harpyNpc.health}/{harpyNpc.maxHealth}");
                    }
                    else if (Time.time >= harpyFeedUntil) LandHarpy("gave up on the body");
                    break;
                case HarpyState.Landed:
                    if (PlayerDead && Time.time >= harpyFeedReadyAt) FeedHarpy();
                    break;
            }
            if ((harpyState == HarpyState.Landed || harpyState == HarpyState.Feeding) && Time.time >= harpyResetAt)
                TakeOffHarpy();
            if (harpyNpc != null && !harpyNpc.dead) harpyNpc.pos = harpyPos;
        }

        private Vector3 HarpyRoutePoint() => HarpyRoutes[harpyRoute][harpyHeadingBack ? 0 : 1];

        /// <summary>
        /// One tick of straight-line flight to harpyTarget, height included (the client moves NPCs in straight lines
        /// and never puts them on the ground). Returns true once she is within arrive metres of it.
        /// </summary>
        private bool FlyHarpy(float speed, float arrive)
        {
            if (Vector3.Distance(harpyPos, harpyTarget) < arrive) return true;
            var c = HarpyInWorld && Scr_NpcHandler.instance != null ? Scr_NpcHandler.instance.getNpc(harpyNpc.uid) : null;
            if (c == null)
            {
                harpyPos = Vector3.MoveTowards(harpyPos, harpyTarget, speed * HarpyTickEvery);
                return false;
            }
            if (Time.time < harpyNpc.stunnedUntil || Time.time < harpyNpc.rootedUntil) return false;
            if (c.movementSpeed != (int)speed) Send(198, 31, (int)speed, harpyNpc.uid);
            if (c.walking) Send(198, 30, harpyNpc.uid, false);
            Send(198, 25, harpyNpc.uid, harpyPos, harpyTarget, harpyTarget);
            if (!c.moving) Send(13, 2, harpyNpc.uid, true);
            SetAnim(harpyNpc, AnimHarpyFly);
            return false;
        }

        private void SpawnHarpy(int route)
        {
            harpyRoute = route;
            harpyHeadingBack = false;
            harpyPos = HarpyRoutes[route][0];
            harpyTarget = HarpyRoutePoint();
            harpyState = HarpyState.Flying;
            if (harpyNpc != null) world?.npcs.Remove(harpyNpc);
            harpyNpc = null;
            harpySpawns++;
            Plugin.Log.LogInfo($"[harpy] Aellope out over {HarpyRouteNames[route]} Goldon");
            AddHarpyToWorld();
        }

        /// <summary>Puts her into the world while it's Goldon (each spawn and each entry into the zone).</summary>
        private void AddHarpyToWorld()
        {
            if (world == null || world.sceneId != HarpyScene || harpyState == HarpyState.Away || HarpyInWorld) return;
            GameData.Npcs.TryGetValue(HarpyType, out var info);
            int hp = info?.health ?? 30;
            // A fresh uid per spawn: the client keeps a corpse with the old one until the list drops it.
            harpyNpc = new NpcEntity
            {
                uid = HarpyUidBase + harpySpawns % 100, typeId = HarpyType, spawnPos = harpyPos, pos = harpyPos,
                health = hp, maxHealth = hp, flying = true, ownRespawn = true, anim = AnimHarpyFly,
            };
            world.npcs.Add(harpyNpc);
        }

        /// <summary>Any damage (DealDamage): resets her 20 seconds, and in the air sends her diving at the attacker.</summary>
        private void HarpyHurt(NpcEntity npc, int damage)
        {
            if (npc != harpyNpc) return;
            harpyResetAt = Time.time + HarpyResetAfter;
            if (harpyState != HarpyState.Flying || damage >= npc.health) return;   // a killing blow: HarpyDied grounds her
            var player = LocalPlayer;
            if (player == null) return;
            harpyState = HarpyState.Diving;
            harpyTarget = player.transform.position;
            Send(198, 28, npc.uid, harpyTarget);
            Plugin.Log.LogInfo($"[harpy] hit in the air: diving from {SceneWorld.Vec(harpyPos)} at {SceneWorld.Vec(harpyTarget)}");
        }

        /// <summary>Down on the ground where she is, a normal boss fighting the player.</summary>
        private void LandHarpy(string why)
        {
            if (GroundAt(harpyPos, harpyPos.y + 3f, 80f, out var ground)) harpyPos = ground;
            harpyState = HarpyState.Landed;
            harpyResetAt = Time.time + HarpyResetAfter;
            var npc = harpyNpc;
            npc.pos = npc.spawnPos = harpyPos;   // her leash and her way home start here
            npc.flying = false;
            npc.hasDest = npc.returning = false;
            Send(198, 31, HarpySpeed, npc.uid);
            Send(198, 25, npc.uid, harpyPos, harpyPos, harpyPos);
            Send(9, 1, npc.uid, harpyPos);
            Send(13, 2, npc.uid, false);
            GameData.Npcs.TryGetValue(HarpyType, out var info);
            SetAnim(npc, info?.animIdle ?? NpcAnims.Idle);
            if (!PlayerDead) Aggro(npc);
            Plugin.Log.LogInfo($"[harpy] landed at {SceneWorld.Vec(harpyPos)} ({why})");
        }

        /// <summary>The player died near her: she flies to the body to feed on it.</summary>
        private void FeedHarpy()
        {
            var player = LocalPlayer;
            if (player == null) return;
            harpyState = HarpyState.Feeding;
            harpyFeedReadyAt = Time.time + HarpyFeedCooldown;
            harpyFeedUntil = Time.time + HarpyFeedTime;
            harpyTarget = player.transform.position;
            StopHarpyFighting();
            harpyNpc.flying = true;
            Plugin.Log.LogInfo("[harpy] flying to feed on the player's body");
        }

        /// <summary>The 20 seconds ran out: back into the air at full health, heading on along her route.</summary>
        private void TakeOffHarpy()
        {
            harpyState = HarpyState.Flying;
            harpyTarget = HarpyRoutePoint();
            if (harpyNpc != null && !harpyNpc.dead)
            {
                StopHarpyFighting();
                harpyNpc.flying = true;
                harpyNpc.health = harpyNpc.maxHealth;
                harpyNpc.stunnedUntil = harpyNpc.rootedUntil = 0f;
                harpyNpc.dotTicks = 0;
                if (HarpyInWorld) Send(12, 0, harpyNpc.uid, harpyNpc.health, harpyNpc.maxHealth, 0);
            }
            Plugin.Log.LogInfo($"[harpy] taking off from {SceneWorld.Vec(harpyPos)} back to the {HarpyRouteNames[harpyRoute]} route");
        }

        private void StopHarpyFighting()
        {
            var npc = harpyNpc;
            npc.aggro = npc.returning = npc.hasDest = false;
            npc.attackAnimUntil = 0f;
            if (npc.attackingSent && HarpyInWorld)
            {
                npc.attackingSent = false;
                Send(11, 0, npc.uid, false, string.Empty);
            }
            npc.attackingSent = false;
        }

        /// <summary>From KillNpc, before the loot drops: shot down in the air, she falls to the ground under her.</summary>
        private void HarpyDied(NpcEntity npc)
        {
            if (npc != harpyNpc) return;
            if (npc.flying && GroundAt(harpyPos, harpyPos.y + 3f, 120f, out var ground))
            {
                harpyPos = npc.pos = ground;
                // Moved at once rather than by 9/1 next frame: the death sound and the loot bag read the client's NPC.
                var c = Scr_NpcHandler.instance != null ? Scr_NpcHandler.instance.getNpc(npc.uid) : null;
                if (c != null) c.transform.position = ground;
            }
            npc.flying = false;
            harpyState = HarpyState.Away;
            harpyBackAt = Time.time + HarpyRespawn;
            Plugin.Log.LogInfo($"[harpy] Aellope killed at {SceneWorld.Vec(npc.pos)}; back in {HarpyRespawn / 60f:0} minutes");
        }

        /// <summary>Dev: harpy [spawn [route] | hit [damage] | takeoff | back]</summary>
        internal void DevHarpy(string[] args)
        {
            string cmd = args.Length > 1 ? args[1] : string.Empty;
            if (cmd == "spawn") SpawnHarpy(args.Length > 2 ? int.Parse(args[2]) % HarpyRoutes.Length : Random.Range(0, HarpyRoutes.Length));
            else if (cmd == "takeoff" && harpyState != HarpyState.Away) TakeOffHarpy();
            else if (cmd == "hit" && HarpyInWorld && !harpyNpc.dead) DealDamage(harpyNpc, args.Length > 2 ? int.Parse(args[2]) : 1, WeaponSkill(EquippedWeapon()));
            else if (cmd == "back" && harpyState == HarpyState.Away) harpyBackAt = 0f;
            string where = harpyState == HarpyState.Away ? $"away, back in {Mathf.Max(0f, harpyBackAt - Time.time):0} s" :
                $"{harpyState} on the {HarpyRouteNames[harpyRoute]} route at {SceneWorld.Vec(harpyPos)} heading to {SceneWorld.Vec(harpyTarget)}";
            string ground = harpyState != HarpyState.Away && HarpyInWorld && GroundAt(harpyPos, harpyPos.y + 3f, 200f, out var g) ? $", {harpyPos.y - g.y:0.0} m up" : string.Empty;
            var c = HarpyInWorld && Scr_NpcHandler.instance != null ? Scr_NpcHandler.instance.getNpc(harpyNpc.uid) : null;
            Plugin.Log.LogInfo($"[dev] harpy: {where}{ground}; client speed {(c != null ? c.movementSpeed.ToString() : "-")}; uid {harpyNpc?.uid}, health {harpyNpc?.health}/{harpyNpc?.maxHealth}, in world {HarpyInWorld}" +
                (harpyState == HarpyState.Landed || harpyState == HarpyState.Feeding ? $", takes off in {harpyResetAt - Time.time:0.0} s" : string.Empty));
        }
    }
}
