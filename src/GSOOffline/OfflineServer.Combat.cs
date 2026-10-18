using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Tunable combat numbers. The real formulas died with the server; these aim for the feel of the
    /// early game (a level-2 crab takes a handful of sword swings and can't kill a fresh character alone).
    /// </summary>
    internal static class CombatRules
    {
        public static int NpcHealth(int level) => 10 + 8 * Mathf.Max(1, level);
        // Bosses: the patch notes' changes relative to ordinary creatures, compounded. Health: bosses +100% (136),
        // +50% (137) against creatures +50% (137), -11.1% (139) = 2.25x. Damage: bosses +60% (136), +25% (137),
        // -12.5% (139) against creatures +30% (136) = 1.35x. The game data gives bosses no health of their own,
        // and its few damage values (Lakhar 1700) are on the old server's scale, so bosses use the level formula.
        public const float BossHealthMultiplier = 2.25f, BossDamageMultiplier = 1.35f;
        public static int BossHealth(int level) => Mathf.RoundToInt(NpcHealth(level) * BossHealthMultiplier);
        public static int NpcMaxHit(NpcInfo n) =>
            n.boss ? Mathf.RoundToInt((2 + 2 * Mathf.Max(1, n.level)) * BossDamageMultiplier)
            : n.damage > 0 ? n.damage : 2 + 2 * Mathf.Max(1, n.level);
        // Share of a creature's hit that Damage block on your gear absorbs: diminishing, and less against stronger
        // creatures (about a third for gear of the creature's level, ~200 block at 40). Never all of it.
        public static float BlockReduction(int block, int attackerLevel) =>
            block <= 0 ? 0f : block / (block + 10f * Mathf.Max(1, attackerLevel));
        public static int PlayerMaxHit(int weaponDamage, int skillLevel) => 4 + weaponDamage + skillLevel;
        public static float HitChance = 0.85f;   // the player's blows
        // An NPC's blows land by its accuracy (1-100): 70% at 1, 82% at 50, 95% at 100 (it was 85% for all).
        public static float NpcHitChance(int accuracy) => 0.7f + 0.25f * Mathf.Clamp(accuracy, 0, 100) / 100f;
        // Your gear's resistance to a blow's kind, in percent (a full set of one level gives about 40-60), through the
        // soft cap of update 136: full value to 60%, then half to 80 (70% effective), a quarter to 120 (80%),
        // an eighth to 200 (90%), and never past 90%.
        public static float PlayerResistance(int percent) => EffectiveResistance(percent) / 100f;

        public static float EffectiveResistance(int percent)
        {
            float p = Mathf.Max(-100, percent);
            if (p <= 60f) return p;
            if (p <= 80f) return 60f + (p - 60f) * 0.5f;
            if (p <= 120f) return 70f + (p - 80f) * 0.25f;
            return Mathf.Min(90f, 80f + (p - 120f) * 0.125f);
        }

        // No single blow takes more than this share of your maximum health (update 140).
        public const float MaxHitShare = 0.48f;
        // Death's toll (issue #69): each death in a row takes 5% more experience for 5 more minutes of play, up to
        // 5 deaths (25%, 25 minutes). From the 4th, half the deaths drop equipped gear into a bag where you fell:
        // one piece 60%, two 30%, else three to all of it. John Brech's fee per level of the toll, times
        // 1 + combat level / 10.
        public const float DeathTollXpLoss = 0.05f, DeathTollMinutesPerStack = 5f;
        public const int DeathTollMaxStacks = 5, DeathTollDropFromStack = 4;
        public const float DeathDropChance = 0.5f, DeathDropOne = 0.6f, DeathDropTwo = 0.3f;
        public static readonly int[] DeathCleanseFee = { 100, 300, 500, 800, 1000 };
        // Bounty board (issue #67): 3 to 5 outlaws within 5 levels of your combat level, the board topped up
        // every 15 minutes and an outlaw not taken on gone after an hour. They are tougher than a creature of their level (they
        // stand in for a player); their hits and defences come from their gear. Each ability every 4th swing.
        public const int BountyMin = 3, BountyMax = 5, BountyLevelRange = 5;
        public const long BountyLifetimeSeconds = 3600, BountyRefreshSeconds = 900;
        public const float BountyAggroDistance = 15f, BountyShootDistance = 18f, BountyGearDropChance = 0.4f;
        public const int BountyAccuracy = 60, BountyMinLevel = 3;
        // A bounty taken on lasts an hour of play (issue #86). Giving one up, or letting it run out, earns Coward's
        // mark: 5% less experience for 10 minutes of play per bounty, each one while it lasts adding 5% and 10
        // minutes, up to 5 (25%, 50 minutes). It adds to Death's toll, and only time takes it away.
        public const float BountyHuntSeconds = 3600f;
        public const float CowardXpLoss = 0.05f, CowardMinutesPerStack = 10f;
        public const int CowardMaxStacks = 5;
        // Each outlaw's temperament: 15-35% of its swings an ability, shooters keeping 5-10 m away.
        public const int BountyAbilityChanceMin = 15, BountyAbilityChanceMax = 35;
        public const float BountyKiteMin = 5f, BountyKiteMax = 10f;
        public const float BountyStep = 2.5f;   // metres per half-second waypoint: a running player
        public const float BountyKiteCooldown = 8f;   // a shooter backs off at most this often
        public static int BountyHealth(int level) => NpcHealth(level) * 3;
        // 50 per level plus 8 x level^1.5 (about 900 at 10, 4,900 at 40, 8,300 at 60), 5% more per level above yours.
        public static int BountySilver(int level, int combatLevel) =>
            Mathf.RoundToInt((50f * level + 8f * Mathf.Pow(level, 1.5f)) * (1f + 0.05f * Mathf.Max(0, level - combatLevel)));
        // The explosive potion and black candy: the fuse and reach are the original's; the blow (times the hit of
        // your alchemy level and gear) and the share of your own health if you stay too close are reconstructions.
        public const float ExplosiveFuse = 3f, ExplosionRadius = 6f, ExplosionHitPower = 3f, ExplosionSelfShare = 0.3f;
        // After a stun wears off, the target can't be stunned again for this long (updates 117 and 131; NPCs too, 126).
        public const float StunImmunity = 4f;
        // Pets (update 132): a pet's blow is this share of its owner's basic hit (reconstruction; the notes only
        // say it comes from the owner's damage and weapon level), times its grade: 94% Damaged to 136% Mythic
        // (update 138). Flying followers instead add 8.5% (Damaged) to 19% (Mythic) to your damage (update 136;
        // update 140 lowered it by an unrecorded amount).
        public const float PetDamageShare = 0.5f;
        public static float PetGradeMultiplier(int grade) => 1f + 0.06f * (Mathf.Clamp(grade, 1, 8) - 2);
        public static float FlyingPetBonus(int grade) => 0.085f + 0.015f * (Mathf.Clamp(grade, 1, 8) - 1);

        // Bosses give double experience (update 137).
        public const int BossXpMultiplier = 2;

        // Abilities with their own lowest roll: Brutal slash and Cripple (update 130), Fire ball (update 134).
        public static readonly Dictionary<int, float> AbilityMinDamageShare = new Dictionary<int, float>
        {
            { 86, 0.75f }, { 88, 0.5f }, { 1, 0.6f },
        };

        // A weapon's lowest basic-attack roll as a share of its highest (update 130); unlisted weapons roll from 1.
        public static float MinDamageShare(ItemTemplate w)
        {
            if (w == null) return 0f;
            if (w.flags.Contains("isHalberd")) return 0.65f;
            if (w.flags.Contains("isBroadsword") || w.flags.Contains("isBow")) return 0.5f;
            if (w.flags.Contains("isGreatsword")) return 0.3f;
            return 0f;
        }

        // Armor alignment (update 135): worn light or heavy armor scores chest 4, legs 3, head 2, back, gloves and
        // boots 1 each (shields don't count). 7 points in one type give its passive.
        public const int AlignmentNeeded = 7;
        public static int AlignmentWeight(string slot)
        {
            switch (slot)
            {
                case "Body": return 4;
                case "Legs": return 3;
                case "Head": return 2;
                case "Back": case "Bracelet": case "Feet": return 1;
                default: return 0;
            }
        }
        public const float LightAlignmentDamage = 1.15f, HeavyAlignmentTaken = 0.9f;
        public const int BuffLightArmor = 94, BuffHeavyArmor = 95;   // client buff icons (XMLs/Buffs)

        // Combat level (update 136): the three best combat skills count in full (/3), every other one a twelfth.
        public static readonly int[] CombatSkills = { 15, 16, 17, 18, 20, 21, 22, 23, 24, 25, 26 };
        // Block also takes a flat bite out of every blow, so well-armoured players shrug off weak creatures (a crab's
        // few points against ~200 block at level 40) while creatures of their own level still hurt.
        public static float FlatBlock(int block) => Mathf.Max(0, block) / 20f;
        public const float AbilityMultiplier = 1.6f;
        public static int KillXp(int npcLevel) => 12 * Mathf.Max(1, npcLevel);
        // Armor skills share a kill's XP if the player was hit this recently before it (community wiki: 30 s).
        public const float ArmorXpWindow = 30f;
        // Silver in the loot bag of NPCs the wiki has no drop table for.
        public static int SilverDrop(int npcLevel) => Random.Range(0, 3 * Mathf.Max(1, npcLevel) + 1);
        public const float LeashDistance = 40f;
        // Ordinary creatures, tuned for one player. The original server spawned them in camps for a shared world:
        // 30 m aggro reach (most creatures' data) pulled a whole camp at once, and they were back 2 minutes after
        // dying (the community wiki and the original's default), often while the player was still clearing the
        // camp. Bosses and creatures with their own respawntime keep theirs.
        public const int NpcRespawn = 300;   // seconds
        public const float MaxAggroDistance = 15f;
        // A dead creature waits for the player to be this far from where it spawns, so it never pops back in mid-fight.
        public const float RespawnClearance = 40f;

        public static float AggroDistance(NpcInfo n) => n.ordinary ? Mathf.Min(n.aggroDistance, MaxAggroDistance) : n.aggroDistance;

        public static bool RespawnClear(NpcInfo n, Vector3 spawn, Vector3 player) =>
            n == null || !n.ordinary || new Vector2(spawn.x - player.x, spawn.z - player.z).magnitude >= RespawnClearance;
        public const float RespawnDelay = 5f;   // seconds dead before waking at the home wayshrine
        public const float RespawnProtection = 10f;

        // Carrying capacity before gear (the client's "Weight: x/150"). Over it you slow down: straight to
        // OverweightStart of your run speed, then faster the more you carry (to 40% at a quarter over), never
        // below OverweightFloor. The original game's rule is lost; this is a reconstruction.
        public const int BaseCarryingCapacity = 150;
        public const float OverweightStart = 0.8f, OverweightSoftness = 0.25f, OverweightFloor = 0.1f;

        public static float OverweightSpeed(int weight, int capacity)
        {
            if (capacity <= 0 || weight <= capacity) return 1f;
            float over = (weight - capacity) / (float)capacity / OverweightSoftness;
            return Mathf.Max(OverweightFloor, OverweightStart / (1f + over * over));
        }

        // Town guards are flagged aggressive with huge damage: on the real server they only went after
        // criminals. There is no crime system offline, so they only fight back when attacked.
        public static bool IsGuard(NpcInfo n)
        {
            string name = (n.name ?? string.Empty).Trim().ToLowerInvariant();
            return name == "guard" || name.EndsWith(" guard");
        }
    }

    public partial class OfflineServer
    {
        private int targetUid;
        private bool autoAttacking;
        private bool swingShown;   // a 3/20 attack animation was sent since the last 7/2
        private float castEndsAt = -1f;   // when an ability's cast animation is over
        private float nextPlayerSwing;
        private float lastPlayerSwing, measureSwingAt = -1f;
        // The current swing's hit or shot, waiting for its moment in the animation: called once per strike of the
        // clip with (strike, strikes), so a flurry deals its damage a share at a time.
        private System.Action<int, int> pendingBlow;
        private Vector3? clickTargetPos;   // 23/1: where a click-target ability (Fire wall, Blast, Landmine) goes
        private bool measuringCast;          // the animation being measured is an ability's
        private float pendingBlowFallback;   // a cast's blow time (its attackdelay) when the clip isn't in ClipImpact
        // Seconds the client's basic attack animation lasts, per weapon type (measured as it plays).
        private readonly Dictionary<int, float> swingLength = new Dictionary<int, float>();
        private float respawnPlayerAt = -1f;
        private float aggroImmuneUntil;
        private float lastHitTaken = -1000f;
        private bool PlayerDead => respawnPlayerAt > 0f;

        private void RegisterCombatHandlers()
        {
            On(3, 7, c => SelectTarget((int)c[2]));
            On(3, 0, c => UseAbility((int)c[2]));
            On(8, 7, c => AssignAbilitySlot((int)c[2], (int)c[3]));
            On(25, 12, c => { });   // auto-target slot hint
            On(23, 1, c => clickTargetPos = (Vector3)c[2]);   // the ground spot for the click-target ability that follows
        }

        private void ResetCombat()
        {
            targetUid = 0;
            clickTargetPos = null;
            pendingBlow = null;   // a swing from the old scene
            measureSwingAt = -1f;
            StopAttacking();
        }

        /// <summary>
        /// Ends the player's attack on both sides. Each swing (3/20) puts the client's player into its attack
        /// animation, and only 7/2 takes it out again; without it the player stays stuck mid-swing after a kill.
        /// </summary>
        private void StopAttacking()
        {
            bool wasAttacking = autoAttacking || swingShown;
            autoAttacking = swingShown = false;
            castEndsAt = -1f;
            if (wasAttacking) ProfileEvent("end");
            if (wasAttacking && character != null) Send(7, 2, character.name);
        }

        private void SelectTarget(int uid)
        {
            targetUid = uid;
            if (uid == 0) StopAttacking();
            Send(3, 19, character.name, uid);
        }

        private void AssignAbilitySlot(int slot, int abilityId)
        {
            character.SetAbilitySlot(slot, abilityId);
            Send(8, 6, character.name, slot, abilityId);
        }

        private void UseAbility(int slot)
        {
            if (character == null || PlayerDead) return;
            if (slot == 0)
            {
                var npc = world?.GetNpc(targetUid);
                if (npc == null || npc.dead || !CanFight(npc)) return;
                CancelJob("attacking");
                ReadyWeapon();
                autoAttacking = true;
                Send(3, 1, character.name, 0);
                return;
            }

            int ability = slot <= WeaponAbilitySlots ? WeaponAbilityInSlot(slot) : character.GetAbilitySlot(slot);
            if (!SkillData.Abilities.TryGetValue(ability, out var a))
            {
                UseAbility(0);
                return;
            }
            CancelJob("attacking");
            if (RuleFor(a).kind != AbilityKind.Bandage) ReadyWeapon();
            UseSkillAbility(slot, a);
        }

        private readonly Dictionary<int, float> slotReadyAt = new Dictionary<int, float>();

        private static bool CanFight(NpcEntity npc) =>
            GameData.Npcs.TryGetValue(npc.typeId, out var info) && info.canFight;

        // ---- player attacks ------------------------------------------------------------------

        private ItemSave EquippedWeapon()
        {
            foreach (var it in character.items)
                if (it.equipped && ItemData.Get(it.typeId)?.slot == "Weapon") return it;
            return null;
        }

        // A weapon's "requires<Skill>" flag names the skill it trains (wands: Mental grim, lutes: Healing).
        private static readonly Dictionary<string, int> RequiresSkill = new Dictionary<string, int>
        {
            { "requiresSwordsmanship", 15 }, { "requiresFencing", 16 }, { "requiresArchery", 17 },
            { "requiresMacefighting", 18 }, { "requiresElementalgrim", 20 }, { "requiresMentalgrim", 21 },
            { "requiresHealing", 24 },
        };

        // Combat skill trained by the equipped weapon (Scr_SkillsHandler ids).
        private int WeaponSkill(ItemSave weapon)
        {
            if (weapon == null) return 23;   // Wrestling
            var flags = ItemData.Get(weapon.typeId)?.flags;
            if (flags != null)
                foreach (var kv in RequiresSkill)
                    if (flags.Contains(kv.Key)) return kv.Value;
            var ih = Scr_ItemHandler.instance;
            int t = weapon.typeId;
            if (ih.isBow(t) || ih.isCrossBow(t)) return 17;
            if (ih.isStaff(t)) return 20;
            if (flags != null)
            {
                if (flags.Contains("isDagger")) return 16;
                if (flags.Contains("isMace")) return 18;
                if (flags.Contains("isInstrument")) return 24;
            }
            return 15;   // Swordsmanship
        }

        /// <summary>
        /// Attack reach, as the client's Scr_SkillsHandler.getMeleeWeaponRange: the client stops walking at this
        /// distance, so a shorter server range would leave the player standing there never swinging.
        /// </summary>
        private float WeaponRange(ItemSave weapon)
        {
            const float slack = 1f;   // server and client NPC positions drift a little apart
            if (weapon == null) return 3f + slack;
            var ih = Scr_ItemHandler.instance;
            int t = weapon.typeId;
            if (ih.isBow(t) || ih.isCrossBow(t)) return 50f + slack;
            if (ih.isStaff(t)) return 25f + slack;
            if (ih.isWand(t) || ih.isInstrument(t)) return 20f + slack;
            if (ih.isMace(t)) return 4f + slack;
            if (ih.isDagger(t)) return 3f + slack;
            if (ih.isHalberd(t) || ih.isGreatsword(t)) return 4.5f + slack;
            if (ih.isBroadsword(t)) return 4f + slack;
            return 3f + slack;
        }

        private float WeaponSpeed(ItemSave weapon)
        {
            var flags = weapon != null ? ItemData.Get(weapon.typeId)?.flags : null;
            if (flags == null) return 2.4f;
            if (flags.Contains("isDagger")) return 1.8f;
            if (flags.Contains("isGreatsword") || flags.Contains("isHalberd")) return 3.2f;
            return 2.4f;
        }

        private void TickPlayerAttack(Vector3 playerPos)
        {
            MeasureSwing();
            if (!autoAttacking || PlayerDead || Time.time < nextPlayerSwing) return;
            var npc = world.GetNpc(targetUid);
            if (npc == null || npc.dead)
            {
                StopAttacking();
                return;
            }
            var weapon = EquippedWeapon();
            if (Vector3.Distance(NpcPosition(npc), playerPos) > WeaponRange(weapon)) return;   // walk closer

            // One swing per play of the attack animation. Damage per hit scales so damage per second stays that of
            // a swing every WeaponSpeed seconds, and attack speed buffs add damage rather than cut the animation short.
            float interval = swingLength.TryGetValue(weapon?.typeId ?? 0, out float len) ? len : WeaponSpeed(weapon);
            nextPlayerSwing = Time.time + interval;
            lastPlayerSwing = Time.time;
            ProfileSwingStarted(0);
            ResolvePendingBlow();
            measureSwingAt = Time.time + 0.25f;
            measuringCast = false;
            Send(3, 20, character.name, 0);
            swingShown = true;   // attack id 0: basic attack animation for the equipped weapon
            Aggro(npc);
            WeaponSounds(weapon, out int swingSfx, out int hitSfx);
            int projectile = WeaponProjectile(weapon);
            if (projectile == 0) PlaySound(swingSfx, playerPos);   // a shot's sound goes with the release

            int hit = RollPlayerHit(weapon, npc, interval / (WeaponSpeed(weapon) * AttackSpeedMultiplier()));
            int skill = WeaponSkill(weapon);
            System.Action land = () =>
            {
                if (npc.dead) return;
                if (hit > 0) PlaySound(hitSfx, NpcPosition(npc));
                DealDamage(npc, hit, skill, reflectable: true);
            };
            pendingBlow = (strike, strikes) =>
            {
                if (npc.dead || PlayerDead || strike > 0) return;
                if (projectile == 0)
                {
                    land();
                    return;
                }
                var pl = LocalPlayer;
                if (pl != null) PlaySound(swingSfx, pl.transform.position);
                ShootAtNpc(projectile, npc, land);
            };
        }

        // When the blows land (or the shot leaves) in each attack clip, in seconds, measured in-game with the
        // swingprofile bridge command: the weapon hand's fastest movements, just before they stop. Unlisted basic
        // attacks land 40% in; unlisted casts at their attackdelay. Generic clip names are keyed with their length.
        private static readonly Dictionary<string, float[]> ClipImpact = new Dictionary<string, float[]>
        {
            { "169_standing_melee_attack_horizontal 1", new[] { 0.65f } },   // broadsword, dagger, mace; strike abilities
            { "2Hand-Sword-Attack1", new[] { 0.7f } },                       // greatsword
            { "Unarmed-Attack-R3", new[] { 0.5f } },                         // fists, wands
            { "Standing_1H_Magic_Attack_02", new[] { 0.5f } },               // staffs, Fire ball
            { "Standing_1H_Magic_Attack_03", new[] { 0.3f } },               // lutes
            { "2Hand-Bow-Attack3", new[] { 1f } },                           // bows and arrow abilities: the release
            { "Armature|Anim@3.2", new[] { 0.5f } },                         // halberds
            { "Armature|Anim@3.6", new[] { 0.55f, 1.2f, 1.85f, 2.35f, 3f } },   // Slashing: a flurry of five
            { "Armature|CrossbowShoot", new[] { 0.3f } },                    // crossbows (the hand doesn't move; a guess)
        };

        private void ResolvePendingBlow()
        {
            var blow = pendingBlow;
            pendingBlow = null;
            measureSwingAt = -1f;
            blow?.Invoke(0, 1);
        }

        /// <summary>
        /// Each 3/20 restarts the client's attack animation, which loops while attacking. Swinging on a timer of our
        /// own cut it off midway, so shortly after a swing starts (once the animator is in the attack state) this
        /// reads the clip's length and times the next swing to its end. Standing and walking attacks differ.
        /// </summary>
        private void MeasureSwing()
        {
            if (measureSwingAt < 0f || Time.time < measureSwingAt) return;
            measureSwingAt = -1f;
            var blow = pendingBlow;
            pendingBlow = null;
            float[] impacts = { 0f };
            var anim = LocalPlayer != null ? LocalPlayer.anim : null;
            if (anim != null && anim.animator != null && anim.attackId >= 0)
            {
                // Layer 5 holds the weapon's attack clip (layer 3, used on the move, has a placeholder).
                var a = anim.animator;
                bool next = a.IsInTransition(5);
                var state = next ? a.GetNextAnimatorStateInfo(5) : a.GetCurrentAnimatorStateInfo(5);
                var clips = next ? a.GetNextAnimatorClipInfo(5) : a.GetCurrentAnimatorClipInfo(5);
                if (state.length >= 0.5f && state.length <= 5f)
                {
                    if (measuringCast)
                    {
                        // Abilities reuse weapon clips whose length differs from their attacktime: end the cast
                        // (and resume basic attacks) when the clip does, rather than cut it or start it again.
                        castEndsAt = lastPlayerSwing + state.length;
                        nextPlayerSwing = castEndsAt;
                    }
                    else
                    {
                        swingLength[EquippedWeapon()?.typeId ?? 0] = state.length;
                        nextPlayerSwing = lastPlayerSwing + state.length;
                    }
                }
                string clip = clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : string.Empty;
                string key = clip + "@" + state.length.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                if (!ClipImpact.TryGetValue(key, out impacts) && !ClipImpact.TryGetValue(clip, out impacts))
                    impacts = new[] { measuringCast ? pendingBlowFallback : 0.4f * Mathf.Min(state.length, 3f) };
            }
            else if (measuringCast) impacts = new[] { pendingBlowFallback };
            if (blow == null) return;
            for (int i = 0; i < impacts.Length; i++)
            {
                int strike = i, strikes = impacts.Length;
                float wait = lastPlayerSwing + impacts[i] - Time.time;
                if (wait > 0.02f) Later(wait, () => blow(strike, strikes));
                else blow(strike, strikes);
            }
        }

        /// <summary>
        /// The player's attack sounds were the server's to send and no data names them for basic attacks. These are
        /// the ids the ability data gives weapon blows: "Swing" and Brutal slash (321 swing, 320 impact), Stab (93),
        /// the arrows (34). Staffs, wands and lutes shoot projectiles, which bring their own impact sound.
        /// </summary>
        private static void WeaponSounds(ItemSave weapon, out int swing, out int hit)
        {
            swing = 321;
            hit = 320;
            if (weapon == null) return;
            var ih = Scr_ItemHandler.instance;
            int t = weapon.typeId;
            if (ih.isBow(t) || ih.isCrossBow(t))
            {
                swing = 34;
                hit = 0;
            }
            else if (ih.isStaff(t) || ih.isWand(t) || ih.isInstrument(t)) swing = hit = 0;
            else if (ih.isDagger(t)) hit = 93;
        }

        /// <summary>
        /// An ability puts the client's player into its attack animation like a swing does. Buffs, heals and
        /// sweeps with nothing to keep attacking would stay in it, so the cast ends once its attacktime is up.
        /// </summary>
        private void TickCastEnd()
        {
            if (castEndsAt < 0f || Time.time < castEndsAt) return;
            castEndsAt = -1f;
            if (!autoAttacking) StopAttacking();
        }

        // Every damage stat on everything worn: Damage, the physical kinds (SlashDamage...) and the elemental ones
        // (FireDamage, PsychicDamage, DmgPoison...), so staffs, wands and lutes count theirs, and so do rings,
        // amulets and armour (only the weapon's used to count). The weapon on your back isn't equipped.
        private int GearDamage()
        {
            int sum = 0;
            foreach (var it in character.items)
                if (it.equipped)
                    foreach (var s in it.stats)
                        if (s.key.EndsWith("Damage") || s.key == "DmgPoison") sum += Graded(it, s.value);
            return sum;
        }

        // Hit splash styles (Scr_EffectController.createHitSplash): 0 plain, 1 blocked, 3 weakened, 4 poisoned.
        private const int SplashPlain = 0, SplashBlocked = 1, SplashWeakened = 3, SplashPoisoned = 4;
        private const int AggressiveSnowball = 152;   // the only thing that hurts Snowmen and the winter bosses

        private int RollPlayerHit(ItemSave weapon, NpcEntity npc, float power = 1f, AbilityInfo ability = null)
        {
            npc.hitSplash = SplashPlain;
            if (Random.value >= CombatRules.HitChance) return 0;
            int max = CombatRules.PlayerMaxHit(GearDamage(), SkillLevel(WeaponSkill(weapon)));
            float minShare = ability == null ? CombatRules.MinDamageShare(weapon != null ? ItemData.Get(weapon.typeId) : null)
                : CombatRules.AbilityMinDamageShare.TryGetValue(ability.id, out float s) ? s : 0f;
            int min = Mathf.Clamp(Mathf.RoundToInt(max * minShare), 1, max);
            float damage = Random.Range(min, max + 1) * power * DamageDealtMultiplier();
            if (weapon != null) damage *= WeaponGradeMultiplier(weapon.grade);
            if (!GameData.Npcs.TryGetValue(npc.typeId, out var info)) return Mathf.RoundToInt(damage);
            if (ability == null || ability.id != AggressiveSnowball)
            {
                float through = HitThrough(weapon, info, ability, max);
                if (through <= 0f)
                {
                    npc.hitSplash = SplashBlocked;   // immune: Snowmen to all but snowballs, Golems to poison...
                    return 0;
                }
                if (through > 1.01f) npc.hitSplash = SplashWeakened;   // hit a weakness
                damage *= through;
            }
            int outlaw = BountyBlockedHit(npc, damage);   // a bounty outlaw blocks as a player does
            if (outlaw >= 0) return outlaw;
            return Mathf.Max(1, Mathf.RoundToInt(damage) - info.damageBlock / 10);
        }

        /// <summary>
        /// The share of a hit that gets through an NPC's resistances. A hit is the sum of its kinds: each damage stat
        /// on your gear is its own kind (a staff's FireDamage, a ring's IceDamage), and the rest (the base, plain
        /// Damage and your skill level) is the ability's element, or your weapon's main kind for plain blows.
        /// </summary>
        private float HitThrough(ItemSave weapon, NpcInfo info, AbilityInfo ability, int max)
        {
            int element = ability != null ? DamageKinds.OfAbility(ability.name) : DamageKinds.None;
            int main = element != DamageKinds.None ? element : WeaponKind(weapon);
            float through = 0f;
            int typed = 0;
            for (int k = 1; k < DamageKinds.Count; k++)
            {
                int part = GearStat(DamageKinds.DamageStat(k));
                if (part <= 0) continue;
                typed += part;
                through += part * DamageKinds.Through(info.resistance[k]);
            }
            int rest = Mathf.Max(0, max - typed);
            through += rest * DamageKinds.Through(info.resistance[main]);
            return max > 0 ? through / max : 1f;
        }

        /// <summary>Dev: what share of your hit (plain, or with an ability) gets through an NPC (bridge command hitcheck).</summary>
        internal void DevHitCheck(int typeId, int abilityId)
        {
            if (character == null || !GameData.Npcs.TryGetValue(typeId, out var info)) return;
            var weapon = EquippedWeapon();
            SkillData.Abilities.TryGetValue(abilityId, out var ability);
            int max = CombatRules.PlayerMaxHit(GearDamage(), SkillLevel(WeaponSkill(weapon)));
            var res = new List<string>();
            for (int k = 1; k < DamageKinds.Count; k++)
                if (info.resistance[k] != 0) res.Add(DamageKinds.Name(k) + " " + info.resistance[k]);
            string through = ability != null && ability.id == AggressiveSnowball ? "100% (snowball)" : (HitThrough(weapon, info, ability, max) * 100f).ToString("F0") + "%";
            Plugin.Log.LogInfo($"[dev] hit on {info.name} ({typeId}) with {(ability != null ? ability.name + " (" + DamageKinds.Name(DamageKinds.OfAbility(ability.name)) + ")" : "plain blows")}, " +
                $"weapon kind {DamageKinds.Name(WeaponKind(weapon))}, max hit {max}: {through} gets through; resistances [{string.Join(", ", res.ToArray())}]; " +
                $"its blows: {DamageKinds.Name(info.damageType)}, land {CombatRules.NpcHitChance(info.accuracy) * 100f:F0}%");
        }

        /// <summary>The kind of the weapon's blows: its largest damage stat; slash for a sword, blunt for fists.</summary>
        private static int WeaponKind(ItemSave weapon)
        {
            int best = DamageKinds.Blunt, bestValue = 0;
            if (weapon == null) return best;
            for (int k = 1; k < DamageKinds.Count; k++)
                foreach (var s in weapon.stats)
                    if (s.key == DamageKinds.DamageStat(k) && s.value > bestValue)
                    {
                        best = k;
                        bestValue = s.value;
                    }
            return best;
        }

        /// <param name="reflectable">The player's own blow or ability: while the target absorbs, it comes back at
        /// them. Other damage (pets, damage over time, reflections) is just soaked up.</param>
        /// <param name="dev">Dev kills ignore Absorb.</param>
        private void DealDamage(NpcEntity npc, int damage, int skill, bool reflectable = false, bool dev = false)
        {
            HarpyHurt(npc, damage);
            if (!dev && damage > 0 && Time.time >= npc.reflectFrom && Time.time < npc.absorbUntil)
            {
                Send(12, 0, npc.uid, npc.health, npc.maxHealth, SplashBlocked);
                if (reflectable) ReflectAbsorbed(npc, damage);
                return;
            }
            if (damage > 0 && damage < npc.health && GameData.Npcs.TryGetValue(npc.typeId, out var hurt))
                NpcSound(npc, hurt.sfxTakeHit);
            ProfileEvent("hit");
            npc.health = Mathf.Max(0, npc.health - damage);
            Send(12, 0, npc.uid, npc.health, npc.maxHealth, npc.hitSplash);
            npc.hitSplash = SplashPlain;
            if (npc.health <= 0) KillNpc(npc, skill);
        }

        /// <summary>Dev: a test character's base health, e.g. to stand in front of bosses (bridge command devhealth).</summary>
        internal void DevSetHealth(int health)
        {
            character.health = Mathf.Max(1, health);
            character.currentHealth = MaxHealth();
            SendHealth();
            Plugin.Log.LogInfo($"[dev] health {character.currentHealth}/{MaxHealth()}");
        }

        /// <summary>Dev: kill a visible NPC as the player would (bridge command killnpc).</summary>
        internal void DevKill(int uid)
        {
            var npc = world?.GetNpc(uid);
            if (npc == null || npc.dead)
            {
                Plugin.Log.LogInfo($"[dev] killnpc: no live NPC {uid}");
                return;
            }
            DealDamage(npc, npc.health, WeaponSkill(EquippedWeapon()), dev: true);
        }

        private void KillNpc(NpcEntity npc, int skill)
        {
            npc.dead = true;
            npc.aggro = false;
            if (npc.uid == targetUid) StopAttacking();
            GameData.Npcs.TryGetValue(npc.typeId, out var info);
            character.AddKill(npc.typeId);
            // A summoned minion's body fades a few seconds after it falls (TickBossMechanics removes it).
            npc.respawnAt = Time.time + (npc.master != null ? 5f : info?.respawnTime ?? CombatRules.NpcRespawn);
            HarpyDied(npc);   // shot down in the air: on the ground before the loot drops
            Send(11, 0, npc.uid, false, string.Empty);
            Send(13, 2, npc.uid, false);
            Send(13, 0, npc.uid, true);
            SetAnim(npc, info?.animDeath ?? NpcAnims.Death);
            if (info != null) NpcSound(npc, info.sfxDeath);

            int level = info?.level ?? 1;
            AwardKillXp(skill, CombatRules.KillXp(level) * (info != null && info.boss ? CombatRules.BossXpMultiplier : 1));
            if (npc.master == null) DropLoot(npc, info);   // summoned minions carry nothing
            OnNpcKilled(npc.typeId);
            OnBossKilled(npc);
        }

        /// <summary>
        /// Kill XP goes to the weapon skill and, if the player was hit within <see cref="CombatRules.ArmorXpWindow"/>,
        /// is shared with the armor skills worn (light and/or heavy), split evenly as the community wiki describes.
        /// </summary>
        private void AwardKillXp(int weaponSkill, int xp)
        {
            var skills = new List<int> { weaponSkill };
            if (Time.time - lastHitTaken <= CombatRules.ArmorXpWindow)
            {
                foreach (var it in character.items)
                {
                    if (!it.equipped) continue;
                    var flags = ItemData.Get(it.typeId)?.flags;
                    if (flags == null) continue;
                    if (flags.Contains("isLightArmor") && !skills.Contains(25)) skills.Add(25);
                    if (flags.Contains("isHeavyArmor") && !skills.Contains(26)) skills.Add(26);
                }
            }
            foreach (int s in skills) AddXp(s, xp / skills.Count);
        }

        /// <summary>Stunned or rooted NPCs stop where they are (the client keeps walking to the last waypoint).</summary>
        private void HoldNpc(NpcEntity npc)
        {
            Vector3 here = NpcPosition(npc);
            npc.hasDest = false;
            Send(198, 25, npc.uid, here, here, here);
            Send(13, 2, npc.uid, false);
        }

        // ---- NPC AI --------------------------------------------------------------------------

        private void SetAnim(NpcEntity npc, int anim, bool restart = false)
        {
            if (npc.anim == anim && !restart) return;
            npc.anim = anim;
            Send(198, 26, npc.uid, anim);
        }

        private static float AnimLength(int anim)
        {
            var clips = EasyAnimationHandler.instance != null ? EasyAnimationHandler.instance.animations : null;
            return clips != null && anim >= 0 && anim < clips.Length && clips[anim] != null ? clips[anim].length : 1f;
        }

        // Idle or run, following the client's own moving flag (it walks NPCs along our waypoints); attack while a swing plays.
        private void UpdateAnim(NpcEntity npc, NpcInfo info)
        {
            if (Time.time < npc.attackAnimUntil) return;
            var c = Scr_NpcHandler.instance != null ? Scr_NpcHandler.instance.getNpc(npc.uid) : null;
            if (c == null) return;
            int idle = info?.animIdle ?? NpcAnims.Idle, run = info?.animRun ?? NpcAnims.Run;
            SetAnim(npc, c.moving ? run : idle);
        }

        // The client walks NPCs along the waypoints we give it; its transform is the truth for position.
        private Vector3 NpcPosition(NpcEntity npc)
        {
            var c = Scr_NpcHandler.instance != null ? Scr_NpcHandler.instance.getNpc(npc.uid) : null;
            if (c != null) npc.pos = c.transform.position;
            else if (BountyTransform(npc) is Transform outlaw) npc.pos = outlaw.position;   // drawn as a player
            return npc.pos;
        }

        private void Aggro(NpcEntity npc)
        {
            if (npc.aggro) return;
            npc.aggro = true;
            Send(198, 30, npc.uid, false);   // run, not walk
            npc.nextSwing = Time.time + 1f;
        }

        private void MoveNpc(NpcEntity npc, Vector3 to)
        {
            npc.dest = to;
            npc.hasDest = true;
            StepNpc(npc);
        }

        /// <summary>
        /// The client walks an NPC in a straight line to its waypoint, height included, so a far waypoint over
        /// a hump takes it through the ground. Each waypoint is a short step towards the destination, put on
        /// the ground, and the next one goes out before the NPC reaches it.
        /// </summary>
        private void StepNpc(NpcEntity npc)
        {
            if (Time.time < npc.nextWaypoint || Time.time < npc.rootedUntil || Time.time < npc.stunnedUntil) return;
            var c = Scr_NpcHandler.instance != null ? Scr_NpcHandler.instance.getNpc(npc.uid) : null;
            Transform body = c != null ? c.transform : BountyTransform(npc);   // a bounty outlaw is drawn as a player
            if (body == null) return;
            npc.nextWaypoint = Time.time + 0.5f;
            Vector3 from = body.position;
            npc.pos = from;
            Vector3 flat = npc.dest - from;
            flat.y = 0f;
            float dist = flat.magnitude;
            if (dist < 0.3f)
            {
                // Arrived. Without this the client keeps the run animation going until its own 2-second stall check.
                npc.hasDest = false;
                Send(13, 2, npc.uid, false);
                return;
            }
            // About a second of walking; an outlaw half a second, the most the client's other-player smoothing follows.
            float step = Mathf.Min(dist, c != null ? Mathf.Max(2f, c.walking ? c.walkspeed : c.movementSpeed) : CombatRules.BountyStep);
            bool asPlayer = c == null;   // a bounty outlaw moves by a player's rules (OutlawBlocked)
            if (!TryStep(npc, from, flat / dist, step, dist, out var to, asPlayer) && !Detour(npc, from, flat / dist, step, dist, out to, asPlayer))
            {
                // Walled in: stop rather than walk through. A return home ends here.
                npc.hasDest = false;
                Send(13, 2, npc.uid, false);
                if (npc.returning) FinishReturn(npc);
                return;
            }
            Send(198, 25, npc.uid, from, to, to);
            Send(13, 2, npc.uid, true);
        }

        private const float ObstacleRadius = 0.4f;
        internal static bool NpcObstaclesOff;   // dev: npcobstacles off

        /// <summary>
        /// The waypoint one step from from in direction dir, on the ground, unless the step runs into something.
        /// The last step straight at the destination ends on it exactly.
        /// </summary>
        private static bool TryStep(NpcEntity npc, Vector3 from, Vector3 dir, float step, float dist, out Vector3 to, bool asPlayer = false)
        {
            to = npc.dest;
            if (asPlayer ? OutlawBlocked(from, dir, step) : Blocked(from, dir, step)) return false;
            if (step >= dist)
            {
                if (asPlayer && GroundAt(to, to.y + 2.5f, 12f, out var end)) to = end;
                return asPlayer ? OutlawCanStand(from, to, step) : !Inside(to);
            }
            to = from + dir * step;
            to.y = Mathf.Lerp(from.y, npc.dest.y, step / dist);
            float top = Mathf.Max(from.y, npc.dest.y) + 2.5f;
            if (GroundAt(to, top, top - Mathf.Min(from.y, npc.dest.y) + 8f, out var ground)) to = ground;
            return asPlayer ? OutlawCanStand(from, to, step) : !Inside(to);
        }

        /// <summary>
        /// Whether a step runs into something: a steep surface (rock, wall, tree) at body height, not a walkable
        /// slope or a creature. Without it NPCs walked through rocks: the ground ray of a rock taller than its
        /// start height finds the sand under the rock. Surfaces already touching at the start are ignored, so an
        /// NPC that ends up inside a collider can walk out.
        /// </summary>
        private static bool Blocked(Vector3 from, Vector3 dir, float dist)
        {
            if (NpcObstaclesOff) return false;
            Vector3 origin = from + Vector3.up * (ObstacleRadius + 0.3f);
            foreach (var h in Physics.SphereCastAll(origin, ObstacleRadius, dir, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (h.distance > 0f && h.normal.y < 0.5f && !IsCreature(h.collider)) return true;
            return false;
        }

        /// <summary>
        /// Whether an NPC standing at p would have something solid through its body: a waypoint snapped to the
        /// ground under a rock (a slope gentle enough to walk up, onto a rock taller than the ground ray's start).
        /// </summary>
        private static bool Inside(Vector3 p)
        {
            if (NpcObstaclesOff) return false;
            foreach (var c in Physics.OverlapSphere(p + Vector3.up * (ObstacleRadius + 0.3f), ObstacleRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!IsCreature(c)) return true;
            return false;
        }

        /// <summary>A step around an obstacle, nearest direction first, keeping to the side last used.</summary>
        private static bool Detour(NpcEntity npc, Vector3 from, Vector3 dir, float step, float dist, out Vector3 to, bool asPlayer = false)
        {
            if (npc.detourSide == 0) npc.detourSide = Random.value < 0.5f ? 1 : -1;
            foreach (float angle in new[] { 35f, 70f, 105f, 140f })
                for (int side = 0; side < 2; side++)
                {
                    int sign = side == 0 ? npc.detourSide : -npc.detourSide;
                    // A sideways step is never the last one, so it doesn't end on the destination.
                    if (!TryStep(npc, from, Quaternion.Euler(0f, angle * sign, 0f) * dir, step, float.MaxValue, out to, asPlayer)) continue;
                    npc.detourSide = sign;
                    return true;
                }
            to = from;
            return false;
        }

        private static bool IsCreature(Collider c) =>
            c.GetComponentInParent<Scr_Npc>() != null || c.GetComponentInParent<Scr_Player>() != null || c.GetComponentInParent<Scr_OtherPlayer>() != null;

        private void FinishReturn(NpcEntity npc)
        {
            npc.returning = false;
            Send(13, 2, npc.uid, false);
            if (npc.ownRespawn) return;   // Aellope heals by feeding on the dead and when she takes off (OfflineServer.Harpy.cs)
            npc.health = npc.maxHealth;
            Send(12, 0, npc.uid, npc.health, npc.maxHealth, 0);
        }

        /// <summary>The first solid surface straight down from height top above p, ignoring NPCs and players.</summary>
        private static bool GroundAt(Vector3 p, float top, float depth, out Vector3 ground)
        {
            ground = p;
            float best = float.MaxValue;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, top, p.z), Vector3.down, depth, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (h.distance >= best || IsCreature(h.collider)) continue;
                best = h.distance;
                ground = h.point;
            }
            return best < float.MaxValue;
        }

        /// <summary>Dev: how far the nearest client NPCs stand above the ground (bridge command npcground).</summary>
        internal void LogNpcGround(int count)
        {
            var player = LocalPlayer;
            if (player == null || Scr_NpcHandler.instance == null) return;
            Vector3 me = player.transform.position;
            var list = new List<Scr_Npc>(Scr_NpcHandler.instance.npcList);
            list.RemoveAll(x => x == null);
            list.Sort((a, b) => (a.transform.position - me).sqrMagnitude.CompareTo((b.transform.position - me).sqrMagnitude));
            for (int i = 0; i < list.Count && i < count; i++)
            {
                Vector3 p = list[i].transform.position;
                string above = GroundAt(p, p.y + 5f, 30f, out var g) ? (p.y - g.y).ToString("F2") : "no ground";
                var server = world?.GetNpc(list[i].npcUniqueId);
                Vector3 forward = list[i].transform.forward, toMe = me - p;
                forward.y = toMe.y = 0f;
                // Solid things overlapping its body (a rock it walked into), not the ground under it or creatures.
                var inside = new List<string>();
                foreach (var col in Physics.OverlapSphere(p + Vector3.up * (ObstacleRadius + 0.3f), ObstacleRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (!IsCreature(col)) inside.Add(col.name);
                Plugin.Log.LogInfo($"[dev] {list[i].npcUniqueId} type={list[i].npcTypeId} d={Vector3.Distance(p, me):F1} moving={list[i].moving} anim={server?.anim} facing-me-off={Vector3.Angle(forward, toMe):F0}deg above-ground={above} inside={(inside.Count > 0 ? string.Join(",", inside.ToArray()) : "-")}");
            }
        }

        private void Wander(NpcEntity npc, NpcInfo info)
        {
            npc.nextWander = Time.time + info.wanderFrequency * Random.Range(0.6f, 1.4f);
            Vector2 offset = Random.insideUnitCircle * info.wanderDistance;
            // A spot on the ground within 5 m of the spawn's height (not a roof above it or a cliff below).
            if (!GroundAt(npc.spawnPos + new Vector3(offset.x, 0f, offset.y), npc.spawnPos.y + 5f, 10f, out var spot)) return;
            // Only somewhere it can stroll to in a straight line: not behind or inside a rock.
            Vector3 from = NpcPosition(npc), flat = spot - from;
            flat.y = 0f;
            if (Inside(spot) || flat.magnitude > 0.3f && Blocked(from, flat.normalized, flat.magnitude)) return;
            Send(198, 30, npc.uid, true);   // walk
            npc.nextWaypoint = 0f;
            MoveNpc(npc, spot);
        }

        private void Disengage(NpcEntity npc)
        {
            npc.aggro = false;
            if (npc.attackingSent)
            {
                npc.attackingSent = false;
                Send(11, 0, npc.uid, false, string.Empty);
            }
            npc.returning = true;
            npc.nextWaypoint = 0f;
            MoveNpc(npc, npc.spawnPos);
        }

        private void TickNpcs(Vector3 playerPos)
        {
            foreach (var npc in world.npcs)
            {
                if (npc.dead)
                {
                    if (npc.master == null && !npc.ownRespawn && Time.time >= npc.respawnAt
                        && CombatRules.RespawnClear(GameData.Npcs.TryGetValue(npc.typeId, out var dead) ? dead : null, npc.spawnPos, playerPos))
                        RespawnNpc(npc);
                    continue;
                }
                if (npc.flying) continue;
                if (!NpcShown(npc))
                {
                    npc.aggro = npc.attackingSent = npc.returning = false;
                    continue;
                }
                GameData.Npcs.TryGetValue(npc.typeId, out var info);
                Vector3 pos = NpcPosition(npc);
                float dist = Vector3.Distance(pos, playerPos);
                if (dist > Plugin.NpcViewDistance.Value) continue;
                UpdateAnim(npc, info);
                if (info == null) continue;

                if (npc.hasDest) StepNpc(npc);
                if (!npc.aggro && !npc.returning && info.wandering && Time.time >= npc.nextWander && npc.uid != dialogueNpc?.uid)
                    Wander(npc, info);
                if (!info.canFight) continue;

                if (npc.returning)
                {
                    if (Vector3.Distance(pos, npc.spawnPos) < 1.5f) FinishReturn(npc);
                    else MoveNpc(npc, npc.spawnPos);
                    continue;
                }

                if (!npc.aggro && info.aggressive && !CombatRules.IsGuard(info) && !PlayerDead
                    && Time.time >= aggroImmuneUntil && dist <= CombatRules.AggroDistance(info) && !CalmedBy(info))
                {
                    Plugin.Log.LogInfo($"[combat] {info.name} ({npc.uid}) notices you at {dist:F1} m");
                    Aggro(npc);
                }
                if (!npc.aggro) continue;
                if (Time.time < npc.stunnedUntil) continue;   // stunned: no moving, no swinging

                if (PlayerDead || Vector3.Distance(pos, npc.spawnPos) > CombatRules.LeashDistance)
                {
                    Disengage(npc);
                    continue;
                }

                if (BountyKites(npc, pos, playerPos)) continue;   // outlaw shooters keep their distance

                // Ranged NPCs (a projectile and a share of attacks that use it) fight from projectileattackdistance.
                bool ranged = info.projectile > 0 && info.projectileRate > 0;
                float reach = ranged ? Mathf.Max(info.attackDistance, info.projectileDistance) : info.attackDistance;
                if (dist > reach)
                {
                    if (npc.attackingSent)
                    {
                        npc.attackingSent = false;
                        Send(11, 0, npc.uid, false, string.Empty);
                    }
                    MoveNpc(npc, playerPos);
                    continue;
                }

                if (!npc.attackingSent)
                {
                    npc.attackingSent = true;
                    npc.hasDest = false;
                    Send(13, 2, npc.uid, false);
                    Send(11, 0, npc.uid, true, character.name);   // client animates swings at attackSpeed
                    Send(198, 28, npc.uid, playerPos);   // turn to the player: the client only turns NPCs as they walk
                }
                if (Time.time < npc.nextSwing) continue;
                npc.nextSwing = Time.time + info.attackSpeed;
                Send(198, 28, npc.uid, playerPos);
                SetAnim(npc, info.animAttack, true);
                npc.attackAnimUntil = Time.time + Mathf.Min(info.attackSpeed, AnimLength(info.animAttack));
                NpcSound(npc, info.sfxAttack);
                BountyPrepareSwing(npc);
                bool shoot = ranged && (dist > info.attackDistance || Random.Range(0, 100) < info.projectileRate);
                var attacker = npc;
                var attackerInfo = info;
                // The blow lands attackdelay into the swing, in time with the animation.
                Later(Mathf.Min(info.attackDelay, info.attackSpeed), () => NpcSwingLands(attacker, attackerInfo, shoot));
            }
        }

        internal static bool LogBlows;   // dev: blowlog on - every creature blow aimed at the player

        private void NpcSwingLands(NpcEntity npc, NpcInfo info, bool shoot)
        {
            var player = LocalPlayer;
            if (npc.dead || !npc.aggro || PlayerDead || player == null || Time.time < npc.stunnedUntil) return;
            if (LogBlows) Plugin.Log.LogInfo($"[blow] {info.name} ({npc.typeId}) {(shoot ? "shoots" : "swings")} from {Vector3.Distance(NpcPosition(npc), player.transform.position):0.0} m");
            if (shoot)
            {
                NpcShoots(npc, npc.blowProjectile > 0 ? npc.blowProjectile : info.projectile, () => { if (!PlayerDead && !npc.dead) NpcHitsPlayer(info, npc); });
                return;
            }
            // Stepping out of reach during the wind-up dodges the blow.
            if (Vector3.Distance(NpcPosition(npc), player.transform.position) > info.attackDistance + info.attackMissDistance)
            {
                Send(8, 2, character.name, 0, PlayerSplash(info));
                return;
            }
            NpcHitsPlayer(info, npc);
        }

        private void NpcHitsPlayer(NpcInfo info, NpcEntity attacker)
        {
            int damage = 0;
            if (Random.value < CombatRules.NpcHitChance(info.accuracy))
            {
                // Your resistance to the blow's kind, then your damage block against every blow, whatever its kind.
                float roll = Random.Range(1, CombatRules.NpcMaxHit(info) + 1) * (attacker != null ? attacker.blowPower : 1f);
                if (info.damageType > 0 && info.damageType < DamageKinds.Count)
                    roll *= 1f - CombatRules.PlayerResistance(GearStat(DamageKinds.ResistanceStat(info.damageType)));
                int block = GearStat("Damageblock");
                roll *= 1f - CombatRules.BlockReduction(block, info.level);
                damage = Mathf.Max(0, Mathf.RoundToInt(roll - CombatRules.FlatBlock(block)));
                lastHitTaken = Time.time;
                damage = ApplyDefensiveBuffs(damage, attacker);
                damage = Mathf.Min(damage, Mathf.CeilToInt(MaxHealth() * CombatRules.MaxHitShare));
            }
            if (LogBlows) Plugin.Log.LogInfo($"[blow] {info.name} ({info.id}) hits you for {damage} (health {character.currentHealth - damage}/{MaxHealth()})");
            HurtPlayer(damage, PlayerSplash(info));
        }

        /// <summary>Takes damage off the player (already reduced) with a hit splash; at 0 they die.</summary>
        private void HurtPlayer(int damage, int splash)
        {
            if (PlayerDead) return;
            character.currentHealth = Mathf.Max(0, character.currentHealth - damage);
            Send(8, 2, character.name, -damage, splash);
            SendHealth();
            if (character.currentHealth <= 0) PlayerDies();
        }

        /// <summary>
        /// Damage the original server dealt in its own numbers (100 health per combat level), taken as the same share
        /// of this player's health, less their resistance to its kind: the dungeons' scripted hazards.
        /// </summary>
        private int HurtByOriginal(int originalDamage, int damageKind, int splash = SplashPlain)
        {
            float share = Mathf.Min(1f, originalDamage / (Mathf.Max(1, CombatLevel()) * 100f));
            float res = damageKind > 0 && damageKind < DamageKinds.Count ? CombatRules.PlayerResistance(GearStat(DamageKinds.ResistanceStat(damageKind))) : 0f;
            int damage = Mathf.RoundToInt(MaxHealth() * share * (1f - res));
            HurtPlayer(damage, splash);
            return damage;
        }

        // The splash style for a blow on you. damagetype used to go straight through, but splash styles aren't damage
        // kinds: spiders' poison (1) showed as "blocked", hounds' fire (3) as "weakened", Ents' earth (4) as "poisoned".
        private static int PlayerSplash(NpcInfo info) => info.damageType == DamageKinds.Poison ? SplashPoisoned : SplashPlain;

        private void RespawnNpc(NpcEntity npc)
        {
            npc.dead = false;
            npc.aggro = npc.returning = npc.attackingSent = false;
            npc.health = npc.maxHealth;
            npc.pos = npc.spawnPos;
            npc.hasDest = false;
            npc.attackAnimUntil = 0f;
            npc.stunnedUntil = npc.rootedUntil = npc.stunImmuneUntil = 0f;
            npc.dotTicks = 0;
            Send(13, 0, npc.uid, false);
            Send(9, 1, npc.uid, npc.spawnPos);
            Send(12, 0, npc.uid, npc.health, npc.maxHealth, 0);
            SetAnim(npc, NpcAnims.IdleOf(npc.typeId));
        }

        // ---- player death --------------------------------------------------------------------

        private void PlayerDies()
        {
            ApplyDeathToll(LocalPlayer != null ? LocalPlayer.transform.position : character.Position);
            StopAttacking();
            ClearBuffs();
            respawnPlayerAt = Time.time + CombatRules.RespawnDelay;
            Send(1, 2, character.name, true);
            Send(1, 4, character.name, false);   // the client only stops movement when told: WASD dragged the body along the floor
            foreach (var npc in world.npcs)
                if (npc.aggro) Disengage(npc);
        }

        private void TickPlayerRespawn()
        {
            if (!PlayerDead || Time.time < respawnPlayerAt) return;
            respawnPlayerAt = -1f;
            aggroImmuneUntil = Time.time + CombatRules.RespawnProtection;
            character.currentHealth = MaxHealth();
            RefreshAlignment(true);
            Send(1, 2, character.name, false);
            Send(1, 4, character.name, true);
            SendHealth();
            if (GameData.Wayshrines.TryGetValue(character.homeWayshrine, out var w))
                Teleport(w.scene, w.pos);
            else
                Teleport(StartScene, StartPos);
            SendDeathToll();   // a respawn in the same scene doesn't reload it
            SendCowardMark();
        }

        private void TickCombat()
        {
            if (character == null || world == null || !sceneReady) return;
            var player = LocalPlayer;
            if (player == null) return;
            Vector3 p = player.transform.position;
            TickPlayerRespawn();
            if (world == null || !sceneReady) return;   // respawned into another zone: this world is gone
            TickBuffs();
            TickNpcEffects();
            TickCastEnd();
            TickSwingProfile();
            TickPlayerAttack(p);
            TickNpcs(p);
            TickBossMechanics(p);
        }
    }
}
