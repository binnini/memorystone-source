using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// One-shot hex tile flash for area effect impacts (AoE hits, field placements/ticks, traps,
    /// scout reveals). Replaces the legacy opaque quad highlights: each flash builds fill/boundary
    /// meshes through the shared overlay mesh builder and Tile Overlay shader, then drives the
    /// material's _Fade through a rise/fall envelope and destroys itself. Unlike the layer-keyed
    /// <see cref="BatchedMeshCombatOverlayRenderer"/> (persistent, replace-on-show semantics),
    /// every flash is an independent instance, so overlapping flashes — several fields ticking on
    /// the same turn start, staggered monster attacks — never clobber each other.
    /// </summary>
    public sealed class CombatTileFlashPresenter : MonoBehaviour
    {
        private static readonly int FadePropertyId = Shader.PropertyToID("_Fade");

        // Flashes render above every themed overlay layer (TutorialTarget holds the highest static
        // priority, 7) so an impact always reads on top of range/intent fills.
        private const int FlashRenderPriority = 8;

        // Slightly above the static overlay lifts so coplanar fills never z-fight a flash.
        private const float FillLift = 0.055f;
        private const float BoundaryLift = 0.065f;

        private readonly CombatOverlayMeshBuilder meshBuilder = new CombatOverlayMeshBuilder();
        private readonly List<ActiveFlash> activeFlashes = new List<ActiveFlash>();
        private readonly List<HexCoord> coordScratch = new List<HexCoord>();

        private IHexMapWorldProjector projector;
        private System.Func<HexCoord, bool> tileAllowed;

        public int ActiveFlashCount => activeFlashes.Count;

        public void Configure(IHexMapWorldProjector projector)
        {
            this.projector = projector;
        }

        /// <summary>Same per-tile gate as the overlay renderer (e.g. water tiles never flash).</summary>
        public void SetTileFilter(System.Func<HexCoord, bool> filter)
        {
            tileAllowed = filter;
        }

        public void Flash(IEnumerable<HexCoord> coords, CombatTileFlashSpec spec)
        {
            // Envelope and teardown run in Update, so flashes are play-mode only by design.
            if (!Application.isPlaying || projector == null || coords == null)
            {
                return;
            }

            coordScratch.Clear();
            foreach (var coord in coords)
            {
                if (tileAllowed == null || tileAllowed(coord))
                {
                    coordScratch.Add(coord);
                }
            }

            if (coordScratch.Count == 0)
            {
                return;
            }

            var style = spec.Style;
            var root = new GameObject("Combat Tile Flash");
            root.transform.SetParent(transform, false);

            Mesh fillMesh = null;
            Mesh boundaryMesh = null;
            Material fillMaterial = null;
            Material boundaryMaterial = null;
            if (style.UseFill)
            {
                fillMesh = meshBuilder.BuildFillMesh(coordScratch, projector, FillLift, style.FillRadiusScale);
                fillMaterial = CreatePart(root.transform, "Fill", fillMesh, style, boundary: false);
            }

            if (style.UseBoundary)
            {
                boundaryMesh = meshBuilder.BuildBoundaryMesh(coordScratch, projector, style.BoundaryThickness, BoundaryLift, style.FillRadiusScale);
                boundaryMaterial = CreatePart(root.transform, "Boundary", boundaryMesh, style, boundary: true);
            }

            var flash = new ActiveFlash(root, fillMesh, boundaryMesh, fillMaterial, boundaryMaterial, spec);
            ApplyFade(flash, 0f);
            activeFlashes.Add(flash);
        }

        private void Update()
        {
            for (var i = activeFlashes.Count - 1; i >= 0; i--)
            {
                var flash = activeFlashes[i];
                flash.Elapsed += Time.deltaTime;
                if (flash.Elapsed >= flash.Spec.DurationSeconds)
                {
                    DestroyFlash(flash);
                    activeFlashes.RemoveAt(i);
                    continue;
                }

                ApplyFade(flash, EvaluateEnvelope(flash.Spec, flash.Elapsed));
            }
        }

        // Rise fast to full intensity, then ease out with a quadratic tail — the flash lands hard
        // and dies soft. The style's own shader pulse (if any) multiplies on top of this envelope.
        private static float EvaluateEnvelope(CombatTileFlashSpec spec, float elapsed)
        {
            var duration = spec.DurationSeconds;
            var attack = Mathf.Clamp(spec.AttackSeconds, 0.01f, duration * 0.5f);
            var rise = Mathf.Clamp01(elapsed / attack);
            var fallProgress = Mathf.Clamp01((elapsed - attack) / Mathf.Max(0.01f, duration - attack));
            var fall = (1f - fallProgress) * (1f - fallProgress);
            return rise * fall;
        }

        private static void ApplyFade(ActiveFlash flash, float value)
        {
            SetFade(flash.FillMaterial, value);
            SetFade(flash.BoundaryMaterial, value);
        }

        private static void SetFade(Material material, float value)
        {
            // The legacy URP-Unlit fallback shader has no _Fade; there the flash simply holds its
            // authored alpha and disappears at end of life instead of fading.
            if (material != null && material.HasProperty(FadePropertyId))
            {
                material.SetFloat(FadePropertyId, value);
            }
        }

        private static Material CreatePart(Transform parent, string name, Mesh mesh, CombatOverlayStyle style, bool boundary)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            var filter = part.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // The factory needs a layer only to resolve a render priority; override the queue right
            // after so flashes sit above every themed layer regardless of the theme asset's table.
            var material = CombatOverlayMaterialFactory.CreateOverlayMaterial(
                boundary ? "Combat Tile Flash Boundary" : "Combat Tile Flash Fill",
                HexOverlayLayer.TutorialTarget,
                style,
                boundary);
            material.renderQueue = HexOverlayRenderOrder.CombatOverlayRenderQueue + FlashRenderPriority;
            renderer.sharedMaterial = material;
            return material;
        }

        private void OnDestroy()
        {
            foreach (var flash in activeFlashes)
            {
                DestroyFlash(flash);
            }

            activeFlashes.Clear();
        }

        private static void DestroyFlash(ActiveFlash flash)
        {
            // Meshes and materials are unmanaged assets: destroying the root GameObject alone leaks them.
            DestroyObjectSafely(flash.FillMesh);
            DestroyObjectSafely(flash.BoundaryMesh);
            DestroyObjectSafely(flash.FillMaterial);
            DestroyObjectSafely(flash.BoundaryMaterial);
            DestroyObjectSafely(flash.Root);
        }

        private static void DestroyObjectSafely(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private sealed class ActiveFlash
        {
            public ActiveFlash(GameObject root, Mesh fillMesh, Mesh boundaryMesh, Material fillMaterial, Material boundaryMaterial, CombatTileFlashSpec spec)
            {
                Root = root;
                FillMesh = fillMesh;
                BoundaryMesh = boundaryMesh;
                FillMaterial = fillMaterial;
                BoundaryMaterial = boundaryMaterial;
                Spec = spec;
            }

            public GameObject Root { get; }
            public Mesh FillMesh { get; }
            public Mesh BoundaryMesh { get; }
            public Material FillMaterial { get; }
            public Material BoundaryMaterial { get; }
            public CombatTileFlashSpec Spec { get; }
            public float Elapsed;
        }
    }
}
