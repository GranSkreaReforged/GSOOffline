using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GSOOffline
{
    [Serializable]
    public class ItemAmount
    {
        public int type;
        public int amount;

        public int Amount => amount > 0 ? amount : 1;
    }

    /// <summary>
    /// A condition on quest progress shared by entries, NPC rules and triggers: quests that must be complete
    /// ("after") and a quest variable that must hold a value ("var": "quest,var,value").
    /// </summary>
    [Serializable]
    public class QuestCondition
    {
        public List<int> after = new List<int>();
        public string var;

        public bool Holds(CharacterSave ch, Func<int, int, int> getVar)
        {
            if (after != null)
                foreach (int q in after)
                    if (ch.GetQuestPhase(q) != -1) return false;
            var v = Content.ParseInts(var);
            return v == null || v.Length < 3 || getVar(v[0], v[1]) == v[2];
        }
    }

    [Serializable]
    public class EntryDef : QuestCondition
    {
        public int node;
        public string npc;
        public int npcType;        // only this copy of the NPC (0 = any NPC with the name)
        public int quest;          // 0 = derive from the dialogue graph
        public int phase;
        public bool always;        // unconditional greeting
        public List<ItemAmount> requires = new List<ItemAmount>();
    }

    /// <summary>
    /// Which copy of a quest NPC exists at which point of a quest. The original server showed each copy
    /// (Alden at the dinner table, Alden wounded in the tunnel...) only in its own phases. With "spawn" the NPC
    /// has no marker in the scene and is created at that position (ambushers, summoned ghosts).
    /// </summary>
    [Serializable]
    public class NpcRule : QuestCondition
    {
        public int type;
        public string note;
        public int quest;            // 0 = only "after"/"var" decide
        public List<int> phases = new List<int>();   // shown while the quest is in one of these phases
        public bool hidden;          // never shown (copies no quest uses)
        public int scene;            // spawn: scene id
        public List<string> spawn = new List<string>();   // spawn: positions "x,y,z"
    }

    [Serializable]
    public class ActionDef
    {
        public int id;
        public string note;
        public List<ItemAmount> give = new List<ItemAmount>();
        public List<ItemAmount> take = new List<ItemAmount>();
        public bool onlyIfMissing;
        public int silver;
        public int scene;
        public string pos;
        public int window;         // open a client window (14 = bank)
        public string setQuest;    // "quest,phase"
        public string setVar;      // "quest,var,value"
        public int dialogue;       // open this dialogue node...
        public int dialogueNpc;    // ...with the nearest NPC of this type (the conversation's speaker)
        public string notice;      // a chat line for the player
    }

    /// <summary>
    /// Advances a quest on a game event while it is at <see cref="phase"/>. "item": the inventory holds
    /// <see cref="amount"/> of the item; "kill": an NPC of type <see cref="npc"/> dies; "heal": a bandage is used;
    /// "enter": the player comes within <see cref="radius"/> of <see cref="pos"/> in <see cref="scene"/>;
    /// "say": the player says <see cref="text"/> in chat; "use": the player uses or drops the item.
    /// "enter", "say" and "use" can also be limited to an area with scene/pos/radius.
    /// </summary>
    [Serializable]
    public class TriggerDef : QuestCondition
    {
        public string on;
        public string note;
        public int quest;
        public int phase;
        public int item;
        public int amount;
        public int npc;
        public int setPhase;       // 0 = leave the phase as it is
        public int action;         // also run this content action
        public int chance = 100;   // percent, for kills that only sometimes drop a quest item
        public bool consume;       // "use": the item is used up
        public int scene;
        public string pos;
        public float radius = 10f;
        public string text;
    }

    [Serializable]
    public class ContentFile
    {
        public List<EntryDef> entries = new List<EntryDef>();
        public List<ActionDef> actions = new List<ActionDef>();
        public List<TriggerDef> triggers = new List<TriggerDef>();
        public List<NpcRule> npcs = new List<NpcRule>();
    }

    /// <summary>
    /// Hand-authored server content (Data/content.json, embedded). A content.json placed next to the
    /// plugin DLL replaces the built-in one, so quest fixes can be tried without rebuilding.
    /// </summary>
    internal static class Content
    {
        public static readonly Dictionary<int, ActionDef> Actions = new Dictionary<int, ActionDef>();
        public static readonly List<TriggerDef> Triggers = new List<TriggerDef>();
        public static readonly Dictionary<int, List<NpcRule>> NpcRules = new Dictionary<int, List<NpcRule>>();

        public static int[] ParseInts(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split(',');
            var r = new int[p.Length];
            for (int i = 0; i < p.Length; i++) r[i] = int.Parse(p[i].Trim());
            return r;
        }

        private static string ReadJson()
        {
            string external = Path.Combine(Path.GetDirectoryName(typeof(Content).Assembly.Location), "content.json");
            if (File.Exists(external))
            {
                Plugin.Log.LogInfo("Using content override " + external);
                return File.ReadAllText(external);
            }
            using (var s = typeof(Content).Assembly.GetManifestResourceStream("GSOOffline.content.json"))
            using (var r = new StreamReader(s))
                return r.ReadToEnd();
        }

        public static void Apply()
        {
            ContentFile file;
            try
            {
                file = Json.Deserialize<ContentFile>(ReadJson());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("content.json is invalid: " + e.Message);
                return;
            }

            Plugin.Log.LogInfo($"content.json: {file.entries?.Count ?? -1} entries, {file.actions?.Count ?? -1} actions, {file.triggers?.Count ?? -1} triggers, {file.npcs?.Count ?? -1} NPC rules");
            if (file.entries == null) return;
            foreach (var d in file.entries)
            {
                if (!DialogueData.Nodes.ContainsKey(d.node) || string.IsNullOrEmpty(d.npc))
                {
                    Plugin.Log.LogWarning($"content.json: entry for unknown node {d.node} or missing npc");
                    continue;
                }
                var e = new DialogueEntry { node = d.node, npc = d.npc.Trim().ToLowerInvariant(), npcType = d.npcType, condition = d };
                if (d.always)
                    e.priority = 1;
                else if (d.quest != 0)
                {
                    e.quest = d.quest;
                    e.phase = d.phase;
                    e.priority = 3;
                }
                else
                    DialogueData.DeriveCondition(e);
                if (d.requires != null)
                    foreach (var r in d.requires) e.requiredItems.Add(new[] { r.type, r.Amount });
                DialogueData.AddEntry(e);
            }
            if (file.actions != null)
                foreach (var a in file.actions) Actions[a.id] = a;
            if (file.triggers != null)
                Triggers.AddRange(file.triggers);
            if (file.npcs != null)
                foreach (var n in file.npcs)
                {
                    if (!NpcRules.TryGetValue(n.type, out var list)) NpcRules[n.type] = list = new List<NpcRule>();
                    list.Add(n);
                }
        }
    }
}
