using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    internal enum BossMoveKind
    {
        Summon,      // minions appear beside the boss and join the fight
        Hazard,      // marked on the ground under the player, goes off after a delay where it was
        Lingering,   // a field under the player that hurts each second while they stand in it
        Nova,        // a blast around the boss after a wind-up
        Stun,        // the player can't move for a moment
    }

    /// <summary>One timed move of a boss fight. Numbers are reconstructions: the patch notes name the moves but
    /// rarely their values. Damage shares are of the boss's own maximum hit (CombatRules.NpcMaxHit).</summary>
    internal class BossMove
    {
        public BossMoveKind kind;
        public float every;            // seconds between uses while the boss is fighting
        public float delay;            // telegraph: seconds between the warning effect and the hit
        public float radius = 5f;
        public float share;            // damage as a share of the boss's maximum hit
        public float healthPerSecond;  // Lingering: share of the player's maximum health per second
        public float duration;         // Lingering: seconds the field lasts; Stun: seconds
        public int warnGfx, hitGfx;    // effects (XMLs "_GFX IDs")
        public int damageKind;         // DamageKinds index the player's resistance applies to (0 = none)
        public int[] minions;          // Summon: NPC types
        public int cap;                // Summon: most of them alive at once
        public string note;
    }

    public partial class OfflineServer
    {
        // Boss type -> its moves. Sources: the official patch notes (updates 130-140) and the client's effects list.
        private static readonly Dictionary<int, BossMove[]> BossMoves = new Dictionary<int, BossMove[]>
        {
            // Nirish, King of the Sewers: poison clouds you get 2 seconds to leave (update 137), and a healing
            // spiderling that mends him while it lives (update 140).
            { 275, new[] {
                new BossMove { kind = BossMoveKind.Hazard, every = 12f, delay = 2f, radius = 4f, share = 1.2f, warnGfx = 110, hitGfx = 110,
                    damageKind = DamageKinds.Poison, note = "poison cloud" },
                new BossMove { kind = BossMoveKind.Summon, every = 30f, minions = new[] { 276 }, cap = 1, note = "healing spiderling" },
            } },
            // Supreme Sea Serpent: 2 serpents, 2 fog fields at 5% health, water explosions (update 139).
            { 102, new[] {
                new BossMove { kind = BossMoveKind.Summon, every = 30f, minions = new[] { 101 }, cap = 2, note = "sea serpents" },
                new BossMove { kind = BossMoveKind.Lingering, every = 15f, radius = 5f, healthPerSecond = 0.05f, duration = 10f, warnGfx = 171, note = "fog field" },
                new BossMove { kind = BossMoveKind.Hazard, every = 20f, delay = 2.5f, radius = 5f, share = 1.5f, warnGfx = 174, hitGfx = 175, note = "water explosion" },
            } },
            // Zalranaz: psychic explosions (update 139) with the "zal finale wind up" effect, and a 2 second stun
            // (update 138).
            { 207, new[] {
                new BossMove { kind = BossMoveKind.Nova, every = 20f, delay = 2.5f, radius = 8f, share = 1.5f, warnGfx = 169, hitGfx = 170,
                    damageKind = DamageKinds.Psychic, note = "psychic explosion" },
                new BossMove { kind = BossMoveKind.Stun, every = 25f, radius = 8f, duration = 2f, note = "stun" },
            } },
            // Tomb guardians: psychic explosions on a 30 second cycle (update 139).
            { 227, TombGuardian() }, { 228, TombGuardian() }, { 229, TombGuardian() },
            // Sepitus, the Dark Tamer: summons lava hounds ("poacher dungeon boss / spawn lava doggo" effect).
            { 292, new[] {
                new BossMove { kind = BossMoveKind.Summon, every = 30f, minions = new[] { 296 }, cap = 2, hitGfx = 155, note = "infernal hounds" },
            } },
            // Lydia, the Tamer's Apprentice: the data's "summoner boss minions".
            { 362, new[] {
                new BossMove { kind = BossMoveKind.Summon, every = 30f, minions = new[] { 364, 365 }, cap = 2, note = "skeletal berserk and scout" },
            } },
            // Jotnar, the Crusher of Bones: the "troll slam explosion" effect.
            { 372, new[] {
                new BossMove { kind = BossMoveKind.Nova, every = 15f, delay = 1.5f, radius = 6f, share = 1.3f, hitGfx = 176, note = "troll slam" },
            } },
        };

        private static BossMove[] TombGuardian() => new[]
        {
            new BossMove { kind = BossMoveKind.Nova, every = 30f, delay = 2f, radius = 6f, share = 1.2f, hitGfx = 170,
                damageKind = DamageKinds.Psychic, note = "psychic explosion" },
        };

        private const int NirishSpiderling = 276;
        private const float SpiderlingHealPerSecond = 0.01f;   // of Nirish's health (reconstruction)

        // Per boss uid: when each move is next due.
        private readonly Dictionary<int, float[]> bossMoveAt = new Dictionary<int, float[]>();
        private float bossTick;

        private void TickBossMechanics(Vector3 playerPos)
        {
            bossTick -= Time.deltaTime;
            if (bossTick > 0f) return;
            bossTick = 0.5f;
            foreach (var boss in world.npcs.ToArray())
            {
                if (!BossMoves.TryGetValue(boss.typeId, out var moves)) continue;
                if (boss.dead || !boss.aggro || PlayerDead)
                {
                    if (bossMoveAt.Remove(boss.uid))
                    {
                        Plugin.Log.LogInfo($"[boss] {boss.uid} fight over (dead={boss.dead} aggro={boss.aggro} returning={boss.returning} playerDead={PlayerDead})");
                        DismissMinions(boss);   // the fight ended: start fresh next time
                    }
                    continue;
                }
                if (!bossMoveAt.TryGetValue(boss.uid, out var due))
                {
                    // First moves a few seconds into the fight, staggered.
                    due = new float[moves.Length];
                    for (int i = 0; i < moves.Length; i++) due[i] = Time.time + 5f + 3f * i;
                    bossMoveAt[boss.uid] = due;
                }
                for (int i = 0; i < moves.Length; i++)
                {
                    if (Time.time < due[i]) continue;
                    due[i] = Time.time + moves[i].every;
                    UseBossMove(boss, moves[i], playerPos);
                }
                if (boss.typeId == 275) MendNirish(boss);
            }
            world.npcs.RemoveAll(n => n.master != null && n.dead && Time.time > n.respawnAt);   // fallen minions fade away
        }

        private void UseBossMove(NpcEntity boss, BossMove m, Vector3 playerPos)
        {
            GameData.Npcs.TryGetValue(boss.typeId, out var info);
            if (info == null) return;
            Vector3 bossPos = NpcPosition(boss);
            Plugin.Log.LogInfo($"[boss] {info.name} ({boss.uid}): {m.note} at {Time.time:0.0}");
            switch (m.kind)
            {
                case BossMoveKind.Summon:
                    SummonMinions(boss, m);
                    break;
                case BossMoveKind.Hazard:
                {
                    Vector3 spot = playerPos;
                    PlayEffect(m.warnGfx, spot);
                    Later(m.delay, () =>
                    {
                        if (boss.dead || !boss.aggro) return;
                        PlayEffect(m.hitGfx, spot);
                        if (PlayerWithin(spot, m.radius)) HazardHit(info, m, m.note);
                    });
                    break;
                }
                case BossMoveKind.Lingering:
                {
                    Vector3 spot = playerPos;
                    PlayEffect(m.warnGfx, spot);
                    for (int s = 1; s <= Mathf.RoundToInt(m.duration); s++)
                        Later(s, () =>
                        {
                            if (boss.dead || !boss.aggro || PlayerDead || !PlayerWithin(spot, m.radius)) return;
                            int dmg = Mathf.Max(1, Mathf.RoundToInt(MaxHealth() * m.healthPerSecond));
                            if (LogBlows) Plugin.Log.LogInfo($"[blow] {info.name} ({info.id}) {m.note} hurts you for {dmg}");
                            HurtPlayer(dmg, SplashPlain);
                        });
                    break;
                }
                case BossMoveKind.Nova:
                    if (m.warnGfx > 0) PlayNpcEffect(boss, m.warnGfx);
                    Later(m.delay, () =>
                    {
                        if (boss.dead || !boss.aggro) return;
                        Vector3 at = NpcPosition(boss);
                        PlayEffect(m.hitGfx, at);
                        if (PlayerWithin(at, m.radius)) HazardHit(info, m, m.note);
                    });
                    break;
                case BossMoveKind.Stun:
                    if (!PlayerWithin(bossPos, m.radius)) break;
                    PlayPlayerEffect(21);
                    Send(1, 4, character.name, false);   // the client lets the player move again on true
                    Later(m.duration, () => { if (!PlayerDead && character != null) Send(1, 4, character.name, true); });
                    break;
            }
        }

        private bool PlayerWithin(Vector3 spot, float radius)
        {
            var p = LocalPlayer;
            if (p == null) return false;
            Vector3 d = p.transform.position - spot;
            d.y = 0f;
            return d.magnitude <= radius;
        }

        /// <summary>A hazard's damage: a share of the boss's maximum hit, through the player's resistance and buffs.</summary>
        private void HazardHit(NpcInfo info, BossMove m, string what)
        {
            float dmg = CombatRules.NpcMaxHit(info) * m.share * Random.Range(0.8f, 1f);
            if (m.damageKind > 0) dmg *= 1f - CombatRules.PlayerResistance(GearStat(DamageKinds.ResistanceStat(m.damageKind)));
            int hit = ApplyDefensiveBuffs(Mathf.RoundToInt(dmg), null);
            hit = Mathf.Min(hit, Mathf.CeilToInt(MaxHealth() * CombatRules.MaxHitShare));
            lastHitTaken = Time.time;
            if (LogBlows) Plugin.Log.LogInfo($"[blow] {info.name} ({info.id}) {what} hits you for {hit}");
            HurtPlayer(hit, m.damageKind == DamageKinds.Poison ? SplashPoisoned : SplashPlain);
        }

        private void SummonMinions(NpcEntity boss, BossMove m)
        {
            int alive = world.npcs.FindAll(n => n.master == boss && !n.dead).Count;
            Vector3 at = NpcPosition(boss);
            if (m.hitGfx > 0) PlayEffect(m.hitGfx, at);
            for (int i = 0; alive < m.cap; i++, alive++)
            {
                int type = m.minions[i % m.minions.Length];
                int hp = GameData.Npcs.TryGetValue(type, out var info) ? info.health : 30;
                Vector2 o = Random.insideUnitCircle.normalized * 3f;
                Vector3 p = at + new Vector3(o.x, 0f, o.y);
                if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    p = hit.point;
                var minion = new NpcEntity { uid = world.sceneId * 10000 + 9800 + (nextMinion++ % 100), typeId = type, spawnPos = p, pos = p, health = hp, maxHealth = hp, master = boss };
                world.npcs.Add(minion);
                Aggro(minion);
                Plugin.Log.LogInfo($"[boss] summoned {info?.name} ({type}) as {minion.uid}");
            }
            visibilityTimer = 0f;
        }

        private int nextMinion;

        /// <summary>Nirish's spiderling mends him while it lives (update 140).</summary>
        private void MendNirish(NpcEntity nirish)
        {
            if (nirish.health >= nirish.maxHealth || !world.npcs.Exists(n => n.master == nirish && n.typeId == NirishSpiderling && !n.dead)) return;
            int heal = Mathf.Max(1, Mathf.RoundToInt(nirish.maxHealth * SpiderlingHealPerSecond * 0.5f));   // per half-second tick
            nirish.health = Mathf.Min(nirish.maxHealth, nirish.health + heal);
            Send(12, 0, nirish.uid, nirish.health, nirish.maxHealth, 0);
        }

        /// <summary>A boss died or the fight ended: its minions go too.</summary>
        private void DismissMinions(NpcEntity boss)
        {
            int n = world.npcs.RemoveAll(x => x.master == boss);
            if (n > 0) visibilityTimer = 0f;
        }

        private void OnBossKilled(NpcEntity npc)
        {
            if (!BossMoves.ContainsKey(npc.typeId)) return;
            bossMoveAt.Remove(npc.uid);
            DismissMinions(npc);
        }
    }
}
