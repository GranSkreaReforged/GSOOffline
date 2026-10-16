using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Items used from the inventory (3/18): food, potions, teleport scrolls and experience scrolls (crates are in
    /// OfflineServer.Crates.cs, books, recipe scrolls and other oddities in OfflineServer.ItemUses.cs). The item data only marks them usable; what each did was server code. Experience
    /// scroll amounts come from the community wiki, the rest are reconstructions.
    /// </summary>
    public partial class OfflineServer
    {
        private class Potion
        {
            public int health, mana;
            public int buff;          // client buff icon
            public float seconds;
            public BuffEffect effect;
            public Potion also;       // a second buff from the same item
            public string line;       // seasonal treats: "You eat the X." and this line
            public bool drink;
            public int gfx, gfx2, sfx;   // effects on the player and a sound
        }

        private static readonly Dictionary<int, Potion> Potions = new Dictionary<int, Potion>
        {
            { 10067, new Potion { health = 40 } },    // Weak health potion
            { 10068, new Potion { health = 120 } },   // Health potion
            { 10069, new Potion { health = 300 } },   // Strong health potion
            { 10093, new Potion { mana = 40 } },      // Weak mana potion
            { 10454, new Potion { mana = 120 } },     // Mana potion
            { 10456, new Potion { buff = 31, seconds = 60f, effect = new BuffEffect { manaPerSecond = 3f } } },   // Mana regeneration potion
            { 10482, new Potion { buff = 10, seconds = 120f, effect = new BuffEffect { bonusHealthPercent = 20 } } },   // Health boost potion
            { 10484, new Potion { buff = 45, seconds = 300f, effect = new BuffEffect { craftTime = 0.75f } } },   // Crafting speed potion: 25%
            { 10485, new Potion { buff = 46, seconds = 300f, effect = new BuffEffect { gatherTime = 0.75f } } },  // Gathering speed potion: 25%
            // Effects the offline server doesn't model (resistances, carrying capacity, run speed): the icon only.
            { 10433, IconOnly(17, 300f) }, { 10434, IconOnly(18, 300f) }, { 10435, IconOnly(19, 300f) },
            { 10436, IconOnly(20, 300f) }, { 10437, IconOnly(21, 300f) }, { 10438, IconOnly(22, 300f) },
            { 10439, IconOnly(23, 300f) }, { 10440, IconOnly(24, 300f) }, { 10441, IconOnly(25, 300f) },
            { 10442, IconOnly(26, 300f) },
            { 10446, IconOnly(30, 20f) }, { 10447, IconOnly(30, 40f) }, { 10448, IconOnly(30, 60f) },   // stamina (wiki: strong = 60 s)
            { 10483, IconOnly(33, 15f) },   // Movement speed potion (wiki: 15 s)
            { 10310, IconOnly(13, 60f) }, { 10311, IconOnly(13, 60f) },   // beer, wine: drunk
            // Halloween candy and Christmas treats (the original's usePotion; the amounts of its potions of the same
            // strength). Brown candy and gingerbread cookies are plain food.
            { 10681, new Potion { health = 300, line = "... it restores some health." } },   // Red candy
            { 10718, new Potion { health = 300, line = "... it restores some health." } },   // Fruitcake
            { 10683, new Potion { mana = 120, line = "... it restores some mana." } },       // Blue candy
            { 10719, new Potion { mana = 120, line = "... it restores some mana." } },       // Figgy pudding
            { 10685, new Potion { buff = 30, seconds = 30f, effect = new BuffEffect(), line = "... you feel much lighter now!" } },   // White candy: stamina
            { 10682, new Potion { buff = 33, seconds = 15f, effect = new BuffEffect(), gfx = 82, sfx = 66, line = "" } },           // Golden candy: speed
            { 10717, new Potion { buff = 33, seconds = 5f, effect = new BuffEffect(), gfx = 82, sfx = 66, line = "... wheee." } },  // Candy cane
            { 10721, new Potion { buff = 23, seconds = 120f, effect = new BuffEffect(), drink = true,                               // Eggnog
                also = new Potion { buff = 31, seconds = 120f, effect = new BuffEffect { manaPerSecond = 3f } },
                line = "... you feel much warmer and a little bit dizzy" } },
            { 10722, new Potion { buff = 46, seconds = 1200f, effect = new BuffEffect { gatherTime = 0.75f }, gfx = 83, gfx2 = 84, sfx = 65,   // Golden candy cane
                also = new Potion { buff = 45, seconds = 1200f, effect = new BuffEffect { craftTime = 0.75f } },
                line = "... you get the sugar rush of your lifetime!." } },
        };

        private static Potion IconOnly(int buff, float seconds) => new Potion { buff = buff, seconds = seconds, effect = new BuffEffect() };

        // Teleport scrolls -> wayshrine (0 = the home wayshrine); the house teleport is in Housing.cs. Scrolls to
        // places that don't exist offline (guild halls, the auction house) aren't here and stay in the bag.
        private static readonly Dictionary<int, int> TeleportScrolls = new Dictionary<int, int>
        {
            { 10388, 1 },    // Yorkhill Monastery
            { 10389, 2 },    // Bal Sardan
            { 10390, 6 },    // Athagos lighthouse: the East Athagos wayshrine
            { 10395, 0 },    // Home wayshrine
            { 10457, 7 },    // Mages' guild
            { 10464, 3 },    // Yorkhill
            { 10532, 9 },    // Arena
            { 10739, 11 },   // Deepheart
        };

        // Experience scrolls (16/3 after the client's skill picker): community wiki amounts; Small isn't recorded.
        private static readonly Dictionary<int, int> ExperienceScrolls = new Dictionary<int, int>
        {
            { 10326, 100 }, { 10327, 300 }, { 10328, 1000 }, { 10329, 5000 }, { 10724, 50000 },
        };

        private void UseItem(int typeId)
        {
            if (character == null || CountItem(typeId) == 0) return;
            if (UseDynamite(typeId)) return;   // the Poacher dungeon's rock pile
            var t = ItemData.Get(typeId);
            if (Potions.TryGetValue(typeId, out var p)) DrinkPotion(typeId, p);   // before food: fruitcake and figgy pudding are both
            else if (t != null && t.food > 0)
            {
                TakeItems(typeId, 1);
                Heal(t.food);
            }
            else if (TeleportScrolls.TryGetValue(typeId, out int shrine)) ReadTeleportScroll(typeId, shrine);
            else if (ReadHouseTeleport(typeId)) { }
            else if (TryOpenCrate(typeId) || TryOpenHalloweenCrate(typeId) || TryUseCuriosity(typeId) || TryLaunchBoat(typeId) || TryBoatTeleport(typeId)) { }
            Send(3, 24, character.name, typeId);   // client opens item-specific UI (recipe books, maps, experience scrolls...)
        }

        private void DrinkPotion(int typeId, Potion p)
        {
            if (PlayerDead) return;
            TakeItems(typeId, 1);
            if (p.health > 0) Heal(p.health);
            if (p.mana > 0)
            {
                character.currentMana = Mathf.Min(MaxMana(), character.currentMana + p.mana);
                SendMana();
            }
            if (p.effect != null) AddBuff(-typeId, p.buff, p.seconds, p.effect);   // negative: item sources never clash with abilities
            if (p.also != null) AddBuff(-typeId - 100000, p.also.buff, p.also.seconds, p.also.effect);
            if (p.line != null)
            {
                Notice($"You {(p.drink ? "drink" : "eat")} the {ItemData.Get(typeId)?.name}.", "white");
                if (p.line.Length > 0) Notice(p.line, "white");
                var player = LocalPlayer;
                if (player != null) PlaySound(p.drink ? 299 : 300, player.transform.position, 30);
                if (player != null && p.sfx > 0) PlaySound(p.sfx, player.transform.position, 40);
                PlayPlayerEffect(p.gfx);
                PlayPlayerEffect(p.gfx2);
            }
            Plugin.Log.LogInfo($"[item] used {ItemData.Get(typeId)?.name}");
        }

        private void ReadTeleportScroll(int typeId, int shrine)
        {
            if (PlayerDead) return;
            int id = shrine > 0 ? shrine : character.homeWayshrine;
            if (!GameData.Wayshrines.TryGetValue(id, out var w))
            {
                Notice("That teleport leads nowhere offline.", "gray");
                return;
            }
            TakeItems(typeId, 1);
            Plugin.Log.LogInfo($"[item] {ItemData.Get(typeId)?.name} -> wayshrine {w.id} {w.name}");
            var player = LocalPlayer;
            if (player != null) PlayEffect(GfxTeleport, player.transform.position);
            arrivalGfx = GfxTeleportLand;
            Teleport(w.scene, w.pos);
        }

        /// <summary>16/3 from the experience scroll window: skill id, scroll type id.</summary>
        private void UseExperienceScroll(int skillId, int scroll)
        {
            if (character == null || !ExperienceScrolls.TryGetValue(scroll, out int xp) || CountItem(scroll) == 0) return;
            if (!SkillById.ContainsKey(skillId)) return;
            TakeItems(scroll, 1);
            AddXp(skillId, xp);
            Plugin.Log.LogInfo($"[item] {ItemData.Get(scroll)?.name}: {xp} XP in {SkillById[skillId]}");
        }
    }
}
