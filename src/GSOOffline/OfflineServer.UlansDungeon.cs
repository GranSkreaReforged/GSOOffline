using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Ulan's dungeon (scene 25), as the original server ran it (Scr_UlansDungeon and Scr_InteractableHandler 1, 31, 32,
    /// 35, 36, 39): the objects it spawned there and the key to the gate.
    /// - A lift platform cycles between three floors, a stop every 20 seconds.
    /// - The gate (spawned object 4, interactable 32) opens for 10 seconds with the strong key, or from the far side
    ///   with the returning lever (39). A fragile key snaps back into its halves.
    /// - The key: search the skeleton (31) for a key broken in half, combine the halves into a fragile key, press it
    ///   into soft clay (the clay nodes) for a key mould, and smelt iron ore into the mould at the dungeon's forges.
    /// - The deep door to the Cave Dragons (35) opens when both levers (36) are pulled. The original reset each lever
    ///   after 2 seconds, which needed two players 40 m apart; offline a pulled lever stays down for 30 seconds.
    /// - Ulan follows the player through the dungeon during quest 17 (phase 2), and the lowest floor ends the phase.
    /// </summary>
    public partial class OfflineServer
    {
        private const int SceneUlan = 25, QuestUlan = 17, UlanFollowing = 115;
        private const int ObjElevator = 3, ObjGate = 4, ObjLever = 5;   // ab_spawnedObject_<n>
        private const int UlanUidBase = 7100000;
        private const int InteractForge = 1, InteractSkeleton = 31, InteractGate = 32, InteractDeepDoor = 35, InteractLever = 36, InteractReturningLever = 39;
        private const int ItemStrongKey = 10459, ItemIronOre = 10036;
        private const float LeverHold = 30f, GateOpenFor = 10f, DeepDoorOpenFor = 20f, ElevatorStop = 20f;

        private static readonly Vector3 ElevatorAt = new Vector3(231f, 88f, 59f);
        private static readonly float[] ElevatorFloors = { 71.5f, 77f, 88f };
        private static readonly Vector3 GateClosed = new Vector3(253.3f, 55.3f, -84.4f), GateOpen = new Vector3(253.3f, 61f, -84.4f);
        private static readonly Vector3[] Levers = { new Vector3(93.88f, 17.79f, -211.64f), new Vector3(115.69f, 17.79f, -177.19f) };
        private static readonly int[] LeverUp = { 1, 180 }, LeverDown = { 75, 90 };
        private static readonly Vector3 DeepDoorPos = new Vector3(80f, 16f, -182f);

        private bool ulanObjectsShown;
        private Vector3 elevatorPos = ElevatorAt;
        private int elevatorFloor = 2;
        private float elevatorMoveAt, elevatorSyncAt;
        private bool elevatorStopped = true;
        private float gateOpenUntil, deepDoorOpenUntil;
        private readonly float[] leverDownUntil = new float[2];
        private readonly bool[] leverShownDown = new bool[2];
        private bool gateShownOpen;

        private static int UlanUid(int i) => UlanUidBase + i;   // 0 lift, 1 gate, 2-3 levers

        private void ResetUlansDungeon() => ulanObjectsShown = false;   // the client drops spawned objects with the zone

        private void TickUlansDungeon()
        {
            if (character == null || !sceneReady || world == null || world.sceneId != SceneUlan) return;
            var player = LocalPlayer;
            if (player == null) return;
            if (!ulanObjectsShown)
            {
                ulanObjectsShown = true;
                gateShownOpen = Time.time < gateOpenUntil;
                var parts = new List<string>
                {
                    UlanUid(0) + "_" + ObjElevator + "_" + SceneWorld.Vec(elevatorPos) + "_0",
                    UlanUid(1) + "_" + ObjGate + "_" + SceneWorld.Vec(gateShownOpen ? GateOpen : GateClosed) + "_0",
                };
                for (int i = 0; i < Levers.Length; i++)
                {
                    leverShownDown[i] = Time.time < leverDownUntil[i];
                    parts.Add(UlanUid(2 + i) + "_" + ObjLever + "_" + SceneWorld.Vec(Levers[i]) + "_" + (leverShownDown[i] ? LeverDown[i] : LeverUp[i]));
                }
                Send(2, 35, character.name, string.Join(">", parts.ToArray()));
            }
            TickElevator();
            bool open = Time.time < gateOpenUntil;
            if (open != gateShownOpen)
            {
                gateShownOpen = open;
                Send(14, 1, UlanUid(1), 0, open ? GateOpen : GateClosed);
                if (!open) PlaySound(144, GateClosed, 30);
            }
            for (int i = 0; i < Levers.Length; i++)
            {
                bool down = Time.time < leverDownUntil[i];
                if (down == leverShownDown[i]) continue;
                leverShownDown[i] = down;
                Send(14, 1, UlanUid(2 + i), down ? LeverDown[i] : LeverUp[i], Levers[i]);
                if (!down) PlaySound(343, Levers[i], 30);
            }
            TickFollowingUlan(player.transform.position);
        }

        /// <summary>A stop every 20 s, cycling bottom, middle, top; it travels 1 m/s (the client glides it there).</summary>
        private void TickElevator()
        {
            if (Time.time >= elevatorMoveAt)
            {
                elevatorMoveAt = Time.time + ElevatorStop;
                elevatorFloor = (elevatorFloor + 1) % ElevatorFloors.Length;
                elevatorStopped = false;
                PlaySound(323, elevatorPos, 90);
            }
            var target = new Vector3(ElevatorAt.x, ElevatorFloors[elevatorFloor], ElevatorAt.z);
            elevatorPos = Vector3.MoveTowards(elevatorPos, target, Time.deltaTime);
            if (!elevatorStopped && Vector3.Distance(elevatorPos, target) < 0.15f)
            {
                elevatorStopped = true;
                PlaySound(324, elevatorPos, 90);
            }
            if (!elevatorStopped && Time.time >= elevatorSyncAt)
            {
                elevatorSyncAt = Time.time + 0.25f;
                Send(14, 1, UlanUid(0), 0, elevatorPos);
            }
        }

        /// <summary>A click on an object in the dungeon. True if handled here.</summary>
        private bool HandleUlanInteract(int typeId)
        {
            var player = LocalPlayer;
            if (character == null || player == null || character.scene != SceneUlan) return false;
            Vector3 me = player.transform.position;
            switch (typeId)
            {
                case InteractForge:
                    if (CountItem(ItemIronOre) == 0 || CountItem(ItemKeyMould) == 0 || CountItem(ItemStrongKey) > 0) return false;   // an ordinary forge
                    TakeItems(ItemIronOre, 1);
                    GiveItem(ItemStrongKey);
                    Notice("You smelt some iron into a strong key.");
                    return true;
                case InteractSkeleton:
                    if (CountItem(ItemStrongKey) == 0 && (CountItem(ItemKeyHalfA) == 0 || CountItem(ItemKeyHalfB) == 0))
                    {
                        GiveItem(ItemKeyHalfA);
                        GiveItem(ItemKeyHalfB);
                        Notice("You search the skeleton. You find a key. It's broken in half.");
                    }
                    else
                        Notice("You search the skeleton. You find nothing.");
                    return true;
                case InteractGate:
                    if (Time.time < gateOpenUntil) return true;
                    if (CountItem(ItemStrongKey) > 0) OpenGate();
                    else if (CountItem(ItemFragileKey) > 0)
                    {
                        Notice("You attempt to open the lock, but the key is too weak. The key breaks.");
                        TakeItems(ItemFragileKey, 1);
                        GiveItem(ItemKeyHalfA);
                        GiveItem(ItemKeyHalfB);
                        PlaySound(325, me, 30);
                    }
                    else
                    {
                        PlaySound(326, me, 30);
                        Notice("It's locked.");
                    }
                    return true;
                case InteractReturningLever:
                    OpenGate();
                    return true;
                case InteractLever:
                    for (int i = 0; i < Levers.Length; i++)
                    {
                        if (!Near(me, Levers[i], 7f) || Time.time < leverDownUntil[i]) continue;
                        leverDownUntil[i] = Time.time + LeverHold;
                        PlaySound(343, Levers[i], 30);
                        Notice("You pull the lever and hear a click.");
                        if (Time.time < leverDownUntil[1 - i])
                        {
                            deepDoorOpenUntil = Time.time + DeepDoorOpenFor;
                            PlaySound(344, DeepDoorPos, 50);
                            Notice("You hear another loud click from the back of the room.");
                        }
                    }
                    return true;
                case InteractDeepDoor:
                    if (Time.time < deepDoorOpenUntil) return false;   // open: the door table takes the player through
                    PlaySound(326, me, 30);
                    Notice("It's locked.");
                    return true;
            }
            return false;
        }

        private void OpenGate()
        {
            gateOpenUntil = Time.time + GateOpenFor;
            PlaySound(144, GateClosed, 30);
            Notice("The gate opens.");
        }

        // ---- Ulan along for the adventure (quest 17, phase 2) -----------------------------------

        private float ulanFollowAt;

        private void TickFollowingUlan(Vector3 me)
        {
            if (Time.time < ulanFollowAt) return;
            ulanFollowAt = Time.time + 0.5f;
            var ulan = world.npcs.Find(n => n.typeId == UlanFollowing);
            if (character.GetQuestPhase(QuestUlan) != 2)
            {
                if (ulan != null) world.npcs.Remove(ulan);
                return;
            }
            if (me.y < 25f)
            {
                // The lowest floor: the end of the dungeon, where Ulan (the one already standing there) waits.
                if (ulan != null) world.npcs.Remove(ulan);
                SetQuestPhase(QuestUlan, 3);
                return;
            }
            if (ulan == null)
            {
                Vector3 at = BesidePlayer(me);
                ulan = new NpcEntity { uid = SceneUlan * 10000 + 9600, typeId = UlanFollowing, spawnPos = at, pos = at, health = 30, maxHealth = 30 };
                world.npcs.Add(ulan);
                visibilityTimer = 0f;
                var u = ulan;
                Later(1.5f, () => { if (world != null && world.npcs.Contains(u)) NpcSay(u, "Onward into the dark, friend! Adventure awaits!"); });
                return;
            }
            KeepBeside(ulan, me);
        }
    }
}
