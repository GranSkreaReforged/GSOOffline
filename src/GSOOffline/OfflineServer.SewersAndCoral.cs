using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Two more dungeons as the original server ran them.
    /// Yorkhill sewers (scene 4, Scr_YorkhillSewers): the broken bridge on the way to Nirish gives way under you (2 s
    /// stunned, then you drop) unless a pillar holds it up. Pick up the pillar by the alchemy table, carry it to the
    /// spot under the bridge and drop it there: the bridge holds for 10 minutes.
    /// Coral dungeon (scenes 27 and 28, Scr_CoralDungeon): the caves are flooded (the water is a spawned object), and
    /// three tidal waves sweep through them at 15 m/s; one that catches you stuns you and 3 s later washes you back to
    /// the entrance, 30% of your health lost. When all three have passed the water settles, and they come again
    /// 30-60 s later. In the second part, explosions burst around the hall every 4 s.
    /// </summary>
    public partial class OfflineServer
    {
        // ---- Yorkhill sewers ---------------------------------------------------------------------

        private const int SceneSewers = 4, ItemPillar = 395, ObjBridgeColliders = 34, ObjBridgeSupport = 30, SewerUidBase = 7300000;
        private static readonly Vector3 SewerBridge = new Vector3(145.8f, 60f, 229.13f), PillarSpot = new Vector3(146f, 51f, 226f);
        private static readonly Vector3 PillarPile = new Vector3(50f, 57.2f, 283.1f);

        private bool sewerShown, bridgeCollidersShown, supportShown;
        private float bridgeSupportedUntil, bridgeBackAt, sewerTick;

        private bool InSewers => character != null && sceneReady && world != null && world.sceneId == SceneSewers;

        private void TickSewers()
        {
            if (!InSewers) return;
            var player = LocalPlayer;
            if (player == null) return;
            if (!sewerShown)
            {
                sewerShown = true;
                bridgeCollidersShown = Time.time >= bridgeBackAt;
                supportShown = Time.time < bridgeSupportedUntil;
                var parts = new List<string>();
                if (bridgeCollidersShown) parts.Add(SewerObj(0, ObjBridgeColliders, SewerBridge));
                if (supportShown) parts.Add(SewerObj(1, ObjBridgeSupport, PillarSpot));
                if (parts.Count > 0) Send(2, 35, character.name, string.Join(">", parts.ToArray()));
            }
            sewerTick -= Time.deltaTime;
            if (sewerTick > 0f) return;
            sewerTick = 0.25f;

            if (!bridgeCollidersShown && Time.time >= bridgeBackAt)
            {
                bridgeCollidersShown = true;
                Send(2, 35, character.name, SewerObj(0, ObjBridgeColliders, SewerBridge));
            }
            bool supported = Time.time < bridgeSupportedUntil;
            if (supportShown && !supported)
            {
                supportShown = false;
                Send(25, 10, SewerUid(1));
            }
            Vector3 me = player.transform.position;
            if (supported || PlayerDead || !(me.z < 231.5f && me.z > 220.5f && me.x > 142f && me.x < 148f && me.y > 59.5f)) return;
            if (!bridgeCollidersShown) return;
            // The bridge gives way: stunned for 2 s while it drops out from under you, back 2 s later.
            Notice("The bridge collapses under your weight!", "red");
            StunPlayer(2f);
            bridgeCollidersShown = false;
            bridgeBackAt = Time.time + 2f;
            PlaySound(411, SewerBridge, 60);
            PlayEffect(132, new Vector3(145f, 60f, 226f));
            Send(25, 10, SewerUid(0));
        }

        private static int SewerUid(int i) => SewerUidBase + i;
        private static string SewerObj(int i, int type, Vector3 pos) => SewerUid(i) + "_" + type + "_" + SceneWorld.Vec(pos) + "_0";

        /// <summary>Clicking the pillar by the alchemy table: you pick it up (and hold it). True if handled.</summary>
        private bool HandleSewerInteract(int typeId)
        {
            var player = LocalPlayer;
            if (!InSewers || player == null || typeId != ObjectQuest || !Near(player.transform.position, PillarPile, 5f)) return false;
            Notice("You pick up a pillar...");
            var pillar = GiveItem(ItemPillar);
            if (pillar != null) Later(2f, () => { if (character != null && character.items.Contains(pillar) && !pillar.equipped) Equip(pillar.id); });
            return true;
        }

        /// <summary>Dropping the pillar under the bridge props it up for 10 minutes. True if it was used up.</summary>
        private bool DropPillar(int typeId)
        {
            var player = LocalPlayer;
            if (typeId != ItemPillar || !InSewers || player == null || !Near(player.transform.position, PillarSpot, 5f)) return false;
            Notice("You place the pillar to support the bridge.");
            bridgeSupportedUntil = Time.time + 600f;
            if (!supportShown)
            {
                supportShown = true;
                Send(2, 35, character.name, SewerObj(1, ObjBridgeSupport, PillarSpot));
            }
            return true;
        }

        // ---- Coral dungeon -----------------------------------------------------------------------

        private const int SceneCoral = 27, SceneCoralHall = 28, ObjWave = 6, ObjWater = 7, CoralUidBase = 7400000;
        private const float WaveSpeed = 15f, WaterLow = 157.03f, WaterHigh = 157.3f;
        private static readonly Vector3 CoralEntrance = new Vector3(223f, 160f, 230f);

        private static Vector3 V(float x, float z) => new Vector3(x, 160f, z);

        // Each wave starts hidden at index 0 (under the map), rises at 1 and runs its route.
        private static readonly Vector3[][] WaveRoutes =
        {
            new[] { new Vector3(300, 0, 300), V(300, 300), V(223, 230), V(173, 217), V(156, 219), V(146, 196), V(117, 196), V(105, 210), V(103, 230),
                    V(62, 202), V(43, 212), V(20, 212), V(31, 163), V(43, 161), V(45, 144), V(63, 141) },
            new[] { new Vector3(300, 0, 300), V(300, 300), V(223.1f, 229.1f), V(186.1f, 220.9f), V(172.8f, 213.7f), V(155.9f, 218.3f), V(150.8f, 201.4f),
                    V(142.1f, 192.7f), V(126.7f, 192.2f), V(124.1f, 178.9f), V(107.7f, 159.4f), V(112.3f, 137.8f), V(123.6f, 126f), V(139.5f, 121.9f),
                    V(149.3f, 136.3f), V(139.5f, 151.2f), V(150.3f, 163f), V(155.4f, 181f), V(181.6f, 184.1f), V(197f, 186.2f), V(204.7f, 172.3f),
                    V(200.1f, 154.8f), V(215.5f, 144f), V(217f, 135.3f), V(207.8f, 129.7f), V(190.9f, 128.7f), V(185.8f, 115.9f), V(184.3f, 94.9f),
                    V(157.1f, 91.3f), V(143.8f, 93.9f), V(134.6f, 89.8f), V(133.1f, 63.6f), V(136.7f, 49.7f), V(136.2f, 30.7f), V(131.6f, 21.5f),
                    V(109f, 26.1f), V(96.2f, 21f), V(75.7f, 20f), V(51.1f, 41f), V(56.7f, 74.4f), V(82.4f, 77f), V(104.5f, 50.3f) },
            new[] { new Vector3(300, 0, 300), V(300, 300), V(223.1f, 229.1f), V(186.1f, 220.9f), V(172.8f, 213.7f), V(155.9f, 218.3f), V(150.8f, 201.4f),
                    V(142.1f, 192.7f), V(126.7f, 192.2f), V(124.1f, 178.9f), V(107.7f, 159.4f), V(122.5f, 145.1f), V(140f, 148f), V(149.5f, 162.6f),
                    V(167f, 160.4f), V(193.3f, 155.3f), V(212.3f, 146.5f), V(216.7f, 139.2f), V(210.1f, 129f), V(190.4f, 129f), V(183.8f, 94.7f),
                    V(164.8f, 91.8f), V(145.8f, 94f), V(135.6f, 88.9f), V(133.4f, 72.1f), V(137.1f, 54.6f), V(153.9f, 53.9f), V(164.9f, 56.8f),
                    V(174.4f, 66.3f), V(190.5f, 62.6f), V(207.3f, 66.3f), V(216.1f, 56.8f), V(222.7f, 43.7f), V(224.2f, 29.8f), V(222f, 17.4f),
                    V(211.8f, 15.2f) },
        };

        private readonly Vector3[] wavePos = { new Vector3(500, 160, 500), new Vector3(500, 160, 500), new Vector3(500, 160, 500) };
        private readonly int[] waveTarget = { 0, 0, 0 };
        private readonly bool[] waveActive = { true, true, true };
        private float wavesBackAt, waterLevel = WaterLow, waveSyncAt, waveHitCheckAt, coralExplodeAt;
        private bool coralShown, washedAway;

        private bool InCoral => character != null && sceneReady && world != null && (world.sceneId == SceneCoral || world.sceneId == SceneCoralHall);

        private void ResetSewersAndCoral() => sewerShown = coralShown = false;

        private void TickCoral()
        {
            if (!InCoral) return;
            var player = LocalPlayer;
            if (player == null) return;
            if (world.sceneId == SceneCoralHall)
            {
                TickCoralExplosions(player.transform.position);
                return;
            }
            if (!coralShown)
            {
                coralShown = true;
                var parts = new List<string> { CoralUid(9) + "_" + ObjWater + "_" + SceneWorld.Vec(new Vector3(0f, waterLevel, 0f)) + "_0" };
                for (int i = 0; i < 3; i++) parts.Add(CoralUid(i) + "_" + ObjWave + "_" + SceneWorld.Vec(wavePos[i]) + "_0");
                Send(2, 35, character.name, string.Join(">", parts.ToArray()));
            }

            bool any = false;
            for (int i = 0; i < 3; i++)
            {
                if (!waveActive[i]) continue;
                any = true;
                var route = WaveRoutes[i];
                wavePos[i] = Vector3.MoveTowards(wavePos[i], route[waveTarget[i]], WaveSpeed * Time.deltaTime);
                if (Vector3.Distance(wavePos[i], route[waveTarget[i]]) >= 1f) continue;
                if (++waveTarget[i] < route.Length) continue;
                waveTarget[i] = 1;
                wavePos[i] = route[0];
                waveActive[i] = false;
                Send(14, 1, CoralUid(i), 0, wavePos[i]);
            }
            if (!any)
            {
                if (wavesBackAt <= 0f) wavesBackAt = Time.time + 30f + Random.Range(30, 60);
                if (Time.time >= wavesBackAt)
                {
                    wavesBackAt = 0f;
                    for (int i = 0; i < 3; i++) waveActive[i] = true;
                }
            }
            float level = Mathf.MoveTowards(waterLevel, any ? WaterHigh : WaterLow, 0.5f * Time.deltaTime);
            bool waterMoved = !Mathf.Approximately(level, waterLevel);
            waterLevel = level;

            if (Time.time >= waveSyncAt)
            {
                waveSyncAt = Time.time + 0.2f;
                for (int i = 0; i < 3; i++) if (waveActive[i]) Send(14, 1, CoralUid(i), 0, wavePos[i]);
                if (waterMoved) Send(14, 1, CoralUid(9), 0, new Vector3(0f, waterLevel, 0f));
            }

            if (Time.time < waveHitCheckAt || washedAway || PlayerDead) return;
            waveHitCheckAt = Time.time + 1.5f;
            Vector3 me = player.transform.position;
            for (int i = 0; i < 3; i++)
            {
                if (!waveActive[i] || Vector3.Distance(wavePos[i], me) >= 8f) continue;
                // Caught: knocked down for 3 s, then washed back to the entrance, 30% of your health gone.
                washedAway = true;
                StunPlayer(3f);
                UpdateCanMove();   // held even if stun immunity keeps the stun off
                Later(3f, () =>
                {
                    washedAway = false;
                    if (character == null || PlayerDead || world == null || world.sceneId != SceneCoral) return;
                    UpdateCanMove();
                    Teleport(SceneCoral, CoralEntrance);
                    Notice("A massive wave knocks you unconscious!", "red");
                    HurtPlayer(Mathf.RoundToInt(character.currentHealth * 0.3f), SplashPlain);
                });
                break;
            }
        }

        private static int CoralUid(int i) => CoralUidBase + i;

        /// <summary>The hall: 7 warning swirls every 4 s around its middle, bursting 2-3 s later (6 m).</summary>
        private void TickCoralExplosions(Vector3 me)
        {
            if (Time.time < coralExplodeAt) return;
            coralExplodeAt = Time.time + 4f;
            for (int i = 0; i < 7; i++)
            {
                var pos = new Vector3(123f - Random.Range(-55, 55), 160.5f, 131f + Random.Range(-55, 55));
                PlayEffect(75, pos);
                Later(Random.Range(2f, 3f), () =>
                {
                    if (world == null || world.sceneId != SceneCoralHall) return;
                    PlayEffect(76, pos);
                    PlaySound(359, pos, 60);
                    var p = LocalPlayer;
                    if (p != null && !PlayerDead && Vector3.Distance(pos, p.transform.position) < 6f)
                        Plugin.Log.LogInfo($"[dungeon] coral explosion hits the player for {HurtByOriginal(Random.Range(1000, 1501), DamageKinds.Blunt)}");
                });
            }
        }
    }
}
