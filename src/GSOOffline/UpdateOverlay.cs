using System.Collections;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.Networking;

namespace GSOOffline
{
    /// <summary>
    /// A small line in the top-left corner: "&lt;mod&gt; &lt;version&gt;", followed by "- NEW UPDATE AVAILABLE" in another colour
    /// when the mod's latest GitHub release is newer. The check runs once per launch against the public releases
    /// API, through UnityWebRequest: the game's Mono HttpWebRequest can't do the TLS 1.2 GitHub requires. It only
    /// reads the version; nothing is downloaded. Failures (offline, rate limit) just mean no update notice.
    /// The same file is in GSOOffline and GSOHDTextures (only the namespace differs); keep them in sync. Each mod
    /// draws its own line, GSOOffline's on top.
    /// </summary>
    internal class UpdateOverlay : MonoBehaviour
    {
        private const string OfflineGuid = "gso.offline.server";
        private const string ReleasesApi = "https://api.github.com/repos/GranSkreaReforged/{0}/releases/latest";

        private static readonly Color VersionColor = new Color(1f, 1f, 1f, 0.6f);
        private static readonly Color UpdateColor = new Color(1f, 0.78f, 0.2f, 1f);
        private static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.6f);

        private string modName, repo, guid, label;
        private int[] current;
        private string newer;    // the newer release's version, once the check finds one
        private int line = -1;   // which line of the corner this mod uses
        private GUIStyle style;
        private static UpdateOverlay instance;

        internal static void Create(GameObject host, string modName, string version, bool releaseBuild, string repo,
            string guid, bool checkForUpdates)
        {
            var o = instance = host.AddComponent<UpdateOverlay>();
            o.modName = modName;
            o.repo = repo;
            o.guid = guid;
            o.current = ParseVersion(version);
            o.label = modName + " " + version + (releaseBuild ? "" : " (dev)");
            if (checkForUpdates) o.StartCoroutine(o.Check());
        }

        // Bridge testing: compare against another installed version (DevCommands `updatecheck`).
        internal static void Recheck(string version)
        {
            if (instance == null) return;
            instance.current = ParseVersion(version);
            instance.newer = null;
            instance.StartCoroutine(instance.Check());
        }

        private IEnumerator Check()
        {
            using (var req = UnityWebRequest.Get(string.Format(ReleasesApi, repo)))
            {
                req.SetRequestHeader("User-Agent", repo);   // the API refuses requests without one
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.isNetworkError || req.isHttpError)
                {
                    LogInfo($"Update check skipped: {(req.isNetworkError ? req.error : "HTTP " + req.responseCode)}.");
                    yield break;
                }
                var m = Regex.Match(req.downloadHandler.text, "\"tag_name\"\\s*:\\s*\"v?([0-9][0-9.]*)\"");
                if (!m.Success)
                {
                    LogInfo("Update check skipped: no version in the reply.");
                    yield break;
                }
                var latest = ParseVersion(m.Groups[1].Value);
                if (Compare(latest, current) > 0)
                {
                    newer = string.Join(".", System.Array.ConvertAll(latest, n => n.ToString()));
                    LogInfo($"{modName} {newer} is available: https://github.com/GranSkreaReforged/{repo}/releases/latest");
                }
                else LogInfo($"{modName} is up to date (latest release {m.Groups[1].Value}).");
            }
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            var mc = Menucontroller.instance;
            if (mc != null && !mc.showGUI) return;   // the game's hide-interface mode (screenshots)
            if (line < 0) line = guid != OfflineGuid && Chainloader.PluginInfos.ContainsKey(OfflineGuid) ? 1 : 0;

            float scale = Mathf.Max(1f, Screen.height / 1080f);
            if (style == null) style = new GUIStyle(GUI.skin.label) { wordWrap = false, richText = false };
            style.fontSize = Mathf.RoundToInt(12f * scale);
            GUI.matrix = Matrix4x4.identity;
            GUI.depth = -1000;

            float x = 6f * scale, y = (4f + line * 16f) * scale;
            x = Draw(label, x, y, VersionColor);
            if (newer != null) Draw(" - NEW UPDATE AVAILABLE (" + newer + ")", x, y, UpdateColor);
        }

        // Draws text with a 1-pixel shadow and returns where the next text starts.
        private float Draw(string text, float x, float y, Color color)
        {
            var content = new GUIContent(text);
            var size = style.CalcSize(content);
            style.normal.textColor = ShadowColor;
            GUI.Label(new Rect(x + 1f, y + 1f, size.x, size.y), content, style);
            style.normal.textColor = color;
            GUI.Label(new Rect(x, y, size.x, size.y), content, style);
            return x + size.x;
        }

        // "1.3" and "1.3.0" are the same version; anything after the numbers (-dev+...) is ignored.
        private static int[] ParseVersion(string v)
        {
            var parts = new int[3];
            var m = Regex.Match(v ?? "", "^v?(\\d+)(?:\\.(\\d+))?(?:\\.(\\d+))?");
            for (int i = 0; i < 3; i++)
                if (m.Success && m.Groups[i + 1].Success) parts[i] = int.Parse(m.Groups[i + 1].Value);
            return parts;
        }

        private static int Compare(int[] a, int[] b)
        {
            for (int i = 0; i < 3; i++)
                if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return 0;
        }

        private static void LogInfo(string text) => Plugin.Log.LogInfo(text);
    }
}
