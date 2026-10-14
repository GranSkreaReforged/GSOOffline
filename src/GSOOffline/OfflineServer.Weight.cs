using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Carrying more than your capacity slows you down (CombatRules.OverweightSpeed). The client only shows the
    /// weight ("Weight: x/150" and a warning icon in the inventory) and never enforces it. The server sets the client's
    /// run speed directly: the stat message for it (16/5, stat 5) only takes whole numbers, too coarse for a curve.
    /// </summary>
    public partial class OfflineServer
    {
        private Scr_Player weightPlayer;      // the player object the base speeds were read from
        private float baseForward, baseBackwards, baseSideways;   // its speeds before any slowdown (8, 3, 5)
        private float appliedSpeed = 1f;      // the share of it in use now
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

        private void TickWeight()
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
            float speed = CombatRules.OverweightSpeed(weight, capacity);
            if (Mathf.Abs(speed - appliedSpeed) < 0.005f) return;
            if (appliedSpeed >= 1f) Notice("You are carrying too much and move more slowly.", "gray");
            else if (speed >= 1f) Notice("You are no longer overburdened.", "gray");
            appliedSpeed = speed;
            var mv = player.characterMovement.movement;
            mv.maxForwardSpeed = baseForward * speed;
            mv.maxBackwardsSpeed = baseBackwards * speed;
            mv.maxSidewaysSpeed = baseSideways * speed;
            Plugin.Log.LogInfo($"[weight] {weight}/{capacity}: run speed {speed * 100f:0}% ({mv.maxForwardSpeed:0.00})");
        }
    }
}
