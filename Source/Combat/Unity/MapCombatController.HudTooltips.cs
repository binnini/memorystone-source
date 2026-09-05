using System.Collections;
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
        // 호버 레이캐스트 사거리. 맵 오브젝트·필드 오브젝트 판정과 같은 값을 쓴다
        // (MapObjectVisualRegistry.ResolveHoveredController, TryGetFieldObjectAtScreenPos).
        private const float MonsterHoverRayDistance = 500f;

        private void RefreshModelStatusText()
        {
            StatusText = hudPresenter.FormatStatusText(
                State,
                isSequencePlaying,
                resolvingStatusText,
                selectedBoardEvidenceText);
        }

        private void RefreshHudOnly()
        {
            if (State == null)
            {
                StatusText = "Uninitialized";
                return;
            }

            RefreshModelStatusText();
            HudHost.RefreshCurrentTurnPhaseDock();
            RefreshCardRewardPopup();
        }

        /// <summary>
        /// 네임플레이트 배지(이름 위 의도·상태 아이콘) 호버 → 우상단 정보창(실플레이 피드백 ②③).
        /// 이것이 3번 피드백의 답이기도 하다: 예전에는 상태 설명을 읽으려면 몬스터 본체에 커서를
        /// 올려야 했는데, 커서를 정보창 쪽으로 옮기는 순간 호버가 풀려 읽을 수가 없었다. 이제는
        /// 배지 자체가 호버 대상이라 아이콘 위에 머무는 동안 설명이 떠 있는다.
        /// </summary>
        /// <summary>이번 프레임에 뒤끝 오버레이를 띄우고 있는 배지의 주인(없으면 빈 문자열).
        /// 오버레이 재구축을 <b>바뀐 프레임에만</b> 돌리기 위한 열쇠다 — 칸 목록을 매 프레임 비교하는
        /// 것보다 싸고, 「누구의 뒤끝을 그리는가」라는 의미 그대로다.</summary>
        private string traitReachOverlayOwnerId = string.Empty;

        private void UpdateNameplateBadgeHover()
        {
            var previousTraitReachOwner = traitReachOverlayOwnerId;
            UpdateNameplateBadgeHoverCore();

            // 🔴 데이터만 채우고 끝내면 화면은 그대로다(2026-09-02 #1). 맵 오버레이는 상태 변화 때만
            //   다시 그려지는데, 배지 호버는 상태를 바꾸지 않으므로 아무도 재구축을 부르지 않았다 —
            //   같은 메서드가 부르는 SetHoveredMonster(null)도 이미 null이면 조기 반환한다.
            //   그래서 <b>여기서</b> 직접 부른다. 매 프레임이 아니라 주인이 바뀐 프레임에만 돈다.
            if (!string.Equals(previousTraitReachOwner, traitReachOverlayOwnerId, System.StringComparison.Ordinal))
            {
                RefreshMapVisibilityAndHighlights();
            }
        }

        private void UpdateNameplateBadgeHoverCore()
        {
            isNameplateBadgeHoverActive = false;
            traitReachOverlayOwnerId = string.Empty;
            // 매 프레임 비운다 — 유도값이라 커서가 떠나면 다음 프레임에 저절로 꺼진다(취소 경로를
            // 하나 빠뜨려 오버레이가 남는 사고가 구조적으로 불가능해진다).
            hoveredTraitReachCells = System.Array.Empty<HexCoord>();
            if (State == null || actorMarkerPresenter == null || prototype3DCamera == null)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
            {
                return;
            }
            var screenPos = Mouse.current.position.ReadValue();
#else
            var screenPos = (Vector2)Input.mousePosition;
#endif

            if (!actorMarkerPresenter.TryGetHoveredBadge(
                    prototype3DCamera, screenPos, out var badge, out var badgeMonsterId))
            {
                return;
            }

            isNameplateBadgeHoverActive = true;
            tooltipPresenter?.Hide();
            SetHoveredMonster(null);
            ClearFieldObjectHover();

            // 뒤끝 배지에 손을 얹으면 <b>대가가 미치는 칸</b>을 판에 그린다(2026-09-01 #3 사용자 요구).
            // 툴팁과 오버레이가 <b>같은 편 값</b>을 읽으므로 글자와 그림이 갈라질 수 없다.
            CombatState.MonsterAftermathPreview? aftermath = null;
            if (badge.Kind == MonsterNameplateBadgeKind.Trait && State != null)
            {
                // 뒤끝 갈래별 실제 내용(저주 몇 장·훔친 액수)은 툴팁의 「지금 상태」 줄이 쓴다.
                if (State.TryGetMonsterAftermathPreview(badgeMonsterId, out var resolved))
                {
                    aftermath = resolved;
                }

                // 특성 범위 오버레이(2026-09-04) — 뒤끝·담력 시험·홀림이 각자 다른 계산으로 같은
                // 보라 레이어를 채운다. 범위가 없는 특성은 빈 목록이라 주인도 잡지 않는다: 그래야
                // 「그릴 게 없는데 오버레이를 다시 그리는」 헛일이 없다.
                if (State.TryGetMonsterTraitReach(badgeMonsterId, badge.TraitId, out var reachCells))
                {
                    hoveredTraitReachCells = reachCells;
                    traitReachOverlayOwnerId = badgeMonsterId;
                }
            }

            // {값} 토큰이 해소될 실값 — 맷집의 재장전 턴, 약오름의 상한처럼 <b>몬스터마다 다른</b>
            // 수치라 CSV 값 컬럼을 그대로 찍으면 다른 저작에서 화면이 거짓말한다(D7).
            var descriptionValue = 0;
            if (badge.Kind == MonsterNameplateBadgeKind.Trait
                && State != null
                && TryGetMonsterCatalogEntryForBadge(badgeMonsterId, out var badgeEntry))
            {
                descriptionValue = MonsterTraitText.ResolveDescriptionValue(badgeEntry, badge.TraitId);
            }

            EnsureObjectInfoTooltipPresenter();
            objectInfoTooltipPresenter.Show(
                MonsterBadgeTooltipContent.Title(badge),
                ObjectInfoTooltipContent.StatusIconTitleColor,
                MonsterBadgeTooltipContent.BuildLines(badge, aftermath, descriptionValue));
        }

        /// <summary>배지가 붙은 몬스터의 저작 행. 특성 설명문의 {값}을 그 개체 값으로 해소하는 데 쓴다.</summary>
        private bool TryGetMonsterCatalogEntryForBadge(string monsterId, out MonsterCatalogEntry entry)
        {
            entry = default;
            if (State == null || string.IsNullOrEmpty(monsterId))
            {
                return false;
            }

            foreach (var monster in State.Monsters)
            {
                if (string.Equals(monster.Id, monsterId, System.StringComparison.Ordinal))
                {
                    return State.TryGetMonsterCatalogEntry(monster.DefinitionId, out entry);
                }
            }

            return false;
        }

        private void UpdateMonsterTooltip()
        {
            if (isNameplateBadgeHoverActive) { return; }
            if (tooltipPresenter == null || State == null || prototype3DCamera == null) { SetHoveredMonster(null); return; }
            var phase = State.Phase;
            if (phase != CombatPhase.PlayerMovement && phase != CombatPhase.PlayerAction)
            { tooltipPresenter.Hide(); SetHoveredMonster(null); return; }

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) { tooltipPresenter.Hide(); SetHoveredMonster(null); return; }
            var screenPos = Mouse.current.position.ReadValue();
#else
            var screenPos = (Vector2)Input.mousePosition;
#endif

            if (TryGetMonsterAtScreenPos(screenPos, out var monster, out var entry))
            {
                var iconCatalog = statusIconOverlayRenderer != null ? statusIconOverlayRenderer.IconCatalog : null;
                tooltipPresenter.Show(
                    monster,
                    entry,
                    State.ActiveEffects,
                    iconCatalog,
                    ResolveBossPropTooltipInfo(monster));
                SetHoveredMonster(monster.Id);
                NotifyTutorialHover($"monster:{monster.Id}");
            }
            else
            {
                tooltipPresenter.Hide();
                SetHoveredMonster(null);
            }
        }

        /// <summary>
        /// 상태 카드 함정(InjectStatusCard)의 저작 id를 카드 표시명으로 푼다. 저작은 카드 id 또는
        /// effectRef 어느 쪽으로도 가능하므로(<c>TryInjectStatusCard</c>와 같은 계약) 둘 다 본다.
        /// 못 찾으면 빈 문자열 — 툴팁이 id를 그대로 보여주고, 그 편이 존재하지 않는 이름을 지어내는 것보다 낫다.
        /// </summary>
        private string ResolveStatusCardDisplayName(string statusCardId)
        {
            if (State == null || string.IsNullOrWhiteSpace(statusCardId))
            {
                return string.Empty;
            }

            foreach (var entry in State.CardCatalog.Entries)
            {
                if (string.Equals(entry.Id, statusCardId, System.StringComparison.Ordinal))
                {
                    return entry.DisplayName ?? string.Empty;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 호버한 것이 보스 기물(철조각)이면 툴팁에 실을 성숙 카운트다운·폭발 저작을 투영한다.
        /// 기물이 아니거나 소유 보스 프로필을 찾을 수 없으면 null이며, 그 경우 툴팁은 기물 본문을
        /// 그리되 카운트다운 줄만 비운다(없는 값을 0으로 꾸며 내지 않는다).
        /// </summary>
        private MonsterTooltipHudPresenter.BossPropInfo? ResolveBossPropTooltipInfo(MonsterRuntimeState monster)
        {
            if (State == null
                || !MonsterSpawnRoles.IsBossProp(monster.SpawnRole)
                || !State.TryGetBossPropAbsorptionCountdown(monster.Id, out var turnsRemaining, out var maturityTurns))
            {
                return null;
            }

            State.TryGetBossPropBlastAuthoring(monster.Id, out var blastRadius, out var blastDamage);
            return new MonsterTooltipHudPresenter.BossPropInfo(turnsRemaining, maturityTurns, blastRadius, blastDamage);
        }

        // Records the hovered monster and, when it changes, rebuilds the map overlays so the
        // attack-intent overlay narrows to the hovered monster (or restores the full set on clear).
        private void SetHoveredMonster(string monsterId)
        {
            var normalized = string.IsNullOrEmpty(monsterId) ? null : monsterId;
            if (hoveredMonsterId == normalized) return;
            var previous = hoveredMonsterId;
            hoveredMonsterId = normalized;
            if (!string.IsNullOrEmpty(previous))
            {
                // Pointer left the previously hovered monster: lets a "release hover" tutorial step advance.
                NotifyTutorialHoverEnded($"monster:{previous}");
            }
            RefreshMapVisibilityAndHighlights();
        }

        /// <summary>
        /// 커서 아래 몬스터를 <b>모델 크기 판정 상자</b>(<see cref="CharacterHoverTarget"/>)로 레이캐스트해
        /// 찾는다. 옛 방식은 발밑 앵커를 화면에 투영해 고정 60px 원으로 재서, 화면 높이 569px인 보스는
        /// 몸통의 89%가 판정 밖이고(몸통을 가리켜도 툴팁이 안 떴다) 폭 81px인 터렛은 판정이 1.5배 넓어
        /// 옆 빈 땅에도 반응했다. 상자는 대상 자기 크기에서 나오므로 양쪽이 함께 해소된다
        /// (docs/object-outline-aura-plan.md §5.0 · §9.3 R10 실측).
        /// </summary>
        private bool TryGetMonsterAtScreenPos(Vector2 screenPos, out MonsterRuntimeState monster, out MonsterCatalogEntry entry)
        {
            monster = default;
            entry = default;
            if (State == null || prototype3DCamera == null) return false;

            var ray = prototype3DCamera.ScreenPointToRay(screenPos);
            var hits = Physics.RaycastAll(ray, MonsterHoverRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0) return false;

            var bestDistance = float.MaxValue;
            var found = false;
            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.distance >= bestDistance) continue;
                var target = hit.collider.GetComponentInParent<CharacterHoverTarget>();
                if (target == null || !target.gameObject.activeInHierarchy) continue;
                if (!TryResolveHoverableMonster(target.MarkerId, out var candidate)) continue;
                bestDistance = hit.distance;
                monster = candidate;
                found = true;
            }

            if (!found) return false;
            return State.TryGetMonsterCatalogEntry(monster.DefinitionId, out entry);
        }

        /// <summary>
        /// 마커 id로 호버 가능한 몬스터를 되찾는다. 죽었거나 아직 밝혀지지 않은 몬스터는 거절한다 —
        /// 마커가 프레임 한 박자 늦게 정리될 수 있으므로 판정은 상태를 정본으로 다시 물어야 한다.
        /// </summary>
        private bool TryResolveHoverableMonster(string markerId, out MonsterRuntimeState monster)
        {
            monster = default;
            if (State == null || string.IsNullOrEmpty(markerId)) return false;

            foreach (var m in State.Monsters)
            {
                if (!string.Equals(m.Id, markerId, System.StringComparison.Ordinal)) continue;
                if (m.IsDead) return false;
                // Only monsters on revealed tiles are interactive; an unrevealed monster shows nothing
                // on the map, so it must not surface a tooltip either (debug reveal-all bypasses this).
                if (!revealAllMapCellsInDebugMode && State.GetVisibility(m.Coord) != HexCellVisibility.Revealed) return false;
                monster = m;
                return true;
            }

            return false;
        }

        private void EnsureTooltipPresenter()
        {
            if (tooltipPresenter != null) return;
            var go = new GameObject("MonsterTooltipHudPresenter");
            go.transform.SetParent(transform);
            tooltipPresenter = go.AddComponent<MonsterTooltipHudPresenter>();
        }

        private void EnsureFieldObjectVisualPresenter()
        {
            if (fieldObjectVisualPresenter != null) return;
            var parent = atlasTilePresentationView != null ? atlasTilePresentationView.transform : transform;
            var go = new GameObject("FieldObjectVisualPresenter");
            go.transform.SetParent(parent, false);
            fieldObjectVisualPresenter = go.AddComponent<FieldObjectVisualPresenter>();
            fieldObjectVisualPresenter.Configure(atlasTilePresentationView, BuildFieldObjectVisualMappingPairs());
        }

        private IEnumerable<KeyValuePair<string, GameObject>> BuildFieldObjectVisualMappingPairs()
        {
            if (fieldObjectVisualMappings == null) yield break;
            foreach (var mapping in fieldObjectVisualMappings)
            {
                if (mapping != null && !string.IsNullOrWhiteSpace(mapping.VisualRef) && mapping.Prefab != null)
                {
                    yield return new KeyValuePair<string, GameObject>(mapping.VisualRef, mapping.Prefab);
                }
            }
        }

        // Pure projection of the runtime field-object registry into persistent tile markers. Runs each
        // RefreshView after presentation commits so spawned marker anchors line up with current tiles.
        private void SyncFieldObjectVisuals()
        {
            if (atlasTilePresentationView == null || State == null) return;
            EnsureFieldObjectVisualPresenter();
            fieldObjectVisualPresenter.Sync(State.FieldObjects.Objects);
        }

        private void EnsureFieldObjectTooltipPresenter()
        {
            if (fieldObjectTooltipPresenter != null) return;
            var go = new GameObject("FieldObjectTooltipHudPresenter");
            go.transform.SetParent(transform);
            fieldObjectTooltipPresenter = go.AddComponent<FieldObjectTooltipHudPresenter>();
        }

        // Player-placed fields stamp the originating card id into FieldObject.VisualRef (CombatState field
        // placement), so the tooltip can title itself with the card's display name. Card-less fields
        // (e.g. monster/effect-runtime spawns with an empty VisualRef) fall back to the kind title.
        private string ResolveFieldObjectSourceCardName(FieldObject fieldObject)
        {
            var catalog = State?.CardCatalog;
            if (catalog == null || string.IsNullOrWhiteSpace(fieldObject.VisualRef))
            {
                return string.Empty;
            }

            foreach (var entry in catalog.Entries)
            {
                if (string.Equals(entry.Id, fieldObject.VisualRef, System.StringComparison.Ordinal))
                {
                    return entry.DisplayName;
                }
            }

            return string.Empty;
        }

        private void UpdateStatusIconOverlayHover()
        {
            isStatusIconOverlayHoverActive = false;
            if (State == null || isNameplateBadgeHoverActive)
            {
                return;
            }

            var renderer = ResolveStatusIconOverlayRendererForHover();
            if (renderer == null)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
            {
                return;
            }
            var screenPos = Mouse.current.position.ReadValue();
#else
            var screenPos = (Vector2)Input.mousePosition;
#endif

            if (!TryGetStatusIconHover(renderer, screenPos, out var payload))
            {
                return;
            }

            isStatusIconOverlayHoverActive = true;
            tooltipPresenter?.Hide();
            SetHoveredMonster(null);
            ClearFieldObjectHover();
            EnsureObjectInfoTooltipPresenter();
            if (payload.IsWeakSpot)
            {
                objectInfoTooltipPresenter.Show(
                    ObjectInfoTooltipContent.WeakSpotTitle,
                    ObjectInfoTooltipContent.StatusIconTitleColor,
                    ObjectInfoTooltipContent.BuildWeakSpotLines(
                        payload.WeakSpotDamagePercent, payload.WeakSpotTurnsRemaining));
            }
            else if (payload.IsSafeZoneConfirmed)
            {
                objectInfoTooltipPresenter.Show(
                    ObjectInfoTooltipContent.SafeZoneConfirmedTitle,
                    ObjectInfoTooltipContent.StatusIconTitleColor,
                    ObjectInfoTooltipContent.BuildSafeZoneConfirmedLines());
            }
            else if (payload.IsSafeZoneCandidate)
            {
                objectInfoTooltipPresenter.Show(
                    ObjectInfoTooltipContent.SafeZoneCandidateTitle,
                    ObjectInfoTooltipContent.StatusIconTitleColor,
                    ObjectInfoTooltipContent.BuildSafeZoneCandidateLines());
            }
            else if (payload.IsAnnihilation)
            {
                objectInfoTooltipPresenter.Show(
                    ObjectInfoTooltipContent.AnnihilationTitle,
                    ObjectInfoTooltipContent.StatusIconTitleColor,
                    ObjectInfoTooltipContent.BuildAnnihilationLines(
                        payload.AnnihilationTurnsRemaining, payload.AnnihilationDamage));
            }
            else if (payload.IsKnockback)
            {
                // 부호가 방향이다(§16.1) — 끌어당김은 배지 툴팁과 같은 어휘로 갈라 말한다
                // (MonsterBadgeTooltipContent.Title도 같은 기준으로 갈린다).
                objectInfoTooltipPresenter.Show(
                    payload.IsPull
                        ? ObjectInfoTooltipContent.PullTitle
                        : ObjectInfoTooltipContent.KnockbackTitle,
                    ObjectInfoTooltipContent.StatusIconTitleColor,
                    payload.IsPull
                        ? ObjectInfoTooltipContent.BuildPullLines()
                        : ObjectInfoTooltipContent.BuildKnockbackLines());
            }
            else if (payload.IsCurse)
            {
                objectInfoTooltipPresenter.Show(
                    ObjectInfoTooltipContent.CurseTitle,
                    ObjectInfoTooltipContent.StatusIconTitleColor,
                    ObjectInfoTooltipContent.BuildCurseLines());
            }
            else
            {
                objectInfoTooltipPresenter.Show(
                    StatusEffectTooltipContent.Title(payload.Effect.Kind),
                    ObjectInfoTooltipContent.StatusIconTitleColor,
                    ObjectInfoTooltipContent.BuildStatusEffectLines(payload.Effect));
            }
        }

        private StatusIconOverlayRenderer ResolveStatusIconOverlayRendererForHover()
        {
            if (statusIconOverlayRenderer != null && statusIconOverlayRenderer.isActiveAndEnabled)
            {
                return statusIconOverlayRenderer;
            }

            var parent = atlasTilePresentationView != null ? atlasTilePresentationView.transform : transform;
            statusIconOverlayRenderer = parent != null
                ? parent.GetComponentInChildren<StatusIconOverlayRenderer>(includeInactive: false)
                : null;
            return statusIconOverlayRenderer != null && statusIconOverlayRenderer.isActiveAndEnabled
                ? statusIconOverlayRenderer
                : null;
        }

        private bool TryGetStatusIconHover(
            StatusIconOverlayRenderer renderer,
            Vector2 screenPos,
            out StatusIconOverlayRenderer.TooltipPayload payload)
        {
            payload = default;
            if (renderer == null)
            {
                return false;
            }

            if (prototype3DCamera != null && renderer.TryGetHoveredTooltip(prototype3DCamera, screenPos, out payload))
            {
                return true;
            }

            if (Camera.main != null && Camera.main != prototype3DCamera &&
                renderer.TryGetHoveredTooltip(Camera.main, screenPos, out payload))
            {
                return true;
            }

            var cameraCount = Camera.allCamerasCount;
            if (cameraCount <= 0)
            {
                return false;
            }

            var cameras = new Camera[cameraCount];
            Camera.GetAllCameras(cameras);
            for (var i = 0; i < cameras.Length; i++)
            {
                var candidate = cameras[i];
                if (candidate == null || !candidate.isActiveAndEnabled ||
                    candidate == prototype3DCamera || candidate == Camera.main)
                {
                    continue;
                }

                if (renderer.TryGetHoveredTooltip(candidate, screenPos, out payload))
                {
                    return true;
                }
            }

            return false;
        }

        // Hover info for placed field objects: tooltip (effect + remaining turns) + green range overlay.
        // Shown at all times (including enemy turns) ??intentionally not phase-gated, unlike the monster
        // tooltip. The green range layer is non-tactical, so per-refresh overlay rebuilds leave it intact.
        private void UpdateFieldObjectHover()
        {
            if (State == null || prototype3DCamera == null || fieldObjectVisualPresenter == null
                || isStatusIconOverlayHoverActive || isNameplateBadgeHoverActive)
            {
                ClearFieldObjectHover();
                return;
            }

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) { ClearFieldObjectHover(); return; }
            var screenPos = Mouse.current.position.ReadValue();
#else
            var screenPos = (Vector2)Input.mousePosition;
#endif

            if (TryGetFieldObjectAtScreenPos(screenPos, out var fieldObject))
            {
                EnsureFieldObjectTooltipPresenter();
                fieldObjectTooltipPresenter.Show(fieldObject, ResolveFieldObjectSourceCardName(fieldObject));
                NotifyTutorialHover($"field:{fieldObject.Position.Q}_{fieldObject.Position.R}");
                if (lastFieldObjectHoverCoord != fieldObject.Position)
                {
                    EnsureOverlayPresenter()?.ShowFieldObjectRange(ComputeFieldObjectFootprint(fieldObject));
                    lastFieldObjectHoverCoord = fieldObject.Position;
                }
            }
            else
            {
                ClearFieldObjectHover();
            }
        }

        private void ClearFieldObjectHover()
        {
            fieldObjectTooltipPresenter?.Hide();
            if (lastFieldObjectHoverCoord != null)
            {
                EnsureOverlayPresenter()?.ClearFieldObjectRange();
                lastFieldObjectHoverCoord = null;
            }
        }

        private bool TryGetFieldObjectAtScreenPos(Vector2 screenPos, out FieldObject fieldObject)
        {
            fieldObject = default;
            var ray = prototype3DCamera.ScreenPointToRay(screenPos);
            var hits = Physics.RaycastAll(ray, 500f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0) return false;

            var bestDistance = float.MaxValue;
            var found = false;
            foreach (var hit in hits)
            {
                var view = hit.collider != null ? hit.collider.GetComponentInParent<FieldObjectMarkerView>() : null;
                if (view == null) continue;
                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    fieldObject = view.Data;
                    found = true;
                }
            }

            return found;
        }

        private IEnumerable<HexCoord> ComputeFieldObjectFootprint(FieldObject fieldObject)
        {
            var map = State?.Map;
            if (map == null) yield break;
            foreach (var cell in map.AllCells)
            {
                if (fieldObject.Contains(cell.Coord))
                {
                    yield return cell.Coord;
                }
            }
        }

        private void EnsureObjectInfoTooltipPresenter()
        {
            if (objectInfoTooltipPresenter != null) return;
            var go = new GameObject("ObjectInfoTooltipHudPresenter");
            go.transform.SetParent(transform);
            objectInfoTooltipPresenter = go.AddComponent<ObjectInfoTooltipHudPresenter>();
            objectInfoTooltipPresenter.SetSortingOrder(650);
            objectInfoTooltipPresenter.KoreanFontApplier ??= TooltipFontProvider.Apply;
            // §28.6 채택안: 맵 호버 툴팁은 우상단 고정 패널(카드 키워드 툴팁 인스턴스는 커서 추종 유지).
            objectInfoTooltipPresenter.FixedTopRightPlacement = true;
        }

        // Hover info for scout-revealed traps and treasure chests. Neither is a monster
        // nor a placed field object, so they get their own descriptive panel. Suppressed while the
        // monster or field-object tooltip already owns the hover so panels never stack.
        private void UpdateObjectInfoHover()
        {
            if (isStatusIconOverlayHoverActive || isNameplateBadgeHoverActive)
            {
                return;
            }

            if (State == null || prototype3DCamera == null
                || hoveredMonsterId != null || lastFieldObjectHoverCoord != null)
            {
                objectInfoTooltipPresenter?.Hide();
                return;
            }

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) { objectInfoTooltipPresenter?.Hide(); return; }
            var screenPos = Mouse.current.position.ReadValue();
#else
            var screenPos = (Vector2)Input.mousePosition;
#endif

            // Treasure chests only raycast-hit while their visual is active, and the visibility pass
            // only activates them on Revealed/Hinted tiles ??so a hit already satisfies the requirement.
            if (TryGetTreasureChestAtScreenPos(screenPos, out var chest))
            {
                EnsureObjectInfoTooltipPresenter();
                objectInfoTooltipPresenter.Show(ObjectInfoTooltipContent.TreasureChestTitle, ObjectInfoTooltipContent.TreasureTitleColor, ObjectInfoTooltipContent.BuildTreasureChestLines());
                NotifyTutorialHover("object:treasure");
                return;
            }

            if (TryGetMemoryStoneAtScreenPos(screenPos, out var memoryStone))
            {
                EnsureObjectInfoTooltipPresenter();
                objectInfoTooltipPresenter.Show(ObjectInfoTooltipContent.MemoryStoneTitle, ObjectInfoTooltipContent.MemoryStoneTitleColor, ObjectInfoTooltipContent.BuildMemoryStoneLines());
                NotifyTutorialHover("object:memorystone");
                return;
            }

            if (TryGetShopAtScreenPos(screenPos, out _))
            {
                EnsureObjectInfoTooltipPresenter();
                objectInfoTooltipPresenter.Show(ObjectInfoTooltipContent.ShopTitle, ObjectInfoTooltipContent.ShopTitleColor, ObjectInfoTooltipContent.BuildShopLines());
                NotifyTutorialHover("object:shop");
                return;
            }

            if (TryGetServiceObjectAtScreenPos(screenPos, out var serviceObject))
            {
                EnsureObjectInfoTooltipPresenter();
                if (serviceObject.IsCamperVan)
                {
                    objectInfoTooltipPresenter.Show(ObjectInfoTooltipContent.CamperVanTitle, ObjectInfoTooltipContent.CamperVanTitleColor, ObjectInfoTooltipContent.BuildCamperVanLines());
                    NotifyTutorialHover("object:campervan");
                }
                else
                {
                    objectInfoTooltipPresenter.Show(ObjectInfoTooltipContent.WorkshopTitle, ObjectInfoTooltipContent.WorkshopTitleColor, ObjectInfoTooltipContent.BuildWorkshopLines());
                    NotifyTutorialHover("object:workshop");
                }

                return;
            }

            if (TryGetRevealedTrapAtScreenPos(screenPos, out var trap))
            {
                EnsureObjectInfoTooltipPresenter();
                objectInfoTooltipPresenter.Show(
                    ObjectInfoTooltipContent.TrapTitle,
                    ObjectInfoTooltipContent.TrapTitleColor,
                    ObjectInfoTooltipContent.BuildTrapLines(trap, ResolveStatusCardDisplayName));
                NotifyTutorialHover("object:trap");
                return;
            }

            // 철조각 살포 예고 칸(2026-09-05 후속 #1). 오브젝트가 아니라 <b>상태 술어</b>라 좌표로 찾는다 —
            // 오버레이(붉은 해치)와 같은 목록을 읽으므로 해치가 선 칸에서만 뜬다.
            if (TryGetBossPropVolleyTelegraphAtScreenPos(screenPos))
            {
                EnsureObjectInfoTooltipPresenter();
                objectInfoTooltipPresenter.Show(
                    ObjectInfoTooltipContent.BossPropVolleyTelegraphTitle,
                    ObjectInfoTooltipContent.BossPropVolleyTelegraphTitleColor,
                    ObjectInfoTooltipContent.BuildBossPropVolleyTelegraphLines());
                NotifyTutorialHover("object:boss_prop_volley_telegraph");
                return;
            }

            // 부서진 땅(2026-09-01 #18)은 <b>가장 마지막</b>에 본다 — 상자·상점·함정이 그 위에 겹쳐 있을
            // 수 있고, 그때 플레이어가 알고 싶은 것은 지형이 아니라 그 물건이다.
            if (TryGetRupturedGroundAtScreenPos(screenPos, out var rupture))
            {
                EnsureObjectInfoTooltipPresenter();
                objectInfoTooltipPresenter.Show(
                    ObjectInfoTooltipContent.RupturedGroundTitle,
                    ObjectInfoTooltipContent.RupturedGroundTitleColor,
                    ObjectInfoTooltipContent.BuildRupturedGroundLines(rupture.StatusKind, rupture.RemainingTurns));
                NotifyTutorialHover("object:ruptured_ground");
                return;
            }

            objectInfoTooltipPresenter?.Hide();
        }

        /// <summary>
        /// 커서 아래 칸이 철조각 살포 예고 칸인가(2026-09-05 후속 #1). 오버레이와 <b>같은</b> 술어
        /// (<c>GetBossPropVolleyTelegraphCells</c>)와 같은 페이즈 게이트(플레이어 결정 페이즈)를 쓴다 —
        /// 해치가 없는데 툴팁만 뜨거나 그 반대가 되지 않는다.
        /// </summary>
        private bool TryGetBossPropVolleyTelegraphAtScreenPos(Vector2 screenPos)
        {
            if (State == null
                || (State.Phase != CombatPhase.PlayerMovement && State.Phase != CombatPhase.PlayerAction)
                || !TrySelectedScreenToHex(screenPos, out var coord))
            {
                return false;
            }

            var cells = State.GetBossPropVolleyTelegraphCells();
            for (var i = 0; i < cells.Count; i++)
            {
                if (cells[i].Equals(coord))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 커서 아래 칸의 부서진 땅(2026-09-01 #18). 오브젝트가 아니라 지형 상태라 레이캐스트가 아니라
        /// <b>좌표</b>로 찾는다 — 세울 물건이 없으므로 맞출 콜라이더도 없다.
        /// </summary>
        private bool TryGetRupturedGroundAtScreenPos(Vector2 screenPos, out FieldObject rupture)
        {
            rupture = default;
            if (State == null || !TrySelectedScreenToHex(screenPos, out var coord))
            {
                return false;
            }

            // 안개 속 칸은 말하지 않는다 — 안 보이는 지형을 설명하면 정찰의 값이 샌다.
            if (GetVisibilitySafeCellInfo(coord).Visibility != HexCellVisibility.Revealed)
            {
                return false;
            }

            foreach (var fieldObject in State.FieldObjects.Objects)
            {
                if (fieldObject.Kind == FieldObjectKind.StatusZone
                    && !fieldObject.IsExpired
                    && fieldObject.Contains(coord))
                {
                    rupture = fieldObject;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetTreasureChestAtScreenPos(Vector2 screenPos, out HexMapObjectData chest)
        {
            chest = default;
            if (atlasTilePresentationView == null) return false;
            if (!atlasTilePresentationView.TryRaycastMapObject(prototype3DCamera, screenPos, out var obj)) return false;
            if (!string.Equals(obj.ObjectType, "TreasureChest", System.StringComparison.Ordinal)) return false;
            chest = obj;
            return true;
        }

        private bool TryGetShopAtScreenPos(Vector2 screenPos, out HexMapObjectData shop)
        {
            shop = default;
            if (atlasTilePresentationView == null) return false;
            if (!atlasTilePresentationView.TryRaycastMapObject(prototype3DCamera, screenPos, out var obj)) return false;
            if (!obj.IsShop) return false;
            shop = obj;
            return true;
        }

        private bool TryGetServiceObjectAtScreenPos(Vector2 screenPos, out HexMapObjectData serviceObject)
        {
            serviceObject = default;
            if (atlasTilePresentationView == null) return false;
            if (!atlasTilePresentationView.TryRaycastMapObject(prototype3DCamera, screenPos, out var obj)) return false;
            if (!obj.IsCamperVan && !obj.IsWorkshop) return false;
            serviceObject = obj;
            return true;
        }

        private bool TryGetMemoryStoneAtScreenPos(Vector2 screenPos, out HexMapObjectData memoryStone)
        {
            memoryStone = default;
            if (atlasTilePresentationView == null) return false;
            if (!atlasTilePresentationView.TryRaycastMapObject(prototype3DCamera, screenPos, out var obj)) return false;
            if (!obj.IsMemoryStone && !string.Equals(obj.ObjectType, "MemoryStone", System.StringComparison.Ordinal)) return false;
            memoryStone = obj;
            return true;
        }

        private bool TryGetRevealedTrapAtScreenPos(Vector2 screenPos, out HexTrapData trap)
        {
            trap = default;
            if (!TrySelectedScreenToHex(screenPos, out var coord)) return false;
            // Trap discovery is recorded only on the trap's origin tile (RevealTrapsInArea), which is
            // also where the marker is drawn ??so match the hovered tile against trap origins.
            if (!GetVisibilitySafeCellInfo(coord).TrapRevealed) return false;
            foreach (var candidate in State.AllTrapRefs)
            {
                if (candidate.Coord.Equals(coord))
                {
                    trap = candidate;
                    return true;
                }
            }

            return false;
        }

        private void EnsureCardRewardPopupView()
        {
            var scene = gameObject.scene;
            var sceneRewardViews = FindObjectsByType<CardRewardPopupView>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(view => view != null && view.gameObject.scene == scene)
                .ToArray();
            var prefabOverlayView = sceneRewardViews
                .FirstOrDefault(view => view.name == CardRewardOverlayRootName);

            if (prefabOverlayView != null)
            {
                cardRewardPopupView = prefabOverlayView;
            }

            if (prefabOverlayView == null)
            {
                prefabOverlayView = InstantiateCardRewardOverlayPrefab(scene);
                if (prefabOverlayView != null)
                {
                    cardRewardPopupView = prefabOverlayView;
                }
            }

            if (cardRewardPopupView == null)
            {
                cardRewardPopupView = GetComponentInChildren<CardRewardPopupView>(true);
            }

            if (cardRewardPopupView == null)
            {
                cardRewardPopupView = sceneRewardViews.FirstOrDefault();
            }

            if (cardRewardPopupView != null)
            {
                CardRewardPresentation.AutoBindFromHierarchy();
            }
        }

        private void EnsureGameOverOverlayRoot()
        {
            var scene = gameObject.scene;
            if (gameOverOverlayRoot == null || gameOverOverlayRoot.scene != scene)
            {
                var existing = FindSceneRectTransform(scene, GameOverOverlayRootName);
                if (existing != null)
                {
                    gameOverOverlayRoot = existing.gameObject;
                }
            }

            if (gameOverOverlayRoot == null)
            {
                gameOverOverlayRoot = InstantiateGameOverOverlayPrefab(scene);
            }

            if (gameOverOverlayRoot == null)
            {
                return;
            }

            ConfigureGameOverOverlayButtons(gameOverOverlayRoot);
            gameOverOverlayRoot.SetActive(false);
        }

        private void EnsureGameVictoryOverlayRoot()
        {
            var scene = gameObject.scene;
            if (gameVictoryOverlayRoot == null || gameVictoryOverlayRoot.scene != scene)
            {
                var existing = FindSceneRectTransform(scene, GameVictoryOverlayRootName);
                if (existing != null)
                {
                    gameVictoryOverlayRoot = existing.gameObject;
                }
            }

            if (gameVictoryOverlayRoot == null)
            {
                gameVictoryOverlayRoot = InstantiateGameVictoryOverlayPrefab(scene);
            }

            if (gameVictoryOverlayRoot == null)
            {
                return;
            }

            ConfigureGameVictoryOverlayButtons(gameVictoryOverlayRoot);
            gameVictoryOverlayRoot.SetActive(false);
        }

        private GameObject InstantiateGameOverOverlayPrefab(UnityEngine.SceneManagement.Scene scene)
        {
            var gameplayLayers = FindSceneRectTransform(scene, GameplaySceneContract.GameplayLayerRootName);
            if (gameplayLayers == null)
            {
                return null;
            }

            GameObject instance = null;
#if UNITY_EDITOR
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(GameOverOverlayPrefabAssetPath);
            if (prefabAsset != null)
            {
                instance = PrefabUtility.InstantiatePrefab(prefabAsset, gameplayLayers) as GameObject;
            }
#endif
            if (instance == null)
            {
                return null;
            }

            instance.name = GameOverOverlayRootName;
            var rect = instance.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.SetParent(gameplayLayers, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            instance.SetActive(false);
#if UNITY_EDITOR
            EditorUtility.SetDirty(instance);
#endif
            return instance;
        }

        private GameObject InstantiateGameVictoryOverlayPrefab(UnityEngine.SceneManagement.Scene scene)
        {
            var gameplayLayers = FindSceneRectTransform(scene, GameplaySceneContract.GameplayLayerRootName);
            if (gameplayLayers == null)
            {
                return null;
            }

            GameObject instance = null;
#if UNITY_EDITOR
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(GameVictoryOverlayPrefabAssetPath);
            if (prefabAsset != null)
            {
                instance = PrefabUtility.InstantiatePrefab(prefabAsset, gameplayLayers) as GameObject;
            }
#endif
            if (instance == null)
            {
                return null;
            }

            instance.name = GameVictoryOverlayRootName;
            var rect = instance.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.SetParent(gameplayLayers, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            instance.SetActive(false);
#if UNITY_EDITOR
            EditorUtility.SetDirty(instance);
#endif
            return instance;
        }

        private void ShowGameOverOverlay()
        {
            // A trailer take films the death beat itself; the result screen is chrome that must never end
            // up in the plate. The alpha-based UI hide can't be trusted here — this overlay activates
            // mid-take and the hide controller only rescans for new UI on an interval.
            if (trailerFilmingActive)
            {
                return;
            }

            EnsureGameOverOverlayRoot();
            if (gameOverOverlayRoot == null)
            {
                return;
            }

            ConfigureGameOverOverlayButtons(gameOverOverlayRoot);
            UpdateGameOverInfoDock(gameOverOverlayRoot);
            EnsureEventSystem();
            gameOverOverlayRoot.SetActive(true);
            gameOverOverlayRoot.transform.SetAsLastSibling();
        }

        private void ShowGameVictoryOverlay()
        {
            EnsureGameVictoryOverlayRoot();
            if (gameVictoryOverlayRoot == null)
            {
                return;
            }

            ConfigureGameVictoryOverlayButtons(gameVictoryOverlayRoot);
            UpdateGameVictoryInfoDock(gameVictoryOverlayRoot);
            EnsureEventSystem();
            gameVictoryOverlayRoot.SetActive(true);
            gameVictoryOverlayRoot.transform.SetAsLastSibling();
        }

        // Victory result window: fill the authored RewardCount / TimeLog labels (they otherwise stay on their
        // placeholder text "추가한 카드 : X장" / "플레이 시간 : xx:xx" because nothing populated them).
        private void UpdateGameVictoryInfoDock(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            SetGameOverInfoText(root, "RewardCount", $"추가한 카드 : {RewardFlow.AcquiredRewardCardCount}장");
            SetGameOverInfoText(root, "TimeLog", $"플레이 시간 : {FormatElapsedPlayTime()}");
        }

        private void HideGameOverOverlay()
        {
            if (gameOverOverlayRoot != null)
            {
                gameOverOverlayRoot.SetActive(false);
            }
        }

        private void HideGameVictoryOverlay()
        {
            Cinematics.AbortMemoryStoneVictoryPresentation();
            if (gameVictoryOverlayRoot != null)
            {
                gameVictoryOverlayRoot.SetActive(false);
            }
        }

        private void UpdateGameOverInfoDock(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            SetGameOverInfoText(root, "TimeLog", $"\uACBD\uACFC \uC2DC\uAC04: {FormatElapsedPlayTime()}");
            SetGameOverInfoText(root, "DeathLog", $"\uC0AC\uB9DD \uC6D0\uC778: {ResolveGameOverDeathLogText()}");
            SetGameOverInfoText(root, "RewardCount", $"추가한 카드 : {RewardFlow.AcquiredRewardCardCount}장");
        }

        private void SetGameOverInfoText(GameObject root, string objectName, string text)
        {
            var label = root.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .FirstOrDefault(candidate => candidate != null && candidate.name == objectName);
            if (label != null)
            {
                label.text = text ?? string.Empty;
            }
        }

        private string FormatElapsedPlayTime()
        {
            var elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - combatStartRealtimeSeconds);
            var totalSeconds = Mathf.FloorToInt(elapsed);
            return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
        }

        private string ResolveGameOverDeathLogText()
        {
            return string.IsNullOrWhiteSpace(lastPlayerDeathSourceText)
                ? "\uC54C \uC218 \uC5C6\uB294 \uC801 \uACF5\uACA9"
                : lastPlayerDeathSourceText;
        }

        private void ConfigureGameOverOverlayButtons(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var buttons = root.GetComponentsInChildren<Button>(includeInactive: true);
            for (var i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                if (button == null)
                {
                    continue;
                }

                if (button.name == "Restart Button")
                {
                    button.onClick.RemoveAllListeners();
                    button.interactable = true;
                    button.onClick.AddListener(RestartDemo);
                }
                else if (button.name == "Lobby Button")
                {
                    button.onClick.RemoveAllListeners();
                    button.interactable = true;
                    button.onClick.AddListener(ReturnToLobby);
                }
            }
        }

        private void ConfigureGameVictoryOverlayButtons(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var buttons = root.GetComponentsInChildren<Button>(includeInactive: true);
            for (var i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                if (button == null)
                {
                    continue;
                }

                if (button.name == "Restart Button")
                {
                    button.onClick.RemoveAllListeners();
                    button.interactable = true;
                    button.onClick.AddListener(RestartDemo);
                    SetVictoryButtonAnchoredX(button, -120f);
                }
                else if (button.name == "Lobby Button")
                {
                    button.onClick.RemoveAllListeners();
                    button.interactable = true;
                    button.onClick.AddListener(ReturnToLobby);
                    SetVictoryButtonAnchoredX(button, 120f);
                }
                else if (button.name == "Next Button")
                {
                    // The "다음으로" button was removed from the victory UI; only 처음부터/로비로 remain.
                    // The overlay is scene-embedded in player builds and prefab-instantiated in the
                    // editor, so disabling it here guarantees it is gone in both paths regardless of
                    // whether the on-disk prefab/scene still carries the button. The two remaining
                    // buttons are re-centred (from -220/+220 to -120/+120) to close the middle gap.
                    button.onClick.RemoveAllListeners();
                    button.gameObject.SetActive(false);
                }
            }
        }

        private static void SetVictoryButtonAnchoredX(Button button, float anchoredX)
        {
            if (button == null || button.transform is not RectTransform rect)
            {
                return;
            }

            var position = rect.anchoredPosition;
            position.x = anchoredX;
            rect.anchoredPosition = position;
        }

        private CardRewardPopupView InstantiateCardRewardOverlayPrefab(UnityEngine.SceneManagement.Scene scene)
        {
            var gameplayLayers = FindSceneRectTransform(scene, GameplaySceneContract.GameplayLayerRootName);
            if (gameplayLayers == null)
            {
                return null;
            }

            GameObject instance = null;
#if UNITY_EDITOR
            // In edit mode keep the prefab connection so the overlay can be authored in the scene.
            if (!Application.isPlaying)
            {
                var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CardRewardOverlayPrefabAssetPath);
                if (prefabAsset != null)
                {
                    instance = PrefabUtility.InstantiatePrefab(prefabAsset, gameplayLayers) as GameObject;
                }
            }
#endif
            // Resources load works in player builds, where AssetDatabase is unavailable. Without this
            // the build fell back to the stale scene-embedded reward view (old reward UI).
            if (instance == null)
            {
                var prefab = Resources.Load<GameObject>(CardRewardOverlayResourcesPath);
                if (prefab == null)
                {
                    return null;
                }

                instance = Instantiate(prefab, gameplayLayers, false);
            }

            instance.name = CardRewardOverlayRootName;
            var rect = instance.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.SetParent(gameplayLayers, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            var view = instance.GetComponent<CardRewardPopupView>() ?? instance.AddComponent<CardRewardPopupView>();
            view.AutoBindFromHierarchy();
            instance.SetActive(false);
#if UNITY_EDITOR
            EditorUtility.SetDirty(instance);
#endif
            return view;
        }

        internal static RectTransform FindSceneRectTransform(UnityEngine.SceneManagement.Scene scene, string objectName)
        {
            if (!scene.IsValid() || string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                var rect = root.GetComponentsInChildren<RectTransform>(includeInactive: true)
                    .FirstOrDefault(candidate => candidate.name == objectName);
                if (rect != null)
                {
                    return rect;
                }
            }

            return null;
        }

        private void EnsureDebugControlPanel()
        {
            // The debug panel is an editor/development-build tool only. In release player builds we never
            // resolve, auto-create, or bind it, so it cannot draw or affect play. (Its own OnGUI is also
            // guarded, so a scene-wired panel stays inert in release too.)
            if (!CombatDebugControlPanel.DebugUiAvailable)
            {
                return;
            }

            if (debugControlPanel == null)
            {
                debugControlPanel = GetComponentInChildren<CombatDebugControlPanel>(true);
            }

            if (debugControlPanel == null)
            {
                if (!autoCreateDebugControlPanel)
                {
                    return;
                }

                var panelObject = new GameObject("Combat Debug Control Panel");
                panelObject.transform.SetParent(transform, false);
                debugControlPanel = panelObject.AddComponent<CombatDebugControlPanel>();
            }

            debugControlPanel.Bind(this);
        }

        private void EnsureAudioPresenter()
        {
            if (audioPresenter == null)
            {
                audioPresenter = GetComponentInChildren<CombatAudioPresenter>(true);
            }

            if (audioPresenter == null && autoCreateAudioPresenter)
            {
                var audioObject = new GameObject("M2 Map Combat Audio Presenter");
                audioObject.transform.SetParent(transform, false);
                audioPresenter = audioObject.AddComponent<CombatAudioPresenter>();
            }

            if (audioPresenter != null)
            {
                audioPresenter.Bind(this, soundCatalog);
            }
        }

        private static void EnsureEventSystem()
        {
            var eventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                eventSystem = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            }

            if (eventSystem == null)
            {
                eventSystem = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            }

            if (eventSystem.transform.parent != null)
            {
                eventSystem.transform.SetParent(null, true);
            }

            eventSystem.gameObject.SetActive(true);
#if ENABLE_INPUT_SYSTEM
            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }
#else
            if (eventSystem.GetComponent<StandaloneInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }
#endif
        }
    }
}
