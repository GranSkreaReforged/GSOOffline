using System.Collections.Generic;

namespace GSOOffline
{
    /// <summary>
    /// The unique gear effects of the original server's last version (its uniqueItems.WearsItem checks, spread over
    /// Scr_PlayerAttacksHandler, Scr_Projectile, Scr_PlayerHandler, Scr_PlayerBuffs and Scr_PlayerPets). The class
    /// that mapped the keys to items wasn't in the snapshot; each key names its item, matched here by type id.
    /// Effects of a piece apply while it's worn or in hand.
    /// </summary>
    public partial class OfflineServer
    {
        private static readonly Dictionary<string, int> UniqueItemIds = new Dictionary<string, int>
        {
            { "huntersBow", 432 }, { "huntersBody", 461 }, { "huntersLegs", 462 },
            { "brunosBracers", 451 }, { "brunosBoots", 452 },
            { "storageGuardsBody", 453 }, { "storageGuardsChaps", 454 }, { "storageGuardsHood", 455 }, { "storageGuardsBoots", 456 },
            { "storageGuardsBracelet", 457 }, { "storageGuardsQuiver", 458 }, { "storageGuardsCrossbow", 459 },
            { "zalranazsCleaver", 463 }, { "tombGuardiansAmulet", 465 },
            { "lydiasEarrings", 474 }, { "rokesRing", 475 },
            { "li", 481 }, { "jin", 482 }, { "barrage", 484 }, { "thorn", 485 },
            { "jotnarsBody", 486 }, { "jotnarsChaps", 487 },
            { "venomousQuiver", 404 },
            { "darkTamerBody", 414 }, { "darkTamerLegs", 415 }, { "darkTamerBracers", 416 }, { "darkTamerBoots", 417 }, { "darkTamerHood", 418 },
            { "naturesBlessingHelmet", 497 }, { "naturesBlessingBody", 498 }, { "naturesBlessingLegs", 499 },
            { "naturesBlessingBoots", 500 }, { "naturesBlessingGloves", 501 },
        };

        private const int BerserkersHelmet = 471, BerserkersAmulet = 472;

        private bool Wears(string key)
        {
            if (character == null || !UniqueItemIds.TryGetValue(key, out int typeId)) return false;
            foreach (var it in character.items)
                if (it.equipped && it.typeId == typeId) return true;
            return false;
        }

        private bool WearsType(int typeId)
        {
            if (character == null) return false;
            foreach (var it in character.items)
                if (it.equipped && it.typeId == typeId) return true;
            return false;
        }

        /// <summary>The Storage guard's set: each piece adds to explosive arrows and landmines (the crossbow and body 30%).</summary>
        private float StorageGuardSetMultiplier()
        {
            float m = 1f;
            if (Wears("storageGuardsBody")) m += 0.3f;
            if (Wears("storageGuardsChaps")) m += 0.225f;
            if (Wears("storageGuardsHood")) m += 0.15f;
            if (Wears("storageGuardsBoots")) m += 0.075f;
            if (Wears("storageGuardsBracelet")) m += 0.075f;
            if (Wears("storageGuardsQuiver")) m += 0.1125f;
            if (Wears("storageGuardsCrossbow")) m += 0.3f;
            return m;
        }

        /// <summary>The Dark Tamer's set: pets and summons hit harder (up to 22%).</summary>
        private float DarkTamerPetMultiplier()
        {
            float m = 1f;
            if (Wears("darkTamerBody")) m += 0.08f;
            if (Wears("darkTamerLegs")) m += 0.06f;
            if (Wears("darkTamerBracers")) m += 0.02f;
            if (Wears("darkTamerBoots")) m += 0.02f;
            if (Wears("darkTamerHood")) m += 0.04f;
            return m;
        }

        /// <summary>The Dark Tamer's set: summoning takes less of your mana (up to 11%).</summary>
        private float DarkTamerSummonCost()
        {
            float m = 1f;
            if (Wears("darkTamerBody")) m -= 0.04f;
            if (Wears("darkTamerLegs")) m -= 0.03f;
            if (Wears("darkTamerBracers")) m -= 0.01f;
            if (Wears("darkTamerBoots")) m -= 0.01f;
            if (Wears("darkTamerHood")) m -= 0.02f;
            return m;
        }

        /// <summary>Nature's blessing: each piece lowers the healing you receive (regeneration is untouched), up to 55%.</summary>
        private float NaturesBlessingHealing()
        {
            float m = 1f;
            if (Wears("naturesBlessingHelmet")) m -= 0.012f;
            if (Wears("naturesBlessingBody")) m -= 0.24f;
            if (Wears("naturesBlessingLegs")) m -= 0.18f;
            if (Wears("naturesBlessingBoots")) m -= 0.06f;
            if (Wears("naturesBlessingGloves")) m -= 0.06f;
            return m;
        }

        /// <summary>Strength and Charge last longer with each Berserker's piece worn (by perPiece seconds).</summary>
        private float BerserkerDuration(float seconds, float perPiece)
        {
            if (WearsType(BerserkersHelmet)) seconds += perPiece;
            if (WearsType(BerserkersAmulet)) seconds += perPiece;
            return seconds;
        }
    }
}
