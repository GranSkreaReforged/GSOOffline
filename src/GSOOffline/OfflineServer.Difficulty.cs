using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// The fate brokers' modifiers: a character can learn faster and give bosses part of their health, the world
    /// having been made for groups of players. Asked of an NPC, not the config, and once changed they stay for a
    /// day (or cost a fortune to change sooner), so they are a choice rather than a switch. The brokers are the
    /// original's insurance advisors (type 160; item insurance has no use alone). Saved with the character
    /// (CharacterSave.xpRatePercent, bossHealthPercent, difficultyLockedUntil); shown in the top-left corner.
    /// </summary>
    public partial class OfflineServer
    {
        private const int BrokerType = 160;
        private const string BrokerName = "Fate broker";
        private const int NodeDifficulty = 1000, NodeXpRate = 90001, NodeBossHealth = 90002;   // 1000: the advisors' default dialogue
        private const int ActionXpRate = 9000, ActionBossHealth = 9010;   // + the choice's index
        private const int ActionPayEarly = 9020;
        private const long DifficultyLockSeconds = 24 * 60 * 60;
        private const int EarlyChangeSilver = 75000;

        private static readonly int[] XpRates = { 100, 150, 200, 300 };          // percent
        private static readonly int[] BossHealths = { 100, 75, 50, 25 };         // percent of their original health

        private float XpRate => character != null ? character.xpRatePercent / 100f : 1f;

        /// <summary>At every scene load: bosses get the character's share of their health, the advisors their new name.</summary>
        private void ApplyDifficulty()
        {
            if (Array.IndexOf(XpRates, character.xpRatePercent) < 0) character.xpRatePercent = 100;
            if (Array.IndexOf(BossHealths, character.bossHealthPercent) < 0) character.bossHealthPercent = 100;
            CombatRules.BossHealthShare = character.bossHealthPercent / 100f;
            GameData.ApplyBossHealthShare();
        }

        // Before each NPC list goes out: spawned NPCs copy their name (and minimap label) from the client's template,
        // which the client reads from its own NPCInfo when its NPC handler starts.
        private Scr_NpcHandler brokersRenamedIn;

        private void RenameBrokers()
        {
            var handler = Scr_NpcHandler.instance;
            if (handler == null || handler == brokersRenamedIn || handler.npcTemplates.Count == 0) return;
            brokersRenamedIn = handler;
            DialogueData.NpcNames[BrokerType] = BrokerName.ToLowerInvariant();
            foreach (var t in handler.npcTemplates)
                if (t.npcId == BrokerType) t.npcName = BrokerName;
        }

        private string DifficultyStatus()
        {
            string s = "I broker fate. These lands were tamed by bands of adventurers, and one alone may want the odds evened: " +
                       "I can let you learn faster, or have the great beasts spare you some of their strength. " +
                       $"You learn at {Rate(character.xpRatePercent)} the usual pace, and bosses have {character.bossHealthPercent}% of their health.";
            if (changedThisTalk)
                return s + " You may still change your mind while we talk; after that, it stays for a day.";
            long wait = character.difficultyLockedUntil - UnixNow;
            if (wait > 0)
                return s + $" Fate settles in {Hours(wait)}; for {EarlyChangeSilver:N0} silver, I can unsettle it sooner.";
            return s + " Choose carefully: once I change it, it stays for a day.";
        }

        private static string Rate(int percent) => (percent / 100f).ToString("0.#", CultureInfo.InvariantCulture) + "x";

        private static string Hours(long seconds)
        {
            long h = (seconds + 3599) / 3600;
            return h == 1 ? "an hour" : h + " hours";
        }

        // Both modifiers can be set in one visit: the day's wait starts with the change, but not while still talking.
        // Paying for an early change covers the rest of that visit too.
        private bool changedThisTalk, payingEarly;
        private bool DifficultyLocked => !changedThisTalk && character.difficultyLockedUntil > UnixNow;
        private bool CanPayEarly => DifficultyLocked && character.silver >= EarlyChangeSilver;

        private void EndDifficultyTalk() => changedThisTalk = payingEarly = false;

        private bool RunDifficultyAction(int id)
        {
            if (id == ActionPayEarly)
            {
                payingEarly = true;
                return true;
            }
            int xp = id - ActionXpRate, boss = id - ActionBossHealth;
            if (xp >= 0 && xp < XpRates.Length)
            {
                if (!PayForChange()) return true;
                character.xpRatePercent = XpRates[xp];
                LockDifficulty();
                Notice($"You now learn at {Rate(character.xpRatePercent)} the usual pace.");
                Plugin.Log.LogInfo($"[difficulty] experience {character.xpRatePercent}%");
                return true;
            }
            if (boss >= 0 && boss < BossHealths.Length)
            {
                if (!PayForChange()) return true;
                character.bossHealthPercent = BossHealths[boss];
                LockDifficulty();
                ApplyDifficulty();
                RescaleBosses();
                Notice($"Bosses now have {character.bossHealthPercent}% of their health.");
                Plugin.Log.LogInfo($"[difficulty] boss health {character.bossHealthPercent}%");
                return true;
            }
            return false;
        }

        /// <summary>Free once the day is up (or within the visit that changed it); otherwise the early change's price.</summary>
        private bool PayForChange()
        {
            if (!DifficultyLocked) return true;
            if (!payingEarly) return false;
            if (character.silver < EarlyChangeSilver)
            {
                Notice($"You need {EarlyChangeSilver:N0} silver to change your fate early.", "red");
                return false;
            }
            SetSilver(character.silver - EarlyChangeSilver);
            Plugin.Log.LogInfo($"[difficulty] paid {EarlyChangeSilver} silver for an early change");
            return true;
        }

        private void LockDifficulty()
        {
            character.difficultyLockedUntil = UnixNow + DifficultyLockSeconds;
            changedThisTalk = true;
            SaveCurrentCharacter();
        }

        /// <summary>The bosses already in the scene take the new share, wounds and all.</summary>
        private void RescaleBosses()
        {
            if (world == null) return;
            foreach (var n in world.npcs)
            {
                if (!GameData.Npcs.TryGetValue(n.typeId, out var info) || !info.sharedHealth || n.maxHealth == info.health) continue;
                n.health = Mathf.Clamp(Mathf.RoundToInt(n.health * (float)info.health / Mathf.Max(1, n.maxHealth)), n.dead ? 0 : 1, info.health);
                n.maxHealth = info.health;
            }
        }

        // ---- the corner line ----------------------------------------------------------------

        private static readonly Color DifficultyColor = new Color(0.55f, 0.85f, 1f, 0.9f);
        private static readonly Color DifficultyShadow = new Color(0f, 0f, 0f, 0.6f);
        private GUIStyle difficultyStyle;

        /// <summary>"2x experience, bosses 25% health - change in 23h 41m", under the version lines, while either is set.</summary>
        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || character == null || !sceneReady) return;
            if (character.xpRatePercent == 100 && character.bossHealthPercent == 100) return;
            var mc = Menucontroller.instance;
            if (mc != null && !mc.showGUI) return;   // the game's hide-interface mode (screenshots)

            var parts = new List<string>();
            if (character.xpRatePercent != 100) parts.Add(Rate(character.xpRatePercent) + " experience");
            if (character.bossHealthPercent != 100) parts.Add("bosses " + character.bossHealthPercent + "% health");
            long wait = character.difficultyLockedUntil - UnixNow;
            string text = string.Join(", ", parts.ToArray()) + " - " +
                          (wait > 0 ? $"change at a fate broker in {wait / 3600}h {wait % 3600 / 60:00}m" : "a fate broker can change it now");

            float scale = Mathf.Max(1f, Screen.height / 1080f);
            if (difficultyStyle == null) difficultyStyle = new GUIStyle(GUI.skin.label) { wordWrap = false, richText = false };
            difficultyStyle.fontSize = Mathf.RoundToInt(12f * scale);
            GUI.matrix = Matrix4x4.identity;
            GUI.depth = -1000;
            var content = new GUIContent(text);
            var size = difficultyStyle.CalcSize(content);
            float x = 6f * scale, y = (4f + 2 * 16f) * scale;   // the third line: the mods' versions are above (UpdateOverlay)
            difficultyStyle.normal.textColor = DifficultyShadow;
            GUI.Label(new Rect(x + 1f, y + 1f, size.x, size.y), content, difficultyStyle);
            difficultyStyle.normal.textColor = DifficultyColor;
            GUI.Label(new Rect(x, y, size.x, size.y), content, difficultyStyle);
        }

        // ---- the dialogue -------------------------------------------------------------------
        // The client only knows its own Dialogue.xml, so the brokers' nodes are added to its list (and ours) before a
        // conversation shows. Their node 1000 (the advisors' insurance offer) becomes the broker's menu.

        private Scr_DialogueHandler difficultyAddedTo;

        private void AddDifficultyDialogue()
        {
            if (!DialogueData.Nodes.ContainsKey(NodeXpRate)) AddServerDifficultyNodes();
            var handler = Scr_DialogueHandler.instance;
            if (handler == null || handler == difficultyAddedTo) return;
            difficultyAddedTo = handler;
            foreach (var n in DifficultyNodes())
            {
                var c = new Scr_DialogueHandler.Node { noteId = n.id, nodeText = n.text };
                foreach (var o in n.options) c.options.Add(new Scr_DialogueHandler.Option { optionId = o.id, optionText = o.text });
                handler.nodesList.Insert(0, c);   // getNode takes the first with the id: ours before the data's
            }
        }

        private void AddServerDifficultyNodes()
        {
            foreach (var n in DifficultyNodes())
            {
                var node = new DialogueNode { id = n.id, text = n.text };
                foreach (var o in n.options)
                    node.options.Add(new DialogueOption { id = o.id, next = o.next, otherAction = o.action, shownIf = o.shownIf });
                DialogueData.Nodes[n.id] = node;
            }
        }

        private class AddedOption
        {
            public int id, next, action;
            public string text;
            public Func<bool> shownIf;
        }

        private class AddedNode
        {
            public int id;
            public string text;
            public readonly List<AddedOption> options = new List<AddedOption>();
        }

        private List<AddedNode> DifficultyNodes()
        {
            var nodes = new List<AddedNode>();
            string price = $" ({EarlyChangeSilver:N0} silver)";

            var menu = new AddedNode { id = NodeDifficulty, text = "$1" };
            menu.options.Add(new AddedOption { id = 1, next = NodeXpRate, text = "Change how quickly I learn.", shownIf = () => !DifficultyLocked });
            menu.options.Add(new AddedOption { id = 2, next = NodeBossHealth, text = "Change how sturdy bosses are.", shownIf = () => !DifficultyLocked });
            menu.options.Add(new AddedOption { id = 4, next = NodeXpRate, action = ActionPayEarly, text = "Change how quickly I learn now." + price, shownIf = () => CanPayEarly });
            menu.options.Add(new AddedOption { id = 5, next = NodeBossHealth, action = ActionPayEarly, text = "Change how sturdy bosses are now." + price, shownIf = () => CanPayEarly });
            menu.options.Add(new AddedOption { id = 3, next = 0, text = "Nevermind" });
            nodes.Add(menu);

            var xp = new AddedNode { id = NodeXpRate, text = "How quickly would you like to learn? At 1x, every skill grows as it did for everyone before you." };
            for (int i = 0; i < XpRates.Length; i++)
            {
                int rate = XpRates[i];
                xp.options.Add(new AddedOption { id = i + 1, next = NodeDifficulty, action = ActionXpRate + i,
                    text = Rate(rate) + (rate == 100 ? " (the original)" : ""), shownIf = () => character.xpRatePercent != rate });
            }
            xp.options.Add(new AddedOption { id = XpRates.Length + 1, next = NodeDifficulty, text = "Back" });
            nodes.Add(xp);

            var boss = new AddedNode { id = NodeBossHealth, text = "How much of their health should bosses keep? All of it is how they were made: for a group of adventurers." };
            for (int i = 0; i < BossHealths.Length; i++)
            {
                int share = BossHealths[i];
                boss.options.Add(new AddedOption { id = i + 1, next = NodeDifficulty, action = ActionBossHealth + i,
                    text = share + "%" + (share == 100 ? " (the original)" : ""), shownIf = () => character.bossHealthPercent != share });
            }
            boss.options.Add(new AddedOption { id = BossHealths.Length + 1, next = NodeDifficulty, text = "Back" });
            nodes.Add(boss);
            return nodes;
        }
    }
}
