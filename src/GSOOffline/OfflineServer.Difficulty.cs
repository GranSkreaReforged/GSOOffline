using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// The fate brokers' modifiers: a character can learn faster and give bosses part of their health, the world
    /// having been made for groups of players. Asked of an NPC, not the config, and settled for a day once chosen
    /// (or for a fortune, sooner), so they are a choice rather than a switch. The brokers are the original's
    /// insurance advisors (type 160; item insurance has no use alone). Saved with the character
    /// (CharacterSave.xpRatePercent, bossHealthPercent, difficultyLockedUntil); shown in the top-left corner.
    ///
    /// One sitting sets both: the player drafts the two choices, nothing changes (and nothing is paid) until they
    /// confirm, and confirming settles both for 24 hours. The player stands still for the whole conversation, so
    /// it can't close by walking off mid-choice.
    /// </summary>
    public partial class OfflineServer
    {
        private const int BrokerType = 160;
        private const string BrokerName = "Fate broker";
        private const int NodeDifficulty = 1000;   // the advisors' default dialogue, now the broker's greeting
        private const int NodeFatePlan = 90000, NodeXpRate = 90001, NodeBossHealth = 90002;
        private const int ActionXpRate = 9000, ActionBossHealth = 9010;   // + the choice's index
        private const int ActionStartPlan = 9020, ActionStartPaidPlan = 9021, ActionConfirmFate = 9022;
        private const long DifficultyLockSeconds = 24 * 60 * 60;
        private const int EarlyChangeSilver = 75000;

        private static readonly int[] XpRates = { 100, 150, 200, 300 };          // percent
        private static readonly int[] BossHealths = { 100, 75, 50, 25 };         // percent of their original health

        private float XpRate => character != null ? character.xpRatePercent / 100f : 1f;

        /// <summary>At every scene load: bosses get the character's share of their health.</summary>
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

        // ---- the sitting --------------------------------------------------------------------

        private bool planning, planPaid;   // drafting a new fate; planPaid: confirming costs EarlyChangeSilver
        private int planXp, planBoss;
        private bool heldByBroker;         // we stopped the player's movement for this conversation

        private bool DifficultyLocked => character.difficultyLockedUntil > UnixNow;
        private bool CanPayEarly => DifficultyLocked && character.silver >= EarlyChangeSilver;
        private bool PlanChanged => planXp != character.xpRatePercent || planBoss != character.bossHealthPercent;

        /// <summary>A broker conversation begins: the player stays put until it closes.</summary>
        private void HoldForBroker()
        {
            if (heldByBroker || PlayerDead) return;
            heldByBroker = true;
            Send(1, 4, character.name, false);
        }

        /// <summary>Any conversation ends (or another begins): an unconfirmed draft is dropped, nothing paid.</summary>
        private void EndDifficultyTalk()
        {
            planning = planPaid = false;
            if (!heldByBroker) return;
            heldByBroker = false;
            if (character != null && !PlayerDead) Send(1, 4, character.name, true);
        }

        private string DifficultyNodeVar(int nodeId)
        {
            if (nodeId == NodeDifficulty)
            {
                HoldForBroker();
                return DifficultyStatus();
            }
            if (nodeId == NodeFatePlan) return PlanStatus();
            return null;
        }

        private string DifficultyStatus()
        {
            string s = "I broker fate. These lands were tamed by bands of adventurers, and one alone may want the odds evened: " +
                       "I can let you learn faster, or have the great beasts spare you some of their strength. " +
                       $"You learn at {Rate(character.xpRatePercent)} the usual pace, and bosses have {character.bossHealthPercent}% of their health.";
            long wait = character.difficultyLockedUntil - UnixNow;
            if (wait > 0)
                return s + $" Your fate is settled for {Hours(wait)}. For {EarlyChangeSilver:N0} silver, paid once, I can reopen it now, and you may set both again.";
            return s + " In one sitting you may set both, and when you confirm, both are settled for 24 hours.";
        }

        private string PlanStatus()
        {
            string s = $"You would learn at {Rate(planXp)} the usual pace, and bosses would have {planBoss}% of their health. ";
            if (!PlanChanged) return s + "That is your fate as it stands. Change one or both, then confirm.";
            return s + (planPaid
                ? $"Confirming costs {EarlyChangeSilver:N0} silver, once, for both, and settles them for 24 hours."
                : "Confirming settles both for 24 hours.") + " Nothing changes until you confirm.";
        }

        private static string Rate(int percent) => (percent / 100f).ToString("0.#", CultureInfo.InvariantCulture) + "x";

        private static string Hours(long seconds)
        {
            long h = (seconds + 3599) / 3600;
            return h == 1 ? "another hour" : "another " + h + " hours";
        }

        private bool RunDifficultyAction(int id)
        {
            if (id == ActionStartPlan || id == ActionStartPaidPlan)
            {
                planning = true;
                planPaid = id == ActionStartPaidPlan;
                planXp = character.xpRatePercent;
                planBoss = character.bossHealthPercent;
                return true;
            }
            if (id == ActionConfirmFate)
            {
                ConfirmFate();
                return true;
            }
            int xp = id - ActionXpRate, boss = id - ActionBossHealth;
            if (xp >= 0 && xp < XpRates.Length)
            {
                if (planning) planXp = XpRates[xp];
                return true;
            }
            if (boss >= 0 && boss < BossHealths.Length)
            {
                if (planning) planBoss = BossHealths[boss];
                return true;
            }
            return false;
        }

        private void ConfirmFate()
        {
            if (!planning || !PlanChanged) return;
            if (DifficultyLocked && !planPaid) return;
            if (planPaid)
            {
                if (character.silver < EarlyChangeSilver)
                {
                    Notice($"You need {EarlyChangeSilver:N0} silver to reopen your fate.", "red");
                    return;
                }
                SetSilver(character.silver - EarlyChangeSilver);
                Plugin.Log.LogInfo($"[difficulty] paid {EarlyChangeSilver} silver for an early change");
            }
            bool bossChanged = planBoss != character.bossHealthPercent;
            character.xpRatePercent = planXp;
            character.bossHealthPercent = planBoss;
            character.difficultyLockedUntil = UnixNow + DifficultyLockSeconds;
            planning = planPaid = false;
            SaveCurrentCharacter();
            if (bossChanged)
            {
                ApplyDifficulty();
                RescaleBosses();
            }
            Notice($"Your fate is settled for 24 hours: you learn at {Rate(planXp)} the usual pace, and bosses have {planBoss}% of their health.");
            Plugin.Log.LogInfo($"[difficulty] experience {planXp}%, boss health {planBoss}%");
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

        private static readonly Color FateTextColor = new Color(0.62f, 0.88f, 1f, 1f);
        private static readonly Color FateBackdrop = new Color(0f, 0f, 0f, 0.55f);

        /// <summary>
        /// "Fate: 2x experience, bosses 25% health - settled for 23h 41m" just above the health bar, while either
        /// modifier is set, in the HUD's own outlined font on a tinted backdrop. Shown when the game draws its bar.
        /// </summary>
        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || character == null || !sceneReady) return;
            if (character.xpRatePercent == 100 && character.bossHealthPercent == 100) return;
            var mc = Menucontroller.instance;
            var options = Scr_Options.instance;
            if (mc == null || options == null || !mc.showGUI) return;   // showGUI off: the game's hide-interface mode
            if (Scr_DialogueHandler.instance != null && Scr_DialogueHandler.instance.dialogueActive) return;
            if (CameraController.instance != null && CameraController.instance.cinematicMode) return;

            var parts = new List<string>();
            if (character.xpRatePercent != 100) parts.Add(Rate(character.xpRatePercent) + " experience");
            if (character.bossHealthPercent != 100) parts.Add("bosses " + character.bossHealthPercent + "% health");
            long wait = character.difficultyLockedUntil - UnixNow;
            string text = "Fate: " + string.Join(", ", parts.ToArray()) + " - " +
                          (wait > 0 ? $"settled for {wait / 3600}h {wait % 3600 / 60:00}m" : "a fate broker can change it");

            // The HUD's layout space. The game places the bar at (layout width / 2 - 125, layout height - 128): its
            // own pixels unscaled, a smaller "virtual" screen under GSO HD Textures' UI scale (drawn scaled up by
            // the same factor), or with the game's /scalegui its matrix. The newer LUI HUD has no IMGUI bar; there
            // the line goes where the classic one would be.
            Vector2 bar;
            if (options.scaleGUI)
            {
                GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / options.nativeSize.x, Screen.height / options.nativeSize.y, 1f));
                bar = mc.guiPositions.healthBarPosition;
            }
            else if (!options.useLUI && mc.guiPositions.healthBarPosition.x > 0f)
            {
                bar = mc.guiPositions.healthBarPosition;
                float factor = Screen.width / (2f * (bar.x + 125f));
                GUI.matrix = Matrix4x4.Scale(new Vector3(factor, factor, 1f));
            }
            else
            {
                GUI.matrix = Matrix4x4.identity;
                bar = new Vector2(Screen.width / 2 - 125, Screen.height - 128);
            }
            GUI.depth = -1000;
            var skin = GUI.skin;
            GUI.skin = mc.skin;
            var style = mc.skin.GetStyle("WhiteTextCenter");
            float width = style.CalcSize(new GUIContent(text)).x + 16f;
            float x = bar.x + 125f - width / 2f, y = bar.y - 22f;   // the bar is drawn from bar.y + 2, 20 high
            GUI.color = FateBackdrop;
            GUI.DrawTexture(new Rect(x, y, width, 20f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            mc.drawGUITextWhiteCenter(x, y, text, FateTextColor, (int)width, 20);   // black outline, as the HUD's numbers
            GUI.skin = skin;
            GUI.matrix = Matrix4x4.identity;
        }

        // ---- the dialogue -------------------------------------------------------------------
        // The client only knows its own Dialogue.xml, so the brokers' nodes are added to its list (and ours) before a
        // conversation shows. Their node 1000 (the advisors' insurance offer) becomes the broker's greeting.

        private Scr_DialogueHandler difficultyAddedTo;

        private void AddDifficultyDialogue()
        {
            if (!DialogueData.Nodes.ContainsKey(NodeFatePlan)) AddServerDifficultyNodes();
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
            string price = $"{EarlyChangeSilver:N0} silver";

            var greeting = new AddedNode { id = NodeDifficulty, text = "$1" };
            greeting.options.Add(new AddedOption { id = 1, next = NodeFatePlan, action = ActionStartPlan,
                text = "Set my fate: both choices, settled for 24 hours.", shownIf = () => !DifficultyLocked });
            greeting.options.Add(new AddedOption { id = 4, next = NodeFatePlan, action = ActionStartPaidPlan,
                text = $"Reopen my fate now: {price} once, for both.", shownIf = () => CanPayEarly });
            greeting.options.Add(new AddedOption { id = 3, next = 0, text = "Nevermind" });
            nodes.Add(greeting);

            var plan = new AddedNode { id = NodeFatePlan, text = "$1" };
            plan.options.Add(new AddedOption { id = 1, next = NodeXpRate, text = "Change how quickly I learn." });
            plan.options.Add(new AddedOption { id = 2, next = NodeBossHealth, text = "Change how sturdy bosses are." });
            plan.options.Add(new AddedOption { id = 3, next = NodeDifficulty, action = ActionConfirmFate,
                text = "Confirm both: settled for 24 hours.", shownIf = () => PlanChanged && !planPaid });
            plan.options.Add(new AddedOption { id = 4, next = NodeDifficulty, action = ActionConfirmFate,
                text = $"Confirm both and pay {price}: settled for 24 hours.", shownIf = () => PlanChanged && planPaid });
            plan.options.Add(new AddedOption { id = 5, next = 0, text = "Leave my fate as it is." });
            nodes.Add(plan);

            var xp = new AddedNode { id = NodeXpRate, text = "How quickly would you like to learn? At 1x, every skill grows as it did for everyone before you." };
            for (int i = 0; i < XpRates.Length; i++)
            {
                int rate = XpRates[i];
                xp.options.Add(new AddedOption { id = i + 1, next = NodeFatePlan, action = ActionXpRate + i,
                    text = Rate(rate) + (rate == 100 ? " (the original)" : ""), shownIf = () => planXp != rate });
            }
            xp.options.Add(new AddedOption { id = XpRates.Length + 1, next = NodeFatePlan, text = "Back" });
            nodes.Add(xp);

            var boss = new AddedNode { id = NodeBossHealth, text = "How much of their health should bosses keep? All of it is how they were made: for a group of adventurers." };
            for (int i = 0; i < BossHealths.Length; i++)
            {
                int share = BossHealths[i];
                boss.options.Add(new AddedOption { id = i + 1, next = NodeFatePlan, action = ActionBossHealth + i,
                    text = share + "%" + (share == 100 ? " (the original)" : ""), shownIf = () => planBoss != share });
            }
            boss.options.Add(new AddedOption { id = BossHealths.Length + 1, next = NodeFatePlan, text = "Back" });
            nodes.Add(boss);
            return nodes;
        }
    }
}
