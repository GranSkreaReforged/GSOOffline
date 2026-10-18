using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Worn gear of a high grade shimmers (#87): small sparkles come off where the item is worn (the helmet on the
    /// head, the weapon in the hand, the trousers on the legs) in the grade's colour, the tooltip's name colours.
    /// The better the grade, the more of them: Rare (4) a few, Unique (5) and Epic (6) more, Epic and up drifting
    /// upwards, and Mythic (8) the most, its colour wandering between red and gold. Lower grades show nothing. The
    /// local player only; `GearEffects` in the config turns it off.
    /// The sparkles are few and a few centimetres across: many large additive ones piled up past white and the bloom
    /// post-effect turned them into a glare. The component sits on the item's model, so its emitters go when the client unloads
    /// or swaps the item. Items the game has no model for (the Golden eye, Highlander's boots, most jewellery) show
    /// nothing.
    /// </summary>
    internal class GradeAura : MonoBehaviour
    {
        private const string Name = "GSO grade aura";
        private static Material material;

        private int grade;
        private Color color;
        private readonly System.Collections.Generic.List<ParticleSystem> systems = new System.Collections.Generic.List<ParticleSystem>();

        // Sparkles per second per metre of the item's size, by grade (index = grade).
        private static readonly float[] Rate = { 0f, 0f, 0f, 0f, 1.5f, 2.5f, 4f, 6f, 9f };

        internal static void Apply(GameObject model, int grade)
        {
            if (model == null) return;
            var old = model.GetComponent<GradeAura>();
            if (old != null) old.Remove();
            if (!Plugin.GearEffects.Value || grade < 4 || grade >= Rate.Length) return;
            model.AddComponent<GradeAura>().Setup(grade);
        }

        private void Setup(int g)
        {
            grade = g;
            color = Menucontroller.instance != null ? Menucontroller.instance.GetGradeColor(g) : Color.white;
            foreach (var r in GetComponentsInChildren<Renderer>(false))
                if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)) parts.Add(r);
            if (parts.Count == 0 || !Shown(out Bounds b)) return;
            systems.Add(Sparkles(b));
            LateUpdate();
        }

        // One emitter per piece: a box that follows the bounds of the piece's parts (where it's drawn, animation and
        // all; a shield is four parts in one place). It lives at the scene root: a mesh-surface emitter used the
        // armour's unanimated mesh, lying on its side in the rig's own scale, and the models' and bones' scales
        // (0.01, 100) bent the sparkles' sizes.
        private readonly System.Collections.Generic.List<Renderer> parts = new System.Collections.Generic.List<Renderer>();

        private bool Shown(out Bounds all)
        {
            all = default(Bounds);
            bool any = false;
            foreach (var r in parts)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (any) all.Encapsulate(r.bounds);
                else { all = r.bounds; any = true; }
            }
            return any;
        }

        private void LateUpdate()
        {
            if (systems.Count == 0 || systems[0] == null) return;
            var ps = systems[0];
            bool shown = Shown(out Bounds b);
            if (ps.gameObject.activeSelf != shown) ps.gameObject.SetActive(shown);
            if (!shown) return;
            ps.transform.position = b.center;
            var shape = ps.shape;
            shape.scale = b.size * 0.8f;
        }

        private void OnDestroy()
        {
            foreach (var ps in systems) if (ps != null) Destroy(ps.gameObject);
        }

        private ParticleSystem Sparkles(Bounds bounds)
        {
            var go = new GameObject(Name);
            go.layer = parts[0].gameObject.layer;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            float size = Mathf.Clamp(bounds.size.magnitude, 0.3f, 2f);
            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, grade >= 7 ? 1.4f : 1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.02f);
            // Playtested: 15% bigger than first tried, and Mythic 25% so it feels rare.
            float scale = grade == 8 ? 1.25f : 1.15f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f * scale, (grade >= 7 ? 0.08f : 0.06f) * scale);
            main.startColor = new Color(color.r, color.g, color.b, 0.8f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.gravityModifier = grade >= 6 ? -0.02f : 0f;   // Epic and up: they drift upwards
            main.maxParticles = 40;

            var emission = ps.emission;
            emission.rateOverTime = Rate[grade] * size;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;   // sized and placed in LateUpdate

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var shrink = ps.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = SparkleMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        private void Update()
        {
            if (grade != 8) return;
            // Mythic: the colour wanders between red and gold.
            var c = Color.Lerp(color, new Color(1f, 0.75f, 0.1f), Mathf.PingPong(Time.time * 0.5f, 1f));
            c.a = 0.8f;
            foreach (var ps in systems)
            {
                if (ps == null) continue;
                var main = ps.main;
                main.startColor = c;
            }
        }

        private void Remove()
        {
            foreach (var ps in systems) if (ps != null) Destroy(ps.gameObject);
            Destroy(this);
        }

        /// <summary>A soft round dot: made here rather than borrowed, so it never depends on what a scene loaded.</summary>
        private static Material SparkleMaterial()
        {
            if (material != null) return material;
            const int n = 32;
            var dot = new Texture2D(n, n, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * a + 0.6f * Mathf.Clamp01(1f - d * 4f);   // a small bright core in a faint halo
                    dot.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
                }
            dot.Apply();
            material = new Material(Shader.Find("Particles/Additive")) { mainTexture = dot, name = "GSO sparkle" };
            return material;
        }
    }

    /// <summary>The local player's gear model was (re)loaded: give it its grade's aura.</summary>
    [HarmonyPatch(typeof(Script_EquipmentLoader), nameof(Script_EquipmentLoader.loadItem), typeof(Item))]
    internal static class GradeEffectPatch
    {
        private static void Postfix(Script_EquipmentLoader __instance, Item item)
        {
            if (item == null || Inventory.instance == null || __instance != Inventory.instance.eqLoader) return;
            foreach (var slot in __instance.eqSlots)
                if (slot.slot == item.itemSlot && slot.itemId == item.itemID && slot.instancedObject != null)
                    GradeAura.Apply(slot.instancedObject, item.itemGrade);
        }
    }
}
