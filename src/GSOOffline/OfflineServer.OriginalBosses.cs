using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Bosses as the original server scripted them (Scr_YorkhillSewers, Scr_PoacherDungeon). They replace this server's
    /// reconstructed moves for Nirish and Sepitus; George and Sytrax had none. Each runs while the player is near.
    /// - Nirish: every 15-25 s he stops and webs you (a 1 s stun), pulling you to him a second later (stunned 2 s);
    ///   below a third of your health he webs you every 1.5 s. Below 60% health, with you close, he scatters poison
    ///   clouds whose venom eats 20% of your health a second for 20 s. Below 35% a spiderling crawls to him from a
    ///   corner of the hall and heals him by a third if it gets there.
    /// - George: every 6 s he throws dynamite around the room, more the more he's hurt (none at full health, 30 near
    ///   death); each stick bursts 4 m wide.
    /// - Sytrax: 15 s on the ground, then he takes off, hovers and spits fire around you for 5 s, and lands.
    /// - Sepitus: a 3 s charge at you, then a bolt that takes 30% of your health; 10 s of fighting; a second charge
    ///   whose bolt also plagues you for 10 s (heals hurt instead); then, if he and Sytrax are far apart in health, a
    ///   healing bird from the healthier one evens them up. Below half health he calls three lava hounds every 30 s.
    /// Blows given in the original's numbers are taken as the same share of the player's health (HurtByOriginal).
    /// </summary>
    public partial class OfflineServer
    {
        private const int Nirish = 275, Spiderling = 276, Sytrax = 293, LavaHound = 296;
        private const int BuffVenom = 77, BuffPlague = 83;
        private const float BossTick = 0.25f;

        private float originalBossTick;

        private void TickOriginalBosses()
        {
            if (character == null || !sceneReady || world == null) return;
            originalBossTick -= Time.deltaTime;
            if (originalBossTick > 0f) return;
            originalBossTick = BossTick;
            var player = LocalPlayer;
            if (player == null) return;
            Vector3 me = player.transform.position;
            if (world.sceneId == SceneSewers) TickNirish(me);
            if (world.sceneId == ScenePoacher)
            {
                TickGeorge(me);
                TickSytrax(me);
                TickSepitus(me);
            }
        }

        /// <summary>Dev: npcpercent.</summary>
        internal void DevNpcPercent(int uid, int percent)
        {
            var n = world?.GetNpc(uid);
            if (n == null) return;
            n.health = Mathf.Clamp(n.maxHealth * percent / 100, 1, n.maxHealth);
            Send(12, 0, n.uid, n.health, n.maxHealth, 0);
            Plugin.Log.LogInfo($"[dev] npc {uid} health {n.health}/{n.maxHealth}");
        }

        private NpcEntity Boss(int type) => world.npcs.Find(n => n.typeId == type && n.master == null);

        private static float HealthShare(NpcEntity n) => n.maxHealth > 0 ? (float)n.health / n.maxHealth : 0f;

        private float PlayerHealthShare => (float)character.currentHealth / Mathf.Max(1, MaxHealth());

        private void HealNpc(NpcEntity n, int amount)
        {
            n.health = Mathf.Min(n.maxHealth, n.health + amount);
            Send(12, 0, n.uid, n.health, n.maxHealth, 0);
        }

        /// <summary>Stops a boss for a while: no swings, no steps (the AI waits out stunnedUntil).</summary>
        private void PauseBoss(NpcEntity n, float seconds)
        {
            n.stunnedUntil = Mathf.Max(n.stunnedUntil, Time.time + seconds);
            if (n.attackingSent)
            {
                n.attackingSent = false;
                Send(11, 0, n.uid, false, string.Empty);
            }
            HoldNpc(n);
        }

        private void Stun(float seconds)
        {
            Send(1, 4, character.name, false);
            Later(seconds, () => { if (!PlayerDead && character != null) Send(1, 4, character.name, true); });
        }

        private NpcEntity SpawnBossMinion(NpcEntity boss, int type, Vector3 at, bool fights)
        {
            if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out var hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                at = hit.point;
            int hp = GameData.Npcs.TryGetValue(type, out var info) ? info.health : 30;
            var minion = new NpcEntity { uid = world.sceneId * 10000 + 9800 + (nextMinion++ % 100), typeId = type, spawnPos = at, pos = at, health = hp, maxHealth = hp, master = boss };
            world.npcs.Add(minion);
            if (fights) Aggro(minion);
            else minion.flying = true;   // scripted: walks where the script sends it
            visibilityTimer = 0f;
            return minion;
        }

        // ---- Nirish ------------------------------------------------------------------------------

        private static readonly Vector3[] SpiderlingStarts = { new Vector3(135, 57, 368), new Vector3(171, 57, 377), new Vector3(142, 57, 349), new Vector3(185, 57, 364) };

        private class PoisonCloud
        {
            public Vector3 pos;
            public float from, until;
        }

        private readonly List<PoisonCloud> poisonClouds = new List<PoisonCloud>();
        private float nirishSpecialAt, nirishLowPullAt, nirishCloudCheckAt;
        private bool nirishPulling;

        private void TickNirish(Vector3 me)
        {
            var nirish = Boss(Nirish);
            poisonClouds.RemoveAll(c => Time.time >= c.until);
            if (nirish == null || nirish.dead)
            {
                if (nirish != null) DismissMinions(nirish);
                return;
            }
            Vector3 at = NpcPosition(nirish);
            float dist = Vector3.Distance(at, me);
            TickSpiderling(nirish, at);
            TickPoisonClouds(me);
            if (PlayerDead || dist > 40f) return;

            if (!nirishPulling && Time.time >= nirishLowPullAt && PlayerHealthShare < 0.333f && dist < 25f)
            {
                nirishLowPullAt = Time.time + 1.5f;
                WebAndPull(nirish, at);
            }
            if (Time.time < nirishSpecialAt) return;
            nirishSpecialAt = Time.time + Random.Range(15, 25);
            PauseBoss(nirish, 2f);
            if (dist < 25f && (Random.Range(0, 101) > 70 || PlayerHealthShare < 0.2f || Random.Range(0, 101) > 70))
                WebAndPull(nirish, at);
            if (dist < 10f && HealthShare(nirish) < 0.6f) SpawnPoisonClouds(nirish, at);
        }

        /// <summary>A web (1 s stun), and a second later the pull to Nirish (2 s stun).</summary>
        private void WebAndPull(NpcEntity nirish, Vector3 at)
        {
            nirishPulling = true;
            Plugin.Log.LogInfo("[boss] Nirish webs the player");
            var player = LocalPlayer;
            Send(21, 0, 44, at, player.transform.position);
            Stun(1f);
            Later(1f, () =>
            {
                nirishPulling = false;
                var p = LocalPlayer;
                if (p == null || PlayerDead || nirish.dead || world == null || world.sceneId != SceneSewers) return;
                Vector3 to = NpcPosition(nirish);
                if (Vector3.Distance(to, p.transform.position) >= 30f) return;
                Send(32, 1, 50f, 0.5f, to);   // the client drags the player there
                Plugin.Log.LogInfo("[boss] Nirish pulls the player in");
                Stun(2f);
            });
        }

        private void SpawnPoisonClouds(NpcEntity nirish, Vector3 at)
        {
            PlaySound(410, at, 50);
            bool wide = Random.Range(0, 101) > 50;
            SetAnim(nirish, wide ? 14 : 15, true);
            int radius = wide ? 30 : 10, chance = wide ? 98 : 92, before = poisonClouds.Count;
            for (int i = -radius; i < radius; i++)
                for (int j = -radius; j < radius; j++)
                {
                    if (i * i + j * j >= radius * radius || Random.Range(0, 101) <= chance) continue;
                    var pos = new Vector3(at.x + i, 57f, at.z + j);
                    if (wide && Vector3.Distance(at, pos) <= 5f) continue;
                    PlayEffect(116, pos);
                    poisonClouds.Add(new PoisonCloud { pos = pos, from = Time.time + 3f, until = Time.time + 11f });
                }
            Plugin.Log.LogInfo($"[boss] Nirish scatters {poisonClouds.Count - before} poison clouds ({(wide ? "wide" : "close")})");
        }

        private void TickPoisonClouds(Vector3 me)
        {
            if (Time.time < nirishCloudCheckAt) return;
            nirishCloudCheckAt = Time.time + 2f;
            if (PlayerDead) return;
            foreach (var c in poisonClouds)
            {
                if (Time.time < c.from || Vector3.Distance(c.pos, me) >= 3f) continue;
                if (!HasBuff(BuffVenom)) Notice("You have been poisoned!", "red");
                AddBuff(-BuffVenom, BuffVenom, 20f, new BuffEffect { healPercentPerSecond = -0.2f, poisonDamage = true });
                return;
            }
        }

        /// <summary>Below 35% a spiderling crawls to Nirish and heals him by a third; above it, it's gone.</summary>
        private void TickSpiderling(NpcEntity nirish, Vector3 at)
        {
            var healer = world.npcs.Find(n => n.typeId == Spiderling && n.master == nirish && !n.dead);
            if (HealthShare(nirish) >= 0.35f)
            {
                if (healer != null)
                {
                    world.npcs.Remove(healer);
                    visibilityTimer = 0f;
                }
                return;
            }
            if (healer == null)
            {
                if (!world.npcs.Exists(n => n.typeId == Spiderling && n.master == nirish))
                {
                    SpawnBossMinion(nirish, Spiderling, SpiderlingStarts[Random.Range(0, SpiderlingStarts.Length)], false);
                    Plugin.Log.LogInfo("[boss] a spiderling sets off to heal Nirish");
                }
                return;
            }
            if (Vector3.Distance(NpcPosition(healer), at) < 2f)
            {
                HealNpc(nirish, Mathf.RoundToInt(nirish.maxHealth * 0.33f));
                PlayEffect(108, healer.pos);
                PlaySound(389, at, 40);
                world.npcs.Remove(healer);
                visibilityTimer = 0f;
                Plugin.Log.LogInfo("[boss] the spiderling reached Nirish and healed him");
                return;
            }
            MoveNpc(healer, at);
            GameData.Npcs.TryGetValue(Spiderling, out var info);
            UpdateAnim(healer, info);
        }

        // ---- George, the Storage Guard -------------------------------------------------------------

        private float georgeThrowAt;

        private void TickGeorge(Vector3 me)
        {
            var george = Boss(George);
            if (george == null || george.dead || PlayerDead || Vector3.Distance(NpcPosition(george), me) > 50f || Time.time < georgeThrowAt) return;
            georgeThrowAt = Time.time + 6f;
            int sticks = (int)((1f - HealthShare(george)) * 30f);
            if (sticks <= 0) return;
            Vector3 at = NpcPosition(george);
            Plugin.Log.LogInfo($"[boss] George throws {sticks} sticks of dynamite");
            PauseBoss(george, 2f);
            SetAnim(george, 33, true);
            Later(2f, () => { if (!george.dead) SetAnim(george, NpcAnims.IdleOf(George)); });
            for (int i = 0; i < sticks; i++)
            {
                var spot = new Vector3(at.x + Random.Range(-30, 30), at.y, at.z + Random.Range(-30, 30));
                Send(21, 0, 50, at + Vector3.up * 1.5f, spot);
                Send(198, 1, spot, 4f, 2f, (byte)0);
                Later(FlightTime(50, at, spot), () =>
                {
                    if (world == null || world.sceneId != ScenePoacher) return;
                    PlayEffect(146, spot);
                    if (!PlayerDead && PlayerWithin(spot, 4f)) HurtByOriginal(Random.Range(8000, 11001), DamageKinds.Fire);
                });
            }
        }

        // ---- Sytrax ---------------------------------------------------------------------------

        private int sytraxPhase;   // 0/1 on the ground, 2 taking off, 3 hovering, 4 spitting fire, 5 landing
        private float sytraxPhaseUntil, sytraxFireAt;
        private Vector3 sytraxGround;

        private void TickSytrax(Vector3 me)
        {
            var dragon = Boss(Sytrax);
            if (dragon == null) return;
            if (dragon.dead)
            {
                if (sytraxPhase > 1 && dragon.flying)
                {
                    dragon.flying = false;
                    dragon.pos = sytraxGround;
                    Send(9, 1, dragon.uid, sytraxGround);
                }
                sytraxPhase = 0;
                return;
            }
            bool near = !PlayerDead && Vector3.Distance(NpcPosition(dragon), me) < 60f;
            if (!near && sytraxPhase <= 1) return;   // he only takes off with someone to burn

            if (sytraxPhase == 2) Lift(dragon, 1.5f);
            else if (sytraxPhase == 5) Lift(dragon, -1.65f);
            else if (sytraxPhase == 4 && Time.time >= sytraxFireAt)
            {
                sytraxFireAt = Time.time + 0.5f;
                if (near)
                {
                    var spot = me + new Vector3(Random.Range(-10, 10), 0f, Random.Range(-10, 10));
                    Vector3 from = dragon.pos + Vector3.up * 1.5f;
                    Send(21, 0, 52, from, spot);
                    Send(198, 1, spot, 5f, 2f, (byte)0);
                    Later(FlightTime(52, from, spot), () =>
                    {
                        if (world == null || world.sceneId != ScenePoacher) return;
                        PlaySound(434, spot, 50);
                        PlayEffect(156, spot);
                        if (!PlayerDead && PlayerWithin(spot, 5f)) HurtByOriginal(Random.Range(15000, 22501), DamageKinds.Fire);
                    });
                }
            }
            if (Time.time < sytraxPhaseUntil) return;
            sytraxPhase = sytraxPhase >= 5 ? 0 : sytraxPhase + 1;
            Plugin.Log.LogInfo($"[boss] Sytrax: {new[] { "down", "on the ground", "taking off", "hovering", "spitting fire", "landing" }[sytraxPhase]}");
            switch (sytraxPhase)
            {
                case 0:
                    dragon.flying = false;   // down again: back to fighting on the ground
                    dragon.pos = sytraxGround;
                    Send(9, 1, dragon.uid, sytraxGround);
                    sytraxPhaseUntil = Time.time;
                    break;
                case 1:
                    sytraxPhaseUntil = Time.time + 15f;
                    SetAnim(dragon, NpcAnims.IdleOf(Sytrax));
                    break;
                case 2:
                    sytraxPhaseUntil = Time.time + 2f;
                    sytraxGround = NpcPosition(dragon);
                    PauseBoss(dragon, 0f);
                    dragon.aggro = false;
                    dragon.flying = true;
                    SetAnim(dragon, 22, true);
                    PlaySound(431, dragon.pos, 100);
                    break;
                case 3:
                    sytraxPhaseUntil = Time.time + 2f;
                    SetAnim(dragon, 18, true);
                    break;
                case 4:
                    sytraxPhaseUntil = Time.time + 5f;
                    PlaySound(432, dragon.pos, 100);
                    SetAnim(dragon, 19, true);
                    break;
                case 5:
                    sytraxPhaseUntil = Time.time + 2f;
                    SetAnim(dragon, 20, true);
                    break;
            }
        }

        private void Lift(NpcEntity dragon, float step)
        {
            dragon.pos += Vector3.up * step;
            Send(9, 1, dragon.uid, dragon.pos);
        }

        // ---- Sepitus, the Dark Tamer -------------------------------------------------------------

        private int sepitusPhase;
        private float sepitusPhaseUntil, sepitusHoundsAt;

        private void TickSepitus(Vector3 me)
        {
            var tamer = Boss(Sepitus);
            if (tamer == null || tamer.dead)
            {
                sepitusPhase = 0;
                return;
            }
            Vector3 at = NpcPosition(tamer);
            bool near = !PlayerDead && Vector3.Distance(at, me) < 50f;
            if (!near) return;

            if (Time.time >= sepitusHoundsAt && HealthShare(tamer) < 0.5f)
            {
                sepitusHoundsAt = Time.time + 30f;
                Plugin.Log.LogInfo("[boss] Sepitus calls three lava hounds");
                for (int i = 0; i < 3; i++)
                {
                    var pos = at + new Vector3(Random.Range(-5, 5), 0f, Random.Range(-5, 5));
                    var hound = SpawnBossMinion(tamer, LavaHound, pos, true);
                    PlayEffect(155, pos);
                    Later(180f, () => { if (world != null && world.npcs.Remove(hound)) visibilityTimer = 0f; });
                }
            }
            if (Time.time < sepitusPhaseUntil) return;
            sepitusPhase = sepitusPhase >= 4 ? 1 : sepitusPhase + 1;
            switch (sepitusPhase)
            {
                case 1:
                case 3:
                {
                    bool plague = sepitusPhase == 3;
                    sepitusPhaseUntil = Time.time + 3f;
                    PauseBoss(tamer, 3f);
                    SetAnim(tamer, 32, true);
                    PlayPlayerEffect(plague ? 151 : 147);   // charging
                    Later(2f, () =>
                    {
                        var p = LocalPlayer;
                        if (tamer.dead || PlayerDead || p == null || world == null || world.sceneId != ScenePoacher) return;
                        SetAnim(tamer, NpcAnims.IdleOf(Sepitus));
                        if (Vector3.Distance(NpcPosition(tamer), p.transform.position) >= 50f) return;
                        if (!plague) Send(21, 0, 51, NpcPosition(tamer) + Vector3.up * 1.5f, p.transform.position + Vector3.up * 1.5f);
                        PlayPlayerEffect(plague ? 152 : 148);
                        PlaySound(429, p.transform.position, 70);
                        float res = CombatRules.PlayerResistance(GearStat(DamageKinds.ResistanceStat(DamageKinds.Psychic)));
                        HurtPlayer(Mathf.RoundToInt(MaxHealth() * 0.3f * (1f - res)), SplashPlain);
                        Plugin.Log.LogInfo($"[boss] Sepitus's {(plague ? "plague " : "")}bolt hits the player");
                        if (plague) AddBuff(-BuffPlague, BuffPlague, 10f, new BuffEffect { plague = true });
                    });
                    break;
                }
                case 2:
                    sepitusPhaseUntil = Time.time + 10f;
                    break;
                case 4:
                {
                    sepitusPhaseUntil = Time.time;
                    var dragon = Boss(Sytrax);
                    if (dragon == null || dragon.dead) break;
                    float d = HealthShare(dragon), t = HealthShare(tamer);
                    NpcEntity from = d - t > 0.2f ? dragon : t - d > 0.2f ? tamer : null;
                    if (from == null) break;
                    var to = from == dragon ? tamer : dragon;
                    int amount = from.health - to.health;
                    sepitusPhaseUntil = Time.time + 5f;
                    SetAnim(tamer, 32, true);
                    Send(198, 3, 48, from.uid, to.uid);   // a healing bird
                    Plugin.Log.LogInfo($"[boss] a healing bird from {(from == dragon ? "Sytrax" : "Sepitus")} heals {amount}");
                    Later(FlightTime(48, NpcPosition(from), NpcPosition(to)), () => { if (!to.dead && amount > 0) HealNpc(to, amount); });
                    break;
                }
            }
        }
    }
}
