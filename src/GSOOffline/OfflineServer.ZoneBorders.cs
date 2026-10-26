using System;
using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Walking or sailing out of a zone (#134). The client has no border triggers: the original server checked every
    /// player's position each second (Scr_PlayerHandler.forceChangeSceneCheck) and moved them into the neighbouring
    /// zone. The table below is that check, rule for rule and in its order. A sailor keeps their boat and stays
    /// aboard, as on the original (forcePlayerSceneChange moved the ship with its owner).
    /// </summary>
    public partial class OfflineServer
    {
        private class ZoneBorder
        {
            public int scene;
            public Func<Vector3, bool> at;
            public int toScene;
            public Vector3 arrival;   // the original's; its heights are rough, the arrival finds the ground
            public string note;
        }

        private static ZoneBorder Border(int scene, Func<Vector3, bool> at, int toScene, float x, float y, float z, string note) =>
            new ZoneBorder { scene = scene, at = at, toScene = toScene, arrival = new Vector3(x, y, z), note = note };

        private static Func<Vector3, bool> Near(float x, float y, float z, float r) =>
            p => Vector3.Distance(new Vector3(x, y, z), p) < r;

        // Bal Sardan (7) and its town (31) are declared in ItemUses.cs and LakhmusBasement.cs.
        private const int SceneWestAthagos = 1, SceneEastAthagos = 13, SceneGoldon = 18, SceneBlueSea = 19;

        private static readonly List<ZoneBorder> ZoneBorders = new List<ZoneBorder>
        {
            Border(SceneWestAthagos, Near(856, 11, 1133, 10), SceneEastAthagos, 842, 12, 1100, "Grimwall farm, south exit"),
            Border(SceneWestAthagos, Near(933, 10, 1227, 10), SceneEastAthagos, 977, 16, 1230, "Grimwall farm, north exit"),
            Border(SceneWestAthagos, p => p.z < 650, SceneBlueSea, 1000, 4, 1650, "the Blue Sea"),
            Border(SceneWestAthagos, p => p.z < 815 && p.x > 550, SceneEastAthagos, 565, 4, 781, "East Athagos, from the south"),
            Border(SceneWestAthagos, p => p.x > 920 && p.z > 1875, SceneGoldon, 83, 7, 8, "Goldon"),
            Border(SceneWestAthagos, p => p.x > 1070 && p.z > 1350, SceneEastAthagos, 1115, 7, 1557, "East Athagos, from the north"),

            Border(SceneEastAthagos, Near(852, 12, 1134, 15), SceneWestAthagos, 855, 7.5f, 1150, "Grimwall farm, south exit"),
            Border(SceneEastAthagos, Near(940, 10, 1226, 25), SceneWestAthagos, 912, 9, 1224, "Grimwall farm, north exit"),
            Border(SceneEastAthagos, p => p.x < 1070 && p.z > 1530, SceneWestAthagos, 1045, 6.5f, 1600, "West Athagos, from the north"),
            Border(SceneEastAthagos, p => p.x < 550, SceneWestAthagos, 500, 6.5f, 730, "West Athagos, from the south"),
            Border(SceneEastAthagos, p => p.z > 1750, SceneGoldon, 1400, 6.5f, 0, "Goldon"),
            Border(SceneEastAthagos, p => p.z < 0, SceneBalSardan, 400, 6.5f, 1250, "Bal Sardan"),

            Border(SceneBalSardan, p => p.z > 1300, SceneEastAthagos, 1350, 6.5f, 30, "East Athagos"),
            Border(SceneBalSardan, p => p.z > 450 && p.z < 550 && p.x > 900 && p.x < 1015, SceneBalSardanTown, 995, 6.5f, 510, "Bal Sardan town"),
            Border(SceneBalSardan, p => p.x < -250, SceneBlueSea, 1682, 6.5f, 727, "the Blue Sea"),

            Border(SceneGoldon, p => p.z < -150 && p.x < 300, SceneWestAthagos, 800, 6.5f, 1848, "West Athagos"),
            Border(SceneGoldon, p => p.z < -150 && p.x >= 300, SceneEastAthagos, 1461, 6.5f, 1700, "East Athagos"),

            Border(SceneBlueSea, p => p.x > 1800, SceneBalSardan, -200, 4, 400, "Bal Sardan"),
            Border(SceneBlueSea, p => p.z > 1700, SceneWestAthagos, 300, 4, 680, "West Athagos"),

            Border(SceneBalSardanTown, p => p.x > 1015, SceneBalSardan, 1045, 6.5f, 510, "Bal Sardan's exterior"),
        };

        private const float BorderCheckInterval = 0.5f;
        private float borderTimer;
        private bool borderArrival;   // the pending spawn is a border's: find the ground (or the water) there

        /// <summary>Crosses into the next zone if the player is past a border. Returns true on a zone change.</summary>
        private bool TickZoneBorders()
        {
            if (pendingSpawn.HasValue || PlayerDead || ridingId != 0) return false;   // a ferry crosses on its own
            borderTimer -= Time.deltaTime;
            if (borderTimer > 0f) return false;
            borderTimer = BorderCheckInterval;
            var player = LocalPlayer;
            if (player == null) return false;
            var p = player.transform.position;
            foreach (var b in ZoneBorders)
            {
                if (b.scene != character.scene || !b.at(p)) continue;
                CrossZoneBorder(b, p);
                return true;
            }
            return false;
        }

        private void CrossZoneBorder(ZoneBorder b, Vector3 from)
        {
            bool sailing = aboard && BoatOut;
            Plugin.Log.LogInfo($"[travel] zone border at {from} in scene {b.scene} ({b.note}): to scene {b.toScene} at {b.arrival}{(sailing ? ", sailing" : "")}");
            var spawn = b.arrival;
            if (sailing)
            {
                spawn.y = BoatWaterY + 0.5f;
                boatPos = new Vector3(spawn.x, BoatWaterY, spawn.z);
            }
            ChangeScene(b.toScene, spawn, sailing);
            pendingSpawnNeedsClearing = true;   // after ChangeScene, whose world reset clears them
            borderArrival = true;
        }

        /// <summary>
        /// Where a border arrival lands, once the zone's colliders exist. On land: the terrain under the original's
        /// point, then the door arrival's search for open ground. A sailor lands on the water there; if it's land
        /// after all, the boat goes back in the bag and they arrive on foot.
        /// </summary>
        private bool TryBorderSpot(Vector3 center, out Vector3 spot)
        {
            spot = center;
            if (!TerrainAt(center, out float ground))
            {
                // Nothing under the point. Once the zone has colliders nearby, that's open sea with no seabed.
                if (!Physics.CheckSphere(center, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
                ground = float.MinValue;
            }
            bool water = ground < BoatWaterY - 0.3f;
            if (aboard && BoatOut)
            {
                if (water) return true;
                StowBoat("arrived on land at a zone border");
            }
            if (water) return true;   // on foot into the sea: swim from the original's point
            var onGround = new Vector3(center.x, ground, center.z);
            if (!TryClearSpotNear(onGround, false, out spot)) spot = onGround + Vector3.up * 0.1f;
            return true;
        }

        // The terrain's height under a point. Prefers the terrain collider over anything on it (trees, roofs);
        // without one, the lowest walkable surface.
        private bool TerrainAt(Vector3 p, out float height)
        {
            height = 0f;
            bool found = false, terrain = false;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, 1000f, p.z), Vector3.down, 2000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (IsPlayerCollider(h.collider) || h.normal.y < 0.6f) continue;
                bool isTerrain = h.collider.GetType().Name == "TerrainCollider";
                if (terrain && !isTerrain) continue;
                if (!found || (isTerrain && !terrain) || h.point.y < height)
                {
                    height = h.point.y;
                    found = true;
                    terrain |= isTerrain;
                }
            }
            return found;
        }
    }
}
