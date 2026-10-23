using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Combat level, armor alignment and effective resistances, as the patch notes of updates 135 and 136
    /// describe them. The client's stats window has lines for all three (stats 96-106 and 108).
    /// </summary>
    public partial class OfflineServer
    {
        private const int StatCombatLevel = 108;

        // Resistance gear stat -> the client's "Effective ... res" stat (Scr_StatsHandler.SetStatAmount 96-106).
        private static readonly KeyValuePair<string, int>[] EffectiveResistanceStats =
        {
            Pair("ResPoison", 96), Pair("ArcaneResistance", 97), Pair("FireResistance", 98), Pair("EarthResistance", 99),
            Pair("WindResistance", 100), Pair("ElectricResistance", 101), Pair("PsychicResistance", 102),
            Pair("IceResistance", 103), Pair("SlashResistance", 104), Pair("BluntResistance", 105),
            Pair("PiercingResistance", 106),
        };

        private enum Alignment { None, Light, Heavy }
        private Alignment alignment;

        private int CombatLevel()
        {
            var levels = new List<int>();
            foreach (int s in CombatRules.CombatSkills) levels.Add(SkillLevel(s));
            return Mathf.Max(1, CombatRules.CombatLevel(levels));
        }

        private void SendCombatLevel() => Send(16, 5, StatCombatLevel, CombatLevel());

        private void SendEffectiveResistances()
        {
            for (int kind = 1; kind < DamageKinds.Count; kind++)
                foreach (var kv in EffectiveResistanceStats)
                    if (kv.Key == DamageKinds.ResistanceStat(kind))
                        Send(16, 5, kv.Value, Mathf.RoundToInt(CombatRules.EffectiveResistance(StatResistance(kind))));
        }

        /// <param name="passive">For the alignment passive: Jotnar's body and chaps together count light armor as heavy.</param>
        private Alignment WornAlignment(bool passive = false)
        {
            int light = 0, heavy = 0;
            foreach (var it in character.items)
            {
                if (!it.equipped) continue;
                var t = ItemData.Get(it.typeId);
                if (t?.slot == null) continue;
                int w = CombatRules.AlignmentWeight(t.slot);
                if (t.flags.Contains("isLightArmor")) light += w;
                if (t.flags.Contains("isHeavyArmor")) heavy += w;
            }
            if (passive && heavy < light && Wears("jotnarsBody") && Wears("jotnarsChaps"))
            {
                int swap = heavy;
                heavy = light;
                light = swap;
            }
            // 12 points in all, so only one type can reach 7.
            if (light >= CombatRules.AlignmentNeeded) return Alignment.Light;
            if (heavy >= CombatRules.AlignmentNeeded) return Alignment.Heavy;
            return Alignment.None;
        }

        /// <summary>
        /// Updates the armor alignment passive and its icon. <paramref name="resend"/> shows the icon again
        /// even if nothing changed (a new scene, a respawn).
        /// </summary>
        private void RefreshAlignment(bool resend = false)
        {
            if (character == null) return;
            var now = WornAlignment(passive: true);
            if (now == alignment && !resend) return;
            if (alignment != now && alignment != Alignment.None) Send(3, 35, character.name, AlignmentIcon(alignment));
            alignment = now;
            if (now == Alignment.None) return;
            const int tenths = 100 * 24 * 3600 * 10;   // a passive: the icon hides its timer (hidetime="1")
            Send(4, 9, character.name, AlignmentIcon(now), tenths, tenths);
        }

        private static int AlignmentIcon(Alignment a) =>
            a == Alignment.Light ? CombatRules.BuffLightArmor : CombatRules.BuffHeavyArmor;

        /// <summary>Dev: combat level, alignment and effective resistances, server against client (combatstats).</summary>
        internal void DevCombatStats()
        {
            var h = Scr_StatsHandler.instance;
            Plugin.Log.LogInfo($"[dev] combat level server {CombatLevel()} client {h.stat_CombatLevel}; alignment {WornAlignment()} (active {alignment}); " +
                $"x{AlignmentDamageDealt():0.00} dealt, x{AlignmentDamageTaken():0.00} taken; max hit taken {Mathf.CeilToInt(MaxHealth() * CombatRules.MaxHitShare)} of {MaxHealth()}");
            Plugin.Log.LogInfo($"[dev] slash res base {GearStat("SlashResistance")} effective server {CombatRules.EffectiveResistance(GearStat("SlashResistance")):0.#} client {h.stat_EffectiveResistanceSlash}; " +
                $"fire base {GearStat("FireResistance")} effective client {h.stat_EffectiveResistanceFire}");
        }

        private float AlignmentDamageDealt() => alignment == Alignment.Light ? CombatRules.LightAlignmentDamage : 1f;
        private float AlignmentDamageTaken() => alignment == Alignment.Heavy ? CombatRules.HeavyAlignmentTaken : 1f;
    }
}
