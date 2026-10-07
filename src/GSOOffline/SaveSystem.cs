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
        public int health = 100;
        public int mana = 100;
        public int homeWayshrine = 1;
        public List<int> wayshrines = new List<int> { 1 };
        public List<SkillXp> skills = new List<SkillXp>();

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

        private static T Load<T>(string dir, string name) where T : class
        {
            string path = Path.Combine(dir, FileKey(name) + ".json");
            if (!File.Exists(path)) return null;
            try
            {
                return JsonUtility.FromJson<T>(File.ReadAllText(path));
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
            File.WriteAllText(tmp, JsonUtility.ToJson(obj, true));
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
