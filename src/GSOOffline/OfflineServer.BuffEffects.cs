using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// What the original's player buffs did, by client buff id (Scr_PlayerBuffs.buffEffects and endBuff, and the
    /// hasBuff checks in Scr_PlayerHandler, Scr_PlayerInventory and Scr_Projectile): run speed, resistance and
    /// damage bonuses, stuns and roots, damage over time, and the debuffs NPC shots leave on you. Effects tied to
    /// one ability (Stability's halving, Flame barrier's reflection...) stay in OriginalAbilities and OriginalCombat.
    /// </summary>
    public partial class OfflineServer
    {
        // Client buff ids (XMLs/Buffs).
        private const int BuffSlow = 1, BuffStun = 2, BuffFood = 3, BuffPotionPoisoning = 4, BuffIgnite = 9, BuffDrunk = 13,
            BuffShieldBlock = 16, BuffWeakPoison = 27, BuffRooted = 29, BuffStamina = 30, BuffSwiftness = 33,
            BuffStunImmunity = 47, BuffCharge = 49, BuffStability = 52, BuffTruce = 53, BuffOoze = 54,
            BuffTeleportCooldown = 58, BuffSnakebite = 70, BuffWeakness = 76, BuffSmolder = 84, BuffWindwalking = 91,
            BuffTraverse = 96, BuffFlyingFollower = 99;

        private const int SceneKeshk = 33;   // the Keshk raid, where a snakebite triples poison damage
        private const float PassiveSeconds = 100 * 24 * 3600;   // a buff that lasts while its cause does

        private int IconAmount(int icon) => buffs.Find(b => b.icon == icon)?.amount ?? 0;

        private float BuffTimeLeft(int icon)
        {
            float left = 0f;
            foreach (var b in buffs)
                if (b.icon == icon) left = Mathf.Max(left, b.until - Time.time);
            return left;
        }

        // ---- run speed ----------------------------------------------------------------------------------------

        // The original's run speed: stat Movementspeed, 8 for every character, plus statBonus_Movementspeed: +5
        // Swiftness and Charge, -5 Slow and Stability, Windwalking and Traverse their amount, and gear's
        // Movementspeed, and riding a mount. Each buff counts once, however many things gave it.
        private const int BaseRunSpeed = 8;

        private int RunSpeedBonus()
        {
            int sum = GearStat("Movementspeed");
            if (HasBuff(BuffSwiftness)) sum += 5;
            if (HasBuff(BuffCharge)) sum += 5;
            if (HasBuff(BuffSlow)) sum -= 5;
            if (HasBuff(BuffStability)) sum -= 5;
            if (Mounted) sum += CombatRules.MountRunBonus;
            return sum + IconAmount(BuffWindwalking) + IconAmount(BuffTraverse);
        }

        // ---- stats --------------------------------------------------------------------------------------------

        // Resistance potions: +15 to their kind for as long as they last.
        private static readonly Dictionary<int, int> ResistancePotionKinds = new Dictionary<int, int>
        {
            { 17, DamageKinds.Poison }, { 18, DamageKinds.Fire }, { 19, DamageKinds.Earth }, { 20, DamageKinds.Wind },
            { 21, DamageKinds.Electric }, { 22, DamageKinds.Psychic }, { 23, DamageKinds.Ice }, { 24, DamageKinds.Slash },
            { 25, DamageKinds.Piercing }, { 26, DamageKinds.Blunt },
        };

        /// <summary>What buffs add to a kind's resistance: its potion +15, Weakness -15 to every kind.</summary>
        private int BuffResistance(int kind)
        {
            int v = 0;
            foreach (var kv in ResistancePotionKinds)
                if (kv.Value == kind && HasBuff(kv.Key)) v += 15;
            if (HasBuff(BuffWeakness)) v -= 15;
            return v;
        }

        /// <summary>A buff came or went: the stats window and the HUD hear the new numbers.</summary>
        private void BuffStatsChanged()
        {
            if (character == null || !sceneReady) return;
            SendGearStats();
        }

        // ---- stuns and roots ----------------------------------------------------------------------------------

        /// <summary>
        /// The original's basic stun (buff 2): no moving, attacking, abilities or items. Charge and Traverse keep it
        /// off, and so does the 4 s stun immunity that follows one, unless <paramref name="scripted"/> (a boss's
        /// move that chains stuns). A stun while stunned does nothing. True if it took.
        /// </summary>
        private bool StunPlayer(float seconds, bool scripted = false)
        {
            if (character == null || PlayerDead || HasBuff(BuffStun) || HasBuff(BuffCharge) || HasBuff(BuffTraverse)) return false;
            if (!scripted && HasBuff(BuffStunImmunity)) return false;
            AddBuff(-BuffStun, BuffStun, seconds, null);
            RemoveBuff(AbilityFlameLance, false);   // a stun puts out Flame lance
            UpdateCanMove();
            return true;
        }

        /// <summary>Rooted (buff 29): can't move, can still fight. Charge and Traverse keep it off.</summary>
        private void RootPlayer(float seconds)
        {
            if (character == null || PlayerDead || HasBuff(BuffCharge) || HasBuff(BuffTraverse)) return;
            AddBuff(-BuffRooted, BuffRooted, seconds, null);
            UpdateCanMove();
        }

        private bool PlayerStunned => HasBuff(BuffStun);

        /// <summary>The original's setPlayerCanMove: nothing that holds the player still is left.</summary>
        private bool PlayerCanMove() => character != null && !PlayerDead && !HasBuff(BuffStun) && !HasBuff(BuffRooted)
            && !HasBuffFrom(AbilityCryostasis) && !heldByBroker && !washedAway;

        private void UpdateCanMove()
        {
            if (character != null) Send(1, 4, character.name, PlayerCanMove());
        }

        /// <summary>What ending a buff does besides the icon (endBuff): a stun leaves 4 s of stun immunity.</summary>
        private void BuffEnded(ActiveBuff b, bool expired)
        {
            if (b.icon == BuffStun && expired) AddBuff(-BuffStunImmunity, BuffStunImmunity, 4f, null);
            if (b.icon == BuffWhiskerSickness && expired && RatsTailUnderway)
                AddBuff(-BuffWhiskerLingering, BuffWhiskerLingering, PassiveSeconds, null);
            if ((b.icon == BuffStun || b.icon == BuffRooted || b.ability == AbilityCryostasis) && !PlayerDead) UpdateCanMove();
        }

        // ---- damage over time ---------------------------------------------------------------------------------

        /// <summary>A buff that hurts every <paramref name="every"/> seconds: true damage, or reduced by your resistance.</summary>
        private void AddDotBuff(int icon, int damage, float every, float seconds, int kind, bool trueDamage, int gfx = 0)
        {
            if (damage <= 0) return;
            var b = AddBuff(-icon, icon, seconds, new BuffEffect { dot = damage, dotEvery = every, dotKind = kind, dotTrue = trueDamage, dotGfx = gfx });
            b.nextDot = Time.time + every;
        }

        private void TickDots()
        {
            if (PlayerDead) return;
            foreach (var b in buffs.ToArray())
            {
                if (b.effect.dot <= 0 || Time.time < b.nextDot) continue;
                b.nextDot += b.effect.dotEvery;
                int damage = b.effect.dot;
                int kind = b.effect.dotKind;
                if (!b.effect.dotTrue && kind > 0 && kind < DamageKinds.Count)
                    damage = (int)(damage * (0.01f * (100f - CombatRules.EffectiveResistance(StatResistance(kind)))));
                if (b.effect.dotGfx > 0) PlayPlayerEffect(b.effect.dotGfx);
                if (kind == DamageKinds.Fire) PlaySound(265, PlayerPos, 30);
                HurtPlayer(damage, kind == DamageKinds.Poison ? SplashPoisoned : SplashPlain);
                if (PlayerDead) return;
            }
        }

        // ---- NPC shots ----------------------------------------------------------------------------------------

        /// <summary>
        /// What an NPC's shot leaves on you besides its damage (Scr_Projectile, an NPC owner hitting a player).
        /// <paramref name="damage"/> is the shot's damage before your defences, which damage over time is a share of.
        /// </summary>
        private void ShotEffects(int projectile, int damage)
        {
            if (PlayerDead || character == null) return;
            Vector3 me = PlayerPos;
            switch (projectile)
            {
                case 11:   // fire arrow (ability): ignites for 30% a second for 5 s
                    AddDotBuff(BuffIgnite, (int)(damage * 0.3f), 1f, 5f, DamageKinds.Fire, false);
                    break;
                case 12:   // ice arrow (Poachers)
                    StunPlayer(2f);
                    break;
                case 14:   // poison arrow: 40% every 5 s for 30 s
                    AddDotBuff(BuffWeakPoison, (int)(damage * 0.4f), 5f, 30f, DamageKinds.Poison, true, 110);
                    break;
                case 15:   // weakness arrow
                    AddBuff(-BuffWeakness, BuffWeakness, 28f, null);
                    break;
                case 17:   // guards' freezing arrow
                    Notice("You have been frozen!", "red");
                    StunPlayer(8f);
                    PlayEffect(8, me);
                    break;
                case 19:   // hunting net
                    Notice("You have been slowed!", "red");
                    if (!HasBuff(BuffSlow)) PlayPlayerEffect(20);
                    AddBuff(-BuffSlow, BuffSlow, 8f, null);
                    break;
                case 29:   // Golem's stun
                case 37:   // Ent's strangle vine
                    Notice("You have been stunned!", "red");
                    StunPlayer(3f);
                    PlayEffect(8, me);
                    if (projectile == 37)
                    {
                        PlayEffect(12, me);
                        PlaySound(249, me, 40);
                    }
                    break;
                case 35:   // Ravannae's ooze: 20% a second for 5 s, as poison
                    AddDotBuff(BuffOoze, (int)(damage * 0.2f), 1f, 5f, DamageKinds.Poison, true, 38);
                    break;
                case 38:   // Vipers' and Nirish's snakebite
                    AddBuff(-BuffSnakebite, BuffSnakebite, 10f, null);
                    PlayPlayerEffect(111);
                    break;
                case 55:   // Aellope's cyclone: half as long behind a shield block
                    Notice("You have been stunned!", "red");
                    StunPlayer(HasBuff(BuffShieldBlock) ? 2.5f : 5f);
                    PlayEffect(8, me);
                    break;
            }
        }

        // ---- A Rat's Tail ------------------------------------------------------------------------------------

        private const int QuestRatsTail = 27, BuffWhiskerSickness = 80, BuffWhiskerLingering = 81;

        private bool RatsTailUnderway => character != null && character.GetQuestPhase(QuestRatsTail) >= 2;

        /// <summary>
        /// A Rat's Tail: when the giant rat falls its poison sets in (Whisker sickness, 80): 5 + 2% of your health as
        /// true damage every 30 s for 10 minutes. After that a harmless lingering sickness (81) stays until the quest
        /// is done, which cures both. Called on a quest phase change and on entering a scene (buffs aren't saved).
        /// </summary>
        private void UpdateWhiskerSickness(int quest)
        {
            if (quest != QuestRatsTail || character == null) return;
            if (character.GetQuestPhase(QuestRatsTail) == -1)
            {
                RemoveBuff(-BuffWhiskerSickness, false);
                RemoveBuff(-BuffWhiskerLingering, false);
            }
            else if (character.GetQuestPhase(QuestRatsTail) == 2 && !HasBuff(BuffWhiskerSickness) && !HasBuff(BuffWhiskerLingering))
                AddDotBuff(BuffWhiskerSickness, 5 + (int)(MaxHealth() * 0.02f), 30f, 600f, DamageKinds.Poison, true);
            else if (RatsTailUnderway && !HasBuff(BuffWhiskerSickness) && !HasBuff(BuffWhiskerLingering))
                AddBuff(-BuffWhiskerLingering, BuffWhiskerLingering, PassiveSeconds, null);
        }

        /// <summary>Dev: an NPC shot's effects without the shot (bridge command shothit).</summary>
        internal void DevShotHit(int projectile, int damage)
        {
            ShotEffects(projectile, damage);
            Plugin.Log.LogInfo($"[dev] shot {projectile} ({damage}): can move {PlayerCanMove()}, stunned {PlayerStunned}, run bonus {RunSpeedBonus()}");
        }

        /// <summary>Dev: rooted (bridge command root).</summary>
        internal void DevRoot(float seconds)
        {
            RootPlayer(seconds);
            Plugin.Log.LogInfo($"[dev] root {seconds}s: can move {PlayerCanMove()}");
        }

        // ---- icons --------------------------------------------------------------------------------------------

        /// <summary>A new scene drops the client's buff icons: every active buff shows again with the time it has left.</summary>
        private void ResendBuffIcons()
        {
            if (character == null) return;
            var shown = new HashSet<int>();
            foreach (var b in buffs)
            {
                if (b.icon <= 0 || !shown.Add(b.icon)) continue;
                int left = Mathf.RoundToInt(BuffTimeLeft(b.icon) * 10f);
                if (left > 0) Send(4, 9, character.name, b.icon, left, Mathf.Max(left, b.totalTenths));
            }
        }
    }
}
