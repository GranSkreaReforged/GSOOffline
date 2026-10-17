using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace GSOOffline
{
    [Serializable]
    public class AccountSave
    {
        public string name;
        public List<string> characters = new List<string>();
    }

    [Serializable]
    public class SkillXp
    {
        public string skill;
        public int xp;
    }

    [Serializable]
    public class ItemStat
    {
        public string key;
        public int value;
    }

    [Serializable]
    public class ItemSave
    {
        public int id;
        public int typeId;
        public int amount = 1;
        public int tab;
        public int grade;
        public bool equipped;
        public List<ItemStat> stats = new List<ItemStat>();
    }

    [Serializable]
    public class AbilitySlotSave
    {
        public int slot;
        public int ability;
    }

    [Serializable]
    public class QuestVar
    {
        public int quest;
        public int var;
        public int value;
    }

    [Serializable]
    public class QuestState
    {
        public int id;
        public int phase;   // 0 not started, -1 completed
    }

    [Serializable]
    public class FurnitureSave
    {
        public int type;
        public int rot;
        public float x, y, z;
    }

    [Serializable]
    public class HouseSave
    {
        public int scene;
        public int plot;               // plot id within the scene (Data/houseplots.json)
        public int type;               // the client's HouseTypes.xml typeid
        public int rotation;
        public string name;
        public bool locked;
        public int extraSpace;         // furniture space bought on top of 5 per metre of plot
        public long builtAt;           // unix seconds: demolishing within the hour gives everything back
        public List<FurnitureSave> furniture = new List<FurnitureSave>();
    }

    [Serializable]
    public class DeathBagSave
    {
        public int scene;
        public float x, y, z;
        public List<ItemSave> items = new List<ItemSave>();   // the very items, grades and stats intact
    }

    [Serializable]
    public class BountySave
    {
        public int id;
        public string name;
        public int level;
        public int style;              // OfflineServer.BountyStyle
        public int gender, hair, body, eyebrow, hairR, hairG, hairB;
        public int scene;
        public float x, y, z;          // where it waits
        public string lastSeen;
        public int silver;
        public int murders;
        public long createdAt;         // unix seconds: an hour later it leaves the board
        public int abilityChance;      // percent per swing it uses an ability (its temperament)
        public float kiteDistance;     // shooters back off when you come closer than this
        public List<ItemSave> gear = new List<ItemSave>();   // worn, rolled like loot; dropped from these on its death
    }

    [Serializable]
    public class KillCount
    {
        public int npc;
        public int count;
    }

    [Serializable]
    public class CharacterSave
    {
        public string name;
        public string account;
        public bool creationDone;

        public int gender;
        public int hairstyle = 15;
        public int hairR = 60, hairG = 40, hairB = 20;
        public int bodyType;
        public int bodySize = 100;
        public int eyebrow;
        public int profession;
        public int fightingStyle;

        public int scene = OfflineServer.StartScene;
        public float x = OfflineServer.StartPos.x, y = OfflineServer.StartPos.y, z = OfflineServer.StartPos.z;
        public int rot;

        public int silver = 100;
        public int bankSilver;
        public int gold;
        public int health = 100;          // base max health, before equipment
        public int currentHealth = 100;
        public int mana = 100;            // base max mana
        public int currentMana = 100;
        public int homeWayshrine = 1;
        public List<int> wayshrines = new List<int> { 1 };
        public List<SkillXp> skills = new List<SkillXp>();

        // Order matches the client's Inventory.items list; swap/insert requests are index based.
        public List<ItemSave> items = new List<ItemSave>();
        public int nextItemId = 1;
        public int secondaryWeaponId;   // weapon carried on the back (item id in items, not equipped); 0 = none

        public List<QuestState> quests = new List<QuestState>();
        public List<QuestVar> questVars = new List<QuestVar>();
        public List<AbilitySlotSave> abilitySlots = new List<AbilitySlotSave>();
        public List<ItemSave> bank = new List<ItemSave>();
        public List<ReturnPoint> returnPoints = new List<ReturnPoint>();   // where interior exits lead back to
        public ItemSave boat;   // boat on the water, out of the bag; null = none (returned to the bag on the next login)
        public int christmasPetCounter;   // Christmas 2019 crates opened without the untradeable Reindeer; -1 = got it
        public int halloweenPetCounter;   // Halloween 2019 crates opened without the untradeable Bat; -1 = got it
        public HouseSave house;           // null = no house
        public List<KillCount> kills = new List<KillCount>();   // per NPC type, for the house trophies
        public int deathStacks;                 // Death's toll: deaths in a row while it lasted (0 = none)
        public float deathTollSeconds;          // playing time left on it
        public List<DeathBagSave> deathBags = new List<DeathBagSave>();   // gear dropped on death, until fetched
        public List<BountySave> bounties = new List<BountySave>();         // the wanted boards' outlaws
        public long bountyRefreshAt;      // unix seconds: the board fills up again then
        public int nextBountyId;

        public int GetKills(int npc)
        {
            foreach (var k in kills)
                if (k.npc == npc) return k.count;
            return 0;
        }

        public void AddKill(int npc)
        {
            foreach (var k in kills)
                if (k.npc == npc) { k.count++; return; }
            kills.Add(new KillCount { npc = npc, count = 1 });
        }

        public int GetAbilitySlot(int slot)
        {
            foreach (var a in abilitySlots)
                if (a.slot == slot) return a.ability;
            return 0;
        }

        public void SetAbilitySlot(int slot, int ability)
        {
            abilitySlots.RemoveAll(a => a.slot == slot);
            if (ability != 0) abilitySlots.Add(new AbilitySlotSave { slot = slot, ability = ability });
        }

        public int GetQuestPhase(int quest)
        {
            foreach (var q in quests)
                if (q.id == quest) return q.phase;
            return 0;
        }

        public void SetQuestPhase(int quest, int phase)
        {
            foreach (var q in quests)
                if (q.id == quest) { q.phase = phase; return; }
            quests.Add(new QuestState { id = quest, phase = phase });
        }

        public Vector3 Position
        {
            get => new Vector3(x, y, z);
            set { x = value.x; y = value.y; z = value.z; }
        }

        public int GetXp(string skill)
        {
            foreach (var s in skills)
                if (s.skill == skill) return s.xp;
            return 0;
        }

        public void SetXp(string skill, int xp)
        {
            foreach (var s in skills)
                if (s.skill == skill) { s.xp = xp; return; }
            skills.Add(new SkillXp { skill = skill, xp = xp });
        }
    }

    internal static class SaveSystem
    {
        private static string AccountDir => Path.Combine(Plugin.SaveDir, "accounts");
        private static string CharacterDir => Path.Combine(Plugin.SaveDir, "characters");

        // Names come straight from client text fields; keep them filesystem-safe and case-insensitive.
        private static string FileKey(string name)
        {
            var sb = new StringBuilder();
            foreach (char ch in name.Trim().ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
            return sb.Length == 0 ? "_" : sb.ToString();
        }

        private static T Load<T>(string dir, string name) where T : class, new()
        {
            string path = Path.Combine(dir, FileKey(name) + ".json");
            if (!File.Exists(path)) return null;
            try
            {
                return Json.Deserialize<T>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Failed to read {path}: {e}");
                return null;
            }
        }

        private static void Store(string dir, string name, object obj)
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, FileKey(name) + ".json");
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(obj));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static AccountSave LoadAccount(string name) => Load<AccountSave>(AccountDir, name);
        public static void SaveAccount(AccountSave a) => Store(AccountDir, a.name, a);

        public static CharacterSave LoadCharacter(string name) => Load<CharacterSave>(CharacterDir, name);
        public static void SaveCharacter(CharacterSave c) => Store(CharacterDir, c.name, c);
        public static bool CharacterExists(string name) => File.Exists(Path.Combine(CharacterDir, FileKey(name) + ".json"));

        public static void DeleteCharacter(string name)
        {
            string path = Path.Combine(CharacterDir, FileKey(name) + ".json");
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
