using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Bal Sardan oyster fishing. Oysters dart about the harbour basin and soon vanish; harpoon guns on the quay stun
    /// them (they stop and stay longer), and a fishing vessel's crane lowers a net to catch them (Fishing 30), as the
    /// community wiki and the shipwright's dialogue describe. All of it was server-side and lost: the oysters, guns
    /// and harpoons are the client's spawned objects (2/35, moved with 14/1, removed with 25/10), harpoon mode is
    /// 24/3, shots arrive as 28/0 and crane drops as 28/1. Where the bed and guns are, how many oysters, how long they
    /// last and how close the net must be are reconstructions, in the constants below.
    /// </summary>
    public partial class OfflineServer
    {
        private const int OysterScene = 7;
        private const int ObjOyster = 10, ObjHarpoon = 11, ObjHarpoonGun = 12;   // ab_spawnedObject_<n>
        private const int InteractHarpoonGun = 46;
        private const int OysterItem = 10291, OysterFishingLevel = 30;

        // The basin between the quay and the moored ships south of the shipwright (open water x 973-1009, z 424-500).
        private static readonly Vector3 OysterBedMin = new Vector3(978f, 3.1f, 430f), OysterBedMax = new Vector3(1004f, 3.1f, 470f);
        private const float OysterActiveRange = 120f;   // the bed runs while the player is this close to it

        // Guns on the quay edge (top y 5.7, x up to 970, z 422-457), either side of the shipwright, facing the basin.
        private static readonly Vector3[] HarpoonGuns = { new Vector3(969f, 5.7f, 436f), new Vector3(969f, 5.7f, 453f) };
        private const int HarpoonGunRot = 90;
        private const float HarpoonGunReach = 4f;      // stand this close to use a gun; walking further away puts it down
        private const float HarpoonRange = 45f, HarpoonHitRadius = 2.5f, HarpoonCooldown = 1.2f;

        private const int MaxOysters = 3;
        private const float OysterSpawnMin = 3f, OysterSpawnMax = 7f;   // seconds between oysters
        private const float OysterLifetime = 10f, OysterStunTime = 20f, OysterMoveInterval = 1.5f, OysterHop = 7f;
        private const float NetRadius = 1.5f, NetRadiusStunned = 3.5f, NetCooldown = 3f, NetHaulTime = 1.5f;

        private const int SfxHarpoonShot = 34, SfxNet = 274, GfxSplashSmall = 112, GfxSplashLarge = 115;
        private const int OysterUidBase = 7000000;

        private class Oyster
        {
            public int uid;
            public Vector3 pos;
            public float expires, nextMove, stunnedUntil;
        }

        private readonly List<Oyster> oysters = new List<Oyster>();
        private bool oysterBedShown;         // guns sent to the client for this visit
        private float nextOysterAt, nextHarpoonAt, nextNetAt;
        private int nextOysterUid = OysterUidBase;
        private int harpoonGun = -1;         // gun the player is manning, or -1
        private Oyster netHaul;              // caught, coming up in the net
        private float netHaulAt;

        private void RegisterOysterHandlers()
        {
            On(28, 0, c => ShootHarpoon((Vector3)c[1]));   // ShootHarpoonInBalSardanOysterFishing(point aimed at)
            On(28, 1, c => DropNet((Vector3)c[1]));        // CatchOysterInBalSardanOysterFishing(crane position)
        }

        private static Vector3 OysterBedCentre => (OysterBedMin + OysterBedMax) * 0.5f;

        private static Vector3 RandomBedSpot() => new Vector3(
            Random.Range(OysterBedMin.x, OysterBedMax.x), OysterBedMin.y, Random.Range(OysterBedMin.z, OysterBedMax.z));

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        /// <summary>The zone was left or reloaded: the client's objects went with it.</summary>
        private void ResetOysters()
        {
            oysters.Clear();
            oysterBedShown = false;
            harpoonGun = -1;
            netHaul = null;
        }

        private void TickOysters()
        {
            var player = LocalPlayer;
            if (character == null || !sceneReady || player == null || character.scene != OysterScene) return;
            bool near = Flat(player.transform.position, OysterBedCentre) < OysterActiveRange;
            if (!near)
            {
                if (oysterBedShown) HideOysterBed();
                return;
            }
            if (!oysterBedShown) ShowOysterBed();

            if (harpoonGun >= 0 && Flat(player.transform.position, HarpoonGuns[harpoonGun]) > HarpoonGunReach)
                PutDownHarpoon();

            float now = Time.time;
            if (netHaul != null && now >= netHaulAt) LandNetHaul();

            for (int i = oysters.Count - 1; i >= 0; i--)
            {
                var o = oysters[i];
                if (now >= o.expires)
                {
                    RemoveOyster(o);
                    continue;
                }
                if (now < o.stunnedUntil || now < o.nextMove) continue;
                o.nextMove = now + OysterMoveInterval * Random.Range(0.7f, 1.3f);
                var hop = o.pos + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(OysterHop * 0.5f, OysterHop);
                o.pos = new Vector3(Mathf.Clamp(hop.x, OysterBedMin.x, OysterBedMax.x), OysterBedMin.y, Mathf.Clamp(hop.z, OysterBedMin.z, OysterBedMax.z));
                Send(14, 1, o.uid, Random.Range(0, 360), o.pos);
            }

            if (oysters.Count < MaxOysters && now >= nextOysterAt)
            {
                nextOysterAt = now + Random.Range(OysterSpawnMin, OysterSpawnMax);
                var o = new Oyster { uid = nextOysterUid++, pos = RandomBedSpot(), expires = now + OysterLifetime, nextMove = now + OysterMoveInterval };
                oysters.Add(o);
                Send(2, 35, character.name, o.uid + "_" + ObjOyster + "_" + SceneWorld.Vec(o.pos) + "_" + Random.Range(0, 360));
                PlayEffect(GfxSplashSmall, o.pos);
            }
        }

        // 2/35 only adds objects (sending one twice shows it twice), so each goes out once per visit.
        private void ShowOysterBed()
        {
            oysterBedShown = true;
            nextOysterAt = Time.time + 1f;
            var parts = new List<string>();
            for (int i = 0; i < HarpoonGuns.Length; i++)
                parts.Add((OysterUidBase - 1 - i) + "_" + ObjHarpoonGun + "_" + SceneWorld.Vec(HarpoonGuns[i]) + "_" + HarpoonGunRot);
            Send(2, 35, character.name, string.Join(">", parts.ToArray()));
            Plugin.Log.LogInfo("[oyster] harbour bed active");
        }

        private void HideOysterBed()
        {
            PutDownHarpoon();
            foreach (var o in oysters) Send(25, 10, o.uid);
            oysters.Clear();
            for (int i = 0; i < HarpoonGuns.Length; i++) Send(25, 10, OysterUidBase - 1 - i);
            oysterBedShown = false;
            netHaul = null;
        }

        private void RemoveOyster(Oyster o)
        {
            oysters.Remove(o);
            Send(25, 10, o.uid);
        }

        // Dev: drop the net on the first oyster (offset metres east of it), stunning it first if asked.
        internal void DevNetOyster(bool stun, float offset)
        {
            if (oysters.Count == 0) { Plugin.Log.LogInfo("[dev] no oysters"); return; }
            var o = oysters[0];
            if (stun) o.stunnedUntil = o.expires = Time.time + OysterStunTime;
            nextNetAt = 0f;
            DropNet(o.pos + Vector3.right * offset);
        }

        internal void LogOysters()
        {
            foreach (var o in oysters)
                Plugin.Log.LogInfo($"[dev] oyster {o.uid} at {SceneWorld.Vec(o.pos)} {(Time.time < o.stunnedUntil ? "stunned" : "moving")}, {o.expires - Time.time:F0} s left");
            int client = 0;
            if (Scr_SpawnedObjectHandler.instance != null)
                foreach (var so in Scr_SpawnedObjectHandler.instance.objectList) if (so != null) client++;
            Plugin.Log.LogInfo($"[dev] oysters: {oysters.Count}, bed {(oysterBedShown ? "active" : "inactive")}, harpoon gun {harpoonGun}, client objects {client}");
        }

        /// <summary>3/11 on a harpoon gun: man the nearest one (or put it down if already manning it).</summary>
        private bool HandleOysterInteract(int typeId)
        {
            if (typeId != InteractHarpoonGun || character == null || character.scene != OysterScene) return false;
            var player = LocalPlayer;
            if (player == null) return true;
            if (harpoonGun >= 0)
            {
                PutDownHarpoon();
                return true;
            }
            int best = -1;
            float bestD = HarpoonGunReach + 2f;   // the client already checked its own interact range
            for (int i = 0; i < HarpoonGuns.Length; i++)
            {
                float d = Flat(player.transform.position, HarpoonGuns[i]);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0)
            {
                Plugin.Log.LogInfo($"[oyster] harpoon refused: no gun near {player.transform.position}");
                return true;
            }
            harpoonGun = best;
            Send(24, 3, true);
            Notice("Aim with the mouse and left-click to fire. Walk away to stop.", "gray");
            Plugin.Log.LogInfo($"[oyster] manning harpoon gun {best}");
            return true;
        }

        private void PutDownHarpoon()
        {
            if (harpoonGun < 0) return;
            harpoonGun = -1;
            Send(24, 3, false);
            Plugin.Log.LogInfo("[oyster] harpoon gun put down");
        }

        private void ShootHarpoon(Vector3 aim)
        {
            if (harpoonGun < 0 || PlayerDead || Time.time < nextHarpoonAt) return;
            nextHarpoonAt = Time.time + HarpoonCooldown;
            var gun = HarpoonGuns[harpoonGun] + Vector3.up * 1.2f;
            if (Flat(gun, aim) > HarpoonRange) aim = gun + (aim - gun).normalized * HarpoonRange;
            PlaySound(SfxHarpoonShot, gun);

            // The harpoon flies from the gun to the point aimed at, then is gone.
            int uid = nextOysterUid++;
            int rot = (int)Quaternion.LookRotation(new Vector3(aim.x - gun.x, 0f, aim.z - gun.z)).eulerAngles.y;
            Send(2, 35, character.name, uid + "_" + ObjHarpoon + "_" + SceneWorld.Vec(gun) + "_" + rot);
            Send(14, 1, uid, rot, aim);
            pendingHarpoons.Add(new KeyValuePair<int, float>(uid, Time.time + 0.8f));

            Oyster hit = null;
            float bestD = HarpoonHitRadius;
            foreach (var o in oysters)
            {
                float d = Flat(o.pos, aim);
                if (d < bestD) { bestD = d; hit = o; }
            }
            PlayEffect(GfxSplashSmall, hit != null ? hit.pos : new Vector3(aim.x, 3.15f, aim.z));
            if (hit == null)
            {
                Plugin.Log.LogInfo($"[oyster] harpoon missed at {SceneWorld.Vec(aim)}");
                return;
            }
            hit.stunnedUntil = Time.time + OysterStunTime;
            hit.expires = Mathf.Max(hit.expires, hit.stunnedUntil);
            Notice("You hit an oyster! It's stunned.", "green");
            Plugin.Log.LogInfo($"[oyster] harpoon hit oyster {hit.uid} at {SceneWorld.Vec(hit.pos)} ({bestD:F1} m from the aim)");
        }

        private readonly List<KeyValuePair<int, float>> pendingHarpoons = new List<KeyValuePair<int, float>>();

        private void TickHarpoons()
        {
            for (int i = pendingHarpoons.Count - 1; i >= 0; i--)
            {
                if (Time.time < pendingHarpoons[i].Value) continue;
                Send(25, 10, pendingHarpoons[i].Key);
                pendingHarpoons.RemoveAt(i);
            }
        }

        /// <summary>28/1: the fishing vessel's crane lowered its net at this point.</summary>
        private void DropNet(Vector3 net)
        {
            if (character == null || character.scene != OysterScene || !BoatOut || !aboard || PlayerDead) return;
            if (!Boats.TryGetValue(character.boat.typeId, out var type) || type.prefab != 4) return;
            if (Time.time < nextNetAt || netHaul != null) return;
            if (SkillLevel(FishingSkill) < OysterFishingLevel)
            {
                Notice($"You need level {OysterFishingLevel} Fishing to catch oysters.", "red");
                return;
            }
            nextNetAt = Time.time + NetCooldown;
            Send(7, 30, character.name);   // crane animation
            PlaySound(SfxNet, net);
            PlayEffect(GfxSplashLarge, new Vector3(net.x, 3.15f, net.z));

            Oyster caught = null;
            float bestD = float.MaxValue;
            foreach (var o in oysters)
            {
                float d = Flat(o.pos, net);
                float reach = Time.time < o.stunnedUntil ? NetRadiusStunned : NetRadius;
                if (d < reach && d < bestD) { bestD = d; caught = o; }
            }
            if (caught == null)
            {
                Plugin.Log.LogInfo($"[oyster] net empty at {SceneWorld.Vec(net)}");
                Notice("The net comes up empty.", "gray");
                return;
            }
            // Held in the net while it comes up.
            caught.stunnedUntil = caught.expires = float.MaxValue;
            netHaul = caught;
            netHaulAt = Time.time + NetHaulTime;
            Plugin.Log.LogInfo($"[oyster] netted oyster {caught.uid} {bestD:F1} m from the net");
        }

        private void LandNetHaul()
        {
            var o = netHaul;
            netHaul = null;
            RemoveOyster(o);
            GiveItem(OysterItem);
            AddXp(FishingSkill, SkillData.BaseXp(OysterFishingLevel));
            Notice("You caught an oyster!", "green");
        }
    }
}
