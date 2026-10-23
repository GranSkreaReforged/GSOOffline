using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// The staffs' summons (Scr_PlayerAttacksHandler abilities 176 and 177, Scr_PlayerPets): Lydia's staff calls a
    /// skeletal berserk, Roke's a skeleton, for 118 seconds. Each takes half your maximum mana (at most 600 or 250) out
    /// of your pool while it lasts (the buff, less with the Dark Tamer's set) and hits harder the more it took: its own
    /// damage plus 40% of your Damage and PsychicDamage, times 0.23% (berserk) or 0.3% (skeleton) per mana point and
    /// your weapon's grade. It fights what you fight, like a pet, and can't be hurt (isPet). Lydia's earrings and
    /// Roke's ring add to its blows. Dying sends it away; the mana stays held until the buff ends.
    /// </summary>
    public partial class OfflineServer
    {
        private const int SkeletalBerserk = 366, SummonedSkeleton = 367, BuffSkeletalBerserk = 101, BuffSkeleton = 102;
        private const float SummonSeconds = 118f;

        private class SummonState
        {
            public NpcEntity npc;
            public int damage;
            public float until, nextHit;
        }

        private readonly List<SummonState> summons = new List<SummonState>();
        private float summonTick;

        /// <summary>Mana your summons hold out of your pool (the buffs' amounts).</summary>
        private int SummonManaHeld() => IconAmount(BuffSkeletalBerserk) + IconAmount(BuffSkeleton);

        private void Summon(bool berserk)
        {
            int buff = berserk ? BuffSkeletalBerserk : BuffSkeleton;
            if (HasBuff(buff) || world == null || LocalPlayer == null) return;   // one of each at a time
            int type = berserk ? SkeletalBerserk : SummonedSkeleton;
            int consumed = Mathf.Min(MaxMana() / 2, berserk ? 600 : 250);
            float manaMultiplier = consumed * (berserk ? 0.0023f : 0.003f);
            int cost = (int)(consumed * DarkTamerSummonCost());
            var weapon = EquippedWeapon();
            float grade = weapon != null ? WeaponGradeMultiplier(weapon.grade) : 1f;
            GameData.Npcs.TryGetValue(type, out var info);
            int baseDamage = info != null ? CombatRules.NpcMaxHit(info) : 10;
            baseDamage += (int)((StatDamage() + StatDamageOf(DamageKinds.Psychic)) * 0.4f);
            int damage = (int)(baseDamage * manaMultiplier * grade);

            AddBuff(-buff, buff, SummonSeconds, null, cost);
            ManaStatsChanged();
            Vector3 at = BesidePlayer(LocalPlayer.transform.position);
            var npc = new NpcEntity { uid = world.sceneId * 10000 + 9710 + (berserk ? 0 : 1), typeId = type, spawnPos = at, pos = at, health = 1, maxHealth = 1 };
            world.npcs.Add(npc);
            visibilityTimer = 0f;
            summons.Add(new SummonState { npc = npc, damage = damage, until = Time.time + SummonSeconds });
            Plugin.Log.LogInfo($"[summon] {info?.name} ({type}) as {npc.uid}: {damage} damage, {cost} mana held");
        }

        /// <summary>Four times a second: summons follow you and fight your target until their time is up.</summary>
        private void TickSummons()
        {
            if (summons.Count == 0) return;
            summonTick -= Time.deltaTime;
            if (summonTick > 0f) return;
            summonTick = 0.25f;
            var player = LocalPlayer;
            foreach (var s in summons.ToArray())
            {
                if (Time.time >= s.until || world == null || !world.npcs.Contains(s.npc) || PlayerDead)
                {
                    DismissSummon(s);
                    continue;
                }
                if (player == null) continue;
                Vector3 me = player.transform.position;
                var target = PetTarget();
                if (target == null)
                {
                    KeepBeside(s.npc, me);
                    continue;
                }
                GameData.Npcs.TryGetValue(s.npc.typeId, out var info);
                float reach = (info != null ? info.attackDistance : 3f) + 0.5f;
                Vector3 targetPos = NpcPosition(target);
                if (Vector3.Distance(NpcPosition(s.npc), targetPos) > reach)
                {
                    MoveNpc(s.npc, targetPos);
                    continue;
                }
                if (Time.time < s.nextHit) continue;
                float speed = info != null ? info.attackSpeed : 2f;
                s.nextHit = Time.time + speed;
                s.npc.hasDest = false;
                Send(13, 2, s.npc.uid, false);
                Send(198, 28, s.npc.uid, targetPos);
                if (info != null)
                {
                    SetAnim(s.npc, info.animAttack, true);
                    s.npc.attackAnimUntil = Time.time + Mathf.Min(speed, AnimLength(info.animAttack));
                }
                var victim = target;
                var summon = s;
                Later(info != null ? Mathf.Min(info.attackDelay, speed) : 0.25f, () => SummonHits(summon, victim));
            }
        }

        /// <summary>A summon's blow: 60-100% of its damage, with the earrings, the ring and the Dark Tamer's set.</summary>
        private void SummonHits(SummonState s, NpcEntity target)
        {
            if (!summons.Contains(s) || target.dead || PlayerDead) return;
            float max = s.damage;
            if (Wears("lydiasEarrings")) max *= 1.125f;
            if (Wears("rokesRing")) max *= 1.1f;
            max *= DarkTamerPetMultiplier();
            int kind = GameData.Npcs.TryGetValue(s.npc.typeId, out var info) ? info.damageType : 0;
            int damage = (int)Random.Range(max * 0.6f, max);
            Aggro(target);
            int hit = NpcDamageTaken(target, damage, kind, true, HitNoWeaponGrade);
            Plugin.Log.LogInfo($"[summon] {s.npc.uid} hits {target.uid} for {hit} of {damage}");
            DealDamage(target, hit, SkillElementalGrim);
        }

        private void DismissSummon(SummonState s)
        {
            summons.Remove(s);
            if (world != null && world.npcs.Remove(s.npc)) visibilityTimer = 0f;
        }

        private void DismissSummons()
        {
            foreach (var s in summons.ToArray()) DismissSummon(s);
        }
    }
}
