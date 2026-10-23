using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Quest steps the original server ran in code rather than data (Scr_DialogueHandler's actions and next-node
    /// choices, Scr_NpcHandler's per-NPC lines, Scr_QuestHandler); the rest of these quests is in content.json.
    /// - Powder Monkey (25): action 61, G'Ertagnon's wound staunched with the first bandage you carry.
    /// - Mantle of St. Jakob (33): which of Nimuel's lines follows "I've found a piece" (2260) and "When can we meet
    ///   again?" (2254, Archery 40 or not); Nimuel sending you off with a blink (75); the master tailor's work (74:
    ///   49 990 silver, the torn piece, 500 silk, 3 life motes and 10 harpy feathers for the mantle); a new ring
    ///   (77: 100 iridium ingots and 3 life motes).
    /// - A Magical Journey (16): Flame Lance cast at phase 3 passes Kromwell's test; Arthos's line once he's had
    ///   his beer; 500 Elemental grimoire experience at the end.
    /// - Joining the Hunters Guild (35): showing the Porter your insignia (78) opens the guild's door for the session;
    ///   without it the door sends you to him. A member who lost the insignia hunts the bear again for a new one.
    /// - Commander Grant's gloves (34): his line once the job is done (the gloves themselves: ItemCombos).
    /// </summary>
    public partial class OfflineServer
    {
        private static readonly int[] Bandages = { 10367, 10368, 10369, 10370, 10443, 10444 };

        private bool RunQuestScriptAction(int id)
        {
            switch (id)
            {
                case 78:   // Hunters Guild: the Porter sees the insignia; the door opens
                    huntersGuildOpen = true;
                    return true;
                case 79:   // the Porter's "go kill that bear": the option's own questupdate (35,2) does it
                    return true;
                case ActionHuntersGuildTip:   // finishing The Lone Hunter points a non-member to the guild
                    if (character.GetQuestPhase(QuestHuntersGuild) == 0) SetQuestPhase(QuestHuntersGuild, 1);
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
                case 77:   // Nimuel builds a new Ring of the Monastery
                    if (CountItem(10500) >= 3 && CountItem(10418) >= 100)
                    {
                        TakeItems(10500, 3);
                        TakeItems(10418, 100);
                        GiveItem(434);
                        if (dialogueNpc != null) NpcSay(dialogueNpc, "There you go bud. A brand new ring of the monastery!");
                    }
                    else if (dialogueNpc != null)
                        NpcSay(dialogueNpc, "Come back when you have the materials.");
                    return true;
            }
            return false;
        }

        private const int QuestMagicalJourney = 16, QuestHuntersGuild = 35, QuestGrantsGloves = 34;
        private const int ActionHuntersGuildTip = 1036, Porter = 330;
        private const int ItemInsignia = 10730;
        private bool huntersGuildOpen;   // the original's canEnterHuntersGuild: until the game is closed

        // Quests the original let you take up again after finishing: the Hunters Guild, for a lost insignia.
        private static readonly HashSet<int> ReopenableQuests = new HashSet<int> { QuestHuntersGuild };

        // Experience the original's QuestRewards gave in code, with no line in the quest's completion text.
        private static readonly Dictionary<int, KeyValuePair<int, int>> QuestXpInCode = new Dictionary<int, KeyValuePair<int, int>>
        {
            { QuestMagicalJourney, new KeyValuePair<int, int>(SkillElementalGrim, 500) },
        };

        /// <summary>A Magical Journey: casting Flame Lance at phase 3 is Kromwell's test (Scr_QuestHandler.UseAbility).</summary>
        private void OnAbilityCastAt(NpcEntity target, AbilityInfo a)
        {
            if (a.id != AbilityFlameLance || character.GetQuestPhase(QuestMagicalJourney) != 3) return;
            SetQuestPhase(QuestMagicalJourney, 4);
        }

        /// <summary>The Hunters Guild's door, until the Porter has seen your insignia: he has a word with you instead.</summary>
        private bool HuntersGuildDoorShut(DoorDef entrance)
        {
            if (huntersGuildOpen || entrance.note != "hunters guild") return false;
            var porter = world?.npcs.Find(n => n.typeId == Porter && !n.dead);
            if (porter != null) InteractNpc(porter.uid);
            else Notice("The door is locked.", "gray");
            return true;
        }

        private static readonly string[] MageLines =
        {
            "Greetings.", "I should be studying.", "Horatius said I may go up in rank soon.", "...", "Bugger off, I'm busy.",
            "Hello!", "I need a new staff.", "Something really stinks.", "Wow, they really are just letting anyone join now.",
            "Welcome.", "Can't wait to try out some new spells!", "What element do you prefer?",
            "They should have brought me along to study ancient ruins.", "Lovely weather, eh?",
            "Those wild elementals play a bit rough.", "Ugh, I HATE studying earth magic.",
            "Are you nervous about the upcoming examination?", "Why are they flooding our ranks with new people?",
            "I can't believe they decided to open the guild up to the public.",
            "I don't feel safe anymore since they removed the invisiblity spell on the front gate.", "ANOTHER new person?",
            "Get away, you smell bad.", "I need some new robes.", "I should have been a carpenter instead...",
            "I haven't been home since they closed the portals to the mainland.", "Kromwell is such an entitled prick...",
            "Can you imagine how dull the life of an ent must be?", "No fair! I want a familiar!", "Whoa, where did you get that?",
            "Practice, practice, practice.", "I really wish they'd let us duel each other here.",
        };

        private static readonly string[] GlaciesLines =
        {
            "Watch out down there. She won't attack unless you do so first.",
            "We use her for her essence of ice since other sources require travel to dangerous lands.",
            "Shes a lot bigger than the other ones, isn't she?",
            "You'll have to knock her out to harvest her, so be careful.",
        };

        /// <summary>NPCs the original answered with a line over their head instead of a conversation. True if it did.</summary>
        private bool QuestNpcLine(NpcEntity npc)
        {
            switch (npc.typeId)
            {
                case 110:   // Arthos, once you've brought him his beer
                {
                    int phase = character.GetQuestPhase(QuestMagicalJourney);
                    if (phase == 0 || phase == 1) return false;
                    NpcSay(npc, "Head over to the west ridge and there you'll find a giant gate that leads to the Mages guild. Good luck kid, and say hi to Kromwell for me.");
                    return true;
                }
                case 329:   // Commander Grant, paid
                    if (character.GetQuestPhase(QuestGrantsGloves) != -1) return false;
                    NpcSay(npc, "Can't you see that I'm busy?");
                    return true;
                case 384:   // Glacies, by the ice elemental in the Mages guild
                    NpcSay(npc, GlaciesLines[Random.Range(0, GlaciesLines.Length)]);
                    return true;
                case 378: case 379: case 380:   // the Mages guild's initiates, apprentices and scribes
                    NpcSay(npc, MageLines[Random.Range(0, MageLines.Length)]);
                    return true;
            }
            return false;
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
