using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Boarding a ferry by talking to the Sailor on its pier (#137). The lighthouse Sailor (type 77) serves both
    /// lighthouse piers; content.json spawns more of them at Yorkhill and Bal Sardan. A docked ferry takes the player
    /// aboard for silver (the original's boat-ride prices); one at sea is due back in the time the Sailor gives.
    /// </summary>
    public partial class OfflineServer
    {
        private class FerryPier
        {
            public int scene, ferry, waypoint, silver;
            public float x, z;          // the ferry's stop
            public string ship, to;
        }

        // The original's rides: lighthouse -> Bal Sardan 20 silver (action 36), back 50 (action 37). The Coral Lady had
        // no fare; she costs what the short ferry does.
        private static readonly FerryPier[] FerryPiers =
        {
            new FerryPier { scene = 1, ferry = 3, waypoint = 0, x = 520.9f, z = 881f, ship = "the Coral Lady", to = "Yorkhill", silver = 20 },
            new FerryPier { scene = 1, ferry = 3, waypoint = 13, x = 961f, z = 1557f, ship = "the Coral Lady", to = "the lighthouse", silver = 20 },
            new FerryPier { scene = 1, ferry = 5, waypoint = 0, x = 563.3f, z = 882.3f, ship = "the ferry", to = "Bal Sardan", silver = 20 },
            new FerryPier { scene = 7, ferry = 4, waypoint = 0, x = 158.3f, z = 731.3f, ship = "the ferry", to = "West Athagos", silver = 50 },
        };

        private const int FerrySailorType = 77, FerrySailorGreeting = 537;   // "Hello traveler!"
        private const int NodeFerrySailor = 90010;
        private const int ActionBoardFerry = 9030;   // + the pier's index
        private const float PierServeDistance = 45f;   // a Sailor this close to a ferry's stop serves it

        /// <summary>A Sailor by a ferry pier greets with the ferries instead of "Hello traveler!".</summary>
        private int FerrySailorNode(NpcEntity npc, int node)
        {
            if (npc.typeId != FerrySailorType || node != FerrySailorGreeting) return node;
            return PiersServedBy(npc).Count > 0 ? NodeFerrySailor : node;
        }

        private List<FerryPier> PiersServedBy(NpcEntity npc)
        {
            var piers = new List<FerryPier>();
            if (npc == null || character == null) return piers;
            var at = npc.spawnPos;
            foreach (var p in FerryPiers)
                if (p.scene == character.scene && new Vector2(p.x - at.x, p.z - at.z).magnitude < PierServeDistance) piers.Add(p);
            return piers;
        }

        private bool PierServed(FerryPier p) => dialogueNpc != null && PiersServedBy(dialogueNpc).Contains(p);

        private bool FerryDocked(FerryPier p)
        {
            var f = ferries.Find(e => e.id == p.ferry);
            return f != null && !f.moving && f.index == p.waypoint;
        }

        /// <summary>A docked ferry waits while the player talks to its Sailor (the docks' waits are 5 to 20 seconds).</summary>
        private bool HeldBySailor(Ferry f)
        {
            if (dialogueNode == null || dialogueNode.id != NodeFerrySailor) return false;
            foreach (var p in PiersServedBy(dialogueNpc))
                if (p.ferry == f.id && p.waypoint == f.index) return true;
            return false;
        }

        private List<AddedNode> FerryNodes()
        {
            var greeting = new AddedNode { id = NodeFerrySailor, text = "$1" };
            for (int i = 0; i < FerryPiers.Length; i++)
            {
                var p = FerryPiers[i];
                greeting.options.Add(new AddedOption { id = i + 1, next = 0, action = ActionBoardFerry + i,
                    text = $"Take me aboard {p.ship} to {p.to} ({p.silver} silver).", shownIf = () => PierServed(p) && FerryDocked(p) });
            }
            greeting.options.Add(new AddedOption { id = 9, next = 0, text = "Nevermind" });
            return new List<AddedNode> { greeting };
        }

        private string FerryNodeVar(int nodeId)
        {
            if (nodeId != NodeFerrySailor) return null;
            var lines = new List<string> { "Need a ride?" };
            foreach (var p in PiersServedBy(dialogueNpc))
            {
                var f = ferries.Find(e => e.id == p.ferry);
                if (f == null) continue;
                string ship = char.ToUpperInvariant(p.ship[0]) + p.ship.Substring(1);
                lines.Add(FerryDocked(p)
                    ? $"{ship} to {p.to} is in: {p.silver} silver and you're aboard."
                    : $"{ship} to {p.to} is out at sea, due back in {DueIn(FerryDueIn(f, p.waypoint))}.");
            }
            return string.Join(" ", lines.ToArray());
        }

        private static string DueIn(float seconds)
        {
            if (seconds < 60f) return $"about {Mathf.Max(10, Mathf.CeilToInt(seconds / 10f) * 10)} seconds";
            int minutes = Mathf.RoundToInt(seconds / 60f);
            return minutes == 1 ? "about a minute" : $"about {minutes} minutes";
        }

        /// <summary>Seconds until the ferry next stands at that waypoint, by the server's schedule.</summary>
        private static float FerryDueIn(Ferry f, int waypoint)
        {
            int n = f.waypoints.Count, i = f.index;
            float t;
            if (f.moving)
            {
                t = Vector3.Distance(f.pos, f.waypoints[i]) / f.speed;
                if (i == waypoint) return t;
                t += f.waits[i];
            }
            else
            {
                if (i == waypoint) return 0f;
                t = Mathf.Clamp(f.waitUntil - Time.time, 0f, f.waits[i]);
            }
            for (int step = 0; step < n; step++)
            {
                int next = (i + 1) % n;
                t += Vector3.Distance(f.waypoints[i], f.waypoints[next]) / f.speed;
                i = next;
                if (i == waypoint) return t;
                t += f.waits[i];
            }
            return t;
        }

        private bool RunFerryAction(int id)
        {
            int i = id - ActionBoardFerry;
            if (i < 0 || i >= FerryPiers.Length) return false;
            BoardFerry(FerryPiers[i]);
            return true;
        }

        private void BoardFerry(FerryPier p)
        {
            var f = ferries.Find(e => e.id == p.ferry);
            var boat = ClientFerry(p.ferry);
            var player = LocalPlayer;
            if (f == null || boat == null || player == null || !FerryDocked(p))
            {
                Notice($"{p.ship.Substring(0, 1).ToUpperInvariant()}{p.ship.Substring(1)} has just sailed.", "gray");
                return;
            }
            if (character.silver < p.silver)
            {
                Notice("You don't have enough silver.", "red");
                return;
            }
            SetSilver(character.silver - p.silver);
            player.transform.position = DeckSpot(boat);
            f.passengerOnArrival = false;   // aboard now: it sails a moment later (BoardedAtDock)
            Plugin.Log.LogInfo($"[ferry] boarded ferry {p.ferry} at waypoint {p.waypoint} for {p.silver} silver: {SceneWorld.Vec(player.transform.position)}");
        }

        // The middle of the deck: the lowest surface of the ferry straight down through its centre (sails and spars
        // are above the deck, and the hull's inside faces don't stop a raycast).
        private static Vector3 DeckSpot(Scr_Transport boat)
        {
            var centre = boat.transform.position;
            float? deck = null;
            foreach (var hit in Physics.RaycastAll(centre + Vector3.up * 25f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.transform.IsChildOf(boat.transform) && (deck == null || hit.point.y < deck.Value)) deck = hit.point.y;
            return new Vector3(centre.x, (deck ?? centre.y + 2f) + 0.3f, centre.z);
        }
    }
}
