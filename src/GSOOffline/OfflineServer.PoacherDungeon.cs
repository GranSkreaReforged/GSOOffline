using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// The Poacher dungeon (scene 38), as the original server's Scr_PoacherDungeon ran it (its bosses keep the moves in
    /// OfflineServer.BossMechanics.cs). What it spawned and what its objects did:
    /// - The vault: three keys open it (George drops the first, a skeleton by the lava room holds the second, Sepitus
    ///   drops the third). Opened, it gives a bag rolled from Sepitus's own drop table plus a chance at three rarities,
    ///   and a lever inside lets you out.
    /// - The storage room behind George: its door opens once he's dead (before that he throws you out), or from inside
    ///   with the lever. The dynamite crate in it gives dynamite, which blows up the rock pile blocking a cave.
    /// - The lava room: two rocks drift across the lava, and a bridge rises while someone (or a dropped loot bag) stands
    ///   on one of its two pressure plates. Lava burns 30-40% of your health every half second.
    /// - Arrow traps in three rooms: stepping on one roots you and arrows fly from the walls; it rearms after 10 s.
    /// - Chests, each once an hour: the second vault key, a teleport crystal with supplies, the lava room's supplies,
    ///   an emote and a lava lamp, and silver with potions by the graveyard. A stone that quickens you for a minute.
    /// Damage is the original's against its 100 health per combat level, taken as the same share of yours.
    /// </summary>
    public partial class OfflineServer
    {
        private const int ScenePoacher = 38, PoacherUidBase = 7200000;
        private const int ObjLavaRock = 36, ObjLavaBridge = 37, ObjStorageDoor = 38, ObjTrapArmed = 39, ObjTrapSprung = 40;
        private const int ObjRockPile = 41, ObjJailDoor = 42, ObjVaultDoor = 43;
        private const int InteractStorageDoor = 64, InteractDynamiteBox = 65, InteractVaultDoor = 66;
        private const int ItemVaultKey1 = 10630, ItemVaultKey2 = 10631, ItemVaultKey3 = 10632, ItemDynamite = 10633;
        private const int George = 294, Sepitus = 292;

        // Uids: 0 vault door, 1-2 lava rocks, 3 bridge, 4 rock pile, 5 storage door, 6 jail door, 10+ traps.
        private static int PoacherUid(int i) => PoacherUidBase + i;

        private static readonly Vector3 VaultDoorShut = new Vector3(-32f, 58f, 216f), VaultDoorOpen = new Vector3(-32f, 63f, 216f);
        private static readonly Vector3 VaultLootSpot = new Vector3(-40.2f, 56.63f, 229.6f);
        private static readonly Vector3 BridgeUp = new Vector3(641f, 29f, -5f), BridgeDown = new Vector3(641f, 24f, -5f);
        private static readonly Vector3[] BridgePlates = { new Vector3(663f, 32.7f, -9.3f), new Vector3(650f, 33.1f, 24.1f) };
        private static readonly Vector3 RockPile = new Vector3(38f, 56f, 110f), RockPileSpot = new Vector3(35.6f, 56.1f, 112.3f);
        private static readonly Vector3 StorageDoorPos = new Vector3(-344.2f, 56.3f, 62.2f), StorageEntrance = new Vector3(-340f, 56f, 64.6f);
        private static readonly Vector3 StorageLever = new Vector3(-357.6f, 56f, 70.8f), DynamiteBox = new Vector3(-357f, 56f, 65f);
        private static readonly Vector3 JailDoorPos = new Vector3(-276.5f, 56.3f, -13.3f);
        private static readonly Vector3 VaultLever = new Vector3(-28.9f, 56.2f, 234.2f);
        private static readonly Vector3 LavaRoom = new Vector3(650f, 30f, -50f), SepitusLava = new Vector3(895f, 20.7f, -639f);

        private class Trap
        {
            public Vector3 pos;
            public Vector3[] arrows;
            public float rearmAt;   // sprung until then
        }

        private static Trap MakeTrap(float x, float y, float z, params Vector3[] arrows) => new Trap { pos = new Vector3(x, y, z), arrows = arrows };

        private static readonly Vector3[] StorageArrows = { new Vector3(-341f, 66.2f, 68.3f), new Vector3(-329f, 66.5f, 55.6f), new Vector3(-330f, 66.2f, 90.9f) };
        private static readonly Vector3[] EmoteRoomArrows = { new Vector3(68.1f, 64.4f, 25.6f), new Vector3(63.8f, 62.6f, 39.1f) };
        private static readonly Vector3[] SecondTrapArrows = { new Vector3(168.1f, 62.3f, 16.03f) };

        private readonly Trap[] traps =
        {
            MakeTrap(-316f, 55.953f, 80f, StorageArrows),
            MakeTrap(67.7f, 56.07f, 33.6f, EmoteRoomArrows), MakeTrap(60.2f, 56.07f, 34.7f, EmoteRoomArrows),
            MakeTrap(60.5f, 56.07f, 27.8f, EmoteRoomArrows), MakeTrap(57.9f, 56.07f, 29.68f, EmoteRoomArrows),
            MakeTrap(156.8f, 56.05f, 16.15f, SecondTrapArrows), MakeTrap(160.99f, 56.05f, 18.03f, SecondTrapArrows),
            MakeTrap(160.99f, 56.05f, 14.24f, SecondTrapArrows),
        };

        private bool poacherShown, bridgeShownUp, storageShownOpen, vaultShownOpen, rockPileShown = true;
        private float vaultOpenUntil, vaultBusyUntil, storageOpenUntil, bridgeUpUntil, rockPileBackAt, rockPileBlowsAt, dynamiteBoxReadyAt;
        private float lavaRockPos, lavaRockDir = 1f, poacherTick, lavaBurnAt;
        private readonly bool[] trapShownArmed = new bool[8];
        private readonly Dictionary<string, float> chestReadyAt = new Dictionary<string, float>();

        private void ResetPoacherDungeon() => poacherShown = false;

        private void TickPoacherDungeon()
        {
            TickRockPile();
            if (character == null || !sceneReady || world == null || world.sceneId != ScenePoacher) return;
            var player = LocalPlayer;
            if (player == null) return;
            if (!poacherShown) ShowPoacherObjects();
            poacherTick -= Time.deltaTime;
            if (poacherTick > 0f) return;
            poacherTick = 0.5f;
            Vector3 me = player.transform.position;
            TickLavaRoom(me);
            TickTraps(me);
            SyncPoacherDoors();
        }

        private void ShowPoacherObjects()
        {
            poacherShown = true;
            vaultShownOpen = Time.time < vaultOpenUntil;
            storageShownOpen = Time.time < storageOpenUntil;
            bridgeShownUp = Time.time < bridgeUpUntil;
            var parts = new List<string>
            {
                Obj(0, ObjVaultDoor, vaultShownOpen ? VaultDoorOpen : VaultDoorShut, 0),
                Obj(1, ObjLavaRock, LavaRock1, 0),
                Obj(2, ObjLavaRock, LavaRock2, 0),
                Obj(3, ObjLavaBridge, bridgeShownUp ? BridgeUp : BridgeDown, 0),
                Obj(5, ObjStorageDoor, StorageDoorPos, storageShownOpen ? 130 : 1),
                Obj(6, ObjJailDoor, JailDoorPos, 1),
            };
            if (rockPileShown) parts.Add(Obj(4, ObjRockPile, RockPile, 0));
            for (int i = 0; i < traps.Length; i++)
            {
                trapShownArmed[i] = Time.time >= traps[i].rearmAt;
                parts.Add(Obj(10 + i, trapShownArmed[i] ? ObjTrapArmed : ObjTrapSprung, traps[i].pos, 0));
            }
            Send(2, 35, character.name, string.Join(">", parts.ToArray()));
        }

        private static string Obj(int i, int type, Vector3 pos, int rot) => PoacherUid(i) + "_" + type + "_" + SceneWorld.Vec(pos) + "_" + rot;

        private void Respawn(int i, int type, Vector3 pos, int rot)
        {
            Send(25, 10, PoacherUid(i));
            Send(2, 35, character.name, Obj(i, type, pos, rot));
        }

        private Vector3 LavaRock1 => new Vector3(600f + lavaRockPos, 29f, -50f);
        private Vector3 LavaRock2 => new Vector3(650f, 29f, -15f - lavaRockPos * 0.5f);

        /// <summary>The drifting rocks (2 m/s back and forth), the bridge's plates, and the lava's burn.</summary>
        private void TickLavaRoom(Vector3 me)
        {
            if (lavaRockPos < 0f) lavaRockDir = 1f;
            else if (lavaRockPos > 50f) lavaRockDir = -1f;
            lavaRockPos += lavaRockDir;
            Send(14, 1, PoacherUid(1), 0, LavaRock1);
            Send(14, 1, PoacherUid(2), 0, LavaRock2);

            bool pressed = false;
            foreach (var plate in BridgePlates)
            {
                if (Near(me, plate, 2f)) pressed = true;
                foreach (var bag in SceneLoot(ScenePoacher))
                    if (Near(bag.pos, plate, 2f)) pressed = true;
            }
            if (pressed)
            {
                if (Time.time >= bridgeUpUntil) PlaySound(385, me, 50);
                bridgeUpUntil = Time.time + 1f;
            }

            bool inLava = Near(me, LavaRoom, 115f) && me.y < 30.8f || Near(me, SepitusLava, 115f) && me.y < 20.7f;
            if (inLava && !PlayerDead && Time.time >= lavaBurnAt)
            {
                lavaBurnAt = Time.time + 0.5f;
                PlayEffect(7, me);
                float share = Random.Range(0.3f, 0.4f);
                float res = CombatRules.PlayerResistance(GearStat(DamageKinds.ResistanceStat(DamageKinds.Fire)));
                HurtPlayer(Mathf.RoundToInt(MaxHealth() * share * (1f - res)), SplashPlain);
            }
        }

        private void SyncPoacherDoors()
        {
            bool up = Time.time < bridgeUpUntil;
            if (up != bridgeShownUp)
            {
                bridgeShownUp = up;
                Send(14, 1, PoacherUid(3), 0, up ? BridgeUp : BridgeDown);
            }
            bool vault = Time.time < vaultOpenUntil;
            if (vault != vaultShownOpen)
            {
                vaultShownOpen = vault;
                Send(14, 1, PoacherUid(0), 0, vault ? VaultDoorOpen : VaultDoorShut);
            }
            bool storage = Time.time < storageOpenUntil;
            if (storage != storageShownOpen)
            {
                storageShownOpen = storage;
                Send(14, 1, PoacherUid(5), storage ? 130 : 1, StorageDoorPos);
            }
        }

        // ---- traps --------------------------------------------------------------------------

        private void TickTraps(Vector3 me)
        {
            for (int i = 0; i < traps.Length; i++)
            {
                var t = traps[i];
                bool armed = Time.time >= t.rearmAt;
                if (armed && !trapShownArmed[i])
                {
                    trapShownArmed[i] = true;
                    Respawn(10 + i, ObjTrapArmed, t.pos, 0);
                    PlaySound(427, t.pos, 40);
                }
                if (!armed || PlayerDead || !Near(me, t.pos, 2f)) continue;
                t.rearmAt = Time.time + 10f;
                trapShownArmed[i] = false;
                Respawn(10 + i, ObjTrapSprung, t.pos, 0);
                PlaySound(428, t.pos, 40);
                Notice("You activated a trap!", "red");
                SpringTrap(t);
            }
        }

        /// <summary>The plate's blow, 5 s rooted, and ten arrows from the walls over the next 2.5 s.</summary>
        private void SpringTrap(Trap t)
        {
            int hit = HurtByOriginal(Random.Range(2000, 2501), DamageKinds.Piercing);
            Plugin.Log.LogInfo($"[dungeon] trap at {SceneWorld.Vec(t.pos)} sprung: {hit} damage, rooted, 10 arrows");
            Send(1, 4, character.name, false);
            Later(5f, () => { if (!PlayerDead && character != null) Send(1, 4, character.name, true); });
            for (int i = 0; i < 10; i++)
            {
                Vector3 from = t.arrows[Random.Range(0, t.arrows.Length)];
                Later(Random.Range(0f, 2.5f), () =>
                {
                    var player = LocalPlayer;
                    if (player == null || PlayerDead || world == null || world.sceneId != ScenePoacher) return;
                    Vector3 at = player.transform.position + Vector3.up;
                    PlaySound(344, from, 60);
                    Send(21, 0, 5, from, at);   // a simple arrow projectile
                    Later(FlightTime(5, from, at), () => { if (!PlayerDead && world != null && world.sceneId == ScenePoacher) HurtByOriginal(Random.Range(1400, 1801), DamageKinds.Piercing); });
                });
            }
        }

        // ---- the rock pile and the dynamite -----------------------------------------------------

        private void TickRockPile()
        {
            if (rockPileBlowsAt > 0f && Time.time >= rockPileBlowsAt)
            {
                rockPileBlowsAt = 0f;
                rockPileShown = false;
                rockPileBackAt = Time.time + 10f;
                if (InPoacherDungeon)
                {
                    Send(25, 10, PoacherUid(4));
                    PlayEffect(29, new Vector3(33.5f, 59.7f, 111.7f));
                    PlaySound(219, new Vector3(33f, 59.7f, 111.7f), 60);
                }
            }
            if (!rockPileShown && rockPileBackAt > 0f && Time.time >= rockPileBackAt)
            {
                rockPileBackAt = 0f;
                rockPileShown = true;
                if (InPoacherDungeon) Send(2, 35, character.name, Obj(4, ObjRockPile, RockPile, 0));
            }
        }

        private bool InPoacherDungeon => character != null && sceneReady && world != null && world.sceneId == ScenePoacher;

        /// <summary>Using dynamite (item use). True if it's dynamite.</summary>
        private bool UseDynamite(int typeId)
        {
            if (typeId != ItemDynamite) return false;
            var player = LocalPlayer;
            if (InPoacherDungeon && player != null && Near(player.transform.position, RockPileSpot, 5f) && rockPileShown && rockPileBlowsAt <= 0f)
            {
                TakeItems(ItemDynamite, 1);
                Notice("You place dynamite on the pile of rocks...", "red");
                rockPileBlowsAt = Time.time + 5f;
            }
            else
                Notice("There is nothing here to use dynamite on.");
            return true;
        }

        // ---- clicks ---------------------------------------------------------------------------

        private NpcEntity PoacherBoss(int type) => world?.npcs.Find(n => n.typeId == type);

        private bool GeorgeAlive => PoacherBoss(George) is NpcEntity g && !g.dead;

        /// <summary>A click on an object in the Poacher dungeon. True if handled here.</summary>
        private bool HandlePoacherInteract(int typeId)
        {
            var player = LocalPlayer;
            if (!InPoacherDungeon || player == null) return false;
            Vector3 me = player.transform.position;
            switch (typeId)
            {
                case InteractStorageDoor:
                    if (!Near(me, StorageDoorPos, 5f)) return true;
                    if (!GeorgeAlive) storageOpenUntil = Time.time + 10f;
                    else ThrownOutByGeorge(me.x < -345f);
                    return true;
                case InteractDynamiteBox:
                    if (Time.time < dynamiteBoxReadyAt) Notice("The chest has recently been looted, it is empty.");
                    else if (GeorgeAlive) ThrownOutByGeorge(true);
                    else if (Near(me, DynamiteBox, 10f))
                    {
                        dynamiteBoxReadyAt = Time.time + 60f;
                        GiveItem(ItemDynamite);
                        Notice("You take some dynamite from the crate.");
                    }
                    return true;
                case InteractVaultDoor:
                    OpenVault(me);
                    return true;
                case ObjectQuest:
                    return ClickPoacherObject(me);
            }
            return false;
        }

        private void ThrownOutByGeorge(bool throwOut)
        {
            var george = PoacherBoss(George);
            if (george != null) NpcSay(george, "Intruder! Die!");
            if (!throwOut) return;
            Notice("You have been removed from the storage room by the guard!", "red");
            Teleport(ScenePoacher, StorageEntrance);
        }

        /// <summary>The dungeon's generic objects (43), told apart by where they are.</summary>
        private bool ClickPoacherObject(Vector3 me)
        {
            if (Near(me, StorageLever, 5f))
            {
                storageOpenUntil = Time.time + 4f;
                return true;
            }
            if (Near(me, VaultLever, 5f))
            {
                vaultOpenUntil = Time.time + 15f;
                return true;
            }
            if (Near(me, new Vector3(621f, 32f, 28f), 4f))
            {
                if (OpenChest("vault key", "The chest is locked. It will open again in {0}."))
                {
                    GiveItem(ItemVaultKey2);
                    Notice("You find a shiny key.");
                }
                return true;
            }
            if (Near(me, new Vector3(891f, 24f, -430f), 4f))
            {
                if (OpenChest("teleport", "The chest is locked. It will open again in {0}."))
                {
                    Notice("You find a teleport crystal and some supplies.");
                    GiveItem(10638);
                    GiveItem(10454, 5);
                    GiveItem(10537, 5);
                    GiveItem(10438);
                    SetSilver(character.silver + 2500);
                    Notice("+ 2500 silver", "green");
                }
                return true;
            }
            if (Near(me, new Vector3(607f, 33f, 38f), 4f))
            {
                if (OpenChest("lava room", "The chest is locked. It will open again in {0}."))
                {
                    Notice("You find some supplies.");
                    GiveItem(10069, 5);
                    GiveItem(10454, 2);
                    GiveItem(10564, 5);
                    SetSilver(character.silver + 900);
                    Notice("+ 900 silver", "green");
                }
                return true;
            }
            if (Near(me, new Vector3(57f, 56f, 25f), 4f))
            {
                if (OpenChest("emote", "The chest is empty."))
                {
                    GiveItem(10649);
                    GiveItem(10644);
                }
                return true;
            }
            if (Near(me, new Vector3(-38f, 63f, 59f), 4f))
            {
                if (OpenChest("graveyard", "The chest is empty."))
                {
                    int silver = Random.Range(500, 2001);
                    SetSilver(character.silver + silver);
                    Notice($"+ {silver} silver", "green");
                    GiveItem(10069, 3);
                    GiveItem(10454, 3);
                }
                return true;
            }
            if (Near(me, new Vector3(475f, 15f, -112f), 4f))
            {
                Notice("You touch the stone... ");
                PlayPlayerEffect(82);
                PlaySound(66, me, 40);
                AddBuff(-33, 33, 60f, new BuffEffect());   // the client's quickness buff (movement speed isn't modelled)
                return true;
            }
            return false;
        }

        /// <summary>Each chest opens once an hour (per session). False, with the chest's message, while it's shut.</summary>
        private bool OpenChest(string chest, string lockedMessage)
        {
            if (chestReadyAt.TryGetValue(chest, out float at) && Time.time < at)
            {
                int minutes = 1 + (int)((at - Time.time) / 60f);
                Notice(string.Format(lockedMessage, minutes == 1 ? "1 minute" : minutes + " minutes"), "red");
                return false;
            }
            chestReadyAt[chest] = Time.time + 3600f;
            Notice("You search the chest...");
            return true;
        }

        // ---- the vault ------------------------------------------------------------------------

        private void OpenVault(Vector3 me)
        {
            if (Time.time < vaultOpenUntil || Time.time < vaultBusyUntil)
            {
                Notice("The vault is currently being used, please wait a moment and try again.", "red");
                return;
            }
            if (!Near(me, VaultDoorShut, 10f)) return;
            int[] keys = { ItemVaultKey1, ItemVaultKey2, ItemVaultKey3 };
            string[] which = { "first", "second", "third" };
            bool all = true;
            for (int i = 0; i < keys.Length; i++)
            {
                bool has = CountItem(keys[i]) > 0;
                all &= has;
                int n = i;
                Later(i, () => Notice($"You attempt to open the {which[n]} lock..."));
                Later(i + 0.5f, () =>
                {
                    Notice(has ? "It opens!" : "You don't have a key that fits.", has ? "green" : "red");
                    PlaySound(has ? 386 : 326, me, has ? 30 : 20);
                });
            }
            if (!all)
            {
                Later(3f, () => { Notice("The vault door remains locked.", "red"); PlaySound(384, me, 30); });
                return;
            }
            foreach (int k in keys) TakeItems(k, 1);
            vaultBusyUntil = Time.time + 3f;
            Later(3f, () =>
            {
                Notice("The vault door opens...", "green");
                PlaySound(387, me, 30);
                vaultOpenUntil = Time.time + 15f;
                DropVaultLoot();
            });
        }

        /// <summary>Sepitus's drop table (each drop 1 in its rate less one, as the vault rolled it; no silver or third key), a
        /// 1 in 14 chance each at his helmet, his earring and a chalice, and 1000-2000 silver.</summary>
        private void DropVaultLoot()
        {
            var bag = new LootBag();
            foreach (var d in LootData.OriginalDrops(Sepitus))
                if (d.typeId != 0 && d.typeId != ItemVaultKey3 && Random.Range(1, d.rate) == 1) AddToBag(bag, d.typeId, Random.Range(d.min, d.max + 1));
            foreach (int rare in new[] { 411, 412, 10648 })
                if (Random.Range(1, 15) == 1) AddToBag(bag, rare, 1);
            bag.silver = Random.Range(1000, 2001);
            bag.id = nextLootId++;
            bag.scene = ScenePoacher;
            bag.despawnAt = Time.time + LootBagLifetime;
            bag.pos = VaultLootSpot;
            SceneLoot(bag.scene).Add(bag);
            Plugin.Log.LogInfo($"[loot] vault -> bag {bag.id}: {LootLabel(bag)}");
            Send(198, 34, LootEntry(bag));
        }
    }
}
