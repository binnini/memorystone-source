using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// Decorates fog-of-war ("암시야") tiles with drifting fog particles. It does NOT spawn one
    /// emitter per tile every refresh (the pattern that made the old decorator grow unbounded).
    /// Instead it is driven by the frontier cell set that
    /// <see cref="AtlasTilePresentationView"/> already recomputes only when the Unknown/Hinted
    /// membership changes, and pools a small number of looping emitters keyed by cell coord:
    /// only the boundary between explored and unexplored (the "edge of the unknown") is fogged,
    /// so the active count stays in the tens, not the thousands.
    ///
    /// Wiring: <see cref="SyncIfChanged"/> is called once, right after
    /// <c>presenter.ApplyVisibility(...)</c>, and no-ops on any refresh where the frontier revision
    /// is unchanged. Particle drift is the ParticleSystem's own looping simulation, not re-emission.
    ///
    /// Sizing is auto-scaled to the live map: the world distance between two adjacent tiles is
    /// measured through the world-position resolver, so particle size/radius stay correct regardless
    /// of the map view's transform scale. Rendering uses a runtime URP-compatible particle material
    /// (the shipping Fog Particles material ships with a legacy built-in shader that does not render
    /// under URP), copying the source texture/tint.
    /// </summary>
    public sealed class VisibilityFogPresenter : MonoBehaviour
    {
        /// <summary>Resolves a cell coord to its world position on the live map view.</summary>
        public delegate bool TryGetTileWorld(HexCoord coord, out Vector3 worldPosition);

        private static readonly int[] NeighborDQ = { 1, 1, 0, -1, -1, 0 };
        private static readonly int[] NeighborDR = { 0, -1, -1, 0, 1, 1 };

        [Header("Coverage")]
        [Tooltip("Minimum spacing (in tiles) between fog clumps. Each clump is one big multi-tile fog; larger spacing = fewer, more separated clumps placed here and there.")]
        [Min(1)]
        [SerializeField] private int seedSpacing = 5;
        [Tooltip("Fraction of Unknown tiles eligible to seed a clump (adds randomness to where clumps land). Final clump count is governed mainly by seedSpacing.")]
        [Range(0f, 1f)]
        [SerializeField] private float coverageProbability = 0.6f;
        [Tooltip("Extra random height (0..this) added per tile on top of heightOffset, so fog sits at varied heights.")]
        [SerializeField] private float heightVariation = 0.9f;

        [Header("Look")]
        [Tooltip("Source material for texture/tint. A URP particle material is built from it at runtime.")]
        [SerializeField] private Material fogMaterial;
        [Tooltip("Vertical lift above the tile surface where fog sits.")]
        [SerializeField] private float heightOffset = 0.4f;
        [Tooltip("Random horizontal offset per cell (fraction of tile spacing) so fog is not a grid.")]
        [SerializeField] private float positionJitterRatio = 0.25f;
        [Tooltip("Tint (multiplies the texture). Alpha controls overall density.")]
        [SerializeField] private Color tint = new Color(0.55f, 0.68f, 1f, 0.85f);
        [Tooltip("Blend: 0 = Alpha (soft haze), 2 = Additive (glowy smoke). Alpha reads as subtle fog.")]
        [SerializeField] private int blendMode = 0;
        [Tooltip("Peak per-particle opacity. Low values let many particles overlap into a smooth haze.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float peakOpacity = 0.28f;

        [Header("Particle (auto-scaled to tile spacing)")]
        [Tooltip("Particle diameter as a multiple of the measured world tile spacing.")]
        [SerializeField] private float particleSizeRatio = 1.6f;
        [Tooltip("Per-cell emission disc radius as a multiple of tile spacing.")]
        [SerializeField] private float emissionRadiusRatio = 0.55f;
        [SerializeField] private float particleLifetime = 6f;
        [SerializeField] private float driftSpeed = 0.04f;
        [SerializeField] private float emissionRate = 7f;
        [Tooltip("Max live particles per cell. Higher = smoother overlap (more overdraw).")]
        [SerializeField] private int maxParticlesPerCell = 96;
        [Tooltip("Fallback world tile spacing used until the live spacing is measured.")]
        [SerializeField] private float fallbackTileSpacing = 6f;

        [Header("Pool")]
        [SerializeField] private int initialPoolSize = 64;
        [Tooltip("Hard cap on simultaneous fogged cells. Exceeding it is logged, never silently dropped.")]
        [SerializeField] private int maxActiveCells = 400;

        [Header("Debug")]
        [Tooltip("Logs frontier counts, measured spacing, and emitter activity to the Console.")]
        [SerializeField] private bool verboseLogging = true;

        private sealed class Emitter
        {
            public GameObject Go;
            public ParticleSystem System;
        }

        private struct Candidate
        {
            public HexCoord Coord;
            public Vector3 World;
            public float SqrDist;
        }

        private readonly Dictionary<HexCoord, Emitter> active = new Dictionary<HexCoord, Emitter>();
        private readonly List<Emitter> draining = new List<Emitter>();
        private readonly Stack<Emitter> pool = new Stack<Emitter>();
        private readonly HashSet<HexCoord> incomingFrontier = new HashSet<HexCoord>();
        private readonly List<HexCoord> removedScratch = new List<HexCoord>();
        private readonly List<Candidate> candidateScratch = new List<Candidate>();
        private readonly List<Candidate> selectedScratch = new List<Candidate>();

        private bool hasLastRevision;
        private int lastRevision;
        private bool poolWarmed;
        private Material runtimeMaterial;
        private float worldTileSpacing;
        private bool spacingMeasured;
        private int syncCount;

        /// <summary>
        /// Re-decorate the fog frontier. No-ops when <paramref name="frontierRevision"/> matches the
        /// last applied revision (the frontier membership has not changed since the last call).
        /// </summary>
        public void SyncIfChanged(int fogRevision, IReadOnlyList<HexCoord> unknownCells, TryGetTileWorld resolver, Vector3 priorityCenter)
        {
            // Master on/off: unchecking the component (or disabling its GameObject) turns fog off — see
            // OnDisable, which clears live emitters. Re-enabling re-syncs on the next visibility refresh.
            if (!isActiveAndEnabled)
            {
                return;
            }
            if (hasLastRevision && fogRevision == lastRevision)
            {
                return;
            }

            hasLastRevision = true;
            lastRevision = fogRevision;
            Sync(unknownCells, resolver, priorityCenter);
        }

        private void Sync(IReadOnlyList<HexCoord> unknownCells, TryGetTileWorld resolver, Vector3 priorityCenter)
        {
            syncCount++;
            WarmPool();
            MeasureSpacing(unknownCells, resolver);

            // Probabilistic scatter across the whole Unknown region: each tile is deterministically
            // included with coverageProbability (hash-based, so it does not flicker between refreshes).
            // Resolve the world position once — used for both distance sorting and placement.
            candidateScratch.Clear();
            var totalUnknown = unknownCells?.Count ?? 0;
            if (unknownCells != null && resolver != null)
            {
                for (var i = 0; i < totalUnknown; i++)
                {
                    var coord = unknownCells[i];
                    if (Hash01((coord.Q * 0x1f1f1f1f) ^ (coord.R * 0x27d4eb2d) ^ 0x5bd1e995) >= coverageProbability)
                    {
                        continue;
                    }
                    if (!resolver(coord, out var world))
                    {
                        continue;
                    }
                    candidateScratch.Add(new Candidate { Coord = coord, World = world, SqrDist = (world - priorityCenter).sqrMagnitude });
                }
            }

            // Nearest-first so clumps concentrate around the player when the region exceeds the budget.
            candidateScratch.Sort(CandidateNearestFirst);

            // Greedy min-spacing seeding: walk candidates nearest-first and accept one only when it is at
            // least seedSpacing tiles from every already-accepted seed. Each accepted seed becomes one big
            // multi-tile fog clump, so fog lands here and there instead of on every tile.
            selectedScratch.Clear();
            for (var i = 0; i < candidateScratch.Count && selectedScratch.Count < maxActiveCells; i++)
            {
                var candidate = candidateScratch[i];
                var tooClose = false;
                for (var s = 0; s < selectedScratch.Count; s++)
                {
                    if (candidate.Coord.DistanceTo(selectedScratch[s].Coord) < seedSpacing)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (!tooClose)
                {
                    selectedScratch.Add(candidate);
                }
            }

            incomingFrontier.Clear();
            for (var i = 0; i < selectedScratch.Count; i++)
            {
                incomingFrontier.Add(selectedScratch[i].Coord);
            }

            // Retire clumps no longer selected (revealed, or dropped): fade their particles out.
            removedScratch.Clear();
            foreach (var kvp in active)
            {
                if (!incomingFrontier.Contains(kvp.Key))
                {
                    removedScratch.Add(kvp.Key);
                }
            }
            for (var i = 0; i < removedScratch.Count; i++)
            {
                var coord = removedScratch[i];
                var emitter = active[coord];
                active.Remove(coord);
                Retire(emitter);
            }

            // Activate newly-selected clumps.
            var spawned = 0;
            for (var i = 0; i < selectedScratch.Count; i++)
            {
                var candidate = selectedScratch[i];
                if (active.ContainsKey(candidate.Coord))
                {
                    continue;
                }
                var emitter = Acquire();
                PlaceAndPlay(emitter, candidate.Coord, candidate.World);
                active[candidate.Coord] = emitter;
                spawned++;
            }

            if (verboseLogging)
            {
                var mat = ResolveRuntimeMaterial();
                Debug.Log(
                    $"[FogDbg] sync#{syncCount} unknown={totalUnknown} candidates={candidateScratch.Count} clumps={selectedScratch.Count} " +
                    $"active={active.Count} spawned={spawned} retired={removedScratch.Count} spacing={worldTileSpacing:F2} " +
                    $"shader={(mat != null && mat.shader != null ? mat.shader.name : "NULL")}");
            }
        }

        private static int CandidateNearestFirst(Candidate a, Candidate b) => a.SqrDist.CompareTo(b.SqrDist);

        private void MeasureSpacing(IReadOnlyList<HexCoord> frontierCells, TryGetTileWorld resolver)
        {
            if (spacingMeasured)
            {
                return;
            }

            if (resolver != null && frontierCells != null)
            {
                for (var i = 0; i < frontierCells.Count; i++)
                {
                    var c = frontierCells[i];
                    if (!resolver(c, out var w0))
                    {
                        continue;
                    }

                    for (var n = 0; n < NeighborDQ.Length; n++)
                    {
                        var neighbor = new HexCoord(c.Q + NeighborDQ[n], c.R + NeighborDR[n]);
                        if (resolver(neighbor, out var w1))
                        {
                            var d = Vector3.Distance(w0, w1);
                            if (d > 0.01f)
                            {
                                worldTileSpacing = d;
                                spacingMeasured = true;
                                return;
                            }
                        }
                    }
                }
            }

            // Not measurable yet (empty frontier / no resolver): use fallback but keep trying next sync.
            if (worldTileSpacing <= 0f)
            {
                worldTileSpacing = fallbackTileSpacing;
            }
        }

        private void PlaceAndPlay(Emitter emitter, HexCoord coord, Vector3 world)
        {
            var jitterAmt = worldTileSpacing * positionJitterRatio;
            var jitter = Vector3.zero;
            if (jitterAmt > 0f)
            {
                var hx = Hash01(coord.Q * 73856093 ^ coord.R * 19349663);
                var hz = Hash01(coord.Q * 83492791 ^ coord.R * 51875291);
                jitter = new Vector3((hx - 0.5f) * 2f * jitterAmt, 0f, (hz - 0.5f) * 2f * jitterAmt);
            }

            // Vary the height per tile so the fog reads as an uneven, layered bank rather than a flat sheet.
            var extraHeight = heightVariation > 0f
                ? Hash01((coord.Q * 0x2545F491) ^ (coord.R * unchecked((int)0x9E3779B1))) * heightVariation
                : 0f;
            emitter.Go.transform.position = world + Vector3.up * (heightOffset + extraHeight) + jitter;

            var main = emitter.System.main;
            main.startSize = worldTileSpacing * particleSizeRatio;
            var shape = emitter.System.shape;
            shape.radius = worldTileSpacing * emissionRadiusRatio;

            emitter.Go.SetActive(true);
            emitter.System.Clear(true);
            emitter.System.Play(true);
        }

        private void Retire(Emitter emitter)
        {
            var emission = emitter.System.emission;
            emission.enabled = false;
            emitter.System.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            draining.Add(emitter);
        }

        private void OnDisable()
        {
            // Turned off (component unchecked / GameObject disabled): pool every live emitter so no fog
            // lingers, and force a re-sync when re-enabled.
            foreach (var kvp in active)
            {
                kvp.Value.Go.SetActive(false);
                pool.Push(kvp.Value);
            }
            active.Clear();
            for (var i = 0; i < draining.Count; i++)
            {
                draining[i].Go.SetActive(false);
                pool.Push(draining[i]);
            }
            draining.Clear();
            hasLastRevision = false;
        }

        private void Update()
        {
            if (draining.Count == 0)
            {
                return;
            }

            for (var i = draining.Count - 1; i >= 0; i--)
            {
                var emitter = draining[i];
                if (emitter.System.particleCount == 0)
                {
                    draining.RemoveAt(i);
                    var emission = emitter.System.emission;
                    emission.enabled = true;
                    emitter.System.Clear(true);
                    emitter.Go.SetActive(false);
                    pool.Push(emitter);
                }
            }
        }

        private void WarmPool()
        {
            if (poolWarmed)
            {
                return;
            }

            poolWarmed = true;
            for (var i = 0; i < initialPoolSize; i++)
            {
                pool.Push(CreateEmitter(i));
            }
        }

        private Emitter Acquire()
        {
            if (pool.Count > 0)
            {
                return pool.Pop();
            }

            return CreateEmitter(active.Count + draining.Count);
        }

        private Emitter CreateEmitter(int index)
        {
            var go = new GameObject($"FogEmitter_{index}");
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = particleLifetime;
            main.startSpeed = driftSpeed;
            main.startSize = Mathf.Max(0.1f, worldTileSpacing * particleSizeRatio);
            main.startColor = tint;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = Mathf.Max(8, maxParticlesPerCell);
            // No random spin — tumbling reads as smoke. A near-still, slowly blooming particle reads as fog.
            main.startRotation = 0f;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = emissionRate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.1f, worldTileSpacing * emissionRadiusRatio);
            shape.radiusThickness = 1f;

            // Bloom: each particle is born small and spreads as it fades, so overlapping particles
            // dissolve into a continuous haze instead of reading as discrete puffs.
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            // Rise to near-full size quickly and hold it, so at any instant most particles are large and
            // overlapping — that overlap is what fuses the per-tile emitters into one continuous fog bank
            // (young, still-small particles would otherwise punch visible holes in the sheet). Fade is
            // handled by alpha, not size, so nothing pops.
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.75f),
                new Keyframe(0.2f, 1f),
                new Keyframe(1f, 1f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // Low, soft alpha in/out. peakOpacity keeps each particle faint so density comes from overlap.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(peakOpacity, 0.4f),
                    new GradientAlphaKey(peakOpacity, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = ResolveRuntimeMaterial();
            renderer.sortingFudge = 0f;

            return new Emitter { Go = go, System = ps };
        }

        // The shipping Fog Particles material uses a legacy built-in particle shader that does not
        // render under URP. Build a URP Particles/Unlit material at runtime and copy the source
        // texture/tint so the fog actually shows.
        private Material ResolveRuntimeMaterial()
        {
            if (runtimeMaterial != null)
            {
                return runtimeMaterial;
            }

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                runtimeMaterial = fogMaterial;
                return runtimeMaterial;
            }

            var mat = new Material(shader) { name = "VisibilityFog (runtime)" };

            Texture tex = null;
            if (fogMaterial != null)
            {
                if (fogMaterial.HasProperty("_MainTex")) tex = fogMaterial.GetTexture("_MainTex");
                if (tex == null && fogMaterial.HasProperty("_BaseMap")) tex = fogMaterial.GetTexture("_BaseMap");
            }

            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);

            // Transparent surface config (works across URP unlit / sprite shaders).
            mat.SetOverrideTag("RenderType", "Transparent");
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", blendMode);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_SrcBlend"))
            {
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            }
            if (mat.HasProperty("_DstBlend"))
            {
                mat.SetFloat("_DstBlend", blendMode == 2 ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            }
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;

            runtimeMaterial = mat;
            return runtimeMaterial;
        }

        private static float Hash01(int seed)
        {
            unchecked
            {
                var h = (uint)seed;
                h ^= h >> 16;
                h *= 0x7feb352du;
                h ^= h >> 15;
                h *= 0x846ca68bu;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }
    }
}
