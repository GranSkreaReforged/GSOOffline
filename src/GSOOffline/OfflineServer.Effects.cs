using System;
using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Particles, projectiles and sounds. The client plays only what the server tells it to: effects by id
    /// (the game's "_GFX IDs" list), sounds by id, and projectiles from XMLs/Projectiles, which it flies itself
    /// but ends silently. The data says which sounds and effects belong to NPCs, harvestables, abilities and
    /// projectiles; which projectile each weapon and ability fired was server code, rebuilt in the tables below.
    /// </summary>
    public partial class OfflineServer
    {
        // Effect ids from the game's "_GFX IDs" list.
        private const int GfxLevelUp = 1, GfxTeleport = 40, GfxTeleportLand = 41;

        // Ability -> projectile, matched by name against XMLs/Projectiles (the old server's own mapping is lost).
        private static readonly Dictionary<int, int> AbilityProjectile = new Dictionary<int, int>
        {
            { 1, 1 },     // Fire ball
            { 48, 9 },    // Sword throw
            { 62, 11 }, { 63, 12 }, { 64, 13 }, { 65, 10 }, { 66, 14 }, { 67, 15 },   // flame/ice/electric/explosive/poison/weakness arrow
            { 77, 16 }, { 164, 16 },   // pistol shots
            { 90, 19 },   // Hunting net
            { 91, 20 },   // Piercing shot
            { 116, 27 },  // Flame lance
            { 144, 32 },  // Vampiric arrow
            { 148, 39 },  // Torch throw
            { 150, 41 },  // Fire arrows
            { 152, 43 },  // Aggressive snowball
            { 154, 46 },  // Venom arrow
            { 155, 47 },  // Thorny vines
            { 165, 56 },  // Wounding shot
        };

        // Ability -> effect on the target, from the names in "_GFX IDs" for abilities whose data has no hitgfx.
        private static readonly Dictionary<int, int> AbilityTargetGfx = new Dictionary<int, int>
        {
            { 86, 18 },   // Brutal slash
            { 89, 21 }, { 105, 21 },   // stuns: overhead stun effect
            { 88, 20 }, { 93, 20 },    // cripples: slow
            { 58, 13 },   // Rockslide
            { 57, 12 },   // Stranglevine: earth staff root
            { 112, 49 },  // Combustion
            { 114, 50 },  // Trail blaze
            { 117, 53 },  // Immolate
            { 121, 55 },  // Firestorm pulse
            { 123, 61 },  // Flashfreeze
            { 128, 64 },  // Glacial spike
            { 129, 65 },  // Shatter
            { 131, 67 },  // Frostburn
            { 132, 68 },  // Ice hail
            { 133, 69 },  // Frozen barrage
            { 141, 92 },  // Taunt
        };

        // Ability -> effect on the caster.
        private static readonly Dictionary<int, int> AbilityCasterGfx = new Dictionary<int, int>
        {
            { 15, 10 },   // Thick skin
            { 26, 9 },    // Heavy shield block
            { 98, 32 }, { 99, 33 },    // Healing music / aura
            { 115, 56 },  // Flame barrier
            { 116, 51 },  // Flame lance (caster)
            { 125, 59 },  // Ice barrier
            { 124, 60 },  // Waterwalk
            { 126, 58 },  // Cryostasis
            { 134, 66 },  // Inner frost
            { 140, 91 },  // Hack and slash
            { 168, 160 }, // Mana surge
            { 169, 161 }, // Embrace the pain
        };

        private class Impact
        {
            public float at;
            public Action land;
        }

        private readonly List<Impact> impacts = new List<Impact>();

        // ---- client messages -----------------------------------------------------------------

        private static int Pick(int[] ids) => ids != null && ids.Length > 0 ? ids[UnityEngine.Random.Range(0, ids.Length)] : 0;

        /// <summary>17/3: a sound at a world position (heard within <paramref name="distance"/> metres).</summary>
        private void PlaySound(int id, Vector3 pos, int distance = 50)
        {
            if (id <= 0 || character == null) return;
            Send(17, 3, character.name, id, distance, pos);
        }

        /// <summary>9/0: an effect at a world position.</summary>
        private void PlayEffect(int id, Vector3 pos)
        {
            if (id > 0) Send(9, 0, id, pos);
        }

        /// <summary>16/2: an effect that follows an NPC.</summary>
        private void PlayNpcEffect(NpcEntity npc, int id)
        {
            if (id > 0) Send(16, 2, npc.uid, id);
        }

        /// <summary>5/1: an effect that follows the player.</summary>
        private void PlayPlayerEffect(int id)
        {
            var player = LocalPlayer;
            if (id > 0 && player != null) Send(5, 1, character.name, id, player.transform.position);
        }

        // ---- projectiles ---------------------------------------------------------------------

        /// <summary>The basic attack projectile for a weapon, or 0 for melee.</summary>
        private static int WeaponProjectile(ItemSave weapon)
        {
            if (weapon == null) return 0;
            int t = weapon.typeId;
            var ih = Scr_ItemHandler.instance;
            var tmpl = ItemData.Get(t);
            var flags = tmpl?.flags;
            string name = (tmpl?.name ?? string.Empty).ToLowerInvariant();
            if (t == 274) return 23;                         // Roke's staff: skull spell
            if (ih.isCrossBow(t)) return 6;
            if (ih.isBow(t)) return 5;
            if (flags != null && flags.Contains("isIceStaff")) return 28;
            if (flags != null && flags.Contains("isFireStaff")) return 2;
            if (flags != null && flags.Contains("isWand"))
                return name.Contains("electric") ? 8 : name.Contains("sardan") ? 36 : 7;
            if (ih.isStaff(t)) return 2;
            if (ih.isInstrument(t)) return 24;               // lute
            if (name.Contains("pistol")) return 16;
            return 0;
        }

        private float FlightTime(int projectile, Vector3 from, Vector3 to)
        {
            float speed = SkillData.Projectiles.TryGetValue(projectile, out var p) ? p.speed : 30f;
            return Vector3.Distance(from, to) / (speed * 1.1f);   // Scr_Projectile moves at speed * 1.1 m/s
        }

        /// <summary>
        /// 8/3: a projectile from the player to an NPC. <paramref name="land"/> runs when it arrives, after the
        /// projectile's impact effect and sound, so the hit splash shows as it lands.
        /// </summary>
        private void ShootAtNpc(int projectile, NpcEntity npc, Action land)
        {
            ProfileEvent("shot");
            Send(8, 3, character.name, projectile, npc.uid);
            SkillData.Projectiles.TryGetValue(projectile, out var p);
            if (p != null && p.startGfx > 0) PlayPlayerEffect(p.startGfx);
            float delay = FlightTime(projectile, LocalPlayer.transform.position + Vector3.up * 2f, NpcPosition(npc));
            Later(delay, () =>
            {
                if (p != null)
                {
                    PlayNpcEffect(npc, p.endGfx);
                    PlaySound(p.endSound, NpcPosition(npc));
                }
                land();
            });
        }

        /// <summary>8/10: a projectile from an NPC to the player; <paramref name="land"/> runs when it arrives.</summary>
        private void NpcShoots(NpcEntity npc, int projectile, Action land)
        {
            var player = LocalPlayer;
            Send(8, 10, character.name, projectile, npc.uid);
            SkillData.Projectiles.TryGetValue(projectile, out var p);
            float delay = player != null ? FlightTime(projectile, NpcPosition(npc), player.transform.position) : 0.5f;
            Later(delay, () =>
            {
                var pl = LocalPlayer;
                if (p != null && pl != null)
                {
                    PlayEffect(p.endGfx, pl.transform.position + Vector3.up);
                    PlaySound(p.endSound, pl.transform.position);
                }
                land();
            });
        }

        private void Later(float seconds, Action action) => impacts.Add(new Impact { at = Time.time + seconds, land = action });

        private void TickImpacts()
        {
            if (impacts.Count == 0) return;
            if (character == null || !sceneReady)
            {
                impacts.Clear();   // scene change or logout: whatever was in flight is gone with the scene
                return;
            }
            for (int i = impacts.Count - 1; i >= 0; i--)
            {
                if (Time.time < impacts[i].at) continue;
                var land = impacts[i].land;
                impacts.RemoveAt(i);
                try { land(); }
                catch (Exception e) { Plugin.Log.LogError($"[fx] impact failed: {e}"); }
            }
        }

        // ---- NPC sounds ----------------------------------------------------------------------

        private void NpcSound(NpcEntity npc, int[] ids)
        {
            int id = Pick(ids);
            if (id > 0) PlaySound(id, NpcPosition(npc));
        }

        /// <summary>Dev: play an effect or a sound at the player (bridge commands fx and sfx).</summary>
        internal void DevEffect(int id, bool sound)
        {
            var player = LocalPlayer;
            if (player == null) return;
            if (sound) PlaySound(id, player.transform.position);
            else PlayEffect(id, player.transform.position + player.transform.forward * 3f);
            Plugin.Log.LogInfo($"[dev] {(sound ? "sound" : "effect")} {id} at {player.transform.position}");
        }

        // ---- teleports ----------------------------------------------------------------------

        private int arrivalGfx;   // effect to play where the player lands (set by wayshrine teleports)

        private void PlayArrivalGfx(Vector3 pos)
        {
            if (arrivalGfx <= 0) return;
            PlayEffect(arrivalGfx, pos);
            arrivalGfx = 0;
        }

        // ---- level up ------------------------------------------------------------------------

        /// <summary>The client's own level-up banner is never triggered; the server's effect was the cue.</summary>
        private void OnLevelUp(string skill, int level)
        {
            PlayPlayerEffect(GfxLevelUp);
            Notice($"Level up! {skill} is now level {level}.", "green");
        }
    }
}
