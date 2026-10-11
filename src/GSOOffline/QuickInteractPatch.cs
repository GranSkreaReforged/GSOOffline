using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// The Interact key (F) uses what the mouse cursor is over, if it's in reach, and otherwise the nearest thing.
    /// The client's own check (Scr_Player.quickInteractCheck) always took a loot bag within 10 m, then the nearest
    /// NPC within 5 m, and only then an object, and of the objects the first in its list within 5 m rather than the
    /// nearest: next to an NPC a chair or door could never be used with F, and a chair could win over the door
    /// beside it. Texts and actions are the client's.
    /// </summary>
    internal static class QuickInteract
    {
        private enum Kind { None, Loot, Npc, Interactable, Harvestable, Boat }
        private const int ChairType = 54;

        private static readonly AccessTools.FieldRef<Menucontroller, Scr_Npc> HoverNpc =
            AccessTools.FieldRefAccess<Menucontroller, Scr_Npc>("hoveringNpc");
        private static readonly AccessTools.FieldRef<Menucontroller, Scr_Interactable> HoverInteractable =
            AccessTools.FieldRefAccess<Menucontroller, Scr_Interactable>("hoveringInteractable");
        private static readonly AccessTools.FieldRef<Menucontroller, Scr_Harvestable> HoverHarvestable =
            AccessTools.FieldRefAccess<Menucontroller, Scr_Harvestable>("hoveringHarvestable");
        private static readonly AccessTools.FieldRef<Menucontroller, Lootbag> HoverLootbag =
            AccessTools.FieldRefAccess<Menucontroller, Lootbag>("hoveringLootbag");
        private static readonly AccessTools.FieldRef<Menucontroller, Scr_PlayerShip> HoverBoat =
            AccessTools.FieldRefAccess<Menucontroller, Scr_PlayerShip>("hoveringPlayerBoat");

        // What the cursor was over in the last GUI pass. Menucontroller.Update clears its hover fields every frame
        // (after its own click handling), so they're copied just before that.
        private static Component hovered;
        private static Kind hoveredKind;

        // Bridge testing: stands in for the cursor (the `hover` command).
        internal static Component DevHover;

        internal static void SnapshotHover(Menucontroller mc)
        {
            hoveredKind = Kind.None;
            hovered = null;
            if (DevHover != null) Set(DevHover is Scr_Npc ? Kind.Npc : Kind.Interactable, DevHover);
            else if (HoverLootbag(mc) != null) Set(Kind.Loot, HoverLootbag(mc));
            else if (HoverNpc(mc) != null) Set(Kind.Npc, HoverNpc(mc));
            else if (HoverInteractable(mc) != null) Set(Kind.Interactable, HoverInteractable(mc));
            else if (HoverHarvestable(mc) != null) Set(Kind.Harvestable, HoverHarvestable(mc));
            else if (HoverBoat(mc) != null) Set(Kind.Boat, HoverBoat(mc));
        }

        private static void Set(Kind kind, Component c)
        {
            hoveredKind = kind;
            hovered = c;
        }

        internal static void Check(Scr_Player player)
        {
            if (Scr_DialogueHandler.instance.dialogueActive) return;
            var mc = Menucontroller.instance;
            mc.quickInteractText = string.Empty;
            Vector3 p = player.transform.position;

            Kind kind = hoveredKind;
            Component target = hovered;
            if (target == null || !Usable(kind, target, player, p, true))
                Nearest(player, p, out kind, out target);
            if (target == null) return;

            mc.quickInteractText = Text(kind, target);
            if (Scr_KeyboardInputs.instance.GetKeyDown("Interact") || devPress)
            {
                devPress = false;
                Use(kind, target, player);
            }
        }

        // Bridge testing: the next check acts as if the Interact key was pressed (`interactprompt press`).
        private static bool devPress;
        internal static void DevPress() => devPress = true;

        private static bool Usable(Kind kind, Component c, Scr_Player player, Vector3 p, bool underCursor)
        {
            float d = Vector3.Distance(p, c.transform.position);
            switch (kind)
            {
                case Kind.Loot:
                    return d < 10f;
                case Kind.Npc:
                    var n = (Scr_Npc)c;
                    return d < 5f && n.canInteract && !n.dead && n.npcLevel == 0;
                case Kind.Interactable:
                    // A click reaches 5 m plus the object's own extra distance; the client's F check only 5 m.
                    var i = (Scr_Interactable)c;
                    return !i.noQuickInteract && d < (underCursor ? 5f + i.customDistance : 5f);
                case Kind.Harvestable:
                    return d < 5f && ((Scr_Harvestable)c).currentHealth > 0;
                case Kind.Boat:
                    return d < 10f && !player.inShip;
            }
            return false;
        }

        private static void Nearest(Scr_Player player, Vector3 p, out Kind kind, out Component target)
        {
            kind = Kind.None;
            target = null;
            float best = float.MaxValue;
            foreach (var x in Lootbag.list) Consider(Kind.Loot, x, player, p, ref best, ref kind, ref target);
            foreach (var x in Scr_NpcHandler.instance.npcList) Consider(Kind.Npc, x, player, p, ref best, ref kind, ref target);
            foreach (var x in Scr_Interactable.list) Consider(Kind.Interactable, x, player, p, ref best, ref kind, ref target);
            foreach (var x in Scr_HarvestableHandler.instance.harvestablesList) Consider(Kind.Harvestable, x, player, p, ref best, ref kind, ref target);
            foreach (var x in Scr_PlayerBoatHandler.instance.boats) Consider(Kind.Boat, x, player, p, ref best, ref kind, ref target);
        }

        private static void Consider(Kind k, Component c, Scr_Player player, Vector3 p, ref float best, ref Kind kind, ref Component target)
        {
            if (c == null || !Usable(k, c, player, p, false)) return;
            float d = Vector3.Distance(p, c.transform.position);
            if (d >= best) return;
            best = d;
            kind = k;
            target = c;
        }

        private static string Text(Kind kind, Component c)
        {
            switch (kind)
            {
                case Kind.Loot: return " take loot";
                case Kind.Npc: return " interact with " + ((Scr_Npc)c).npcName;
                case Kind.Interactable:
                    var i = (Scr_Interactable)c;
                    // Chairs have no name in the game data, which left "Press [F] to interact with".
                    if (string.IsNullOrEmpty(i.interactableName) && i.typeId == ChairType) return " sit down";
                    return " interact with " + i.interactableName;
                case Kind.Harvestable: return " harvest " + ((Scr_Harvestable)c).harvestableName;
                case Kind.Boat:
                    var b = (Scr_PlayerShip)c;
                    return " board " + b.boatName + " (" + b.ownername + ")";
            }
            return string.Empty;
        }

        private static void Use(Kind kind, Component c, Scr_Player player)
        {
            switch (kind)
            {
                case Kind.Loot:
                    var lb = (Lootbag)c;
                    player.connecter.lootHandler.collectLoot(lb.id, lb);
                    break;
                case Kind.Npc:
                    Scr_NpcHandler.instance.interact(((Scr_Npc)c).npcUniqueId);
                    break;
                case Kind.Interactable:
                    player.connecter.interactableHandler.interact((Scr_Interactable)c);
                    break;
                case Kind.Harvestable:
                    Scr_HarvestableHandler.instance.attemptHarvest(((Scr_Harvestable)c).uniqueId);
                    break;
                case Kind.Boat:
                    Scr_RPCSender.instance.sendChatMessage(player.playerName, "/enterboat " + ((Scr_PlayerShip)c).ownername);
                    break;
            }
        }
    }

    [HarmonyPatch(typeof(Menucontroller), "Update")]
    internal static class HoverSnapshotPatch
    {
        private static void Prefix(Menucontroller __instance) => QuickInteract.SnapshotHover(__instance);
    }

    [HarmonyPatch(typeof(Scr_Player), "quickInteractCheck")]
    internal static class QuickInteractPatch
    {
        private static bool Prefix(Scr_Player __instance)
        {
            QuickInteract.Check(__instance);
            return false;
        }
    }
}
