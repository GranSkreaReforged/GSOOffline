using System.Text;
using UnityEngine;

namespace GSOOffline
{
    public partial class OfflineServer
    {
        // ---- dev: swing profiling (bridge command swingprofile) ---------------------------------
        // Records the player's right hand speed (relative to the body) through each attack animation, in 0.05 s
        // bins, to find when the blow actually strikes. Nothing runs unless the command started it.

        private float profileUntil = -1f, profileStart = -1f;
        private int profileAttack;
        private Vector3 profileHand;
        private readonly float[] profileBins = new float[80];
        private string profileClip;
        private readonly StringBuilder profileEvents = new StringBuilder();

        internal void DevSetNpcHealth(int uid, int hp)
        {
            var npc = world?.GetNpc(uid);
            if (npc == null) return;
            npc.health = npc.maxHealth = hp;
            Send(12, 0, npc.uid, npc.health, npc.maxHealth, 0);
            Plugin.Log.LogInfo($"[dev] npc {uid} health {hp}");
        }

        internal void DevProfileSwings(float seconds)
        {
            profileUntil = Time.time + seconds;
            profileStart = -1f;
            Plugin.Log.LogInfo($"[dev] profiling swings for {seconds}s");
        }

        private void ProfileSwingStarted(int attack)
        {
            if (profileUntil < 0f) return;
            FlushSwingProfile();
            profileStart = Time.time;
            profileAttack = attack;
            profileClip = null;
            profileEvents.Length = 0;
            System.Array.Clear(profileBins, 0, profileBins.Length);
            var hand = ProfileHand(out var pl);
            if (hand != null) profileHand = pl.transform.InverseTransformPoint(hand.position);
        }

        /// <summary>Marks a moment (a hit, a shot) in the swing being profiled.</summary>
        private void ProfileEvent(string what)
        {
            if (profileUntil >= 0f && profileStart >= 0f) profileEvents.Append($" {what}@{Time.time - profileStart:F2}s");
        }

        private Transform ProfileHand(out Scr_Player pl)
        {
            pl = LocalPlayer;
            var a = pl != null && pl.anim != null ? pl.anim.animator : null;
            return a != null && a.isHuman ? a.GetBoneTransform(HumanBodyBones.RightHand) : null;
        }

        private void TickSwingProfile()
        {
            if (profileUntil < 0f) return;
            if (Time.time > profileUntil)
            {
                FlushSwingProfile();
                profileUntil = -1f;
                Plugin.Log.LogInfo("[dev] profiling done");
                return;
            }
            if (profileStart < 0f || Time.deltaTime <= 0f) return;
            var hand = ProfileHand(out var pl);
            if (hand == null) return;
            Vector3 local = pl.transform.InverseTransformPoint(hand.position);
            float speed = (local - profileHand).magnitude / Time.deltaTime;
            profileHand = local;
            int bin = (int)((Time.time - profileStart) / 0.05f);
            if (bin >= 0 && bin < profileBins.Length) profileBins[bin] = Mathf.Max(profileBins[bin], speed);
            if (profileClip == null && Time.time - profileStart > 0.3f)
            {
                var a = pl.anim.animator;
                var sb = new StringBuilder();
                foreach (int l in new[] { 2, 3, 5 })
                {
                    var clips = a.GetCurrentAnimatorClipInfo(l);
                    var st = a.GetCurrentAnimatorStateInfo(l);
                    sb.Append($" L{l} w={a.GetLayerWeight(l):F1} {(clips.Length > 0 ? clips[0].clip.name : "-")} {st.length:F2}s");
                }
                profileClip = sb.ToString();
            }
        }

        private void FlushSwingProfile()
        {
            if (profileStart < 0f) return;
            int n = Mathf.Min(profileBins.Length, (int)((Time.time - profileStart) / 0.05f));
            int peak = 0;
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                if (profileBins[i] > profileBins[peak]) peak = i;
                sb.Append(profileBins[i].ToString("F1")).Append(' ');
            }
            Plugin.Log.LogInfo($"[dev] swing attack={profileAttack} weapon={EquippedWeapon()?.typeId} lasted {Time.time - profileStart:F2}s peak at {peak * 0.05f:F2}s;{profileClip};{profileEvents}");
            Plugin.Log.LogInfo($"[dev] swing speeds (0.05s bins): {sb}");
            profileStart = -1f;
        }
    }
}
