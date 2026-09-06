using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Combat.Unity
{
    public sealed class CombatActorMarkerPresenter
    {
        private const string DefaultMarkerId = "enemy";
        private const string MarkerBadgeName = "Monster Marker Badge";
        private const string MarkerNameplateName = "Monster Nameplate";
        private const string MarkerLabelName = "Monster Name Text";
        private const string MarkerBossLabelName = "Monster Boss Text";
        private const string MarkerUnknownMarkName = "Monster Unknown Mark";
        private const string MarkerHealthBarName = "Monster Health Bar";
        private const string MarkerStatusRowName = "Monster Badge Row";

        /// <summary>
        /// 배지 아이콘 한 칸(px). 예전 상태 아이콘(14px)보다 크다 — 이름 위로 올라오면서 숫자를
        /// 함께 싣게 됐고, 14px에서는 숫자가 뭉갠다(Q4가 턴 수를 뺐던 이유가 그것이었다).
        /// 2026-08-19 #2: 실플레이 카메라(pitch45·FOV50)에서 20px 배지·15px 숫자가 판독 불가
        /// 판정 — ×1.5 상향(20→30·15→22). 2026-08-20 #3: 그래도 부족 판정 — 한 단계 더(30→40·22→30,
        /// 이름 텍스트 22→32 동반). 이 네 상수가 배지 크기의 정본이다(실플레이 재판정 대상).
        /// </summary>
        private const float StatusIconPixelSize = 40f;

        /// <summary>표시 상한. 넘으면 마지막 칸이 <c>+N</c>이 된다.</summary>
        private const int MaxStatusIcons = 6;

        private const float StatusIconGapPx = 6f;

        /// <summary>배지에 붙는 숫자 글자 크기와 한 글자가 먹는 폭(px).</summary>
        private const float BadgeNumberFontSize = 30f;
        private const float BadgeNumberCharWidthPx = 17f;

        /// <summary>배지 행 밑변이 플레이트 상단에서 떨어지는 거리(px). 이름과 붙어 보이지 않을 만큼만.</summary>
        private const float BadgeRowBaseY = 4f;

        /// <summary>보스 라벨이 켜져 있을 때 배지가 비켜서는 높이(px). 라벨 자리(+28, 높이 28)와 같다.</summary>
        private const float BossLabelReservedHeight = 30f;
        private const string MarkerHealthBarBackName = "Monster Health Bar Back";
        private const string MarkerHealthBarFillName = "Monster Health Bar Fill";
        private static readonly Vector3 BadgeLocalPosition = new Vector3(0f, 0.22f, 0f);
        private static readonly Vector3 BadgeLocalScale = new Vector3(0.26f, 0.04f, 0.26f);
        private static readonly Vector3 NameplateLocalPosition = new Vector3(0f, 1.28f, 0f);
        private static readonly Vector3 NameplateLocalScale = Vector3.one * 0.0085f;
        private const float NameplateHeadPaddingWorld = 0.45f;
        private static readonly int ZTestModePropertyId = Shader.PropertyToID("_ZTestMode");
        private static readonly int OutlineColorPropertyId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthPropertyId = Shader.PropertyToID("_OutlineWidth");
        private const float NameplateWidth = 260f;
        private const float NameplateHeight = 76f;
        private const float HealthBarPixelWidth = 150f;
        private const float HealthBarPixelHeight = 12f;
        private static readonly Color BossLabelColor = new Color(1f, 0.08f, 0.04f, 1f);
        private const string AuraRingObjectName = "Boss Aura Ring (runtime)";

        // 미지 표식 색. 발주한 아이콘의 글로우(#35D6E8 시안)와 같은 값이라, 아이콘이 들어와
        // 이 글자를 대체해도 색이 바뀌지 않는다.
        private static readonly Color UnknownMarkColor = new Color(0.208f, 0.839f, 0.910f, 1f);
        // 표식이 몬스터 몸 위에 겹치므로 배경을 특정할 수 없다 — 어두운 외곽선으로 배경과 무관하게
        // 세운다. 근거는 ApplyUnknownMarkOutline 주석 참조.
        private static readonly Color UnknownMarkOutlineColor = new Color(0.03f, 0.05f, 0.10f, 1f);
        private const float UnknownMarkOutlineWidth = 0.22f;
        private static readonly Color EliteLabelColor = new Color(0.72f, 0.28f, 1f, 1f);
        private const HideFlags RuntimeUiHideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        private readonly Dictionary<string, MarkerEntry> markers = new Dictionary<string, MarkerEntry>(StringComparer.Ordinal);
        // 마커별 발밑 링 인스턴스. MarkerEntry(readonly struct)에 넣지 않는 이유는 생성자
        // 인자가 이미 열넷이기 때문이고, 링은 마커 수명과 독립적으로 켜졌다 꺼졌다 한다(페이즈).
        private readonly Dictionary<string, GameObject> auraRingInstances = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        // 배지 호버 판정 대상. 배지를 다시 만들 때마다 통째로 갈아 끼운다(ApplyMonsterNameplateBadges).
        private readonly List<BadgeHitTarget> badgeHitTargets = new List<BadgeHitTarget>();
        private string primaryMarkerId;
        // See SetMarkerUiVisible: cinematics hide the nameplate/badge dressing on every marker.
        private bool markerUiVisible = true;

        public GameObject Marker => TryGetPrimaryEntry(out var entry) ? entry.Marker : null;
        public CharacterActorVisual ActorVisual => TryGetPrimaryEntry(out var entry) ? entry.ActorVisual : null;
        public int MarkerCount => markers.Count;

        // World-space bounds of the marker's rendered body (mesh/skinned renderers only — no particles, no
        // nameplate UI). The tutorial spotlight cuts its hole from this so it follows the actual silhouette.
        public bool TryGetMarkerVisualBounds(string markerId, out Bounds bounds)
        {
            bounds = default;
            if (!TryGetEntry(markerId, out var entry) || entry.Marker == null)
            {
                return false;
            }

            var any = false;
            foreach (var renderer in entry.Marker.GetComponentsInChildren<Renderer>(includeInactive: false))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                {
                    continue;
                }

                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return any;
        }

        // World-space corners of the marker's nameplate (name/health bar) and the badge row above it, appended
        // to <paramref name="corners"/>. False when the marker has no visible nameplate. The tutorial spotlight
        // unions these with the body bounds when a step points at the intent badge rather than the monster.
        public bool TryGetMarkerNameplateWorldCorners(string markerId, List<Vector3> corners)
        {
            if (corners == null || !TryGetEntry(markerId, out var entry) || entry.NameplateRoot == null
                || !entry.NameplateRoot.activeInHierarchy)
            {
                return false;
            }

            var any = false;
            if (entry.NameplateRoot.transform is RectTransform rootRect)
            {
                rootRect.GetWorldCorners(NameplateCornerBuffer);
                corners.AddRange(NameplateCornerBuffer);
                any = true;
            }

            if (entry.StatusRow != null && entry.StatusRow.gameObject.activeInHierarchy)
            {
                entry.StatusRow.GetWorldCorners(NameplateCornerBuffer);
                corners.AddRange(NameplateCornerBuffer);
                any = true;
            }

            return any;
        }

        private static readonly Vector3[] NameplateCornerBuffer = new Vector3[4];

        public bool TryGetMarkerWorldPosition(string markerId, out Vector3 worldPosition)
        {
            if (TryGetEntry(markerId, out var entry) && entry.Marker != null)
            {
                worldPosition = entry.Marker.transform.position;
                return true;
            }

            worldPosition = default;
            return false;
        }

        /// <summary>
        /// Transform variant of <see cref="TryGetMarkerVfxAnchorWorldPosition"/> for callers that must keep
        /// tracking the anchor after spawn (followSourceAnchor cues). No marker-position fallback: without a
        /// live actor anchor there is nothing to follow.
        /// </summary>
        public bool TryGetMarkerVfxAnchor(string markerId, CharacterVfxAnchorKind anchorKind, out Transform anchor)
        {
            if (TryGetEntry(markerId, out var entry) &&
                entry.ActorVisual != null &&
                entry.ActorVisual.TryGetVfxAnchor(anchorKind, out anchor) &&
                anchor != null)
            {
                return true;
            }

            anchor = null;
            return false;
        }

        /// <summary>
        /// 마커 모델의 발자국 지름(월드 단위, 2026-08-20 WS-2). 상태이상 바닥 링이 몸집에 비례하도록
        /// 하는 계수의 원천 — 보스 아우라 링이 판정 상자를 재사용하는 것과 같은 자
        /// (<see cref="CharacterFootprint"/>)를 쓴다. 보스 페이즈 배율도 함께 들어온다.
        /// </summary>
        public bool TryGetMarkerFootprintDiameter(string markerId, out float diameter)
        {
            if (TryGetEntry(markerId, out var entry) &&
                entry.ActorVisual != null &&
                entry.ActorVisual.TryGetFootprintDiameter(out diameter))
            {
                return true;
            }

            diameter = 0f;
            return false;
        }

        public bool TryGetMarkerVfxAnchorWorldPosition(string markerId, CharacterVfxAnchorKind anchorKind, out Vector3 worldPosition)
        {
            if (TryGetEntry(markerId, out var entry))
            {
                if (entry.ActorVisual != null &&
                    entry.ActorVisual.TryGetVfxAnchor(anchorKind, out var anchor) &&
                    anchor != null)
                {
                    worldPosition = anchor.position;
                    return true;
                }

                if (entry.Marker != null)
                {
                    worldPosition = entry.Marker.transform.position;
                    return true;
                }
            }

            worldPosition = default;
            return false;
        }

        public GameObject EnsureEnemyMarker(
            Transform selectedViewTransform,
            Transform fallbackParent,
            GameObject markerPrefab,
            Color markerColor,
            float markerRadius,
            Vector3 visualLocalOffset,
            Vector3 visualLocalEulerAngles,
            Vector3 visualLocalScale)
        {
            return EnsureEnemyMarker(
                DefaultMarkerId,
                selectedViewTransform,
                fallbackParent,
                markerPrefab,
                markerColor,
                markerRadius,
                visualLocalOffset,
                visualLocalEulerAngles,
                visualLocalScale);
        }

        public GameObject EnsureEnemyMarker(
            string markerId,
            Transform selectedViewTransform,
            Transform fallbackParent,
            GameObject markerPrefab,
            Color markerColor,
            float markerRadius,
            Vector3 visualLocalOffset,
            Vector3 visualLocalEulerAngles,
            Vector3 visualLocalScale)
        {
            markerId = NormalizeMarkerId(markerId);
            if (markers.TryGetValue(markerId, out var existing) && existing.Marker != null)
            {
                if (existing.SourcePrefab == markerPrefab)
                {
                    // 같은 프리팹이어도 배율은 바뀔 수 있다(보스 페이즈 성장). 여기서 다시 적용하지 않으면
                    // 마커는 처음 만들어질 때의 크기에 영원히 고정되고, 페이즈 배율은 조용히 무시된다.
                    ApplyVisualLocalScale(existing, visualLocalScale);
                    RefreshHoverTarget(existing);
                    if (string.IsNullOrEmpty(primaryMarkerId))
                    {
                        primaryMarkerId = markerId;
                    }

                    return existing.Marker;
                }

                // The resolved visual changed (e.g. the spawn now maps to a different monster). Rebuild so the
                // marker shows the correct model instead of keeping the originally instantiated prefab.
                DestroyMarkerObject(existing.Marker);
                markers.Remove(markerId);
                // 새 모델에는 링이 안 붙어 있다. 기록만 남겨 두면 다음 갱신이 "이미 있다"고 보고
                // 다시 만들지 않아 아우라가 조용히 사라진다.
                DiscardAuraRing(markerId);
            }

            var marker = markerPrefab != null
                ? new GameObject("M2 Integration Enemy Marker")
                : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = markerId == DefaultMarkerId ? "M2 Integration Enemy Marker" : $"M2 Integration Enemy Marker {markerId}";
            marker.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

            var parent = selectedViewTransform != null ? selectedViewTransform : fallbackParent;
            if (parent != null)
            {
                marker.transform.SetParent(parent, false);
            }

            Material material = null;
            CharacterActorVisual actorVisual = null;
            GameObject visualRoot = null;
            var prefabBaseScale = Vector3.one;
            if (markerPrefab != null)
            {
                marker.transform.localScale = Vector3.one;
                visualRoot = UnityEngine.Object.Instantiate(markerPrefab, marker.transform, false);
                visualRoot.name = "M2 Integration Enemy Visual";
                visualRoot.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                prefabBaseScale = visualRoot.transform.localScale;
                var resolvedVisualLocalScale = Vector3.Scale(prefabBaseScale, visualLocalScale);
                actorVisual = CharacterActorVisual.PrepareInstantiatedVisual(
                    visualRoot,
                    visualLocalOffset,
                    visualLocalEulerAngles,
                    resolvedVisualLocalScale);
                // 발을 마커 원점(타일 윗면)에 붙인다 — 명판·호버 상자가 모델 위치를 재기 전에.
                CharacterActorVisual.SnapGroundAnchorToWorldY(visualRoot.transform, marker.transform.position.y);
            }
            else
            {
                marker.transform.localScale = Vector3.one * markerRadius;
                var markerCollider = marker.GetComponent<Collider>();
                if (markerCollider != null)
                {
                    markerCollider.enabled = false;
                }

                material = new Material(Shader.Find("Standard"));
                material.color = markerColor;
                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = material;
                    renderer.enabled = false;
                }
            }

            var badge = CreateMarkerBadge(marker.transform, markerColor);
            var nameplate = CreateMarkerNameplate(marker.transform, visualRoot);

            // 마우스 판정 상자. 모델 배율이 적용된 뒤(PrepareInstantiatedVisual 이후)에 재야 크기가 맞고,
            // 마커 루트에 붙어야 한다 — 모델 하위는 ApplyRaycastSafeVisualPolicy가 Ignore Raycast로 내린다.
            // 뱃지·명판은 크기 산정에서 빠진다(visualRoot만 잰다): 명판은 머리 위 UI라 포함하면
            // 빈 공중이 판정에 들어온다.
            var hoverTarget = CharacterHoverTarget.Ensure(marker, markerId, visualRoot != null ? visualRoot.transform : null);

            markers[markerId] = new MarkerEntry(marker, markerPrefab, prefabBaseScale, actorVisual, material, badge, nameplate.Root, nameplate.Label, nameplate.BossLabel, nameplate.HealthBarFill, nameplate.HealthBarFillImage, nameplate.HealthBarView, hoverTarget, visualRoot, nameplate.StatusRow);
            if (string.IsNullOrEmpty(primaryMarkerId))
            {
                primaryMarkerId = markerId;
            }

            return marker;
        }

        public void ApplyEnemyMarkerSettings(GameObject markerPrefab, Color markerColor, float markerRadius)
        {
            foreach (var entry in markers.Values)
            {
                ApplyMarkerSettings(entry, markerPrefab, markerColor, markerRadius);
            }
        }

        public void ShowMonsterMarkers(
            IEnumerable<MonsterMarkerState> markerStates,
            Transform selectedViewTransform,
            Transform fallbackParent,
            GameObject markerPrefab,
            Color markerColor,
            float markerRadius,
            Vector3 visualLocalOffset,
            Vector3 visualLocalEulerAngles,
            Vector3 visualLocalScale)
        {
            var activeIds = new HashSet<string>(StringComparer.Ordinal);
            if (markerStates != null)
            {
                foreach (var state in markerStates)
                {
                    var markerId = NormalizeMarkerId(state.Id);
                    var resolvedMarkerPrefab = state.VisualPrefab != null ? state.VisualPrefab : markerPrefab;
                    activeIds.Add(markerId);
                    EnsureEnemyMarker(
                        markerId,
                        selectedViewTransform,
                        fallbackParent,
                        resolvedMarkerPrefab,
                        markerColor,
                        markerRadius,
                        visualLocalOffset,
                        visualLocalEulerAngles,
                        Vector3.Scale(state.VisualLocalScaleMultiplier, visualLocalScale));
                    SetActive(markerId, true);
                    SetLocalPosition(markerId, state.LocalPosition);
                    ApplyMarkerIdentity(markerId, state.Label, state.AccentColor, state.Hp, state.MaxHp, state.IsBoss, state.IsElite);
                }
            }

            foreach (var pair in markers)
            {
                if (!activeIds.Contains(pair.Key) && pair.Value.Marker != null)
                {
                    pair.Value.Marker.SetActive(false);
                }
            }

            primaryMarkerId = activeIds.FirstOrDefault() ?? primaryMarkerId;
        }

        /// <summary>
        /// 미지(<see cref="StatusEffectKind.Unknown"/>)가 걸린 몬스터 머리 위에 '?' 표식을 켠다.
        ///
        /// <para>맵 타일 오버레이가 아니라 <b>몬스터에 붙는 별도 UI</b>다(2026-08-05 사용자 결정).
        /// 타일 아이콘 경로(<c>StatusIconOverlayRenderer</c>)는 스프라이트가 없으면 아무것도 그리지 않아
        /// 아트가 들어올 때까지 화면에 안 뜨는데, 이 표식은 예고가 사라진 이유를 즉시 알려야 하므로
        /// 아트를 기다릴 수 없다. 네임플레이트에 글자로 그리면 발주와 무관하게 동작한다.</para>
        ///
        /// <para>마커 엔트리 구조를 건드리지 않으려고 네임플레이트 아래에서 이름으로 찾고 없으면 만든다 —
        /// <see cref="MonsterMarkerState"/>에 필드를 더하면 생성자 오버로드 다섯 개를 모두 고쳐야 한다.</para>
        /// </summary>
        public void SetIntentHiddenMark(string markerId, bool hidden)
        {
            if (!TryGetEntry(NormalizeMarkerId(markerId), out var entry) || entry.NameplateRoot == null)
            {
                return;
            }

            var existing = entry.NameplateRoot.transform.Find(MarkerUnknownMarkName);
            if (existing == null)
            {
                if (!hidden)
                {
                    return; // 켤 일이 없으면 만들지도 않는다.
                }

                existing = CreateUnknownMark(entry.NameplateRoot.transform);
            }

            existing.gameObject.SetActive(hidden && markerUiVisible);
        }

        private static Transform CreateUnknownMark(Transform nameplateRoot)
        {
            var markObject = new GameObject(MarkerUnknownMarkName, typeof(RectTransform));
            markObject.hideFlags = RuntimeUiHideFlags;
            markObject.transform.SetParent(nameplateRoot, false);

            var rect = markObject.GetComponent<RectTransform>();
            // 🔑 2026-08-05 사용자 결정: 더 크게, 그리고 몬스터 머리에 살짝 겹치게.
            //
            // 초판은 판 위쪽(+62)에 뒀다 — 역할 라벨(보스/엘리트)과 겹치면 둘 다 못 읽기 때문이다.
            // 그 제약은 여전히 유효하므로 **판 안쪽으로 내리지 않는다**: 판은 이름(위)·체력바(아래)가
            // 차 있고 그 위 바깥은 역할 라벨이 쓴다. 셋을 모두 피하면서 머리에 닿는 자리는
            // **판 아래 바깥** 하나뿐이라, 판 바닥에서 아래로 걸어 내린다.
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            // y=+55는 실기 대조로 고른 값이다(2026-08-06, MonsterLab·깡패 돼지):
            //   −8  → 물음표가 얼굴 한가운데를 덮는다("살짝"이 아니다)
            //   +55 → 머리 위에 앉고 아래 점만 갈기에 걸친다  ← 채택
            //   +85 → 체력바를 침범한다
            //
            // 🔴🔴 z=−120(카메라 쪽)이 **겹침을 성립시키는 유일한 장치**다. 이 판은 월드 스페이스
            // 캔버스라 3D 모델과 깊이 테스트를 하므로, z=0이면 표식이 머리 **뒤로 들어가 잘린다**
            // (초판 주석의 "겹침은 공짜"는 틀린 서술이었고 실기에서 잘렸다).
            // ⚠️ 위 ApplyOverlayZTest로는 못 푼다 — DNF 폰트가 쓰는 `TextMeshPro/Mobile/Distance
            // Field` 셰이더에 `_ZTestMode` 속성이 아예 없어서 그 함수가 HasProperty 가드에 걸려
            // **조용히 아무것도 하지 않는다**(2026-08-06 런타임 실측 ZTest=-1). 비모바일
            // `TextMeshPro/Distance Field`로 갈아도 같았다.
            // 명찰이 빌보드(CombatWorldSpaceBillboard)라 로컬 −z가 곧 카메라 방향이고,
            // 판 배율 0.0085를 곱하면 약 1.0 월드 유닛 앞으로 나온다 — 몬스터 몸 두께를 넘긴다.
            rect.anchoredPosition3D = new Vector3(0f, 55f, -120f);
            rect.sizeDelta = new Vector2(96f, 88f);

            var text = markObject.AddComponent<TextMeshProUGUI>();
            ApplyKoreanFontIfAvailable(text);
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 76f;
            text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.color = UnknownMarkColor;
            text.text = "?";
            ApplyOverlayZTest(text);
            ApplyUnknownMarkOutline(text);
            return markObject.transform;
        }

        /// <summary>
        /// 미지 표식에만 어두운 외곽선을 두른다.
        /// <para>🔴 다른 명찰 텍스트는 <b>빈 하늘 위</b>에 떠서 색만으로 선다. 이 표식은 머리에
        /// 겹치도록 내려온 순간 그 전제가 깨진다 — 배경이 몬스터 몸이 되고, 몬스터마다 색이 다르다.
        /// 시안 <c>#35D6E8</c>은 어두운 야경을 기준으로 고른 색이라 밝거나 청록 계열 몬스터 위에서는
        /// 대비가 죽는다.</para>
        /// <para>이 프로젝트는 같은 함정을 이미 한 번 밟았다 — 밀치기 아이콘(주황)이 항상 빨간 공격
        /// 예고 타일 위에만 떠서 묻혔다(<c>docs/design/style-guide-status-icons.md</c> §4-3).
        /// 그때의 교훈은 "그 표식이 실제로 어떤 배경 위에 뜨는가를 먼저 보라"였고, 외곽선은 배경을
        /// 특정할 수 없을 때의 답이다.</para>
        /// <para><c>fontMaterial</c>은 인스턴스를 만들어 돌려주므로(<see cref="ApplyOverlayZTest"/>와
        /// 같은 경로) 공유 머티리얼을 오염시키지 않는다 — 다른 명찰 글자는 영향받지 않는다.</para>
        /// </summary>
        private static void ApplyUnknownMarkOutline(TMP_Text label)
        {
            if (label == null)
            {
                return;
            }

            var fontMaterial = label.fontMaterial;
            if (fontMaterial == null)
            {
                return;
            }

            if (fontMaterial.HasProperty(OutlineColorPropertyId))
            {
                fontMaterial.SetColor(OutlineColorPropertyId, UnknownMarkOutlineColor);
            }

            if (fontMaterial.HasProperty(OutlineWidthPropertyId))
            {
                fontMaterial.SetFloat(OutlineWidthPropertyId, UnknownMarkOutlineWidth);
            }
        }

        public void DestroyAllMarkers()
        {
            foreach (var entry in markers.Values)
            {
                DestroyMarkerObject(entry.Marker);
            }

            markers.Clear();
            DiscardAllAuraRings();
            primaryMarkerId = null;
        }
        /// <summary>
        /// 링 인스턴스는 마커의 자식이라 마커가 파괴되면 함께 사라지지만, 기록을 비우지 않으면
        /// 다음 갱신이 파괴된 참조를 "이미 있다"고 읽는다.
        /// </summary>
        private void DiscardAllAuraRings()
        {
            foreach (var ring in auraRingInstances.Values)
            {
                if (ring != null)
                {
                    UnityEngine.Object.Destroy(ring);
                }
            }

            auraRingInstances.Clear();
        }
        private static void DestroyMarkerObject(GameObject marker)
        {
            if (marker == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(marker);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(marker);
            }
        }

        public void SetActive(bool active)
        {
            SetActive(DefaultMarkerId, active);
        }

        public void SetActive(string markerId, bool active)
        {
            if (TryGetEntry(markerId, out var entry) && entry.Marker != null)
            {
                entry.Marker.SetActive(active);
            }
        }

        public void SetLocalPosition(Vector3 localPosition)
        {
            SetLocalPosition(DefaultMarkerId, localPosition);
        }

        public void SetLocalPosition(string markerId, Vector3 localPosition)
        {
            if (TryGetEntry(markerId, out var entry) && entry.Marker != null)
            {
                entry.Marker.transform.localPosition = localPosition;
            }
        }

        public void FaceToward(Vector3 fromLocal, Vector3 toLocal)
        {
            FaceToward(DefaultMarkerId, fromLocal, toLocal);
        }

        public void FaceToward(string markerId, Vector3 fromLocal, Vector3 toLocal)
        {
            FaceTransformToward(TryGetEntry(markerId, out var entry) ? entry.Marker.transform : null, fromLocal, toLocal);
        }

        public void SetFacingDirection(string markerId, Vector3 worldForward)
        {
            if (!TryGetEntry(markerId, out var entry) || entry.Marker == null) return;
            if (worldForward.sqrMagnitude < 0.0001f) return;
            entry.Marker.transform.rotation = Quaternion.LookRotation(worldForward, Vector3.up);
        }

        /// <summary>
        /// 이 마커가 <b>지금 실제로 보고 있는</b> 방향(월드, y 제거·정규화). 없으면 false.
        ///
        /// <para>2026-09-02 #4: 공격 VFX의 회전 기준이다. 몸을 돌린 것은 <see cref="SetFacingDirection"/>
        /// 한 곳이므로 그 결과를 그대로 되읽으면 <b>몸과 이펙트가 갈라질 수 없다</b> —
        /// 좌표에서 다시 유도하면 「무엇을 겨눴는가」의 정본이 두 벌이 되고, 그것이 이번 결함이었다.</para>
        /// </summary>
        public bool TryGetFacingDirection(string markerId, out Vector3 worldForward)
        {
            worldForward = default;
            if (!TryGetEntry(markerId, out var entry) || entry.Marker == null)
            {
                return false;
            }

            var forward = entry.Marker.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            worldForward = forward.normalized;
            return true;
        }

        public void SetMoveSpeed(float speed)
        {
            SetMoveSpeed(DefaultMarkerId, speed);
        }

        public void SetMoveSpeed(string markerId, float speed)
        {
            if (TryGetEntry(markerId, out var entry))
            {
                entry.ActorVisual?.SetMoveSpeed(speed);
            }
        }

        public bool TryGetAnimationSpeed(string markerId, out float speed)
        {
            if (TryGetEntry(markerId, out var entry) && entry.ActorVisual != null)
            {
                speed = entry.ActorVisual.AnimationSpeed;
                return true;
            }

            speed = 1f;
            return false;
        }

        public void SetAnimationSpeed(string markerId, float speed)
        {
            if (TryGetEntry(markerId, out var entry))
            {
                entry.ActorVisual?.SetAnimationSpeed(speed);
            }
        }

        public void TriggerAttack()
        {
            TriggerAttack(DefaultMarkerId);
        }

        public void TriggerAttack(string markerId)
        {
            TriggerAttack(markerId, string.Empty);
        }

        public void TriggerAttack(string markerId, string animationTrigger)
        {
            if (!TryGetEntry(markerId, out var entry) || entry.ActorVisual == null)
            {
                return;
            }

            // An unauthored trigger must go through the parameterless overload, which resolves the real
            // parameter name off the controller (AttackTrigger → Attack → Attack1..5). The string overload
            // would blindly poke "AttackTrigger", which monster controllers do not have (their authored
            // triggers are Attack1..5), so the swing silently never played.
            if (string.IsNullOrWhiteSpace(animationTrigger))
            {
                entry.ActorVisual.TriggerAttack();
                return;
            }

            entry.ActorVisual.TriggerAttack(animationTrigger);
        }

        /// <summary>
        /// 은신 해제 페이드(2026-09-05). 코루틴 호스트가 없는 정적 프리젠터라 <b>비주얼 자신</b>이
        /// 돌린다 — 진입·중단·원복을 전부 <see cref="CharacterActorVisual.BeginRevealFade"/> 하나가
        /// 소유하므로 여기서는 중단을 흉내 내지 않는다(종전의 문자열 StopCoroutine은 IEnumerator로
        /// 시작한 코루틴을 못 잡아 아무것도 멈추지 않았고, 겹친 페이드가 투명 사본을 원본으로 굳혔다).
        /// </summary>
        public void PlayStealthRevealFade(string markerId, float seconds, float startAlpha)
        {
            if (!TryGetEntry(markerId, out var entry)
                || entry.ActorVisual == null
                || !entry.ActorVisual.isActiveAndEnabled)
            {
                return;
            }

            entry.ActorVisual.BeginRevealFade(seconds, startAlpha);
        }

        public void TriggerHit()
        {
            TriggerHit(DefaultMarkerId);
        }

        public void TriggerHit(string markerId)
        {
            if (TryGetEntry(markerId, out var entry))
            {
                entry.ActorVisual?.TriggerHit();
            }
        }

        public void TriggerKnockback()
        {
            TriggerKnockback(DefaultMarkerId);
        }

        public void TriggerKnockback(string markerId)
        {
            if (TryGetEntry(markerId, out var entry))
            {
                entry.ActorVisual?.TriggerKnockback();
            }
        }

        public void TriggerDead()
        {
            TriggerDead(DefaultMarkerId);
        }

        public void TriggerDead(string markerId)
        {
            if (TryGetEntry(markerId, out var entry))
            {
                entry.ActorVisual?.TriggerDead();
                if (entry.HealthBarView != null)
                {
                    entry.HealthBarView.NotifyDead();
                }
            }
        }

        // Waits until the given marker's attack animation reaches its strike frame; fixed-delay fallback
        // when the marker/actor is missing. Used by the presentation scheduler to align attack impacts.
        public IEnumerator WaitForAttackStrike(string markerId, float strikeFraction, float fallbackSeconds, float maxWaitSeconds)
        {
            if (TryGetEntry(markerId, out var entry) && entry.ActorVisual != null)
            {
                return entry.ActorVisual.WaitForAttackStrike(strikeFraction, fallbackSeconds, maxWaitSeconds);
            }

            return WaitFallback(fallbackSeconds);
        }

        // Waits for the given marker's death animation to play out; fixed-delay fallback if absent.
        public IEnumerator WaitForDead(string markerId, float fallbackSeconds, float maxWaitSeconds)
        {
            if (TryGetEntry(markerId, out var entry) && entry.ActorVisual != null)
            {
                return entry.ActorVisual.WaitForDead(fallbackSeconds, maxWaitSeconds);
            }

            return WaitFallback(fallbackSeconds);
        }

        private static IEnumerator WaitFallback(float seconds)
        {
            if (seconds > 0f)
            {
                yield return new WaitForSeconds(seconds);
            }
        }

        /// <summary>
        /// Global visibility for the per-marker UI dressing (nameplate: name/role label + health bar, and
        /// the accent badge disc). Cinematics hide these so featured monsters read as film subjects, not
        /// game pieces; markers built while hidden also come up without their UI. Restored on teardown.
        /// </summary>
        public void SetMarkerUiVisible(bool visible)
        {
            markerUiVisible = visible;
            foreach (var entry in markers.Values)
            {
                if (entry.NameplateRoot != null)
                {
                    entry.NameplateRoot.SetActive(visible);
                }

                if (entry.BadgeRenderer != null)
                {
                    entry.BadgeRenderer.enabled = visible;
                }
            }
        }

        private void ApplyMarkerIdentity(string markerId, string label, Color accentColor, int hp, int maxHp, bool isBoss, bool isElite)
        {
            if (!TryGetEntry(markerId, out var entry))
            {
                return;
            }

            if (entry.NameplateRoot != null)
            {
                entry.NameplateRoot.SetActive(markerUiVisible);
            }

            if (entry.Label != null)
            {
                entry.Label.text = string.IsNullOrWhiteSpace(label) ? markerId : label;
                entry.Label.color = Color.white;
            }

            if (entry.BossLabel != null)
            {
                var showRoleLabel = isBoss || isElite;
                entry.BossLabel.gameObject.SetActive(showRoleLabel);
                entry.BossLabel.text = isBoss ? "보스" : isElite ? "엘리트" : string.Empty;
                entry.BossLabel.color = isBoss ? BossLabelColor : EliteLabelColor;
            }

            if (entry.BadgeRenderer != null)
            {
                entry.BadgeRenderer.sharedMaterial.color = accentColor;
                entry.BadgeRenderer.enabled = markerUiVisible;
            }

            if (entry.HealthBarView != null)
            {
                entry.HealthBarView.Bind(markerId);
            }

            ApplyHealthBar(entry, hp, maxHp);
        }

        /// <summary>
        /// 이미 만들어진 마커의 모델 배율만 바꾼다(보스 페이즈 성장 트윈이 프레임마다 부른다).
        /// 마커를 재구성하지 않으므로 연출 도중 전체 뷰 갱신을 돌리지 않아도 크기가 따라온다.
        /// 마커가 아직 없으면 조용히 무시한다 — 다음 뷰 갱신이 저작값으로 만들어 준다.
        /// </summary>
        public void SetMarkerVisualScale(string markerId, Vector3 visualLocalScale)
        {
            if (markers.TryGetValue(NormalizeMarkerId(markerId), out var entry))
            {
                ApplyVisualLocalScale(entry, visualLocalScale);
                RefreshHoverTarget(entry);
            }
        }

        /// <summary>마커가 인스턴스화될 때 잰 프리팹 루트 배율(모델 원본 크기). 디버그 몸집 조절이 「넣을 루트 스케일」을 계산하는 데 쓴다.</summary>
        public bool TryGetMarkerPrefabBaseScale(string markerId, out Vector3 prefabBaseScale)
        {
            if (markers.TryGetValue(NormalizeMarkerId(markerId), out var entry))
            {
                prefabBaseScale = entry.PrefabBaseScale;
                return true;
            }

            prefabBaseScale = Vector3.one;
            return false;
        }

        /// <summary>
        /// 프리팹 에셋의 루트 배율이 바뀐 뒤(디버그 패널이 프리팹에 저장) 살아 있는 마커의 기준 배율을 다시 읽는다.
        /// 기준을 갱신하지 않으면 다음 배율 적용이 옛 기준에 곱해져 저장 직후 몸집이 되돌아간다.
        /// </summary>
        public void RefreshMarkerPrefabBaseScale(string markerId, Vector3 visualLocalScale)
        {
            var key = NormalizeMarkerId(markerId);
            if (!markers.TryGetValue(key, out var entry) || entry.SourcePrefab == null)
            {
                return;
            }

            var refreshed = entry.WithPrefabBaseScale(entry.SourcePrefab.transform.localScale);
            markers[key] = refreshed;
            ApplyVisualLocalScale(refreshed, visualLocalScale);
            RefreshHoverTarget(refreshed);
        }
        /// <summary>
        /// 보스 아우라의 <b>발밑 링 층</b>을 켜거나 끈다. 링은 마커 루트 아래에 붙는 별도 인스턴스라
        /// 마커와 함께 움직이고 함께 사라진다.
        ///
        /// <para>🔑 크기는 <see cref="CharacterHoverTarget"/>의 판정 상자에서 가져온다. 그 상자는 이미
        /// 모델 크기에서 뽑히고 페이즈 배율이 바뀔 때마다 다시 재지므로, 링도 보스가 커지면 저절로
        /// 함께 커진다 — 링 전용 크기 저작을 따로 두면 그 값이 페이즈 배율과 조용히 어긋난다.</para>
        /// </summary>
        public void SetMarkerAuraRing(string markerId, bool enabled, GameObject ringPrefab, float radiusMultiplier)
        {
            markerId = NormalizeMarkerId(markerId);
            if (!markers.TryGetValue(markerId, out var entry) || entry.Marker == null)
            {
                return;
            }

            if (!enabled || ringPrefab == null)
            {
                DiscardAuraRing(markerId);
                return;
            }

            if (!auraRingInstances.TryGetValue(markerId, out var ring) || ring == null)
            {
                ring = UnityEngine.Object.Instantiate(ringPrefab, entry.Marker.transform, false);
                ring.name = AuraRingObjectName;
                ring.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                auraRingInstances[markerId] = ring;
            }

            // 🔑 프리팹 고유 지름으로 나눈다. 안 나누면 "발자국 지름"을 배율로 그대로 넣는 셈이라
            // 고유 지름이 2.6인 링이 그 배로 커져 아레나를 덮는다(실측으로 밟았다).
            var footprint = ResolveMarkerFootprintDiameter(entry);
            var native = Mathf.Max(0.01f, ResolveRingNativeDiameter(ring));
            var scale = Mathf.Max(0.01f, footprint * Mathf.Max(0.01f, radiusMultiplier) / native);
            ring.transform.localPosition = Vector3.zero;
            ring.transform.localScale = new Vector3(scale, scale, scale);
        }

        /// <summary>
        /// 링 프리팹이 배율 1에서 갖는 고유 지름. 파티클 shape 반지름에서 읽으므로 프리팹의 룩을
        /// 바꿔도 코드 상수를 따라 고칠 필요가 없다.
        /// </summary>
        private static float ResolveRingNativeDiameter(GameObject ring)
        {
            var diameter = 0f;
            foreach (var ps in ring.GetComponentsInChildren<ParticleSystem>(includeInactive: true))
            {
                if (ps == null)
                {
                    continue;
                }

                var shape = ps.shape;
                if (shape.enabled)
                {
                    diameter = Mathf.Max(diameter, shape.radius * 2f);
                }
            }

            return diameter;
        }

        /// <summary>
        /// 마커의 발자국 지름(월드 단위). 판정 상자의 가로 두 축 중 큰 쪽을 쓴다.
        /// </summary>
        private static float ResolveMarkerFootprintDiameter(MarkerEntry entry)
        {
            var box = entry.Marker != null ? entry.Marker.GetComponent<BoxCollider>() : null;
            if (box == null)
            {
                return 1f;
            }

            var lossy = entry.Marker.transform.lossyScale;
            return Mathf.Max(box.size.x * Mathf.Abs(lossy.x), box.size.z * Mathf.Abs(lossy.z));
        }

        private void DiscardAuraRing(string markerId)
        {
            if (!auraRingInstances.TryGetValue(markerId, out var ring))
            {
                return;
            }

            auraRingInstances.Remove(markerId);
            if (ring != null)
            {
                UnityEngine.Object.Destroy(ring);
            }
        }
        /// <summary>
        /// 마커의 마우스 판정 상자를 현재 모델 크기로 다시 잰다. 배율을 적용한 <b>뒤에</b> 불러야 한다 —
        /// 먼저 부르면 보스 페이즈 성장 뒤에도 상자만 옛 크기로 남아 몸통이 판정 밖에 놓인다.
        /// </summary>
        private static void RefreshHoverTarget(MarkerEntry entry)
        {
            if (entry.HoverTarget == null)
            {
                return;
            }

            entry.HoverTarget.Resize(entry.VisualRoot != null ? entry.VisualRoot.transform : null);
        }

        /// <summary>
        /// 이미 만들어진 마커의 모델 배율을 다시 적용한다. 항상 <see cref="MarkerEntry.PrefabBaseScale"/>에서
        /// 다시 계산하므로 반복 호출이 멱등이다(현재 스케일에 곱하면 프레임마다 복리로 부푼다).
        /// </summary>
        private static void ApplyVisualLocalScale(MarkerEntry entry, Vector3 visualLocalScale)
        {
            if (entry.ActorVisual == null)
            {
                return;
            }

            var resolved = Vector3.Scale(entry.PrefabBaseScale, visualLocalScale);
            var visualTransform = entry.ActorVisual.transform;
            if ((visualTransform.localScale - resolved).sqrMagnitude > 1e-8f)
            {
                visualTransform.localScale = resolved;
            }

            // 배율이 바뀌면 발 높이(앵커 × 스케일)도 바뀐다 — 다시 붙인다(멱등).
            if (entry.Marker != null)
            {
                CharacterActorVisual.SnapGroundAnchorToWorldY(visualTransform, entry.Marker.transform.position.y);
            }

            // 🔴 명판·체력바 앵커도 다시 잰다(2026-09-05 후속 #4). 명판 높이는 생성 시 렌더러 바운즈에서 한 번만
            //   유도됐고, 보스 페이즈 성장 트윈은 모델 배율만 바꿔 명판이 몸통 안에 파묻혔다(1.4배면 머리가 명판을
            //   뚫는다). 바운즈는 월드값이라 배율 적용 직후 다시 재면 새 머리 높이가 나온다. 멱등.
            RefreshNameplateAnchor(entry);
        }

        /// <summary>명판 루트를 현재 모델 높이 바로 위로 다시 붙인다(생성 시 계산과 같은 식).</summary>
        private static void RefreshNameplateAnchor(MarkerEntry entry)
        {
            if (entry.NameplateRoot == null || entry.Marker == null || entry.VisualRoot == null)
            {
                return;
            }

            entry.NameplateRoot.transform.localPosition = ResolveNameplateLocalPosition(entry.Marker.transform, entry.VisualRoot);
        }

        /// <summary>마커 루트의 로컬 위치(연출이 「지금 어디 서 있는가」를 물을 때). 마커가 없으면 false.</summary>
        public bool TryGetMarkerLocalPosition(string markerId, out Vector3 localPosition)
        {
            if (TryGetEntry(markerId, out var entry) && entry.Marker != null)
            {
                localPosition = entry.Marker.transform.localPosition;
                return true;
            }

            localPosition = default;
            return false;
        }

        private static void ApplyMarkerSettings(MarkerEntry entry, GameObject markerPrefab, Color markerColor, float markerRadius)
        {
            if (entry.Marker == null)
            {
                return;
            }

            entry.Marker.transform.localScale = markerPrefab != null ? Vector3.one : Vector3.one * markerRadius;
            if (entry.Material != null)
            {
                entry.Material.color = markerColor;
            }

            if (entry.BadgeRenderer != null)
            {
                entry.BadgeRenderer.sharedMaterial.color = markerColor;
            }
        }

        private static Renderer CreateMarkerBadge(Transform parent, Color markerColor)
        {
            var badge = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            badge.name = MarkerBadgeName;
            badge.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            badge.transform.SetParent(parent, false);
            badge.transform.localPosition = BadgeLocalPosition;
            badge.transform.localRotation = Quaternion.identity;
            badge.transform.localScale = BadgeLocalScale;
            var collider = badge.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }

            var renderer = badge.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = CreateMarkerMaterial("Monster Marker Badge", markerColor);
                renderer.enabled = false;
            }

            badge.SetActive(false);
            return renderer;
        }

        private static (GameObject Root, TMP_Text Label, TMP_Text BossLabel, RectTransform HealthBarFill, Image HealthBarFillImage, MonsterHealthBarView HealthBarView, RectTransform StatusRow) CreateMarkerNameplate(Transform parent, GameObject visualRoot)
        {
            var root = new GameObject(MarkerNameplateName, typeof(RectTransform));
            root.hideFlags = RuntimeUiHideFlags;
            root.transform.SetParent(parent, false);
            root.transform.localPosition = ResolveNameplateLocalPosition(parent, visualRoot);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = NameplateLocalScale;
            root.AddComponent<CombatWorldSpaceBillboard>();

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;

            // Overlay UI material (ZTest Always) so the nameplate/health bar draw on top of the 3D model
            // instead of being clipped by it. The TMP label gets the same treatment via ApplyOverlayZTest.
            var overlayMaterial = CreateOverlayUiMaterial();

            var rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(NameplateWidth, NameplateHeight);

            var labelObject = new GameObject(MarkerLabelName, typeof(RectTransform));
            labelObject.hideFlags = RuntimeUiHideFlags;
            labelObject.transform.SetParent(root.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -4f);
            labelRect.sizeDelta = new Vector2(0f, 44f);

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            ApplyKoreanFontIfAvailable(label);
            label.alignment = TextAlignmentOptions.Center;
            // 2026-08-20 #3: 배지와 함께 이름도 확대(22→32) — 실플레이 카메라에서 이름 판독 불가 판정.
            label.fontSize = 32f;
            label.fontStyle = FontStyles.Bold;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            label.color = Color.white;
            label.text = string.Empty;
            ApplyOverlayZTest(label);

            var bossLabelObject = new GameObject(MarkerBossLabelName, typeof(RectTransform));
            bossLabelObject.hideFlags = RuntimeUiHideFlags;
            bossLabelObject.transform.SetParent(root.transform, false);
            var bossLabelRect = bossLabelObject.GetComponent<RectTransform>();
            bossLabelRect.anchorMin = new Vector2(0.5f, 1f);
            bossLabelRect.anchorMax = new Vector2(0.5f, 1f);
            bossLabelRect.pivot = new Vector2(0.5f, 1f);
            bossLabelRect.anchoredPosition = new Vector2(0f, 28f);
            bossLabelRect.sizeDelta = new Vector2(88f, 28f);

            var bossLabel = bossLabelObject.AddComponent<TextMeshProUGUI>();
            ApplyKoreanFontIfAvailable(bossLabel);
            bossLabel.alignment = TextAlignmentOptions.Center;
            bossLabel.fontSize = 24f;
            bossLabel.fontStyle = FontStyles.Bold;
            bossLabel.textWrappingMode = TextWrappingModes.NoWrap;
            bossLabel.raycastTarget = false;
            bossLabel.color = BossLabelColor;
            bossLabel.text = string.Empty;
            bossLabel.gameObject.SetActive(false);
            ApplyOverlayZTest(bossLabel);

            var barBackObject = new GameObject(MarkerHealthBarName, typeof(RectTransform), typeof(Image));
            barBackObject.hideFlags = RuntimeUiHideFlags;
            barBackObject.transform.SetParent(root.transform, false);
            var barBackRect = barBackObject.GetComponent<RectTransform>();
            barBackRect.anchorMin = new Vector2(0.5f, 0f);
            barBackRect.anchorMax = new Vector2(0.5f, 0f);
            barBackRect.pivot = new Vector2(0.5f, 0.5f);
            barBackRect.anchoredPosition = new Vector2(0f, 18f);
            barBackRect.sizeDelta = new Vector2(HealthBarPixelWidth, HealthBarPixelHeight);
            var backImage = barBackObject.GetComponent<Image>();
            backImage.color = new Color(0.05f, 0.02f, 0.02f, 0.88f);
            backImage.raycastTarget = false;
            backImage.material = overlayMaterial;

            var fillObject = new GameObject(MarkerHealthBarFillName, typeof(RectTransform), typeof(Image));
            fillObject.hideFlags = RuntimeUiHideFlags;
            fillObject.transform.SetParent(barBackObject.transform, false);
            var fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0.5f);
            fillRect.anchorMax = new Vector2(0f, 0.5f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(HealthBarPixelWidth, HealthBarPixelHeight);
            var fillImage = fillObject.GetComponent<Image>();
            fillImage.color = new Color(0.18f, 0.95f, 0.26f, 1f);
            fillImage.raycastTarget = false;
            fillImage.material = overlayMaterial;

            var healthBarView = barBackObject.AddComponent<MonsterHealthBarView>();
            healthBarView.Initialize(backImage, fillImage, fillRect, HealthBarPixelWidth, HealthBarPixelHeight);

            // 배지 행 — 이름 <b>위</b>(실플레이 피드백 ②). 예전에는 체력바 아래였고 의도는 타일
            // 숫자였는데, 그러면 "이 놈이 지금 뭘 하려는가"와 "무엇에 걸려 있는가"를 서로 다른 두
            // 군데서 읽어야 했다. 빈 행은 항상 있고 내용만 ApplyMonsterNameplateBadges가 채운다 —
            // 마커 재생성 없이 매 갱신 반영되도록.
            // Y는 ResolveBadgeRowY가 정한다: 보스 라벨이 켜져 있으면 그 위로 비켜선다.
            var statusRowObject = new GameObject(MarkerStatusRowName, typeof(RectTransform));
            statusRowObject.hideFlags = RuntimeUiHideFlags;
            statusRowObject.transform.SetParent(root.transform, false);
            var statusRowRect = statusRowObject.GetComponent<RectTransform>();
            statusRowRect.anchorMin = new Vector2(0.5f, 1f);
            statusRowRect.anchorMax = new Vector2(0.5f, 1f);
            statusRowRect.pivot = new Vector2(0.5f, 0f);
            statusRowRect.anchoredPosition = new Vector2(0f, BadgeRowBaseY);
            statusRowRect.sizeDelta = new Vector2(NameplateWidth, StatusIconPixelSize);

            root.SetActive(true);
            return (root, label, bossLabel, fillRect, fillImage, healthBarView, statusRowRect);
        }

        // Nameplate Y is auto-derived from the model's rendered height so it sits just above any monster,
        // regardless of model size. Falls back to the authored constant when no renderers are found.
        private static Vector3 ResolveNameplateLocalPosition(Transform markerRoot, GameObject visualRoot)
        {
            if (markerRoot == null || visualRoot == null)
            {
                return NameplateLocalPosition;
            }

            var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            var maxWorldY = float.NegativeInfinity;
            var found = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                var bounds = renderer.bounds;
                if (bounds.size == Vector3.zero)
                {
                    continue;
                }

                maxWorldY = Mathf.Max(maxWorldY, bounds.max.y);
                found = true;
            }

            if (!found)
            {
                return NameplateLocalPosition;
            }

            var scaleY = Mathf.Abs(markerRoot.lossyScale.y);
            if (scaleY < 0.0001f)
            {
                scaleY = 1f;
            }

            var localY = (maxWorldY - markerRoot.position.y + NameplateHeadPaddingWorld) / scaleY;
            localY = Mathf.Max(localY, NameplateLocalPosition.y);
            return new Vector3(NameplateLocalPosition.x, localY, NameplateLocalPosition.z);
        }

        // UI/Default material with ZTest Always (unity_GUIZTestMode = 8) so world-space nameplate graphics
        // render over the 3D model rather than being occluded by it.
        private static Material CreateOverlayUiMaterial()
        {
            var shader = Shader.Find("UI/Default");
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader)
            {
                name = "Monster Nameplate Overlay",
                hideFlags = HideFlags.DontSave,
            };
            material.SetInt("unity_GUIZTestMode", 8);
            return material;
        }

        /// <summary>
        /// 네임플레이트 배지 행(이름 위)을 갈아 끼운다. 마커 재생성과 무관한 독립 관심사라 MarkerState
        /// 파이프라인(튜플 3중 오버로드)을 건드리지 않고 별도 표면으로 둔다 — 의도와 상태는 마커가
        /// 아니라 턴 진행·효과 이벤트로 바뀐다.
        ///
        /// <para>정렬은 <b>의도(공격 &gt; 다단 &gt; 밀치기) &gt; 제어 &gt; 지속피해 &gt; 그 밖의 디버프 &gt;
        /// 버프·약오름</b> — 급한 것이 항상 왼쪽에 오므로 여러 마리를 훑을 때 첫 칸만 봐도 걸러진다.
        /// 상한 <see cref="MaxStatusIcons"/>를 넘으면 마지막 칸이 <c>+N</c>이 된다.</para>
        ///
        /// <para>호버 판정 대상(<see cref="TryGetHoveredBadge"/>)도 여기서 함께 다시 만든다 — 배지가
        /// 사라졌는데 판정만 남으면 없는 툴팁이 뜬다.</para>
        /// </summary>
        internal void ApplyMonsterNameplateBadges(
            IReadOnlyDictionary<string, IReadOnlyList<MonsterNameplateBadge>> badgesByMonsterId,
            StatusEffectIconCatalog catalog)
        {
            badgeHitTargets.Clear();

            foreach (var pair in markers)
            {
                var row = pair.Value.StatusRow;
                if (row == null)
                {
                    continue;
                }

                for (var i = row.childCount - 1; i >= 0; i--)
                {
                    var child = row.GetChild(i).gameObject;
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(child);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(child);
                    }
                }

                // 보스 라벨이 이름 위 첫 줄을 이미 쓰고 있으면 배지는 그 위로 비켜선다.
                var bossLabelActive = pair.Value.BossLabel != null && pair.Value.BossLabel.gameObject.activeSelf;
                row.anchoredPosition = new Vector2(0f, ResolveBadgeRowY(bossLabelActive));

                if (badgesByMonsterId == null
                    || !badgesByMonsterId.TryGetValue(pair.Key, out var badges)
                    || badges == null
                    || badges.Count == 0)
                {
                    continue;
                }

                var ordered = badges
                    .OrderBy(BadgeSortRank)
                    .ThenBy(badge => (int)badge.Kind)
                    .ToList();

                var shown = Mathf.Min(ordered.Count, MaxStatusIcons);
                var overflow = ordered.Count - shown;
                // 넘칠 때는 배지 하나를 양보해 +N 칸을 만든다 — "6개 + 나머지"가 아니라 "5개 + +N".
                if (overflow > 0)
                {
                    shown = Mathf.Max(0, MaxStatusIcons - 1);
                    overflow = ordered.Count - shown;
                }

                var totalWidth = 0f;
                for (var i = 0; i < shown; i++)
                {
                    totalWidth += ResolveBadgeWidth(ordered[i]) + StatusIconGapPx;
                }
                if (overflow > 0)
                {
                    totalWidth += StatusIconPixelSize + 6f + StatusIconGapPx;
                }
                totalWidth = Mathf.Max(0f, totalWidth - StatusIconGapPx);

                var x = -totalWidth * 0.5f;
                for (var i = 0; i < shown; i++)
                {
                    var width = ResolveBadgeWidth(ordered[i]);
                    var rect = CreateMarkerBadge(row, ordered[i], catalog, x, width);
                    badgeHitTargets.Add(new BadgeHitTarget(rect, ordered[i], pair.Key));
                    x += width + StatusIconGapPx;
                }

                if (overflow > 0)
                {
                    CreateMarkerStatusOverflow(row, overflow, x);
                }
            }
        }

        /// <summary>
        /// 배지 행의 Y(플레이트 상단 기준). 보스 라벨(+28 자리, 높이 28)이 켜져 있으면 그 위로 올라간다.
        /// </summary>
        private static float ResolveBadgeRowY(bool bossLabelActive) =>
            bossLabelActive ? BadgeRowBaseY + BossLabelReservedHeight : BadgeRowBaseY;

        /// <summary>
        /// 의도(0~2) &gt; 방어막(3) &gt; 제어(4) &gt; 지속피해(5) &gt; 그 밖의 디버프(6) &gt; 버프(7) &gt;
        /// 특성(8~9).
        ///
        /// <para>특성(견고·맷집·약오름)이 <b>맨 뒤</b>인 이유: 이번 턴에 바뀌는 것(의도·상태이상)이 먼저
        /// 읽혀야 하고, 특성은 그 몬스터가 늘 갖고 있는 성질이라 급하지 않다. 지울 수 있는 것과
        /// 없는 것이 줄 위에서 갈리는 부수 효과도 있다.</para>
        /// </summary>
        private static int BadgeSortRank(MonsterNameplateBadge badge)
        {
            switch (badge.Kind)
            {
                case MonsterNameplateBadgeKind.Attack:
                // 자기부여·기믹 의도는 공격 의도와 같은 슬롯이다(한 턴에 둘이 공존하지 않는다) — 맨 앞.
                case MonsterNameplateBadgeKind.SelfBuffStatus:
                case MonsterNameplateBadgeKind.SelfBuffShield:
                case MonsterNameplateBadgeKind.BossGimmick:
                // 기물 성숙은 그 기물의 유일한 이야기다 — 맨 앞.
                case MonsterNameplateBadgeKind.PropMaturity:
                    return 0;
                case MonsterNameplateBadgeKind.MultiHit:
                    return 1;
                case MonsterNameplateBadgeKind.Knockback:
                    return 2;
                case MonsterNameplateBadgeKind.Shield:
                    return 3;
                case MonsterNameplateBadgeKind.Sturdy:
                    return 8;
                case MonsterNameplateBadgeKind.Toughness:
                    return 9;
                case MonsterNameplateBadgeKind.Agitation:
                    return 10;
                case MonsterNameplateBadgeKind.Aftermath:
                    return 11;
                case MonsterNameplateBadgeKind.Trait:
                    // 정렬 정본은 monster_traits.csv다 — 특성이 늘어도 이 switch는 안 는다.
                    return TryGetTrait(badge, out var sortTrait) ? sortTrait.BadgeSortRank : 12;
                default:
                    return StatusIconSortRank(badge.Effect.Kind);
            }
        }

        /// <summary>
        /// 제어(4) &gt; 지속피해(5) &gt; 그 밖의 디버프(6) &gt; 버프(7). 정본은 <c>status_effects.csv</c>의
        /// <c>badgeSortRank</c>(1단계 구조 리팩토링) — 여기 있던 switch는 <see cref="StatusEffectInfo"/> 폴백으로 옮겼다.
        /// </summary>
        private static int StatusIconSortRank(StatusEffectKind kind) => StatusEffectInfo.BadgeSortRank(kind);

        internal static int BadgeSortRankForTests(MonsterNameplateBadge badge) => BadgeSortRank(badge);

        /// <summary>아이콘 한 칸 + (숫자가 있으면) 숫자 폭. 호버 판정 사각형도 이 폭을 쓴다.</summary>
        private static float ResolveBadgeWidth(MonsterNameplateBadge badge)
        {
            var number = ResolveBadgeNumberText(badge);
            return string.IsNullOrEmpty(number)
                ? StatusIconPixelSize
                : StatusIconPixelSize + 2f + number.Length * BadgeNumberCharWidthPx;
        }

        /// <summary>배지 오른쪽에 붙는 숫자. 빈 문자열이면 아이콘만 그린다.</summary>
        private static string ResolveBadgeNumberText(MonsterNameplateBadge badge)
        {
            switch (badge.Kind)
            {
                case MonsterNameplateBadgeKind.Attack:
                    // 2026-09-02 #10: 반복 횟수는 별도 칩이 아니라 이 글자에 ×N으로 붙는다.
                    if (badge.Amount <= 0)
                    {
                        return badge.HitCount > 1 ? $"×{badge.HitCount}" : string.Empty;
                    }

                    return badge.HitCount > 1
                        ? $"{badge.Amount}×{badge.HitCount}"
                        : badge.Amount.ToString();
                case MonsterNameplateBadgeKind.MultiHit:
                    // 더 이상 발행되지 않는다(#10) — enum은 append-only라 남겨 두고 표시만 껐다.
                    return badge.Amount > 1 ? $"×{badge.Amount}" : string.Empty;
                case MonsterNameplateBadgeKind.Knockback:
                    // §16.1 부호 규약: 양수 = 밀치기, 음수 = 끌어당김. 칸수는 절댓값으로 읽힌다.
                    return Mathf.Abs(badge.Amount).ToString();
                case MonsterNameplateBadgeKind.Agitation:
                    return badge.Amount > 0 ? badge.Amount.ToString() : string.Empty;
                case MonsterNameplateBadgeKind.Shield:
                    return badge.Amount > 0 ? badge.Amount.ToString() : string.Empty;
                case MonsterNameplateBadgeKind.Sturdy:
                    return string.Empty;
                case MonsterNameplateBadgeKind.Toughness:
                    // 준비/소진은 숫자가 아니라 색으로 말한다 — 숫자 칸을 비워 배지 폭을 아낀다.
                    return string.Empty;
                case MonsterNameplateBadgeKind.Aftermath:
                    return string.Empty;
                case MonsterNameplateBadgeKind.SelfBuffStatus:
                    // 상태 배지와 같은 규약(지속 턴) — 아직 예고라 Amount가 지속 턴을 든다.
                    return badge.Amount > 0 ? badge.Amount.ToString() : string.Empty;
                case MonsterNameplateBadgeKind.SelfBuffShield:
                    // 현재 잔량(Shield)과 구별되는 "얻을 예정" — +가 미래형을 말한다.
                    return badge.Amount > 0 ? $"+{badge.Amount}" : string.Empty;
                case MonsterNameplateBadgeKind.BossGimmick:
                    // 무엇을 하는지는 글리프·툴팁이 말한다 — 숫자 칸이 없다.
                    return string.Empty;
                case MonsterNameplateBadgeKind.PropMaturity:
                    // 0 = 다음 몬스터 페이즈에 흡수·폭발 — 숫자 대신 !로 임박을 말한다.
                    return badge.Amount <= 0 ? "!" : badge.Amount.ToString();
                case MonsterNameplateBadgeKind.Trait:
                    // 숫자의 뜻이 특성마다 달라 찍을지는 조립부가 정했다(ShowsNumber) — 여기는 한 비트만 본다.
                    return badge.ShowsNumber && badge.Amount > 0 ? badge.Amount.ToString() : string.Empty;
                default:
                    // 상태이상은 남은 턴이 더 급한 정보다(수치는 툴팁이 말한다).
                    // 단 소비형(수호)만은 턴이 아니라 남은 횟수를 찍는다 — 판정은 툴팁과 같은 곳에 있다.
                    return StatusEffectTooltipContent.BadgeNumber(badge.Effect);
            }
        }

        private static RectTransform CreateMarkerBadge(
            RectTransform row,
            MonsterNameplateBadge badge,
            StatusEffectIconCatalog catalog,
            float x,
            float width)
        {
            var go = new GameObject($"Badge {badge.Kind}", typeof(RectTransform));
            go.hideFlags = RuntimeUiHideFlags;
            go.transform.SetParent(row, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, StatusIconPixelSize);

            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObject.hideFlags = RuntimeUiHideFlags;
            iconObject.transform.SetParent(rect, false);
            var iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(StatusIconPixelSize, StatusIconPixelSize);

            var image = iconObject.GetComponent<Image>();
            image.raycastTarget = false;

            // 카탈로그 스프라이트가 정본이고, 없으면 종류 색 칩 + 글리프로 떨어진다 — 툴팁·플레이어
            // 도크와 같은 폴백 규약이라 저작이 덜 된 상태에서도 판이 읽힌다.
            var sprite = ResolveBadgeSprite(badge, catalog);
            if (sprite != null)
            {
                image.sprite = sprite;
                image.color = ResolveBadgeSpriteTint(badge);
            }
            else
            {
                image.color = ResolveBadgeChipColor(badge);
                CreateBadgeGlyph(iconRect, ResolveBadgeGlyph(badge));
            }

            var number = ResolveBadgeNumberText(badge);
            if (!string.IsNullOrEmpty(number))
            {
                CreateBadgeNumber(rect, number, width - StatusIconPixelSize - 2f);
            }

            return rect;
        }

#if UNITY_INCLUDE_TESTS
        /// <summary>
        /// 배지 종류 → 카탈로그 슬롯 해소를 테스트에 그대로 노출한다. 게이트가 이 경로를 쓰지 않고
        /// 스위치를 베껴 두면 두 벌이 조용히 갈라진다 — 이 프로젝트가 반복해서 밟은 함정이다.
        /// </summary>
        internal static Sprite ResolveBadgeSpriteForTests(MonsterNameplateBadge badge, StatusEffectIconCatalog catalog)
            => ResolveBadgeSprite(badge, catalog);
#endif

        private static Sprite ResolveBadgeSprite(MonsterNameplateBadge badge, StatusEffectIconCatalog catalog)
        {
            if (catalog == null)
            {
                return null;
            }

            switch (badge.Kind)
            {
                case MonsterNameplateBadgeKind.Knockback:
                    // 부호가 방향이다(§16.1) — 음수는 끌어당김이라 그림이 갈라진다.
                    // 끌어당김 아트가 아직 없으면 밀치기로 떨어지느니 글리프 칩이 낫다(방향을 거짓말하지 않는다).
                    return badge.Amount < 0 ? catalog.PullSprite : catalog.KnockbackSprite;
                case MonsterNameplateBadgeKind.Attack:
                    return catalog.AttackIntentSprite;
                case MonsterNameplateBadgeKind.Agitation:
                    return catalog.AgitationSprite;
                case MonsterNameplateBadgeKind.Toughness:
                    return catalog.ToughnessSprite;
                case MonsterNameplateBadgeKind.Sturdy:
                    return catalog.SturdySprite;
                case MonsterNameplateBadgeKind.Shield:
                    // 아트를 새로 발주하지 않고 플레이어 HUD 방어도와 같은 그림을 재사용한다(§H-1).
                    return catalog.BlockSprite;
                case MonsterNameplateBadgeKind.Status:
                    return catalog.GetSprite(badge.Effect.Kind);
                case MonsterNameplateBadgeKind.SelfBuffStatus:
                    // 걸릴 상태의 그림을 그대로 예고에 쓴다 — 걸린 뒤의 Status 배지와 같은 어휘.
                    return catalog.GetSprite(badge.SelfBuffKind);
                case MonsterNameplateBadgeKind.SelfBuffShield:
                    return catalog.BlockSprite;
                case MonsterNameplateBadgeKind.Aftermath:
                    return catalog.AftermathSprite;
                case MonsterNameplateBadgeKind.Trait:
                    // traitId 키 슬롯. 아직 아트가 없는 특성은 null로 떨어져 글리프 칩이 그린다 —
                    // 저작이 덜 된 상태에서도 판이 읽히는 것이 이 프로젝트의 폴백 규약이다.
                    return catalog.GetTraitSprite(badge.TraitId);
                default:
                    // 다단은 발주하지 않기로 확정 — 공격 배지 옆 ×N 텍스트로 읽힌다.
                    // 전용 슬롯이 없는 종류는 글리프 칩으로 그린다.
                    return null;
            }
        }

        /// <summary>
        /// 스프라이트가 있을 때의 색. 기본은 원본 색(흰색 곱)이지만 <b>맷집만 예외</b>다 —
        /// 준비/소진이 색으로만 갈리는 배지라(<see cref="ResolveBadgeChipColor"/>) 아트가 들어온 뒤에도
        /// 소진 상태를 흐리게 눌러 주지 않으면 "지금 때리면 온전히 들어간다"는 정보가 사라진다.
        /// </summary>
        private static Color ResolveBadgeSpriteTint(MonsterNameplateBadge badge)
        {
            if (badge.Kind == MonsterNameplateBadgeKind.Toughness && badge.Amount <= 0)
            {
                return new Color(1f, 1f, 1f, 0.45f);
            }

            if (badge.Kind == MonsterNameplateBadgeKind.Trait && badge.Dimmed)
            {
                return new Color(1f, 1f, 1f, 0.45f);
            }

            return Color.white;
        }

        private static Color ResolveBadgeChipColor(MonsterNameplateBadge badge)
        {
            switch (badge.Kind)
            {
                case MonsterNameplateBadgeKind.Attack:
                case MonsterNameplateBadgeKind.MultiHit:
                    return new Color(0.78f, 0.16f, 0.14f, 0.92f);
                case MonsterNameplateBadgeKind.Knockback:
                    return new Color(0.30f, 0.42f, 0.72f, 0.92f);
                case MonsterNameplateBadgeKind.Shield:
                    // 플레이어 방어도 HUD와 같은 계열의 청회색 — 같은 기능이므로 같은 어휘로 읽혀야 한다.
                    return new Color(0.42f, 0.60f, 0.78f, 0.92f);
                case MonsterNameplateBadgeKind.Sturdy:
                    // 방어막과 같은 계열이되 더 짙게 — "그 방어막이 안 풀린다"는 뜻이라 색이 이어져야 한다.
                    return new Color(0.24f, 0.38f, 0.55f, 0.92f);
                case MonsterNameplateBadgeKind.Agitation:
                    return new Color(0.86f, 0.42f, 0.10f, 0.92f);
                case MonsterNameplateBadgeKind.Toughness:
                    // 준비 = 눈에 띄는 황록(막힌다), 소진 = 흐린 회색(지금은 온전히 들어간다).
                    return badge.Amount > 0
                        ? new Color(0.72f, 0.68f, 0.24f, 0.92f)
                        : new Color(0.34f, 0.36f, 0.40f, 0.70f);
                case MonsterNameplateBadgeKind.Aftermath:
                    // 죽음에 걸린 특성이라 보라 계열 — 상태이상 팔레트와 겹치지 않는 자리.
                    return new Color(0.52f, 0.30f, 0.62f, 0.92f);
                case MonsterNameplateBadgeKind.SelfBuffStatus:
                    return StatusEffectIconStyle.BackgroundColor(badge.SelfBuffKind, new Color(0.3f, 0.3f, 0.35f, 0.9f));
                case MonsterNameplateBadgeKind.SelfBuffShield:
                    // 방어막 잔량 배지와 같은 계열 — 같은 기능의 예고이므로 색이 이어져야 한다.
                    return new Color(0.42f, 0.60f, 0.78f, 0.92f);
                case MonsterNameplateBadgeKind.BossGimmick:
                    // 공격(적색)도 상태(팔레트)도 아닌 "판을 바꾸는 턴" — 호박색 계열로 따로 선다.
                    return new Color(0.82f, 0.52f, 0.10f, 0.92f);
                case MonsterNameplateBadgeKind.PropMaturity:
                    // 임박(0)은 위험 적색, 여유는 기믹과 같은 호박 계열 — "저 시계가 다 돌면 보스가 큰다".
                    return badge.Amount <= 0
                        ? new Color(0.78f, 0.16f, 0.14f, 0.92f)
                        : new Color(0.82f, 0.52f, 0.10f, 0.92f);
                case MonsterNameplateBadgeKind.Trait:
                    return ResolveTraitChipColor(badge);
                default:
                    return StatusEffectIconStyle.BackgroundColor(badge.Effect.Kind, new Color(0.3f, 0.3f, 0.35f, 0.9f));
            }
        }

        /// <summary>배지가 든 traitId로 저작 행을 편다. 카탈로그 미할당이면 false — 색·글리프가 중립값으로 떨어진다.</summary>
        private static bool TryGetTrait(MonsterNameplateBadge badge, out MonsterTraitDefinition trait)
        {
            trait = null;
            var catalog = MonsterTraitCatalogProvider.Active;
            return catalog != null && catalog.TryGet(badge.TraitId, out trait);
        }

        /// <summary>
        /// 특성 칩 색 — 정본은 monster_traits.csv의 badgeColorHex다. 소진 상태(<c>Dimmed</c>)는
        /// 그 색을 눌러 「지금은 일하지 않는다」를 말한다(맷집 준비/소진 규약을 일반화한 것).
        /// </summary>
        private static Color ResolveTraitChipColor(MonsterNameplateBadge badge)
        {
            var color = new Color(0.40f, 0.40f, 0.46f, 0.92f);
            if (TryGetTrait(badge, out var trait)
                && ColorUtility.TryParseHtmlString("#" + trait.BadgeColorHex, out var parsed))
            {
                color = new Color(parsed.r, parsed.g, parsed.b, 0.92f);
            }

            return badge.Dimmed ? new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.55f, 0.70f) : color;
        }

        private static string ResolveBadgeGlyph(MonsterNameplateBadge badge)
        {
            switch (badge.Kind)
            {
                case MonsterNameplateBadgeKind.Attack:
                    return "공";
                case MonsterNameplateBadgeKind.MultiHit:
                    return "연";
                case MonsterNameplateBadgeKind.Knockback:
                    return badge.Amount < 0 ? "끌" : "밀";
                case MonsterNameplateBadgeKind.Shield:
                    return "막";
                case MonsterNameplateBadgeKind.Sturdy:
                    return "견";
                case MonsterNameplateBadgeKind.Agitation:
                    return "약";
                case MonsterNameplateBadgeKind.Toughness:
                    return "맷";
                case MonsterNameplateBadgeKind.Aftermath:
                    return "뒤";
                case MonsterNameplateBadgeKind.SelfBuffStatus:
                    return StatusEffectIconStyle.Glyph(badge.SelfBuffKind);
                case MonsterNameplateBadgeKind.SelfBuffShield:
                    return "막";
                case MonsterNameplateBadgeKind.BossGimmick:
                    return BossGimmickBadgeText.Glyph(badge.GimmickId);
                case MonsterNameplateBadgeKind.PropMaturity:
                    return "흡";
                case MonsterNameplateBadgeKind.Trait:
                    return TryGetTrait(badge, out var glyphTrait) ? glyphTrait.Glyph : "특";
                default:
                    return StatusEffectIconStyle.Glyph(badge.Effect.Kind);
            }
        }

        private static void CreateBadgeGlyph(RectTransform iconRect, string glyph)
        {
            var go = new GameObject("Glyph", typeof(RectTransform));
            go.hideFlags = RuntimeUiHideFlags;
            go.transform.SetParent(iconRect, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var label = go.AddComponent<TextMeshProUGUI>();
            ApplyKoreanFontIfAvailable(label);
            label.text = glyph;
            // 칩 크기에 비례(#2 상향과 함께 움직인다) — 고정 13px는 30px 칩에서 글자가 빈다.
            label.fontSize = StatusIconPixelSize * 0.65f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            label.color = Color.white;
            ApplyOverlayZTest(label);
        }

        private static void CreateBadgeNumber(RectTransform badgeRect, string text, float width)
        {
            var go = new GameObject("Number", typeof(RectTransform));
            go.hideFlags = RuntimeUiHideFlags;
            go.transform.SetParent(badgeRect, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(Mathf.Max(0f, width), StatusIconPixelSize);

            var label = go.AddComponent<TextMeshProUGUI>();
            ApplyKoreanFontIfAvailable(label);
            label.text = text;
            label.fontSize = BadgeNumberFontSize;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            label.color = Color.white;
            ApplyOverlayZTest(label);
        }

        private static void CreateMarkerStatusOverflow(RectTransform row, int overflow, float x)
        {
            var go = new GameObject("Badge Overflow", typeof(RectTransform));
            go.hideFlags = RuntimeUiHideFlags;
            go.transform.SetParent(row, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(StatusIconPixelSize + 6f, StatusIconPixelSize);

            var label = go.AddComponent<TextMeshProUGUI>();
            ApplyKoreanFontIfAvailable(label);
            label.text = $"+{overflow}";
            // 칩 크기에 비례(#3 상향과 함께 움직인다) — 배지 40px에서 12px 글자는 판독 불가.
            label.fontSize = StatusIconPixelSize * 0.45f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            label.color = new Color(0.85f, 0.88f, 0.96f, 1f);
            ApplyOverlayZTest(label);
        }

        /// <summary>
        /// 커서 아래 배지를 찾는다(호버 툴팁용). 월드 스페이스 캔버스라 <see cref="GraphicRaycaster"/>
        /// 대신 사각형 검사를 쓴다 — 배지는 빌보드라 화면 정렬 사각형이 정확하고, 이벤트 시스템을
        /// 끌어들이지 않으므로 3D 픽킹(몬스터 본체 호버)과 우선순위가 꼬이지 않는다.
        ///
        /// <para>겹치면 <b>나중에 만들어진</b> 배지가 이긴다 — 렌더 순서상 위에 그려지는 쪽이다.</para>
        /// </summary>
        internal bool TryGetHoveredBadge(Camera camera, Vector2 screenPos, out MonsterNameplateBadge badge)
        {
            return TryGetHoveredBadge(camera, screenPos, out badge, out _);
        }

        internal bool TryGetHoveredBadge(
            Camera camera, Vector2 screenPos, out MonsterNameplateBadge badge, out string monsterId)
        {
            badge = default;
            monsterId = string.Empty;
            if (camera == null || !markerUiVisible)
            {
                return false;
            }

            var found = false;
            for (var i = 0; i < badgeHitTargets.Count; i++)
            {
                var target = badgeHitTargets[i];
                if (target.Rect == null || !target.Rect.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (RectTransformUtility.RectangleContainsScreenPoint(target.Rect, screenPos, camera))
                {
                    badge = target.Badge;
                    monsterId = target.MonsterId;
                    found = true;
                }
            }

            return found;
        }

        private readonly struct BadgeHitTarget
        {
            public BadgeHitTarget(RectTransform rect, MonsterNameplateBadge badge, string monsterId)
            {
                Rect = rect;
                Badge = badge;
                MonsterId = monsterId ?? string.Empty;
            }

            public RectTransform Rect { get; }
            public MonsterNameplateBadge Badge { get; }

            /// <summary>이 배지가 붙은 몬스터(2026-09-01 #3) — 뒤끝 호버 오버레이가 누구의 것인지 알아야 한다.</summary>
            public string MonsterId { get; }
        }

        private static void ApplyOverlayZTest(TMP_Text label)
        {
            if (label == null)
            {
                return;
            }

            var fontMaterial = label.fontMaterial;
            if (fontMaterial != null && fontMaterial.HasProperty(ZTestModePropertyId))
            {
                fontMaterial.SetFloat(ZTestModePropertyId, 8f);
            }
        }

        private static Material CreateMarkerMaterial(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Sprites/Default");
            var material = shader != null ? new Material(shader) : new Material(Shader.Find("Hidden/InternalErrorShader"));
            material.name = name;
            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            return material;
        }
        private static void ApplyHealthBar(MarkerEntry entry, int hp, int maxHp)
        {
            if (entry.HealthBarFill == null)
            {
                return;
            }

            var ratio = maxHp <= 0 ? 0f : Mathf.Clamp01((float)Mathf.Max(0, hp) / maxHp);
            entry.HealthBarFill.sizeDelta = new Vector2(
                Mathf.Max(1f, HealthBarPixelWidth * ratio),
                HealthBarPixelHeight);

            if (entry.HealthBarFillImage != null)
            {
                entry.HealthBarFillImage.color = HealthColor(ratio);
            }

            if (entry.HealthBarView != null)
            {
                entry.HealthBarView.SetHealth(hp, maxHp);
            }
        }

        private static void ApplyKoreanFontIfAvailable(TMP_Text label)
        {
            var font = KoreanFontProvider.Load();
            if (font != null)
            {
                label.font = font;
            }
        }

        private static Color HealthColor(float ratio)
        {
            if (ratio > 0.5f)
            {
                return new Color(0.18f, 0.95f, 0.26f, 1f);
            }

            if (ratio > 0.25f)
            {
                return new Color(1f, 0.82f, 0.16f, 1f);
            }

            return new Color(1f, 0.18f, 0.12f, 1f);
        }

        private bool TryGetPrimaryEntry(out MarkerEntry entry)
        {
            if (!string.IsNullOrEmpty(primaryMarkerId) && markers.TryGetValue(primaryMarkerId, out entry) && entry.Marker != null)
            {
                return true;
            }

            var first = markers.FirstOrDefault(pair => pair.Value.Marker != null);
            if (first.Value.Marker != null)
            {
                primaryMarkerId = first.Key;
                entry = first.Value;
                return true;
            }

            entry = default;
            return false;
        }

        private bool TryGetEntry(string markerId, out MarkerEntry entry)
        {
            markerId = NormalizeMarkerId(markerId);
            if (markers.TryGetValue(markerId, out entry) && entry.Marker != null)
            {
                return true;
            }

            if (markerId == DefaultMarkerId)
            {
                return TryGetPrimaryEntry(out entry);
            }

            entry = default;
            return false;
        }

        private static string NormalizeMarkerId(string markerId)
        {
            return string.IsNullOrWhiteSpace(markerId) ? DefaultMarkerId : markerId;
        }

        private static void FaceTransformToward(Transform target, Vector3 fromLocal, Vector3 toLocal)
        {
            if (target == null)
            {
                return;
            }

            var direction = toLocal - fromLocal;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            target.localRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        public readonly struct MonsterMarkerState
        {
            public MonsterMarkerState(string id, Vector3 localPosition)
                : this(id, localPosition, string.Empty, Color.white, 0, 0, false, null, Vector3.one)
            {
            }

            public MonsterMarkerState(string id, Vector3 localPosition, string label, Color accentColor)
                : this(id, localPosition, label, accentColor, 0, 0, false, null, Vector3.one)
            {
            }

            public MonsterMarkerState(string id, Vector3 localPosition, string label, Color accentColor, int hp, int maxHp)
                : this(id, localPosition, label, accentColor, hp, maxHp, false, null, Vector3.one)
            {
            }

            public MonsterMarkerState(
                string id,
                Vector3 localPosition,
                string label,
                Color accentColor,
                int hp,
                int maxHp,
                GameObject visualPrefab,
                Vector3 visualLocalScaleMultiplier)
                : this(id, localPosition, label, accentColor, hp, maxHp, false, false, visualPrefab, visualLocalScaleMultiplier)
            {
            }

            public MonsterMarkerState(
                string id,
                Vector3 localPosition,
                string label,
                Color accentColor,
                int hp,
                int maxHp,
                bool isBoss,
                GameObject visualPrefab,
                Vector3 visualLocalScaleMultiplier,
                bool isElite = false)
                : this(id, localPosition, label, accentColor, hp, maxHp, isBoss, isElite, visualPrefab, visualLocalScaleMultiplier)
            {
            }

            public MonsterMarkerState(
                string id,
                Vector3 localPosition,
                string label,
                Color accentColor,
                int hp,
                int maxHp,
                bool isBoss,
                bool isElite,
                GameObject visualPrefab,
                Vector3 visualLocalScaleMultiplier)
            {
                Id = id;
                LocalPosition = localPosition;
                Label = label ?? string.Empty;
                AccentColor = accentColor;
                Hp = hp;
                MaxHp = maxHp;
                IsBoss = isBoss;
                IsElite = isElite;
                VisualPrefab = visualPrefab;
                VisualLocalScaleMultiplier = SanitizeScale(visualLocalScaleMultiplier);
            }

            public string Id { get; }
            public Vector3 LocalPosition { get; }
            public string Label { get; }
            public Color AccentColor { get; }
            public int Hp { get; }
            public int MaxHp { get; }
            public bool IsBoss { get; }
            public bool IsElite { get; }
            public GameObject VisualPrefab { get; }
            public Vector3 VisualLocalScaleMultiplier { get; }

            private static Vector3 SanitizeScale(Vector3 scale)
            {
                return scale.x > 0f && scale.y > 0f && scale.z > 0f
                    ? scale
                    : Vector3.one;
            }
        }

        private readonly struct MarkerEntry
        {
            public MarkerEntry(GameObject marker, GameObject sourcePrefab, Vector3 prefabBaseScale, CharacterActorVisual actorVisual, Material material, Renderer badgeRenderer, GameObject nameplateRoot, TMP_Text label, TMP_Text bossLabel, RectTransform healthBarFill, Image healthBarFillImage, MonsterHealthBarView healthBarView, CharacterHoverTarget hoverTarget, GameObject visualRoot, RectTransform statusRow = null)
            {
                HoverTarget = hoverTarget;
                VisualRoot = visualRoot;
                StatusRow = statusRow;
                Marker = marker;
                SourcePrefab = sourcePrefab;
                PrefabBaseScale = prefabBaseScale;
                ActorVisual = actorVisual;
                Material = material;
                BadgeRenderer = badgeRenderer;
                NameplateRoot = nameplateRoot;
                Label = label;
                BossLabel = bossLabel;
                HealthBarFill = healthBarFill;
                HealthBarFillImage = healthBarFillImage;
                HealthBarView = healthBarView;
            }

            public MarkerEntry WithPrefabBaseScale(Vector3 prefabBaseScale)
            {
                return new MarkerEntry(Marker, SourcePrefab, prefabBaseScale, ActorVisual, Material, BadgeRenderer, NameplateRoot, Label, BossLabel, HealthBarFill, HealthBarFillImage, HealthBarView, HoverTarget, VisualRoot, StatusRow);
            }

            public GameObject Marker { get; }
            // Prefab this marker's visual was instantiated from (null = primitive fallback). Used to detect when a
            // monster's resolved model changes (e.g. spawn definition edited) so the visual can be rebuilt.
            public GameObject SourcePrefab { get; }
            // 프리팹이 원래 갖고 있던 localScale. 배율은 항상 이 값에 곱해서 다시 계산한다 — 현재
            // localScale(이미 배율이 곱해진 값)에 다시 곱하면 갱신할 때마다 크기가 복리로 부푼다.
            public Vector3 PrefabBaseScale { get; }
            public CharacterActorVisual ActorVisual { get; }
            public Material Material { get; }
            public Renderer BadgeRenderer { get; }
            public GameObject NameplateRoot { get; }
            public TMP_Text Label { get; }
            public TMP_Text BossLabel { get; }
            public RectTransform HealthBarFill { get; }
            public Image HealthBarFillImage { get; }
            public MonsterHealthBarView HealthBarView { get; }

            /// <summary>상태 아이콘 행(⑥). 마커 수명 동안 살아 있고 내용만 갈아 끼운다.</summary>
            public RectTransform StatusRow { get; }
            // 마우스 판정 상자(마커 루트에 붙는다). 배율이 바뀔 때마다 다시 재야 한다.
            public CharacterHoverTarget HoverTarget { get; }
            // 판정 상자를 재는 기준이 되는 모델 루트. 뱃지·명판을 빼고 이것만 잰다.
            public GameObject VisualRoot { get; }
        }
    }

    internal sealed class CombatWorldSpaceBillboard : MonoBehaviour
    {
        private Camera cachedCamera;

        private void LateUpdate()
        {
            var targetCamera = ResolveCamera();
            if (targetCamera == null)
            {
                return;
            }

            var direction = transform.position - targetCamera.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = targetCamera.transform.forward;
                direction.y = 0f;
            }

            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        private Camera ResolveCamera()
        {
            if (cachedCamera != null && cachedCamera.isActiveAndEnabled)
            {
                return cachedCamera;
            }

            cachedCamera = Camera.main;
            if (cachedCamera != null)
            {
                return cachedCamera;
            }

            cachedCamera = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(camera => camera.isActiveAndEnabled);
            return cachedCamera;
        }
    }
}

