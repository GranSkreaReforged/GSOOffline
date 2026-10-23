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
            public int health, healthTo, mana, manaTo;   // restored: a roll from the first up to (not including) the second
            public int buff;          // client buff icon
            public float seconds;
            public BuffEffect effect;
            public bool once;         // no new buff (or its effects) while one is on: the speed treats
            public Potion also;       // a second buff from the same item
            public string line;       // seasonal treats: "You eat the X." and this line
            public bool drink;
            public int gfx, gfx2, sfx;   // effects on the player and a sound
        }

        // The original's usePotion (update 136); the health boost potion came later and is a reconstruction.
        private static readonly Dictionary<int, Potion> Potions = new Dictionary<int, Potion>
        {
            { 10067, new Potion { health = 40, healthTo = 60 } },     // Weak health potion
            { 10068, new Potion { health = 140, healthTo = 200 } },   // Health potion
            { 10069, new Potion { health = 300, healthTo = 360 } },   // Strong health potion
            { 10093, new Potion { mana = 20, manaTo = 30 } },         // Weak mana potion
            { 10454, new Potion { mana = 80, manaTo = 130 } },        // Mana potion
            { 10456, new Potion { buff = 31, seconds = 120f, effect = new BuffEffect { manaPerSecond = 3f } } },   // Mana regeneration potion
            { 10482, new Potion { buff = 10, seconds = 120f, effect = new BuffEffect { bonusHealthPercent = 20 } } },   // Health boost potion
            { 10484, new Potion { buff = 45, seconds = 1200f, effect = new BuffEffect { craftTime = 0.75f }, gfx = 83, sfx = 65 } },   // Crafting speed potion: 25%
            { 10485, new Potion { buff = 46, seconds = 1200f, effect = new BuffEffect { gatherTime = 0.75f }, gfx = 84, sfx = 65 } },  // Gathering speed potion: 25%
            // Resistance potions: +15 to their kind (BuffResistance), poison, fire, earth, wind, electric, psychic,
            // ice, slash, piercing, blunt.
            { 10433, Timed(17, 120f) }, { 10434, Timed(18, 120f) }, { 10435, Timed(19, 120f) },
            { 10436, Timed(20, 120f) }, { 10437, Timed(21, 120f) }, { 10438, Timed(22, 120f) },
            { 10439, Timed(23, 120f) }, { 10440, Timed(24, 120f) }, { 10441, Timed(25, 120f) },
            { 10442, Timed(26, 120f) },
            // Stamina: carry too much without slowing down (Weight.cs).
            { 10446, Timed(30, 10f) }, { 10447, Timed(30, 30f) }, { 10448, Timed(30, 60f) },
            { 10483, new Potion { buff = 33, seconds = 15f, effect = new BuffEffect(), gfx = 82, sfx = 66, once = true } },   // Movement speed potion: +5 run speed
            // Halloween candy and Christmas treats (the original's usePotion). Brown candy and gingerbread cookies are
            // plain food.
            { 10681, new Potion { health = 300, healthTo = 360, line = "... it restores some health." } },   // Red candy
            { 10718, new Potion { health = 300, healthTo = 360, line = "... it restores some health." } },   // Fruitcake
            { 10683, new Potion { mana = 80, manaTo = 130, line = "... it restores some mana." } },          // Blue candy
            { 10719, new Potion { mana = 80, manaTo = 130, line = "... it restores some mana." } },          // Figgy pudding
            { 10685, new Potion { buff = 30, seconds = 30f, effect = new BuffEffect(), line = "... you feel much lighter now!" } },   // White candy: stamina
            { 10682, new Potion { buff = 33, seconds = 15f, effect = new BuffEffect(), gfx = 82, sfx = 66, once = true, line = "" } },           // Golden candy: speed
            { 10717, new Potion { buff = 33, seconds = 5f, effect = new BuffEffect(), gfx = 82, sfx = 66, once = true, line = "... wheee." } },  // Candy cane
            { 10721, new Potion { buff = 23, seconds = 120f, effect = new BuffEffect(), drink = true,                               // Eggnog
                also = new Potion { buff = 31, seconds = 120f, effect = new BuffEffect { manaPerSecond = 3f } },
                line = "... you feel much warmer and a little bit dizzy" } },
            { 10722, new Potion { buff = 46, seconds = 1200f, effect = new BuffEffect { gatherTime = 0.75f }, gfx = 83, gfx2 = 84, sfx = 65,   // Golden candy cane
                also = new Potion { buff = 45, seconds = 1200f, effect = new BuffEffect { craftTime = 0.75f } },
                line = "... you get the sugar rush of your lifetime!." } },
        };

        // A buff whose effect is found by its icon (BuffEffects.cs, Weight.cs).
        private static Potion Timed(int buff, float seconds) => new Potion { buff = buff, seconds = seconds, effect = new BuffEffect() };

        // Beer and wine: drunk for 30 or 60 s more; it adds up.
        private static readonly Dictionary<int, float> Alcohol = new Dictionary<int, float> { { 10310, 30f }, { 10311, 60f } };

        private const int ItemSmolderfish = 10643;
        private float nextItemUse;

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

        // Teleport scrolls to a spot rather than a wayshrine (the original's Scr_PlayerInventory positions).
        private static readonly Dictionary<int, KeyValuePair<int, Vector3>> TeleportSpots = new Dictionary<int, KeyValuePair<int, Vector3>>
        {
            { 10531, new KeyValuePair<int, Vector3>(13, new Vector3(831f, 12f, 596f)) },   // South Athagos
            { 10538, new KeyValuePair<int, Vector3>(7, new Vector3(469f, 5f, 98f)) },      // Bal Amana
        };

        // Experience scrolls (16/3 after the client's skill picker): community wiki amounts; Small isn't recorded.
        private static readonly Dictionary<int, int> ExperienceScrolls = new Dictionary<int, int>
        {
            { 10326, 100 }, { 10327, 300 }, { 10328, 1000 }, { 10329, 5000 }, { 10724, 50000 },
        };

        private void UseItem(int typeId)
        {
            if (character == null || CountItem(typeId) == 0) return;
            // The original's "Using item cooldown" (buff 28): one item a second, and none while stunned.
            if (PlayerStunned || Time.time < nextItemUse) return;
            nextItemUse = Time.time + 1f;
            if (UseDynamite(typeId)) return;   // the Poacher dungeon's rock pile
            var t = ItemData.Get(typeId);
            if (Potions.TryGetValue(typeId, out var p)) DrinkPotion(typeId, p);   // before food: fruitcake and figgy pudding are both
            else if (Alcohol.TryGetValue(typeId, out float drunk)) DrinkAlcohol(typeId, drunk);
            else if (t != null && t.food > 0) EatFood(typeId, t);
            else if (TeleportScrolls.TryGetValue(typeId, out int shrine)) ReadTeleportScroll(typeId, shrine);
            else if (TeleportSpots.TryGetValue(typeId, out var spot)) ReadTeleportScroll(typeId, spot.Key, spot.Value);
            else if (ReadHouseTeleport(typeId)) { }
            else if (TryOpenCrate(typeId) || TryOpenHalloweenCrate(typeId) || TryUseCuriosity(typeId) || TryLaunchBoat(typeId) || TryBoatTeleport(typeId)) { }
            Send(3, 24, character.name, typeId);   // client opens item-specific UI (recipe books, maps, experience scrolls...)
        }

        private void DrinkPotion(int typeId, Potion p)
        {
            if (PlayerDead) return;
            TakeItems(typeId, 1);
            if (p.health > 0) Heal(Roll(p.health, p.healthTo));
            if (p.mana > 0)
            {
                character.currentMana = Mathf.Min(MaxMana(), character.currentMana + Roll(p.mana, p.manaTo));
                SendMana();
            }
            string poisoning = p.health > 0 || p.mana > 0 ? PotionPoisoning() : string.Empty;
            bool effects = !(p.once && HasBuff(p.buff));
            if (p.effect != null && effects) AddBuff(-typeId, p.buff, p.seconds, p.effect);   // negative: item sources never clash with abilities
            if (p.also != null) AddBuff(-typeId - 100000, p.also.buff, p.also.seconds, p.also.effect);
            var player = LocalPlayer;
            bool drink = p.line == null || p.drink;
            Notice($"You {(drink ? "drink" : "eat")} the {ItemData.Get(typeId)?.name}.{poisoning}", "white");
            if (!string.IsNullOrEmpty(p.line)) Notice(p.line, "white");
            if (player != null) PlaySound(drink ? 299 : 300, player.transform.position, 30);
            if (effects)
            {
                if (player != null && p.sfx > 0) PlaySound(p.sfx, player.transform.position, 40);
                PlayPlayerEffect(p.gfx);
                PlayPlayerEffect(p.gfx2);
            }
            Plugin.Log.LogInfo($"[item] used {ItemData.Get(typeId)?.name}");
        }

        private static int Roll(int min, int to) => to > min ? Random.Range(min, to) : min;

        /// <summary>
        /// Potion poisoning (buff 4, 8 s after a potion that restores health or mana) and its warning. The original
        /// worked out a weaker dose from it only after the potion had already worked, so it never weakened one; the
        /// warning and the icon are all there is.
        /// </summary>
        private string PotionPoisoning()
        {
            float left = BuffTimeLeft(BuffPotionPoisoning);
            string warning = left > 25f ? " It has no effect! You are suffering a potion poisoning from drinking too many potions."
                : left > 15f ? " Its effect is greatly diminished because you haven't recovered form the last potion's effects."
                : left > 6f ? " Its effect is diminished because you haven't recovered form the last potion's effects."
                : string.Empty;
            if (!HasBuff(BuffPotionPoisoning)) AddBuff(-BuffPotionPoisoning, BuffPotionPoisoning, 8f, null);
            return warning;
        }

        /// <summary>
        /// The original's useFood: a Food buff (3) for as many seconds as the food's value, adding twice that to your
        /// damage and 1-1.5% of your health a second (TickRegen). Eating again while it lasts doesn't renew it.
        /// Smolderfish also makes fire heal you for 10 s.
        /// </summary>
        private void EatFood(int typeId, ItemTemplate t)
        {
            if (PlayerDead) return;
            TakeItems(typeId, 1);
            if (!HasBuff(BuffFood)) AddBuff(-BuffFood, BuffFood, t.food, null, 2 * t.food);
            if (typeId == ItemSmolderfish && !HasBuff(BuffSmolder)) AddBuff(-BuffSmolder, BuffSmolder, 10f, null);
            Notice($"You eat the {t.name}.", "white");
            PlaySound(300, PlayerPos, 30);
            Plugin.Log.LogInfo($"[item] ate {t.name}: food {t.food}");
        }

        /// <summary>Beer and wine: Drunk adds up, and the original's word on how drunk you are; past 800 s it hurts.</summary>
        private void DrinkAlcohol(int typeId, float seconds)
        {
            if (PlayerDead) return;
            TakeItems(typeId, 1);
            Notice($"You drink the {ItemData.Get(typeId)?.name.ToLowerInvariant()}.", "white");
            float drunk = BuffTimeLeft(BuffDrunk) + seconds;
            AddBuff(-BuffDrunk, BuffDrunk, drunk, null);
            if (drunk > 800f)
            {
                Notice("You're suffering from a serious alcohol poisoning.", "white");
                HurtPlayer((int)(MaxHealth() * 0.33f), SplashPlain);
            }
            else if (drunk > 360f) Notice("You're absolutely wasted.", "white");
            else if (drunk > 240f) Notice("You're very drunk.", "white");
            else if (drunk > 120f) Notice("You feel clearly drunk.", "white");
            else if (drunk > 60f) Notice("You feel a bit tipsy.", "white");
        }

        /// <summary>Teleport cooldown (buff 58, 10 s after a teleport): true, with the original's message, while it lasts.</summary>
        private bool TeleportCoolingDown()
        {
            float left = BuffTimeLeft(BuffTeleportCooldown);
            if (left <= 0f) return false;
            int s = Mathf.CeilToInt(left);
            Notice(s == 1 ? "You cannot use another teleport so soon, please wait 1 more second."
                : $"You cannot use another teleport so soon, please wait another {s} seconds.", "white");
            return true;
        }

        private void StartTeleportCooldown() => AddBuff(-BuffTeleportCooldown, BuffTeleportCooldown, 10f, null);

        private void ReadTeleportScroll(int typeId, int shrine)
        {
            if (PlayerDead) return;
            int id = shrine > 0 ? shrine : character.homeWayshrine;
            if (!GameData.Wayshrines.TryGetValue(id, out var w))
            {
                Notice("That teleport leads nowhere offline.", "gray");
                return;
            }
            Plugin.Log.LogInfo($"[item] {ItemData.Get(typeId)?.name} -> wayshrine {w.id} {w.name}");
            ReadTeleportScroll(typeId, w.scene, w.pos);
        }

        private void ReadTeleportScroll(int typeId, int scene, Vector3 pos)
        {
            if (PlayerDead || TeleportCoolingDown()) return;
            TakeItems(typeId, 1);
            StartTeleportCooldown();
            Plugin.Log.LogInfo($"[item] {ItemData.Get(typeId)?.name} -> scene {scene} {pos}");
            var player = LocalPlayer;
            if (player != null) PlayEffect(GfxTeleport, player.transform.position);
            arrivalGfx = GfxTeleportLand;
            Teleport(scene, pos);
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
