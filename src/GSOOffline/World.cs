using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace GSOOffline
{
    public class NpcEntity
    {
        public int uid;
        public int typeId;
        public Vector3 spawnPos;
        public Vector3 pos;
        public int health;
        public int maxHealth;
        public bool dead;

        // combat AI state (server side only)
        public bool aggro, returning, attackingSent;
        public float nextSwing, nextWaypoint, respawnAt, nextWander;
    }

    public class HarvestableEntity
    {
        public int uid;
        public int typeId;
        public Vector3 pos;
        public int health = 10;
        public bool dead;
        public float respawnAt;
    }

    /// <summary>
    /// Server-side population of one scene. The developers placed Scr_NPCDummy / Scr_HarvestableDummy
    /// markers in every scene and exported them to the old server (see Scr_AITerrainGenerator);
    /// those markers still ship with the client, so we rebuild the spawn tables from them.
    /// </summary>
    internal class SceneWorld
    {
        public readonly int sceneId;
        public readonly List<NpcEntity> npcs = new List<NpcEntity>();
        public readonly List<HarvestableEntity> harvestables = new List<HarvestableEntity>();

        private SceneWorld(int sceneId)
        {
            this.sceneId = sceneId;
        }

        public static SceneWorld Build(int sceneId, string unitySceneName)
        {
            var w = new SceneWorld(sceneId);
            int i = 0;
            foreach (var d in Resources.FindObjectsOfTypeAll<Scr_NPCDummy>())
            {
                if (!InScene(d, unitySceneName)) continue;
                int hp = GameData.Npcs.TryGetValue(d.typeId, out var info) ? info.health : 30;
                Vector3 p = d.transform.position;
                w.npcs.Add(new NpcEntity { uid = sceneId * 10000 + i++, typeId = d.typeId, spawnPos = p, pos = p, health = hp, maxHealth = hp });
            }
            i = 0;
            foreach (var d in Resources.FindObjectsOfTypeAll<Scr_HarvestableDummy>())
            {
                if (!InScene(d, unitySceneName)) continue;
                w.harvestables.Add(new HarvestableEntity
                {
                    uid = 5000000 + sceneId * 10000 + i++, typeId = d.typeId, pos = d.transform.position,
                    health = w.RollHarvestableHealth(d.typeId),
                });
            }
            Plugin.Log.LogInfo($"Scene {sceneId} ({unitySceneName}): {w.npcs.Count} NPCs, {w.harvestables.Count} harvestables.");
            return w;
        }

        // FindObjectsOfTypeAll also returns prefab assets; only keep instances living in the loaded scene.
        private static bool InScene(Component c, string sceneName)
        {
            var s = c.gameObject.scene;
            return s.IsValid() && s.isLoaded && s.name == sceneName;
        }

        public int RollHarvestableHealth(int typeId) =>
            SkillData.Harvestables.TryGetValue(typeId, out var info) ? Random.Range(info.healthMin, info.healthMax + 1) : 5;

        public HarvestableEntity GetHarvestable(int uid)
        {
            foreach (var h in harvestables) if (h.uid == uid) return h;
            return null;
        }

        public NpcEntity GetNpc(int uid)
        {
            foreach (var n in npcs) if (n.uid == uid) return n;
            return null;
        }

        public string BuildVisibleNpcList(Vector3 center, float range)
        {
            var sb = new StringBuilder();
            float r2 = range * range;
            foreach (var n in npcs)
            {
                if ((n.pos - center).sqrMagnitude > r2) continue;
                if (sb.Length > 0) sb.Append('>');
                // uid_type_x,y,z_hp_maxhp_dead_customName_anim  (see Scr_NpcHandler.updateVisibleNpcs)
                sb.Append(n.uid).Append('_').Append(n.typeId).Append('_').Append(Vec(n.pos)).Append('_')
                  .Append(n.health).Append('_').Append(n.maxHealth).Append('_').Append(n.dead ? 1 : 0)
                  .Append("__0");
            }
            return sb.ToString();
        }

        public string BuildVisibleHarvestableList(Vector3 center, float range)
        {
            var sb = new StringBuilder();
            float r2 = range * range;
            foreach (var h in harvestables)
            {
                if ((h.pos - center).sqrMagnitude > r2) continue;
                if (sb.Length > 0) sb.Append('>');
                // uid_type_hp_x,y,z  (see Scr_HarvestableHandler.updateVisibleHarvestables)
                sb.Append(h.uid).Append('_').Append(h.typeId).Append('_').Append(h.health).Append('_').Append(Vec(h.pos));
            }
            return sb.ToString();
        }

        public static string Vec(Vector3 v) =>
            v.x.ToString("F2", CultureInfo.InvariantCulture) + "," +
            v.y.ToString("F2", CultureInfo.InvariantCulture) + "," +
            v.z.ToString("F2", CultureInfo.InvariantCulture);
    }
}
