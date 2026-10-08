using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Where a door drops the player. The arrival points in doors.json were guessed offline (a couple of metres off
    /// the far door) and some land in a pocket behind rocks or the door frame. Once the destination scene's colliders
    /// exist, this picks the nearby spot with solid ground, room to stand and the most open space around it.
    /// </summary>
    public partial class OfflineServer
    {
        private const float StandRadius = 0.5f, StandHeight = 1.9f, OpenCheckDistance = 6f;

        private const float ArrivalWait = 3f;

        private bool pendingSpawnNeedsClearing;
        private float arrivalDeadline = -1f;

        /// <summary>Like Teleport, but moves the arrival point to open ground near <paramref name="pos"/>.</summary>
        private void TeleportNear(int sceneId, Vector3 pos)
        {
            if (character == null) return;
            if (sceneReady && sceneId == character.scene)
            {
                Teleport(sceneId, ClearSpotNear(pos));
                return;
            }
            Teleport(sceneId, pos);
            pendingSpawnNeedsClearing = true;
        }

        // Some scenes switch their colliders on a moment after loading. Until a clear spot exists (or the wait runs
        // out), the loading screen stays up and the player isn't placed, so nobody drops through a missing floor.
        private void TickArrival()
        {
            if (arrivalDeadline < 0f) return;
            if (!pendingSpawn.HasValue) { arrivalDeadline = -1f; return; }
            bool found = TryClearSpotNear(pendingSpawn.Value, false, out var spot);
            if (!found && Time.time < arrivalDeadline) return;
            if (!found) Plugin.Log.LogWarning($"[travel] no clear spot near {pendingSpawn.Value} after {ArrivalWait} s; using it as is.");
            arrivalDeadline = -1f;
            pendingSpawnNeedsClearing = false;
            pendingSpawn = spot;
            FinishSceneLoad();
        }

        internal Vector3 ClearSpotNear(Vector3 center, bool verbose = false)
        {
            if (!TryClearSpotNear(center, verbose, out var spot))
                Plugin.Log.LogWarning($"[travel] no clear spot near {center}; using it as is.");
            return spot;
        }

        private bool TryClearSpotNear(Vector3 center, bool verbose, out Vector3 spot)
        {
            bool backfaces = Physics.queriesHitBackfaces;
            // With backfaces on, a probe that starts inside a rock hits it instead of seeing open space.
            Physics.queriesHitBackfaces = true;
            try
            {
                // A candidate must be in sight of the arrival point or of the door itself, so the search can't
                // pick open ground on the far side of a wall (outside the level).
                var door = NearestDoorObject(center);
                var eyes = new List<Vector3> { center + Vector3.up * 1.5f };
                if (door != null) eyes.Add(door.transform.position + Vector3.up * 1.5f);
                bool found = Search(center, center.y, eyes, door, verbose, out spot, out float score);

                // The guessed arrival can be on the wrong side of the door entirely (a wedge with nothing walkable).
                // Only then, look around the door from both of its sides.
                if (!found && door != null)
                {
                    var t = door.transform;
                    var d = t.position + Vector3.up * 1.5f;
                    eyes = new List<Vector3> { d + t.forward * 1.2f, d - t.forward * 1.2f, d + t.right * 1.2f, d - t.right * 1.2f };
                    found = Search(t.position, center.y, eyes, door, verbose, out spot, out score);
                }
                if (!found) { spot = center; return false; }
                if ((spot - center).sqrMagnitude > 0.25f)
                    Plugin.Log.LogInfo($"[travel] arrival moved from {center} to {spot} (score {score:F1}).");
                return true;
            }
            finally
            {
                Physics.queriesHitBackfaces = backfaces;
            }
        }

        // Rings around `origin`, near ones first (within 6 m); only if none works, further out (up to 12 m).
        private bool Search(Vector3 origin, float floorY, List<Vector3> eyes, Scr_Interactable door, bool verbose, out Vector3 best, out float bestScore)
        {
            best = origin;
            bestScore = float.MinValue;
            for (int ring = 0; ring <= 8; ring++)
            {
                if (ring == 5 && bestScore > float.MinValue) break;
                float r = ring * 1.5f;
                int steps = ring == 0 ? 1 : Mathf.Min(8 * ring, 48);   // about 1.2 m apart
                for (int a = 0; a < steps; a++)
                {
                    float ang = a * Mathf.PI * 2f / steps;
                    var probe = origin + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                    if (!GroundAt(probe, floorY, out var ground, out string why) || Obstructed(ground, out why)
                        || !InSight(eyes, ground + Vector3.up * 1f, door, out why))
                    {
                        if (verbose) Plugin.Log.LogInfo($"[travel] {probe}: {why}");
                        continue;
                    }
                    float score = Openness(ground) * 10f - r;
                    if (verbose) Plugin.Log.LogInfo($"[travel] {probe}: ground {ground.y:F2}, score {score:F1}");
                    if (score > bestScore) { bestScore = score; best = ground + Vector3.up * 0.1f; }
                }
            }
            return bestScore > float.MinValue;
        }

        private static Scr_Interactable NearestDoorObject(Vector3 p)
        {
            Scr_Interactable best = null;
            float bestDist = 4f;
            foreach (var i in Scr_Interactable.list)
            {
                if (i == null) continue;
                float d = Vector3.Distance(i.transform.position, p);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        private bool InSight(List<Vector3> eyes, Vector3 target, Scr_Interactable door, out string why)
        {
            why = "out of sight";
            foreach (var eye in eyes)
            {
                var dir = target - eye;
                bool blocked = false;
                foreach (var h in Physics.RaycastAll(eye, dir.normalized, dir.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (IsPlayerCollider(h.collider) || (door != null && h.collider.transform.IsChildOf(door.transform))) continue;
                    blocked = true;
                    why = $"out of sight (behind '{h.collider.name}')";
                    break;
                }
                if (!blocked) return true;
            }
            return false;
        }

        // Walkable ground under the probe, within reach of the door's height. The probe starts at waist height,
        // below low dungeon ceilings; starting inside a rock hits its backface (normal pointing down) and fails.
        // A second, higher start finds hillsides rising above the door; the line-of-sight check stops that from
        // landing on top of a ceiling.
        private bool GroundAt(Vector3 probe, float doorY, out Vector3 ground, out string why)
        {
            ground = probe;
            why = "no ground";
            foreach (float start in new[] { 1f, 4f })
            {
                probe.y = doorY + start;
                if (!NearestHit(probe, Vector3.down, start + 3f, out var hit)) continue;
                why = $"ground '{hit.collider.name}' y={hit.point.y:F2} normal={hit.normal}";
                if (hit.normal.y < 0.6f || Mathf.Abs(hit.point.y - doorY) > 4f) continue;
                ground = hit.point;
                return true;
            }
            return false;
        }

        private bool Obstructed(Vector3 ground, out string why)
        {
            why = null;
            var bottom = ground + Vector3.up * (StandRadius + 0.15f);
            var top = ground + Vector3.up * (StandHeight - StandRadius);
            foreach (var c in Physics.OverlapCapsule(bottom, top, StandRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!IsPlayerCollider(c)) { why = $"blocked by '{c.name}'"; return true; }
            // Also blocked if there's a surface right above the head (inside a rock with only its far side hit).
            if (!NearestHit(ground + Vector3.up * 0.3f, Vector3.up, StandHeight, out var above)) return false;
            why = $"low ceiling '{above.collider.name}' at {above.point.y:F2}";
            return true;
        }

        // How many of 8 horizontal directions are free for a few metres: a pocket behind a rock scores low.
        private int Openness(Vector3 ground)
        {
            int open = 0;
            var origin = ground + Vector3.up * 1.2f;
            for (int i = 0; i < 8; i++)
            {
                float ang = i * Mathf.PI / 4f;
                if (!NearestHit(origin, new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)), OpenCheckDistance, out _)) open++;
            }
            return open;
        }

        private bool NearestHit(Vector3 origin, Vector3 dir, float distance, out RaycastHit nearest)
        {
            nearest = default(RaycastHit);
            bool found = false;
            foreach (var h in Physics.RaycastAll(origin, dir, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (IsPlayerCollider(h.collider)) continue;
                if (!found || h.distance < nearest.distance) { nearest = h; found = true; }
            }
            return found;
        }

        private bool IsPlayerCollider(Collider c)
        {
            var root = c.transform.root;
            var player = LocalPlayer;
            if (player != null && root == player.transform.root) return true;
            var cam = CameraController.instance;
            return cam != null && root == cam.transform.root;
        }
    }
}
