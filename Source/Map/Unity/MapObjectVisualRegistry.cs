using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// The slice of <see cref="AtlasTilePresentationView"/> that the presentation view needs from its host to
    /// place and refresh map object visuals. Deliberately narrow: the registry can project coordinates, resolve
    /// a prefab, read cached visibility and report a diagnostic — and nothing else. It has no way to touch tile
    /// visuals, overlays, chunk batching, or the lighting mask, which is what keeps the extraction from becoming
    /// a second entry point into the view's state.
    ///
    /// Prefab resolution and the placement lift stay behind this interface rather than moving into the registry
    /// because both come from <c>[SerializeField]</c>s authored in four scenes (MainGameplay, ArtLookdev,
    /// CombatTimingLab, PrototypeTest). Moving a serialized field would mean a scene migration for a pure
    /// refactor; keeping it on the MonoBehaviour costs one interface member and zero scene edits.
    /// </summary>
    internal interface IMapObjectVisualHost
    {
        /// <summary>Parent for spawned instances, and the local space that fade/occlusion math runs in.</summary>
        Transform MapTransform { get; }

        /// <summary>Authored lift applied above the tile surface so objects do not z-fight the top face.</summary>
        float MapObjectLift { get; }

        Vector3 ProjectOverlaySurface(HexCoord coord);

        bool TryResolveMapObjectPrefab(string objectRef, out GameObject prefab);

        /// <summary>Reads the view's per-refresh visibility cache. False when the coord is not cached at all.</summary>
        bool TryGetVisibilitySafeInfo(HexCoord coord, out HexVisibilitySafeCellInfo safeInfo);

        /// <summary>Appends to the view's missing-visual diagnostics list (surfaced by map validation tools).</summary>
        void ReportMissingVisual(string message);
    }

    /// <summary>
    /// Owns every placed map object visual — instantiation, pointer hover, occlusion fade, visibility-driven
    /// show/hide, the consumed-object set, buildings-only cinematic mode, and the night-look dimmer.
    ///
    /// Extracted from <see cref="AtlasTilePresentationView"/>, which retains the whole public API as thin
    /// delegation: 13 members with 76 call sites across gameplay and three performance test fixtures, so moving
    /// the surface itself would have been churn with no structural payoff. What moved is the state — the entry
    /// list, night-look capture, consumed ids, buildings-only flag, and the counts — which previously sat as
    /// nine fields in a 4,144-line file and could be mutated from anywhere in it.
    /// </summary>
    internal sealed class MapObjectVisualRegistry
    {
        private readonly IMapObjectVisualHost host;
        private readonly List<MapObjectVisualEntry> entries = new List<MapObjectVisualEntry>();

        // Night-look dimmer state, captured lazily from the live instances on first use and invalidated
        // whenever the entry list is rebuilt.
        private readonly List<(Renderer Renderer, Color NightEmission)> nightEmissives =
            new List<(Renderer, Color)>();

        private readonly List<(Light Light, float AuthoredIntensity, bool AuthoredEnabled, float IgnitionWeight)> nightPropLights =
            new List<(Light, float, bool, float)>();

        private readonly HashSet<string> consumedObjectIds = new HashSet<string>(StringComparer.Ordinal);

        private bool nightLookCaptured;
        private MaterialPropertyBlock nightLookBlock;

        private static readonly Color DayEmissionColor = new Color(0.10768955f, 0.14818439f, 0.18867922f, 1f);
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public MapObjectVisualRegistry(IMapObjectVisualHost host)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public int VisualCount { get; private set; }
        public int ActiveVisualCount { get; private set; }

        /// <summary>
        /// Buildings-only cinematic mode. Read by the view's trap-marker pass too, which hides trap markers
        /// alongside the non-building objects this flag suppresses.
        /// </summary>
        public bool RestrictToBuildings { get; private set; }

        public void CreateVisuals(IEnumerable<HexMapObjectData> objectRefs)
        {
            if (objectRefs == null)
            {
                return;
            }

            var parent = host.MapTransform;
            foreach (var objectRef in objectRefs)
            {
                if (!objectRef.IsRuntimeVisualObject)
                {
                    continue;
                }

                // 은퇴한 서비스 오브젝트는 세우지 않는다(공작소 — ServiceObjectAvailability).
                // 🔴 트리거(MapCombatController.Services)와 <b>한 쌍</b>이다: 한쪽만 끄면
                //    「보이는데 안 되는 물건」이나 「안 보이는데 밟히는 칸」이 된다.
                if (objectRef.IsWorkshop && !ServiceObjectAvailability.WorkshopEnabled)
                {
                    continue;
                }

                // 은퇴한 이벤트 오브젝트(저주받은 인형뽑기 — EventObjectAvailability)도 같은 규칙이다.
                if (objectRef.IsCursedGachaMachine && !EventObjectAvailability.CursedGachaMachineEnabled)
                {
                    continue;
                }

                if (!host.TryResolveMapObjectPrefab(objectRef.ObjectRef, out var prefab))
                {
                    host.ReportMissingVisual(
                        $"Map object '{objectRef.ObjectId}' references missing prefab id '{objectRef.ObjectRef}'.");
                    continue;
                }

                var instance = UnityEngine.Object.Instantiate(prefab, parent);
                instance.name = $"Map_Object_{objectRef.Coord.Q}_{objectRef.Coord.R}_{objectRef.ObjectId}_{prefab.name}";
                instance.transform.localPosition =
                    host.ProjectOverlaySurface(objectRef.Coord) + Vector3.up * Mathf.Max(0f, host.MapObjectLift);
                instance.transform.localRotation = Quaternion.Euler(0f, objectRef.YawDegrees, 0f);
                instance.transform.localScale = Vector3.Scale(
                    instance.transform.localScale,
                    new Vector3(objectRef.VisualScaleX, objectRef.VisualScaleY, objectRef.VisualScaleZ));
                // 호버 판정 넓히기는 <b>툴팁이 걸린 물건</b>만(2026-09-05). 배경 건물·소품까지 넓히면
                // 뒤에 선 서비스 오브젝트의 호버를 가로챈다.
                var expandHover = objectRef.IsShop || objectRef.IsCamperVan || objectRef.IsWorkshop
                    || objectRef.IsMemoryStone || objectRef.IsTreasureChest || objectRef.IsCursedGachaMachine;
                var controller = MapObjectVisualController.Ensure(instance, expandHover);
                entries.Add(new MapObjectVisualEntry(objectRef, instance, controller));
                VisualCount++;
                ActiveVisualCount++;
            }
        }

        /// <summary>
        /// Recomputes the active count from the live instances. The visibility pass flips objects directly via
        /// <see cref="MapObjectVisualEntry.SetActive"/>, so the counter is re-derived rather than tracked there.
        /// </summary>
        public void RecountActive()
        {
            ActiveVisualCount = entries.Count(entry => entry.Root != null && entry.Root.activeSelf);
        }

        /// <summary>
        /// Presses every placed map object's night dressing toward its day state (0 = day, 1 = authored
        /// night) for the stage-intro trailer: window emission lerps to the shared day emission constant
        /// and embedded PropLight lights switch off and on. Lights are never dimmed — ramping their
        /// intensity read as wrong brightness in playtest, so each light instead stays at its authored
        /// intensity and simply flips off for the day phase, snapping back on once the weight passes its
        /// own seeded ignition threshold (city lights coming on one by one). Presentation-only —
        /// materials are never mutated (emission rides a MaterialPropertyBlock, read-modify-write so
        /// MapObjectFadeTarget's _BaseColor fade coexists) and lights restore their authored values at
        /// weight 1. Callers must end on weight 1 (the cinematic's teardown does) so gameplay always sees
        /// the authored night.
        /// </summary>
        public void SetNightLookWeight(float weight)
        {
            if (!nightLookCaptured)
            {
                CaptureNightLook();
            }

            weight = Mathf.Clamp01(weight);
            nightLookBlock ??= new MaterialPropertyBlock();
            foreach (var (renderer, nightEmission) in nightEmissives)
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(nightLookBlock);
                nightLookBlock.SetColor(EmissionColorId, Color.Lerp(DayEmissionColor, nightEmission, weight));
                renderer.SetPropertyBlock(nightLookBlock);
            }

            foreach (var (light, authoredIntensity, authoredEnabled, ignitionWeight) in nightPropLights)
            {
                if (light == null)
                {
                    continue;
                }

                // Authored intensity is restored defensively (never scaled) so nothing survives from the
                // old ramp-the-intensity behaviour; the only thing the weight drives is on/off.
                if (!Mathf.Approximately(light.intensity, authoredIntensity))
                {
                    light.intensity = authoredIntensity;
                }

                // Off through the day phase (also saves their culling cost), then discrete per-light
                // ignition as the weight climbs. IgnitionWeight tops out below 1, so weight 1 always
                // lands exactly on the authored enabled state.
                light.enabled = authoredEnabled && weight >= ignitionWeight;
            }
        }

        // Snapshots the authored night state from the live instances: every renderer that carries an
        // emission color (night window glow) and every PropLight-marked Light with its authored
        // intensity/enabled. Values are read from sharedMaterial before any fade has had a chance to
        // instantiate copies, but material copies preserve emission, so late capture stays correct too.
        private void CaptureNightLook()
        {
            nightLookCaptured = true;
            nightEmissives.Clear();
            nightPropLights.Clear();

            foreach (var entry in entries)
            {
                if (entry.Root == null)
                {
                    continue;
                }

                foreach (var renderer in entry.Root.GetComponentsInChildren<Renderer>(includeInactive: true))
                {
                    var material = renderer != null ? renderer.sharedMaterial : null;
                    if (material != null && material.HasProperty(EmissionColorId))
                    {
                        nightEmissives.Add((renderer, material.GetColor(EmissionColorId)));
                    }
                }

                foreach (var propLight in entry.Root.GetComponentsInChildren<PropLight>(includeInactive: true))
                {
                    var light = propLight != null ? propLight.GetComponent<Light>() : null;
                    if (light != null)
                    {
                        nightPropLights.Add(
                            (light, light.intensity, light.enabled, ResolveLightIgnitionWeight(nightPropLights.Count)));
                    }
                }
            }
        }

        // Spreads each PropLight's ignition point over the back half of the night ramp so the city lights
        // come on a few at a time instead of all at once. Deterministic from the capture index (capture
        // order follows map authoring order), so a stage always lights up the same way; the range stays
        // strictly inside (0, 1) so weight 0 is fully dark and weight 1 is fully authored.
        private static float ResolveLightIgnitionWeight(int captureIndex)
        {
            // Cheap integer hash (Knuth multiplicative) — index order must not read as a sweep.
            var hashed = unchecked((uint)captureIndex * 2654435761u);
            var normalized = (hashed % 1000u) / 999f;
            return Mathf.Lerp(0.3f, 0.9f, normalized);
        }

        public void InvalidateNightLook()
        {
            nightLookCaptured = false;
            nightEmissives.Clear();
            nightPropLights.Clear();
        }

        public void RefreshFade(Camera camera, IEnumerable<AtlasTilePresentationView.MapObjectOcclusionTarget> actorTargets)
        {
            var targets = actorTargets == null
                ? Array.Empty<AtlasTilePresentationView.MapObjectOcclusionTarget>()
                : actorTargets.Where(target => target.IsValid).ToArray();
            var mapTransform = host.MapTransform;
            foreach (var entry in entries)
            {
                entry.RefreshFade(camera, mapTransform, targets);
            }
        }

        public void RefreshHoverRay(Ray ray)
        {
            var hoveredController = ResolveHoveredController(ray);
            foreach (var entry in entries)
            {
                entry.SetPointerHovered(entry.Controller == hoveredController);
            }
        }

        public void ClearHover()
        {
            foreach (var entry in entries)
            {
                entry.SetPointerHovered(false);
            }
        }

        public bool TryRaycast(Ray ray, out HexMapObjectData objectData)
        {
            objectData = default;
            var controller = ResolveHoveredController(ray);
            if (controller == null)
            {
                return false;
            }

            var entry = entries.FirstOrDefault(candidate => candidate.Controller == controller);
            if (entry == null)
            {
                return false;
            }

            objectData = entry.Data;
            return true;
        }

        public bool SetVisualActive(string objectId, bool active)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return false;
            }

            var changed = false;
            foreach (var entry in entries)
            {
                if (entry == null ||
                    !string.Equals(entry.Data.ObjectId, objectId.Trim(), StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.SetActive(active))
                {
                    ActiveVisualCount += active ? 1 : -1;
                    ActiveVisualCount = Mathf.Clamp(ActiveVisualCount, 0, VisualCount);
                    changed = true;
                }
            }

            return changed;
        }

        // Restricts visible map objects to buildings (and the memory stone), hiding monster spawn props,
        // treasure chests, and trap markers. Re-applies immediately. Used by the victory cinematic.
        public void SetBuildingsOnly(bool enabled)
        {
            if (RestrictToBuildings == enabled)
            {
                return;
            }

            RestrictToBuildings = enabled;
            ApplyVisibilityStates();
        }

        public void Hide(IEnumerable<string> objectIds)
        {
            if (objectIds == null)
            {
                return;
            }

            foreach (var objectId in objectIds)
            {
                Consume(objectId);
            }
        }

        /// <summary>
        /// Permanently hides a map object for the current render and records it as consumed so the
        /// visibility refresh cannot re-activate it. Use this when an interactable (e.g. a treasure
        /// chest) is claimed and should disappear for the rest of the encounter.
        /// </summary>
        public void Consume(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return;
            }

            consumedObjectIds.Add(objectId.Trim());
            SetVisualActive(objectId, false);
        }

        /// <summary>
        /// 소비 표식을 되돌린다(#20 서비스 디버그 전용). 시각을 즉시 켜고, 이후의 가시성 갱신은
        /// ApplyVisibilityStates가 평소 규칙대로 관리한다.
        /// </summary>
        public void RestoreConsumed(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return;
            }

            consumedObjectIds.Remove(objectId.Trim());
            SetVisualActive(objectId, true);
        }

        public void ApplyVisibilityStates()
        {
            foreach (var entry in entries)
            {
                if (entry.Root == null)
                {
                    continue;
                }

                // Consumed objects (claimed treasure chests, etc.) must never be re-shown by the
                // visibility pass, even on a fully revealed tile.
                if (consumedObjectIds.Contains(entry.Data.ObjectId))
                {
                    entry.SetActive(false);
                    continue;
                }

                // Buildings-only mode (e.g. the victory cinematic) hides every non-building map object
                // (treasure chests, monster spawn props, etc.); the memory stone is kept as the focal point.
                if (RestrictToBuildings && !entry.IsBuilding && !entry.IsMemoryStone)
                {
                    entry.SetActive(false);
                    continue;
                }

                var visible = ShouldShowForVisibility(entry, out var bestVisibility);
                entry.Root.SetActive(visible);
                entry.ApplyVisibilityDarkening(
                    visible && !entry.IgnoresVisibilityDarkening && bestVisibility != HexCellVisibility.Revealed);
            }
        }

        private bool ShouldShowForVisibility(MapObjectVisualEntry entry, out HexCellVisibility bestVisibility)
        {
            bestVisibility = HexCellVisibility.Unknown;
            var hasExistingFootprintCell = false;
            var hasHintedFootprintCell = false;

            foreach (var coord in entry.Data.OccupiedCoords)
            {
                if (!host.TryGetVisibilitySafeInfo(coord, out var safeInfo) || !safeInfo.Exists)
                {
                    continue;
                }

                hasExistingFootprintCell = true;
                if (safeInfo.Visibility == HexCellVisibility.Revealed)
                {
                    bestVisibility = HexCellVisibility.Revealed;
                    return true;
                }

                if (safeInfo.Visibility == HexCellVisibility.Hinted)
                {
                    hasHintedFootprintCell = true;
                }
            }

            if (hasHintedFootprintCell)
            {
                // Hinted = previously explored but currently out of sight. Keep everything that
                // was discovered there visible through the fog (including interactables) so the
                // player retains a memory of what they found.
                bestVisibility = HexCellVisibility.Hinted;
                return true;
            }

            // 미탐색(Unknown) 칸에서도 살아남는 것들. 건물·기억석은 랜드마크라 처음부터 보이고,
            // 서비스 오브젝트(잡화점·캠핑카)는 2026-09-01 사용자 확정으로 여기 합류했다 — footprint가
            // 1칸으로 줄어 우연히 밟을 확률이 떨어진 만큼, 멀리서 보고 "일부러 찾아가는" 대상이 되어야
            // 측면 배치가 성립한다.
            // 🔴 이건 "보이느냐"만 정한다. "또렷하냐"는 IgnoresVisibilityDarkening이 따로 정하며,
            //    서비스는 기억석과 달리 거기에 넣지 않는다(확정: 보이되 어둡게). 둘을 한 덩어리로
            //    착각해 함께 넓히면 확정과 어긋난다.
            return hasExistingFootprintCell && entry.Data.IsVisibleInUnexploredFog;
        }

        /// <summary>
        /// Top of the tallest active renderer on the object at <paramref name="coord"/>, in the map's local
        /// space. Particle and trail renderers are skipped because their bounds swell with live particles and
        /// would push an anchored label sky-high.
        /// </summary>
        public bool TryGetTopLocalY(HexCoord coord, out float topLocalY)
        {
            topLocalY = default;
            MapObjectVisualEntry match = null;
            foreach (var entry in entries)
            {
                if (entry?.Root == null || !entry.Root.activeInHierarchy)
                {
                    continue;
                }

                if (entry.Coord.Equals(coord))
                {
                    match = entry;
                    break;
                }
            }

            if (match == null)
            {
                return false;
            }

            var hasBounds = false;
            var bounds = default(Bounds);
            foreach (var renderer in match.Root.GetComponentsInChildren<Renderer>(includeInactive: false))
            {
                if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer || renderer is TrailRenderer)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                return false;
            }

            var worldTop = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            topLocalY = host.MapTransform.InverseTransformPoint(worldTop).y;
            return true;
        }

        /// <summary>
        /// Destroys every spawned instance through the host's destroy helper (which picks Destroy vs
        /// DestroyImmediate for edit mode), then resets all registry state to empty.
        /// </summary>
        public void DestroyAllAndReset(Action<GameObject> destroyVisualRoot)
        {
            foreach (var entry in entries)
            {
                if (entry.Root != null)
                {
                    destroyVisualRoot(entry.Root);
                }
            }

            entries.Clear();
            InvalidateNightLook();
            consumedObjectIds.Clear();
            VisualCount = 0;
            ActiveVisualCount = 0;
        }

        private static MapObjectVisualController ResolveHoveredController(Ray ray)
        {
            var hits = Physics.RaycastAll(ray, 500f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0)
            {
                return null;
            }

            MapObjectVisualController bestController = null;
            var bestDistance = float.MaxValue;
            foreach (var hit in hits)
            {
                var controller = hit.collider != null ? hit.collider.GetComponentInParent<MapObjectVisualController>() : null;
                if (controller == null || !controller.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    bestController = controller;
                }
            }

            return bestController;
        }

        private sealed class MapObjectVisualEntry
        {
            private const float OcclusionEpsilon = 0.025f;

            public MapObjectVisualEntry(HexMapObjectData data, GameObject root, MapObjectVisualController controller)
            {
                Data = data;
                Coord = data.Coord;
                IsBuilding = data.IsBuilding;
                IsMemoryStone = data.IsMemoryStone;
                Interactable = data.Interactable;
                Root = root;
                Controller = controller;
            }

            public HexMapObjectData Data { get; }
            public HexCoord Coord { get; }
            public bool IsBuilding { get; }
            public bool IsMemoryStone { get; }

            /// <summary>
            /// 어둡게 깔리는 것을 면제받는가. 규칙 자체는 순수 계층(<see cref="HexMapObjectData"/>)에
            /// 있다 — 「보이느냐」(IsVisibleInUnexploredFog)와 짝이 아니라는 계약을 한 곳에서 지키려고
            /// 둘을 나란히 두었다.
            /// </summary>
            public bool IgnoresVisibilityDarkening => Data.IgnoresVisibilityDarkening;
            public bool Interactable { get; }
            public GameObject Root { get; }
            public MapObjectVisualController Controller { get; }

            public void SetPointerHovered(bool hovered)
            {
                if (Controller != null)
                {
                    Controller.SetPointerHovered(hovered);
                }
            }

            public bool SetActive(bool active)
            {
                if (Root == null || Root.activeSelf == active)
                {
                    return false;
                }

                Root.SetActive(active);
                if (!active)
                {
                    SetPointerHovered(false);
                }

                return true;
            }

            public void RefreshFade(
                Camera camera,
                Transform mapTransform,
                IReadOnlyList<AtlasTilePresentationView.MapObjectOcclusionTarget> actorTargets)
            {
                if (Root == null || Controller == null)
                {
                    return;
                }

                var fadeForOcclusion = ShouldFadeForActorOcclusion(camera, mapTransform, actorTargets);
                Controller.ApplyOcclusionFade(fadeForOcclusion);
            }

            public void ApplyVisibilityDarkening(bool darken)
            {
                if (Controller != null)
                {
                    Controller.ApplyVisibilityDarkening(darken);
                }
            }

            private bool ShouldFadeForActorOcclusion(
                Camera camera,
                Transform mapTransform,
                IReadOnlyList<AtlasTilePresentationView.MapObjectOcclusionTarget> actorTargets)
            {
                if (camera == null || mapTransform == null || actorTargets == null || actorTargets.Count == 0)
                {
                    return false;
                }

                if (!TryResolveRendererBounds(out var bounds))
                {
                    return false;
                }

                foreach (var target in actorTargets)
                {
                    if (Coord == target.Coord)
                    {
                        continue;
                    }

                    var actorWorld = mapTransform.TransformPoint(target.LocalPosition);
                    if (BoundsOccludesActor(camera, bounds, actorWorld))
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool TryResolveRendererBounds(out Bounds bounds)
            {
                bounds = default;
                if (Root == null)
                {
                    return false;
                }

                var renderers = Root.GetComponentsInChildren<Renderer>(includeInactive: false);
                var hasBounds = false;
                foreach (var renderer in renderers)
                {
                    if (renderer == null || !renderer.enabled)
                    {
                        continue;
                    }

                    if (!hasBounds)
                    {
                        bounds = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }

                return hasBounds;
            }

            private static bool BoundsOccludesActor(Camera camera, Bounds objectBounds, Vector3 actorWorld)
            {
                if (camera == null)
                {
                    return false;
                }

                var direction = camera.orthographic
                    ? camera.transform.forward
                    : actorWorld - camera.transform.position;
                if (direction.sqrMagnitude <= 0.000001f)
                {
                    return false;
                }

                var normalizedDirection = direction.normalized;
                var distanceToActor = camera.orthographic
                    ? Vector3.Dot(actorWorld - camera.transform.position, normalizedDirection)
                    : direction.magnitude;
                if (distanceToActor <= OcclusionEpsilon)
                {
                    return false;
                }

                var rayOrigin = camera.orthographic
                    ? actorWorld - normalizedDirection * distanceToActor
                    : camera.transform.position;
                var ray = new Ray(rayOrigin, normalizedDirection);
                if (!objectBounds.IntersectRay(ray, out var hitDistance))
                {
                    return false;
                }

                return hitDistance > OcclusionEpsilon && hitDistance < distanceToActor - OcclusionEpsilon;
            }
        }
    }
}
