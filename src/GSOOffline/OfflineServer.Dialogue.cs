using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GSOOffline
{
    public partial class OfflineServer
    {
        private DialogueNode dialogueNode;
        private NpcEntity dialogueNpc;
        private ShopStock openShop;

        private void RegisterDialogueHandlers()
        {
            On(3, 8, c => InteractNpc((int)c[2]));
            On(3, 9, c => ChooseOption((int)c[2]));
            On(8, 8, c => BuyFromShop((int)c[2], (int)c[3]));
            On(4, 5, c => SellToShop((int)c[2], (int)c[3], (int)c[4]));
        }

        // ---- dialogue ------------------------------------------------------------------------

        private void InteractNpc(int uid)
        {
            var npc = world?.GetNpc(uid);
            if (character == null || npc == null || npc.dead || !NpcShown(npc)) return;
            dialogueNpc = npc;
            Send(3, 10, character.name, uid);

            int node = PickEntryNode(npc.typeId);
            if (node > 0)
                EnterNode(node);
            else
                Plugin.Log.LogInfo($"NPC type {npc.typeId} ('{NpcName(npc.typeId)}') has no available dialogue.");
        }

        private static string NpcName(int typeId) => DialogueData.NpcNames.TryGetValue(typeId, out var n) ? n : "?";

        /// <summary>Quest-specific conversations first (advancing, then reminders), else the NPC's default dialogue.</summary>
        private int PickEntryNode(int npcType)
        {
            DialogueEntry best = null;
            if (DialogueData.EntriesByNpc.TryGetValue(NpcName(npcType), out var entries))
            {
                foreach (var e in entries)
                {
                    if (e.npcType != 0 && e.npcType != npcType) continue;
                    if (e.quest != 0 && character.GetQuestPhase(e.quest) != e.phase) continue;
                    if (e.condition != null && !e.condition.Holds(character, GetQuestVar)) continue;
                    if (!HasItems(e.requiredItems)) continue;
                    if (best == null || e.priority > best.priority || (e.priority == best.priority && e.node < best.node))
                        best = e;
                }
            }
            if (best != null && best.priority > 1) return best.node;
            if (DialogueData.DefaultDialogue.TryGetValue(npcType, out int dd)) return dd;
            return best?.node ?? 0;
        }

        private bool HasItems(List<int[]> items)
        {
            foreach (var i in items)
                if (CountItem(i[0]) < i[1]) return false;
            return true;
        }

        private bool OptionVisible(DialogueOption o)
        {
            if (o.questReq != null && !o.questReq.Test(character.GetQuestPhase(o.questReq.quest))) return false;
            if (o.itemReq != null && CountItem(o.itemReq[0]) < (o.itemReq.Length > 1 ? o.itemReq[1] : 1)) return false;
            if (o.questVarReq != null && GetQuestVar(o.questVarReq[0], o.questVarReq[1]) != o.questVarReq[2]) return false;
            return true;
        }

        private void EnterNode(int id)
        {
            if (!DialogueData.Nodes.TryGetValue(id, out var n))
            {
                CloseDialogue();
                return;
            }
            dialogueNode = n;

            if (n.questUpdate != null) SetQuestPhase(n.questUpdate.quest, n.questUpdate.phase);
            if (n.addItem != null) GiveItem(n.addItem[0], n.addItem.Length > 1 ? n.addItem[1] : 1);
            if (n.otherAction > 0) RunAction(n.otherAction, n.id);
            if (n.openShop > 0) OpenShop(n.openShop);

            var visible = new List<string>();
            foreach (var o in n.options)
                if (OptionVisible(o)) visible.Add(o.id.ToString());
            if (visible.Count == 0)
            {
                CloseDialogue();
                return;
            }
            Plugin.Log.LogInfo($"[dialogue] node {n.id} options [{string.Join(",", visible.ToArray())}]");
            // nodeId>opt|opt>var1|var2  ($1 in node text = player name)
            Send(2, 3, character.name, n.id + ">" + string.Join("|", visible.ToArray()) + ">" + character.name + "|-");
        }

        private void ChooseOption(int optionId)
        {
            if (character == null || dialogueNode == null) return;
            var o = dialogueNode.options.Find(x => x.id == optionId);
            if (o == null || !OptionVisible(o))
            {
                CloseDialogue();
                return;
            }
            if (o.questUpdate != null) SetQuestPhase(o.questUpdate.quest, o.questUpdate.phase);
            if (o.otherAction > 0) RunAction(o.otherAction, dialogueNode.id);
            if (o.next > 0)
                EnterNode(o.next);
            else
                CloseDialogue();
        }

        private ActionDef pendingDialogueTeleport;

        private void CloseDialogue()
        {
            dialogueNode = null;
            if (character != null) Send(2, 3, character.name, "-");
            var move = pendingDialogueTeleport;
            pendingDialogueTeleport = null;
            if (move != null && character != null) TeleportNear(move.scene, GameData.ParseVec(move.pos));
        }

        private void RunAction(int id, int nodeId)
        {
            if (!Content.Actions.TryGetValue(id, out var a))
            {
                Plugin.Log.LogWarning($"Dialogue action {id} (node {nodeId}) is not implemented yet; add it to content.json.");
                return;
            }
            foreach (var g in a.give)
                if (!a.onlyIfMissing || CountItem(g.type) == 0) GiveItem(g.type, g.Amount);
            foreach (var t in a.take) TakeItems(t.type, t.Amount);
            if (a.silver != 0) SetSilver(character.silver + a.silver);
            if (a.window > 0) Send(3, 33, character.name, a.window);
            if (!string.IsNullOrEmpty(a.notice)) Notice(a.notice);
            var v = Content.ParseInts(a.setVar);
            if (v != null && v.Length >= 3) SetQuestVar(v[0], v[1], v[2]);
            var q = Content.ParseInts(a.setQuest);
            if (q != null && q.Length >= 2) SetQuestPhase(q[0], q[1]);
            if (a.scene > 0 && !string.IsNullOrEmpty(a.pos))
            {
                // Mid-conversation, the move waits for the conversation to end (a scene change would cut it off).
                if (dialogueNode != null) pendingDialogueTeleport = a;
                else TeleportNear(a.scene, GameData.ParseVec(a.pos));
            }
            if (a.dialogue > 0) StartDialogue(a.dialogue, a.dialogueNpc);
        }

        // ---- quests --------------------------------------------------------------------------

        private int GetQuestVar(int quest, int var)
        {
            foreach (var v in character.questVars)
                if (v.quest == quest && v.var == var) return v.value;
            return 0;
        }

        public void SetQuestPhase(int quest, int phase)
        {
            if (character == null || character.GetQuestPhase(quest) == phase) return;
            if (character.GetQuestPhase(quest) == -1) return;   // completed quests stay completed
            character.SetQuestPhase(quest, phase);
            Plugin.Log.LogInfo($"Quest {quest} -> phase {phase}");
            Send(8, 9, character.name, quest, phase);
            if (phase == -1) CompleteQuest(quest);
            else CheckItemTriggers();
        }

        private static readonly Regex XpReward = new Regex(@"gained (\d+) XP in (.+?)\.", RegexOptions.IgnoreCase);

        private void CompleteQuest(int quest)
        {
            Send(3, 15, character.name, quest);
            if (!DialogueData.Quests.TryGetValue(quest, out var q)) return;
            foreach (var item in q.rewardItems) GiveItem(item[0], item.Length > 1 ? item[1] : 1);
            if (q.silver > 0) SetSilver(character.silver + q.silver);

            // XP rewards only exist as prose ("You have gained 1000 XP in Blacksmithy, Cooking and Mining.").
            var m = q.completionText != null ? XpReward.Match(q.completionText) : Match.Empty;
            if (!m.Success) return;
            int xp = int.Parse(m.Groups[1].Value);
            foreach (string raw in Regex.Split(m.Groups[2].Value, @",\s*|\s+and\s+"))
            {
                string skill = raw.Replace(" ", string.Empty).ToLowerInvariant();
                foreach (var kv in SkillById)
                    if (kv.Value.ToLowerInvariant() == skill) AddXp(kv.Key, xp);
            }
        }

        private bool checkingTriggers;

        /// <summary>Advance quests whose "have N of item" condition is now met.</summary>
        private void CheckItemTriggers()
        {
            if (character == null || checkingTriggers) return;
            checkingTriggers = true;
            try
            {
                foreach (var t in Content.Triggers.ToArray())
                    if (TriggerReady(t, "item") && CountItem(t.item) >= Math.Max(1, t.amount))
                        FireTrigger(t);
            }
            finally
            {
                checkingTriggers = false;
            }
        }

        public void OnHealed()
        {
            foreach (var t in Content.Triggers.ToArray())
                if (TriggerReady(t, "heal"))
                    FireTrigger(t);
        }

        public void OnNpcKilled(int npcType)
        {
            foreach (var t in Content.Triggers.ToArray())
                if (TriggerReady(t, "kill") && t.npc == npcType && UnityEngine.Random.Range(0, 100) < t.chance)
                    FireTrigger(t);
        }

        private string QuestsLine()
        {
            var parts = new List<string>();
            foreach (var q in character.quests) parts.Add(q.id + "_" + q.phase);
            return string.Join(",", parts.ToArray());
        }

        // ---- shops ---------------------------------------------------------------------------

        private void OpenShop(int shopId)
        {
            openShop = ShopData.Get(shopId);
            var parts = new List<string>();
            foreach (var i in openShop.items) parts.Add(i[0] + "-" + i[1] + "-99");
            string list = string.Join(",", parts.ToArray());
            // currency type 0 = silver; the refresh that follows carries the merchant's own silver,
            // which caps what the client will let you sell.
            Send(0, 2, character.name, openShop.name, "0>" + list);
            Send(2, 37, openShop.name, openShop.merchantSilver + ">" + list);
        }

        private void RefreshShop()
        {
            if (openShop == null) return;
            var parts = new List<string>();
            foreach (var i in openShop.items) parts.Add(i[0] + "-" + i[1] + "-99");
            Send(2, 37, openShop.name, openShop.merchantSilver + ">" + string.Join(",", parts.ToArray()));
        }

        private int ShopPrice(int typeId)
        {
            if (openShop == null) return -1;
            foreach (var i in openShop.items)
                if (i[0] == typeId) return i[1];
            return -1;
        }

        private void BuyFromShop(int typeId, int amount)
        {
            int price = ShopPrice(typeId);
            if (character == null || price < 0 || amount <= 0) return;
            int cost = price * amount;
            if (character.silver < cost)
            {
                Send(2, 41, "You don't have enough silver.");
                return;
            }
            SetSilver(character.silver - cost);
            GiveItem(typeId, amount);
            openShop.merchantSilver += cost;
            RefreshShop();
        }

        // Matches the client's shop window: 30% of the shop's price if it stocks the item, else 10% of base value.
        private void SellToShop(int typeId, int itemId, int amount)
        {
            if (character == null || openShop == null) return;
            var t = ItemData.Get(typeId);
            var it = t?.Stacks == true ? FindStack(typeId) : FindItem(itemId);
            if (t == null || it == null) return;
            amount = Mathf.Clamp(amount, 1, it.amount);
            int shopPrice = ShopPrice(typeId);
            float each = shopPrice >= 0 ? shopPrice * 0.3f : t.price * 0.1f;
            each *= Menucontroller.instance.GetItemPriceGradeMultiplier(it.grade);
            int payout = (int)(each * amount);
            if (payout > openShop.merchantSilver)
            {
                Send(2, 41, "The merchant can't afford that.");
                return;
            }
            RemoveFromStack(it, amount);
            SetSilver(character.silver + payout);
            openShop.merchantSilver -= payout;
            RefreshShop();
        }
    }
}
