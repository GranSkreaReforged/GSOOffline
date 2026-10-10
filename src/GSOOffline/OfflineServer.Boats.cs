using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Player boats. Using a boat item while swimming puts it on the water at the player (the item leaves the bag
    /// and is kept in <see cref="CharacterSave.boat"/> until it's stowed). The client steers the boat itself and
    /// reports it with 5/6; the server keeps the one boat and its passenger and sends the boat list (2/28).
    /// Boats are only for the sea: the client stows a boat that touches the shore (/removeboat).
    /// </summary>
    public partial class OfflineServer
    {
        // Height the client keeps boats at; it stows any boat outside 3..3.25.
        private const float BoatWaterY = 3.15f;
        private const float BoatBoardRange = 15f;
        private const int SailingSkill = 13;
        private const int BoatTeleportScroll = 10465;

        private class BoatType
        {
            public int prefab;   // Resources/1LoadingAssets/PlayerShips/ab_PlayerShip_<prefab>
            public int level;    // Sailing level to launch it
        }

        // Prefab numbers follow the item order (checked with the boatprefabs dev command: 4 is the fishing
        // vessel with the oyster crane). Levels are from the community wiki where it lists them (wooden boat 1,
        // small fishing boat 15, longboat 25, fishing vessel 30); the others are reconstructions.
        private static readonly Dictionary<int, BoatType> Boats = new Dictionary<int, BoatType>
        {
            { 10066, new BoatType { prefab = 1, level = 1 } },    // Small wooden boat
            { 10432, new BoatType { prefab = 2, level = 25 } },   // Longboat
            { 10468, new BoatType { prefab = 3, level = 15 } },   // Small fishing boat
            { 10489, new BoatType { prefab = 4, level = 30 } },   // Fishing vessel (oyster crane)
            { 10549, new BoatType { prefab = 5, level = 20 } },   // Small Bal Sardan boat
            { 10550, new BoatType { prefab = 6, level = 25 } },   // Medium Bal Sardan boat
            { 10571, new BoatType { prefab = 7, level = 25 } },   // Ornamental Longboat
            { 10625, new BoatType { prefab = 8, level = 1 } },    // Old raft
        };

        // Sailing XP is a reconstruction (the server's rate is lost): 1 XP per SailingMetresPerXp sailed
        // while steering, paid out every SailingXpChunk XP. Jumps longer than a boat can sail between two
        // 0.5 s position updates (teleports, launches) don't count.
        private const float SailingMetresPerXp = 4f;
        private const int SailingXpChunk = 5;
        private const float SailingMaxStep = 15f;

        // The launched boat (one per character, in the current scene only).
        private Vector3 boatPos;
        private int boatRot;
        private bool aboard;
        private float sailedMetres;

        private void RegisterBoatHandlers()
        {
            On(2, 29, c => EnterBoat(c[2] as string));   // AttemptEnterBoat(plname, owner)
            On(5, 6, OnBoatMoved);                       // UpdateBoatLocRot(plname, rot, pos)
        }

        private bool BoatOut => character != null && character.boat != null;

        /// <summary>Boat item used from the bag. Returns false if the item isn't a boat.</summary>
        private bool TryLaunchBoat(int typeId)
        {
            if (!Boats.TryGetValue(typeId, out var type)) return false;
            var player = LocalPlayer;
            if (player == null || !sceneReady || PlayerDead) return true;
            if (BoatOut)
            {
                Notice("Your boat is already on the water.", "gray");
                return true;
            }
            if (SkillLevel(SailingSkill) < type.level)
            {
                Notice($"You need level {type.level} Sailing to use this boat.", "red");
                return true;
            }
            if (!player.swimming)
            {
                Plugin.Log.LogInfo($"[boat] launch refused: not swimming at {player.transform.position}");
                Notice("You need to be in the water to launch a boat.", "gray");
                return true;
            }
            var it = character.items.Find(i => i.typeId == typeId);
            if (it == null) return true;

            ReleaseItem(it);
            character.items.Remove(it);
            Send(4, 3, character.name, it.id, it.typeId, 1);
            character.boat = it;
            var p = player.transform.position;
            boatPos = new Vector3(p.x, BoatWaterY, p.z);
            boatRot = (int)player.transform.eulerAngles.y;
            aboard = false;
            sailedMetres = 0f;
            Plugin.Log.LogInfo($"[boat] launched {ItemData.Get(typeId)?.name} (ship {type.prefab}) at {boatPos}");
            SendBoats();
            return true;
        }

        /// <summary>Puts a launched boat back in the bag (shore contact, Remove boat, zone change, login).</summary>
        private void StowBoat(string reason, bool notify = true)
        {
            if (!BoatOut) return;
            var it = character.boat;
            character.boat = null;
            aboard = false;
            character.items.Add(it);
            Plugin.Log.LogInfo($"[boat] stowed {ItemData.Get(it.typeId)?.name}: {reason}");
            if (notify)
            {
                SendItemAdded(it, 1);
                SendBoats();
                Notice($"Your {ItemData.Get(it.typeId)?.name ?? "boat"} is back in your bag.", "gray");
            }
        }

        private void EnterBoat(string owner)
        {
            if (!BoatOut || PlayerDead) return;
            if (!IsMe(owner))
            {
                Notice("That isn't your boat.", "gray");
                return;
            }
            if (aboard) return;
            var player = LocalPlayer;
            if (player != null)
            {
                var p = player.transform.position;
                float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(boatPos.x, boatPos.z));
                if (d > BoatBoardRange)
                {
                    Plugin.Log.LogInfo($"[boat] board refused: {d:F1} m from the boat");
                    Notice("You're too far from your boat.", "gray");
                    return;
                }
            }
            aboard = true;
            Plugin.Log.LogInfo("[boat] boarded");
            SendBoats();
        }

        private void LeaveBoat()
        {
            if (!aboard) return;
            aboard = false;
            Plugin.Log.LogInfo($"[boat] left the boat at {boatPos}");
            SendBoats();
            Send(6, 9);
        }

        private void OnBoatMoved(object[] c)
        {
            if (!BoatOut || !IsMe(c[1] as string)) return;
            var pos = (Vector3)c[3];
            float step = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(boatPos.x, boatPos.z));
            boatPos = pos;
            boatRot = (int)c[2];
            if (!aboard || step > SailingMaxStep) return;
            sailedMetres += step;
            int chunk = Mathf.RoundToInt(SailingXpChunk * SailingMetresPerXp);
            if (sailedMetres >= chunk)
            {
                sailedMetres -= chunk;
                AddXp(SailingSkill, SailingXpChunk);
            }
        }

        /// <summary>Boat teleport scroll: straight into the boat, wherever it is in this zone.</summary>
        private bool TryBoatTeleport(int typeId)
        {
            if (typeId != BoatTeleportScroll) return false;
            if (PlayerDead) return true;
            if (!BoatOut)
            {
                Notice("You have no boat on the water.", "gray");
                return true;
            }
            if (aboard) return true;
            TakeItems(typeId, 1);
            var player = LocalPlayer;
            if (player != null) PlayEffect(GfxTeleport, player.transform.position);
            aboard = true;
            Plugin.Log.LogInfo($"[boat] teleported aboard at {boatPos}");
            SendBoats();
            PlayEffect(GfxTeleportLand, boatPos);
            return true;
        }

        // Small fishing boat: "Fishing experience on this boat is shared and increased among passengers."
        // Offline there are no other passengers; the increase is a reconstruction.
        private const float FishingBoatXpBonus = 1.25f;
        private const int FishingSkill = 6;

        private float FishingBoatXpMultiplier() =>
            aboard && BoatOut && Boats.TryGetValue(character.boat.typeId, out var t) && t.prefab == 3 ? FishingBoatXpBonus : 1f;

        // /enterboat, /leaveboat and /removeboat, from the interact key, the boat menu and the client's shore check.
        private bool RunBoatCommand(string cmd, string[] a)
        {
            string owner = a.Length > 1 ? string.Join(" ", a, 1, a.Length - 1) : character.name;
            switch (cmd)
            {
                case "enterboat":
                    EnterBoat(owner);
                    return true;
                case "leaveboat":
                    if (IsMe(owner)) LeaveBoat();
                    return true;
                case "removeboat":
                    if (IsMe(owner)) StowBoat("removed");
                    return true;
            }
            return false;
        }

        private bool IsMe(string name) => character != null && string.Equals(name, character.name, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 2/28: boats joined by '>', each "x,y,z_rot_seat:name,seat:name_owner_prefab". The client parses every
        /// seat entry as a number and a name, so an empty boat still needs one (an empty name matches nobody).
        /// </summary>
        private void SendBoats()
        {
            if (character == null) return;
            string list = string.Empty;
            if (BoatOut && Boats.TryGetValue(character.boat.typeId, out var type))
            {
                string seats = aboard ? "0:" + character.name : "0:";
                list = SceneWorld.Vec(boatPos) + "_" + boatRot + "_" + seats + "_" + character.name + "_" + type.prefab;
            }
            Send(2, 28, character.name, list);
        }

        /// <summary>A same-zone teleport (respawn, wayshrine, scroll) takes the player off the boat first, or the
        /// client would keep pinning them to their seat.</summary>
        private void DisembarkForTeleport()
        {
            if (!aboard) return;
            aboard = false;
            SendBoats();
        }
    }
}
