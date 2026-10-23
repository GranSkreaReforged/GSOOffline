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
        Absorb,      // warning, then a short window in which the player's hits come back at them
    }

    /// <summary>One timed move of a boss fight. Numbers are reconstructions: the patch notes name the moves but
    /// rarely their values. Damage shares are of the boss's own maximum hit (CombatRules.NpcMaxHit, the original's formula).</summary>
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
        // Boss type -> its moves (Nirish, George, Sytrax and Sepitus run the original server's scripts instead:
        // OfflineServer.OriginalBosses.cs). Sources: the official patch notes (updates 130-140) and the client's effects list.
        private static readonly Dictionary<int, BossMove[]> BossMoves = new Dictionary<int, BossMove[]>
        {
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
                Absorb(),
            } },
            // Tomb guardians: psychic explosions on a 30 second cycle (update 139).
            { 227, TombGuardian() }, { 228, TombGuardian() }, { 229, TombGuardian() },
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
            Absorb(),
        };

        // Absorb (updates 138-139 name it for Zalranaz and the Tomb guardians but never say what it did; this is
        // made up): the boss warns, 2 seconds later its shield is up for 4 seconds and every blow or spell the player
        // lands on it comes back at them in full, then it says the shield is down. Speech bubbles carry the timing.
        private const float AbsorbWarning = 2f, AbsorbWindow = 4f;
        private const int GfxAbsorb = 100;   // "black hole into healing"

        private static BossMove Absorb() => new BossMove { kind = BossMoveKind.Absorb, every = 35f, note = "absorb" };


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
            }
            foreach (var n in world.npcs)
                if (BossMoves.TryGetValue(n.typeId, out var m) && System.Array.Exists(m, x => x.kind == BossMoveKind.Absorb))
                    ShowAbsorbAura(n, !n.dead && Time.time < n.absorbUntil);
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
                            int dmg = Mathf.Max(1, Mathf.RoundToInt(MaxHealth() * m.healthPerSecond * CombatRules.WorldShare(info)));
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
                case BossMoveKind.Absorb:
                    NpcSay(boss, "I will drink your strength...");
                    boss.absorbUntil = Time.time + AbsorbWarning + AbsorbWindow;   // the aura shows from the warning on
                    boss.reflectFrom = Time.time + AbsorbWarning;
                    Notice($"{info.name} begins to absorb. Stop attacking!", "orange");
                    PlayNpcEffect(boss, GfxAbsorb);
                    Later(AbsorbWarning, () =>
                    {
                        if (boss.dead || !boss.aggro) return;
                        NpcSay(boss, "Strike me, and suffer!");
                        PlayNpcEffect(boss, GfxAbsorb);
                        Later(AbsorbWindow, () =>
                        {
                            if (boss.dead) return;
                            NpcSay(boss, "The dark shield fades.");
                            Notice($"{info.name}'s shield is down. Attack!", "green");
                        });
                    });
                    break;
                case BossMoveKind.Stun:
                    if (!PlayerWithin(bossPos, m.radius)) break;
                    if (StunPlayer(m.duration)) PlayPlayerEffect(21);
                    break;
            }
        }

        /// <summary>
        /// Zalranaz's red orbs (OrbSmallRed in his model) shimmer only while he absorbs, so the aura itself warns.
        /// Set every tick, so a model streamed in again picks it up.
        /// </summary>
        private static void ShowAbsorbAura(NpcEntity boss, bool on)
        {
            var c = Scr_NpcHandler.instance != null ? Scr_NpcHandler.instance.getNpc(boss.uid) : null;
            if (c == null) return;
            foreach (var t in c.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("OrbSmallRed") && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }

        /// <summary>3/30: a speech bubble over an NPC.</summary>
        private void NpcSay(NpcEntity npc, string text) => Send(3, 30, text, npc.uid);

        /// <summary>A blow landed on an absorbing boss: it hurts the player instead, through the usual caps.</summary>
        private void ReflectAbsorbed(NpcEntity boss, int damage)
        {
            int hit = PlayerDamageTaken(damage, DamageKinds.None, null, 100);
            Plugin.Log.LogInfo($"[boss] {boss.uid} absorbs {damage} and reflects {hit}");
            HurtPlayer(hit, SplashPlain);
        }

        private bool PlayerWithin(Vector3 spot, float radius)
        {
            var p = LocalPlayer;
            if (p == null) return false;
            Vector3 d = p.transform.position - spot;
            d.y = 0f;
            return d.magnitude <= radius;
        }

        /// <summary>A hazard's damage: a share of the boss's maximum hit, through the player's defences.</summary>
        private void HazardHit(NpcInfo info, BossMove m, string what)
        {
            float dmg = CombatRules.NpcMaxHit(info) * m.share * Random.Range(0.8f, 1f) * CombatRules.WorldShare(info);
            int hit = PlayerDamageTaken(Mathf.RoundToInt(dmg), m.damageKind, null, info.accuracy);
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
