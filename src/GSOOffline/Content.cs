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

    [Serializable]
    public class EntryDef
    {
        public int node;
        public string npc;
        public int quest;          // 0 = derive from the dialogue graph
        public int phase;
        public bool always;        // unconditional greeting
        public List<ItemAmount> requires = new List<ItemAmount>();
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
    }

    [Serializable]
    public class TriggerDef
    {
        public string on;          // "item" (inventory reaches amount) or "kill" (NPC type killed)
        public int quest;
        public int phase;
        public int item;
        public int amount;
        public int npc;
        public int setPhase;
    }

    [Serializable]
    public class ContentFile
    {
        public List<EntryDef> entries = new List<EntryDef>();
        public List<ActionDef> actions = new List<ActionDef>();
        public List<TriggerDef> triggers = new List<TriggerDef>();
    }

    /// <summary>
    /// Hand-authored server content (Data/content.json, embedded). A content.json placed next to the
    /// plugin DLL replaces the built-in one, so quest fixes can be tried without rebuilding.
    /// </summary>
    internal static class Content
    {
        public static readonly Dictionary<int, ActionDef> Actions = new Dictionary<int, ActionDef>();
        public static readonly List<TriggerDef> Triggers = new List<TriggerDef>();

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

            Plugin.Log.LogInfo($"content.json: {file.entries?.Count ?? -1} entries, {file.actions?.Count ?? -1} actions, {file.triggers?.Count ?? -1} triggers");
            if (file.entries == null) return;
            foreach (var d in file.entries)
            {
                if (!DialogueData.Nodes.ContainsKey(d.node) || string.IsNullOrEmpty(d.npc))
                {
                    Plugin.Log.LogWarning($"content.json: entry for unknown node {d.node} or missing npc");
                    continue;
                }
                var e = new DialogueEntry { node = d.node, npc = d.npc.Trim().ToLowerInvariant() };
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
        }
    }
}
