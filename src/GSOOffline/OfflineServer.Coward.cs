using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Giving up a bounty (issue #86). A bounty taken on lasts an hour of play; abandoning it on the wanted board (its
    /// "Abandon bounty" button, pressed twice) or with /abandon, or letting the time run out, ends it and earns
    /// "Coward's mark": less experience for a while, worse with each bounty given up while it lasts. It adds to
    /// Death's toll, and nobody lifts it: it only wears off with playing time. The numbers are in CombatRules.
    /// </summary>
    public partial class OfflineServer
    {
        private const int CowardBuffIcon = 121;      // an id the client's Buffs.xml doesn't use: registered at runtime
        private const int CowardBuffIconLook = 82;   // drawn with the Turmoil icon
        private const float AbandonConfirmSeconds = 30f;

        // The outlaw whose "Abandon bounty" was pressed once: a second press within 30 s gives it up. The board's
        // button reads "Confirm abandon" meanwhile (BountyBoardPatch).
        internal static string AbandonArmedName;
        private float abandonArmedUntil = -1f;
        private float huntBoardTimer;

        private bool CowardActive => character != null && character.cowardStacks > 0 && character.cowardSeconds > 0f;

        /// <summary>The share of experience Coward's mark takes (0 without it).</summary>
        private float CowardXpLoss() => CowardActive ? CombatRules.CowardXpLoss * character.cowardStacks : 0f;

        private int NextCowardStacks() => CowardActive ? Mathf.Min(character.cowardStacks + 1, CombatRules.CowardMaxStacks) : 1;

        private static string CowardCost(int stacks) =>
            $"{stacks * CombatRules.CowardXpLoss * 100:F0}% less experience for {stacks * CombatRules.CowardMinutesPerStack:F0} minutes";

        private static bool AbandonArmedFor(BountySave b) => AbandonArmedName == b.name;

        /// <summary>The board's button on a hunted outlaw, or /abandon: the first time warns, the second gives it up.</summary>
        private void RequestAbandon(BountySave b)
        {
            if (!AbandonArmedFor(b) || Time.time > abandonArmedUntil)
            {
                AbandonArmedName = b.name;
                abandonArmedUntil = Time.time + AbandonConfirmSeconds;
                Notice($"Give up the bounty on {b.name}? You'll carry Coward's mark: {CowardCost(NextCowardStacks())}. Press \"Confirm abandon\" (or /abandon again) to give it up.", "yellow");
                return;
            }
            EndHunt(b, $"You give up the bounty on {b.name}.");
        }

        /// <summary>/abandon [name]: the bounty whose outlaw's name starts so, or the only one there is.</summary>
        private void AbandonCommand(string name)
        {
            if (character == null) return;
            var hunted = character.bounties.FindAll(b => b.started);
            name = name.Trim();
            var match = name.Length == 0 ? (hunted.Count == 1 ? hunted : null)
                : hunted.FindAll(b => b.name.StartsWith(name, System.StringComparison.OrdinalIgnoreCase));
            if (hunted.Count == 0)
                Notice("You have no active bounties.", "gray");
            else if (match == null || match.Count == 0)
                Notice("Abandon which bounty? /abandon " + string.Join(", /abandon ", hunted.ConvertAll(b => b.name).ToArray()), "gray");
            else if (match.Count > 1)
                Notice("More than one outlaw goes by that name. Give the full name.", "gray");
            else
                RequestAbandon(match[0]);
        }

        /// <summary>A bounty given up or run out: the outlaw leaves, and Coward's mark grows (or starts).</summary>
        private void EndHunt(BountySave b, string why)
        {
            AbandonArmedName = null;
            character.bounties.Remove(b);
            RemoveBoardLine(b);
            if (world != null) SyncBountyActors();
            int stacks = NextCowardStacks();
            character.cowardStacks = stacks;
            character.cowardSeconds = CombatRules.CowardMinutesPerStack * 60f * stacks;
            Notice($"{why} Coward's mark: {CowardCost(stacks)}.", "red");
            Plugin.Log.LogInfo($"[bounty] ended: {b.name} ({why}) -> Coward's mark {stacks}");
            SendCowardMark();
            RefreshActiveBountiesWindow();
            SaveCurrentCharacter();
        }

        /// <summary>The hunts' hour counts down while playing. Not mid-fight: an outlaw being fought doesn't slip away.</summary>
        private void TickHuntTimers()
        {
            BountySave expired = null;
            foreach (var b in character.bounties)
            {
                if (!b.started) continue;
                b.secondsLeft -= Time.deltaTime;
                if (b.secondsLeft <= 0f && expired == null && !BountyEngaged(b)) expired = b;
            }
            if (expired != null)
                EndHunt(expired, $"{expired.name} got away: you didn't catch them in time. The bounty is off.");
            if (AbandonArmedName != null && Time.time > abandonArmedUntil) AbandonArmedName = null;

            huntBoardTimer -= Time.deltaTime;
            if (huntBoardTimer > 0f) return;
            huntBoardTimer = 5f;   // the board's "minutes left" stay current while it's open
            foreach (var b in character.bounties)
                if (b.started) UpdateBoardLine(b);
        }

        /// <summary>Login: bounties started before 1.8 had no clock; they get the full hour.</summary>
        private void LoadHuntTimers()
        {
            AbandonArmedName = null;
            foreach (var b in character.bounties)
                if (b.started && b.secondsLeft <= 0f) b.secondsLeft = CombatRules.BountyHuntSeconds;
        }

        // ---- Coward's mark -------------------------------------------------------------------

        /// <summary>After entering a scene or respawning: the client lost its buff icons.</summary>
        private void SendCowardMark()
        {
            if (!CowardActive || PlayerDead) return;
            RegisterRuntimeBuff(CowardBuffIcon, "Coward's mark", CowardBuffIconLook,
                $"You gave up on a bounty. You gain {character.cowardStacks * CombatRules.CowardXpLoss * 100:F0}% less experience. "
                + "Only time wears it off.");
            int tenths = Mathf.RoundToInt(character.cowardSeconds * 10f);
            Send(4, 9, character.name, CowardBuffIcon, tenths, tenths);
        }

        /// <summary>Counts down while playing (not while logged out); at 0 the mark is gone.</summary>
        private void TickCowardMark()
        {
            if (character == null || !sceneReady || character.cowardStacks <= 0) return;
            character.cowardSeconds -= Time.deltaTime;
            if (character.cowardSeconds > 0f) return;
            character.cowardStacks = 0;
            character.cowardSeconds = 0f;
            Send(3, 35, character.name, CowardBuffIcon);
            Notice("Coward's mark has worn off.", "green");
            Plugin.Log.LogInfo("[bounty] Coward's mark ended");
        }

        internal void DevBoardPress(string name)
        {
            var b = character.bounties.Find(o => o.name.StartsWith(name, System.StringComparison.OrdinalIgnoreCase));
            if (b != null) StartBounty(b.name + ", level");
        }

        internal void DevHuntAge(float seconds)
        {
            foreach (var b in character.bounties)
                if (b.started) b.secondsLeft -= seconds;
        }
    }
}
