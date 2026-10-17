using System.Collections.Generic;

namespace GSOOffline
{
    /// <summary>
    /// Crates opened from the bag, with what the original server's Scr_PlayerInventory.UseItem put in them.
    /// A gear crate gives one piece of its tier's gear (each equally likely), and another one in four times, over
    /// and over; the Guard pet is a very rare extra. The Christmas 2019 crate gives Christmas food the same way,
    /// a chance at each piece of Christmas furniture and the Santa hat, and the untradeable Reindeer once, more
    /// likely with every crate opened until it comes.
    /// </summary>
    public partial class OfflineServer
    {
        private const int PetGuard = 446, CrateSound = 74;

        private class GearCrate
        {
            public int[] gear;
            public int guardOneIn;
        }

        private static readonly Dictionary<int, GearCrate> GearCrates = new Dictionary<int, GearCrate>
        {
            {   // Iron: iron plate and weapons, cow leather, wool robes, spruce bow, lute, crossbow and fire staff
                10668, new GearCrate { guardOneIn = 30000, gear = new[] {
                    60, 61, 62, 81, 83, 183, 191, 294, 296, 297, 167, 168, 19, 46, 3, 6, 1, 80, 50, 199 } }
            },
            {   // Steel, crawler leather, cotton, oak
                10669, new GearCrate { guardOneIn = 26000, gear = new[] {
                    63, 64, 65, 82, 71, 187, 192, 295, 299, 298, 108, 107, 201, 16, 218, 9, 74, 370, 75, 268 } }
            },
            {   // Verum, moose leather, silk, evark
                10670, new GearCrate { guardOneIn = 11000, gear = new[] {
                    231, 252, 259, 260, 261, 150, 97, 188, 204, 286, 287, 285, 169, 170, 202, 195, 219, 194, 114, 371, 121, 271 } }
            },
            {   // Stranite, bear leather, jute, pertica
                10671, new GearCrate { guardOneIn = 9000, gear = new[] {
                    300, 302, 303, 304, 305, 190, 184, 189, 306, 282, 283, 284, 110, 109, 203, 344, 345, 343, 334, 372, 337, 340 } }
            },
            {   // Shadow, troll leather, flax, deadwood
                10672, new GearCrate { guardOneIn = 7000, gear = new[] {
                    308, 310, 311, 312, 313, 325, 326, 328, 314, 278, 281, 280, 277, 276, 279, 347, 348, 346, 335, 373, 338, 341 } }
            },
            {   // Iridium, dragon leather, ramie, archaeopteris
                10673, new GearCrate { guardOneIn = 5000, gear = new[] {
                    316, 318, 319, 320, 321, 330, 331, 333, 322, 290, 293, 292, 289, 288, 291, 350, 351, 349, 336, 374, 339, 342 } }
            },
            {   // Sardanian weapon crate (lizard warriors): spear, buckler shield, axe
                10701, new GearCrate { guardOneIn = 6500, gear = new[] { 376, 377, 378 } }
            },
        };

        private const int ChristmasCrate = 10723, ReindeerUntradeable = 449;

        // Christmas food out of 44: golden candy cane 1, candy cane, gingerbread cookies, figgy pudding and
        // fruitcake 10 each, eggnog 3.
        private static readonly int[][] ChristmasFood =
        {
            new[] { 10722, 1 }, new[] { 10717, 10 }, new[] { 10720, 10 }, new[] { 10719, 10 }, new[] { 10718, 10 }, new[] { 10721, 3 },
        };

        // Each a chance of one in n: the Santa hat, the furniture, and the tradeable Reindeer.
        private static readonly int[][] ChristmasExtras =
        {
            new[] { 448, 75 }, new[] { 10702, 35 }, new[] { 10703, 35 }, new[] { 10704, 50 }, new[] { 10705, 75 },
            new[] { 10706, 50 }, new[] { 10707, 50 }, new[] { 10708, 50 }, new[] { 10709, 50 }, new[] { 10710, 50 },
            new[] { 10711, 50 }, new[] { 10712, 300 }, new[] { 10713, 250 }, new[] { 10714, 75 }, new[] { 10715, 75 },
            new[] { 10716, 150 }, new[] { 447, 1000 },
        };

        /// <summary>Opens a crate used from the bag; false if the item isn't one.</summary>
        private bool TryOpenCrate(int typeId)
        {
            bool gear = GearCrates.TryGetValue(typeId, out var crate);
            if (!gear && typeId != ChristmasCrate) return false;
            if (PlayerDead) return true;
            TakeItems(typeId, 1);
            var player = LocalPlayer;
            if (player != null) PlaySound(CrateSound, player.transform.position, 20);
            Plugin.Log.LogInfo($"[item] opening {ItemData.Get(typeId)?.name}");

            if (gear)
            {
                do GiveFromCrate(crate.gear[UnityEngine.Random.Range(0, crate.gear.Length)]);
                while (OneIn(4));
                if (OneIn(crate.guardOneIn)) GiveFromCrate(PetGuard);
                return true;
            }

            do GiveFromCrate(Weighted(ChristmasFood));
            while (OneIn(3));
            // The untradeable Reindeer: 1 in 500 with the first crate, 1 in 499 with the next..., then never again.
            if (character.christmasPetCounter >= 0)
            {
                if (OneIn(500 - character.christmasPetCounter))
                {
                    character.christmasPetCounter = -1;
                    GiveFromCrate(ReindeerUntradeable);
                }
                else character.christmasPetCounter++;
            }
            foreach (var e in ChristmasExtras)
                if (OneIn(e[1])) GiveFromCrate(e[0]);
            return true;
        }

        private static int Weighted(int[][] table)
        {
            int total = 0;
            foreach (var t in table) total += t[1];
            int roll = UnityEngine.Random.Range(0, total);
            foreach (var t in table)
            {
                if (roll < t[1]) return t[0];
                roll -= t[1];
            }
            return table[0][0];
        }

        private void GiveFromCrate(int typeId)
        {
            GiveItem(typeId);
            Notice($"+ 1 x {ItemData.Get(typeId)?.name}", "green");
        }
    }
}
