using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Ferries ("transports"): boats that sail fixed routes from Resources/XMLs/transportData_&lt;scene&gt;. The client
    /// sails each ferry toward its current waypoint by itself; the server runs the schedule (which waypoint, when to
    /// wait) with 20/2 and keeps positions in step with 2/15. Players ride by standing on the deck.
    /// The Bal Sardan ferries only sail out to sea and back: the server used to move their passengers to the other
    /// zone, and that hand-off is reconstructed in <see cref="FerryCrossings"/>.
    /// </summary>
    public partial class OfflineServer
    {
        private class Ferry
        {
            public int id, type, speed;
            public readonly List<Vector3> waypoints = new List<Vector3>();
            public readonly List<float> waits = new List<float>();   // seconds to wait on arriving at each waypoint
            public int index;          // the waypoint it is sailing to (or waiting at)
            public Vector3 pos;
            public bool moving;
            public float waitUntil;
        }

        private class FerryCrossing
        {
            public int ferry, waypoint;            // reaching this waypoint carries the passengers...
            public int toScene, toFerry, toWaypoint;   // ...onto this ferry, waiting at this waypoint
        }

        // Reconstruction: each Bal Sardan ferry turns back at a point out at sea (West Athagos: south-east of the
        // lighthouse; Bal Sardan: north of the harbour). Arriving there swaps zones onto the other ferry at its own
        // far point, which then sails into its dock.
        private static readonly FerryCrossing[] FerryCrossings =
        {
            new FerryCrossing { ferry = 5, waypoint = 4, toScene = 7, toFerry = 4, toWaypoint = 3 },   // lighthouse -> Bal Sardan
            new FerryCrossing { ferry = 4, waypoint = 3, toScene = 1, toFerry = 5, toWaypoint = 4 },   // Bal Sardan -> lighthouse
        };

        private const float FerrySyncInterval = 2f;
        private const float FerryArrivalHold = 4f;
        private const float FerryLeaveDistance = 8f;   // a passenger this far from their deck spot was moved off the ferry   // a ferry that just received passengers waits this long before sailing on

        private readonly List<Ferry> ferries = new List<Ferry>();
        private float ferrySyncTimer;
        private FerryCrossing pendingCrossing;   // set while the zone change of a crossing loads
        private Vector3 crossingOffset;          // where the player stood, in the ferry's own frame
        private Scr_TransportsHandler transportsHandler;

        // The client moves ferries by setting their position, so a hull passes through the rocks along some routes
        // while a passenger standing on deck would hit them and be left behind. A passenger is kept on their spot on
        // the deck (as the client does for player-boat seats) until the ferry stops at a dock, where they can walk.
        private int ridingId;          // server ferry id; 0 = not riding
        private Vector3 ridingOffset;  // in the ferry's own frame

        /// <summary>Ferries for a newly loaded scene, all at their first waypoint (or at a crossing's arrival point).</summary>
        private void LoadFerries(int sceneId)
        {
            ferries.Clear();
            var ta = Resources.Load("XMLs/transportData_" + sceneId) as TextAsset;
            if (ta == null) return;
            var doc = new XmlDocument();
            doc.LoadXml(ta.text);
            foreach (XmlNode x in doc.DocumentElement.ChildNodes)
            {
                if (x.NodeType != XmlNodeType.Element) continue;
                var f = new Ferry
                {
                    id = GameData.IntAttr(x, "id"),
                    type = GameData.IntAttr(x, "type"),
                    speed = GameData.IntAttr(x, "speed"),
                };
                foreach (XmlNode ph in x.ChildNodes)
                {
                    if (ph.NodeType != XmlNodeType.Element) continue;
                    var d = GameData.Attr(ph, "destination").Split(',');
                    f.waypoints.Add(new Vector3(F(d[0]), F(d[1]), F(d[2])));
                    f.waits.Add(GameData.IntAttr(ph, "waitingtime"));
                }
                if (f.waypoints.Count == 0) continue;
                f.pos = f.waypoints[0];
                f.index = 0;
                f.waitUntil = 0f;   // "arrived" at the first waypoint: the first tick sends it on its way
                ferries.Add(f);
            }

            var c = pendingCrossing;
            if (c != null && c.toScene == sceneId)
            {
                var f = ferries.Find(e => e.id == c.toFerry);
                if (f != null)
                {
                    f.index = c.toWaypoint;
                    f.pos = f.waypoints[c.toWaypoint];
                    f.moving = false;
                    f.waitUntil = float.MaxValue;   // until the passenger is placed (TakeFerryArrival)
                }
            }
            Plugin.Log.LogInfo($"[ferry] scene {sceneId}: {ferries.Count} ferries");
        }

        private void LateUpdate()
        {
            var player = LocalPlayer;
            if (!sceneReady || player == null || PlayerDead)
            {
                ridingId = 0;
                return;
            }
            if (ridingId == 0)
            {
                foreach (var f in ferries)
                {
                    if (!f.moving) continue;
                    var c = ClientFerry(f.id);
                    if (c == null || !c.moving || !StandingOn(player, c)) continue;
                    ridingId = f.id;
                    ridingOffset = Quaternion.Inverse(c.transform.rotation) * (player.transform.position - c.transform.position);
                    Plugin.Log.LogInfo($"[ferry] riding ferry {f.id}");
                    break;
                }
                if (ridingId == 0) return;
            }
            var ferry = ferries.Find(e => e.id == ridingId);
            if (ferry == null || (!ferry.moving && ferry.waits[ferry.index] > 0f))
            {
                if (ferry != null) Plugin.Log.LogInfo($"[ferry] ferry {ferry.id} docked at waypoint {ferry.index}: passenger free to walk");
                ridingId = 0;
                return;
            }
            var boat = ClientFerry(ridingId);
            if (boat == null) return;   // a crossing's ferry the client hasn't created yet
            var spot = boat.transform.position + boat.transform.rotation * ridingOffset;
            if ((player.transform.position - spot).sqrMagnitude > FerryLeaveDistance * FerryLeaveDistance)
            {
                Plugin.Log.LogInfo($"[ferry] passenger left ferry {ridingId} (moved {Vector3.Distance(player.transform.position, spot):F0} m away)");
                ridingId = 0;   // teleported, respawned...: off the ferry
                return;
            }
            player.transform.position = spot;
        }

        /// <summary>
        /// Where a crossing passenger arrives: the same spot on the destination ferry's deck. They ride from there,
        /// and the ferry waits FerryArrivalHold seconds from now before sailing on.
        /// </summary>
        private Vector3? TakeFerryArrival()
        {
            var c = pendingCrossing;
            pendingCrossing = null;
            if (c == null) return null;
            var f = ferries.Find(e => e.id == c.toFerry);
            if (f == null) return null;
            f.waitUntil = Time.time + FerryArrivalHold;
            ridingId = f.id;
            ridingOffset = crossingOffset;
            // The client creates ferries facing north, so the stored offset applies unrotated.
            var spot = f.pos + crossingOffset + Vector3.up * 0.3f;
            Plugin.Log.LogInfo($"[ferry] crossing passenger placed on ferry {f.id} at {SceneWorld.Vec(spot)}");
            return spot;
        }

        private void TickFerries()
        {
            if (!sceneReady || ferries.Count == 0) return;
            foreach (var f in ferries)
            {
                if (!f.moving)
                {
                    if (Time.time < f.waitUntil) continue;
                    f.index = (f.index + 1) % f.waypoints.Count;   // parked at a waypoint: sail on to the next
                    f.moving = true;
                    Send(20, 2, f.id, f.index, true);
                    continue;
                }
                f.pos = Vector3.MoveTowards(f.pos, f.waypoints[f.index], f.speed * Time.deltaTime);
                if ((f.pos - f.waypoints[f.index]).sqrMagnitude > 0.01f) continue;
                if (ArriveFerry(f)) return;   // zone change: the ferry list is gone
            }
            ferrySyncTimer -= Time.deltaTime;
            if (ferrySyncTimer <= 0f) SendFerries();
        }

        /// <summary>Ferry reached its waypoint: cross zones, wait, or sail on. Returns true on a zone change.</summary>
        private bool ArriveFerry(Ferry f)
        {
            foreach (var c in FerryCrossings)
                if (c.ferry == f.id && c.waypoint == f.index && TryCrossOnFerry(f, c)) return true;

            float wait = f.waits[f.index];
            if (wait > 0f)
            {
                f.moving = false;
                f.waitUntil = Time.time + wait;
                Send(20, 2, f.id, f.index, false);
                return false;
            }
            f.index = (f.index + 1) % f.waypoints.Count;
            Send(20, 2, f.id, f.index, true);
            return false;
        }

        private bool TryCrossOnFerry(Ferry f, FerryCrossing c)
        {
            var player = LocalPlayer;
            var boat = ClientFerry(f.id);
            if (player == null || boat == null || PlayerDead) return false;
            if (ridingId == f.id) crossingOffset = ridingOffset;
            else if (StandingOn(player, boat)) crossingOffset = Quaternion.Inverse(boat.transform.rotation) * (player.transform.position - boat.transform.position);
            else return false;
            ridingId = 0;
            Plugin.Log.LogInfo($"[ferry] ferry {f.id} reached waypoint {f.index}: crossing to scene {c.toScene} on ferry {c.toFerry} (deck offset {crossingOffset})");
            ChangeScene(c.toScene, player.transform.position);   // the spawn becomes the deck spot once the ferries exist
            pendingCrossing = c;   // after ChangeScene, whose world reset clears it
            return true;
        }

        private Scr_Transport ClientFerry(int id)
        {
            if (transportsHandler == null) transportsHandler = Object.FindObjectOfType<Scr_TransportsHandler>();
            if (transportsHandler == null) return null;
            foreach (var t in transportsHandler.transports)
                if (t != null && t.id == id) return t;
            return null;
        }

        // On the deck: the ground under the player belongs to the ferry's model.
        private static bool StandingOn(Scr_Player player, Scr_Transport boat)
        {
            var from = player.transform.position + Vector3.up * 0.5f;
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.transform.IsChildOf(boat.transform)) return true;
            return false;
        }

        // Dev: drop the player onto a ferry's deck (client side, like walking aboard).
        internal void DevBoardFerry(int id, float up)
        {
            var c = ClientFerry(id);
            var player = LocalPlayer;
            if (c == null || player == null) { Plugin.Log.LogInfo($"[dev] no ferry {id} here"); return; }
            player.transform.position = c.transform.position + Vector3.up * up;
            Plugin.Log.LogInfo($"[dev] dropped onto ferry {id} from {SceneWorld.Vec(player.transform.position)}");
        }

        internal void LogFerries()
        {
            var player = LocalPlayer;
            foreach (var f in ferries)
            {
                var c = ClientFerry(f.id);
                Plugin.Log.LogInfo($"[dev] ferry {f.id} type {f.type}: server {SceneWorld.Vec(f.pos)} -> waypoint {f.index} {(f.moving ? "moving" : "waiting")}; " +
                    (c != null ? $"client {SceneWorld.Vec(c.transform.position)} waypoint {c.currentwaypoint} moving={c.moving} aboard={player != null && StandingOn(player, c)}" : "client: none"));
            }
            if (player != null) Plugin.Log.LogInfo($"[dev] player at {SceneWorld.Vec(player.transform.position)}");
        }

        /// <summary>
        /// First ferry list of a zone. The client keeps the previous zone's ferries in its list after their objects
        /// are destroyed with the scene, and its 2/15 handler throws removing them, so empty that list first.
        /// </summary>
        private void SendSceneFerries()
        {
            if (transportsHandler == null) transportsHandler = Object.FindObjectOfType<Scr_TransportsHandler>();
            if (transportsHandler != null) transportsHandler.clearTransports();
            SendFerries();
        }

        /// <summary>2/15: every ferry in the zone, "id_type_x_y_z_waypoint_moving" joined by '>'.</summary>
        private void SendFerries()
        {
            ferrySyncTimer = FerrySyncInterval;
            if (character == null) return;
            var inv = CultureInfo.InvariantCulture;
            var parts = new List<string>();
            foreach (var f in ferries)
                parts.Add(f.id + "_" + f.type + "_" + f.pos.x.ToString("F2", inv) + "_" + f.pos.y.ToString("F2", inv) + "_" +
                          f.pos.z.ToString("F2", inv) + "_" + f.index + "_" + (f.moving ? 1 : 0));
            Send(2, 15, character.name, string.Join(">", parts.ToArray()));
        }
    }
}
