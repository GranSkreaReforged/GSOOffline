using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using UnityEngine;

namespace GSOOffline
{
    public class WayshrineInfo
    {
        public int id;
        public string name;
        public int scene;
        public Vector3 pos;
    }

    public class NpcInfo
    {
        public int id;
        public string name;
        public int level;
        public int health;
        public bool canFight;
        public int respawnTime;
        public bool aggressive;
        public float aggroDistance;
        public float attackDistance;
        public float attackSpeed;      // seconds between swings
        public int damage;             // explicit damage (rare); 0 = derive from level
        public int defence;
        public int damageBlock;
        public int damageType;
        public bool wandering;
        public float wanderFrequency;  // seconds between strolls
        public float wanderDistance;
        public bool dropLootAtPlayer;  // loot bag at the killer's feet (NPCs that die out of reach, e.g. in water)
        // Ranged attacks: projectile id (XMLs/Projectiles), the share of attacks that use it (percent), and its reach.
        public int projectile, projectileRate;
        public float projectileDistance;
        public int[] sfxAttack = new int[0], sfxTakeHit = new int[0], sfxDeath = new int[0];
        // Ids into the client's shared animation table (EasyAnimationHandler); defaults are the client's own.
        public int animIdle = NpcAnims.Idle, animRun = NpcAnims.Run, animAttack = NpcAnims.Attack, animDeath = NpcAnims.Death;
    }

    /// <summary>
    /// NPC animation ids. The client plays only what the server sends (2/2 spawn list, 198/26), and id 0 is an
    /// empty clip that leaves models in their bind pose: humans sunk to the waist, animals stretched to hundreds of metres.
    /// </summary>
    internal static class NpcAnims
    {
        public const int Idle = 28, Run = 29, Attack = 39, Death = 30;

        public static int IdleOf(int typeId) => GameData.Npcs.TryGetValue(typeId, out var i) ? i.animIdle : Idle;
    }

    /// <summary>Server-side view of the XML data files the client ships in Resources/XMLs.</summary>
    internal static class GameData
    {
        public static readonly Dictionary<int, WayshrineInfo> Wayshrines = new Dictionary<int, WayshrineInfo>();
        public static readonly Dictionary<int, NpcInfo> Npcs = new Dictionary<int, NpcInfo>();

        private static bool loaded;

        public static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                LoadWayshrines();
                LoadNpcs();
                ItemData.Load();
                DialogueData.Load();
                SkillData.Load();
                Plugin.Log.LogInfo($"Game data: {Wayshrines.Count} wayshrines, {Npcs.Count} NPC types, {ItemData.Templates.Count} items.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Loading game data failed: " + e);
            }
        }

        internal static XmlDocument LoadXml(string resource)
        {
            var ta = Resources.Load(resource) as TextAsset;
            if (ta == null) throw new Exception("Missing resource " + resource);
            var doc = new XmlDocument();
            doc.LoadXml(ta.text);
            return doc;
        }

        internal static string Attr(XmlNode n, string name) => n.Attributes?[name]?.Value;

        internal static int IntAttr(XmlNode n, string name, int def = 0)
        {
            string v = Attr(n, name);
            return v != null && int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : def;
        }

        /// <summary>"334,335,336" -> [334, 335, 336] (sound lists pick one at random).</summary>
        internal static int[] IntListAttr(XmlNode n, string name)
        {
            var list = new List<int>();
            foreach (string part in (Attr(n, name) ?? string.Empty).Split(','))
                if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) && r != 0) list.Add(r);
            return list.ToArray();
        }

        public static Vector3 ParseVec(string s)
        {
            string[] p = s.Split(',');
            return new Vector3(
                float.Parse(p[0], CultureInfo.InvariantCulture),
                float.Parse(p[1], CultureInfo.InvariantCulture),
                float.Parse(p[2], CultureInfo.InvariantCulture));
        }

        private static void LoadWayshrines()
        {
            foreach (XmlNode n in LoadXml("XMLs/Wayshrines").DocumentElement.ChildNodes)
            {
                if (n.NodeType != XmlNodeType.Element) continue;
                var w = new WayshrineInfo
                {
                    id = IntAttr(n, "id"),
                    name = Attr(n, "name"),
                    scene = IntAttr(n, "scene"),
                    pos = ParseVec(Attr(n, "pos")),
                };
                Wayshrines[w.id] = w;
            }
        }

        private static void LoadNpcs()
        {
            foreach (XmlNode n in LoadXml("XMLs/NPCInfo").DocumentElement.ChildNodes)
            {
                if (n.NodeType != XmlNodeType.Element) continue;
                int level = IntAttr(n, "level", 1);
                var info = new NpcInfo
                {
                    id = IntAttr(n, "id"),
                    name = Attr(n, "name"),
                    level = level,
                    // Only a couple of entries carry explicit health; the rest was a server-side formula.
                    health = IntAttr(n, "health", CombatRules.NpcHealth(level)),
                    canFight = IntAttr(n, "canfight") == 1,
                    respawnTime = IntAttr(n, "respawntime", 60),
                    aggressive = IntAttr(n, "aggressive") == 1,
                    aggroDistance = IntAttr(n, "aggrodistance", 10),
                    attackDistance = Mathf.Max(2.5f, IntAttr(n, "attackdistance", 3)),
                    attackSpeed = IntAttr(n, "attackspeed", 3000) / 1000f,
                    damage = IntAttr(n, "damage"),
                    defence = IntAttr(n, "defence"),
                    damageBlock = IntAttr(n, "damageblock"),
                    damageType = IntAttr(n, "damagetype"),
                    wandering = IntAttr(n, "wandering") == 1,
                    wanderFrequency = Mathf.Max(2, IntAttr(n, "wanderingfrequency", 24)),
                    wanderDistance = IntAttr(n, "wanderingdistance", 10),
                    dropLootAtPlayer = IntAttr(n, "droplootatplayer") == 1,
                    projectile = IntAttr(n, "projectile"),
                    projectileRate = IntAttr(n, "projectilerate"),
                    projectileDistance = IntAttr(n, "projectileattackdistance"),
                    sfxAttack = IntListAttr(n, "sfxattack"),
                    sfxTakeHit = IntListAttr(n, "sfxtakehit"),
                    sfxDeath = IntListAttr(n, "sfxdeath"),
                    animIdle = IntAttr(n, "anim_idle", NpcAnims.Idle),
                    animRun = IntAttr(n, "anim_run", NpcAnims.Run),
                    animAttack = IntAttr(n, "anim_attack", NpcAnims.Attack),
                    animDeath = IntAttr(n, "anim_death", NpcAnims.Death),
                };
                Npcs[info.id] = info;
            }
        }
    }
}
