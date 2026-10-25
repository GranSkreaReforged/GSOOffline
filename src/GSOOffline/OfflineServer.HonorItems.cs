using System.Collections.Generic;

namespace GSOOffline
{
    /// <summary>
    /// The honor and darkness items without honor and darkness (#125). The original moved both meters with PvP kills
    /// and the Salvation and Despair potions, and gated the mitres, mantles, Yin and Yang and the title guards behind
    /// them; alone there is no one to fight for them. So the gear is plain gear (crafted, or the mitres dropped by the
    /// Elder Ent and Zalranaz), its tooltips lose the requirement, and the potions give 30 minute combat blessings:
    /// Salvation the Archbishop's Typhon's Blessing and Despair the Archmagus's Eos's Favor, the title buffs the
    /// original had written but never switched on (Scr_Achievements), with Buffs.xml's numbers.
    /// </summary>
    public partial class OfflineServer
    {
        private const int ItemPotionOfSalvation = 10639, ItemSmallPotionOfSalvation = 10640, ItemPotionOfDespair = 10641;
        private const float HonorPotionSeconds = 30 * 60f;
        private const float NeutralHonor = 100f;   // 198/9 honor and 198/8 darkness: the client reads a float

        // Ids the client's Buffs.xml doesn't use, registered at runtime: its own Typhon's Blessing and Eos's Favor
        // (97, 98) hide their timer, as passives, and have no icon texture. Drawn with Block's radiant blue, Survivor's
        // heart and Disperse's crimson.
        private const int BuffSalvation = 122, BuffSmallSalvation = 123, BuffDespair = 124;
        private const int BuffSalvationLook = 16, BuffSmallSalvationLook = 10, BuffDespairLook = 11;

        private static Potion HonorPotion(int buff, BuffEffect effect) =>
            new Potion { buff = buff, seconds = HonorPotionSeconds, effect = effect, gfx = 83, sfx = 65 };

        private static BuffEffect TyphonsBlessing() => new BuffEffect
        {
            stats = new Dictionary<string, int> { { "Health", 500 }, { "Healthregen", 50 }, { "Healing", 50 } },
            healingTaken = 1.05f,
        };

        private static BuffEffect SmallSalvation() => new BuffEffect { stats = new Dictionary<string, int> { { "Healthregen", 25 } } };

        private static BuffEffect EosFavor() => new BuffEffect
        {
            stats = new Dictionary<string, int> { { "Mana", 50 }, { "Manaregen", 5 }, { "Damage", 50 } },
            hitDamage = 1.05f,
        };

        // What the tooltips lose: the title or meter they asked for (the whole text where that was all of it).
        private static readonly Dictionary<int, string> HonorRequirementTexts = new Dictionary<int, string>
        {
            { 419, "Requires Bishop title to wear." },                                // Bishop's mitre
            { 420, "Requires Paladin title to wear. " },                              // Mantle of St. Jakob
            { 421, "Requires Magus title to wear." },                                 // Dark mitre
            { 422, "Requires Berserk title so wear. " },                              // Mantle of Olafsson, son of Olaf
            { 423, "Can only be worn with 91-109 honor and darkness. " },             // Yin and Yang
            { 424, "Can only be worn with 110+ darkness. " },                         // Yin
            { 425, "Can only be worn with 110+ honor. " },                            // Yang
            { 430, "Only the Archbishop has the right to command the Archbishop's Guard" },
            { 431, "Only the Archmagus has the right to command the Archmagus's Guard" },
        };

        private static readonly Dictionary<int, string> HonorPotionTexts = new Dictionary<int, string>
        {
            { ItemPotionOfSalvation, "This potion is brimming with holy energy! Grants Typhon's Blessing for 30 minutes: +500 health, "
                + "+50 health regeneration, +50 healing and 5% more healing received." },
            { ItemSmallPotionOfSalvation, "You get a warm, soothing feeling holding the potion in your hands. "
                + "+25 health regeneration for 30 minutes." },
            { ItemPotionOfDespair, "A dark, murky glow is surrounding the potion. You have a bad premonition about this. Grants Eos's "
                + "Favor for 30 minutes: +50 mana, +5 mana regeneration, +50 damage and 5% more damage dealt." },
        };

        /// <summary>A buff with stats began or ended: max health and mana may have moved, so both are capped and sent.</summary>
        private void VitalStatsChanged()
        {
            if (character == null) return;
            HealthStatsChanged();
            ManaStatsChanged();
        }

        private bool honorTextsApplied;

        /// <summary>On entering the world: the potions' buffs, and the item texts in the client's templates (tooltips and shops read them).</summary>
        private void RegisterHonorItems()
        {
            RegisterRuntimeBuff(BuffSalvation, "Typhon's Blessing", BuffSalvationLook,
                "+500 health, +50 health regeneration, +50 healing, 5% more healing received.");
            RegisterRuntimeBuff(BuffSmallSalvation, "Salvation", BuffSmallSalvationLook, "+25 health regeneration.");
            RegisterRuntimeBuff(BuffDespair, "Eos's Favor", BuffDespairLook, "+50 mana, +5 mana regeneration, +50 damage, 5% more damage dealt.");
            // The character window's Honor and Darkness lines: everyone's starting 100%, which nothing moves offline.
            Send(198, 9, NeutralHonor);
            Send(198, 8, NeutralHonor);

            var h = Scr_ItemHandler.instance;
            if (honorTextsApplied || h == null) return;
            honorTextsApplied = true;
            foreach (var kv in HonorRequirementTexts)
            {
                var t = h.GetItemTemplate(kv.Key);
                if (t?.itemDescription == null) continue;
                if (t.itemDescription.StartsWith(kv.Value)) t.itemDescription = t.itemDescription.Substring(kv.Value.Length);
                else Plugin.Log.LogWarning($"[items] {t.itemName} ({kv.Key}): its text no longer starts with the requirement");
            }
            foreach (var kv in HonorPotionTexts)
            {
                var t = h.GetItemTemplate(kv.Key);
                if (t != null) t.itemDescription = kv.Value;
            }
        }
    }
}
