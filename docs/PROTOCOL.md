# GSO client ↔ server protocol

This was reverse-engineered from `Scr_RPCSender` (client → server) and `Scr_RPCReceiver.OnEventCall` (server → client).

Every message is a Photon event: `(byte eventType, object[] data)`. `data[0]` is always an `int` sub-opcode. Below, `ev/sub` means that pair.

Types must match exactly, because the receiver casts with `(int)`, `(string)`, `(bool)` and `(Vector3)`. Lists inside strings use the formats shown. Numbers are parsed with the current culture, which is why the plugin forces `InvariantCulture`.

## Connection and login flow

| Step | Direction | Event | Payload |
|---|---|---|---|
| Login | C→S | 26/1 | username, encrypted pw, client version, steamId |
| Login result | S→C | 0/6 | `AccountLoginPacket` JSON, account name, `LoginResult` JSON (`resultCode > 0` = ok) |
| Create account | C→S | 0/7 | email, encrypted pw, steamId → reply 25/29 int code (1 = ok, client then logs in) |
| Re-request character list | C→S | 6/30 | → reply 7/55 `AccountLoginPacket` JSON |
| Create character | C→S | 7/47 | name → reply 3/60 packet JSON, int code; on success the client sends 7/50 |
| Delete character | C→S | 7/49 | name → reply 3/61 packet JSON, int code |
| Enter world | C→S | 7/50 | character name |
| Spawn local player | S→C | 2/42 | name, `key=value\n` block (see `Scr_PlayerHandler.addPlayer`) |
| Open character creator | S→C | 7/3 | name |
| Creator done | C→S | 7/4 | name |
| Login confirmed | S→C | 25/28 | int (> 0 ok) |
| Change scene | S→C | 3/28 | name, sceneId |
| Scene finished loading | C→S | 6/10 | — |
| Clear loading flag | S→C | 198/10 | — (must arrive before visibility updates) |
| Force position | S→C | 5/3 | name, int type (1 = fade), Vector3 |
| Logout | C→S | 7/7 | name |

## World streaming (S→C)

| Event | Payload |
|---|---|
| 2/2 visible NPCs | name, `uid_type_x,y,z_hp_maxhp_dead(0/1)_customName_animId` joined by `>` |
| 2/4 visible harvestables | name, `uid_type_hp_x,y,z` joined by `>` |
| 2/6 visible loot | name, list |
| 2/17 wayshrines | name, `id,id,id_homeId` |
| 2/28 boats | name, `x,y,z_rot_seats_owner_prefab` joined by `>` (empty = none). `seats` is `seat:name` joined by `,`; the client parses every entry as a number and a name, so an empty boat needs a placeholder (`0:`). `prefab` loads `1LoadingAssets/PlayerShips/ab_PlayerShip_<n>`: 1 small wooden boat, 2 longboat, 3 small fishing boat, 4 fishing vessel (the only one with a working crane), 5/6 Bal Sardan boats, 7 ornamental longboat, 8 old raft. Boats sit at y 3.15. Being in a seat pins the player to it each frame; the owner steers. |
| 6/9 left the boat | — (clears the player's boat) |
| 2/35 spawned objects | name, `uid_type_x,y,z_rot` joined by `>`. Loads `1LoadingAssets/SpawnedObjects/ab_spawnedObject_<type>` (10 oyster, 11 harpoon, 12 harpoon gun with interactable 46, ...); it only adds, so send each object once. The objects die with the scene, but the client keeps them in its list. |
| 14/1 move spawned object | uid, int rot, Vector3. The client glides there (slerp) when it's more than 1 m away. |
| 25/10 remove spawned object | uid |
| 2/15 ferries | name, `id_type_x_y_z_waypoint_moving` joined by `>` (the position fields are `_`-separated). Ferries are `Resources/XMLs/transportData_<scene>` (`transport` id, type = `1LoadingAssets/Transports/ab_transport_<type>`, speed; `phase` destination and `waitingtime`, waypoint = phase order). The client sails toward the current waypoint itself and is created facing north. The data's routes predate the current terrain (they run over land), so the server replaces them in `OfflineServer.Ferries.cs` and writes its routes into the client's `Scr_TransportsHandler.transportTypes[].waypoints` before the first list. It keeps the previous zone's ferries in its list after they are destroyed and throws removing them, so the server calls its `clearTransports()` before a zone's first list. |
| 2/16 quest objects | name, the ids of the quest objects to show, `,`-separated. Every `Scr_QuestObject` (the cook book, Nort's body, Kombra's book...) hides itself on load; the client shows those listed and hides the rest, and ignores an empty list, so the server sends `0` to hide them all. Rules: content.json `questObjects`; `questobjects` (bridge) lists a scene's objects and ids. |
| 20/2 ferry waypoint | id, waypoint index, moving (bool). The server runs the schedule: on arrival, a waypoint with `waitingtime` stops the ferry (moving false), then it sails to the next. |
| 16/1 weather | int time (0–2400), int cloudiness |
| 2/0 chat line | unused, text (optional `color=yellow\|` prefix), int channel |
| 2/41 notification | text |
| 7/41 flying followers | `uid_type_owner_petName` joined by `>`. Loads `1LoadingAssets/FlyingFollowers/Prefab_FlyingFollower_<type>` (1 parrot, 2 bat) onto the owner's right shoulder; it survives scene loads (`DontDestroyOnLoad`), so send it once. |
| 25/22 remove flying follower | uid |
| 2/55 flying follower says | owner, text: a chat bubble over the owner's flying follower (`/parrotsay`). |
| Walking pets | no message of their own: the PET creature type (NPCInfo `isPet="1"`, named like the item) goes in the 2/2 NPC list like any NPC. |

## Client → server, by area

Payloads are listed after the sub-opcode. "name" means the player name.

- **Movement:** 17/1 sync (name, rot, scene, Vector3); 198/1 jump; 23/1 click-target position (name, Vector3): the ground spot for the click-target ability (Fire wall, Blast, Landmine) the client sends next (3/0).
- **Chat:** 2/5 (name, msg). Messages starting with `/` are server commands.
- **Interact:** 3/11 object (name, interactable typeId) → reply 3/12 confirm; 3/8 NPC (name, uid); 3/9 dialogue option (name, optionId).
- **Targeting and combat:** 3/7 select NPC; 3/0 use ability (name, slot); 6/25 cancel attack; 8/7 set ability slot.
- **Items:**
  - 3/4 equip and 3/5 unequip (name, itemId)
  - 6/5 switch to the secondary weapon, 6/6 unequip it (no arguments)
  - 3/18 use (name, typeId)
  - 4/4 remove (itemId, typeId, amount, name): destroys; the client never sent it, GSOOffline's Destroy button does
  - 4/14 drop: the server puts the item in a loot bag at the player's feet
  - 198/15 swap and 198/17 insert (index1, index2)
  - 198/11 use an item on another: name, target item id, used item id, target type, used type (OfflineServer.ItemCombos.cs); S→C 198/16 sets an item's grade (item id, grade)
  - 198/16 sort (type, ascending). The client has already sorted its own list when it sends this; the server adopts that order.
  - 3/17 collect loot (name, lootId): the server gives the bag's contents and removes it (198/35)
- **Shops:** 8/8 buy (name, typeId, amount); 4/5 sell (name, typeId, uniqueId, amount).
- **Bank:** 4/11 add and 4/12 remove items; 3/37 silver.
- **Skills:** 8/4 start action (name, actionType, targetId), used for harvesting and crafting.
- **Travel:** 3/39 wayshrine teleport (name, id); 7/11 set home wayshrine.
- **Boats:** a boat item is launched with 3/18 use item. 2/29 board (name, owner); 5/6 boat position (name, int rot, Vector3), every 0.5 s while the owner is aboard; chat `/enterboat owner`, `/leaveboat owner`, `/removeboat owner` (the boat menu, the F key, and the client's shore check, which sends `/removeboat` whenever the boat's height leaves 3..3.25). Oyster fishing: 3/11 interactable 46 (a harpoon gun) -> the server sends 24/3 (bool) to turn harpoon mode on or off (a crosshair; the client never turns it off itself); 28/0 harpoon shot (Vector3: the point under the crosshair); interactable 47 is the fishing vessel's crane, which the client only uses from a ship with typeId 4 and answers with 28/1 (Vector3: the net, 4 m off the vessel's side) after the 3/12 confirm; 7/30 (name) plays the crane.
- **Housing:** 3/11 interactable 20 (a house door: yours opens with 20/1 (houseId, scene, bool) and shuts after 10 s; a ruin's opens the Building window, 3/33 type 13) -> 3/34 build (name, houseType). 17/2 place furniture (name, itemType, int rot, Vector3), 5/4 pick up furniture (name, itemType, Vector3), 6/22 rotate the house (reply 16/6 houseId, rotation), 7/8 House window data (reply 2/13 `name_locked_owners_whitelist_blacklist_guildId`), 198/7 buy furniture space, 198/8, 198/9 and 198/10 ask its cost, total and used (replies 198/11, 198/12, 198/14, each one int). Chat `/demolishhouse`, `/lockhousedoor`. Guest lists (18/1), visitors (7/9), selling and guild houses (3/52, 6/18) were multiplayer only. S->C 2/12 (name, list): the houses near the player, `id_type_x,y,z_furniture_forceUpdate_name_owners_size_rotation` joined by `>`, where furniture is `type,rot,x,y,z` joined by `|` (or `0`), the position is the plot's centre, type 0 is a ruin (`ab_poh_0`, `ab_poh_0small` up to size 6), and forceUpdate 1 rebuilds a house the client already has. The client drops houses missing from a list.
- **Other players** (S->C, used for the bounty board's outlaws): 2/8 (name, list) visible players, `name_hp_max_equips_x,y,z_gender_hair_body_eyebrow_r_g_b_size_pvp_criminal_title_bounty_guildJson_secondary_emblem_emblemBg_nameColour` joined by `>` (integer position; players missing from a list are removed); 5/2 (name, int rot, Vector3) position, which the client glides to (snaps beyond 6 m) and walks to by itself; 7/22 (`name,` + Scr_Animator.AnimationPacket JSON) pose: b1 combat, b3 dead, i4 attack animation, layer weights f6/f10 for the combat stance; 3/20 (name, animation id) restarts an attack (broadsword/mace/dagger 2, greatsword 15, halberd 14, bow 3, staff 4, crossbow 10); 8/2 (name, -damage, splash) hit splash; 8/13 (name, hp, max); 1/2 (name, dead); 18/0 (owner, target, projectile) a projectile between players; 2/23 (you, name) select a player target. C->S 2/9 (you, target name) selecting one. 3/19 with an NPC uid clears the player target.
- **Wanted board:** interactable 43 at a board; 3/33 window 36 (opening it clears the list), then 7/31 per entry (Scr_WantedBoard.WantedCriminal JSON: playerName, lastArea, criminalTimer, murderCount, bounty; a criminalTimer over 1800 shows "Outlaw" and an "Add bounty" button). 7/43 (text) a large notification window.
- **Auction house:** interactable 40 (a stand) -> 3/33 window 31, which GSOOffline draws itself (AuctionWindowPatch: two lists, no search or posting). The client then sends chat `/requestmyoffers` (the window opens on "My sell offers"), answered with 4/15 per sell offer (item data string, offerId, amount, price) and 7/40 per buy order (Scr_Packets.AHBuyOffer JSON: offerId, itemTypeId, amount, price). Buying: question 7 sends chat `/buyoffer id` (only for an offer in the client's `Scr_AuctionHouse.offers`), then `/requestoffers keyword`. Selling: question 20 sends 25/13 (offerId), then 3/55 (keyword, startIndex). 3/54 searched sell offers. Posting and removing your own offers (31/1, 25/11, 6/23, chat `/addoffer`, `/removeoffer`) and interactable 41 (the silver booth, chat `/collectsilver`) were player trading.
- **Appearance:** 3/22 gender, 3/23 hair, 3/24 body type, 3/25 eyebrows, 3/26 body size, 4/6 hair colour (r, g, b); 8/9 fighting style and profession.
- **Ignored offline** (heartbeats and telemetry): 7/0, 3/47, 6/27, 7/18, 7/21, 27/0, 198/2, 198/3.
- **Message board:** interactable 26 (a town board) -> 3/33 window 27 ("Messages"). Opening it clears the list and sends 3/45 (name, int page 1-10); the reply is one 2/33 (name, Scr_GlobalMessageBoard.Message JSON: subject, message, playerName, date) per post, ten to a page (the original's paging). "+ New message" sends 0/5 (name, subject up to 30 characters, text up to 300), then 3/45 page 1. GSOOffline's board is read-only: it holds the developer's posts and answers 0/5 with a notice.

## Items, dialogue and shops (S→C)

| Event | Payload |
|---|---|
| 2/1 add item | item data `Key=Value
...` (Id, Typeid, Amount, Tab, Grade, Slot, stat keys), player name. For stackables, Amount is the delta. |
| Stackable ids | Stackables (`typeId > 10000` and not `isUnique`) are `Id=0` in every message: 2/1, 2/14, 4/3, 4/13. The inventory and bank windows only draw a stack's count when its id is 0, and the client finds bank stacks by id. |
| 8/0 equip | name, itemId, replaced itemId (0 = none). Send after the world scene has loaded, or the slots stay empty. |
| 3/6 unequip, 4/3 remove | name, itemId [, typeId, amount] |
| 8/19 secondary weapon | name, itemId, typeId (0, 0 = none). Drawn on the back; the inventory hides that item. There's no client message to put a weapon there: the server does it when a weapon is equipped over another. Switch (C→S 6/5) swaps it into the hand; 6/6 takes it off. |
| 198/37 swap, 198/38 insert | inventory indexes (server list order must match the client's) |
| 3/16 silver, 8/1 XP | name, value / name, skillId, amount |
| 8/13 health | name, current, max |
| 3/10 + 2/3 dialogue | confirm interact (name, uid), then node `nodeId>opt\|opt>var1\|var2`; `-` closes |
| 8/9 quest phase, 3/15 complete | name, quest, phase / name, quest. Rewards must be granted by the server. |
| 0/2 open shop, 2/37 refresh | name, shop name, `0>type-price-stock,...` / shop name, `merchantSilver>type-price-stock,...` |

| 8/4 start action (C→S) | name, type (1 harvest, 2 craft), target (harvestable uid / recipe id) |
| 8/5 action started, 3/13 complete, 3/14 cancel | name, type, target / name, type / name, type. The client re-issues 8/4 for the next item in a crafting batch on 3/13. The gathering animation (from the node's tool type) only plays outside the attack stance, so send 7/2 first; the hand shows the equipped weapon, so equip the tool. |
| 13/1 harvestable status | uid, depleted (bool) |

| 198/33 mount | mount id (Prefab_Mount_N: 1 pony, 2 turbobeetle; 0 = get off), player name. Puts the local player on the mount (`Scr_PlayerMount.ActivateMount`); other players' mounts aren't drawn. Never send 0 while the client shows no mount: it throws. The client's own `/mount <n>` (with a space) does the same without telling the server. |
| 198/34 add loot bags | `id_x,y,z_name_typeId` joined by `>`. The client only adds (sending a bag twice shows it twice) and loses bags with the scene, so resend a scene's bags once after it loads. `name` is the hover text (`-` = none); `typeId` picks the ground model (equipment with a model, else the default sack) and must be a real item. Also 2/6 (name, list). |
| 198/35 remove loot bag | id. Also 25/14. |

| 3/7 select target (C→S) → 3/19 | name, npc uid (0 clears) |
| 3/0 use ability (C→S) → 3/1 | name, slot (0 = basic weapon attack) |
| 3/20 attack animation | name, attack id (0 = basic attack for the equipped weapon). Puts the player into attack mode until 7/2. |
| 7/2 stop attacking | name. Ends the player's attack animation and combat stance; send it when the target dies, the attack is cancelled (6/25) or the target is cleared. |
| 12/0 NPC health | uid, current, max, damage type (the client shows the difference as a hit splash) |
| 11/0 NPC attacking | uid, attacking, target player name (combat state only; it plays no animation) |
| 198/26 NPC animation | uid, animation id. The client plays only what the server sends: idle, run, attack (each swing) and death. Ids index the client's shared clip table (`EasyAnimationHandler.animations`) and come from `anim_idle`/`anim_run`/`anim_attack`/`anim_death` in NPCInfo (client defaults 28/29/39/30). Id 0 is an empty clip that leaves models in their bind pose. |
| 198/25 + 13/2 NPC move | uid, current pos, waypoint1, waypoint2 / uid, moving. The client walks in a straight line, height included (never put on the ground), so a waypoint in the air flies the NPC there (Aellope). |
| 198/31 NPC movement speed | **speed, uid** (this order: the client's `UpdateNpcMovementSpeed(v1, uniqueId)`), int metres per second. |
| 13/0 NPC dead, 9/1 set NPC position | uid, dead / uid, pos |
| 8/2 player damage | name, signed delta (negative = damage), damage type |
| 1/2 player dead | name, dead |
| 7/7 log out (C→S) → 7/1 | name / name. The client waits for 7/1, then tears down the world (sending 7/7 again, ignored) and returns to the login screen; the server logs the account back in, which shows character select. |
| 1/4 player can move | name, can move. The client never stops movement by itself (not even when dead); `false` also zeroes the velocity. Sent on character creation, death and respawn. |
| 4/0 ability cooldown | name, slot, current, total (hundredths of a second) |
| 4/9 add buff, 3/35 end buff | name, buff id (XMLs/Buffs), time left, total time (tenths of a second) / name, buff id. The icon shows by the health bar; the effect is the server's. |
| 3/18 use item (C→S) → 3/24 | name, item type id / name, type id. The reply opens item UIs (knife, mortar, experience scroll picker, furniture placement). |
| 16/3 experience scroll (C→S) | skill id, scroll type id (sent from the picker window, with no name) |

Effects, projectiles and sounds. The client plays nothing on its own: every effect is a server message. Effect ids are the game's "_GFX IDs" list (1 level up, 16/17 spruce/oak falling, 40/41 teleport/land...); the client's own level-up banner is never triggered.

| Event | Payload |
|---|---|
| 9/0 effect at a position | effect id, Vector3 |
| 5/1 effect following a player | name, effect id, Vector3 |
| 16/2 effect following an NPC | npc uid, effect id |
| 3/41 right-hand effect | name, effect id |
| 17/3 sound | name (local player only), sound id, hearing distance, Vector3 |
| 8/3 projectile player -> NPC | name, projectile id, npc uid. Starts 2 m above the player. |
| 8/10 projectile NPC -> player | name, projectile id, npc uid |
| 21/0 projectile between two points | projectile id, from, to |

Projectiles (XMLs/Projectiles) fly at `speed` x 1.1 m/s and play their travel sound, but end silently: the impact effect (`endgfx`) and sound (`endsound`) are the server's to send when it arrives. Data that names sounds and effects: NPCInfo `sfxattack`/`sfxtakehit`/`sfxdeath` (comma lists) and `projectile`/`projectilerate` (percent of attacks)/`projectileattackdistance`; Abilities `startsfx`/`hitsfx`/`startgfx`/`hitgfx`; HarvestableInfo `sfx` (per swing) and `endgfx` (used up).

Town guards are `aggressive` with 1500 damage in NPCInfo. The old server only aimed them at criminals.

Recipe ids are the client's `Script_Crafting.craftingIdCounter`, which counts each `Crafting` stat in item file order. An item without a `Level` attribute has level 0, not 1. The basic tools are hard-coded in `Scr_ItemHandler` (e.g. `isPickaxe` = item 59), not flagged in the XML.

Unity's `JsonUtility` cannot serialize lists of classes defined in a plugin assembly. Use it only for the game's own packet classes.

## Interactable typeIds (`Scr_Interactable.typeId`)

| Type | What |
|---|---|
| 1–8, 17, 18 | Workbenches: forge, anvil, stove, spinning wheel, loom, tailor, grinder, alchemy, carpentry, saw |
| 21 | Wayshrine |
| 26 | Message board (opens window 27) |
| 27 | Campfire |
| 43 | Bank chest and other chests, wanted board, levers |
| 40, 41 | Auction house stand (opens window 31) and silver booth |
| 55 | The Arena's Sign-up board (1v1 and team fights; the server once answered 2/37, window 39). Offline: the artisans' hall's way down and up. Workbench furniture (ab_furniture_10396 etc.) carries interactables 1-6, 17, 18 and opens crafting windows 1-9 anywhere |
| 9–16, 20, 22–25, 28, 29, 33–38, 42, 45 | Doors, gates, hatches and portals. The server mapped each to a destination; this has to be rebuilt by pairing doors across scenes (`GSODevTools/extracted/markers.json`). |
| 37 | Generic doors (158 of them, mostly house and interior doors) |
| 54 | Chairs |
| 20 | House doors (the client remembers which house for the Building window) |
| 49-53, 56, 62, 67, 70, 72, 73 | House furniture: trophies, gallows, activity book, teleporter, Stone of Swiftness, PvP trophy, organ, bank chest |

## Data sources

| Data | Source |
|---|---|
| NPC stats, AI parameters | `Resources/XMLs/NPCInfo` |
| Dialogue trees with quest requirements and updates | `Resources/XMLs/Dialogue` |
| Quests and phases | `Resources/XMLs/Quests` |
| Harvestables, drop tables | `Resources/XMLs/HarvestableInfo` |
| Monster drop tables | Not in the client. `Data/loot.json`, generated from the community wiki by GSODevTools `gen_loot.py` |
| Items, crafting recipes | `Resources/Data/items` |
| Abilities, buffs, projectiles | `Resources/XMLs/Abilities`, `Buffs`, `Projectiles` |
| Wayshrines | `Resources/XMLs/Wayshrines` |
| House types (cost, materials, plot size) | `Resources/XMLs/HouseTypes` |
| House plots | Not in the client. `Data/houseplots.json`, the original server's `HousePlots_<scene>`, by GSODevTools `gen_houseplots.py` |
| NPC and harvestable spawn points | `Scr_NPCDummy` / `Scr_HarvestableDummy` in each scene |
| **Lost** | NPC loot tables, shop stock, door destinations, server-side formulas (HP, damage, XP) |
