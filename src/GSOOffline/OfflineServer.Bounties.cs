using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Random = UnityEngine.Random;

namespace GSOOffline
{
    /// <summary>
    /// The bounty board (issue #67): with no other players to hunt, the wanted boards list outlaws instead, waiting
    /// out in the wild among creatures of their own level. An outlaw looks and fights like a player: the client draws
    /// it as one (2/8 visible players, 5/2 positions, 7/22 animation packets), it wears real gear rolled like loot,
    /// and its numbers come from that gear by the player's own formulas (damage, resistances with the soft cap,
    /// damage block, health). Its weapon decides how it fights: up close, or keeping its distance and shooting or
    /// casting, with a stronger attack every few swings.
    ///
    /// Server side it is an NPC: the creature AI, the player's attacks and abilities, aggro and death all run
    /// unchanged on an NpcEntity with a made-up NpcInfo. Send() turns the NPC messages about it into the matching
    /// player messages (<see cref="TranslateBountySend"/>). Killing it pays its bounty, drops some of the gear it
    /// wore (the very items) and a Small experience scroll, and takes it off the board for good.
    /// </summary>
    public partial class OfflineServer
    {
        private const int WindowWantedBoard = 36, InteractWantedBoard = 43;
        private const int BountyTypeBase = 95000, BountyUidBase = 7600000;
        private const int ItemSmallXpScroll = 10327;
        private const float BountyBodySeconds = 30f;
        // The boss name tag every boss in NPCInfo wears: eyes either side of the name (emblem) on a banner (emblemBg).
        private const int BossEmblem = 156, BossEmblemBg = 244;

        private enum BountyStyle { Swordsman, Brute, Archer, Crossbowman, FireMage }

        private static readonly string[] StyleNames = { "swordsman", "two-hander", "archer", "crossbowman", "fire mage" };

        internal class BountySpot
        {
            public int scene;
            public string zone;
            public List<float> pos = new List<float>();
            public int level, minLevel, maxLevel;
            public List<string> creatures = new List<string>();
            public string landmark, direction;
            public int distance;
        }

        private class BountySpotFile
        {
            public List<BountySpot> spots = new List<BountySpot>();
        }

        private static List<BountySpot> bountySpots;

        private static List<BountySpot> BountySpots
        {
            get
            {
                if (bountySpots != null) return bountySpots;
                bountySpots = new List<BountySpot>();
                using (var s = typeof(OfflineServer).Assembly.GetManifestResourceStream("GSOOffline.bountyspots.json"))
                {
                    if (s == null) return bountySpots;
                    using (var r = new StreamReader(s))
                        bountySpots = Json.Deserialize<BountySpotFile>(r.ReadToEnd()).spots;
                }
                Plugin.Log.LogInfo($"[bounty] {bountySpots.Count} bounty spots");
                return bountySpots;
            }
        }

        // The wanted boards (interactable 43, Prefab_Interactable_43WantedBoard) by scene.
        private static readonly KeyValuePair<int, Vector3>[] WantedBoards =
        {
            new KeyValuePair<int, Vector3>(1, new Vector3(697.6f, 4.43f, 1348.73f)),
            new KeyValuePair<int, Vector3>(8, new Vector3(931.07f, 21.37f, 1354.63f)),
            new KeyValuePair<int, Vector3>(18, new Vector3(789.46f, 30.97f, 400.17f)),
            new KeyValuePair<int, Vector3>(19, new Vector3(471.9f, 5.86f, 259.29f)),
            new KeyValuePair<int, Vector3>(31, new Vector3(901.6f, 5.71f, 554.74f)),
        };

        private static readonly string[] FirstNames =
        {
            "Garrick", "Edric", "Osric", "Wulfric", "Brand", "Torvald", "Halvar", "Cedric", "Rowan", "Aldous", "Fenwick",
            "Mirela", "Isolde", "Brenna", "Sigrun", "Ysolde", "Maren", "Hilde", "Ragna", "Elspeth", "Corwin", "Dunstan",
        };
        private static readonly string[] Epithets =
        {
            "the Red", "the Grim", "Blackhand", "the Butcher", "Two-Knives", "the Wolf", "Ironjaw", "the Quiet", "Ashborn",
            "the Viper", "Coldeye", "the Unhanged", "Bloodfeather", "the Crow", "Stonefist", "the Liar",
        };

        /// <summary>One outlaw in the current scene: its save, its NPC and what the client was last told.</summary>
        private class BountyActor
        {
            public BountySave save;
            public NpcEntity npc;
            public int lastHp;
            public int attackAnim;        // the client's animation id for the weapon (getAnimationdIdFromAttackId)
            public bool ranged, combat;
            public int swings;
            public float nextKiteAt;      // backing off has a cooldown: in between it stands and fights
            public Vector3 lastClear;     // where it last stood with nothing through its body
            public int lastAbility = -1;
            public float removeAt = -1f;  // dead: the body goes after a while
        }

        private readonly Dictionary<int, BountyActor> bountyByUid = new Dictionary<int, BountyActor>();
        private string lastBountyList;
        private float bountyTickTimer, unstickTimer;

        private void RegisterBountyHandlers()
        {
            On(2, 9, c => SelectBountyTarget(c.Length > 2 ? c[2] as string : null));   // selectPlayerTarget (name, target)
            On(7, 32, c => StartBounty(c.Length > 1 ? c[1] as string : null));          // the board's "Add bounty" button
        }

        private static long UnixNow => DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;

        // ---- the board -----------------------------------------------------------------------

        /// <summary>A wanted board click: lists the bounties (window 36, then 7/31 per outlaw). True if it was one.</summary>
        private bool HandleBountyBoard(int typeId)
        {
            if (typeId != InteractWantedBoard || character == null) return false;
            Vector3 me = PlayerPos;
            bool atBoard = false;
            foreach (var b in WantedBoards)
                if (b.Key == character.scene && Vector3.Distance(b.Value, me) < 6f) atBoard = true;
            if (!atBoard) return false;
            RefreshBounties();
            Send(3, 33, character.name, WindowWantedBoard);
            foreach (var b in character.bounties)
            {
                Send(7, 31, JsonUtility.ToJson(BoardEntry(b)));
            }
            if (character.bounties.Count == 0) Notice("The board is empty. Come back later.", "white");
            Later(0.2f, WidenWantedBoard);
            Plugin.Log.LogInfo($"[bounty] board: {character.bounties.Count} outlaws");
            return true;
        }

        /// <summary>
        /// One board line. A bounty not yet started shows as an "Outlaw" in red, which is the client's only entry with
        /// a button ("Add bounty", 7/32): pressing it starts the hunt. A started one shows plain, marked "hunting".
        /// </summary>
        private static Scr_WantedBoard.WantedCriminal BoardEntry(BountySave b) => new Scr_WantedBoard.WantedCriminal
        {
            playerName = $"{b.name}, level {b.level} {StyleNames[b.style]}" + (b.started ? " (hunting)" : string.Empty),
            lastArea = $"{b.lastSeen}. Wears {GearSummary(b)}",
            criminalTimer = b.started ? 0 : 1801,
            murderCount = b.murders,
            bounty = b.silver,
        };

        /// <summary>
        /// 7/32, the board's "Add bounty" button (it sends the entry's name line): the bounty starts, and the outlaw
        /// takes its place in the world. The open board's line is updated in place.
        /// </summary>
        private void StartBounty(string line)
        {
            if (character == null || string.IsNullOrEmpty(line)) return;
            var b = character.bounties.Find(o => line.StartsWith(o.name + ","));
            if (b == null) return;
            if (b.started)
            {
                Notice($"You're already hunting {b.name}.", "gray");
                return;
            }
            b.started = true;
            Notice($"You take the bounty on {b.name}: {b.silver} silver. Last seen: {b.lastSeen}.", "green");
            Plugin.Log.LogInfo($"[bounty] started: {b.name}");
            var list = Script_WindowController.instance?.window_wantedCriminals?.criminalsList;
            if (list != null)
                for (int i = 0; i < list.Count; i++)
                    if (list[i].playerName.StartsWith(b.name + ",")) list[i] = BoardEntry(b);
            if (world != null) SyncBountyActors();
            SaveCurrentCharacter();
        }

        /// <summary>
        /// The board's window is sized for one-line player entries; an outlaw's needs three. Widen it once it's open
        /// (the window is resizable anyway; a player who made it bigger keeps their size).
        /// </summary>
        private static void WidenWantedBoard()
        {
            var wc = Script_WindowController.instance;
            if (wc == null) return;
            var windows = HarmonyLib.Traverse.Create(wc).Field("windows").GetValue<List<Script_Window>>();
            if (windows == null) return;
            foreach (var w in windows)
            {
                if (w.windowType != WindowWantedBoard || !w.windowActive) continue;
                float width = Mathf.Min(Screen.width - 100f, 1100f), height = Mathf.Min(Screen.height - 100f, 620f);
                if (w.windowRect.width >= width) return;
                w.windowRect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
            }
        }

        private static string GearSummary(BountySave b)
        {
            var parts = new List<string>();
            foreach (var it in b.gear)
            {
                var t = ItemData.Get(it.typeId);
                if (t == null) continue;
                if (t.slot == "Weapon" || t.slot == "Shield" || t.slot == "Body") parts.Add(ShortItemName(t.name).ToLowerInvariant());
            }
            return string.Join(", ", parts.ToArray());
        }

        private static string ShortItemName(string name) => name.Replace(" body", " armour");

        /// <summary>Old bounties (an hour) go unless being fought; every 15 minutes the board fills up to 3-5.</summary>
        private void RefreshBounties()
        {
            if (character == null) return;
            long now = UnixNow;
            int before = character.bounties.Count;
            character.bounties.RemoveAll(b => !b.started && now - b.createdAt > CombatRules.BountyLifetimeSeconds && !BountyEngaged(b));
            bool changed = character.bounties.Count != before;
            if (now >= character.bountyRefreshAt)
            {
                character.bountyRefreshAt = now + CombatRules.BountyRefreshSeconds;
                int target = Random.Range(CombatRules.BountyMin, CombatRules.BountyMax + 1);
                for (int tries = 0; character.bounties.Count < target && tries < 20; tries++)
                {
                    var b = MakeBounty();
                    if (b == null) continue;
                    character.bounties.Add(b);
                    changed = true;
                    Plugin.Log.LogInfo($"[bounty] new: {b.name}, level {b.level} {StyleNames[b.style]}, {b.silver} silver, scene {b.scene} {b.lastSeen}");
                }
            }
            if (changed && world != null) SyncBountyActors();
        }

        private bool BountyEngaged(BountySave b)
        {
            foreach (var a in bountyByUid.Values)
                if (a.save == b && (a.npc.aggro || a.npc.dead)) return true;
            return false;
        }

        // ---- making an outlaw ----------------------------------------------------------------

        private BountySave MakeBounty()
        {
            // The outlaw's level stays within BountyLevelRange of your combat level, whatever the spot.
            int cl = CombatLevel();
            int lowest = Mathf.Max(CombatRules.BountyMinLevel, cl - CombatRules.BountyLevelRange), highest = Mathf.Min(100, cl + CombatRules.BountyLevelRange);
            int wanted = Random.Range(lowest, highest + 1);
            // A spot whose creatures are all within 5 levels of the outlaw, not taken by another bounty.
            var free = BountySpots.FindAll(s => !character.bounties.Exists(b => b.scene == s.scene
                && Vector3.Distance(new Vector3(b.x, b.y, b.z), SpotPos(s)) < 60f));
            if (free.Count == 0) return null;
            var fits = free.FindAll(s => s.minLevel >= wanted - 5 && s.maxLevel <= wanted + 5);
            BountySpot spot = null;
            int level = wanted;
            if (fits.Count > 0) spot = fits[Random.Range(0, fits.Count)];
            else
            {
                // None at that level: the nearest spot whose creatures allow an outlaw level still within your range.
                free.Sort((a, b) => Mathf.Abs(a.level - wanted).CompareTo(Mathf.Abs(b.level - wanted)));
                foreach (var s in free)
                {
                    int lo = Mathf.Max(lowest, s.maxLevel - 5), hi = Mathf.Min(highest, s.minLevel + 5);
                    if (lo > hi) continue;
                    spot = s;
                    level = Mathf.Clamp(wanted, lo, hi);
                    break;
                }
                if (spot == null) return null;   // nowhere in the world for an outlaw of your level
            }
            // A style nobody on the board has yet, if there is one.
            var unused = new List<int>();
            for (int i = 0; i < StyleNames.Length; i++)
                if (!character.bounties.Exists(o => o.style == i)) unused.Add(i);
            var style = (BountyStyle)(unused.Count > 0 ? unused[Random.Range(0, unused.Count)] : Random.Range(0, StyleNames.Length));
            Vector2 off = Random.insideUnitCircle * 6f;
            Vector3 pos = SpotPos(spot) + new Vector3(off.x, 0f, off.y);
            var b = new BountySave
            {
                id = ++character.nextBountyId,
                name = FirstNames[Random.Range(0, FirstNames.Length)] + " " + Epithets[Random.Range(0, Epithets.Length)],
                level = level, style = (int)style,
                gender = Random.Range(0, 2), hair = Random.Range(0, 16), body = Random.Range(0, 3), eyebrow = Random.Range(0, 4),
                hairR = Random.Range(20, 200), hairG = Random.Range(10, 150), hairB = Random.Range(5, 100),
                scene = spot.scene, x = pos.x, y = pos.y, z = pos.z,
                murders = Random.Range(1, 3 + level / 4),
                createdAt = UnixNow,
            };
            RollTemperament(b);
            b.gear = RollBountyGear(style, level);
            b.silver = CombatRules.BountySilver(level, cl);
            string among = spot.creatures.Count > 0 ? string.Join(" and ", spot.creatures.ConvertAll(Plural).ToArray()) : "creatures";
            string levels = spot.minLevel == spot.maxLevel ? "level " + spot.minLevel : $"levels {spot.minLevel}-{spot.maxLevel}";
            b.lastSeen = $"{spot.zone}, about {Mathf.Max(10, spot.distance)} m {spot.direction} of {spot.landmark}, among {among} ({levels})";
            return b;
        }

        private static string Plural(string name)
        {
            if (name.EndsWith("f")) return name.Substring(0, name.Length - 1) + "ves";       // Wolf -> Wolves
            if (name.EndsWith("s") || name.EndsWith("x") || name.EndsWith("ch")) return name + "es";
            if (name.EndsWith("y") && !"aeiou".Contains(name[name.Length - 2].ToString())) return name.Substring(0, name.Length - 1) + "ies";
            return name + "s";
        }

        private static Vector3 SpotPos(BountySpot s) => new Vector3(s.pos[0], s.pos[1], s.pos[2]);

        /// <summary>
        /// The outlaw's gear: for each slot the best item of its kind at or below its level, rolled like loot (a grade
        /// around the item's base grade, stats within their ranges). Plate for the sword styles, leather for the
        /// shooters, cloth for the mage.
        /// </summary>
        private List<ItemSave> RollBountyGear(BountyStyle style, int level)
        {
            var gear = new List<ItemSave>();
            Func<ItemTemplate, bool> armour;
            switch (style)
            {
                case BountyStyle.Swordsman:
                case BountyStyle.Brute:
                    armour = t => t.flags.Contains("isHeavyArmor");
                    break;
                case BountyStyle.FireMage:
                    armour = t => t.flags.Contains("isLightArmor") && t.stats.Exists(s => s.key == "Mana");
                    break;
                default:
                    armour = t => t.flags.Contains("isLightArmor") && !t.stats.Exists(s => s.key == "Mana");
                    break;
            }
            foreach (string slot in new[] { "Head", "Body", "Legs", "Feet" })
                AddBest(gear, level, t => t.slot == slot && armour(t));
            switch (style)
            {
                case BountyStyle.Swordsman:
                    AddBest(gear, level, t => t.flags.Contains("isBroadsword"));
                    AddBest(gear, level, t => t.flags.Contains("isShield"));
                    break;
                case BountyStyle.Brute:
                    AddBest(gear, level, t => t.flags.Contains("isGreatsword") || t.flags.Contains("isHalberd"));
                    break;
                case BountyStyle.Archer:
                    AddBest(gear, level, t => t.flags.Contains("isBow"));
                    break;
                case BountyStyle.Crossbowman:
                    AddBest(gear, level, t => t.flags.Contains("isCrossBow"));
                    break;
                case BountyStyle.FireMage:
                    AddBest(gear, level, t => t.flags.Contains("isFireStaff"));
                    break;
            }
            return gear;
        }

        private static HashSet<int> craftable;

        /// <summary>
        /// Gear a crafter makes: the regular tiers, whose Level is honest. Other items lie (the Pirate shirt says
        /// level 1 next to level="40") or don't belong on an outlaw (pet clothes, named uniques).
        /// </summary>
        private static bool Craftable(int typeId)
        {
            if (craftable == null)
            {
                craftable = new HashSet<int>();
                foreach (var r in SkillData.Recipes.Values) craftable.Add(r.product);
            }
            return craftable.Contains(typeId);
        }

        private void AddBest(List<ItemSave> gear, int level, Func<ItemTemplate, bool> match)
        {
            ItemTemplate best = null;
            var ties = new List<ItemTemplate>();
            foreach (var t in ItemData.Templates.Values)
            {
                if (t.slot == null || t.level > level || !match(t) || !Craftable(t.typeId) || t.flags.Contains("untradeable")
                    || (t.name != null && (t.name.Contains("'s ") || t.name.ToLowerInvariant().Contains("pet ")))) continue;
                if (best == null || t.level > best.level) { best = t; ties.Clear(); ties.Add(t); }
                else if (t.level == best.level) ties.Add(t);
            }
            if (best == null) return;
            var pick = ties[Random.Range(0, ties.Count)];
            var it = NewItem(pick, 1);
            it.equipped = true;
            gear.Add(it);
        }

        // ---- the outlaw in the world ---------------------------------------------------------

        /// <summary>Scene loaded (or the board changed): the outlaws waiting in this scene become NPCs.</summary>
        private void SyncBountyActors()
        {
            if (world == null || character == null) return;
            foreach (var kv in new List<KeyValuePair<int, BountyActor>>(bountyByUid))
                if (!character.bounties.Contains(kv.Value.save) && kv.Value.removeAt < 0f)
                    RemoveBountyActor(kv.Value);
            foreach (var b in character.bounties)
            {
                if (b.scene != world.sceneId || !b.started) continue;   // nobody waits until the bounty is taken
                int uid = BountyUidBase + b.id;
                if (bountyByUid.ContainsKey(uid)) continue;
                var info = BountyInfo(b);
                GameData.Npcs[info.id] = info;
                var spot = BountySpots.Find(s => s.scene == b.scene && Vector3.Distance(SpotPos(s), new Vector3(b.x, b.y, b.z)) < 12f);
                var pos = OutlawStandingPlace(new Vector3(b.x, b.y, b.z), spot != null ? SpotPos(spot) : new Vector3(b.x, b.y, b.z));
                b.x = pos.x; b.y = pos.y; b.z = pos.z;
                var npc = new NpcEntity { uid = uid, typeId = info.id, spawnPos = pos, pos = pos, health = info.health, maxHealth = info.health, ownRespawn = true };
                world.npcs.Add(npc);
                var weapon = b.gear.Find(i => ItemData.Get(i.typeId)?.slot == "Weapon");
                bountyByUid[uid] = new BountyActor
                {
                    save = b, npc = npc, lastHp = npc.health,
                    attackAnim = weapon != null && Scr_PlayerHandler.instance != null ? Scr_PlayerHandler.instance.getAnimationdIdFromAttackId(0, weapon.typeId, null, null) : 2,
                    ranged = info.projectile > 0,
                };
                Plugin.Log.LogInfo($"[bounty] {b.name} waits at {SceneWorld.Vec(pos)}: {info.health} health, hits up to {info.damage} {DamageKinds.Name(info.damageType)}, block {BountyBlock(b)}");
            }
            lastBountyList = null;
        }

        private void RemoveBountyActor(BountyActor a)
        {
            bountyByUid.Remove(a.npc.uid);
            world?.npcs.Remove(a.npc);
            GameData.Npcs.Remove(a.npc.typeId);
            if (targetUid == a.npc.uid) StopAttacking();
            lastBountyList = null;
        }

        private static int BountyGearStat(BountySave b, string key)
        {
            int sum = 0;
            foreach (var it in b.gear)
                foreach (var s in it.stats)
                    if (s.key == key) sum += Graded(it, s.value);
            return sum;
        }

        private static int BountyBlock(BountySave b) => BountyGearStat(b, "Damageblock");

        /// <summary>
        /// The outlaw's numbers, from its gear by the player's formulas: max hit (gear damage plus level, times the
        /// weapon's grade), resistances through the soft cap, health on top of a base for its level.
        /// </summary>
        private NpcInfo BountyInfo(BountySave b)
        {
            var weapon = b.gear.Find(i => ItemData.Get(i.typeId)?.slot == "Weapon");
            int gearDamage = 0;
            foreach (var it in b.gear)
                foreach (var s in it.stats)
                    if (s.key.EndsWith("Damage") || s.key == "DmgPoison") gearDamage += Graded(it, s.value);
            int maxHit = Mathf.RoundToInt(CombatRules.PlayerMaxHit(gearDamage, b.level) * (weapon != null ? WeaponGradeMultiplier(weapon.grade) : 1f));
            int projectile = WeaponProjectile(weapon);
            var info = new NpcInfo
            {
                id = BountyTypeBase + b.id, name = b.name, level = b.level,
                health = CombatRules.BountyHealth(b.level) + BountyGearStat(b, "Health"),
                canFight = true, aggressive = true, aggroDistance = CombatRules.BountyAggroDistance,
                attackSpeed = WeaponSpeed(weapon), attackDelay = 0.5f, attackMissDistance = 2f,
                attackDistance = projectile > 0 ? 3f : Mathf.Max(2.5f, WeaponRange(weapon) - 1f),
                damage = Mathf.Max(1, maxHit), damageType = WeaponKind(weapon), accuracy = CombatRules.BountyAccuracy,
                damageBlock = 0,   // the player-style block in RollPlayerHit (BountyBlockedHit)
                resistance = new int[DamageKinds.Count],
                projectile = projectile, projectileRate = projectile > 0 ? 100 : 0,
                projectileDistance = projectile > 0 ? CombatRules.BountyShootDistance : 0f,
            };
            for (int k = 1; k < DamageKinds.Count; k++)
                info.resistance[k] = Mathf.RoundToInt(CombatRules.EffectiveResistance(BountyGearStat(b, DamageKinds.ResistanceStat(k))));
            return info;
        }

        /// <summary>The player's blow on an outlaw: its damage block works as yours does against creatures.</summary>
        private int BountyBlockedHit(NpcEntity npc, float damage)
        {
            if (!bountyByUid.TryGetValue(npc.uid, out var a)) return -1;
            int block = BountyBlock(a.save);
            damage *= 1f - CombatRules.BlockReduction(block, CombatLevel());
            return Mathf.Max(1, Mathf.RoundToInt(damage - CombatRules.FlatBlock(block)));
        }

        /// <summary>
        /// Shooters and casters keep their distance: too close, they back off (then stand their ground a while before
        /// backing off again, or a melee player could never catch them). True while backing off.
        /// </summary>
        private bool BountyKites(NpcEntity npc, Vector3 pos, Vector3 playerPos)
        {
            if (!bountyByUid.TryGetValue(npc.uid, out var a) || !a.ranged) return false;
            if (npc.hasDest && Time.time < a.nextKiteAt) return true;   // still backing off
            float keep = a.save.kiteDistance > 0f ? a.save.kiteDistance : 7f;
            if (Time.time < a.nextKiteAt || Time.time < npc.rootedUntil || Vector3.Distance(pos, playerPos) >= keep) return false;
            a.nextKiteAt = Time.time + CombatRules.BountyKiteCooldown;
            Vector3 away = pos - playerPos;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            MoveNpc(npc, pos + away.normalized * (keep + 2f));
            return true;
        }

        /// <summary>An outlaw ability: a blow this much stronger, maybe with its own projectile and an effect on you.</summary>
        private class OutlawAbility
        {
            public string name;
            public float power;
            public int projectile, gfx;

            public OutlawAbility(string name, float power, int projectile = 0, int gfx = 0)
            {
                this.name = name; this.power = power; this.projectile = projectile; this.gfx = gfx;
            }
        }

        // Per style, after the player abilities they're named for (projectiles from XMLs/Projectiles).
        private static readonly OutlawAbility[][] StyleAbilities =
        {
            new[] { new OutlawAbility("Brutal slash", 1.8f, gfx: 18), new OutlawAbility("Lunge", 1.5f), new OutlawAbility("Cripple", 1.4f) },   // swordsman
            new[] { new OutlawAbility("Crushing blow", 2f, gfx: 18), new OutlawAbility("Whirlwind", 1.6f), new OutlawAbility("Sunder", 1.7f) },  // two-hander
            new[] { new OutlawAbility("Piercing shot", 1.8f, 20), new OutlawAbility("Flame arrow", 1.6f, 11), new OutlawAbility("Venom arrow", 1.5f, 46) },   // archer
            new[] { new OutlawAbility("Wounding shot", 1.8f, 56), new OutlawAbility("Explosive bolt", 1.9f, 10), new OutlawAbility("Piercing shot", 1.7f, 20) },   // crossbowman
            new[] { new OutlawAbility("Fire ball", 2f, 1), new OutlawAbility("Flame lance", 1.8f, 27), new OutlawAbility("Fire arrows", 1.5f, 41) },   // fire mage
        };

        /// <summary>
        /// Each outlaw fights its own way: how often it reaches for an ability, and (shooters) how close it lets you
        /// come before backing off.
        /// </summary>
        private static void RollTemperament(BountySave b)
        {
            b.abilityChance = Random.Range(CombatRules.BountyAbilityChanceMin, CombatRules.BountyAbilityChanceMax + 1);
            b.kiteDistance = Random.Range(CombatRules.BountyKiteMin, CombatRules.BountyKiteMax);
        }

        /// <summary>
        /// Before each swing: maybe an ability (its own chance per swing), any of its style's but never the same one
        /// twice running. Sets the swing's power and projectile; the hit uses them (NpcEntity.blowPower).
        /// </summary>
        private void BountyPrepareSwing(NpcEntity npc)
        {
            if (!bountyByUid.TryGetValue(npc.uid, out var a)) return;
            npc.blowPower = 1f;
            npc.blowProjectile = 0;
            int chance = a.save.abilityChance > 0 ? a.save.abilityChance : 25;
            if (++a.swings < 2 || Random.Range(0, 100) >= chance) return;   // never on the opening swing
            var pool = StyleAbilities[Mathf.Clamp(a.save.style, 0, StyleAbilities.Length - 1)];
            int pick = Random.Range(0, pool.Length);
            if (pick == a.lastAbility && pool.Length > 1) pick = (pick + Random.Range(1, pool.Length)) % pool.Length;
            a.lastAbility = pick;
            var ability = pool[pick];
            npc.blowPower = ability.power;
            npc.blowProjectile = a.ranged ? ability.projectile : 0;
            Notice($"{a.save.name} uses {ability.name}!", "orange");
            if (ability.gfx > 0) Later(0.5f, () => PlayPlayerEffect(ability.gfx));
        }

        // ---- 2/8 and the message translation -------------------------------------------------

        /// <summary>The visible "players": outlaws within view (2/8), then their positions (5/2) for new ones.</summary>
        private void SendVisibleBounties(Vector3 playerPos)
        {
            var sb = new StringBuilder();
            var shown = new List<BountyActor>();
            foreach (var a in bountyByUid.Values)
            {
                if (Vector3.Distance(NpcPosition(a.npc), playerPos) > Plugin.NpcViewDistance.Value) continue;
                if (sb.Length > 0) sb.Append('>');
                var b = a.save;
                var equips = new List<string>();
                foreach (var it in b.gear) equips.Add(it.typeId.ToString());
                Vector3 p = NpcPosition(a.npc);
                // name_hp_max_equips_x,y,z_gender_hair_body_eyebrow_r_g_b_size_pvp_criminal_title_bounty_guild_secondary_emblem_bg_colour
                sb.Append(string.Join("_", new[]
                {
                    b.name, a.npc.health.ToString(), a.npc.maxHealth.ToString(), string.Join(",", equips.ToArray()),
                    (int)p.x + "," + (int)p.y + "," + (int)p.z, b.gender.ToString(), b.hair.ToString(), b.body.ToString(), b.eyebrow.ToString(),
                    b.hairR.ToString(), b.hairG.ToString(), b.hairB.ToString(), "100", "0", "0", string.Empty, "0", "{}", string.Empty,
                    BossEmblem.ToString(), BossEmblemBg.ToString(), "0",
                }));
                shown.Add(a);
            }
            string list = sb.ToString();
            if (list == lastBountyList) return;
            bool first = lastBountyList == null;
            lastBountyList = list;
            Send(2, 8, character.name, list);
            foreach (var a in shown)
            {
                Send(5, 2, a.save.name, 0, NpcPosition(a.npc));
                if (a.npc.dead) SendBountyAnim(a, dead: true);
            }
            if (first && shown.Count > 0) Plugin.Log.LogInfo($"[bounty] {shown.Count} outlaw(s) in view");
        }

        // ---- moving like a player ------------------------------------------------------------

        /// <summary>
        /// The player's shape and limits, from the local player's CharacterController (the same rules your own
        /// character walks by): its radius and height, the step it can climb and the steepest slope it can walk.
        /// </summary>
        private static void PlayerShape(out float radius, out float height, out float stepUp, out float slope)
        {
            radius = 0.4f; height = 1.8f; stepUp = 0.4f; slope = 45f;
            var p = Scr_PlayerHandler.instance != null ? Scr_PlayerHandler.instance.player : null;
            var cc = p != null ? p.GetComponent<CharacterController>() : null;
            if (cc == null) return;
            radius = cc.radius; height = cc.height; stepUp = cc.stepOffset; slope = cc.slopeLimit;
        }

        /// <summary>A player-sized body moving from from along dir would hit something it can't step over.</summary>
        private static bool OutlawBlocked(Vector3 from, Vector3 dir, float dist)
        {
            if (NpcObstaclesOff) return false;
            PlayerShape(out float r, out float h, out float stepUp, out _);
            Vector3 bottom = from + Vector3.up * (stepUp + r + 0.05f), top = from + Vector3.up * Mathf.Max(h - r, stepUp + r + 0.1f);
            foreach (var hit in Physics.CapsuleCastAll(bottom, top, r, dir, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (hit.distance > 0f && hit.normal.y < 0.5f && !IsCreature(hit.collider)) return true;
            return false;
        }

        /// <summary>A player could stand at to, coming from from: nothing through its body, and no steeper climb than its slope limit.</summary>
        private static bool OutlawCanStand(Vector3 from, Vector3 to, float step)
        {
            if (NpcObstaclesOff) return true;
            PlayerShape(out float r, out float h, out float stepUp, out float slope);
            float rise = to.y - from.y;
            if (rise > stepUp && rise > step * Mathf.Tan(slope * Mathf.Deg2Rad)) return false;   // too steep to walk up
            return !OutlawInside(to, r, h, stepUp);
        }

        private static bool OutlawInside(Vector3 at, float r, float h, float stepUp)
        {
            Vector3 bottom = at + Vector3.up * (stepUp + r + 0.05f), top = at + Vector3.up * Mathf.Max(h - r, stepUp + r + 0.1f);
            foreach (var c in Physics.OverlapCapsule(bottom, top, r, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!IsCreature(c)) return true;
            return false;
        }

        /// <summary>
        /// Where an outlaw can wait: its spot on the ground with room for a player, else nearby, else the creature
        /// marker the spot was built from (a place creatures stand).
        /// </summary>
        private static Vector3 OutlawStandingPlace(Vector3 wanted, Vector3 spot)
        {
            PlayerShape(out float r, out float h, out float stepUp, out _);
            var tries = new List<Vector3> { wanted };
            for (int i = 0; i < 12; i++)
            {
                Vector2 o = Random.insideUnitCircle * 5f;
                tries.Add(spot + new Vector3(o.x, 0f, o.y));
            }
            foreach (var t in tries)
                if (GroundAt(t, Mathf.Max(t.y, spot.y) + 6f, 20f, out var g) && !OutlawInside(g, r, h, stepUp)) return g;
            return spot;
        }

        /// <summary>Once a second: an outlaw that ended up inside something goes back to where it last stood clear.</summary>
        private void UnstickOutlaws()
        {
            PlayerShape(out float r, out float h, out float stepUp, out _);
            foreach (var a in bountyByUid.Values)
            {
                if (a.npc.dead) continue;
                Vector3 p = NpcPosition(a.npc);
                if (!OutlawInside(p, r, h, stepUp)) { a.lastClear = p; continue; }
                if (a.lastClear == Vector3.zero) continue;
                Plugin.Log.LogInfo($"[bounty] {a.save.name} stuck at {SceneWorld.Vec(p)}: back to {SceneWorld.Vec(a.lastClear)}");
                a.npc.hasDest = false;
                a.npc.pos = a.lastClear;
                Send(5, 2, a.save.name, 0, a.lastClear);
            }
        }

        /// <summary>The client's other-player object for an outlaw, the truth for its position (null if not drawn).</summary>
        private Transform BountyTransform(NpcEntity npc)
        {
            if (!bountyByUid.TryGetValue(npc.uid, out var a) || Scr_PlayerHandler.instance == null) return null;
            var op = Scr_PlayerHandler.instance.getOtherPlayer(a.save.name);
            return op != null ? op.transform : null;
        }

        private bool BountyUid(object o, out BountyActor a)
        {
            a = null;
            return o is int uid && bountyByUid.TryGetValue(uid, out a);
        }

        /// <summary>
        /// Send() hook: NPC messages about an outlaw become the player messages the client draws it with. True if the
        /// message was about one (and was translated or dropped).
        /// </summary>
        private bool TranslateBountySend(byte ev, object[] d)
        {
            if (bountyByUid.Count == 0 || d == null || d.Length < 2 || !(d[0] is int sub)) return false;
            BountyActor a;
            switch (ev)
            {
                case 198:
                    if (!BountyUid(d[1], out a)) return false;
                    if (sub == 25 && d.Length > 3) Send(5, 2, a.save.name, RotTowards(NpcPosition(a.npc), (Vector3)d[3], a), (Vector3)d[3]);   // walk to
                    else if (sub == 28 && d.Length > 2) Send(5, 2, a.save.name, RotTowards(NpcPosition(a.npc), (Vector3)d[2], a), NpcPosition(a.npc));   // turn to
                    else if (sub == 26 && d.Length > 2 && d[2] is int anim)
                    {
                        // The creature AI's attack and death animations: the outlaw's swing and fall.
                        if (anim == NpcAnims.Attack) SendBountyAnim(a, attack: true);
                        else if (anim == NpcAnims.Death) SendBountyAnim(a, dead: true);
                    }
                    return true;
                case 11:
                    if (sub != 0 || !BountyUid(d[1], out a)) return false;
                    a.combat = d.Length > 2 && d[2] is bool on && on;
                    SendBountyAnim(a);
                    if (!a.combat) Send(7, 2, a.save.name);
                    return true;
                case 12:
                    if (sub != 0 || !BountyUid(d[1], out a)) return false;
                    int hp = (int)d[2], max = (int)d[3], splash = d.Length > 4 ? (int)d[4] : 0;
                    if (hp < a.lastHp) Send(8, 2, a.save.name, hp - a.lastHp, splash);
                    else if (hp == a.lastHp && splash == SplashBlocked) Send(8, 2, a.save.name, 0, splash);
                    a.lastHp = hp;
                    Send(8, 13, a.save.name, hp, max);
                    return true;
                case 13:
                    if (!BountyUid(d[1], out a)) return false;
                    if (sub == 0) Send(1, 2, a.save.name, d[2] is bool dead && dead);
                    return true;   // 13/2 (walking) and the rest: nothing for a player
                case 9:
                    return sub == 1 && BountyUid(d[1], out a);   // respawn: never
                case 16:
                    if (sub != 2 || !BountyUid(d[1], out a)) return false;
                    Send(5, 1, a.save.name, (int)d[2], NpcPosition(a.npc));
                    return true;
                case 20:
                    return sub == 0 && BountyUid(d[1], out a);   // NPC buff icons
                case 8:
                    if ((sub == 3 || sub == 10) && d.Length > 3 && BountyUid(d[3], out a))
                    {
                        // 8/3 the player shoots it, 8/10 it shoots the player: 18/0 (owner, target, projectile)
                        if (sub == 3) Send(18, 0, character.name, a.save.name, (int)d[2]);
                        else Send(18, 0, a.save.name, character.name, (int)d[2]);
                        return true;
                    }
                    return false;
                case 3:
                    if (sub == 19 && d.Length > 2 && BountyUid(d[2], out a))
                    {
                        Send(2, 23, character.name, a.save.name);   // 3/19 with an NPC uid would clear the player target
                        return true;
                    }
                    return false;
            }
            return false;
        }

        private static int RotTowards(Vector3 from, Vector3 to, BountyActor a)
        {
            Vector3 d = to - from;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f) return 0;
            return Mathf.RoundToInt(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
        }

        /// <summary>7/22: the outlaw's pose, as the client's own animation packet (Scr_Animator.AnimationPacket).</summary>
        private void SendBountyAnim(BountyActor a, bool dead = false, bool attack = false)
        {
            bool combat = a.combat && !dead;
            string json = JsonUtility.ToJson(new Scr_Animator.AnimationPacket
            {
                b1 = combat, b3 = dead, f6 = combat ? 1f : 0f, f10 = combat ? 1f : 0f, f8 = 1f,
                i4 = attack ? a.attackAnim : combat ? a.attackAnim : -1,
            });
            Send(7, 22, a.save.name + "," + json);
            if (attack) Send(3, 20, a.save.name, a.attackAnim);
        }

        /// <summary>2/9: the player clicked an outlaw (or nobody).</summary>
        private void SelectBountyTarget(string name)
        {
            if (character == null) return;
            foreach (var a in bountyByUid.Values)
                if (a.save.name == name && !a.npc.dead)
                {
                    Plugin.Log.LogInfo($"[bounty] targeting {name}");
                    SelectTarget(a.npc.uid);
                    return;
                }
            if (string.IsNullOrEmpty(name) && bountyByUid.ContainsKey(targetUid)) SelectTarget(0);
        }

        // ---- the kill ------------------------------------------------------------------------

        /// <summary>
        /// From DropLoot: the outlaw's bounty is paid, some of its gear (the very items) and a Small experience scroll
        /// fall into a bag, a window says so, and it's off the board. True if it was an outlaw.
        /// </summary>
        private bool BountyKilled(NpcEntity npc)
        {
            if (!bountyByUid.TryGetValue(npc.uid, out var a)) return false;
            var b = a.save;
            character.bounties.Remove(b);
            a.removeAt = Time.time + BountyBodySeconds;
            SendBountyAnim(a, dead: true);
            SetSilver(character.silver + b.silver);

            var drop = new List<ItemSave>();
            foreach (var it in b.gear)
                if (Random.value < CombatRules.BountyGearDropChance) drop.Add(it);
            if (drop.Count == 0 && b.gear.Count > 0) drop.Add(b.gear[Random.Range(0, b.gear.Count)]);
            foreach (var it in drop) it.equipped = false;
            var scroll = NewItem(ItemData.Get(ItemSmallXpScroll), 1);
            drop.Add(scroll);
            var bag = new LootBag { id = nextLootId++, scene = character.scene, pos = NpcPosition(npc), despawnAt = Time.time + CombatRules.BountyLootSeconds, kept = drop };
            foreach (var it in drop) bag.items.Add(new ItemAmount { type = it.typeId, amount = it.amount });
            SceneLoot(bag.scene).Add(bag);
            Send(198, 34, LootEntry(bag));

            Send(7, 43, $"Bounty complete!\n\n{b.name} is dead. You collect the bounty of {b.silver} silver.\n\n{b.name} dropped {LootLabel(bag)}.");
            Notice($"Bounty complete: {b.name}. +{b.silver} silver", "green");
            Plugin.Log.LogInfo($"[bounty] {b.name} killed: {b.silver} silver, dropped {LootLabel(bag)}");
            SaveCurrentCharacter();
            return true;
        }

        /// <summary>Once a second: board upkeep, and bodies of dead outlaws leave.</summary>
        private void TickBounties()
        {
            if (character == null || !sceneReady) return;
            foreach (var a in new List<BountyActor>(bountyByUid.Values))
                if (a.removeAt > 0f && Time.time >= a.removeAt) RemoveBountyActor(a);
            unstickTimer -= Time.deltaTime;
            if (unstickTimer <= 0f)
            {
                unstickTimer = 1f;
                UnstickOutlaws();
            }
            bountyTickTimer -= Time.deltaTime;
            if (bountyTickTimer > 0f) return;
            bountyTickTimer = 60f;
            RefreshBounties();
        }

        /// <summary>Dev: each outlaw's position and whether a player-sized body there overlaps anything (bridge command outlaws).</summary>
        internal void DevOutlaws()
        {
            PlayerShape(out float r, out float h, out float stepUp, out float slope);
            Plugin.Log.LogInfo($"[dev] player shape: radius {r}, height {h}, step {stepUp}, slope {slope}");
            foreach (var a in bountyByUid.Values)
            {
                Vector3 p = NpcPosition(a.npc);
                Plugin.Log.LogInfo($"[dev] {a.save.name} at {SceneWorld.Vec(p)} inside {OutlawInside(p, r, h, stepUp)}, " +
                    $"2 m under {OutlawInside(p + Vector3.down * 2f, r, h, stepUp)}, 3 m up {OutlawInside(p + Vector3.up * 3f, r, h, stepUp)}, hp {a.npc.health}/{a.npc.maxHealth}");
            }
        }

        /// <summary>Dev: a fresh board (bridge command bountyreset).</summary>
        internal void DevBountyReset()
        {
            character.bounties.Clear();
            character.bountyRefreshAt = 0;
            RefreshBounties();
        }

        /// <summary>Dev: an outlaw of a style (0-4) and level waiting a few metres away (bridge command bountyhere).</summary>
        internal void DevBountyHere(int style, int level)
        {
            Vector3 me = PlayerPos;
            var b = new BountySave
            {
                id = ++character.nextBountyId, name = FirstNames[Random.Range(0, FirstNames.Length)] + " " + Epithets[Random.Range(0, Epithets.Length)],
                level = level, style = style, gender = Random.Range(0, 2), hair = Random.Range(0, 16), hairR = 120, hairG = 80, hairB = 40,
                scene = character.scene, x = me.x + 8f, y = me.y, z = me.z, murders = 1, createdAt = UnixNow, lastSeen = "right here", started = true,
            };
            RollTemperament(b);
            b.gear = RollBountyGear((BountyStyle)style, level);
            b.silver = CombatRules.BountySilver(level, CombatLevel());
            character.bounties.Add(b);
            SyncBountyActors();
        }

        /// <summary>Zone change: the outlaws of the old scene went with it.</summary>
        private void ResetBounties()
        {
            foreach (var a in bountyByUid.Values) GameData.Npcs.Remove(a.npc.typeId);
            bountyByUid.Clear();
            lastBountyList = null;
        }
    }
}
