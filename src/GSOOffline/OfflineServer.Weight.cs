using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// The player's run speed: the original's base plus what buffs and gear add (RunSpeedBonus: Swiftness, Charge,
    /// Windwalking, Traverse, Slow, Stability), then the overweight slowdown. Carrying more than your capacity slows
    /// you down (CombatRules.OverweightSpeed) unless a stamina potion is carrying it. The client only shows the weight
    /// ("Weight: x/150" and a warning icon in the inventory) and never enforces it. The server sets the client's run
    /// speed directly: the stat messages for it (16/5, stats 5 and 40) only take whole numbers, too coarse for the
    /// weight curve.
    /// </summary>
    public partial class OfflineServer
    {
        private Scr_Player weightPlayer;      // the player object the base speeds were read from
        private float baseForward, baseBackwards, baseSideways;   // its speeds before any change (8, 3, 5)
        private float appliedSpeed = 1f;      // the share of it in use now
        private bool overburdened;
        private float weightTimer;

        /// <summary>What the inventory window counts: everything you have, equipped gear too, each stack rounded down.</summary>
        internal int CarriedWeight()
        {
            int sum = 0;
            foreach (var it in character.items)
            {
                var t = ItemData.Get(it.typeId);
                if (t != null) sum += (int)(t.weight * it.amount);
            }
            return sum;
        }

        internal int CarryingCapacity() => CombatRules.BaseCarryingCapacity + GearStat("Carryingcap");

        private void TickRunSpeed()
        {
            if (character == null || !sceneReady) return;
            weightTimer -= Time.deltaTime;
            if (weightTimer > 0f) return;
            weightTimer = 0.25f;
            var player = LocalPlayer;
            if (player == null || player.characterMovement == null) return;
            if (player != weightPlayer)
            {
                // A new player object (login, zone change) starts at the client's own speed.
                var m = player.characterMovement.movement;
                weightPlayer = player;
                baseForward = m.maxForwardSpeed;
                baseBackwards = m.maxBackwardsSpeed;
                baseSideways = m.maxSidewaysSpeed;
                appliedSpeed = 1f;
            }

            int weight = CarriedWeight(), capacity = CarryingCapacity();
            float load = HasBuff(BuffStamina) ? 1f : CombatRules.OverweightSpeed(weight, capacity);
            if (load < 1f != overburdened)
            {
                overburdened = load < 1f;
                Notice(overburdened ? "You are carrying too much and move more slowly." : "You are no longer overburdened.", "gray");
            }
            int run = Mathf.Max(1, BaseRunSpeed + RunSpeedBonus());
            float speed = load * run / BaseRunSpeed;
            if (Mathf.Abs(speed - appliedSpeed) < 0.005f) return;
            appliedSpeed = speed;
            var mv = player.characterMovement.movement;
            mv.maxForwardSpeed = baseForward * speed;
            mv.maxBackwardsSpeed = baseBackwards * speed;
            mv.maxSidewaysSpeed = baseSideways * speed;
            Plugin.Log.LogInfo($"[speed] run {run}/{BaseRunSpeed}, weight {weight}/{capacity} x{load:0.00}: {speed * 100f:0}% ({mv.maxForwardSpeed:0.00})");
        }
    }
}
