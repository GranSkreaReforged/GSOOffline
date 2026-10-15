# Changelog

All notable changes are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
`release.ps1` turns the Unreleased heading into a version heading.

## [Unreleased]
### Added
- Ulan's dungeon works as in the original game. Its gate is back and locked: search the skeleton for a broken key, put the halves together, press the fragile key into soft clay from the dungeon's clay deposits to make a mould, and smelt iron ore into it at the forge for a strong key (a fragile key snaps in the lock). A lever behind the gate opens it on the way back. The lift between the upper floors runs again, a stop every 20 seconds. The door to the Cave Dragons opens when both levers in the hall are pulled; the original reset each lever after 2 seconds, which took two players, so offline a lever stays down for 30 seconds. During A Life Experience, Ulan follows you through the dungeon until you reach its lowest floor.
- The quest "Lakhmu's basement" can be played, as in the original game. Isthar the baker in Bal Sardan town worries about the noises from her neighbour's basement; the hatch is locked, and Alorus the locksmith wants three barrels taken to the town's taverns before he hands over a lockpick. Pick the hatch's five locks by ear (it ticks; turn the lock on the right tick), find Lakhmu cornered by an ancient wraith deep in the caves, kill it, and lead him back to the ladder. He gives you his backpack, and makes another for 20 Bal Sardan vines and 5 iron ingots. The dungeon beneath, home of the Tomb guardians, is reachable again through the hatch, and its ladder leads back up. Mind the falling rocks in the caves: their dust gives you 3 seconds' warning.
- Creatures, characters and gathering nodes the original server placed but the game's own scene data never marked: six Argi in Bal Sardan, Wynford, the Logger, Ferrarius and the Travelling Axe Seller in East Athagos, Joannia Forbor and the Master tailor in Yorkhill, Joseph Meade in the Poacher dungeon's jail, Goldon's cabbages, and 19 ore and resource nodes in the Poacher dungeon (coal, iridium, saltpeter, sulfur, redberries). Their positions come from the original server's spawn lists.
- Creatures drop what they dropped in the original game, at the original rates, from the original server's drop tables. Most drops are much rarer than before (a wolf's tooth is 1 in 15, a cow's scythe 1 in 30), and a few creatures drop things the wiki never recorded. Every creature can also drop an upgrade or grade stone and now a gear crate for its level, as in the original. Creatures from 2020 updates (Bruno, Betsy, Lakhar, Lydia, Jotnar, Argus, the Elder Ent) and the boss uniques and pets added in updates 137-140 keep their wiki and patch-note drops.
- Three boat rides from the original game: the sailor at the north of Bal Sardan takes you to the southern marshes and the one there brings you back (50 silver each), and the fisherman by the Yorkhill Monastery drops you at the beach by the Coral Cave for free. Their options did nothing before.
- Shops have limited stock, as in the original game: each item shows how many are left (in red when sold out), the shopkeeper sells you only what's on the shelf, and sold-out items come back one at a time (every 5 seconds unless the original stock list says otherwise). Resources you sell to a shop that stocks them go on its shelf.
- The Mantle of St. Jakob navigator answers "Have you sighted any harpies yet?" with where Aellope is flying, or how long since she was last hunted down, in the original game's words. The quest itself is still to come.
- Aellope flies again, as she did in the original game. She patrols one of five routes 30-40 metres above Goldon (west, the centre, north, north-east or south), back and forth. Hit her and she dives at the spot you stood on, lands and fights. If 20 seconds pass without her taking damage, she takes off healed and goes back to her route. If you die while she's down, she flies to your body and feeds on it, healing 30% (at most every 45 seconds). Shot down in the air, she falls to the ground with her loot. Killed, she returns an hour later on a new route. This is how the original server ran her; the routes and timings come from its code. She used to stand still in the middle of the island.

### Changed
- Every shop sells what it sold in the original game: 43 shops, 650 items, from the original server's stock lists (the game's developer sent us the server). Shopkeepers used to sell items picked by theme. The Grimwall Tool vendor now sells the original Tools stock, which includes the knife and the mortar and pestle.

### Fixed
- Bosses stand where the original server put them: Nirish, the Supreme Sea Serpent, George and Sepitus a few metres from where players remembered, the three Tomb guardians spread across Lakhmu's basement instead of huddled together, Phraustie a little west, and Sytrax, who turns out to be Sepitus's dragon, beside Sepitus instead of in the graveyard. Positions from the original server's spawn code.
- A zone could get stuck reloading: when the game's own scene setup failed part-way (its minimap broke), it re-ran every frame, and the server rebuilt the zone each time, resetting every creature and boss and killing a character who had just arrived. A failed setup now runs once, and a repeat is ignored.


## [1.5.0] - 2026-10-15
### Added
- Boss fights have their mechanics back, as the patch notes describe them. Nirish drops poison clouds at your feet (2 seconds to get out) and calls a spiderling that heals him until you kill it. The Supreme Sea Serpent calls two sea serpents, leaves fog fields that cost 5% of your health each second you stand in them, and sets off water explosions after a warning mist. Zalranaz winds up psychic explosions around himself and stuns you for 2 seconds; the Tomb guardians explode every 30 seconds. Sepitus summons lava hounds, Lydia her skeletal berserk and scout, and Jotnar slams the ground around him. Each uses the effect the game made for it. Summoned creatures carry no loot and vanish with their boss. Timings and damage are reconstructions.
- Absorb, for Zalranaz and the Tomb guardians (the patch notes name it but never describe it, so this one is made up): the boss warns you in a speech bubble ("I will drink your strength...") and chat says to stop attacking; 2 seconds later, for 4 seconds, every blow or spell you land comes back at you. Another bubble and a green chat line say when the shield is down. Zalranaz's red aura now shimmers only during Absorb.
- Pets. Put one in the Pet slot and it walks beside you, catches up after teleports and comes along between zones. Once you attack something it runs in and fights it too, as update 132 describes: each blow is half your own basic hit, more for better grades (94% Damaged to 136% Mythic, update 138). Creatures can't target pets. All 30 walking pets work, from the Bear and Infernal Hound to the Snowman. The parrot and the Halloween bats sit on your shoulder instead and add 8.5-19% to your damage by grade (update 136); `/parrotsay <text>` makes the parrot talk. The damage share is a reconstruction.
- The quest "Sightseeing" can be played. Talk to Nora in the Salty Wound inn in Grimwall and show her West Athagos: she walks with you (and catches up if you teleport) to the Yorkhill Monastery, the farm's cows, the beach by the Coral Cave and the golem in the forest west of Grimwall, then back to the inn for her reward. Tell her "Yes, we're here" at each stop. "Can we continue later?" sends her back to the inn; talk to her there to carry on. The old server walked her and knew the stops, so these were rebuilt from the quest log's descriptions.
- Armor alignment, from the game's update 135: wear enough light or heavy armor (chest 4 points, legs 3, head 2, back, gloves and boots 1 each; 7 needed) for its passive. Light armor deals 15% more damage, heavy armor takes 10% less. The game's "Light armor" / "Heavy armor" icon shows next to your health bar while it's active.
- Your combat level shows in the character window (update 136: your three best combat skills, plus a twelfth of the others). It used to read 0, and so did every "Effective res" line, which now shows your resistance after the soft cap.
- Bosses give double experience (update 137).
- Bosses drop the uniques and pets the official patch notes name: the Storage Guard's armor set and crossbow and the Poacher pet (George), Zalranaz's cleaver, the Dark Tamer set and Infernal Hound (Sepitus), Nirish's venomous ring and amulet, the Tomb guardian's amulet, Serpent's Fang (Supreme Sea Serpent), Roke's ring, Lydia's staff and earrings, Jotnar's body and chaps, the Bear (Bruno), Young Ghit (Betsy), Lizard warrior (Lakhar), Argus and Ent (Elder Ent) pets, and the Elder Ent's crafting materials. Pets are rare (4%), uniques uncommon (15%); the rates are reconstructions.

### Changed
- Bosses are tougher than ordinary creatures of their level, as the patch notes made them: 2.25 times the health and 1.35 times the damage (updates 136, 137 and 139 compounded). Zalranaz has 1642 health instead of 730.
- Resistances follow the game's soft cap (update 136): full value up to 60%, then diminishing to a hard cap of 90%. They were cut off flat at 75%.
- Broadswords and bows always deal at least half their maximum hit with basic attacks, greatswords 30% and halberds 65% (update 130). Brutal slash deals at least 75%, Cripple 50% and Fire ball 60%. Every hit used to roll from 1.
- After a stun wears off, a creature can't be stunned again for 4 seconds (updates 117 and 131), so stuns can't chain forever.
- No single blow takes more than 48% of your maximum health (update 140).

### Fixed
- Phraustie the Snowman swings once every 3 seconds. The game data gives him an attack speed of 0, so he struck every frame and killed anyone who came near.
- Argus is a boss, as update 139 calls him: boss health and damage instead of a creature's (his data damage, 8, is on the pets' scale, so he hit for at most 6).
- Eight bosses the game had no spawn point for are in the world: Bruno on the bear island of East Athagos, the Elder Ent in its forests, Lydia in the hills south of Trout, Jotnar among the trolls south of Trout, Betsy among the north-west ghits of Bal Sardan, Lakhar among the eastern lizard warriors of Bal Sardan, Aellope on Goldon and Phraustie the Snowman on Ruteran. The old server placed them; their spots follow the patch notes' descriptions and stand on the ground of a creature of their kind. Sytrax and Viper are placed too, though nothing records where they were: Sytrax in the poacher dungeon's graveyard (his data sits with the poacher dungeon's bosses), Viper deep in the Yorkhill sewers, away from the entrance and from Nirish (he spits Nirish's venom).
- The Tool vendor in Grimwall's market can be talked to: he opens a shop of basic gathering tools (hatchet, pickaxe, fishing rod and net, sickle, scythe, shears, shovel and torch). The game gives him no dialogue, so clicking him did nothing. General stores now stock those tools too, as update 135 says they did: the basic ones were missing because the game marks them in code rather than in the item data. They no longer sell the Dragon torch, a level 56 craft.
- Skill abilities need the right gear, as in the original game: Elemental grimoire spells a staff or wand in hand, Healing's lute songs and spells a lute (bandage heals only need bandages), Swordsmanship skills a broadsword, greatsword or halberd (update 121), Archery skills a bow or crossbow, and the light and heavy armor skills 7 points of that armor worn (update 135). Any of them used to work with any weapon. Without the gear you get a message saying what's missing.
- Execution deals double damage to stunned targets, as its tooltip says. It doubled against badly hurt targets instead.

## [1.4.0] - 2026-10-14
### Added
- Drag an item from your inventory onto the bank window to deposit it, as double-clicking does: a single item goes straight in, a stack asks how many, and holding Ctrl deposits the whole stack.
- The mod's version is shown in small text in the top-left corner, with "NEW UPDATE AVAILABLE" in gold when a newer release is out on GitHub. The game asks GitHub once at startup; only the version number is read and nothing is downloaded. Turn either off with `Updates.ShowVersion` and `Updates.CheckForUpdates` in the config.

### Fixed
- Log out (in the Escape menu or the options) takes you back to character select, where you can pick another character or the same one again. It did nothing. Your character is saved first; logging out while dead wakes you at full health on your next login.
- You can't move while dead. WASD used to drag your body along the ground until you woke at the wayshrine.
- Escape closes the crafting window (anvil, stove, loom and every other workbench), like its Close button. It used to open the game menu on top and leave the crafting window open; the original game did the same. As with the Close button, it waits while an item is being crafted.
- Sitting works on every bench. On some (the west bench at the Yorkhill tavern's long table, a bench in another Yorkhill building, a bench by the monastery and two seats in Grimwall) you stood straight back up: settling onto the seat counted as walking away. Stepping off still stands you up.
- Grimwall has one banker and one insurance advisor again, not two of each standing in the same spot. The game places a few creatures and people twice on the same spot (also a skeleton on Goldon); the second copy is now skipped.
- "A Cook Book" can be finished: the large cook book now sits on the monastery kitchen shelf while the guard's errand is open, and disappears once you take it. The game hides such quest objects until the server lists them, which never happened. Nort's body now lies in the monastery yard during "A Deadly Investigation", and Kombra's book is in Yorkhill library during "Growing Pains".
- Doors, gates, hatches and ladders that said "The door is locked." now take you through, both ways: the poacher dungeon (East Athagos), the Yorkhill sewers (the hatch in Yorkhill and the ladder below), the Bir Amez crypts (Bal Sardan), the Bal Sardan town gate, and Yorkhill's city gates. The old server knew where they led; that table is lost, so these were rebuilt from where players stood to use them.
- The lighthouse inn's door opens the Innkeeper's room, and Yorkhill's harbour inn opens the Tavern keeper's. Both used to drop you in the wrong room (Yorkhill's opened Kombra's house). Leaving takes you back out where you came in.
- Deepheart's doors stay locked on purpose: the area was never finished (its towers have no roof to stand on and its cave is empty).
- Eight bosses the game has no spawn point for are back in the world: Nirish, King of the Sewers (Yorkhill sewers), Sarah, Camp Overseer (the poacher camp in East Athagos), the Supreme Sea Serpent (the Blue Sea), the three Tomb guardians in Lakhmu's basement, and George, Storage Guard, and Sepitus, the Dark Tamer (the poacher dungeon). The old server placed them, so they never appeared. Their positions come from players' memory and may be a little off.
- "An Honest Day's Work" can be finished again: the large aggressive crab Nort asks you to slay at the fishing pier (step 4) now appears on the beach beside him. The game has no spawn point for it (the old server placed it), so nothing ever appeared and the quest was stuck.
- Crabs and other creatures no longer walk through rocks. They walk around rocks, walls and trees, and only wander to spots they can reach. A rock taller than a creature's step used to fool the server's ground check, so creatures walked straight through it (on the monastery beach, about one crab in ten stood inside a rock at any moment). Creatures chasing you still reach you.
- Creatures respawn after 2 minutes, as the community wiki records, instead of 1. Bosses and quest creatures use the respawn times the game data gives them, which are in minutes, not seconds: Aellope comes back after an hour (the wiki agrees), Phraustie after five, Gery's ambush after one. They used to return within seconds to a few minutes.
- Mana from gear counts on your mana bar. Items with Mana raised your maximum on the server, but the game was never told, so the bar and stats window stayed at your base mana. Gear Mana regeneration also works now: each point adds to the mana you regain every 3 seconds.
- Every Elemental grimoire spell shows its effect. Fire wall, Blast and Landmine go where you click (within the spell's range) with their wall of fire or explosion; they used to go off around you with nothing to see. Trail blaze, Immolate and Dragon flames show their flames on you, and Flames and Eruption on your target. Which effect belongs to which spell is matched by name from the game's effect list.
- Damage on all your gear counts: rings, amulets, armour and shields add their Damage and elemental damage to your hits, not just your weapon. Golem's ring, for example, adds 30-40.
- Damage block on armour works. Every item stored it under a misspelt name, so it never reduced the damage you took, and item tooltips didn't show it. Your existing items are fixed when you log in. Block now absorbs a share of each hit that shrinks against stronger creatures: about a third with gear of the creature's level. The old rule (half your block off every hit) would have made you untouchable once block applied, so this is a reconstruction.
- The character stats window shows your gear: damage of every kind, damage block, resistances, health and mana regeneration, healing, carrying capacity and the rest. It only ever showed health and mana. (Movement speed from gear still doesn't apply: the game would replace your whole run speed with the bonus.)
- Gear Health regeneration works: each point adds to the health you regain every 3 seconds out of combat.
- Hits you take show the right style. Spider and viper hits were drawn as "blocked", Infernal hound and Wallace hits as "weakened", and Ent hits as "poisoned"; poison hits now show as poisoned and the rest as plain hits.
- Dying and waking at a wayshrine in another zone no longer throws an error.

### Changed
- Carrying more than your capacity (150 plus Carrying capacity on your gear, the "Weight" in your inventory) slows you down. Just over, you run at 80% speed; it drops quickly the more you carry (about half speed at a fifth over, 40% at a quarter over, 16% at half over) but never below 10%, so you can always crawl home. A chat message tells you when you become overburdened and when you no longer are. Weight was shown but never mattered. The original game's rule is lost; this is a reconstruction.
- Conversations close when you walk away, like shop and bank windows: quest NPCs, shopkeepers and everyone else. They stayed on screen, hiding your health bar and action bar, until you picked an option. A trip you already agreed to in the conversation still happens.
- Your maximum mana grows by 5 with each Elemental grimoire level (105 at level 1, 300 at level 40), on top of your gear. Everyone had 100 whatever their level, so Fire wall (185 mana) and Flame barrier (120) could never be cast without mana gear. How the original game grew the mana pool is lost; this is a reconstruction.
- Creatures have their strengths and weaknesses. Each has a resistance to every kind of damage in the game data, which was ignored: your hits are now split into their kinds (your weapon's slash, blunt or piercing, your gear's elemental damage, a spell's element), and each part is reduced or raised by the creature's resistance. Some fights need the right approach: Spiderlings and Snakelings shrug off physical and fire damage (use ice, electric, psychic or earth magic), Ents and Ghosts resist physical damage (Ents burn), Green Arachnids are weak to fire and piercing, undead and golems are immune to poison. A hit that does nothing shows as blocked; one that hits a weakness as weakened.
- Snowmen, Phraustie and the Snow Apostle can only be hurt with the Aggressive snowball spell (Archery 10), as the community wiki records. Anything else bounced off them in the data, but the server let swords kill them.
- Your gear resistances work: each point of a resistance takes 1% off creature blows of that kind (up to 75%). A full set of one level gives about 40-60 in each.
- How often a creature's blows land depends on its accuracy: from 70% (skeletons) to 95% (ghosts, snowmen), instead of 85% for every creature.
- Damage block also takes a flat bite (a twentieth of your block) out of every blow after the share above, so a well-armoured player takes nothing from creatures far below them, while creatures of their own level still hurt. Block applies to blows of every kind, on top of the matching resistance.

## [1.3.1] - 2026-10-11
### Changed
- Each release is packaged into its own folder, `dist\<version>\`, with both zips, `SHA256SUMS.txt` and the release notes (`RELEASE_NOTES-<version>.md`, written by hand). Releases used to share one `dist\` folder, where each release overwrote `SHA256SUMS.txt`.

## [1.3.0] - 2026-10-11
### Fixed
- Shop and bank windows close when you walk away or travel (a door, a wayshrine, a teleport), as crafting windows do. They stayed open, so you could keep buying, selling and banking from anywhere.
- Closing a shop no longer says "Unknown or unavailable offline command: /closeshop".
- Chairs can't be used to hop across the room. Using a chair puts you on its seat, and the mouse cursor could reach chairs several metres away, so you could teleport short distances. Chairs now work from 2.5 m ("Move closer to sit down." otherwise), and the cursor reaches other things as far as F always did (5 m).
- The Interact key (F) uses what your mouse cursor is over, if it's within reach, and otherwise the nearest thing. It used to pick any NPC within 5 m over everything else, and of the objects the first one the game listed rather than the nearest, so next to an NPC you couldn't use a chair or a door with F, and a chair could win over the door beside it (in the monastery, for example). The prompt shows what F will use, and says "sit down" for chairs instead of "interact with".
- Sitting on chairs and benches plays the sitting animation. The game puts you on the seat and waits for the server to start the animation, which it never did, so your character stood in the chair. Walking off, attacking, gathering or changing zone stands you up again.
- Weapons have their abilities again, on keys 2 to 5. Those four slots belong to the weapon in your hand: the game doesn't let you drag anything onto them, and the server never filled them, so no weapon ability could be used. Each weapon now brings its own, and its item window lists them: swords Brutal slash, Slashing, Cripple and Sword throw (or the fire, ice or electric attack on elemental swords); greatswords and halberds Stun instead of the throw (Halberd ice attack on icy halberds); daggers Stab, Poisonous stab, Frenzy and Slashing; maces Stun, Cripple and Thoracic trauma; bows Flame, Poison and Explosive arrow and Hunting net; crossbows Focus, Piercing shot, Wounding shot and Weakness arrow; staffs Fire ball, Flame bash, Fire wall and Flame barrier (earth staffs Rockslide and Landmine, ice staffs the ice spells); wands Condensed psyche or Condensed ice; lutes Tritone and Peace; torches Torch throw and Burn. Marcus's cutlass gives Broadsword fire attack and Sword throw, as the community wiki records. Which weapon gives which abilities was the server's and is a reconstruction from the ability descriptions. Switching weapons swaps them.
- No more green heal numbers ticking over your head after a fight while your health bar is full. Health on your gear (and Survivor's bonus) raised your maximum health on the server but was never sent to the client, which kept its bar capped at your base health and showed every regeneration tick above that cap as a heal. The client now gets the bonus with every health update, so the bar shows your real maximum (105 with the starter kit, for example). Equipping or removing gear updates it at once.
- Yorkhill's shops and the bank open. Every door there said "The door is locked": the server's list of which door leads into which room was lost. The bank, the general stores, the jeweller, the blacksmith (fine blades), the archery and clothing stores, the artisans' guild, the auction house, the library, the governor's manor, Valeria's shop, the gem, food and fish stores, the surgeon, the tavern and the Hall of Heroes now take you inside, next to the shopkeeper, and their doors take you back out where you came in. Shop signs name most buildings and quest texts place the manor, Valeria's shop and the library; the gem, food and fish stores, the second general store, the surgeon and the tavern went to the unsigned doors that suit them best, so those may not be the original buildings. The library is where "Flora of Keshk" (quest 26) is read, and the manor kitchen leads down to the rat cellar (quest 27).
- Changing zone mid-conversation closes the conversation. It stayed on screen in the next zone, hiding the health bar and hotkeys and holding the camera.
- Changing zone while gathering no longer leaves your character mining, chopping or fishing forever. Teleporting, taking a door or a wayshrine stopped the harvest on the server but never told the client, so the gathering animation carried on in the next zone (in a boat, on a ferry...) until the next action.
- Teleporting with a crafting window open closes it. It stayed open in the new zone until you pressed a movement key (Escape doesn't close it), and while it sat under the cursor the camera couldn't be turned with the mouse. A craft in progress is cancelled.
- The game's own chat commands (`/showgui`, `/fps`, `/ping`...) no longer answer "Unknown or unavailable offline command". The client runs them itself and also sends them on to the server, which now leaves them alone.

### Added
- `useobj <typeId>` and `windows` bridge commands: use the nearest object of a type as a mouse click does, and list the open windows.
- `hover` and `interactprompt` bridge commands: act as if the mouse cursor were over an NPC or object, and show (or press) what the Interact key would use.
- `actionanim <id>` bridge command: sets the player's animation action (12 sits, 15 sits on the ground, 3 lies down...; the ids are the player animator's ActionID values).
- `building [n]` and `doorsigns` bridge commands: go into each building as its door would, and list the shop signs with their nearest doors.

## [1.2.0] - 2026-10-11
### Fixed
- Hits, shots and their sounds land when the attack animation strikes, for every weapon and ability, instead of the moment the attack starts. Swords, daggers and maces hit 0.65 seconds into the swing, greatswords 0.7, halberds and fists 0.5; bows release their arrow at 1 second, staffs and wands cast at 0.5 and lutes at 0.3. Abilities strike with the animation they play (Fire ball leaves the staff with its cast, arrow abilities with the bow's release), and Slashing deals its damage over its five slashes. Abilities end when their animation does, and basic attacks carry on after them without cutting into it.
- Attack speed buffs (Frenzy, Hack and slash, Focus) raise damage per hit instead of cutting the attack animation short. Damage per second is the same as a faster swing would give.
- Basic attacks play their whole animation. The server swung on its own 2.4-second timer, which cut the weapon's attack animation off midway and restarted it, so attacks looked fast and choppy. Swings now follow the animation's length (1.6 seconds with a sword), and each hit does proportionally less, so damage per second is unchanged.
- Attacks make sound. Basic attacks play a swing and an impact (daggers use the stab sound; bows and crossbows the arrow abilities' shot), and abilities the game data gives no sound now sound like your weapon, or play the heal chime for heals and buffs. The original server sent these and no data names them, so they are picked from the sounds the ability data uses for weapon blows.
- Monsters turn to face you when they attack, and their blows land partway into the swing (each monster's attack delay from the game data) instead of before the animation. Stepping out of reach during the wind-up dodges the blow.
- Monsters and animals stop their run animation when they stop walking, instead of running on the spot for up to 4 seconds.
- Monsters and animals no longer sink into the ground while walking (sheep and rats around Yorkhill Monastery, for example). The client walks them in a straight line to each point the server gives, so a walk over a hill cut through it; the server now sends short steps that follow the ground.
- Your character no longer stays stuck in a skill's animation after casting it. Buffs, heals and area attacks without a target to keep attacking left the character mid-cast until the next attack; the cast now ends after the ability's own attack time.
- Talking to Alden during the first quest no longer starts "Into the Depths" early, and other quest conversations no longer open on the wrong copy of an NPC. Quest NPCs now exist only in their own phases (the dinner table, the crime scene, Roke in his tunnel, wounded Alden...), the way the original server showed them.
- Buff abilities no longer hurt every monster around you. Thick skin, Heavy shield block, Frenzy, Flame barrier and other untargeted weapon abilities were treated as sweeping attacks.
- Staffs, wands and lutes now count their damage. Only plain and physical damage stats were added to hits, so fire, ice, psychic and poison weapons hit like bare hands.
- Bows and crossbows attack from as far away as the client lets you (50 m; staffs 25 m, wands and lutes 20 m). Standing at the client's range used to leave you there never firing.
- Wands now train Mental grim and lutes train Healing, following each weapon's skill requirement, instead of Swordsmanship.

### Added
- Oyster fishing in Bal Sardan. Oysters dart about the harbour basin by the shipwright and soon vanish. Man one of the two harpoon guns on the quay (F, then aim with the mouse and left-click) to stun an oyster so it stops and stays longer, then lower the fishing vessel's net with its crane (Fishing 30) to catch it, as the community wiki and the shipwright describe. Each catch gives an oyster and Fishing XP. The bed, the guns' places, the number of oysters and the timings were the server's and are reconstructions.
- Cart riders. The four cart riders in West Athagos take you to the Monastery, the Lighthouse, Grimwall or Redwin's Farm for 15 silver, as the community wiki describes. You arrive on the road beside that place's cart rider.
- Sailing. Get into the sea, double-click a boat in your bag and it goes on the water next to you; board it with F or its menu and steer it with the movement keys. Leave or remove it from its menu. Running into the shore, travelling to another zone or logging out puts the boat back in your bag. All eight boats work (small wooden boat, old raft, small fishing boat, the Bal Sardan boats, longboats, fishing vessel), each needing its Sailing level from the community wiki (1, 15, 25, 30; the levels it doesn't list are reconstructions). Sailing your own boat trains Sailing (1 XP per 4 metres, a reconstruction). Boat teleports put you aboard your boat wherever it is in the zone. Fishing from the small fishing boat gives 25% more Fishing XP.
- Ferries. The game's three ferries sail again: The Coral Lady between the lighthouse and Yorkhill (round the west coast and along the north shore), and the small ferries from the lighthouse and from Bal Sardan, which stop at their docks for a while. Step aboard at a dock to ride. The Bal Sardan ferries carry you between West Athagos and Bal Sardan: out at sea you cross to the other zone, on the other ferry, which takes you into its harbour. The routes in the game data were drawn for an older map and cross hills, forests and rocks today, so the ferries follow new routes that stay on open water; the docks, waiting times and speeds are the game's, and the routes and crossing points are reconstructions. While a ferry is under way you stay on your spot on deck; at docks you can walk on and off.
- Abilities do what their descriptions say. Buffs show their icon by the health bar and work: Strength and Immolate raise damage; Thick skin, Stability and the barriers cut damage taken; Heavy shield block, Truce and Traverse make you immune; Disperse and Flame barrier reflect damage; Frenzy, Hack and slash and Focus speed up attacks; Survivor raises health and takes it back when it ends; Peace and Truce calm monsters up to your Healing level; Mana surge and Embrace the pain restore mana. Area spells (Blast, Demolition, Firestorm, Fire wall, the ice spells, Slashing...) hit everything around you, Fire ball, Explosive arrow and Eruption splash around the target, stuns and roots stop monsters, poison, bleeding and Combustion deal damage over time, Tritone and the Condensed attacks spend your mana for one big hit, Vampiric arrow heals you and Taunt pulls nearby monsters. Heals (Charm, Purification, Healing bird) and heals over time (Healing music, Healing aura, Healing plant) heal you. Movement buffs show their icon only, because the client's run speed can't be changed. Summons, Portal and other abilities that need missing systems say they aren't available offline. The effect numbers are reconstructions.
- All bandages work: flax and ramie too, and the "heal other" bandage abilities heal you (there's nobody else to heal).
- Light and heavy armor train. If you were hit in the last 30 seconds before a kill, its XP is shared between your weapon skill and the armor skills you're wearing, as the community wiki describes.
- Usable items: health and mana potions, mana regeneration, health boost, crafting and gathering speed potions (crafting or gathering 25% faster); experience scrolls (100, 300, 1000 and 5000 XP in the skill you pick, the amounts the wiki records; quest rewards hand these out); teleport scrolls to the wayshrines that exist offline, including the home wayshrine teleport; gear crates, which hold a random piece of equipment of their tier. Resistance, stamina and movement speed potions, beer and wine show their buff icon only.
- Quests 5, 6, 7, 17, 18, 19, 20, 26, 27, 28, 31 and 32 can be played to the end, and the ambush in Flowerful Persuasions happens. Quest steps include going to places, clicking quest objects (the pond scroll, the cook book, the rum crate, the graves), using items (the yarrowroot, Tesco's remains, fibervine seeds), saying a name in chat (Wallace), killing quest monsters (with quest drops such as Tesco's remains and the troll's hide), and conversations that start other quests. Ambushers, bosses and summoned ghosts spawn when the story calls for them, and "Into the Depths" takes you into its battle and aftermath scenes.
- content.json: entries for one NPC copy (`npcType`), prerequisites (`after`) and quest variables (`var`); NPC rules (`npcs`) for which copy exists in which phase and for quest spawns; actions that set quest phases and variables, open conversations, post chat lines and spawn NPCs; triggers for areas, chat, item use, object clicks, dialogue nodes and finished quests. See the `gso-content` skill.
- `npcground [n]` bridge command: how far the nearest NPCs stand above the ground under them, whether they're walking, their animation and how far they face away from you.
- `sounds` bridge command: the sounds the client is playing.
- `playeranim` bridge command: the player's animation layers, with clip length and progress.
- `swingprofile [seconds]` bridge command: when each attack animation strikes (the weapon hand's speed through the swing), with the moments hits land and shots leave. `npchp <uid> <hp>` sets a monster's health, for a training dummy.
- `worldnpcs [type...]` bridge command: the server's NPCs in this scene, including hidden quest copies.
- `setlevel <skillId> <level>` and `buffs` bridge commands: set a skill level for testing, and list the active buffs.
- Bridge commands for boats, ferries and oyster fishing: `boatprefabs`, `boatstate`, `sail`, `ferries`, `ferryboard`, `routecheck`, `routeplan`, `watermap`, `profile`, `interactables`, `interactablesnear`, `spawnprefabs`, `spawnobj`/`despawnobj`, `oysters` and `netoyster`. See docs/COMMANDS.md.

## [1.1.0] - 2026-10-09
### Fixed
- Changing zones no longer turns the clock back to 10:00. The start time (`World.StartTimeOfDay`) is sent once when you enter the world, and the day/night cycle runs on from there, so GSO HD Textures' weather and days continue across zones.
- Stack counts show again in the inventory and bank (e.g. "12" on a stack of logs). The windows only draw a count for items with id 0, which is how the original server sent stackable items; stacks now go to the client that way.
- Sorting the inventory no longer scrambles it on the server. The client sorts its own list and sends only "sorted"; dragging or swapping items afterwards moved the wrong ones, and the order came back wrong after a relog. The server now adopts the client's order.
- Steam no longer shows the game as running after you quit, with "Stop" stuck on "Stopping", when you opened an in-game link (Leaderboards, Discord, wiki...) while your browser was closed. The game started the browser as its own child process, with the Steam overlay injected, so Steam waited for the browser to close. Links now open through the Windows shell, outside the game's processes.
- Harvesting right after a fight no longer swings your weapon at the node. Starting a harvest or craft now ends the attack stance; the client only plays the gathering animation outside it.
- You now harvest with the tool in your hand. Starting to mine, chop, fish or gather puts your best matching tool in hand and your weapon on your back; attacking with a tool in hand brings the weapon back. Attacking also stops the harvest.

### Added
- Loot bags. A kill drops a bag where the monster fell, showing what's in it when you hover over it; click it to take everything. Bags stay for 5 minutes and are still there if you leave the area and come back. Drop lists for 43 monsters come from the community wiki (e.g. sheep: wool, sheep leather and bones; wolves: fur and bones; Red crab: silk gear, pistols and its rare pet). Monsters the wiki doesn't cover drop silver by level, and every monster can drop upgrade and grade stones for its level band. Silver now comes in the bag instead of straight into your inventory.
- `loot`, `lootroll <npcType> [n]` and `killnpc <uid>` bridge commands: list loot bags, check a drop table's rates over many kills, and kill an NPC as the player would.
- Particles and sounds are back. Arrows, crossbow bolts, staff, wand, lute and pistol shots fly to the target, and the hit lands when they arrive, with the impact effect and sound. Abilities fire their projectiles (fire ball, sword throw, the elemental arrows, flame lance...) and play their cast and hit effects and sounds. Monsters make their attack, hurt and death sounds, and ranged monsters (crawlers, frogs, sorceresses, Roke, the pond specter...) shoot back from a distance. Harvesting makes its chopping, mining and fishing sounds, trees fall when cut down, wayshrine teleports flash where you leave and land, and levelling up shows the level-up effect with a chat message.
- `fx <id>`, `sfx <id>` and `projectiles` bridge commands: play an effect or a sound, and list projectiles in flight.
- Secondary weapon: equipping a weapon over another puts the old one on your back, as the original server did. Switch with the "Switch weapon" key or the button by the spell bar, or take it off from the equipment window. It is saved with the character.
- `openurl <url>` bridge command: calls `Application.OpenURL` as a menu link would.
- `invorder` and `sortinv` bridge commands: compare the server's and the client's inventory and bank row by row, and sort as the inventory's "By name" button does.
- `playerstate` also shows the action animation, the weapon in hand and the one on the back.

### Changed
- Branching: work happens on `feature/<area>/<name>` branches merged into `dev`, and each release is one merge of `dev` into `main` (`docs/DEVELOPMENT.md`). Risky, large or core changes, and every release, go through a reviewed pull request.
- Dev builds and release builds: every `build.ps1` build is a dev build, versioned like `1.0.0-dev+<branch>.<commit>` and logged at startup with "(dev build)", so you can tell which build you're playing. Only `release.ps1` makes release builds, with the plain version.
- `release.ps1` runs on `dev`: its "Release vX" commit carries that version's CHANGELOG section, which becomes the `dev` -> `main` pull request.

## [1.0.0] - 2026-10-08
### Changed
- The DevBridge, `devbridge.ps1`, `decompile.ps1` and the datamining tools moved to the separate GSODevTools repo. The DevBridge is now its own plugin, and the `Debug.DevCommandFile` setting is gone. `checkrecipes` remains as a GSOOffline bridge command (`DevCommands.cs`), now joined by `npcbounds`, `animclips`, `door`, `clearspot`, `probe` and `playerstate`.
- Builds no longer touch the game. `build.ps1` puts the plugin in `artifacts\build\<Configuration>\`, laid out like the game folder, with an `INSTALL.txt` saying where it goes; `-Deploy` (replacing the old default and `-NoDeploy`) copies it into the game. BepInEx's DLLs for compiling come from the pinned BepInEx zip, so building doesn't need BepInEx installed in the game.
- `docs/INSTALL.md`: installation guides from a release and from a build (with updating, uninstalling and troubleshooting), also shipped in the release zips.

### Added
- Inventory and items:
  - Items are saved per character, and their stats are rolled from the item database.
  - Equip, unequip, destroy, drop, swap/insert and item tabs work.
  - Using food heals.
- Starter kits from the character creator: clothes, a weapon for the chosen fighting style, profession items, and level 5 in the profession skill.
- NPC dialogue:
  - Conversations follow the game's own dialogue trees, including quest, item and quest-variable conditions on options.
  - Which conversation an NPC opens with is rebuilt from the dialogue graph, plus `Data/content.json`.
- Quests:
  - Phases and the quest tracker update as you progress.
  - Completing a quest grants its item, silver and XP rewards.
  - Quests advance on item-based triggers.
  - The first steps of "An Honest Day's Work" are playable through Merrick's hand-in.
- Shops:
  - All 47 shop ids are stocked with rebuilt inventories that fit their merchant.
  - Buying and selling use the client's own pricing rules, and merchants have silver.
- Harvesting:
  - Mining, woodcutting, fishing and gathering use the game's own drop tables, and drop chances rise with skill level.
  - Each node has a tool and level requirement and a limited amount before it depletes, then respawns.
  - Gathering repeats until the node is used up or you walk away.
- Crafting:
  - All 341 recipes are rebuilt with the same ids as the client.
  - Crafting checks materials and skill level, takes the recipe time, and supports batch crafting.
- Harvesting and crafting give XP from a level-scaled base. The original formula was lost; it lives in `SkillData.BaseXp`.
- Combat:
  - Targeting and auto-attack with weapon-based range and speed. Damage scales with the weapon's damage stats and combat skill, and the skill matching the weapon gets XP.
  - NPCs aggro, chase, swing at their own attack speed, leash home and respawn.
  - Kills drop silver and advance quest kill objectives.
  - When you die you respawn at your home wayshrine, with brief protection from aggro.
  - Bandage-heal abilities work, and ability bar slots are saved.
  - Combat numbers live in `CombatRules`, since the originals were lost.
  - Town guards no longer attack on sight (offline, there are no criminals).
- `tools/dev/devbridge.ps1` and `docs/DEVBRIDGE.md`: enable/disable the DevBridge, launch, send commands, read results.
- Abilities on the action bar work:
  - Level, mana, range and cooldown come from the game's ability data.
  - Targeted abilities hit the target, and sweeping weapon techniques hit everything in reach. Damage is a multiple of a basic hit, because the original ability damage values were lost.
- Mana, plus health and mana regeneration.
- NPCs wander around their spawn points using the game's own wander settings, kept on the ground with terrain raycasts.
- Doors and zone transitions:
  - Dungeon, cave and monastery entrances and exits are paired from the developers' own object names (`tools/datamining/gen_doors.py` builds `Data/doors.json`).
  - Building doors lead into their town's interior scene, and exits return you to the door you used.
  - The snowy portal works.
  - Generic doors report as locked.
- Bank: deposit and withdraw items (unique items keep their rolled stats) and silver, from bank chests or a banker's dialogue.
- `content.json` overrides placed next to the DLL replace the built-in quest content without rebuilding.
- Chat commands `/give`, `/silver` and `/quest`.
- An opt-in developer automation bridge (`Debug.DevCommandFile`) for testing through the real client.

### Fixed
- Your character no longer stays stuck in the attack animation after killing a creature. Every swing puts the client into attack mode and the server never told it the fight was over; it now does so when the target dies, when you cancel, when the target is cleared, on death and on scene changes.
- Doors no longer drop you behind rocks or walls. Arrival points were guessed offline and some landed in a pocket you couldn't walk out of (leaving the Grimwall mine, leaving Roke's dungeon by the ruins). The server now picks the arrival in-game: solid, flat-enough ground with room to stand, in sight of the door, preferring open space. Scenes whose floors appear a moment after loading (Roke's dungeon) keep the loading screen up until the floor exists, instead of letting you fall through. All 18 doors were checked in-game.
- NPCs are animated again. The server sent animation 0 (an empty clip) for every NPC, so people stood frozen and sunk to the waist, and animals stretched hundreds of metres across the map. NPCs now get their idle, run, attack and death animations from the game data.
- Saves now keep inventories, skills and other lists. UnityEngine.JsonUtility had silently dropped them, so it was replaced with the plugin's own JSON serializer.
- Max health and carrying capacity are now sent at login.
- Equipment slots are restored correctly after relogging.

### Added (0.1 groundwork)
- Offline server that replaces the shut-down Photon game server, running inside the game process.
- Login with any account name. Accounts and characters are saved as JSON in `OfflineSaves/`.
- Character list, creation (including the appearance editor) and deletion.
- Entering the world, scene loading, position saving and autosave.
- Wayshrine travel and discovery, and setting a home wayshrine.
- NPC and harvestable spawns rebuilt from the developers' scene markers and streamed by distance.
- Chat and offline commands (`/help`, `/pos`, `/tele`, `/scene`, `/wayshrine`, `/wayshrines`, `/time`, `/save`).
- Steamworks calls made safe so the game still works when it is launched without Steam.
- Build, release and data-extraction tooling.
- GPL-3.0-or-later license (included in release zips) and a README note that this is a non-commercial project for a game no longer sold on Steam.
