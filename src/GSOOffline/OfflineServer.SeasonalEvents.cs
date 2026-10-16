using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace GSOOffline
{
    /// <summary>
    /// The Halloween and Christmas events, as the original server ran them (Scr_HalloweenEvent, Scr_ChristmasEvent,
    /// Scr_NpcHandler.dropLoot): while one is on, every creature can drop that event's crate, more likely the higher
    /// its level. During Halloween one group of creatures (crabs, wolves, skeletons...) drops them twice as often,
    /// a different group each hour; the talking pumpkin tells you which. The original's staff switched the events on
    /// and off by hand; offline they follow the calendar (<see cref="Plugin.SeasonalEvents"/>).
    /// </summary>
    public partial class OfflineServer
    {
        private const int HalloweenCrate = 10667, BatUntradeable = 444;

        // The original's dates aren't recorded; these frame the holidays.
        private static bool HalloweenSeason(DateTime d) => (d.Month == 10 && d.Day >= 15) || (d.Month == 11 && d.Day <= 7);
        private static bool ChristmasSeason(DateTime d) => d.Month == 12 || (d.Month == 1 && d.Day <= 6);

        internal static bool HalloweenActive => EventOn("halloween", HalloweenSeason);
        internal static bool ChristmasActive => EventOn("christmas", ChristmasSeason);

        private static bool EventOn(string name, Func<DateTime, bool> season)
        {
            string mode = (Plugin.SeasonalEvents.Value ?? "Auto").Trim().ToLowerInvariant();
            if (mode == "off") return false;
            if (mode == "auto") return season(DateTime.Now);
            return mode == "both" || mode == name;
        }

        // Halloween's hunted groups: the first id names the group (for the pumpkin), the rest drop crates twice as often.
        private static readonly int[][] HalloweenGroups =
        {
            new[] { 2, 163, 164, 173, 9, 84, 93 },                          // crabs
            new[] { 23, 268 },                                              // wolves and snow wolves
            new[] { 13, 43, 75, 73, 97 },                                   // forest animals (deer, crawlers, moose, bears, giant spiders)
            new[] { 245, 275, 276, 97 },                                    // spiders
            new[] { 37, 300, 301, 302 },                                    // skeletons
            new[] { 98, 118, 244, 293, 297 },                               // dragons
            new[] { 101, 102 },                                             // sea serpents
            new[] { 96 },                                                   // trolls
            new[] { 99 },                                                   // golems
            new[] { 165, 166, 167, 168 },                                   // pirates
            new[] { 199, 200, 201, 202, 203, 204, 205, 206, 227, 228, 229, 207 },   // wraiths
            new[] { 195, 196, 197, 198 },                                   // lizards
            new[] { 223, 248 },                                             // plants (ravannae, ents)
            new[] { 249, 250, 251, 252, 303, 294, 292 },                    // poachers
            new[] { 308 },                                                  // infernal hounds
        };

        private int[] halloweenGroup;
        private DateTime halloweenGroupUntil;

        /// <summary>The hunted group, a new one at random every hour.</summary>
        private int[] HalloweenGroup()
        {
            if (halloweenGroup == null || DateTime.Now >= halloweenGroupUntil)
            {
                halloweenGroup = HalloweenGroups[Random.Range(0, HalloweenGroups.Length)];
                halloweenGroupUntil = DateTime.Now.AddHours(1);
                Plugin.Log.LogInfo($"[event] Halloween crates: {NpcName(halloweenGroup[0])}s drop them twice as often for the next hour");
            }
            return halloweenGroup;
        }

        // Candy out of 44: golden 1, brown, red, blue and white 10 each, black 3.
        private static readonly int[][] HalloweenCandy =
        {
            new[] { 10682, 1 }, new[] { 10680, 10 }, new[] { 10681, 10 }, new[] { 10683, 10 }, new[] { 10685, 10 }, new[] { 10684, 3 },
        };

        // Each a chance of one in n: the talking pumpkin, the furniture, and the tradeable Bat.
        private static readonly int[][] HalloweenExtras =
        {
            new[] { 10700, 40 }, new[] { 10686, 350 }, new[] { 10687, 40 }, new[] { 10688, 150 }, new[] { 10689, 75 },
            new[] { 10690, 1000 }, new[] { 10691, 50 }, new[] { 10692, 350 }, new[] { 10693, 40 }, new[] { 10694, 40 },
            new[] { 10695, 50 }, new[] { 10696, 50 }, new[] { 10697, 250 }, new[] { 10698, 300 }, new[] { 10699, 150 },
            new[] { 445, 1000 },
        };

        /// <summary>The event crates a kill can add to its bag (any creature of level 1 or more).</summary>
        private void SeasonalDrops(LootBag bag, NpcInfo info)
        {
            if (info == null || info.level < 1) return;
            if (HalloweenActive)
            {
                int oneIn = EventCrateOneIn(info.level);
                if (Array.IndexOf(HalloweenGroup(), info.id) >= 0) oneIn /= 2;
                if (OneIn(Mathf.Max(1, oneIn))) AddToBag(bag, HalloweenCrate, 1);
            }
            if (ChristmasActive && OneIn(Mathf.Max(1, EventCrateOneIn(info.level))))
                AddToBag(bag, ChristmasCrate, 1);
        }

        // 1 in 29 at level 1 down to 1 in 10 at level 50, then 1 in 9 at 55... and every kill from level 95.
        private static int EventCrateOneIn(int level) =>
            level > 50 ? (int)(10 - (level - 50) * 0.2) : (int)(30 - level * 0.4);

        /// <summary>The Halloween 2019 crate: candy (again 1 in 3), the untradeable Bat once, and the extras.</summary>
        private bool TryOpenHalloweenCrate(int typeId)
        {
            if (typeId != HalloweenCrate) return false;
            if (PlayerDead) return true;
            TakeItems(typeId, 1);
            var player = LocalPlayer;
            if (player != null) PlaySound(CrateSound, player.transform.position, 20);
            Plugin.Log.LogInfo($"[item] opening {ItemData.Get(typeId)?.name}");
            do GiveFromCrate(Weighted(HalloweenCandy));
            while (OneIn(3));
            // The untradeable Bat: 1 in 500 with the first crate, 1 in 499 with the next..., then never again.
            if (character.halloweenPetCounter >= 0)
            {
                if (OneIn(500 - character.halloweenPetCounter))
                {
                    character.halloweenPetCounter = -1;
                    GiveFromCrate(BatUntradeable);
                }
                else character.halloweenPetCounter++;
            }
            foreach (var e in HalloweenExtras)
                if (OneIn(e[1])) GiveFromCrate(e[0]);
            return true;
        }

        /// <summary>The talking pumpkin's hint: which creatures drop Halloween crates most this hour.</summary>
        private void TalkingPumpkin()
        {
            if (!HalloweenActive)
            {
                Notice("Pumpkin says: Huh? Is it Halloween yet? zzz... ");
                return;
            }
            int npc = HalloweenGroup()[0];
            string name = npc == 23 ? "wolves" : npc == 13 ? "forest animals" : npc == 245 ? "spiders" : npc == 223 ? "plants"
                : NpcName(npc) + "s";
            Notice($"Pumpkin says: You collecting Halloween crates? Try hunting {name}! I heard they've got plenty.");
        }

        /// <summary>Once per login, in the first scene: what's on.</summary>
        private void AnnounceSeasonalEvents()
        {
            if (HalloweenActive)
                Notice("The Halloween event is on: creatures drop Halloween crates, the stronger the more often.", "orange");
            if (ChristmasActive)
                Notice("The Christmas event is on: creatures drop Christmas crates, the stronger the more often.", "cyan");
        }
    }
}
