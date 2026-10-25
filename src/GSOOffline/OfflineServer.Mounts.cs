using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Mounts: My Little Pony and the Turbobeetle (pet items 466 and 467, creatures 344 and 345 with mount="1" and "2")
    /// are pets you can ride. Mounts never went live, but the client has the riding side (Scr_PlayerMount: the
    /// Prefab_Mount_N model, the seat, the mount's own run animation, and 198/33 [mount, player] to get on, 0 to get
    /// off). The original server's Scr_MountsHandler is lost; its hooks show the plan: clicking your mount pet gets on
    /// it (Scr_NpcHandler.attemptInteract) and changing pets gets off (Scr_PlayerInventory). /mount does both.
    /// Riding is faster (CombatRules.MountRunBonus). Attacking, a job, sitting, swimming, a boat, dying or changing
    /// pets get you off; doors and zone changes don't (the player object, and the mount on it, carry over).
    /// Nothing is saved: every login starts on foot.
    /// </summary>
    public partial class OfflineServer
    {
        private Scr_Player mountedPlayer;   // the player object the client shows on the mount
        private int mountShown;             // its Prefab_Mount id; 0 on foot
        private float mountedAt;

        private bool Mounted => mountShown != 0 && mountedPlayer != null && LocalPlayer == mountedPlayer;

        /// <summary>The Prefab_Mount id of the pet in the Pet slot, or 0 if it can't be ridden.</summary>
        private int EquippedMount()
        {
            var item = EquippedPet();
            if (item == null || !PetNpcs().TryGetValue(item.typeId, out int npc)) return 0;
            return GameData.Npcs.TryGetValue(npc, out var info) ? info.mount : 0;
        }

        /// <summary>A click on your own mount pet gets you on it.</summary>
        private bool MountInteract(NpcEntity npc)
        {
            if (pet == null || npc != pet || EquippedMount() == 0) return false;
            Mount();
            return true;
        }

        /// <summary>/mount: on or off.</summary>
        private void ToggleMount()
        {
            if (Mounted) Dismount("got off");
            else Mount();
        }

        private void Mount()
        {
            var player = LocalPlayer;
            if (character == null || !sceneReady || player == null || Mounted) return;
            int id = EquippedMount();
            if (id == 0)
            {
                Notice("You have no mount with you. Put a pony or a turbobeetle in your Pet slot.", "gray");
                return;
            }
            if (PlayerDead || PlayerStunned) return;
            if (player.swimming || player.inShip)
            {
                Notice("You can't ride here.", "gray");
                return;
            }
            CancelJob("mounting");
            StopAttacking();
            StandUp();
            mountedPlayer = player;
            mountShown = id;
            mountedAt = Time.time;
            Send(198, 33, id, character.name);
            HidePetWhileRiding();
            Plugin.Log.LogInfo($"[mount] on mount {id}");
        }

        /// <summary>Off the mount, if on it. The client only gets the message while it shows the mount (it throws otherwise).</summary>
        private void Dismount(string reason)
        {
            if (mountShown == 0) return;
            var player = LocalPlayer;
            if (character != null && player != null && player == mountedPlayer && player.playerMount != null && player.playerMount.active)
                Send(198, 33, 0, character.name);
            mountShown = 0;
            mountedPlayer = null;
            visibilityTimer = 0f;   // the walking pet comes back (TickPet)
            Plugin.Log.LogInfo($"[mount] off: {reason}");
        }

        /// <summary>The pet you ride doesn't also walk beside you.</summary>
        private void HidePetWhileRiding()
        {
            if (pet == null) return;
            world?.npcs.Remove(pet);
            pet = null;
            visibilityTimer = 0f;
        }

        /// <summary>Four times a second, from TickPet: what gets you off the mount.</summary>
        private void TickMount()
        {
            if (mountShown == 0) return;
            var player = LocalPlayer;
            if (player == null || player != mountedPlayer)
            {
                // A new player object (back to the character list, relog): the client's mount went with the old one.
                mountShown = 0;
                mountedPlayer = null;
                Plugin.Log.LogInfo("[mount] off: new player object");
                return;
            }
            if (Time.time - mountedAt > 1f && (player.playerMount == null || !player.playerMount.active))
                Dismount("the client got off");   // the client's own /mount 0
            else if (PlayerDead) Dismount("died");
            else if (EquippedMount() != mountShown) Dismount("pet changed");
            else if (job != null) Dismount("job");
            else if (sittingAt.HasValue) Dismount("sat down");
            else if (player.swimming) Dismount("swimming");
            else if (player.inShip) Dismount("boarded a boat");
        }
    }
}
