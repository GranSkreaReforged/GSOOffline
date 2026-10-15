using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Quest steps the original server ran in code rather than data (Scr_DialogueHandler's actions and next-node
    /// choices); the rest of these quests is in content.json.
    /// - Powder Monkey (25): action 61, G'Ertagnon's wound staunched with the first bandage you carry.
    /// - Mantle of St. Jakob (33): which of Nimuel's lines follows "I've found a piece" (2260) and "When can we meet
    ///   again?" (2254, Archery 40 or not); Nimuel sending you off with a blink (75); the master tailor's work (74:
    ///   49 990 silver, the torn piece, 500 silk, 3 life motes and 10 harpy feathers for the mantle); a new ring
    ///   (77: 100 iridium ingots and 3 life motes, as Nimuel asks; the original's code for it came after its 2019
    ///   snapshot, so this one follows his words).
    /// </summary>
    public partial class OfflineServer
    {
        private static readonly int[] Bandages = { 10367, 10368, 10369, 10370, 10443, 10444 };

        private bool RunQuestScriptAction(int id)
        {
            switch (id)
            {
                case 78:   // Hunters Guild: the Porter lets members in
                    if (CountItem(ItemInsignia) > 0)
                    {
                        if (dialogueNode != null) pendingInsideGuild = true;
                        else TeleportNear(SceneAthagosInteriors, HuntersGuildHall);
                    }
                    else if (dialogueNpc != null) NpcSay(dialogueNpc, "That's no insignia.");
                    return true;
                case 79:   // Hunters Guild: a member who lost the insignia gets another
                    if (character.GetQuestPhase(QuestHuntersGuild) == -1 && CountItem(ItemInsignia) == 0)
                    {
                        GiveItem(ItemInsignia);
                        if (dialogueNpc != null) NpcSay(dialogueNpc, "Here, a new one. Don't lose it again.");
                    }
                    return true;
                case 61:
                    foreach (int b in Bandages)
                    {
                        if (CountItem(b) == 0) continue;
                        TakeItems(b, 1);
                        Send(8, 5, character.name, ActionAnimation, 18);   // bandaging
                        Later(3f, () => { if (character != null) Send(3, 14, character.name, ActionAnimation); });
                        break;
                    }
                    return true;
                case 74:
                    MantleTailor();
                    return true;
                case 75:
                    NimuelSendsYouOff();
                    return true;
                case 77:
                    if (CountItem(10418) >= 100 && CountItem(10500) >= 3)
                    {
                        TakeItems(10418, 100);
                        TakeItems(10500, 3);
                        GiveItem(434);
                        if (dialogueNpc != null) NpcSay(dialogueNpc, "Here you are. Try not to lose this one.");
                    }
                    else if (dialogueNpc != null)
                        NpcSay(dialogueNpc, "I will need 100 iridium ingots and 3 life motes for that.");
                    return true;
            }
            return false;
        }

        // ---- reconstructed quests (2020 content the original's 2019 code doesn't have) --------------

        private const int QuestMagicalJourney = 16, QuestHuntersGuild = 35, QuestGrantsGloves = 34;
        private const int ItemInsignia = 10730, SceneAthagosInteriors = 23, SceneMagesGuild = 21, AbilityFlameLance = 116, TrainingDummy = 80;
        private static readonly Vector3 HuntersGuildHall = new Vector3(108f, 139.5f, 447f);
        private bool pendingInsideGuild;

        /// <summary>A Magical Journey: Flame Lance cast at one of Kromwell's training dummies.</summary>
        private void OnAbilityCastAt(NpcEntity target, AbilityInfo a)
        {
            if (a.id != AbilityFlameLance || target.typeId != TrainingDummy || character.scene != SceneMagesGuild) return;
            if (character.GetQuestPhase(QuestMagicalJourney) != 3) return;
            SetQuestPhase(QuestMagicalJourney, 4);
            Notice("Your Flame Lance scorches the training dummy. Kromwell saw it.");
        }

        /// <summary>The Porter's "Go on in": once the conversation closes, into the guild hall.</summary>
        private void TickHuntersGuildDoor()
        {
            if (!pendingInsideGuild || dialogueNode != null || character == null) return;
            pendingInsideGuild = false;
            TeleportNear(SceneAthagosInteriors, HuntersGuildHall);
        }

        /// <summary>The next node for options whose next is -1: the original chose it in code. 0 = close.</summary>
        private int ScriptedNextNode(int nodeId)
        {
            switch (nodeId)
            {
                case 2260: return CountItem(10645) > 0 ? 2262 : 2261;   // the torn piece from Goldon
                case 2254: return SkillLevel(17) >= 40 ? 2255 : 2257;   // Archery 40 to join the search
            }
            return 0;
        }

        private void MantleTailor()
        {
            var tailor = dialogueNpc;
            if (character.silver < 49990)
            {
                if (tailor != null) NpcSay(tailor, "Come back when you have enough money on you.");
                return;
            }
            if (CountItem(10645) == 0 || CountItem(10324) < 500 || CountItem(10500) < 3 || CountItem(10655) < 10)
            {
                if (tailor != null) NpcSay(tailor, "I can't reconstruct the mantle with these item. Check the instructions, you're missing something.");
                return;
            }
            SetSilver(character.silver - 49990);
            TakeItems(10645, 1);
            TakeItems(10324, 500);
            TakeItems(10500, 3);
            TakeItems(10655, 10);
            GiveItem(420);
        }

        /// <summary>"Bite me, monk." Nimuel blinks you out of the monastery, as he did (the original's spot outside).</summary>
        private void NimuelSendsYouOff()
        {
            var player = LocalPlayer;
            if (player == null) return;
            if (dialogueNpc != null)
            {
                var monk = dialogueNpc;
                Send(16, 0, monk.uid, 23);   // 16/0: a human NPC's action (he raises his hands), back to none after 3 s
                Later(3f, () => Send(16, 0, monk.uid, 0));
            }
            Vector3 outside = new Vector3(857f, 7f, 1590f);
            Later(1.5f, () =>
            {
                var p = LocalPlayer;
                if (p == null || character == null) return;
                PlayEffect(138, p.transform.position);
                PlaySound(421, p.transform.position, 40);
                PlayEffect(139, outside);
                PlaySound(422, outside, 40);
            });
            Later(1.75f, () => { if (character != null && character.scene == 1) Teleport(1, outside); });
        }
    }
}
