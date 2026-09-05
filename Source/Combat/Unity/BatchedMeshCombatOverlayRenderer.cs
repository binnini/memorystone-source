using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeoulPlayup.Combat.Unity
{
    public sealed class BatchedMeshCombatOverlayRenderer : MonoBehaviour, ICombatOverlayRenderer
    {
        private readonly Dictionary<HexOverlayLayer, LayerVisual> visuals = new Dictionary<HexOverlayLayer, LayerVisual>();
        private readonly Dictionary<HexOverlayLayer, int> activeCounts = new Dictionary<HexOverlayLayer, int>();
        private readonly Dictionary<HexOverlayLayer, long> layerSignatures = new Dictionary<HexOverlayLayer, long>();
        private readonly CombatOverlayMeshBuilder meshBuilder = new CombatOverlayMeshBuilder();

        // Reused per-call scratch for filtered/deduped/sorted coords, so a ShowLayer that changes nothing
        // (or rebuilds) allocates no coord array.
        private readonly HashSet<HexCoord> dedupScratch = new HashSet<HexCoord>();
        private readonly List<HexCoord> orderedScratch = new List<HexCoord>();

        private static readonly int FadePropertyId = Shader.PropertyToID("_Fade");

        // Short show/hide fade so overlays ease in and out instead of popping. Runtime-only: in the
        // editor / tests the fade snaps instantly so behavior (and assertions) stay unchanged.
        private const float FadeDuration = 0.12f;

        private IHexMapWorldProjector projector;
        private System.Func<HexCoord, bool> tileAllowed;
        private int rebuildCount;

        public int RebuildCount => rebuildCount;
        public bool IsConfigured => projector != null;

        public void Configure(IHexMapWorldProjector projector)
        {
            this.projector = projector;
        }

        /// <summary>
        /// Optional per-tile gate. Coords the predicate rejects (e.g. water) are dropped from every
        /// layer before meshing, so no overlay is ever drawn on them. Null allows all tiles.
        /// </summary>
        public void SetTileFilter(System.Func<HexCoord, bool> filter)
        {
            tileAllowed = filter;
            // Force shown layers to rebuild so the new filter takes effect immediately.
            layerSignatures.Clear();
        }

        public void InvalidateGeometry()
        {
            layerSignatures.Clear();
        }

        public void ShowLayer(HexOverlayLayer layer, IEnumerable<HexCoord> coords, CombatOverlayStyle style)
        {
            if (projector == null)
            {
                ClearLayer(layer);
                return;
            }

            BuildOrderedCoords(coords);
            var visual = GetOrCreateVisual(layer);
            var signature = CreateSignature(orderedScratch, style);
            var geometryUnchanged = layerSignatures.TryGetValue(layer, out var current) && current == signature;
            if (!geometryUnchanged)
            {
                if (style.UseFill)
                {
                    meshBuilder.PopulateFillMesh(visual.FillMesh, orderedScratch, projector, HexOverlayRenderOrder.CombatFillLift, style.FillRadiusScale);
                }
                else
                {
                    visual.FillMesh.Clear();
                }

                if (style.UseBoundary)
                {
                    meshBuilder.PopulateBoundaryMesh(visual.BoundaryMesh, orderedScratch, projector, style.BoundaryThickness, HexOverlayRenderOrder.CombatBoundaryLift, style.FillRadiusScale);
                }
                else
                {
                    visual.BoundaryMesh.Clear();
                }

                ApplyMaterial(layer, visual, style);
                layerSignatures[layer] = signature;
                rebuildCount++;
            }

            visual.WantFill = style.UseFill && orderedScratch.Count > 0;
            visual.WantBoundary = style.UseBoundary && orderedScratch.Count > 0;
            activeCounts[layer] = orderedScratch.Count;
            BeginShow(visual);
        }

        public void ClearLayer(HexOverlayLayer layer)
        {
            if (visuals.TryGetValue(layer, out var visual))
            {
                visual.WantFill = false;
                visual.WantBoundary = false;
                visual.FadeTarget = 0f;
                // In the editor / tests, tear down immediately (no play loop to run the fade). At runtime
                // keep rendering and let Update fade the layer out, finalizing the hide when it reaches 0.
                if (!Application.isPlaying)
                {
                    FinalizeHide(visual);
                }
            }

            activeCounts[layer] = 0;
            layerSignatures.Remove(layer);
            rebuildCount++;
        }

        // Filter (water etc.), dedupe and sort the incoming coords into the reused orderedScratch list.
        private void BuildOrderedCoords(IEnumerable<HexCoord> coords)
        {
            dedupScratch.Clear();
            orderedScratch.Clear();
            if (coords == null)
            {
                return;
            }

            foreach (var coord in coords)
            {
                if ((tileAllowed == null || tileAllowed(coord)) && dedupScratch.Add(coord))
                {
                    orderedScratch.Add(coord);
                }
            }

            orderedScratch.Sort();
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var step = FadeDuration > 0f ? Time.deltaTime / FadeDuration : 1f;
            foreach (var visual in visuals.Values)
            {
                if (Mathf.Approximately(visual.Fade, visual.FadeTarget))
                {
                    continue;
                }

                visual.Fade = Mathf.MoveTowards(visual.Fade, visual.FadeTarget, step);
                SetFade(visual, visual.Fade);
                if (visual.FadeTarget <= 0f && visual.Fade <= 0.0001f)
                {
                    FinalizeHide(visual);
                }
            }
        }

        // Bring a layer toward fully shown. Renderers are enabled up front so the fade-in is visible;
        // the fade value itself eases up in Update (snapped instantly outside play mode).
        private void BeginShow(LayerVisual visual)
        {
            visual.FadeTarget = 1f;
            visual.FillRenderer.enabled = visual.WantFill;
            visual.BoundaryRenderer.enabled = visual.WantBoundary;
            if (!Application.isPlaying)
            {
                visual.Fade = 1f;
            }

            SetFade(visual, visual.Fade);
        }

        private void FinalizeHide(LayerVisual visual)
        {
            visual.Fade = 0f;
            SetFade(visual, 0f);
            visual.FillRenderer.enabled = false;
            visual.BoundaryRenderer.enabled = false;
            // Empty the persistent meshes in place (kept for reuse) rather than allocating new ones.
            visual.FillMesh.Clear();
            visual.BoundaryMesh.Clear();
        }

        private static void SetFade(LayerVisual visual, float value)
        {
            if (visual.FillMaterial != null) visual.FillMaterial.SetFloat(FadePropertyId, value);
            if (visual.BoundaryMaterial != null) visual.BoundaryMaterial.SetFloat(FadePropertyId, value);
        }

        public int GetActiveCount(HexOverlayLayer layer)
        {
            return activeCounts.TryGetValue(layer, out var count) ? count : 0;
        }

        private LayerVisual GetOrCreateVisual(HexOverlayLayer layer)
        {
            if (visuals.TryGetValue(layer, out var visual))
            {
                return visual;
            }

            var root = new GameObject($"CombatOverlay_{layer}");
            root.transform.SetParent(transform, false);
            var fill = CreatePart(root.transform, "Fill");
            var boundary = CreatePart(root.transform, "Boundary");

            // Persistent, dynamically-updated meshes owned per layer: rewritten in place on every rebuild
            // instead of destroy/recreate, so hovering the map does not churn Mesh allocations.
            var fillMesh = new Mesh { name = $"CombatOverlayFill_{layer}" };
            fillMesh.MarkDynamic();
            fill.filter.sharedMesh = fillMesh;
            var boundaryMesh = new Mesh { name = $"CombatOverlayBoundary_{layer}" };
            boundaryMesh.MarkDynamic();
            boundary.filter.sharedMesh = boundaryMesh;

            visual = new LayerVisual(fill.filter, fill.renderer, boundary.filter, boundary.renderer, fillMesh, boundaryMesh);
            visuals[layer] = visual;
            return visual;
        }

        private static (MeshFilter filter, MeshRenderer renderer) CreatePart(Transform parent, string name)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            var filter = part.AddComponent<MeshFilter>();
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.enabled = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return (filter, renderer);
        }

        private void OnDestroy()
        {
            foreach (var visual in visuals.Values)
            {
                DestroyOverlayObject(visual.FillMesh);
                DestroyOverlayObject(visual.BoundaryMesh);
                DestroyOverlayObject(visual.FillMaterial);
                DestroyOverlayObject(visual.BoundaryMaterial);
            }
        }

        private static void DestroyOverlayObject(Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }

        // Per-layer materials are dedicated instances, so cache them and just re-apply the style
        // (no destroy/recreate each rebuild — the previous churn thrashed the GC and lost SRP batches).
        private static void ApplyMaterial(HexOverlayLayer layer, LayerVisual visual, CombatOverlayStyle style)
        {
            visual.FillMaterial = EnsureMaterial(visual.FillMaterial, visual.FillRenderer, "Combat Overlay Fill", layer, style, boundary: false);
            visual.BoundaryMaterial = EnsureMaterial(visual.BoundaryMaterial, visual.BoundaryRenderer, "Combat Overlay Boundary", layer, style, boundary: true);
        }

        private static Material EnsureMaterial(Material current, MeshRenderer renderer, string name, HexOverlayLayer layer, CombatOverlayStyle style, bool boundary)
        {
            if (current == null)
            {
                current = CombatOverlayMaterialFactory.CreateOverlayMaterial(name, layer, style, boundary);
                renderer.sharedMaterial = current;
            }
            else
            {
                CombatOverlayMaterialFactory.Apply(current, layer, style, boundary);
            }

            return current;
        }

        // Allocation-free change key: a 64-bit FNV-1a fold over the style fields and (canonically sorted)
        // coords. Replaces the old per-call signature string so hovering the map produces no garbage.
        // A 64-bit hash makes a false "unchanged" collision astronomically unlikely.
        private static long CreateSignature(List<HexCoord> coords, CombatOverlayStyle style)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                hash = Fold(hash, style.FillColor.GetHashCode());
                hash = Fold(hash, style.BoundaryColor.GetHashCode());
                hash = Fold(hash, style.BoundaryThickness.GetHashCode());
                hash = Fold(hash, style.UseFill ? 1 : 0);
                hash = Fold(hash, style.UseBoundary ? 1 : 0);
                hash = Fold(hash, style.EdgeGlowColor.GetHashCode());
                hash = Fold(hash, style.EdgeGlowWidth.GetHashCode());
                hash = Fold(hash, style.EdgeFeather.GetHashCode());
                hash = Fold(hash, style.FillPatternMode);
                hash = Fold(hash, style.PatternScale.GetHashCode());
                hash = Fold(hash, style.PatternAngle.GetHashCode());
                hash = Fold(hash, style.PatternScroll.GetHashCode());
                hash = Fold(hash, style.PatternOpacity.GetHashCode());
                hash = Fold(hash, style.PulseSpeed.GetHashCode());
                hash = Fold(hash, style.PulseAlphaMin.GetHashCode());
                hash = Fold(hash, style.PulseAlphaMax.GetHashCode());
                hash = Fold(hash, style.AntsSpeed.GetHashCode());
                hash = Fold(hash, style.AntsDashLength.GetHashCode());
                hash = Fold(hash, style.FillRadiusScale.GetHashCode());
                hash = Fold(hash, coords.Count);
                for (var i = 0; i < coords.Count; i++)
                {
                    hash = Fold(hash, coords[i].Q);
                    hash = Fold(hash, coords[i].R);
                }

                return (long)hash;
            }
        }

        private static ulong Fold(ulong hash, int value)
        {
            unchecked
            {
                return (hash ^ (uint)value) * 1099511628211UL;
            }
        }

        private sealed class LayerVisual
        {
            public LayerVisual(MeshFilter fillFilter, MeshRenderer fillRenderer, MeshFilter boundaryFilter, MeshRenderer boundaryRenderer, Mesh fillMesh, Mesh boundaryMesh)
            {
                FillFilter = fillFilter;
                FillRenderer = fillRenderer;
                BoundaryFilter = boundaryFilter;
                BoundaryRenderer = boundaryRenderer;
                FillMesh = fillMesh;
                BoundaryMesh = boundaryMesh;
            }

            public MeshFilter FillFilter { get; }
            public MeshRenderer FillRenderer { get; }
            public MeshFilter BoundaryFilter { get; }
            public MeshRenderer BoundaryRenderer { get; }
            public Mesh FillMesh { get; }
            public Mesh BoundaryMesh { get; }
            public Material FillMaterial;
            public Material BoundaryMaterial;

            // Show/hide fade state. Fade eases toward FadeTarget (1 = shown, 0 = hidden) in Update;
            // WantFill/WantBoundary remember which parts the current show requested.
            public float Fade;
            public float FadeTarget;
            public bool WantFill;
            public bool WantBoundary;
        }
    }
}
