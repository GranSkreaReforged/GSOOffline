using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Odd items that did something of their own when used, as the original server's Scr_PlayerInventory.UseItem
    /// did it (books, recipe scrolls, the party blower, the self-stabbing knife, the explosive potion and black
    /// candy...), and the two objects that hand out such items: Kombra's book in Yorkhill library and the treasure
    /// chest in Bal Sardan with the Argi blueprint.
    /// </summary>
    public partial class OfflineServer
    {
        private const int InteractKombrasBook = 60, InteractTreasureChest = 71;
        private const int ItemKombrasBook = 10610, ItemArgiBlueprint = 10652;
        private const int SceneBalSardan = 7;
        private static readonly Vector3 ArgiChest = new Vector3(1123f, 20f, 97f);
        private const int ObjExplosive = 2, ExplosiveUidBase = 7500000;
        private const int GfxExplosion = 73, SfxExplosion = 219, GfxPartyBlower = 154, SfxPartyBlower = 430, SfxPageTurn = 406;
        private const int AlchemySkill = 8;

        // Books (the item data's Book stat) and their pages, from the original Scr_Books. Book 2 was a test book and
        // book 4 (The fundamentals of upgrading) came after the archived server, so neither has text.
        private class Book
        {
            public string title;
            public string[] pages;
        }

        private static readonly Dictionary<int, Book> Books = new Dictionary<int, Book>
        {
            { ItemKombrasBook, new Book { title = "Flora of Keshk", pages = new[] {
                "Fibervine\n\n\nFibervine is a naturally growing plant on the island of Keshk, so far only found in a few remote locations. It is an extremely fast growing plant that produces several high-fiber fruits. Fibervine grows as a vine usually on trees and large bushes. Despite only found naturally in Keshk, our experiments have proven that with proper treatment it is possible to grow Fibervine in nearly any climate.\n\nIn this research 20 Fibervine seeds were planted and treated using different techniques on the East coast of Athagos. \n",
                "The best quality and fastest growing time from seed to fully grown plant of roughly 36 hours was achieved by using an 8-phase treatment plan.\n\nEach phase lasted for roughly 4.5 hours and consisted of different treatments such as watering and using different fertilizers. The best technique is described here, details of the other experiments can be found in Appendix 9.\n\nFailing to treat the plant according to the plan resulted in noticeable decline in the plant's health.",
                "Fibervine\n\n\n\nPhase 1. Water (3 buckets)\nPhase 2. Water (1 bucket)\nPhase 3. Red Fertilizer (2 buckets)\nPhase 4. -\nPhase 5. Red Fertilizer (2 buckets)\nPhase 6. Block sunlight (optional)\nPhase 7. -\nPhase 8. Water (1 bucket)",
                "Required fertilizer: Red fertilizer\n\nCan be produced using an alchemists' workbench. \n- 10 tomatoes\n- 5 carrots\n- 25 redberries",
            } } },
            { 10654, new Book { title = "Goldon Chronicle", pages = new[] {
                "Goldon 1100,\n\nPalian government had reformed the wild island Goldon, and turned it to an exiled land where they banished all the criminals to. The government wanted to maintain the perfect image of a peaceful and wealthy empire, but they also need to prepare for war. With the strict hierarchy they have set their country, they didn’t have enough labor force to work in remote and dangerous areas for rare resources.\nThus, Goldon was created. \n",
                "Goldon 1150,\n\nEven with the Golem problems, the government still managed to have enough exiles to work on the island. Any petite crimes without enough payment fee could bring the citizens to the island and the living conditions on the island was so horrible that not many of them could survive for 4 years. To the exiles, it was no different than a living hell. \n\nThe mage officials on Goldon started to use the exiles for their magic experiments. Palian government slowly lost control over the island. \n",
                "Goldon 1200,\n\nAs the Palian government power weakened on the island, the exiles were preparing to riot. However, before they could take over the place, an official mage, Macina Allerton, had turned Goldon along with Gran Skrea upside down. \n\nEos had promised Macina the Great Wizard Council position in Gran Skrea along with endless resources for his research after they won the war. So he performed a ritual that sacrificed many of the exiles to weaken the protection barrier between Gran Skrea and the Revenant world.\n",
                "Palian government soon found  out about his treachery and dispatched a group of powerful sorcerers and warriors led by the head monk, St.Jakob to stop Macina’s plan. \n\nHowever, before the group could reach Macina, they had to face a horrendous army of exiles, wardens and officials that binded into blood pacts with Macina, under the command of his most loyal henchman, Olafsson Hjorth.\n\nThe battle took place for a couple of days and only ended when both Olafsson and St.Jakob died together. \n",
                "Macina finished the ritual and help Eos rip the first rift in Goldon, opening the door for Revenant Army to the human world. ",
            } } },
        };

        // Recipe scrolls and blueprints: the original's message on learning. Offline these recipes were never locked,
        // so there is nothing to learn and no "already learned"; the scroll is used up as it was (except the mantle's
        // instructions, which the original kept).
        private class RecipeScroll
        {
            public string message;
            public bool consume = true;
        }

        private static readonly Dictionary<int, RecipeScroll> RecipeScrolls = new Dictionary<int, RecipeScroll>
        {
            { 10548, new RecipeScroll { message = "You study the scroll carefully... You can now make gunpowder!" } },
            { 10660, new RecipeScroll { message = "You study the instructions carefully... You have learned how to reconstruct the mantle of St. Jakob!", consume = false } },
            { 10650, new RecipeScroll { message = "You study the blueprint carefully... You can now build a longboat!" } },
            { ItemArgiBlueprint, new RecipeScroll { message = "You study the blueprint carefully... You can now craft... something!... whatever this thing is" } },
        };

        private int nextExplosiveUid;

        /// <summary>Using one of these items: true if it was one.</summary>
        private bool TryUseCuriosity(int typeId)
        {
            var player = LocalPlayer;
            if (player == null) return false;
            Vector3 me = player.transform.position;
            if (RecipeScrolls.TryGetValue(typeId, out var scroll))
            {
                if (scroll.consume) TakeItems(typeId, 1);
                Notice(scroll.message, "green");
                return true;
            }
            if (typeId == ItemKombrasBook)
            {
                // Skimming to the Fibervine chapter, a page turn at each line, then the book opens.
                Later(0.25f, () => SkimLine("You skim through the book..."));
                Later(1f, () => SkimLine("... It's incredibly boring ..."));
                Later(2f, () => SkimLine("... You finally find the part about Fibervine"));
                Later(2.5f, () => OpenBook(Books[ItemKombrasBook]));
                return true;
            }
            if (Books.TryGetValue(typeId, out var book))
            {
                OpenBook(book);
                return true;
            }
            switch (typeId)
            {
                case 10386:   // Self-stabbing knife
                    if (PlayerDead) return true;
                    Send(2, 0, string.Empty, character.name + ": Ouch!", 0);
                    Notice("Without a second thought you stab yourself in the stomach. It hurts.", "white");
                    HurtPlayer(Mathf.RoundToInt(character.currentHealth * 0.15f), SplashPlain);
                    return true;
                case 10636:   // Party blower
                    Notice("Happy 1 year anniversary!", "white");
                    PlayEffect(GfxPartyBlower, me);
                    PlaySound(SfxPartyBlower, me, 25);
                    return true;
                case 10488:   // Explosive potion
                    TakeItems(typeId, 1);
                    PlaceExplosive(me);
                    Notice("You place the potion on the ground and it starts fizzling.", "white");
                    return true;
                case 10684:   // Black candy
                    TakeItems(typeId, 1);
                    PlaceExplosive(me);
                    Notice("You quickly spit out the candy after it starts fizzling in your mouth.", "white");
                    return true;
                case 10700:   // Talking pumpkin
                    TalkingPumpkin();
                    return true;
                case 10501:   // Life potion: for other players
                    Notice("There is nobody nearby to resurrect.", "white");
                    return true;
                case 10367:   // Wool bandage
                    Notice("To use bandages you need the bandage heal ability (Open the skills menu by pressing K, and under Healing skill find 'Wool bandage heal' ability and drag it to an empty ability slot)", "white");
                    return true;
            }
            return false;
        }

        private void SkimLine(string text)
        {
            Notice(text, "white");
            var player = LocalPlayer;
            if (player != null) PlaySound(SfxPageTurn, player.transform.position, 20);
        }

        /// <summary>7/45 opens the book window with a title, then 7/44 adds each page.</summary>
        private void OpenBook(Book book)
        {
            Send(7, 45, book.title);
            foreach (string page in book.pages) Send(7, 44, page);
        }

        /// <summary>
        /// The explosive (the landmine prop, spawned object 2) goes off after 3 seconds: fire within 6 m, as in the
        /// original. Creatures take a blow of the player's alchemy (<see cref="CombatRules.ExplosionHitPower"/>) and
        /// turn on them; the player, if still standing there, a share of their health.
        /// </summary>
        private void PlaceExplosive(Vector3 pos)
        {
            int uid = ExplosiveUidBase + nextExplosiveUid++ % 1000;
            Send(2, 35, character.name, uid + "_" + ObjExplosive + "_" + SceneWorld.Vec(pos) + "_" + UnityEngine.Random.Range(0, 360));
            Later(CombatRules.ExplosiveFuse, () => Explode(uid, pos));   // a zone change calls it off
        }

        private void Explode(int uid, Vector3 pos)
        {
            Send(25, 10, uid);
            PlayEffect(GfxExplosion, pos);
            PlaySound(SfxExplosion, pos, 40);
            int max = CombatRules.PlayerMaxHit(GearDamage(), SkillLevel(AlchemySkill));
            int hits = 0;
            if (world != null)
                foreach (var n in world.npcs.ToArray())
                {
                    if (n.dead || !CanFight(n) || Vector3.Distance(NpcPosition(n), pos) > CombatRules.ExplosionRadius) continue;
                    int damage = Mathf.Max(1, Mathf.RoundToInt(max * CombatRules.ExplosionHitPower * UnityEngine.Random.Range(0.8f, 1f) * DamageDealtMultiplier()));
                    Aggro(n);
                    DealDamage(n, damage, WeaponSkill(EquippedWeapon()));
                    hits++;
                }
            var player = LocalPlayer;
            if (player != null && Vector3.Distance(player.transform.position, pos) <= CombatRules.ExplosionRadius)
            {
                float res = CombatRules.PlayerResistance(GearStat(DamageKinds.ResistanceStat(DamageKinds.Fire)));
                HurtPlayer(Mathf.RoundToInt(MaxHealth() * CombatRules.ExplosionSelfShare * (1f - res)), SplashPlain);
            }
            Plugin.Log.LogInfo($"[item] explosion at {SceneWorld.Vec(pos)}: {hits} creatures hit (max {max} x {CombatRules.ExplosionHitPower})");
        }

        /// <summary>Kombra's book on the library shelf and the Bal Sardan treasure chest. True if handled here.</summary>
        private bool HandleItemObjectInteract(int typeId)
        {
            var player = LocalPlayer;
            if (player == null) return false;
            if (typeId == InteractKombrasBook)
            {
                if (CountItem(ItemKombrasBook) > 0) Notice("You already have this book.", "white");
                else
                {
                    GiveItem(ItemKombrasBook);
                    Notice("You take the book.", "white");
                }
                return true;
            }
            if (typeId == InteractTreasureChest && character.scene == SceneBalSardan
                && Vector3.Distance(player.transform.position, ArgiChest) < 10f)
            {
                if (CountItem(ItemArgiBlueprint) > 0) Notice("The chest is empty", "white");
                else
                {
                    GiveItem(ItemArgiBlueprint);
                    Notice("What's this?", "white");
                }
                return true;
            }
            return false;
        }
    }
}
