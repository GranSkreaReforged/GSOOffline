using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Combat numbers. Health, damage and experience are the original server's formulas (update 136,
    /// Scr_NpcHandler and Scr_PlayerHandler) with the later patch notes' changes on top; the attacks' own formulas
    /// are in OfflineServer.OriginalCombat.cs and OfflineServer.Abilities.cs. Single-player additions are marked.
    /// </summary>
    internal static class CombatRules
    {
        // Creature health: 100 + 5 x level^2.3, then +50% (update 137) and -11.1% (139). Bosses: 100 + 10 x level^2.3,
        // then +50% (137): Zalranaz at 90 has 468,800, as videos of the live game show.
        public const float CreatureHealthPatches = 1.5f * (8f / 9f), BossHealthPatches = 1.5f;
        public static int NpcHealth(int level) => (int)((100 + 5f * Mathf.Pow(Mathf.Max(1, level), 2.3f)) * CreatureHealthPatches);
        public static int BossHealth(int level) => (int)((100 + 10f * Mathf.Pow(Mathf.Max(1, level), 2.3f)) * BossHealthPatches);

        // Single player: bosses were made for groups, so the character may give them this share of their health
        // (the insurance advisor, OfflineServer.Difficulty.cs; 1 = the original's). Their damage is the original's.
        public static float BossHealthShare = 1f;

        // Creature damage: 10 + 0.65 x level^2; bosses 10 + 0.8 x level^2, then +25% (update 137) and -12.5% (139).
        // A blow rolls 1 to one less than this (Random.Range(1, damage)). The data's own damage replaces it, except the
        // few single-digit values (Argus, Argo, Argi), which are on another scale.
        public const float BossDamagePatches = 1.25f * 0.875f;
        public static int NpcDamage(int level, bool boss)
        {
            float l = Mathf.Max(1, level);
            return boss ? (int)((10 + 0.8f * l * l) * BossDamagePatches) : (int)(10 + 0.65f * l * l);
        }

        public static int NpcMaxHit(NpcInfo n) => n.damage >= 10 ? n.damage : NpcDamage(n.level, n.boss);

        // Your base health: 100 per combat level (update 136), gear on top.
        public static int BaseHealth(int combatLevel) => 100 * Mathf.Max(1, combatLevel);

        // Your gear's resistance to a blow's kind, through the soft cap of update 136, exactly as the original's
        // Scr_StatsHandler.CalculateEffectiveResistance (its bands overlap a little: 81 gives 72, 121 gives 85).
        public static float PlayerResistance(int percent) => EffectiveResistance(percent) / 100f;

        public static int EffectiveResistance(int resistance)
        {
            if (resistance <= 60) return resistance;
            if (resistance <= 80) return 60 + (int)((resistance - 60) * 0.5f);
            if (resistance <= 120) return 70 + (int)((resistance - 70) * 0.25f);
            if (resistance <= 200) return 80 + (int)((resistance - 80) * 0.125f);
            return 90;
        }

        // No single blow takes more than this share of your maximum health (update 140).
        public const float MaxHitShare = 0.48f;
        // Bleeding on creatures: 0.25% of their health a second (update 137, from 0.35%), at most 400 (from 600).
        public const float NpcBleedShare = 0.0025f;
        public const int NpcBleedCap = 400;
        // A kill's experience: (1.35 x level)^1.93 + 30 (Scr_NpcHandler.DropXp), shared between the combat skills
        // used in the last 20 seconds, less for a skill above the creature's level.
        public static int KillXp(int npcLevel) => (int)(Mathf.Pow(1.35f * Mathf.Max(1, npcLevel), 1.93f) + 30);
        public const float XpCounterSeconds = 20f;

        public static float XpShareAgainst(int skillLevel, int npcLevel)
        {
            if (skillLevel <= npcLevel) return 1f;
            float pow2 = Mathf.Pow(0.99995f, Mathf.Pow(Mathf.Abs(npcLevel - skillLevel), 3.2f));
            return (1 + (int)(10 + 90 * pow2)) * 0.01f;
        }
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
        // Outlaws block like players: their level against your accuracy, up to their gear's damage block.
        // Each outlaw's temperament: 15-35% of its swings an ability, shooters keeping 5-10 m away.
        public const int BountyAbilityChanceMin = 15, BountyAbilityChanceMax = 35;
        public const float BountyKiteMin = 5f, BountyKiteMax = 10f;
        public const float BountyStep = 2.5f;   // metres per half-second waypoint: a running player
        public const float BountyKiteCooldown = 8f;   // a shooter backs off at most this often
        // Reconstruction (the original had no outlaws): half again a creature of its level, but at most 4 times a
        // player's base health at its level, its gear's health on top. A creature's health grows with level^2.3 and
        // your blows with level, so uncapped a level 60 outlaw took 120-odd blows; capped (from about level 17) a
        // fight is some 24 average blows at any level, as the outlaw's blows against your health are.
        public const int BountyHealthPlayers = 4;
        public static int BountyHealth(int level) => Mathf.Min(NpcHealth(level) * 3 / 2, BountyHealthPlayers * BaseHealth(level));
        // 50 per level plus 8 x level^1.5 (about 900 at 10, 4,900 at 40, 8,300 at 60), 5% more per level above yours.
        public static int BountySilver(int level, int combatLevel) =>
            Mathf.RoundToInt((50f * level + 8f * Mathf.Pow(level, 1.5f)) * (1f + 0.05f * Mathf.Max(0, level - combatLevel)));
        // The explosive potion and black candy (Scr_PlayerInventory): (1000 + 2 x (Damage + FireDamage) + 40 x Alchemy)
        // x 0.8-1 fire to everything within 6 m after a 3 second fuse. It only hurt players in PvP, so not you.
        public const float ExplosiveFuse = 3f, ExplosionRadius = 6f;
        // After a stun wears off, the target can't be stunned again for this long (the original's buff 47).
        public const float StunImmunity = 5f;
        // Flying followers add a tenth of their grade's multiplier to your damage (8.8% Damaged to 17.2% Mythic with
        // update 138's grades; update 140 lowered it by an unrecorded amount).
        public static float FlyingPetBonus(int grade) => OfflineServer.GradeMultiplier(Mathf.Clamp(grade, 1, 8)) * 0.1f;

        // Bosses give double experience (update 137).
        public const int BossXpMultiplier = 2;

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

        // Combat level (update 136, Scr_PlayerHandler.CalculateCombatLevel): the three best of Archery, Healing, the
        // armor skills, Elemental grimoire, Swordsmanship and Wrestling in full, the other four a quarter, all x 0.333.
        public static readonly int[] CombatSkills = { 17, 24, 26, 25, 20, 15, 23 };

        public static int CombatLevel(List<int> levels)
        {
            levels.Sort((a, b) => b.CompareTo(a));
            int best = 0, rest = 0;
            for (int i = 0; i < levels.Count; i++)
                if (i < 3) best += levels[i];
                else rest += levels[i];
            return (int)((best + rest * 0.25f) * 0.333f);
        }

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
            areas.Clear();        // fire walls, mines and plants stay in the scene they were cast in
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
            if (PlayerStunned)
            {
                Notice("You have been stunned!", "red");
                return;
            }
            if (HasBuff(BuffTruce) || HasBuff(BuffTraverse)) return;   // the original's useAbility: neither lets you fight
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

        private void TickPlayerAttack(Vector3 playerPos)
        {
            MeasureSwing();
            if (!autoAttacking || PlayerDead || PlayerStunned || Time.time < nextPlayerSwing) return;
            var npc = world.GetNpc(targetUid);
            if (npc == null || npc.dead)
            {
                StopAttacking();
                return;
            }
            var weapon = EquippedWeapon();
            if (Vector3.Distance(NpcPosition(npc), playerPos) > WeaponRange(weapon)) return;   // walk closer

            // One swing per play of the attack animation. Damage per hit scales so damage per second stays that of
            // a swing every WeaponSpeed seconds (the original's attack time), and attack speed buffs add damage
            // rather than cut the animation short.
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
            var blow = BasicBlow(weapon);
            int projectile = blow.projectile;
            if (projectile == 0) PlaySound(swingSfx, playerPos);   // a shot's sound goes with the release

            float power = interval / (WeaponSpeed(weapon) * AttackSpeedMultiplier());
            bool punch = BasicBlowIsPunch(weapon);
            System.Action land = () =>
            {
                if (npc.dead) return;
                int damage = Mathf.RoundToInt((projectile > 0 ? ShotDamage(projectile, blow.damage, blow.explicitDamage) : blow.damage) * power);
                if (projectile == 41) FireArrowHit(npc, damage);   // Fire arrows
                else
                {
                    if (HitNpc(npc, damage, blow.type) > 0) PlaySound(hitSfx, NpcPosition(npc));
                    if (weapon != null && weapon.typeId == 380 && !npc.dead) BalSardanWandHit(npc, damage);
                    if (punch && Time.time >= punchStunReadyAt)
                    {
                        StunNpc(npc, 2f);
                        punchStunReadyAt = Time.time + 10f;
                    }
                }
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

        // Hit splash styles (Scr_EffectController.createHitSplash): 0 plain, 1 blocked, 3 weakened, 4 poisoned.
        private const int SplashPlain = 0, SplashBlocked = 1, SplashWeakened = 3, SplashPoisoned = 4;

        /// <summary>The basic attack is a punch: no weapon, or something that isn't one (a tool).</summary>
        private static bool BasicBlowIsPunch(ItemSave weapon)
        {
            if (weapon == null) return true;
            var ih = Scr_ItemHandler.instance;
            int t = weapon.typeId;
            return !(ih.isBroadsword(t) || ih.isGreatsword(t) || ih.isHalberd(t) || ih.isDagger(t) || ih.isMace(t) || ih.isBow(t)
                || ih.isCrossBow(t) || ih.isInstrument(t) || ih.isStaff(t) || ih.isWand(t));
        }

        /// <summary>A Fire arrows shot (projectile 41): the piercing hit, a fifth of it again as fire plus your
        /// FireDamage, and a burn of up to three stacks (10 + 0.3 x (Damage + FireDamage) + 2 x Archery a second, 10 s).</summary>
        private void FireArrowHit(NpcEntity npc, int damage)
        {
            int fire = StatDamageOf(DamageKinds.Fire);
            HitNpc(npc, damage, DamageKinds.Piercing);
            HitNpc(npc, (int)(damage * 0.2f) + fire, DamageKinds.Fire);
            int burn = (int)(10 + (StatDamage() + fire) * 0.3f + SkillLevel(SkillArchery) * 2f);
            for (int id = 87; id <= 89; id++)
                if (!npc.dots.Exists(d => d.id == id))
                {
                    AddDot(npc, id, burn, DamageKinds.Fire, 1f, 10f);
                    PlayNpcEffect(npc, 49);
                    break;
                }
        }

        /// <summary>The Bal Sardan wand: a second after its blow the target heals a quarter to four times it (buff 55).</summary>
        private void BalSardanWandHit(NpcEntity npc, int damage)
        {
            int heal = (int)Random.Range(damage * 0.25f, damage * 4f);
            Later(1f, () =>
            {
                if (npc.dead) return;
                npc.health = Mathf.Min(npc.maxHealth, npc.health + heal);
                Send(12, 0, npc.uid, npc.health, npc.maxHealth, 0);
            });
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

        /// <summary>Dev: a test character's base health, e.g. to stand in front of bosses (bridge command devhealth);
        /// 0 goes back to 100 per combat level.</summary>
        internal void DevSetHealth(int health)
        {
            devBaseHealth = Mathf.Max(0, health);
            character.health = devBaseHealth > 0 ? devBaseHealth : CombatRules.BaseHealth(CombatLevel());
            Send(16, 5, StatHealth, character.health);
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

            AwardKillXp(info);
            if (npc.master == null) DropLoot(npc, info);   // summoned minions carry nothing
            OnNpcKilled(npc.typeId);
            OnBossKilled(npc);
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
                npc.nextSwing = Time.time + info.attackSpeed * (Time.time < npc.chilledUntil ? 1.25f : 1f);   // chilled: a quarter slower
                Send(198, 28, npc.uid, playerPos);
                bool cast = BountyPrepareSwing(npc);   // an outlaw picks its ability first: the swing animates it
                SetAnim(npc, info.animAttack, true);
                npc.attackAnimUntil = Time.time + Mathf.Min(info.attackSpeed, AnimLength(info.animAttack));
                NpcSound(npc, info.sfxAttack);
                if (cast) continue;   // the swing was an outlaw's buff or spell, not a blow
                bool shoot = npc.blowProjectile > 0 || ranged && (dist > info.attackDistance || Random.Range(0, 100) < info.projectileRate);
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
                int projectile = npc.blowProjectile > 0 ? npc.blowProjectile
                    : info.projectiles.Length > 1 ? info.projectiles[Random.Range(0, info.projectiles.Length)] : info.projectile;
                NpcShoots(npc, projectile, () => { if (!PlayerDead && !npc.dead) NpcHitsPlayer(info, npc, projectile); });
                return;
            }
            // The original's blows land wherever you are within 50 m of the creature once its swing is under way.
            NpcHitsPlayer(info, npc);
        }

        /// <summary>
        /// A creature's blow (Scr_NPCAI.AttackHit): 1 to one less than its damage, or its shot's roll, then through
        /// your defences (<see cref="PlayerDamageTaken"/>) with its accuracy against your block.
        /// </summary>
        private void NpcHitsPlayer(NpcInfo info, NpcEntity attacker, int projectile = 0)
        {
            if (OutlawHitsPlayer(attacker, projectile)) return;   // an outlaw rolls its blows as a player does
            int max = CombatRules.NpcMaxHit(info);
            float roll = projectile > 0 ? NpcShotDamage(projectile, max) : Random.Range(1, Mathf.Max(2, max));
            int type = projectile > 0 && ProjectileType(projectile) > 0 ? ProjectileType(projectile) : info.damageType;
            if (projectile == 45 && character.currentMana > 0)   // Diminish takes mana too
            {
                character.currentMana = Mathf.Max(0, character.currentMana - (int)(roll * 0.05f));
                SendMana();
            }
            int damage = PlayerDamageTaken(Mathf.RoundToInt(roll), type, attacker, info.accuracy);
            if (LogBlows) Plugin.Log.LogInfo($"[blow] {info.name} ({info.id}) hits you for {damage} of {roll:0} (health {character.currentHealth - damage}/{MaxHealth()})");
            HurtPlayer(damage, PlayerSplash(info));
            if (projectile > 0) ShotEffects(projectile, Mathf.RoundToInt(roll));
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
            npc.slowedUntil = npc.freezeUntil = npc.chilledUntil = npc.weakenedUntil = 0f;
            npc.freezeStacks = 0;
            npc.dots.Clear();
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
            EndBuffsOnDeath();
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
            TrackMovement(p);
            TickPlayerRespawn();
            if (world == null || !sceneReady) return;   // respawned into another zone: this world is gone
            TickBuffs();
            TickNpcEffects();
            TickAreaEffects();
            TickCastEnd();
            TickSwingProfile();
            TickPlayerAttack(p);
            TickNpcs(p);
            TickBossMechanics(p);
        }
    }
}
