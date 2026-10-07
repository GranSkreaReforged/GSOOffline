using System;
using System.Collections.Generic;
using System.Xml;

namespace GSOOffline
{
    public class QuestRef
    {
        public int quest, phase;

        public static QuestRef Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split(',');
            return new QuestRef { quest = int.Parse(p[0].Trim()), phase = int.Parse(p[1].Trim()) };
        }
    }

    /// <summary>questreq="q,=phase" (also &lt; &gt; ! and a bare number meaning "=").</summary>
    public class QuestReq
    {
        public int quest;
        public char op = '=';
        public int value;

        public static QuestReq Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split(',');
            string v = p[1].Trim();
            var r = new QuestReq { quest = int.Parse(p[0].Trim()) };
            if (v.Length > 0 && "=<>!".IndexOf(v[0]) >= 0)
            {
                r.op = v[0];
                v = v.Substring(1);
            }
            r.value = int.Parse(v);
            return r;
        }

        public bool Test(int phase)
        {
            switch (op)
            {
                case '<': return phase < value;
                case '>': return phase > value;
                case '!': return phase != value;
                default: return phase == value;
            }
        }
    }

    public class DialogueOption
    {
        public int id, next, otherAction;
        public QuestReq questReq;
        public QuestRef questUpdate;
        public int[] questVarReq;   // quest, var, value
        public int[] itemReq;       // typeId, amount
    }

    public class DialogueNode
    {
        public int id, otherAction, openShop;
        public string text, npcName;
        public QuestRef questUpdate;
        public int[] addItem;       // typeId, amount
        public readonly List<DialogueOption> options = new List<DialogueOption>();
    }

    /// <summary>
    /// When the old server picked a conversation's first node. Entry nodes are the ones nothing links to;
    /// the server chose among them by NPC and quest progress. Conditions are derived from the quest
    /// updates reachable from the entry (see <see cref="DialogueData.DeriveEntries"/>) or set in content.json.
    /// </summary>
    public class DialogueEntry
    {
        public int node;
        public string npc;          // lower-case NPC name
        public int quest;           // 0 = unconditional
        public int phase;
        public int priority;        // advancing > reminder > unconditional
        public List<int[]> requiredItems = new List<int[]>();
    }

    public class QuestInfo
    {
        public int id;
        public string name;
        public int silver;
        public string completionText;
        public readonly List<int[]> rewardItems = new List<int[]>();
        public int maxPhase;
    }

    internal static class DialogueData
    {
        public static readonly Dictionary<int, DialogueNode> Nodes = new Dictionary<int, DialogueNode>();
        public static readonly Dictionary<int, QuestInfo> Quests = new Dictionary<int, QuestInfo>();
        public static readonly Dictionary<string, List<DialogueEntry>> EntriesByNpc = new Dictionary<string, List<DialogueEntry>>();
        public static readonly Dictionary<int, int> DefaultDialogue = new Dictionary<int, int>();   // npc type -> node
        public static readonly Dictionary<int, string> NpcNames = new Dictionary<int, string>();    // npc type -> name

        private static int[] Pair(string s, char sep)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split(sep);
            var r = new int[p.Length];
            for (int i = 0; i < p.Length; i++) r[i] = int.Parse(p[i].Trim());
            return r;
        }

        public static void Load()
        {
            LoadNodes();
            LoadQuests();
            LoadNpcLinks();
            DeriveEntries();
            Content.Apply();
            int entries = 0;
            foreach (var l in EntriesByNpc.Values) entries += l.Count;
            Plugin.Log.LogInfo($"Dialogue: {Nodes.Count} nodes, {entries} entry rules for {EntriesByNpc.Count} NPCs, {Quests.Count} quests.");
        }

        private static void LoadNodes()
        {
            foreach (XmlNode x in GameData.LoadXml("XMLs/Dialogue").DocumentElement.ChildNodes)
            {
                if (x.NodeType != XmlNodeType.Element) continue;
                try
                {
                    var n = new DialogueNode
                    {
                        id = GameData.IntAttr(x, "id"),
                        text = GameData.Attr(x, "text"),
                        npcName = GameData.Attr(x, "npcName"),
                        otherAction = GameData.IntAttr(x, "otheraction"),
                        openShop = GameData.IntAttr(x, "openshop"),
                        questUpdate = QuestRef.Parse(GameData.Attr(x, "questupdate")),
                        addItem = Pair(GameData.Attr(x, "additem"), '-'),
                    };
                    foreach (XmlNode o in x.ChildNodes)
                    {
                        if (o.NodeType != XmlNodeType.Element) continue;
                        n.options.Add(new DialogueOption
                        {
                            id = GameData.IntAttr(o, "optionid"),
                            next = GameData.IntAttr(o, "next"),
                            otherAction = GameData.IntAttr(o, "otheraction"),
                            questReq = QuestReq.Parse(GameData.Attr(o, "questreq")),
                            questUpdate = QuestRef.Parse(GameData.Attr(o, "questupdate")),
                            questVarReq = Pair(GameData.Attr(o, "questvarreq"), ','),
                            itemReq = Pair(GameData.Attr(o, "itemreq"), ','),
                        });
                    }
                    Nodes[n.id] = n;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Skipping malformed dialogue node: {e.Message}");
                }
            }
        }

        private static void LoadQuests()
        {
            foreach (XmlNode x in GameData.LoadXml("XMLs/Quests").DocumentElement.ChildNodes)
            {
                if (x.NodeType != XmlNodeType.Element) continue;
                var q = new QuestInfo
                {
                    id = GameData.IntAttr(x, "id"),
                    name = GameData.Attr(x, "name"),
                    silver = GameData.IntAttr(x, "silver"),
                    completionText = GameData.Attr(x, "completiontext"),
                };
                for (int i = 1; i <= 4; i++)
                {
                    var item = Pair(GameData.Attr(x, "item" + i), '-');
                    if (item != null) q.rewardItems.Add(item);
                }
                foreach (XmlNode ph in x.ChildNodes)
                    if (ph.NodeType == XmlNodeType.Element)
                        q.maxPhase = Math.Max(q.maxPhase, GameData.IntAttr(ph, "id"));
                Quests[q.id] = q;
            }
        }

        private static void LoadNpcLinks()
        {
            foreach (XmlNode x in GameData.LoadXml("XMLs/NPCInfo").DocumentElement.ChildNodes)
            {
                if (x.NodeType != XmlNodeType.Element) continue;
                int id = GameData.IntAttr(x, "id");
                NpcNames[id] = (GameData.Attr(x, "name") ?? string.Empty).Trim().ToLowerInvariant();
                int dd = GameData.IntAttr(x, "defaultdialogue");
                if (dd > 0) DefaultDialogue[id] = dd;
            }
        }

        public static HashSet<int> Subtree(int start)
        {
            var seen = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                int id = stack.Pop();
                if (!seen.Add(id) || !Nodes.TryGetValue(id, out var n)) continue;
                foreach (var o in n.options)
                    if (o.next > 0) stack.Push(o.next);
            }
            return seen;
        }

        private static IEnumerable<QuestRef> UpdatesIn(HashSet<int> tree)
        {
            foreach (int id in tree)
            {
                if (!Nodes.TryGetValue(id, out var n)) continue;
                if (n.questUpdate != null) yield return n.questUpdate;
                foreach (var o in n.options)
                    if (o.questUpdate != null) yield return o.questUpdate;
            }
        }

        public static void AddEntry(DialogueEntry e)
        {
            if (!EntriesByNpc.TryGetValue(e.npc, out var list))
                EntriesByNpc[e.npc] = list = new List<DialogueEntry>();
            list.RemoveAll(x => x.node == e.node);
            list.Add(e);
        }

        /// <summary>Fill in the quest condition for an entry from what its conversation does.</summary>
        public static void DeriveCondition(DialogueEntry e)
        {
            var tree = Subtree(e.node);
            QuestRef advancing = null;
            QuestRef completing = null;
            foreach (var u in UpdatesIn(tree))
            {
                if (u.phase > 0 && (advancing == null || u.phase < advancing.phase)) advancing = u;
                if (u.phase == -1) completing = u;
            }
            if (advancing != null)
            {
                // A conversation that moves quest q to phase p is offered while q sits at p-1.
                e.quest = advancing.quest;
                e.phase = advancing.phase - 1;
                e.priority = 3;
                return;
            }
            if (completing != null && Quests.TryGetValue(completing.quest, out var q))
            {
                e.quest = q.id;
                e.phase = q.maxPhase;
                e.priority = 3;
                return;
            }
            // Reminder: repeats a line from a conversation that set phase p, so it belongs to phase p.
            var text = Nodes.TryGetValue(e.node, out var en) ? en.text : null;
            foreach (var n in Nodes.Values)
            {
                if (n.id == e.node || n.questUpdate == null || n.questUpdate.phase <= 0) continue;
                if (n.text == text || Subtree(n.id).Overlaps(tree))
                {
                    e.quest = n.questUpdate.quest;
                    e.phase = n.questUpdate.phase;
                    e.priority = 2;
                    return;
                }
            }
            e.priority = 1;
        }

        private static void DeriveEntries()
        {
            var linked = new HashSet<int>();
            foreach (var n in Nodes.Values)
                foreach (var o in n.options)
                    if (o.next > 0) linked.Add(o.next);
            var defaults = new HashSet<int>(DefaultDialogue.Values);

            foreach (var n in Nodes.Values)
            {
                if (linked.Contains(n.id) || defaults.Contains(n.id) || string.IsNullOrEmpty(n.npcName)) continue;
                var e = new DialogueEntry { node = n.id, npc = n.npcName.Trim().ToLowerInvariant() };
                DeriveCondition(e);
                AddEntry(e);
            }
        }
    }
}
