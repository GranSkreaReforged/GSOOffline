using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Developer automation: when Debug.DevCommandFile is set, lines written to that file are executed
    /// in-game and results go to the BepInEx log with a "[dev]" prefix. Lets features be exercised
    /// end-to-end through the real client without driving its UI.
    ///
    ///   client &lt;Scr_RPCSender method&gt; [args...]   call as if the UI had (ints/floats/bools/strings auto-parsed; "$me" = player name)
    ///   creationdone                               press "Done" in the character creator
    ///   inv                                        dump the client's inventory and equipment
    ///   npcs [n]                                   nearest n client-side NPCs (uid, type, name, distance)
    ///   harvestables [n]                           nearest n harvestables
    ///   shot &lt;name&gt;                                 screenshot to OfflineSaves/dev/&lt;name&gt;.png
    ///   goto x y z                                 move the local player (client side only)
    ///   near &lt;uid&gt;                                 step next to a visible NPC or harvestable
    ///   checkrecipes                               compare server recipe ids with the client's
    /// </summary>
    internal class DevBridge : MonoBehaviour
    {
        private string path;
        private float poll;

        private void Start()
        {
            path = Plugin.DevCommandFile.Value;
            if (!Path.IsPathRooted(path)) path = Path.Combine(BepInEx.Paths.GameRootPath, path);
            Plugin.Log.LogInfo("[dev] watching " + path);
        }

        private void Update()
        {
            poll -= Time.unscaledDeltaTime;
            if (poll > 0f) return;
            poll = 0.5f;
            if (!File.Exists(path)) return;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
                File.Delete(path);
            }
            catch (IOException)
            {
                return;   // writer still has it open; retry next poll
            }
            foreach (string line in lines)
            {
                if (line.Trim().Length == 0) continue;
                Plugin.Log.LogInfo("[dev] > " + line);
                try
                {
                    Run(line.Trim().Split(' '));
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("[dev] " + e);
                }
            }
        }

        private static Scr_Player Player => Scr_PlayerHandler.instance != null ? Scr_PlayerHandler.instance.player : null;

        private void Run(string[] a)
        {
            switch (a[0])
            {
                case "client": CallSender(a); break;
                case "inv": DumpInventory(); break;
                case "creationdone":
                    // What the creator's "Done" button does, minus the GUI.
                    Scr_CharacterCreation.instance.active = false;
                    Scr_RPCSender.instance.doneWithCharacterCreation();
                    break;
                case "npcs": DumpNpcs(a.Length > 1 ? int.Parse(a[1]) : 10); break;
                case "harvestables": DumpHarvestables(a.Length > 1 ? int.Parse(a[1]) : 10); break;
                case "checkrecipes": CheckRecipes(); break;
                case "near": GoNear(int.Parse(a[1])); break;
                case "shot": Screenshot(a.Length > 1 ? a[1] : "shot"); break;
                case "goto":
                    Player.transform.position = new Vector3(F(a[1]), F(a[2]), F(a[3]));
                    break;
                default: Plugin.Log.LogWarning("[dev] unknown command " + a[0]); break;
            }
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        private static object ParseArg(string s, Type t)
        {
            if (s == "$me") s = Player != null ? Player.playerName : string.Empty;
            if (t == typeof(int)) return int.Parse(s, CultureInfo.InvariantCulture);
            if (t == typeof(float)) return F(s);
            if (t == typeof(bool)) return bool.Parse(s);
            if (t == typeof(string)) return s.Replace('_', ' ');
            if (t == typeof(Vector3))
            {
                var p = s.Split(',');
                return new Vector3(F(p[0]), F(p[1]), F(p[2]));
            }
            throw new ArgumentException("Unsupported parameter type " + t);
        }

        private static void CallSender(string[] a)
        {
            int argc = a.Length - 2;
            var method = typeof(Scr_RPCSender).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == a[1] && m.GetParameters().Length == argc);
            if (method == null)
            {
                Plugin.Log.LogWarning($"[dev] no Scr_RPCSender.{a[1]} with {argc} args");
                return;
            }
            var ps = method.GetParameters();
            var args = new object[argc];
            for (int i = 0; i < argc; i++) args[i] = ParseArg(a[i + 2], ps[i].ParameterType);
            method.Invoke(Scr_RPCSender.instance, args);
        }

        private static void DumpInventory()
        {
            var sb = new StringBuilder("[dev] inventory:");
            foreach (var s in Inventory.instance.items)
                sb.Append($"\n  id={s.item.itemID} type={s.item.itemTypeID} '{s.item.itemName}' x{s.amount} slot={s.item.itemSlot} equipped={s.item.currentlyEquipped} hp+{s.item.itemStatHealth}");
            foreach (var e in Inventory.instance.equipSlots)
                if (e.item != null) sb.Append($"\n  [{e.slot}] {e.item.itemName}");
            var p = Player;
            if (p != null) sb.Append($"\n  silver={p.silver} hp={p.currentHealth}/{p.maximumHealth}");
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static void DumpNpcs(int n)
        {
            var p = Player;
            if (p == null) return;
            var sb = new StringBuilder("[dev] npcs:");
            foreach (var npc in Scr_NpcHandler.instance.npcList.Where(x => x != null)
                         .OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).Take(n))
                sb.Append($"\n  uid={npc.npcUniqueId} type={npc.npcTypeId} '{npc.npcName}' d={Vector3.Distance(npc.transform.position, p.transform.position):F1} hp={npc.currentHealth}/{npc.maxHealth} interact={npc.canInteract}");
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static void DumpHarvestables(int n)
        {
            var p = Player;
            if (p == null) return;
            var sb = new StringBuilder("[dev] harvestables:");
            foreach (var h in Scr_HarvestableHandler.instance.harvestablesList.Where(x => x != null)
                         .OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).Take(n))
                sb.Append($"\n  uid={h.uniqueId} type={h.typeId} '{h.harvestableName}' d={Vector3.Distance(h.transform.position, p.transform.position):F1} hp={h.currentHealth}");
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static void GoNear(int uid)
        {
            Transform t = null;
            var h = Scr_HarvestableHandler.instance.harvestablesList.FirstOrDefault(x => x != null && x.uniqueId == uid);
            if (h != null) t = h.transform;
            var n = Scr_NpcHandler.instance.npcList.FirstOrDefault(x => x != null && x.npcUniqueId == uid);
            if (n != null) t = n.transform;
            if (t == null)
            {
                Plugin.Log.LogWarning("[dev] nothing visible with uid " + uid);
                return;
            }
            var dir = (Player.transform.position - t.position);
            dir.y = 0f;
            Player.transform.position = t.position + (dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward) * 1.5f + Vector3.up * 0.5f;
        }

        // Server recipe ids must match the client's Script_Crafting numbering or crafts produce the wrong item.
        private static void CheckRecipes()
        {
            int ok = 0, bad = 0;
            foreach (var c in Script_Crafting.instance.craftingRecipes)
            {
                if (SkillData.Recipes.TryGetValue(c.recipeId, out var r) && r.product == c.product1 && r.skill == c.skill && r.level == c.level)
                    ok++;
                else if (bad++ < 5)
                    Plugin.Log.LogWarning($"[dev] recipe {c.recipeId} mismatch: client product {c.product1}, server {(r != null ? r.product.ToString() : "missing")}");
            }
            Plugin.Log.LogInfo($"[dev] recipes: {ok} match, {bad} mismatch, client {Script_Crafting.instance.craftingRecipes.Count}, server {SkillData.Recipes.Count}");
        }

        private static void Screenshot(string name)
        {
            string dir = Path.Combine(Plugin.SaveDir, "dev");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, name + ".png");
            ScreenCapture.CaptureScreenshot(file);
            Plugin.Log.LogInfo("[dev] screenshot -> " + file);
        }
    }
}
