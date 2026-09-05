using System;
﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Timeline;
using SeoulPlayup.Combat.Unity.Tutorial;
using SeoulPlayup.Combat.Unity.Presentation;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using static SeoulPlayup.Combat.Unity.CombatCameraController;
using Cinemachine;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace SeoulPlayup.Combat.Unity
{
    public sealed partial class MapCombatController
    {
        // Also closed during the stage intro: the cinematic turns featured monsters to face the camera, and
        // this per-frame pass would otherwise snap them straight back to their locked intent direction
        // (isSequencePlaying stays false through the intro). Same gating pattern the intro already uses for
        // the tile overlay sources in RefreshMapVisibilityAndHighlights.
        // Also closed while a trailer take films: the showcase beats turn monsters to face the camera by
        // hand, and intent facing would fight them frame by frame.
        internal bool ShouldUpdateMonsterIntentFacing => State != null && !isSequencePlaying && !IsCinematicViewActive;

        private HexMapData LoadMap()
        {
            if (configuredMap != null && configuredMap.Count > 0)
            {
                selectedBoardEvidenceText = "Configured test map selected; no board source purpose.";
                return configuredMap;
            }

            if (sparseSource != null)
            {
                selectedBoardName = sparseSource.name;
                selectedBoardPurpose = sparseSource.BoardPurpose;
                selectedBoardEvidenceText = FormatBoardSelectionEvidence(sparseSource);

                if (expectedBoardPurpose != HexMapPurpose.Unspecified && sparseSource.BoardPurpose != expectedBoardPurpose)
                {
                    Debug.LogError($"M2 map combat integration selected board '{sparseSource.name}' has purpose {sparseSource.BoardPurpose}, expected {expectedBoardPurpose}.", sparseSource);
                    return null;
                }

                if (sparseSource.TryToHexMapData(out var sparseMap, out var sparseError))
                    return ApplyPlacementRandomizationIfActive(sparseMap);

                Debug.LogError($"M2 map combat integration assigned sparse source is invalid: {sparseError}", sparseSource);
                return null;
            }


            Debug.LogError("MapCombatController requires a configured test map or HexSparseMapAuthoringSource.", this);
            return null;
        }

        /// <summary>
        /// 배치 랜덤화 후처리(placement-randomization-plan §2-1). 전투 진입 경로에서만 걸린다 —
        /// 룩뎁·타일 프리뷰·에디터 검증은 저작 원본을 그대로 쓴다. 시드가 없으면(구세이브 재개,
        /// 랜덤화 off) 저작 원본. 실패는 저작 원본 폴백 + 경고 — 랜덤화가 게임을 깨는 일은 없다.
        /// </summary>
        private HexMapData ApplyPlacementRandomizationIfActive(HexMapData baseMap)
        {
            if (baseMap == null || !placementRandomizationEnabled || !hasPlacementSeed)
                return baseMap;

            // P1 우선: 스테이지에 활성 프로파일(stage_randomization*.csv)이 있으면 풀+위협 예산
            // 추첨. 프로파일이 켜져 있는데 실패하면 P0로 갈아타지 않고 저작 원본 폴백(§4 계약 —
            // 결정성 해석이 갈라지면 안 된다).
            if (StageRandomizationProfileSource.TryLoadProfile(placementStageId, out var profile, out var profileError))
            {
                // 프로파일 행이 있는데 enabled=FALSE면 「이 스테이지는 랜덤화하지 않는다」다 — P0 슬롯 셔플로
                // 갈아타지 않고 저작 원본을 쓴다(2026-09-05 #33). P0는 프로파일 행이 아예 없는 스테이지의 경로다.
                if (!profile.Enabled)
                {
                    Debug.Log($"Placement profile for stage '{placementStageId}' is disabled; using the authored layout.", this);
                    return baseMap;
                }

                if (profile.Enabled)
                {
                    if (HexMapPlacementRandomization.TryApplyProfile(
                            sparseSource, baseMap, placementSeed, profile, out var profiledMap, out var profileEvidence,
                            CollectStealthMonsterIds(),
                            CollectMonsterBodyOffsets()))
                    {
                        LatchEliteStatMultipliers(profile);
                        Debug.Log(profileEvidence, this);
                        return profiledMap;
                    }

                    Debug.LogWarning($"Placement randomization fell back to the authored layout: {profileEvidence}", this);
                    return baseMap;
                }
            }
            else if (!string.IsNullOrWhiteSpace(placementStageId))
            {
                Debug.Log($"Placement profile unavailable for stage '{placementStageId}' ({profileError}); using P0 slot shuffle.", this);
            }

            if (!HexMapPlacementRandomization.TryApply(
                    sparseSource, baseMap, placementSeed, PlacementRandomizationConfig.Default,
                    out var randomizedMap, out var evidence))
            {
                Debug.LogWarning($"Placement randomization fell back to the authored layout: {evidence}", this);
                return baseMap;
            }

            Debug.Log(evidence, this);
            return randomizedMap;
        }

        // 엘리트 강화 배율(#12). 저작면은 stage_randomization.csv이고 규칙층은 CombatConfig로 받는다 —
        // 맵 빌드와 전투 설정이 별도 경로라 여기서 래치해 CreateCombatConfig가 집어 간다.
        // 프로파일이 없거나 랜덤화가 꺼진 판은 100(배율 없음) 그대로다.
        private int eliteHpPercent = 100;
        private int eliteDamagePercent = 100;

        private void LatchEliteStatMultipliers(StageRandomizationProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            eliteHpPercent = profile.EliteHpPercent;
            eliteDamagePercent = profile.EliteDamagePercent;
        }

        private void ClearBoardSelectionEvidence()
        {
            selectedBoardPurpose = HexMapPurpose.Unspecified;
            selectedBoardName = string.Empty;
            selectedBoardEvidenceText = string.Empty;
        }

        private static string FormatBoardSelectionEvidence(HexSparseMapAuthoringSource source)
        {
            return source == null
                ? string.Empty
                : $"Sparse {source.name} purpose {source.BoardPurpose} cells={source.CellCount} object_spawns=[{string.Join(",", source.ObjectRefs.Where(objectRef => objectRef != null && objectRef.IsMonsterSpawn).Select(objectRef => objectRef.ObjectId))}]";
        }

        private (HexCoord player, HexCoord enemy) ResolveStartingCoords(HexMapData map)
        {
            var walkable = map.AllCells.Where(cell => IsWalkable(map, cell.Coord)).Select(cell => cell.Coord).ToList();
            if (walkable.Count == 0)
            {
                return (playerStart, enemyStart == playerStart ? new HexCoord(playerStart.Q + 1, playerStart.R) : enemyStart);
            }

            var player = TryFindPlayerSpawnObjectCoord(map, out var playerSpawnObject)
                ? playerSpawnObject
                : IsWalkable(map, playerStart) ? playerStart : walkable[0];
            var enemy = TryFindWalkableEventCoord(map, MapAuthoringConventions.EnemySpawnEventId, out var enemySpawn) && enemySpawn != player
                ? enemySpawn
                : IsWalkable(map, enemyStart) && enemyStart != player
                    ? enemyStart
                    : walkable.OrderByDescending(coord => coord.DistanceTo(player)).ThenBy(coord => coord.Q).ThenBy(coord => coord.R).First();

            if (enemy == player && walkable.Count > 1)
            {
                enemy = walkable.First(coord => coord != player);
            }

            return (player, enemy);
        }

        private CombatConfig CreateCombatConfig()
        {
            var profile = ResolvePlayerCombatProfile();
            if (testPlayerVisionRangeOverride.HasValue)
            {
                profile = new PlayerCombatProfile(
                    profile.ProfileId,
                    profile.DisplayName,
                    profile.MaxHp,
                    profile.MovePoints,
                    profile.AttackRange,
                    profile.AttackDamage,
                    profile.DefenseBlock,
                    profile.ActionBudget,
                    profile.MovementHandSize,
                    profile.ActionHandSize,
                    testPlayerVisionRangeOverride.Value,
                    profile.Status,
                    profile.DesignerNote);
            }

            return CombatConfig.FromPlayerProfile(
                profile, enemyHp, enemyChaseRange, 1, enemyAttackDamage, enemyDisengageRange,
                eliteHpPercent, eliteDamagePercent);
        }

        private PlayerCombatProfile ResolvePlayerCombatProfile()
        {
            var profileId = string.IsNullOrWhiteSpace(playerCombatProfileId)
                ? PlayerCombatProfileCatalog.DefaultProfileId
                : playerCombatProfileId.Trim();

            try
            {
                var catalog = catalogTextAssetSource != null && catalogTextAssetSource.HasPlayerCombatProfiles
                    ? catalogTextAssetSource.CreatePlayerCombatProfileCatalog()
                    : CombatCatalogFactory.CreatePlayerCombatProfileCatalog();
                if (catalog.TryGetProfile(profileId, out var profile))
                {
                    return profile;
                }

                Debug.LogWarning($"Player combat profile '{profileId}' was not found in {CombatCsvPaths.PlayerCombatProfilesCsv}; using code fallback.", this);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Failed to load player combat profiles from {CombatCsvPaths.PlayerCombatProfilesCsv}: {ex.Message}. Using code fallback.", this);
            }

            return PlayerCombatProfile.Default;
        }

        /// <summary>
        /// 「은신」 특성을 가진 몬스터 id — 배치 bans(<c>monster:stealth</c>)의 표적이다. 특성 정본이
        /// 전투 카탈로그라 맵 레이어가 직접 알 수 없어, 아는 쪽인 여기서 뽑아 넘긴다(요괴 §4-1).
        /// 카탈로그를 못 만들면 빈 목록 — 배치가 규칙 하나 때문에 실패하는 일은 없어야 한다.
        /// </summary>
        private IReadOnlyCollection<string> CollectStealthMonsterIds()
        {
            try
            {
                var catalog = CreateMonsterCatalog(CreateCombatConfig());
                return catalog?.Entries
                    .Where(entry => entry.HasStealthTrait)
                    .Select(entry => entry.Id)
                    .ToList() ?? (IReadOnlyCollection<string>)System.Array.Empty<string>();
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"은신 태그 수집 실패 — bans의 monster:stealth 규칙 없이 배치한다: {exception.Message}", this);
                return System.Array.Empty<string>();
            }
        }

        /// <summary>
        /// 몸이 여러 칸인 몬스터의 점유 오프셋(2026-09-05). 배치 랜덤화가 「이 슬롯에 몸이 들어가는가」를
        /// 물을 때 쓴다 — 이동 판정은 원래부터 몸통 전 칸을 보는데 <b>배치만</b> 한 칸으로 검사하고 있어서
        /// 삼각형 정예가 물 타일에 몸을 얹은 채 판이 시작됐다(200시드 실측 17%).
        ///
        /// <para>은신 태그와 같은 규약이다: 몸 형상 정본은 전투 카탈로그라 맵 레이어가 모른다 —
        /// 아는 쪽이 데이터로 넘긴다. 실패하면 빈 목록(제약 없음)으로 물러선다 — 배치가 규칙 하나 때문에
        /// 실패하는 일은 없어야 한다.</para>
        /// </summary>
        private IReadOnlyDictionary<string, IReadOnlyList<HexCoord>> CollectMonsterBodyOffsets()
        {
            try
            {
                var catalog = CreateMonsterCatalog(CreateCombatConfig());
                if (catalog == null)
                {
                    return null;
                }

                var byId = new Dictionary<string, IReadOnlyList<HexCoord>>(System.StringComparer.Ordinal);
                foreach (var entry in catalog.Entries)
                {
                    var offsets = MonsterFootprints.OffsetsOf(entry.FootprintShape);
                    if (offsets.Count > 1 && !byId.ContainsKey(entry.Id))
                    {
                        byId.Add(entry.Id, offsets);
                    }
                }

                return byId.Count == 0 ? null : byId;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"몸 형상 수집 실패 — 몸 크기를 보지 않고 배치한다: {exception.Message}", this);
                return null;
            }
        }

        private MonsterCatalogDefinition CreateMonsterCatalog(CombatConfig config)
        {
            return catalogTextAssetSource != null && catalogTextAssetSource.HasMonsterCatalog
                ? catalogTextAssetSource.CreateMonsterCatalog()
                : CombatState.CreateMonsterCatalog(config);
        }

        /// <summary>
        /// 보스 확장 데이터. TextAsset이 배선되어 있으면 그것을 쓰고(플레이어 빌드에서는 <c>Assets/</c> CSV가
        /// 디스크에 없다), 에디터에서는 소스 CSV로 폴백한다 — <see cref="CreateMonsterCatalog"/>와 같은 체인.
        /// 플레이어 빌드에서 미배선이면 보스 페이즈 없이 진행한다(<see cref="BossCatalogDefinition.Empty"/>).
        /// </summary>
        private BossCatalogDefinition CreateBossCatalog()
        {
            if (catalogTextAssetSource != null && catalogTextAssetSource.HasBossCatalog)
            {
                return catalogTextAssetSource.CreateBossCatalog();
            }

#if UNITY_EDITOR
            return CombatCatalogFactory.CreateBossCatalog();
#else
            return BossCatalogDefinition.Empty;
#endif
        }

        internal static bool TryFindPlayerSpawnObjectCoord(HexMapData map, out HexCoord coord)
        {
            foreach (var objectRef in map.ObjectRefs
                .Where(objectRef => objectRef.IsPlayerSpawn && IsWalkable(map, objectRef.Coord))
                .OrderBy(objectRef => objectRef.Coord.Q)
                .ThenBy(objectRef => objectRef.Coord.R))
            {
                coord = objectRef.Coord;
                return true;
            }

            coord = default;
            return false;
        }

        private static bool TryFindWalkableEventCoord(HexMapData map, string eventId, out HexCoord coord)
        {
            foreach (var match in map.AllCells
                .Where(cell => IsWalkable(map, cell.Coord) && string.Equals(cell.EventId, eventId, System.StringComparison.OrdinalIgnoreCase))
                .OrderBy(cell => cell.Coord.Q)
                .ThenBy(cell => cell.Coord.R))
            {
                coord = match.Coord;
                return true;
            }

            coord = default;
            return false;
        }

        private static bool IsWalkable(HexMapData map, HexCoord coord)
        {
            return map.TryGetCell(coord, out var cell) && cell.BaseWalkable && !map.HasMovementBlockingObject(coord);
        }

        private bool HasSelected3DView()
        {
            return atlasTilePresentationView != null;
        }

        private Transform GetSelected3DViewTransform()
        {
            return atlasTilePresentationView != null ? atlasTilePresentationView.transform : null;
        }

        private void RenderSelected3DView(HexMapData map)
        {
            if (atlasTilePresentationView != null)
            {
                atlasTilePresentationView.Render(map);
                // Mark already-claimed objects consumed before the visibility pass so it cannot
                // re-activate them on revealed tiles (Render() rebuilds them active from map data).
                atlasTilePresentationView.HideMapObjectVisuals(State?.ClaimedEventObjectIds);
                atlasTilePresentationView.ApplyVisibility(GetViewVisibilitySafeCellInfo);
                Physics.SyncTransforms();
            }
        }

        private bool TrySelectedScreenToHex(Vector3 screenPosition, out HexCoord coord)
        {
            coord = default;
            if (prototype3DCamera == null || atlasTilePresentationView == null)
            {
                return false;
            }

            if (atlasTilePresentationView.TryScreenToHex(prototype3DCamera, screenPosition, out coord))
            {
                return true;
            }

            return TryNearestProjectedScreenHex(screenPosition, out coord);
        }

        private bool TryNearestProjectedScreenHex(Vector3 screenPosition, out HexCoord coord)
        {
            coord = default;
            if (LoadedMap == null || prototype3DCamera == null || atlasTilePresentationView == null)
            {
                return false;
            }

            const float maxProjectionPickPixels = 48f;
            var bestDistance = maxProjectionPickPixels * maxProjectionPickPixels;
            var found = false;
            foreach (var cell in LoadedMap.AllCells)
            {
                var world = atlasTilePresentationView.transform.TransformPoint(atlasTilePresentationView.ProjectOverlaySurface(cell.Coord));
                var projected = prototype3DCamera.WorldToScreenPoint(world);
                if (projected.z <= 0f)
                {
                    continue;
                }

                var distance = ((Vector2)projected - (Vector2)screenPosition).sqrMagnitude;
                if (distance > bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                coord = cell.Coord;
                found = true;
            }

            return found;
        }

        private bool TryProjectSelected3DView(HexCoord coord, out Vector3 position)
        {
            if (atlasTilePresentationView != null)
            {
                position = atlasTilePresentationView.ProjectOverlaySurface(coord);
                return true;
            }
            position = default;
            return false;
        }

        private void SetSelectedPlayerPosition(HexCoord coord)
        {
            atlasTilePresentationView?.SetPlayerPosition(coord);
        }

        private void RefreshMapObjectFade()
        {
            if (State == null || atlasTilePresentationView == null)
            {
                return;
            }

            atlasTilePresentationView.RefreshMapObjectFade(prototype3DCamera, BuildMapObjectOcclusionTargets());
        }

        private IEnumerable<AtlasTilePresentationView.MapObjectOcclusionTarget> BuildMapObjectOcclusionTargets()
        {
            if (State == null || atlasTilePresentationView == null)
            {
                yield break;
            }

            yield return new AtlasTilePresentationView.MapObjectOcclusionTarget(
                State.PlayerCoord,
                atlasTilePresentationView.PlayerMarkerLocalPosition);

            foreach (var monster in State.Monsters.Where(monster => !monster.IsDead && IsMonsterVisibleForPresentation(monster)))
            {
                if (TryProjectSelected3DView(monster.Coord, out var projected))
                {
                    yield return new AtlasTilePresentationView.MapObjectOcclusionTarget(
                        monster.Coord,
                        new Vector3(projected.x, projected.y + enemyMarkerHeightOffset, projected.z));
                }
            }
        }

        private void RefreshMapObjectHover()
        {
            // While a cinematic plays (stage intro or a trailer take), suppress all map-object hover so
            // objects do not turn transparent and no hover tooltips pop up over the "film"; clear any
            // current hover/tooltips.
            if (IsCinematicViewActive)
            {
                atlasTilePresentationView?.ClearMapObjectHover();
                HideAllHoverTooltips();
                return;
            }

            if (atlasTilePresentationView == null || prototype3DCamera == null)
            {
                atlasTilePresentationView?.ClearMapObjectHover();
                return;
            }

#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null)
            {
                atlasTilePresentationView.ClearMapObjectHover();
                return;
            }

            atlasTilePresentationView.RefreshMapObjectHover(prototype3DCamera, mouse.position.ReadValue());
#else
            atlasTilePresentationView.RefreshMapObjectHover(prototype3DCamera, Input.mousePosition);
#endif
        }

        private void RecreateOverlayPresenter()
        {
            overlayPresenter = null;
            activeOverlayPresenterView = null;
            activeOverlayRendererBackend = default;
            EnsureOverlayPresenter();
        }

        private string FormatOverlayRendererStatus()
        {
            return CombatOverlayCoordinator.FormatOverlayRendererStatus(overlayRendererBackend, ActiveOverlayRendererBackend);
        }

        private CombatMapOverlayPresenter EnsureOverlayPresenter()
        {
            var resolvedBackend = ResolveOverlayRendererBackend();
            if (overlayPresenter == null ||
                activeOverlayPresenterView != atlasTilePresentationView ||
                activeOverlayRendererBackend != resolvedBackend)
            {
                overlayPresenter = CreateOverlayPresenter(resolvedBackend);
                activeOverlayPresenterView = atlasTilePresentationView;
                activeOverlayRendererBackend = resolvedBackend;
            }

            return overlayPresenter;
        }

        private CombatOverlayRendererBackend ResolveOverlayRendererBackend()
        {
            return CombatOverlayCoordinator.ResolveOverlayRendererBackend(overlayRendererBackend);
        }

        private CombatMapOverlayPresenter CreateOverlayPresenter(CombatOverlayRendererBackend backend)
        {
            var iconRenderer = EnsureStatusIconOverlayRenderer();
            if (atlasTilePresentationView is IHexMapWorldProjector projector)
            {
                var renderer = EnsureBatchedOverlayRenderer(projector);
                var presenter = new CombatMapOverlayPresenter(renderer);
                presenter.SetVisibilityRenderer(new AtlasCombatOverlayRenderer(atlasTilePresentationView));
                presenter.SetIconRenderer(iconRenderer);
                return presenter;
            }

            // No presentation view yet: keep any stale batched renderer hidden and hand out a
            // presenter whose tactical/visibility calls no-op until the view arrives.
            if (batchedOverlayRenderer != null)
            {
                batchedOverlayRenderer.gameObject.SetActive(false);
            }

            var pendingPresenter = new CombatMapOverlayPresenter();
            pendingPresenter.SetIconRenderer(iconRenderer);
            return pendingPresenter;
        }

        private void InvalidateBatchedOverlayGeometry()
        {
            batchedOverlayRenderer?.InvalidateGeometry();
        }

        private BatchedMeshCombatOverlayRenderer EnsureBatchedOverlayRenderer(IHexMapWorldProjector projector)
        {
            var parent = atlasTilePresentationView != null ? atlasTilePresentationView.transform : transform;

            // Reap overlay renderers under the (persistent) presentation view that this controller
            // does not own. The renderer GameObject is parented beneath the AtlasTilePresentationView,
            // which survives map reloads / scene re-entry, but this controller can be re-created — which
            // resets batchedOverlayRenderer to null and orphans the renderer it previously parented here.
            // The orphan keeps its last enabled overlay meshes (e.g. an AttackRange highlight), which then
            // hover over the map at stale coordinates. Destroy every renderer except ours so exactly one
            // survives; a null field means all existing renderers are orphans and are removed here.
            ReapForeignOverlayRenderers(parent);

            if (batchedOverlayRenderer == null)
            {
                var root = new GameObject("Batched Combat Overlay Renderer");
                root.transform.SetParent(parent, false);
                batchedOverlayRenderer = root.AddComponent<BatchedMeshCombatOverlayRenderer>();
            }
            else if (batchedOverlayRenderer.transform.parent != parent)
            {
                batchedOverlayRenderer.transform.SetParent(parent, false);
            }

            batchedOverlayRenderer.gameObject.SetActive(true);
            batchedOverlayRenderer.Configure(projector);
            // Suppress overlays on water: the closure reads the current LoadedMap so it stays correct
            // across map reloads without re-wiring.
            batchedOverlayRenderer.SetTileFilter(IsOverlayAllowedOnTile);
            return batchedOverlayRenderer;
        }

        // Inaccessible tiles must never show a tactical overlay: water, and tiles cut off from every
        // neighbor by a 2+ height gap (2026-08-20 #4 — 몬스터 공격 형상 포함, "그리지만 명중 없음"이
        // 아니라 표시 자체 제외). 판정은 배치 필터와 같은 공유 술어(HexAccessibility)다.
        private bool IsOverlayAllowedOnTile(HexCoord coord)
        {
            var map = LoadedMap;
            if (map == null || !map.TryGetCell(coord, out _))
            {
                return true;
            }

            return HexAccessibility.IsAccessible(map, coord, HexTerrainTraits.Default);
        }

        private CombatTileFlashPresenter tileFlashPresenter;
        private readonly List<HexCoord> areaFlashCoordScratch = new List<HexCoord>();

        /// <summary>
        /// One-shot shader hex flash over an area effect's footprint, fired by the presentation
        /// bridge at the event's (impact-delay aligned) presentation moment. Replaces the legacy
        /// opaque quad tile highlights. Style and skip policy live in
        /// <see cref="CombatTileFlashStyles"/>; footprint = existing map tiles within the event
        /// radius, minus tiles the shared overlay filter rejects (water).
        /// </summary>
        public void PlayAreaEffectFlash(EffectResultEvent resultEvent)
        {
            // IsCinematicViewActive: a cinematic (the intro, or a trailer take) films a bare map — the
            // danger-hatch flash is a tactical overlay like every gated overlay source above. It also
            // outstays its welcome on film: the death slow-motion stretches the flash fade across the
            // whole frozen beat (seen as a red hatch pinned under the victim in the first cut-7 take).
            if (IsCinematicViewActive)
            {
                return;
            }

            if (!resultEvent.Center.HasValue || !CombatTileFlashStyles.TryResolve(resultEvent, out var spec))
            {
                return;
            }

            var map = LoadedMap;
            var projector = atlasTilePresentationView as IHexMapWorldProjector;
            if (map == null || projector == null)
            {
                return;
            }

            // Events that captured their exact affected footprint at rules-resolution time (monster
            // attacks carry their COMMITTED attack area) flash those tiles verbatim — never a live
            // re-derivation, which would drift once the player moves or the next intent is computed.
            // Events without an authored footprint fall back to the center disk. Shared with the
            // per-tile VFX path so flash tiles and VFX tiles cannot diverge.
            EffectAreaFootprint.Resolve(resultEvent, map, areaFlashCoordScratch);
            EnsureTileFlashPresenter(projector).Flash(areaFlashCoordScratch, spec);
        }

        /// <summary>
        /// Projects the effect's footprint (same resolution as the tile flash:
        /// <see cref="EffectAreaFootprint"/>) into world positions for per-tile VFX spawning.
        /// Returns false when no map is loaded or the footprint is empty.
        /// </summary>
        public bool TryGetAreaEffectFootprintWorldPositions(EffectResultEvent resultEvent, List<Vector3> results)
        {
            if (results == null)
            {
                return false;
            }

            results.Clear();
            var map = LoadedMap;
            if (map == null)
            {
                return false;
            }

            EffectAreaFootprint.Resolve(resultEvent, map, areaFlashCoordScratch);
            foreach (var coord in areaFlashCoordScratch)
            {
                if (TryGetTileWorldPosition(coord, out var world))
                {
                    results.Add(world);
                }
            }

            return results.Count > 0;
        }

        // Parented under the presentation view like the batched overlay renderer so both share the
        // same (identity) world frame for their world-space meshes. Individual flashes are transient
        // (<1s), so a presenter orphaned by controller re-creation is simply re-adopted here.
        private CombatTileFlashPresenter EnsureTileFlashPresenter(IHexMapWorldProjector projector)
        {
            var parent = atlasTilePresentationView != null ? atlasTilePresentationView.transform : transform;
            if (tileFlashPresenter == null)
            {
                tileFlashPresenter = parent.GetComponentInChildren<CombatTileFlashPresenter>(true);
            }

            if (tileFlashPresenter == null)
            {
                var root = new GameObject("Combat Tile Flash Presenter");
                root.transform.SetParent(parent, false);
                tileFlashPresenter = root.AddComponent<CombatTileFlashPresenter>();
            }

            tileFlashPresenter.Configure(projector);
            tileFlashPresenter.SetTileFilter(IsOverlayAllowedOnTile);
            return tileFlashPresenter;
        }

        private void ReapForeignOverlayRenderers(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            var existing = parent.GetComponentsInChildren<BatchedMeshCombatOverlayRenderer>(true);
            foreach (var renderer in existing)
            {
                if (renderer == null || renderer == batchedOverlayRenderer)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(renderer.gameObject);
                }
                else
                {
                    DestroyImmediate(renderer.gameObject);
                }
            }
        }

        private StatusIconOverlayRenderer EnsureStatusIconOverlayRenderer()
        {
            if (atlasTilePresentationView == null)
            {
                statusIconOverlayRenderer?.Configure(null);
                return statusIconOverlayRenderer;
            }

            var parent = atlasTilePresentationView.transform;
            if (statusIconOverlayRenderer == null)
            {
                statusIconOverlayRenderer = parent.GetComponentInChildren<StatusIconOverlayRenderer>(true);
            }

            if (statusIconOverlayRenderer == null)
            {
                var root = new GameObject("Combat Status Icon Overlay Renderer");
                root.transform.SetParent(parent, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                statusIconOverlayRenderer = root.AddComponent<StatusIconOverlayRenderer>();
            }
            else if (statusIconOverlayRenderer.transform.parent != parent)
            {
                statusIconOverlayRenderer.transform.SetParent(parent, false);
            }

            statusIconOverlayRenderer.transform.localPosition = Vector3.zero;
            statusIconOverlayRenderer.transform.localRotation = Quaternion.identity;
            statusIconOverlayRenderer.transform.localScale = Vector3.one;
            statusIconOverlayRenderer.Configure(atlasTilePresentationView, parent);
            statusIconOverlayRenderer.SetHoverCamera(prototype3DCamera);
            return statusIconOverlayRenderer;
        }

        private void RefreshView()
        {
            if (State == null)
            {
                StatusText = "Uninitialized";
                statusLoopVfxController?.Clear();
                fieldObjectVisualPresenter?.Clear();
                if (memoryStoneActivationLabelRoot != null)
                {
                    memoryStoneActivationLabelRoot.SetActive(false);
                }
                return;
            }

            RefreshModelStatusText();
            using (VisibilityHighlightsMarker.Auto())
            {
                RefreshMapVisibilityAndHighlights();
            }

            using (CommitPresentationMarker.Auto())
            {
                CommitPresentationFromState(immediateCamera: !Application.isPlaying);
            }

            // Reconcile after presentation commits so freshly-resolved marker anchors are current.
            StatusLoopVfx.Reconcile();
            SyncFieldObjectVisuals();

            using (HudRefreshMarker.Auto())
            {
                RefreshHudOnly();
            }
        }

        // P4 Stage 4 status-loop VFX service: owns the live loop handles and reconciles them
        // against CombatState.ActiveEffects; this controller keeps the presentation resolve,
        // visibility rules, and anchor lookup, injected as delegates.
        private CombatStatusLoopVfxController StatusLoopVfx =>
            statusLoopVfxController ??= new CombatStatusLoopVfxController(
                ResolveMovementEffectPresentation,
                () => movementEffectPresentation,
                () => State,
                IsStatusLoopMonsterVisibleForPresentation,
                (unitId, anchorKind) => TryGetCombatantVfxAnchorWorldPosition(unitId, anchorKind, out var position) ? position : (Vector3?)null,
                () => transform.position,
                unitId => TryGetCombatantFootprintDiameter(unitId, out var diameter) ? diameter : (float?)null);

        private bool IsStatusLoopMonsterVisibleForPresentation(string targetUnitId)
        {
            if (State == null || string.IsNullOrEmpty(targetUnitId))
            {
                return false;
            }

            var monster = State.Monsters.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, targetUnitId, System.StringComparison.Ordinal));
            return !string.IsNullOrEmpty(monster.Id)
                && !monster.IsDead
                && IsMonsterVisibleForPresentation(monster);
        }

        private void RefreshMapVisibilityAndHighlights()
        {
            CheckTutorialBossSighted();
            var presenter = EnsureOverlayPresenter();
            using (VisApplyMarker.Auto())
            {
                // The safe-cell info each cell resolves to is a pure function of the visibility state
                // (version) and the reveal-all debug flag (the map itself is fixed). Pass that as an
                // epoch so the view can skip its whole-map rescan on refreshes where neither changed
                // (e.g. the redundant second refresh per turn, selection-only refreshes).
                long? visibilityEpoch = State != null
                    ? (long?)(((long)State.VisibilityVersion << 1) | (revealAllMapCellsInDebugMode ? 1L : 0L))
                    : null;
                // The stage-intro finale changes per-cell reveal outside the version/flag the epoch tracks,
                // so force a full rescan each ripple step while that override is active.
                if (cinematicForcedRevealCells != null)
                {
                    visibilityEpoch = null;
                }

                presenter.ApplyVisibility(GetViewVisibilitySafeCellInfo, visibilityEpoch);
            }

            // Scatter fog particles across the fog-of-war (Unknown) tiles. No-ops unless the Unknown
            // membership moved (revision-gated), so this rides the same incremental signal as
            // ApplyVisibility. The player position is passed so the presenter's camera-bounded cap
            // keeps the fog nearest the player when the Unknown region is larger than the budget.
            if (visibilityFogPresenter != null && atlasTilePresentationView != null)
            {
                var fogCenter = TryGetPlayerWorldPosition(out var playerWorld) ? playerWorld : Vector3.zero;
                visibilityFogPresenter.SyncIfChanged(
                    atlasTilePresentationView.VisibilityFogRevision,
                    atlasTilePresentationView.UnknownCells,
                    TryGetTileWorldPosition,
                    fogCenter);
            }

            CombatOverlayPresentation presentation;
            using (VisBuildMarker.Auto())
            {
                presentation = overlayPresentationBuilder.Build(new CombatOverlayPresentationRequest(
                    State,
                    LoadedMap,
                    overlayQuery,
                    IsMoveSelectionActive,
                    SelectedTargetCardKind,
                    // IsCinematicViewActive: a cinematic (the intro, or a trailer take) plays over a bare
                    // map — no movement/action/intent tile overlays — so every overlay source is gated off
                    // while one runs.
                    FilterOverlay(showPlayerMovementOverlay && !IsCinematicViewActive, reachable.Keys),
                    FilterOverlay(showPlayerActionOverlay && !IsCinematicViewActive, GetSelectedTargetHighlightCells()),
                    revealAllMapCellsInDebugMode,
                    showPlayerMovementOverlay && !IsCinematicViewActive,
                    showPlayerActionOverlay && !IsCinematicViewActive,
                    showMonsterMoveOverlay && !isSequencePlaying && !IsCinematicViewActive,
                    showMonsterAttackOverlay && !isSequencePlaying && !IsCinematicViewActive,
                    showMonsterChaseOverlay && !isSequencePlaying && !IsCinematicViewActive,
                    hoveredMonsterId,
                    FilterOverlay((showPlayerActionOverlay || showPlayerMovementOverlay) && !IsCinematicViewActive, GetSelectedEffectAreaHighlightCells()),
                    GetVisibleBossFootprintCoords(),
                    // 뒤끝 호버(2026-09-01 #3). 시네마틱 중에는 다른 오버레이와 함께 꺼진다 —
                    // 그때는 이름표 자체가 없어 손을 얹을 배지도 없다.
                    IsCinematicViewActive ? System.Array.Empty<HexCoord>() : HoveredTraitReachCells));
            }

            using (VisApplyPresentationMarker.Auto())
            {
                presenter.ApplyPresentation(presentation);
            }

            if (clickMoveDebugModeEnabled && clickMoveDebugSelectedCoord.HasValue)
            {
                presenter.ShowDebugSelection(new[] { clickMoveDebugSelectedCoord.Value });
            }

            ApplyTutorialTileHighlight(presenter);

            using (VisPhysicsSyncMarker.Auto())
            {
                Physics.SyncTransforms();
            }
        }

        private void CommitPresentationFromState(bool immediateCamera)
        {
            if (State == null)
            {
                return;
            }

            SetSelectedPlayerPosition(State.PlayerCoord);
            UpdateEnemyMarker();
            UpdateMemoryStoneActivationLabel();
            Physics.SyncTransforms();
            RecenterGameplayCameraOnPlayer(immediate: immediateCamera);
            RefreshMapObjectFade();
        }

        private void UpdateMemoryStoneActivationLabel()
        {
            // IsCinematicViewActive: this is a world-space gameplay prompt, and a cinematic (the intro, or a
            // trailer take) films a bare map. It is NOT covered by the UI hide passes — those dim the HUD
            // canvas, while this label hangs off the 3D view — so a cinematic that happens to refresh the
            // view lit "조사 가능" over the memory stone. Cut #6's fog variants do exactly that (the fog
            // toggle refreshes), which is how it was found.
            if (State == null || IsCinematicViewActive
                || !State.HasMemoryStoneObjective || State.ObjectiveCompleted || !State.IsMemoryStoneAwakened)
            {
                if (memoryStoneActivationLabelRoot != null)
                {
                    memoryStoneActivationLabelRoot.SetActive(false);
                }

                return;
            }

            if (!State.ObjectiveTargetCoord.HasValue ||
                !TryProjectSelected3DView(State.ObjectiveTargetCoord.Value, out var projected))
            {
                if (memoryStoneActivationLabelRoot != null)
                {
                    memoryStoneActivationLabelRoot.SetActive(false);
                }

                return;
            }

            EnsureMemoryStoneActivationLabel();
            var parent = GetSelected3DViewTransform();
            if (parent != null && memoryStoneActivationLabelRoot.transform.parent != parent)
            {
                memoryStoneActivationLabelRoot.transform.SetParent(parent, false);
            }
            else if (parent == null && memoryStoneActivationLabelRoot.transform.parent != transform)
            {
                memoryStoneActivationLabelRoot.transform.SetParent(transform, false);
            }

            // Clear the stone mesh top (visual scale can lift it well past a fixed tile offset).
            var labelLocalY = projected.y + 1.25f;
            if (atlasTilePresentationView != null &&
                atlasTilePresentationView.TryGetMapObjectVisualTopLocalY(State.ObjectiveTargetCoord.Value, out var stoneTopLocalY))
            {
                labelLocalY = Mathf.Max(labelLocalY, stoneTopLocalY + 0.45f);
            }

            memoryStoneActivationLabelRoot.transform.localPosition = new Vector3(projected.x, labelLocalY, projected.z);
            memoryStoneActivationLabelRoot.SetActive(true);
            if (memoryStoneActivationLabel != null)
            {
                memoryStoneActivationLabel.text = "\uC870\uC0AC \uAC00\uB2A5";
            }
        }

        private void EnsureMemoryStoneActivationLabel()
        {
            if (memoryStoneActivationLabelRoot != null)
            {
                return;
            }

            memoryStoneActivationLabelRoot = new GameObject("MemoryStone Activation Label", typeof(RectTransform));
            memoryStoneActivationLabelRoot.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            memoryStoneActivationLabelRoot.transform.SetParent(GetSelected3DViewTransform() ?? transform, false);
            memoryStoneActivationLabelRoot.transform.localScale = Vector3.one * 0.0095f;
            memoryStoneActivationLabelRoot.AddComponent<CombatWorldSpaceBillboard>();

            var canvas = memoryStoneActivationLabelRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 120;

            var rect = memoryStoneActivationLabelRoot.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(180f, 48f);

            memoryStoneActivationLabel = memoryStoneActivationLabelRoot.AddComponent<TextMeshProUGUI>();
            KoreanFontProvider.Apply(memoryStoneActivationLabel);
            memoryStoneActivationLabel.alignment = TextAlignmentOptions.Center;
            memoryStoneActivationLabel.fontSize = 30f;
            memoryStoneActivationLabel.fontStyle = FontStyles.Bold;
            memoryStoneActivationLabel.textWrappingMode = TextWrappingModes.NoWrap;
            memoryStoneActivationLabel.raycastTarget = false;
            memoryStoneActivationLabel.color = new Color(0.25f, 1f, 0.72f, 1f);
            memoryStoneActivationLabel.text = "\uC870\uC0AC \uAC00\uB2A5";
            memoryStoneActivationLabelRoot.SetActive(false);
        }

        private HexVisibilitySafeCellInfo GetViewVisibilitySafeCellInfo(HexCoord coord)
        {
            // During the stage-intro finale a per-cell override force-reveals exactly the cells still in
            // the set; cells removed from it (in rings expanding from the memory stone) fall back to the
            // real fog state, producing the outward darkening ripple without mutating actual visibility.
            var revealAll = revealAllMapCellsInDebugMode
                || (cinematicForcedRevealCells != null && cinematicForcedRevealCells.Contains(coord));
            return visibilityPresenter.GetSafeCellInfo(State, LoadedMap, revealAll, coord);
        }

        private IEnumerable<HexCoord> GetSelectedTargetHighlightCells()
        {
            return overlayQuery.GetSelectedTargetHighlightCells(State, LoadedMap, selectionState);
        }

        private IEnumerable<HexCoord> GetSelectedEffectAreaHighlightCells()
        {
            return overlayQuery.GetSelectedEffectAreaHighlightCells(State, LoadedMap, selectionState, lastHoveredCoord);
        }

        private static IEnumerable<HexCoord> FilterOverlay(bool visible, IEnumerable<HexCoord> coords)
        {
            return visible ? coords : Enumerable.Empty<HexCoord>();
        }

        // Draws each visible (or hovered) monster's intended move route as a ground arrow + ghost, only
        // during the player's movement phase before the monster movement resolves. Reads State.GetMonsterMovePath
        // (presentation-only).
        private void UpdateMonsterMovePaths()
        {
            // IsCinematicViewActive: the move-intent ghosts are a translucent copy of each monster's model at
            // its predicted destination — game-piece dressing that has no place in a cinematic. They are not
            // covered by stageIntroHiddenMonsterIds (that hides the markers, not these previews), and the
            // intro turns reveal-all ON, which defeats the visibility filter below, so every monster on the
            // map drew a blue ghost into the shot until this gate was added.
            if (State == null || atlasTilePresentationView == null
                || isSequencePlaying
                || IsCinematicViewActive
                || State.Phase != CombatPhase.PlayerMovement)
            {
                monsterMovePathPresenter?.Clear();
                return;
            }

            EnsureMonsterMovePathPresenter();
            monsterMovePathEntries.Clear();
            foreach (var monster in State.Monsters)
            {
                if (monster.IsDead
                    || (hoveredMonsterId != null && monster.Id != hoveredMonsterId)
                    || (!revealAllMapCellsInDebugMode && State.GetVisibility(monster.Coord) != HexCellVisibility.Revealed))
                {
                    continue;
                }

                var path = State.GetMonsterMovePath(monster.Id);
                if (path == null || path.Count < 2)
                {
                    continue;
                }

                var worldPath = new List<Vector3>(path.Count);
                var allResolved = true;
                var ghostFootprintOffset = ResolveMonsterFootprintVisualOffset(monster.Id, monster.Coord);
                foreach (var coord in path)
                {
                    if (!TryGetTileWorldPosition(coord, out var worldPosition))
                    {
                        allResolved = false;
                        break;
                    }

                    worldPath.Add(worldPosition + ghostFootprintOffset);
                }

                if (allResolved && worldPath.Count >= 2)
                {
                    // 이동 예고 고스트는 그 몬스터의 "미리 보기"이므로 마커와 <b>같은</b> 배율이어야 한다.
                    // 배율을 빼먹으면 보스에서만 티가 난다: 실물은 줄어드는데 고스트만 원본 크기로 남아
                    // 판에 크기가 다른 보스 둘이 서 있는 것처럼 보인다(랩 캡처에서 실제로 관측했다).
                    monsterMovePathEntries.Add(new MonsterMovePathPresenter.Entry(
                        monster.Id,
                        worldPath,
                        ResolveMonsterVisualPrefab(monster.DefinitionId),
                        enemyVisualLocalScale * ResolveMonsterVisualScale(monster.Id),
                        enemyVisualLocalEulerAngles));
                }
            }

            monsterMovePathPresenter.Refresh(monsterMovePathEntries, hoveredMonsterId);
        }

        private void EnsureMonsterMovePathPresenter()
        {
            if (monsterMovePathPresenter != null)
            {
                return;
            }

            var parent = atlasTilePresentationView != null ? atlasTilePresentationView.transform : transform;
            var go = new GameObject("MonsterMovePathPresenter");
            go.transform.SetParent(parent, false);
            monsterMovePathPresenter = go.AddComponent<MonsterMovePathPresenter>();
            monsterMovePathPresenter.SetArrowSprite(ResolveMonsterPathArrowSprite());
        }

        private static Sprite ResolveMonsterPathArrowSprite()
        {
            var sprite = Resources.Load<Sprite>("UI/Icons/ui_arrow");
#if UNITY_EDITOR
            if (sprite == null)
            {
                sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Icons/ui_arrow.png");
            }
#endif
            return sprite;
        }

        private void UpdateMonsterFacing()
        {
            if (!ShouldUpdateMonsterIntentFacing)
            {
                HideMonsterFacingArrows();
                return;
            }

            // Monster model facing (SetFacingDirection) is always applied so the system intent stays
            // correct. The floating arrow meshes are a debug-only visual gated behind
            // CombatOverlayDebugLayer.MonsterIntentArrows (off by default = invisible to players).
            var activeArrowIds = showMonsterIntentArrows ? new HashSet<string>() : null;
            foreach (var monster in State.Monsters)
            {
                if (monster.IsDead) continue;
                var intent = monster.LockedFacingIntent;
                if (intent.Type != EnemyIntentType.Attack && intent.Type != EnemyIntentType.Chase) continue;
                var facingDir = intent.EnemyCoord.ApproximateDirection(intent.PlayerCoord);
                if (!TryGetTileWorldPosition(monster.Coord, out var fromWorld)) continue;
                if (!TryGetTileWorldPosition(monster.Coord.Neighbor(facingDir), out var toWorld)) continue;
                var forward = toWorld - fromWorld;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) continue;
                actorMarkerPresenter.SetFacingDirection(monster.Id, forward);
                if (showMonsterIntentArrows)
                {
                    UpdateArrowOverlay(monster.Id, fromWorld, forward.normalized, intent.Type);
                    activeArrowIds.Add(monster.Id);
                }
            }

            if (!showMonsterIntentArrows)
            {
                HideMonsterFacingArrows();
                return;
            }

            foreach (var pair in monsterArrowOverlays)
            {
                if (pair.Value != null)
                    pair.Value.SetActive(activeArrowIds.Contains(pair.Key));
            }
        }

        private void HideMonsterFacingArrows()
        {
            foreach (var arrow in monsterArrowOverlays.Values)
            {
                if (arrow != null)
                {
                    arrow.SetActive(false);
                }
            }
        }

        private void UpdateArrowOverlay(string monsterId, Vector3 tileWorldPos, Vector3 forward, EnemyIntentType intentType)
        {
            if (!monsterArrowOverlays.TryGetValue(monsterId, out var arrowGo) || arrowGo == null)
            {
                arrowGo = CreateArrowOverlay(monsterId, intentType);
                monsterArrowOverlays[monsterId] = arrowGo;
            }
            arrowGo.transform.position = tileWorldPos + Vector3.up * (HexOverlayRenderOrder.CombatFillLift + 0.02f);
            arrowGo.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            arrowGo.SetActive(true);
        }

        private GameObject CreateArrowOverlay(string monsterId, EnemyIntentType intentType)
        {
            var scale = atlasTilePresentationView != null ? atlasTilePresentationView.TileRadius : 1f;
            var go = new GameObject($"MonsterArrow_{monsterId}");
            var parent = atlasTilePresentationView != null ? atlasTilePresentationView.transform : transform;
            go.transform.SetParent(parent, worldPositionStays: false);
            var mf = go.AddComponent<MeshFilter>();
            if (arrowMesh == null) arrowMesh = BuildArrowMesh(scale);
            mf.sharedMesh = arrowMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = intentType == EnemyIntentType.Attack ? GetArrowMaterialAttack() : GetArrowMaterialChase();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private Material GetArrowMaterialAttack()
        {
            if (arrowMaterialAttack == null)
                arrowMaterialAttack = CombatOverlayMaterialFactory.Create("MonsterArrow_Attack", new Color(1f, 0.50f, 0.05f, 0.92f));
            return arrowMaterialAttack;
        }

        private Material GetArrowMaterialChase()
        {
            if (arrowMaterialChase == null)
                arrowMaterialChase = CombatOverlayMaterialFactory.Create("MonsterArrow_Chase", new Color(0.9f, 0.85f, 0.2f, 0.75f));
            return arrowMaterialChase;
        }

        private static Mesh BuildArrowMesh(float scale)
        {
            float s = scale;
            var vertices = new Vector3[]
            {
                new Vector3(   0f,       0f,  0.40f * s),  // 0: tip
                new Vector3(-0.22f * s,  0f,  0.10f * s),  // 1: arrowhead left
                new Vector3( 0.22f * s,  0f,  0.10f * s),  // 2: arrowhead right
                new Vector3(-0.09f * s,  0f,  0.10f * s),  // 3: stem top-left
                new Vector3( 0.09f * s,  0f,  0.10f * s),  // 4: stem top-right
                new Vector3(-0.09f * s,  0f, -0.28f * s),  // 5: stem bottom-left
                new Vector3( 0.09f * s,  0f, -0.28f * s),  // 6: stem bottom-right
            };
            var triangles = new int[] { 0, 2, 1,  3, 4, 6,  3, 6, 5 };
            var mesh = new Mesh { name = "MonsterFacingArrow" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        private void EnsureEnemyMarker()
        {
            actorMarkerPresenter.EnsureEnemyMarker(
                GetSelected3DViewTransform(),
                transform,
                enemyMarkerPrefab,
                enemyMarkerColor,
                enemyMarkerRadius,
                enemyVisualLocalOffset,
                enemyVisualLocalEulerAngles,
                enemyVisualLocalScale);
        }

        private void EnsureEnemyMarker(string monsterId)
        {
            // Resolve the monster's own visual prefab. Passing the raw enemyMarkerPrefab fallback here would make the
            // presenter detect a SourcePrefab change and rebuild the marker as the fallback model (ThreeEyeDog),
            // overwriting the correct visual that ShowMonsterMarkers built ??which made every monster's move/attack
            // animation play as the fallback monster.
            var resolvedVisualPrefab = ResolveMonsterVisualPrefabForMarker(monsterId);
            actorMarkerPresenter.EnsureEnemyMarker(
                monsterId,
                GetSelected3DViewTransform(),
                transform,
                resolvedVisualPrefab != null ? resolvedVisualPrefab : enemyMarkerPrefab,
                enemyMarkerColor,
                enemyMarkerRadius,
                enemyVisualLocalOffset,
                enemyVisualLocalEulerAngles,
                // ⚠️ 보스 페이즈 배율을 반드시 곱한다. EnsureEnemyMarker는 이미 있는 마커에도 배율을 <b>다시
                // 적용</b>하므로(페이즈 성장이 무시되지 않게 하려고 그렇게 만들었다), 여기서 맨
                // enemyVisualLocalScale(=1)을 넘기면 이동·넉백 연출이 시작될 때마다 보스가 1.0배로
                // 되돌아갔다가 다음 전체 뷰 갱신에서 원래 크기로 튄다 — 보스가 걷기 시작한 뒤에야
                // 드러난 결함이다(고정 보스일 때는 이 경로가 돌지 않았다).
                enemyVisualLocalScale * ResolveMonsterVisualScale(monsterId));
        }

        private GameObject ResolveMonsterVisualPrefabForMarker(string monsterId)
        {
            if (State == null || string.IsNullOrWhiteSpace(monsterId))
            {
                return null;
            }

            var monster = State.Monsters.FirstOrDefault(candidate => candidate.Id == monsterId);
            return string.IsNullOrWhiteSpace(monster.DefinitionId)
                ? null
                : ResolveMonsterVisualPrefab(monster.DefinitionId);
        }

        private void ApplyEnemyMarkerSettings()
        {
            EnsureEnemyMarker();
            actorMarkerPresenter.ApplyEnemyMarkerSettings(enemyMarkerPrefab, enemyMarkerColor, enemyMarkerRadius);
        }

        private void TriggerEnemyDeadIfVisibleMonsterDied(MonsterPresentationSnapshot beforeVisibleMonster)
        {
            if (beforeVisibleMonster.Id == null || beforeVisibleMonster.IsDead)
            {
                return;
            }

            var afterVisibleMonster = State.Monsters.FirstOrDefault(monster => monster.Id == beforeVisibleMonster.Id);
            if (afterVisibleMonster.Id == beforeVisibleMonster.Id && afterVisibleMonster.IsDead)
            {
                // Marker only — deliberately silent. This runs the instant the card is committed, before the
                // attack timeline has played its impact beat, so sounding the death here made it play *before*
                // the blow landed. The death is sounded by the lethal effect at impact, with
                // RequestMonsterDeathAudioCues as the post-timeline fallback.
                actorMarkerPresenter.TriggerDead();
            }
        }

        private void UpdateEnemyMarker()
        {
            if (State == null)
            {
                return;
            }

            ShowMonsterMarkers(State.Monsters.Where(monster => !monster.IsDead));
        }

        private void ShowMonsterMarkers(IEnumerable<MonsterRuntimeState> monstersToShow)
        {
            actorMarkerPresenter.ShowMonsterMarkers(
                BuildMonsterMarkerStates(monstersToShow.Where(IsMonsterVisibleForPresentation)),
                GetSelected3DViewTransform(),
                transform,
                enemyMarkerPrefab,
                enemyMarkerColor,
                enemyMarkerRadius,
                enemyVisualLocalOffset,
                enemyVisualLocalEulerAngles,
                enemyVisualLocalScale);
            RefreshIntentHiddenMarks();
            PlayStealthRevealFades();
            RefreshBossAuraLayers();
            RefreshMonsterNameplateBadges();
        }

        /// <summary>
        /// 네임플레이트 배지 행(이름 위)을 현재 의도 + 상태이상으로 갱신한다. 마커를 다시 만들 때마다
        /// 불러야 한다 — 행은 네임플레이트에 붙어 있어 마커가 재생성되면 함께 사라진다(미지 표식과 같은 계약).
        ///
        /// <para>🔑 의도 배지의 피해는 <b>예고(<see cref="CombatState.GetMonsterIntentPreviews"/>)에서만</b>
        /// 가져온다 — 집행과 같은 함수를 지난 R-8 실효값이라 약오름·쇠약이 붙어도 화면이 거짓말을 하지
        /// 않는다. 반대로 히트 수·밀치기 칸수는 보정 대상이 아니므로 카탈로그 패턴이 정본이다
        /// (2026-08-10부터 예고의 <c>AttackPatternKnockbackDistance</c>도 부호를 보존한다 —
        /// 예전에는 음수를 0으로 깎아 끌어당김 정보를 잃었다).</para>
        /// </summary>
        private void RefreshMonsterNameplateBadges()
        {
            if (State == null || actorMarkerPresenter == null)
            {
                return;
            }

            var byMonster = new Dictionary<string, IReadOnlyList<MonsterNameplateBadge>>(StringComparer.Ordinal);

            foreach (var effect in State.ActiveEffects)
            {
                if (effect.IsExpired || string.IsNullOrEmpty(effect.TargetUnitId))
                {
                    continue;
                }

                GetOrCreateBadgeList(byMonster, effect.TargetUnitId).Add(MonsterNameplateBadge.Status(effect));
            }

            AddMonsterIntentBadges(byMonster);

            foreach (var monster in State.Monsters)
            {
                // 방어막은 플레이어 방어도와 같은 필드(CombatantState.Block)다 — 지금 남아 있는
                // 잔량을 그대로 띄운다(보스 수호 함정이 이미 이 값을 채운다).
                if (monster.Block > 0)
                {
                    GetOrCreateBadgeList(byMonster, monster.Id).Add(
                        MonsterNameplateBadge.Shield(monster.Block));
                }

                if (!State.TryGetMonsterCatalogEntry(monster.DefinitionId, out var entry))
                {
                    continue;
                }

                // 특성 배지(2026-09-04) — 저작에서 유도하고 표시 규칙은 monster_traits.csv가 정한다.
                // 종전에는 특성마다 여기에 if 한 덩이 + enum 1 + 프리젠터 switch 6벌이었고, 그래서
                // 은신·홀림·소매치기·밀어붙이기 넷이 규칙만 있고 배지가 없는 채로 출하됐다(D4).
                AppendTraitBadges(byMonster, monster, entry);

                // 기물 성숙 카운트다운(2026-09-03 ⑥): "3턴 안에 부순다"가 핵심 퍼즐인데 호버 툴팁으로만
                // 읽혔다 — 상시 배지로 격상. 값은 툴팁과 같은 규칙층 투영에서 온다(두 벌 금지).
                if (MonsterSpawnRoles.IsBossProp(monster.SpawnRole)
                    && State.TryGetBossPropAbsorptionCountdown(monster.Id, out var maturityRemaining, out _))
                {
                    GetOrCreateBadgeList(byMonster, monster.Id).Add(
                        MonsterNameplateBadge.PropMaturity(maturityRemaining));
                }
            }

            actorMarkerPresenter.ApplyMonsterNameplateBadges(
                byMonster,
                statusIconOverlayRenderer != null ? statusIconOverlayRenderer.IconCatalog : null);
        }

        /// <summary>
        /// 이 몬스터의 특성 배지를 전부 단다. <b>어떤 특성을 갖는가</b>는 저작이 정하고
        /// (<see cref="MonsterTraitResolver"/>), <b>어떻게 보이는가</b>는 monster_traits.csv가 정한다 —
        /// 이 함수가 아는 것은 「지금 몇인가」(스택·잠근 장수·소진 여부)뿐이다.
        ///
        /// <para>🔑 숫자의 뜻이 특성마다 달라서(스택 / 장수 / 소진) 찍을지 말지를 저작을 아는
        /// 여기서 정한다. 프리젠터는 <c>ShowsNumber</c> 한 비트만 보므로 특성이 늘어도 안 바뀐다.</para>
        /// </summary>
        private void AppendTraitBadges(
            Dictionary<string, IReadOnlyList<MonsterNameplateBadge>> byMonster,
            MonsterRuntimeState monster,
            MonsterCatalogEntry entry)
        {
            var catalog = MonsterTraitCatalogProvider.Active;
            if (catalog == null)
            {
                return;
            }

            foreach (var traitId in MonsterTraitResolver.CollectTraitIds(entry))
            {
                if (!catalog.TryGet(traitId, out var trait))
                {
                    // 게이트(MonsterTraitCatalogTests)가 출하 저작에서 이걸 막는다 — 여기 오면 새 저작이
                    // 표에 등록되지 않은 것이고, 조용히 빠뜨리느니 배지 없이 넘어가는 편이 낫다.
                    continue;
                }

                var amount = 0;
                var showsNumber = false;
                var dimmed = false;

                switch (traitId)
                {
                    case MonsterTraitIds.Agitation:
                    case MonsterTraitIds.StrengthDistance:
                        // 🔴 수치는 <b>힘 배지</b>가 말한다(2026-09-05 사용자 확정: "인내심 자체의 수치는
                        // 필요없고 힘 수치로 나타나면 됨"). 스택 카운터는 힘 상태이상으로 투영되고
                        // (SyncMonsterMightEffect) 그 배지가 이미 같은 수를 띄우므로, 여기서 또 찍으면
                        // 같은 값이 두 배지에 나란히 뜬다. 이 배지는 「이 놈은 약오름을 가졌다」만 말한다.
                        break;

                    case MonsterTraitIds.Toughness:
                        // 소진도 정보다("지금 때리면 온전히 들어간다") — 숨기지 않고 색을 누른다.
                        dimmed = monster.ToughnessSpent;
                        amount = monster.ToughnessSpent ? 0 : 1;
                        break;

                    case MonsterTraitIds.Stealth:
                        // 「다시 숨기까지 남은 턴」(2026-09-05). 0 = 지금 숨어 있다 — 그때는 몬스터
                        // 자체가 안 보이므로 배지도 함께 안 뜬다(마커 은폐가 배지 줄까지 걷는다).
                        amount = monster.StealthRevealTurnsRemaining;
                        showsNumber = amount > 0;
                        break;

                    case MonsterTraitIds.AuraSeal:
                        amount = MonsterAuraSeal.TryParse(
                            entry.HiddenTraitRef, entry.HiddenTraitParam, out var seal, out _)
                            ? seal.Cards
                            : 0;
                        showsNumber = amount > 0;
                        break;
                }

                // 저작만 있으면 상시(always) / 지금 일하고 있을 때만(active). 약오름이 후자다 —
                // 스택 0인 「빈 배지」가 늘 떠 있으면 이름 위가 아무 말도 안 하면서 붐빈다.
                if (trait.Visibility == MonsterTraitBadgeVisibility.Active && amount <= 0)
                {
                    continue;
                }

                GetOrCreateBadgeList(byMonster, monster.Id).Add(
                    MonsterNameplateBadge.Trait(traitId, amount, showsNumber, dimmed));
            }
        }

        private static List<MonsterNameplateBadge> GetOrCreateBadgeList(
            Dictionary<string, IReadOnlyList<MonsterNameplateBadge>> byMonster,
            string monsterId)
        {
            if (!byMonster.TryGetValue(monsterId, out var badges))
            {
                badges = new List<MonsterNameplateBadge>();
                byMonster[monsterId] = badges;
            }

            return (List<MonsterNameplateBadge>)badges;
        }

        /// <summary>
        /// 공격 의도 배지(칼+피해 · 다단 ×N · 밀치기). 플레이어 페이즈에만 띄운다 — 적 페이즈에는
        /// 예고가 이미 집행되는 중이라 "예정"이 아니고, 타일 예고 오버레이와 같은 게이트다.
        /// 은폐된 의도(D-4)는 값을 지어내지 않고 통째로 건너뛴다.
        /// </summary>
        private void AddMonsterIntentBadges(Dictionary<string, IReadOnlyList<MonsterNameplateBadge>> byMonster)
        {
            if (State.Phase != CombatPhase.PlayerMovement && State.Phase != CombatPhase.PlayerAction)
            {
                return;
            }

            foreach (var preview in State.GetMonsterIntentPreviews(includeUnrevealed: false))
            {
                if (preview.IsIntentHidden)
                {
                    continue;
                }

                // 자기부여(2026-08-20 #16): 커버 판정을 건너뛰는 패턴이라 IntentType이 Chase/Search로
                // 남는다 — IntentType==Attack 게이트만 있으면 버프 턴의 배지가 통째로 빠진다(실측:
                // A031 돼지·A029/A030 불가살). 걸릴 상태·두를 방어막을 예고 배지로 띄운다.
                if (preview.IsSelfBuffIntent)
                {
                    var selfBadges = GetOrCreateBadgeList(byMonster, preview.MonsterId);
                    foreach (var statusKind in preview.AttackPatternStatusEffects)
                    {
                        selfBadges.Add(MonsterNameplateBadge.SelfBuffStatus(
                            statusKind, preview.AttackPatternStatusEffectDurationTurns));
                    }

                    if (preview.AttackPatternShieldGain > 0)
                    {
                        selfBadges.Add(MonsterNameplateBadge.SelfBuffShield(preview.AttackPatternShieldGain));
                    }

                    continue;
                }

                // 기믹 턴 예고(2026-09-03 ⑥): 기믹이 공격을 대체하는 턴에는 아래의 공격 의도 게이트가
                // 전부 걸러지므로(공격 차단 술어가 예고를 비운다) 이 배지가 없으면 보스가 "아무것도
                // 안 하는 턴"으로 읽힌다. id별 배지라 살포+전멸기가 겹치는 저작도 각각 선다.
                if (preview.BossGimmickIds.Count > 0)
                {
                    var gimmickBadges = GetOrCreateBadgeList(byMonster, preview.MonsterId);
                    foreach (var gimmickId in preview.BossGimmickIds)
                    {
                        gimmickBadges.Add(MonsterNameplateBadge.BossGimmick(gimmickId));
                    }

                    continue;
                }

                // 🔴 IntentType은 <b>이동</b> 의도다(plan.MovementIntent) — 걸어와서 때리는 몬스터는
                //    Chase로 남아 「순수 피해 공격의 의도 배지가 안 뜬다」가 됐다(2026-09-01 #1).
                //    규칙층이 확정해 둔 WillAttackPlayer가 답이고, IntentType은 제자리 공격(속박 붕괴 등)
                //    까지 덮는 보조 축으로만 남긴다.
                if (!preview.WillAttackPlayer && preview.IntentType != EnemyIntentType.Attack)
                {
                    continue;
                }

                var badges = GetOrCreateBadgeList(byMonster, preview.MonsterId);
                if (!TryGetIntentPattern(preview.MonsterId, out var pattern))
                {
                    // 패턴을 못 찾으면 반복 횟수를 모른다 — 1회로 읽는 것이 예전 동작과 같다.
                    badges.Add(MonsterNameplateBadge.Attack(preview.AttackPatternDamage));
                    continue;
                }

                // 2026-09-02 #10: 다단 히트는 <b>별도 배지가 아니라</b> 공격 배지의 ×N 표기로 붙는다.
                badges.Add(MonsterNameplateBadge.Attack(preview.AttackPatternDamage, pattern.HitCount));

                if (pattern.KnockbackDistance != 0)
                {
                    badges.Add(MonsterNameplateBadge.Knockback(pattern.KnockbackDistance));
                }
            }
        }

        /// <summary>이번 턴 몬스터가 고른 공격 패턴의 <b>저작</b> 원본. 못 찾으면 false.</summary>
        private bool TryGetIntentPattern(string monsterId, out MonsterAttackPattern pattern)
        {
            pattern = default;
            foreach (var monster in State.Monsters)
            {
                if (!string.Equals(monster.Id, monsterId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!State.TryGetMonsterCatalogEntry(monster.DefinitionId, out var entry))
                {
                    return false;
                }

                var patterns = entry.AttackPatterns;
                if (patterns == null || monster.AttackPatternIndex < 0 || monster.AttackPatternIndex >= patterns.Length)
                {
                    return false;
                }

                pattern = patterns[monster.AttackPatternIndex];
                return true;
            }

            return false;
        }

        /// <summary>
        /// 미지 '?' 표식을 몬스터별로 켜고 끈다. 마커를 다시 만들 때마다 불러야 한다 —
        /// 표식은 네임플레이트에 붙어 있어서 마커가 재생성되면 함께 사라진다.
        /// 판정은 규칙 계층(<c>State.IsMonsterIntentVeiledByStatus</c>)에만 있다.
        ///
        /// <para>🔴 <b>은폐 술어(<c>IsMonsterIntentHidden</c>)를 쓰면 안 된다</b>(2026-09-02 #6).
        /// 2026-09-01에 은신을 그 술어에 합류시킨 뒤로 <b>숨은 몬스터 위에 물음표가 떴다</b> —
        /// 마커를 감춰 놓고 그 자리에 표식을 세우면 위치가 그대로 샌다. 표식은 「보이는데 의도를
        /// 모른다」는 미지의 이야기이고, 은신은 「있는지조차 모른다」라 표시할 대상이 아니다.</para>
        /// </summary>
        private void RefreshIntentHiddenMarks()
        {
            if (State == null || actorMarkerPresenter == null)
            {
                return;
            }

            foreach (var monster in State.Monsters)
            {
                actorMarkerPresenter.SetIntentHiddenMark(monster.Id, State.IsMonsterIntentVeiledByStatus(monster.Id));
            }
        }

        /// <summary>
        /// 보스 아우라의 <b>발밑 링 층</b>을 페이즈에 맞춰 켜고 끈다(D5=페이즈 연동, 2026-08-05 사용자 결정).
        ///
        /// <para>어느 페이즈에 아우라가 붙는지는 이미 저작돼 있다 — <c>boss_phases.csv</c>의
        /// <c>auraStatusKind</c>가 그것이고, 같은 값을 <see cref="CombatStatusLoopVfxController"/>가
        /// 파티클 층에 쓰고 있다. 여기서 판정을 새로 만들지 않고 <b>같은 술어를 공유</b>하는 이유가
        /// 그것이다: 두 층이 서로 다른 조건으로 켜지면 "파티클은 도는데 림만 없는" 페이즈가 생긴다.</para>
        ///
        /// <para>⚠️ 프레넬 림 층은 2026-08-06 사용자 판정으로 <b>폐기</b>했다(§11.8) — 3D 메시에서
        /// 선이 잘게 흩어져 읽히지 않았다. 아우라는 링 + 파티클 두 층이다.</para>
        /// </summary>
        private void RefreshBossAuraLayers()
        {
            if (State == null || actorMarkerPresenter == null)
            {
                return;
            }

            foreach (var boss in State.BossPhases)
            {
                if (string.IsNullOrEmpty(boss.BossUnitId))
                {
                    continue;
                }

                var wantsRing = bossAuraRingEnabled
                    && boss.AuraStatusKind.HasValue
                    && IsBossAuraVisible(boss.BossUnitId);
                actorMarkerPresenter.SetMarkerAuraRing(
                    boss.BossUnitId,
                    wantsRing,
                    ResolveBossAuraRingPrefab(),
                    bossAuraRingRadiusMultiplier);
            }
        }

        /// <summary>
        /// 발밑 링 프리팹을 푼다. 씬에 저작돼 있으면 그것을 쓰고, 없으면 <c>Resources</c>에서 읽는다.
        ///
        /// <para>🔴 폴백이 <b>필수</b>다. 출하 씬은 이 필드를 직렬화하지 않으므로(신규 직렬화 필드)
        /// 참조가 비어 있고, <c>AssetDatabase</c>는 에디터 전용이라 에디터에서만 붙고 빌드에서는
        /// 조용히 사라지는 갭이 생긴다 — <c>Resources/Object/trap.prefab</c>이 빌드에서 원기둥으로
        /// 폴백했던 사고와 같은 함정이며, 같은 해법을 쓴다.</para>
        /// </summary>
        private GameObject ResolveBossAuraRingPrefab()
        {
            if (bossAuraRingPrefab != null)
            {
                return bossAuraRingPrefab;
            }

            if (!bossAuraRingPrefabResolved)
            {
                bossAuraRingPrefabResolved = true;
                bossAuraRingPrefabFallback = Resources.Load<GameObject>(BossAuraRingResourcePath);
            }

            return bossAuraRingPrefabFallback;
        }

        /// <summary>
        /// 안개에 가려졌거나 죽은 보스에는 아우라를 얹지 않는다. 프레젠테이션 목록에서 빠지면 모델이
        /// 아예 안 그려지므로 누출은 없지만, 마커가 남아 있는 사이 링만 살아 있으면 죽은 보스의
        /// 발밑이 한 프레임 빛난다.
        /// </summary>
        private bool IsBossAuraVisible(string bossUnitId)
        {
            foreach (var monster in State.Monsters)
            {
                if (!string.Equals(monster.Id, bossUnitId, System.StringComparison.Ordinal))
                {
                    continue;
                }

                return !monster.IsDead && IsMonsterVisibleForPresentation(monster);
            }

            return false;
        }

        private void ShowMonsterMarkers(IEnumerable<MonsterPresentationSnapshot> monstersToShow)
        {
            actorMarkerPresenter.ShowMonsterMarkers(
                BuildMonsterMarkerStates(monstersToShow.Where(IsMonsterVisibleForPresentation)),
                GetSelected3DViewTransform(),
                transform,
                enemyMarkerPrefab,
                enemyMarkerColor,
                enemyMarkerRadius,
                enemyVisualLocalOffset,
                enemyVisualLocalEulerAngles,
                enemyVisualLocalScale);
            RefreshIntentHiddenMarks();
            PlayStealthRevealFades();
            RefreshBossAuraLayers();
            // 마커 재생성은 배지 행을 함께 부순다(위 오버로드와 같은 계약) — 이 호출이 빠져 있어서
            // 연출 앵커 경유 리빌드마다 배지가 다음 RefreshView까지 통째로 사라졌다(2026-08-20 #16).
            RefreshMonsterNameplateBadges();
        }

        /// <summary>
        /// 보스 점유 오버레이용 좌표를 몬스터 본체·체력바와 <b>같은 가시성 게이트</b>로 거른다(#22).
        /// 게이트 없이 State.BossFootprintCoords를 그대로 그리면 발밑 글로우가 암시야를 뚫고
        /// 조우 전 보스 위치를 누설한다 — 레이드 문법 위반. 판정은 보스 단위다(본체가 보이면
        /// 점유 전체가 보인다) — 본체는 통째로 그려지는데 점유만 칸 단위로 조각나면 어긋난다.
        /// </summary>
        private IReadOnlyList<HexCoord> GetVisibleBossFootprintCoords()
        {
            if (State == null)
            {
                return System.Array.Empty<HexCoord>();
            }

            var monsters = State.Monsters;
            return State.GetBossFootprintCoords(bossUnitId =>
            {
                foreach (var monster in monsters)
                {
                    if (string.Equals(monster.Id, bossUnitId, System.StringComparison.Ordinal))
                    {
                        return IsMonsterVisibleForPresentation(monster);
                    }
                }

                return false;
            });
        }

        /// <summary>
        /// 은신이 풀려 <b>모습이 돌아온</b> 몬스터에 페이드를 건다(2026-09-05 사용자 요구).
        ///
        /// <para>🔑 신호를 규칙층 이벤트가 아니라 <b>마커 목록의 변화</b>에서 읽는다. 숨은 몬스터는
        /// 마커 목록에서 통째로 빠지므로(IsMonsterVisibleForPresentation), 「지난 프레임엔 없었고
        /// 지금 있다」가 곧 「방금 드러났다」다 — 이벤트 디스패치 시점에는 모델이 아직 안 붙어 있어서
        /// 그때 페이드를 걸면 아무 일도 일어나지 않는다(은신 <b>진입</b> 연출과 순서가 반대다).</para>
        /// </summary>
        private void PlayStealthRevealFades()
        {
            if (State == null || actorMarkerPresenter == null)
            {
                return;
            }

            stealthRevealFadeSeen.Clear();
            foreach (var monster in State.Monsters)
            {
                if (monster.IsDead
                    || !State.TryGetMonsterCatalogEntry(monster.DefinitionId, out var entry)
                    || !MonsterTraitResolver.CollectTraitIds(entry).Contains(MonsterTraitIds.Stealth)
                    || !IsMonsterVisibleForPresentation(monster))
                {
                    continue;
                }

                stealthRevealFadeSeen.Add(monster.Id);
                if (stealthVisibleLastFrame.Contains(monster.Id))
                {
                    continue;
                }

                actorMarkerPresenter.PlayStealthRevealFade(
                    monster.Id, StealthRevealFadeSeconds, StealthRevealFadeStartAlpha);
            }

            stealthVisibleLastFrame.Clear();
            foreach (var id in stealthRevealFadeSeen)
            {
                stealthVisibleLastFrame.Add(id);
            }
        }

        private bool IsMonsterVisibleForPresentation(MonsterRuntimeState monster)
        {
            // Stage-intro hide wins over every reveal override (including debug reveal-all), so the intro
            // can sweep a fully lit map while its monsters stay unspawned until their beat reveals them.
            if (stageIntroHiddenMonsterIds != null && stageIntroHiddenMonsterIds.Contains(monster.Id))
            {
                return false;
            }

            if (revealAllMapCellsInDebugMode)
            {
                return true;
            }

            // During the intro finale's darkness ripple, monsters stay visible exactly as long as their
            // cell is still in the forced-reveal set, so the advancing darkness swallows them in sync
            // with the tiles instead of them popping off at the ripple's start.
            if (cinematicForcedRevealCells != null && cinematicForcedRevealCells.Contains(monster.Coord))
            {
                return true;
            }

            if (State == null)
            {
                return true;
            }

            // 🔴 안개 술어와 은신 술어는 <b>따로</b> 선다(요괴 §4-1). 시야 밖 몬스터는 은신과 무관하게
            // 이미 안 보이고, 은신이 더하는 것은 「시야 안인데도 안 보임」 하나뿐이다 — 합치면
            // 노출 규칙이 안개 규칙을 덮어써서 드러난 몬스터가 안개 속에서도 보이게 된다.
            // 🔴 아레나 전 보스도 <b>또 하나의 따로 선 술어</b>다(2026-09-02 #4) — 은신에 합치면
            //    "숨은 것"과 "아직 등장하지 않은 것"이 한 규칙이 되어 한쪽 해제가 다른 쪽을 깨운다.
            return State.GetVisibility(monster.Coord) == HexCellVisibility.Revealed
                   && !State.IsMonsterHiddenByStealth(monster.Id)
                   && !State.IsMonsterHiddenBeforeBossArena(monster.Id);
        }

        private bool IsMonsterVisibleForPresentation(MonsterPresentationSnapshot monster)
        {
            if (stageIntroHiddenMonsterIds != null && stageIntroHiddenMonsterIds.Contains(monster.Id))
            {
                return false;
            }

            if (revealAllMapCellsInDebugMode)
            {
                return true;
            }

            if (cinematicForcedRevealCells != null && cinematicForcedRevealCells.Contains(monster.Coord))
            {
                return true;
            }

            // 스냅샷의 IsVisible은 생산부(CombatPresentationSnapshot)에서 이미 안개 ∧ 은신으로 굳는다.
            return monster.IsVisible;
        }

        private IEnumerable<CombatActorMarkerPresenter.MonsterMarkerState> BuildMonsterMarkerStates(IEnumerable<MonsterRuntimeState> monstersToShow)
        {
            return BuildMonsterMarkerStates(monstersToShow.Select(monster => (monster.Id, monster.DefinitionId, monster.Coord, monster.Hp, monster.MaxHp, monster.Intent.Type, monster.HasAttackIntent, IsBossRole(monster.SpawnRole), IsEliteRole(monster.SpawnRole))));
        }

        private IEnumerable<CombatActorMarkerPresenter.MonsterMarkerState> BuildMonsterMarkerStates(IEnumerable<MonsterPresentationSnapshot> monstersToShow)
        {
            return BuildMonsterMarkerStates(monstersToShow.Select(monster => (monster.Id, monster.DefinitionId, monster.Coord, monster.Hp, monster.MaxHp, monster.IntentType, monster.HasAttackIntent, monster.IsBoss, monster.IsElite)));
        }

        private IEnumerable<CombatActorMarkerPresenter.MonsterMarkerState> BuildMonsterMarkerStates(IEnumerable<(string Id, string DefinitionId, HexCoord Coord, int Hp, int MaxHp, EnemyIntentType IntentType, bool HasAttackIntent, bool IsBoss, bool IsElite)> monstersToShow)
        {
            var index = 0;
            foreach (var monster in monstersToShow)
            {
                if (!string.IsNullOrWhiteSpace(monster.Id) && TryProjectSelected3DView(monster.Coord, out var projected))
                {
                    var labelNumber = index + 1;
                    var displayName = ResolveMonsterDisplayName(monster.DefinitionId, labelNumber);
                    var visualPrefab = ResolveMonsterVisualPrefab(monster.DefinitionId);
                    var footprintOffset = ResolveMonsterFootprintVisualOffset(monster.Id, monster.Coord);
                    yield return new CombatActorMarkerPresenter.MonsterMarkerState(
                        monster.Id,
                        new Vector3(projected.x, projected.y + enemyMarkerHeightOffset, projected.z) + footprintOffset,
                        displayName,
                        MonsterMarkerAccentColors[index % MonsterMarkerAccentColors.Length],
                        monster.Hp,
                        monster.MaxHp,
                        monster.IsBoss,
                        visualPrefab,
                        Vector3.one * ResolveMonsterVisualScale(monster.Id),
                        monster.IsElite);
                }

                index++;
            }
        }

        /// <summary>
        /// 형상 footprint(2026-09-03 · 삼각형 정예) 몬스터의 모델을 앵커 칸이 아니라 <b>점유 칸들의 무게중심</b>에
        /// 세우기 위한 월드 오프셋. 삼각형이면 세 칸이 공유하는 꼭짓점이다. 한 칸·원판(앵커 = 무게중심)은 0이다.
        /// 마커와 이동 고스트가 같은 값을 써야 「미리 보기」가 실물과 어긋나지 않는다.
        /// </summary>
        private Vector3 ResolveMonsterFootprintVisualOffset(string monsterUnitId, HexCoord anchor)
        {
            if (State == null || string.IsNullOrEmpty(monsterUnitId))
            {
                return Vector3.zero;
            }

            var offsets = State.GetMonsterFootprintShapeOffsets(monsterUnitId);
            if (offsets == null || offsets.Count <= 1 || !TryProjectSelected3DView(anchor, out var anchorWorld))
            {
                return Vector3.zero;
            }

            var sum = Vector3.zero;
            var count = 0;
            for (var i = 0; i < offsets.Count; i++)
            {
                if (!TryProjectSelected3DView(anchor + offsets[i], out var cellWorld))
                {
                    return Vector3.zero;
                }

                sum += cellWorld;
                count++;
            }

            return count == 0 ? Vector3.zero : sum / count - anchorWorld;
        }

        /// <summary>
        /// 화면에 실제로 적용 중인 보스 모델 배율. 저작값(규칙)과 <b>표시값(연출)</b>을 분리하기 위한
        /// 래치다(§14.2) — 규칙이 페이즈를 올리는 순간 저작 배율은 바로 커지지만, 화면은 전환 연출이
        /// 버스트 타이밍에 맞춰 키워 줄 때까지 이전 크기를 유지해야 한다.
        /// 키는 보스 유닛 id이고, 연출 비트가 없는 몬스터는 아예 들어오지 않는다.
        /// </summary>
        private readonly Dictionary<string, float> bossVisualScaleDisplayed = new Dictionary<string, float>(System.StringComparer.Ordinal);

        /// <summary>
        /// 몬스터 모델 배율. 보스만 페이즈 저작값(<c>boss_phases.csv</c>의 <c>visualScale</c>)을 따르고
        /// 나머지는 1이다.
        ///
        /// 이 값이 곧 보스의 <b>기본 크기</b>이기도 하다: 프리팹을 건드려 기본 크기를 줄이면 페이즈 배율과
        /// 두 군데에서 크기가 결정되어 "합쳐서 얼마"인지 아무도 모르게 된다. 1페이즈 배율 하나로
        /// 기본 크기를 표현하면 크기 결정이 CSV 한 표에 모인다.
        ///
        /// 반환값은 저작값이 아니라 <b>표시값</b>이다: 처음 보는 보스는 저작값으로 래치되고, 그 뒤로는
        /// 전환 연출만이 값을 옮긴다. 연출이 돌지 않는 경로(디버그 전진·서스펜드 복원)에서 크기가 영원히
        /// 뒤처지지 않도록 연출 시퀀스가 끝날 때 <see cref="SnapBossVisualScalesToAuthored"/>가 맞춘다.
        /// </summary>
        private float ResolveMonsterVisualScale(string monsterUnitId)
        {
            return ResolveMonsterVisualScaleWithoutDebugOverride(monsterUnitId)
                   * ResolveDebugMonsterVisualScaleOverride(monsterUnitId);
        }

        private float ResolveMonsterVisualScaleWithoutDebugOverride(string monsterUnitId)
        {
            var authored = ResolveAuthoredMonsterVisualScale(monsterUnitId);
            if (string.IsNullOrEmpty(monsterUnitId) || Mathf.Approximately(authored, 1f))
            {
                return authored;
            }

            if (bossVisualScaleDisplayed.TryGetValue(monsterUnitId, out var displayed))
            {
                return displayed;
            }

            bossVisualScaleDisplayed[monsterUnitId] = authored;
            return authored;
        }

        /// <summary>
        /// 디버그 패널(Sandbox 탭 「몸집 조절」)이 유닛별로 얹는 배율. 규칙·저작에는 없는 순수 연출 손잡이라
        /// 세이브에도 안 들어가고, 프리팹에 저장하는 순간 1로 돌아간다(<see cref="DebugWriteMonsterVisualScaleToPrefab"/>).
        /// 보스 페이즈 트윈(<see cref="SetBossVisualScaleDisplayed"/>)과 곱으로 합쳐지므로 서로를 덮어쓰지 않는다.
        /// </summary>
        private readonly Dictionary<string, float> debugMonsterVisualScaleOverrides = new Dictionary<string, float>(System.StringComparer.Ordinal);

        private float ResolveDebugMonsterVisualScaleOverride(string monsterUnitId)
        {
            return !string.IsNullOrEmpty(monsterUnitId)
                   && debugMonsterVisualScaleOverrides.TryGetValue(monsterUnitId, out var multiplier)
                   && multiplier > 0f
                ? multiplier
                : 1f;
        }

        /// <summary>규칙이 말하는 배율(저작 그대로). 연출의 트윈 목표치다.</summary>
        private float ResolveAuthoredMonsterVisualScale(string monsterUnitId)
        {
            return State != null && State.TryGetBossPhaseState(monsterUnitId, out var phase) && phase.VisualScale > 0f
                ? phase.VisualScale
                : 1f;
        }

        /// <summary>
        /// 전환 연출이 프레임마다 부른다. 표시값을 옮기고 <b>마커에 즉시 반영</b>한다 —
        /// 래치만 바꾸면 다음 전체 뷰 갱신까지 화면이 그대로라 트윈이 보이지 않는다.
        /// </summary>
        private void SetBossVisualScaleDisplayed(string bossUnitId, float scale)
        {
            if (string.IsNullOrEmpty(bossUnitId))
            {
                return;
            }

            bossVisualScaleDisplayed[bossUnitId] = scale;
            actorMarkerPresenter.SetMarkerVisualScale(bossUnitId, Vector3.one * scale * ResolveDebugMonsterVisualScaleOverride(bossUnitId));
        }

        /// <summary>
        /// 표시값을 저작값으로 맞춘다(안전망). 연출 시퀀스가 끝나는 지점에서 불려, 연출이 돌지 않은
        /// 전환에서도 크기가 결국 진실해진다.
        /// </summary>
        private void SnapBossVisualScalesToAuthored()
        {
            if (bossVisualScaleDisplayed.Count == 0)
            {
                return;
            }

            // 반복 중 수정을 피하려고 키를 먼저 뜬다(보스는 한둘이라 비용은 무시 가능).
            foreach (var unitId in bossVisualScaleDisplayed.Keys.ToList())
            {
                var authored = ResolveAuthoredMonsterVisualScale(unitId);
                if (!Mathf.Approximately(bossVisualScaleDisplayed[unitId], authored))
                {
                    SetBossVisualScaleDisplayed(unitId, authored);
                }
            }
        }

        // 역할 판별은 규칙 계층과 같은 정본(MonsterSpawnRoles)을 쓴다 — 두 계층에 문자열 비교가 갈라져 있으면
        // 새 역할을 추가할 때 한쪽만 고쳐지는 사고가 난다.
        private static bool IsBossRole(string role) => MonsterSpawnRoles.IsBoss(role);

        private static bool IsEliteRole(string role) => MonsterSpawnRoles.IsElite(role);

        private static bool IsBossPropRole(string role) => MonsterSpawnRoles.IsBossProp(role);

        private static bool IsPropRole(string role) => MonsterSpawnRoles.IsProp(role);

        private GameObject ResolveMonsterVisualPrefab(string definitionId)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
            {
                return null;
            }

            if (monsterVisualPrefabByDefinitionId.TryGetValue(definitionId, out var cached) && cached != null)
            {
                return cached;
            }

            // Runtime-safe path: serialized per-monster prefab bindings ship inside player builds, where
            // AssetDatabase is unavailable. This is the only resolution that works outside the editor.
            var prefab = FindBoundMonsterVisualPrefab(definitionId);

#if UNITY_EDITOR
            // Editor convenience fallback: resolve straight from the catalog path so the editor renders correctly
            // even before the bindings list is populated. Builds must rely on the serialized bindings above.
            if (prefab == null &&
                State != null &&
                State.MonsterCatalog.TryGetEntry(definitionId, out var entry) &&
                !string.IsNullOrWhiteSpace(entry.VisualPrefabPath))
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.VisualPrefabPath);
            }
#endif

            // Only cache successful loads. Caching null would permanently pin a monster to the fallback marker
            // prefab if resolution ever missed transiently (e.g. before State/catalog was ready).
            if (prefab != null)
            {
                monsterVisualPrefabByDefinitionId[definitionId] = prefab;
            }

            return prefab;
        }

        private GameObject FindBoundMonsterVisualPrefab(string definitionId)
        {
            if (monsterVisualPrefabBindings == null)
            {
                return null;
            }

            for (var i = 0; i < monsterVisualPrefabBindings.Count; i++)
            {
                var binding = monsterVisualPrefabBindings[i];
                if (binding.Prefab != null &&
                    string.Equals(binding.DefinitionId, definitionId, System.StringComparison.Ordinal))
                {
                    return binding.Prefab;
                }
            }

            return null;
        }

        private string ResolveMonsterDisplayName(string definitionId, int fallbackNumber)
        {
            if (State != null &&
                !string.IsNullOrWhiteSpace(definitionId) &&
                State.MonsterCatalog.TryGetEntry(definitionId, out var entry) &&
                !string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                return entry.DisplayName;
            }

            return $"M{fallbackNumber}";
        }

#if UNITY_EDITOR
        [ContextMenu("Populate Monster Visual Prefabs")]
        private void PopulateMonsterVisualPrefabBindings()
        {
            MonsterCatalogDefinition catalog;
            try
            {
                catalog = CreateMonsterCatalog(CreateCombatConfig());
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"Failed to build monster catalog for visual prefab population: {exception.Message}", this);
                return;
            }

            var bindings = new List<MonsterVisualPrefabBinding>();
            foreach (var entry in catalog.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.VisualPrefabPath))
                {
                    continue;
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.VisualPrefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"Monster '{entry.Id}' visual prefab not found at {entry.VisualPrefabPath}.", this);
                    continue;
                }

                bindings.Add(MonsterVisualPrefabBinding.Create(entry.Id, prefab));
            }

            monsterVisualPrefabBindings = bindings;
            monsterVisualPrefabByDefinitionId.Clear();
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"Populated {bindings.Count} monster visual prefab bindings.", this);
        }

#endif

        [System.Serializable]
        private struct MonsterVisualPrefabBinding
        {
            [SerializeField] private string definitionId;
            [SerializeField] private GameObject prefab;

            public string DefinitionId => definitionId;
            public GameObject Prefab => prefab;

            public static MonsterVisualPrefabBinding Create(string definitionId, GameObject prefab)
            {
                return new MonsterVisualPrefabBinding { definitionId = definitionId, prefab = prefab };
            }
        }
    }
}
