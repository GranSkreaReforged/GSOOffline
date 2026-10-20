using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Player housing, as the original server's Scr_HouseHandler ran it, for one player: the plots of
    /// Data/houseplots.json stand as ruins until you build on one (silver and materials from the client's
    /// HouseTypes.xml, matching the plot's size), one house per character. In your house you place furniture
    /// (any furniture item, many of them Carpentry crafts), pick it up again, rotate the empty house, buy more
    /// furniture space and demolish it. Guests, locks for others, selling, guild houses and decay were for the
    /// shared world and aren't here.
    ///
    /// The client does the rest: house models (1LoadingAssets/Houses/ab_poh_[type]), furniture models, the
    /// "Building" window (13) with the types for the plot's size, the "House" window (12), building mode.
    /// 2/12 lists the houses near the player (id_type_center_furniture_forceUpdate_name_owners_size_rotation,
    /// joined by '>'); the door is interactable 20 and opens with 20/1.
    /// </summary>
    public partial class OfflineServer
    {
        private const int InteractHouseDoor = 20, WindowBuilding = 13;
        private const int InteractHouseTeleporter = 62, InteractSwiftnessStone = 67, InteractOrgan = 72, InteractBankChest = 73;
        private const int InteractTrophyDragonHead = 49, InteractTrophyZalranaz = 50, InteractTrophyCaveDragon = 51,
            InteractTrophyRedCrab = 52, InteractGallows = 53, InteractActivityBook = 56, InteractPvpTrophy = 70;
        private const int ItemHouseTeleport = 10486, FurnitureTeleporter = 10624, FurnitureSwiftnessStone = 10642;
        private static readonly int[] FurnitureBankChests = { 10666, 10690 };
        private const int SfxDoorOpen = 170, SfxDoorClose = 171, SfxOrgan = 443;
        private const float DoorOpenSeconds = 10f;
        private const float RefundShare = 0.7f, FullRefundSeconds = 3600f;

        internal class HousePlot
        {
            public int scene, id;
            public List<float> size = new List<float>();   // x, z
            public List<float> pos = new List<float>();    // the corner with the lowest x and z

            public float SizeX => size[0];
            public float SizeZ => size[1];
            public Vector3 Corner => new Vector3(pos[0], pos[1], pos[2]);
            public Vector3 Center => new Vector3(pos[0] + SizeX / 2f, pos[1], pos[2] + SizeZ / 2f);

            public bool Contains(Vector3 p, float margin = 0f) =>
                p.x >= pos[0] - margin && p.x <= pos[0] + SizeX + margin && p.z >= pos[2] - margin && p.z <= pos[2] + SizeZ + margin;
        }

        private class HousePlotFile
        {
            public List<HousePlot> plots = new List<HousePlot>();
        }

        internal class HouseType
        {
            public int typeId, size, cost;
            public string name;
            public readonly List<int[]> materials = new List<int[]>();   // item type, amount
            public readonly List<int> allowedScenes = new List<int>();
        }

        private static List<HousePlot> housePlots;
        private static Dictionary<int, HouseType> houseTypes;

        private static List<HousePlot> HousePlots
        {
            get
            {
                if (housePlots != null) return housePlots;
                housePlots = new List<HousePlot>();
                using (var s = typeof(OfflineServer).Assembly.GetManifestResourceStream("GSOOffline.houseplots.json"))
                {
                    if (s == null) return housePlots;
                    using (var r = new StreamReader(s))
                        housePlots = Json.Deserialize<HousePlotFile>(r.ReadToEnd()).plots;
                }
                Plugin.Log.LogInfo($"[house] {housePlots.Count} house plots");
                return housePlots;
            }
        }

        /// <summary>The client's own HouseTypes.xml (the same file the original server read).</summary>
        private static Dictionary<int, HouseType> HouseTypes
        {
            get
            {
                if (houseTypes != null) return houseTypes;
                houseTypes = new Dictionary<int, HouseType>();
                var asset = Resources.Load("XMLs/HouseTypes", typeof(TextAsset)) as TextAsset;
                if (asset == null) return houseTypes;
                var doc = new XmlDocument();
                doc.LoadXml(asset.text);
                foreach (XmlNode n in doc["housetypes"].ChildNodes)
                {
                    if (n.Attributes == null || n.Attributes["typeid"] == null) continue;
                    var t = new HouseType
                    {
                        typeId = Convert.ToInt32(n.Attributes["typeid"].Value),
                        name = n.Attributes["name"].Value,
                        cost = Convert.ToInt32(n.Attributes["cost"].Value),
                        size = Convert.ToInt32(n.Attributes["size"].Value),
                    };
                    if (n.Attributes["allowedscenes"] != null)
                        foreach (string sc in n.Attributes["allowedscenes"].Value.Split(','))
                            t.allowedScenes.Add(Convert.ToInt32(sc));
                    foreach (XmlNode m in n.ChildNodes)
                        if (m.Attributes != null && m.Attributes["itemid"] != null)
                            t.materials.Add(new[] { Convert.ToInt32(m.Attributes["itemid"].Value), Convert.ToInt32(m.Attributes["amount"].Value) });
                    houseTypes[t.typeId] = t;
                }
                Plugin.Log.LogInfo($"[house] {houseTypes.Count} house types");
                return houseTypes;
            }
        }

        private string lastHouseList;
        private bool houseChanged;          // rebuild the house on the client with the next list (forceUpdate)
        private float doorClosesAt = -1f;
        private HousePlot buildingPlot;     // the ruin whose door opened the Building window

        private void RegisterHousingHandlers()
        {
            On(3, 34, c => BuildHouse((int)c[2]));
            On(3, 52, c => Notice("There are no guilds offline.", "gray"));             // build for guild
            On(6, 18, c => Notice("There are no guilds offline.", "gray"));             // transfer to guild
            On(6, 22, c => RotateHouse());
            On(7, 8, c => SendHouseData());
            On(7, 9, c => Notice("There are no visitors offline.", "gray"));            // kick visitors
            On(18, 1, c => Notice("There is nobody else to share your house with offline.", "gray"));   // owners, whitelist, blacklist
            On(17, 2, c => PlaceFurniture((int)c[2], (int)c[3], (Vector3)c[4]));
            On(5, 4, c => RemoveFurniture((int)c[2], (Vector3)c[3]));
            On(198, 7, c => UpgradeFurnitureSpace());
            On(198, 8, c => { if (InOwnHouse()) Send(198, 11, FurnitureSpaceUpgradeCost()); });
            On(198, 9, c => { if (InOwnHouse()) Send(198, 12, FurnitureSpace()); });
            On(198, 10, c => { if (InOwnHouse()) Send(198, 14, character.house.furniture.Count); });
        }

        private static HousePlot FindPlot(int scene, int id) => HousePlots.Find(p => p.scene == scene && p.id == id);

        private HousePlot OwnPlot => character?.house != null ? FindPlot(character.house.scene, character.house.plot) : null;

        private Vector3 PlayerPos => LocalPlayer != null ? LocalPlayer.transform.position : character.Position;

        /// <summary>Standing on your own plot (the original's getCurrentHouse).</summary>
        private bool InOwnHouse()
        {
            var plot = OwnPlot;
            return plot != null && character.scene == plot.scene && plot.Contains(PlayerPos);
        }

        private bool OwnsPlot(HousePlot plot) =>
            character?.house != null && character.house.scene == plot.scene && character.house.plot == plot.id;

        // ---- the houses the client sees ------------------------------------------------------

        /// <summary>2/12: every plot near the player, as a ruin or as your house with its furniture.</summary>
        private void SendVisibleHouses()
        {
            if (character == null) return;
            Vector3 me = PlayerPos;
            var sb = new StringBuilder();
            bool force = houseChanged;
            foreach (var plot in HousePlots)
            {
                if (plot.scene != character.scene || Vector3.Distance(me, plot.Center) >= plot.SizeX * 10f) continue;
                if (sb.Length > 0) sb.Append('>');
                Vector3 c = plot.Center;
                sb.Append(plot.id).Append('_');
                if (OwnsPlot(plot))
                {
                    var h = character.house;
                    sb.Append(h.type).Append('_').Append(SceneWorld.Vec(c)).Append('_').Append(FurnitureList(h)).Append('_')
                        .Append(force ? 1 : 0).Append('_').Append(Clean(h.name)).Append('_').Append("Owned by " + character.name)
                        .Append('_').Append((int)plot.SizeX).Append('_').Append(h.rotation);
                }
                else
                {
                    // force: a demolished house goes back to ruins
                    sb.Append(0).Append('_').Append(SceneWorld.Vec(c)).Append("_0_").Append(force ? 1 : 0)
                        .Append("_Abandoned house_For sale_").Append((int)plot.SizeX).Append("_0");
                }
            }
            string list = sb.ToString();
            if (list.Length == 0 || (list == lastHouseList && !force)) return;   // the client drops houses missing from a list
            lastHouseList = force ? null : list;
            houseChanged = false;
            Plugin.Log.LogInfo($"[house] {list.Split('>').Length} houses in view{(force ? " (rebuilt)" : "")}");
            Send(2, 12, character.name, list);
        }

        private static string FurnitureList(HouseSave h)
        {
            if (h.furniture.Count == 0) return "0";
            var parts = new List<string>();
            foreach (var f in h.furniture) parts.Add(f.type + "," + f.rot + "," + SceneWorld.Vec(new Vector3(f.x, f.y, f.z)));
            return string.Join("|", parts.ToArray());
        }

        private static string Clean(string s) => (s ?? "House").Replace('_', ' ').Replace('>', ' ').Replace('|', ' ');

        private void HouseChanged()
        {
            houseChanged = true;
            SendVisibleHouses();
        }

        /// <summary>Zone change: the client lost its house objects with the scene.</summary>
        private void ResetHousing()
        {
            lastHouseList = null;
            buildingPlot = null;
            doorClosesAt = -1f;
        }

        private void TickHousing()
        {
            if (doorClosesAt > 0f && Time.time >= doorClosesAt)
            {
                doorClosesAt = -1f;
                if (character?.house != null) Send(20, 1, character.house.plot, character.house.scene, false);
            }
        }

        // ---- door and building ---------------------------------------------------------------

        /// <summary>A house door (or a ruin's): yours opens and closes, a ruin offers the Building window.</summary>
        private void HouseDoorClicked()
        {
            Vector3 me = PlayerPos;
            foreach (var plot in HousePlots)
            {
                if (plot.scene != character.scene) continue;
                if (plot.Contains(me, 2f))
                {
                    if (OwnsPlot(plot))
                    {
                        bool open = doorClosesAt < 0f;
                        Notice(open ? "You open the door." : "You close the door.", "white");
                        Send(20, 1, plot.id, plot.scene, open);
                        PlaySound(open ? SfxDoorOpen : SfxDoorClose, me, 30);
                        doorClosesAt = open ? Time.time + DoorOpenSeconds : -1f;
                    }
                    else
                    {
                        buildingPlot = plot;
                        Send(3, 33, character.name, WindowBuilding);
                    }
                    return;
                }
                if (Vector3.Distance(plot.Center, me) < 10f)
                {
                    Notice("That's too far away.", "gray");
                    return;
                }
            }
        }

        /// <summary>3/34: build a house type on the ruin whose door opened the window.</summary>
        private void BuildHouse(int typeId)
        {
            var plot = buildingPlot;
            if (plot == null || plot.scene != character.scene || !plot.Contains(PlayerPos, 10f))
            {
                Notice("You must be at the plot to build there.", "gray");
                return;
            }
            if (character.house != null)
            {
                Notice("You already own a house! Demolish your current house to build a new one.", "white");
                return;
            }
            if (!HouseTypes.TryGetValue(typeId, out var type) || type.size != (int)plot.SizeX
                || (type.allowedScenes.Count > 0 && !type.allowedScenes.Contains(character.scene)))
            {
                Notice("You cannot build that house here.", "white");
                return;
            }
            bool hasAll = character.silver >= type.cost;
            foreach (var m in type.materials) if (CountItem(m[0]) < m[1]) hasAll = false;
            if (!hasAll)
            {
                Notice("You don't have the materials to build this house.", "white");
                return;
            }
            foreach (var m in type.materials) TakeItems(m[0], m[1]);
            SetSilver(character.silver + (-type.cost));
            character.house = new HouseSave
            {
                scene = plot.scene, plot = plot.id, type = typeId, name = type.name,
                builtAt = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond,
            };
            buildingPlot = null;
            Plugin.Log.LogInfo($"[house] built {type.name} ({typeId}) on plot {plot.id} in scene {plot.scene}");
            Notice($"You build a {type.name}. Use furniture from your bag inside it to place it.", "green");
            SaveCurrentCharacter();
            HouseChanged();
        }

        // ---- the House window ----------------------------------------------------------------

        /// <summary>7/8 → 2/13: name_locked_owners_whitelist_blacklist_guildId, while you're in your house.</summary>
        private void SendHouseData()
        {
            if (!InOwnHouse()) return;
            var h = character.house;
            Send(2, 13, character.name, Clean(h.name) + "_" + (h.locked ? "1" : "0") + "_" + character.name + "___-1");
            Send(198, 12, FurnitureSpace());
            Send(198, 14, h.furniture.Count);
        }

        private int FurnitureSpace() => (int)OwnPlot.SizeX * 5 + character.house.extraSpace;

        // The original's prices: 5 more pieces for 5,000 silver, rising by 5,000 every 50, up to double the base.
        private int FurnitureSpaceUpgradeCost()
        {
            int extra = character.house.extraSpace;
            return extra < 250 ? 5000 * (extra / 50 + 1) : 30000;
        }

        private void UpgradeFurnitureSpace()
        {
            if (!InOwnHouse()) return;
            var h = character.house;
            if (h.extraSpace >= (int)OwnPlot.SizeX * 5)
            {
                Notice("Your furniture space is already fully upgraded", "red");
                return;
            }
            int cost = FurnitureSpaceUpgradeCost();
            if (character.silver < cost)
            {
                Notice("You don't have enough money to upgrade your furniture space", "red");
                return;
            }
            SetSilver(character.silver + (-cost));
            h.extraSpace += 5;
            Notice($"Furniture space upgrade successfull! Your house now has {FurnitureSpace()} furniture space.", "green");
            Send(198, 11, FurnitureSpaceUpgradeCost());
            Send(198, 12, FurnitureSpace());
        }

        private void RotateHouse()
        {
            if (!InOwnHouse())
            {
                Notice("You must be inside your house to do that.", "white");
                return;
            }
            var h = character.house;
            if (h.furniture.Count > 0)
            {
                Notice($"You must remove all furniture before rotating the house. There {(h.furniture.Count == 1 ? "is" : "are")} still {h.furniture.Count} pieces of furniture in the house.", "white");
                return;
            }
            h.rotation = (h.rotation + 90) % 360;
            Teleport(character.scene, OwnPlot.Corner);   // out of the walls' way
            Notice("The house has been rotated by 90 degrees", "white");
            Send(16, 6, h.plot, h.rotation);
            HouseChanged();
        }

        /// <summary>
        /// The House window's "Demolish house": the furniture must be out first. Its confirmation promises 70% of
        /// the silver and materials back, or all of it within an hour of building; offline it keeps that promise.
        /// </summary>
        private void DemolishHouse()
        {
            if (!InOwnHouse())
            {
                Notice("You must be inside your house to do that.", "white");
                return;
            }
            var h = character.house;
            if (h.furniture.Count > 0)
            {
                Notice($"You must remove all furniture before demolishing the house. There {(h.furniture.Count == 1 ? "is" : "are")} still {h.furniture.Count} pieces of furniture in the house.", "white");
                return;
            }
            Vector3 corner = OwnPlot.Corner;
            long age = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond - h.builtAt;
            float share = age >= 0 && age < FullRefundSeconds ? 1f : RefundShare;
            if (HouseTypes.TryGetValue(h.type, out var type))
            {
                SetSilver(character.silver + (Mathf.RoundToInt(type.cost * share)));
                foreach (var m in type.materials)
                {
                    int n = Mathf.RoundToInt(m[1] * share);
                    if (n > 0) GiveItem(m[0], n);
                }
            }
            character.house = null;
            Plugin.Log.LogInfo($"[house] demolished, {share * 100:F0}% back");
            Notice($"You demolish your house and get back {share * 100:F0}% of its silver and materials.", "white");
            SaveCurrentCharacter();
            Teleport(character.scene, corner);
            HouseChanged();
        }

        /// <summary>Chat commands the House window sends. True if it was one.</summary>
        private bool RunHouseCommand(string cmd, string[] a)
        {
            switch (cmd)
            {
                case "demolishhouse":
                    DemolishHouse();
                    return true;
                case "lockhousedoor":
                    if (!InOwnHouse()) return true;
                    character.house.locked = !character.house.locked;
                    Notice(character.house.locked ? "You lock the door." : "You unlock the door.", "white");
                    SendHouseData();
                    return true;
                case "changehousename":
                    if (!InOwnHouse() || a.Length < 2) return true;
                    character.house.name = string.Join(" ", a, 1, a.Length - 1);
                    HouseChanged();
                    return true;
                case "sellhouse":
                case "accepthouseselloffer":
                case "removefromwhitelist":
                case "removefromblacklist":
                    Notice("There is nobody else to share your house with offline.", "gray");
                    return true;
            }
            return false;
        }

        // ---- furniture -----------------------------------------------------------------------

        /// <summary>17/2: the client placed a furniture item from the bag (building mode).</summary>
        private void PlaceFurniture(int type, int rotation, Vector3 pos)
        {
            var plot = OwnPlot;
            if (plot == null || character.scene != plot.scene || !plot.Contains(PlayerPos) || !plot.Contains(pos))
            {
                Notice("You must be in your house to do that.", "white");
                return;
            }
            var h = character.house;
            if (h.furniture.Count >= FurnitureSpace())
            {
                Notice($"Maximum number of furniture reached ({FurnitureSpace()}).", "white");
                return;
            }
            if (CountItem(type) == 0) return;
            TakeItems(type, 1);
            h.furniture.Add(new FurnitureSave
            {
                type = type, rot = rotation,
                x = Mathf.Round(pos.x * 10f) / 10f, y = Mathf.Round(pos.y * 10f) / 10f, z = Mathf.Round(pos.z * 10f) / 10f,
            });
            Plugin.Log.LogInfo($"[house] placed {ItemData.Get(type)?.name} at {SceneWorld.Vec(pos)} ({h.furniture.Count}/{FurnitureSpace()})");
            HouseChanged();
        }

        /// <summary>5/4: building mode's pick-up: the nearest piece of that type back into the bag.</summary>
        private void RemoveFurniture(int type, Vector3 pos)
        {
            if (!InOwnHouse()) return;
            FurnitureSave best = null;
            float bestDist = float.MaxValue;
            foreach (var f in character.house.furniture)
            {
                if (f.type != type) continue;
                float d = Vector3.Distance(new Vector3(f.x, f.y, f.z), pos);
                if (d < bestDist) { bestDist = d; best = f; }
            }
            if (best == null) return;
            character.house.furniture.Remove(best);
            GiveItem(type);
            Plugin.Log.LogInfo($"[house] picked up {ItemData.Get(type)?.name}");
            HouseChanged();
        }

        private bool NearFurniture(int type, float range = 10f)
        {
            if (!InOwnHouse()) return false;
            Vector3 me = PlayerPos;
            foreach (var f in character.house.furniture)
                if (f.type == type && Vector3.Distance(me, new Vector3(f.x, f.y, f.z)) < range) return true;
            return false;
        }

        /// <summary>Furniture you click: the original's interactables. True if handled here.</summary>
        private bool HandleFurnitureInteract(int typeId)
        {
            switch (typeId)
            {
                case InteractHouseDoor:
                    HouseDoorClicked();
                    return true;
                case InteractBankChest:
                    if (NearFurniture(FurnitureBankChests[0]) || NearFurniture(FurnitureBankChests[1]))
                    {
                        OpenBank();
                        Notice("You open the bank chest.", "white");
                    }
                    return true;
                case InteractOrgan:
                    Notice("The organ starts playing music...", "white");
                    PlaySound(SfxOrgan, PlayerPos, 40);
                    return true;
                case InteractSwiftnessStone:
                    if (!NearFurniture(FurnitureSwiftnessStone))
                    {
                        Notice("You must be inside the house to use this object.", "white");
                        return true;
                    }
                    Notice("You touch the stone... ", "white");
                    if (!buffs.Exists(b => b.icon == 33))
                    {
                        AddBuff(-FurnitureSwiftnessStone, 33, 30f, new BuffEffect());
                        PlayPlayerEffect(82);
                        PlaySound(66, PlayerPos, 40);
                    }
                    return true;
                case InteractHouseTeleporter:
                    UseHouseTeleporter();
                    return true;
                case InteractGallows:
                    Send(2, 0, string.Empty, character.name + ": Not today, old friend.", 0);
                    return true;
                case InteractTrophyDragonHead: TrophyKillCounts(244, 98); return true;
                case InteractTrophyZalranaz: TrophyKillCounts(207); return true;
                case InteractTrophyCaveDragon: TrophyKillCounts(118, 297, 293); return true;
                case InteractTrophyRedCrab: TrophyKillCounts(164); return true;
                case InteractPvpTrophy:
                    Notice("Received for reaching 1st place in the first ever PvP tournament", "white");
                    return true;
                case InteractActivityBook:
                    Notice("Activity books only work in guild houses.", "white");
                    return true;
            }
            return false;
        }

        /// <summary>Two house teleporters: one takes you to the other.</summary>
        private void UseHouseTeleporter()
        {
            if (!InOwnHouse()) return;
            Vector3 me = PlayerPos;
            FurnitureSave from = null;
            float fromDist = 5f;
            foreach (var f in character.house.furniture)
            {
                if (f.type != FurnitureTeleporter) continue;
                float d = Vector3.Distance(me, new Vector3(f.x, f.y, f.z));
                if (d < fromDist) { fromDist = d; from = f; }
            }
            if (from == null)
            {
                Notice("That's too far away.", "white");
                return;
            }
            var to = character.house.furniture.Find(f => f.type == FurnitureTeleporter && f != from);
            if (to == null)
            {
                Notice("This teleporter does not lead anywhere.", "red");
                return;
            }
            Teleport(character.scene, new Vector3(to.x, to.y + 0.2f, to.z));
        }

        private void TrophyKillCounts(params int[] npcTypes)
        {
            if (!InOwnHouse()) return;
            foreach (int t in npcTypes)
                Notice($"{character.name}'s {NpcName(t)} kill count: {character.GetKills(t)}", "white");
        }

        /// <summary>The House teleport scroll: to your plot's corner (the original's spot).</summary>
        private bool ReadHouseTeleport(int typeId)
        {
            if (typeId != ItemHouseTeleport) return false;
            var plot = OwnPlot;
            if (plot == null)
            {
                Notice("You must have a house to use this teleport.", "white");
                return true;
            }
            if (PlayerDead || TeleportCoolingDown()) return true;
            TakeItems(typeId, 1);
            StartTeleportCooldown();
            var player = LocalPlayer;
            if (player != null) PlayEffect(GfxTeleport, player.transform.position);
            arrivalGfx = GfxTeleportLand;
            Teleport(plot.scene, plot.Corner);
            return true;
        }
    }
}
