using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    public static class CombatOverlayTheme
    {
        private const string ThemeAssetResourcePath = "CombatOverlayTheme";

        private static CombatOverlayThemeAsset cachedAsset;
        private static bool assetLoadAttempted;

        /// <summary>
        /// Optional inspector-authored theme (Resources/CombatOverlayTheme.asset). When present its
        /// per-layer entries win; otherwise every layer falls back to the hardcoded design defaults
        /// below. Also the source of the overlay shader reference for the renderer.
        /// </summary>
        public static CombatOverlayThemeAsset Asset
        {
            get
            {
                if (!assetLoadAttempted)
                {
                    cachedAsset = Resources.Load<CombatOverlayThemeAsset>(ThemeAssetResourcePath);
                    assetLoadAttempted = true;
                }

                return cachedAsset;
            }
        }

        /// <summary>Test/editor hook to drop the cached asset so the next resolve reloads.</summary>
        public static void InvalidateCache()
        {
            cachedAsset = null;
            assetLoadAttempted = false;
        }

        public static CombatOverlayStyle ResolveDefaultStyle(HexOverlayLayer layer)
        {
            var asset = Asset;
            if (asset != null && asset.TryResolve(layer, out var style, out _))
            {
                return style;
            }

            return ResolveFallbackStyle(layer);
        }

        /// <summary>
        /// Render-queue offset above <see cref="HexOverlayRenderOrder.CombatOverlayRenderQueue"/> so
        /// overlapping fills composite deterministically (threats always on top). Theme asset entries
        /// override the fallback table.
        /// </summary>
        public static int ResolveRenderPriority(HexOverlayLayer layer)
        {
            var asset = Asset;
            if (asset != null && asset.TryResolve(layer, out _, out var priority))
            {
                return priority;
            }

            return ResolveFallbackPriority(layer);
        }

        private static int ResolveFallbackPriority(HexOverlayLayer layer)
        {
            switch (layer)
            {
                case HexOverlayLayer.Reachable: return 0;
                case HexOverlayLayer.MonsterChaseRange: return 1;
                case HexOverlayLayer.PlayerHoverMove:
                case HexOverlayLayer.PlayerHoverAction:
                case HexOverlayLayer.FieldObjectRange: return 2;
                case HexOverlayLayer.AttackRange:
                case HexOverlayLayer.PlayerActionRange: return 3;
                case HexOverlayLayer.PlayerActionEffectArea: return 4;
                case HexOverlayLayer.MonsterMoveIntent:
                case HexOverlayLayer.Path: return 5;
                case HexOverlayLayer.MonsterAttackIntent: return 6;
                case HexOverlayLayer.TutorialTarget: return 7;
                // 결계는 전술 오버레이 전부보다 위에 그린다 — 봉쇄는 다른 어떤 큐보다 우선하는 사실이다.
                case HexOverlayLayer.BossArenaBoundary: return 8;
                // 보스 점유는 판의 사실이지만 조용한 배경이다 — 도달 채움(0) 위, 호버·조준 힌트(2+) 아래.
                case HexOverlayLayer.BossFootprint: return 1;
                // 부서진 땅(#18)은 판의 조용한 <b>사실</b>이다 — 도달 채움(0) 위, 호버·조준 힌트(2+) 아래.
                // 보스 점유와 같은 층에 둔다: 둘 다 「이 칸은 원래 이렇다」를 말하는 배경이다.
                case HexOverlayLayer.RupturedGround: return 1;
                // 뒤끝 반경(#3)은 <b>질문에 답하는 동안만</b> 뜨는 층이라 위에 올린다 — 손을 얹은
                // 순간 다른 무엇보다 먼저 읽혀야 한다. 결계(8)보다는 아래.
                case HexOverlayLayer.AftermathReach: return 7;
                // 철조각 폭발 범위(2026-09-05 결정 4)도 「손을 얹은 동안만」 층 — 뒤끝 반경과 같은 높이.
                case HexOverlayLayer.BossPropBlastReach: return 7;
                // 사슬 예고(2026-09-05 결정 5)는 위험 해치(6)와 같은 층 — 같은 턴의 「밟으면 아픈 칸」이다.
                case HexOverlayLayer.BossScrapChainTelegraph: return 6;
                // 살포 예고(2026-09-05 후속 #1)도 위험 해치(6)와 같은 층 — 「다음 적 턴에 여기 놓인다」는 같은 급의 경고다.
                case HexOverlayLayer.BossPropVolleyTelegraph: return 6;
                // 취약 부위·전멸기 후보는 보스 점유(1) <b>위</b>에 올라야 한다 — 둘 다 몸통/아레나 칸에
                // 겹쳐 그려지므로 아래로 깔리면 통째로 묻힌다. 결계(8)보다는 아래다.
                case HexOverlayLayer.BossWeakSpot: return 3;
                case HexOverlayLayer.BossSafeZoneCandidate: return 3;
                case HexOverlayLayer.BossSafeZoneConfirmed: return 3;
                // 자기부여 예고(#10)는 몬스터 제자리 한 칸(보스는 footprint)에만 뜨고 위험 해치와
                // 겹치지 않는다(공격 예고가 없는 턴이라 존재하는 표시다). 도달 채움(0) 위, 조준 힌트 아래.
                case HexOverlayLayer.MonsterSelfBuffIntent: return 2;
                // 소환 예고도 자기부여와 같은 층(도달 채움 위·조준 힌트 아래). 둘이 한 칸에서 겹칠 일은
                // 없다 — 소환 턴에는 자기부여 예고가 몬스터 제자리에만 뜨기 때문이다.
                case HexOverlayLayer.MonsterSummonIntent: return 2;
                default: return 0;
            }
        }

        /// <summary>
        /// 취약 부위 스타일. <paramref name="expiringThisTurn"/>이면 <b>저채도·저알파</b> 변형을 돌려
        /// 만료 임박을 알린다(§20-A-7).
        ///
        /// 판명은 2턴짜리이므로 "이번 턴이 마지막인가"가 곧 플레이 결정(지금 정찰을 다시 쓸 것인가)이다.
        /// 아무 예고 없이 사라지면 버그로 읽힌다. 색상은 유지하고 <b>강도만</b> 떨어뜨리는 이유는,
        /// 색을 바꾸면 "다른 것"으로 읽혀 같은 취약 부위라는 정체성이 끊기기 때문이다.
        /// </summary>
        public static CombatOverlayStyle ResolveBossWeakSpotStyle(bool expiringThisTurn)
        {
            var full = ResolveDefaultStyle(HexOverlayLayer.BossWeakSpot);
            if (!expiringThisTurn)
            {
                return full;
            }

            return new CombatOverlayStyle(
                Desaturate(full.FillColor, 0.45f),
                Desaturate(full.BoundaryColor, 0.45f),
                full.BoundaryThickness,
                full.UseFill,
                full.UseBoundary,
                edgeGlowColor: Desaturate(full.EdgeGlowColor, 0.45f),
                edgeGlowWidth: full.EdgeGlowWidth,
                edgeFeather: full.EdgeFeather,
                // 맥동을 빠르게 = "깜빡이며 꺼져 간다". 알파 하한도 함께 내려 대비가 커진다.
                pulseSpeed: full.PulseSpeed * 2f,
                pulseAlphaMin: 0.18f,
                pulseAlphaMax: 0.85f,
                fillRadiusScale: full.FillRadiusScale);
        }

        /// <summary>색상은 유지한 채 채도와 알파만 낮춘다(HDR 값이라 Color.Lerp로 회색에 섞는다).</summary>
        private static Color Desaturate(Color color, float amount)
        {
            var gray = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;
            var muted = Color.Lerp(color, new Color(gray, gray, gray, color.a), Mathf.Clamp01(amount));
            muted.a = color.a * (1f - Mathf.Clamp01(amount) * 0.5f);
            return muted;
        }

        private static CombatOverlayStyle ResolveFallbackStyle(HexOverlayLayer layer)
        {
            switch (layer)
            {
                case HexOverlayLayer.Reachable:
                    // Cyan, faint fill lifted by a cyan edge glow. Static, tile-unit footprint.
                    return new CombatOverlayStyle(
                        new Color(0.08f, 0.82f, 0.95f, 0.10f),
                        new Color(0.08f, 0.82f, 0.95f, 0.0f),
                        0.07f,
                        useFill: true,
                        useBoundary: false,
                        edgeGlowColor: new Color(0.10f, 0.95f, 1.10f, 1.0f),
                        edgeGlowWidth: 0.55f,
                        edgeFeather: 0.16f);

                case HexOverlayLayer.PlayerHoverMove:
                    // Bright cyan rim glow, no boundary line.
                    return new CombatOverlayStyle(
                        new Color(0.08f, 0.82f, 0.95f, 0.35f),
                        new Color(0.08f, 0.82f, 0.95f, 0.0f),
                        0.09f,
                        useFill: true,
                        useBoundary: false,
                        edgeGlowColor: new Color(0.20f, 1.10f, 1.30f, 1.0f),
                        edgeGlowWidth: 0.7f,
                        edgeFeather: 0.18f);

                case HexOverlayLayer.PlayerHoverAction:
                    // Bright blue rim glow, no boundary line.
                    return new CombatOverlayStyle(
                        new Color(0.12f, 0.38f, 1f, 0.35f),
                        new Color(0.12f, 0.38f, 1f, 0.0f),
                        0.12f,
                        useFill: true,
                        useBoundary: false,
                        edgeGlowColor: new Color(0.30f, 0.55f, 1.40f, 1.0f),
                        edgeGlowWidth: 0.7f,
                        edgeFeather: 0.18f);

                case HexOverlayLayer.AttackRange:
                case HexOverlayLayer.PlayerActionRange:
                    // Blue fill with edge glow and a faintly HDR blue border.
                    return new CombatOverlayStyle(
                        new Color(0.12f, 0.38f, 1f, 0.16f),
                        new Color(0.25f, 0.50f, 1.25f, 0.90f),
                        0.10f,
                        useFill: true,
                        useBoundary: true,
                        edgeGlowColor: new Color(0.30f, 0.55f, 1.30f, 0.9f),
                        edgeGlowWidth: 0.5f,
                        edgeFeather: 0.14f);

                case HexOverlayLayer.PlayerActionEffectArea:
                    // Amber fill with a marching-ants border.
                    return new CombatOverlayStyle(
                        new Color(1f, 0.74f, 0.06f, 0.30f),
                        new Color(1f, 0.80f, 0.10f, 0.95f),
                        0.13f,
                        useFill: true,
                        useBoundary: true,
                        edgeGlowColor: new Color(1.10f, 0.80f, 0.10f, 0.6f),
                        edgeGlowWidth: 0.45f,
                        edgeFeather: 0.14f,
                        antsSpeed: 0.6f,
                        antsDashLength: 0.5f);

                case HexOverlayLayer.MonsterSelfBuffIntent:
                    // 자기부여 예고(#10): 금빛 채움 + 실선 테두리 + 느린 숨쉬기. 어휘를 위험(붉은 사선
                    // 해치)·이동(주황 점선)과 <b>형상으로</b> 가른다 — 해치도 점선도 쓰지 않는 유일한
                    // 몬스터 예고라 색이 겹쳐도 한눈에 갈린다(색상환 포화 이후의 식별 규약).
                    return new CombatOverlayStyle(
                        new Color(1.00f, 0.78f, 0.22f, 0.34f),
                        new Color(1.00f, 0.86f, 0.38f, 0.95f),
                        0.12f,
                        useFill: true,
                        useBoundary: true,
                        edgeGlowColor: new Color(1.00f, 0.82f, 0.30f, 0.55f),
                        edgeGlowWidth: 0.40f,
                        edgeFeather: 0.16f,
                        pulseSpeed: 2.2f,
                        pulseAlphaMin: 0.55f,
                        pulseAlphaMax: 1.0f,
                        fillRadiusScale: 0.92f);

                case HexOverlayLayer.MonsterSummonIntent:
                    // 소환 예고(§4-4): 청록 채움 + 실선 테두리 + 빠른 점멸. 위험(붉은 사선 해치)·이동
                    // (주황 점선)·자기부여(금빛 숨쉬기) 어느 쪽과도 색과 리듬이 겹치지 않는다 —
                    // 「여기 나타남」은 아픈 칸이 아니라 곧 막히는 칸이라 어휘가 따로 있어야 한다.
                    return new CombatOverlayStyle(
                        new Color(0.30f, 0.85f, 0.80f, 0.30f),
                        new Color(0.45f, 0.95f, 0.90f, 0.95f),
                        0.12f,
                        useFill: true,
                        useBoundary: true,
                        edgeGlowColor: new Color(0.40f, 0.95f, 0.90f, 0.55f),
                        edgeGlowWidth: 0.42f,
                        edgeFeather: 0.15f,
                        pulseSpeed: 3.4f,
                        pulseAlphaMin: 0.45f,
                        pulseAlphaMax: 1.0f,
                        fillRadiusScale: 0.88f);

                case HexOverlayLayer.MonsterAttackIntent:
                    // Danger: red diagonal stripes that breathe ~1 Hz. Tuned as a translucent "danger
                    // hatch" (transparent gaps, softer pulse) so when it overlaps the movement-reachable
                    // fill the cyan reads through the gaps → a tile shows as movable AND dangerous.
                    // radiusScale 1.0 so threatened tiles merge into one silhouette.
                    return new CombatOverlayStyle(
                        new Color(1f, 0.12f, 0.10f, 1.0f),
                        new Color(1f, 0.12f, 0.10f, 0.0f),
                        0.14f,
                        useFill: true,
                        useBoundary: false,
                        fillPatternMode: 1,
                        patternScale: 4.0f,
                        patternAngle: 45f,
                        patternScroll: 0f,
                        patternOpacity: 0.72f,
                        pulseSpeed: 6.28f,
                        pulseAlphaMin: 0.28f,
                        pulseAlphaMax: 0.45f,
                        fillRadiusScale: 1.0f);

                case HexOverlayLayer.MonsterMoveIntent:
                case HexOverlayLayer.Path:
                    // Orange dashed border, slow scroll, no fill.
                    return new CombatOverlayStyle(
                        new Color(1f, 0.55f, 0.12f, 0.0f),
                        new Color(1f, 0.60f, 0.14f, 0.90f),
                        0.09f,
                        useFill: false,
                        useBoundary: true,
                        antsSpeed: 0.3f,
                        antsDashLength: 0.55f);

                case HexOverlayLayer.MonsterChaseRange:
                    // Purple dashed border only — awareness radius, no fill wash.
                    return new CombatOverlayStyle(
                        new Color(0.72f, 0.18f, 1f, 0.0f),
                        new Color(0.72f, 0.28f, 1f, 0.70f),
                        0.07f,
                        useFill: false,
                        useBoundary: true,
                        antsSpeed: 0.2f,
                        antsDashLength: 0.7f);

                case HexOverlayLayer.FieldObjectRange:
                    // Green footprint with a soft glow shown while hovering a placed field object.
                    return new CombatOverlayStyle(
                        new Color(0.20f, 0.85f, 0.35f, 0.35f),
                        new Color(0.20f, 0.85f, 0.35f, 0.0f),
                        0.10f,
                        useFill: true,
                        useBoundary: false,
                        edgeGlowColor: new Color(0.25f, 1.05f, 0.45f, 1.0f),
                        edgeGlowWidth: 0.6f,
                        edgeFeather: 0.16f);

                case HexOverlayLayer.BossScrapChainTelegraph:
                    // 사슬 예고(2026-09-05): 위험 해치와 같은 사선 무늬지만 <b>전격의 노란빛</b>과 반대 각도(135°)로
                    // 갈라 「패턴 공격이 아니라 철조각 전격」임을 읽게 한다. 붉은 공격 해치와 같은 칸에 겹칠 수 있어
                    // 색·각도 둘 다 달라야 한다. 채움 무늬만·경계 없음(해치 어휘 유지).
                    return new CombatOverlayStyle(
                        new Color(1f, 0.85f, 0.25f, 1.0f),
                        new Color(1f, 0.85f, 0.25f, 0.0f),
                        0.14f,
                        useFill: true,
                        useBoundary: false,
                        fillPatternMode: 1,
                        patternScale: 4.0f,
                        patternAngle: 135f,
                        patternScroll: 0.6f,
                        patternOpacity: 0.78f);

                case HexOverlayLayer.BossPropVolleyTelegraph:
                    // 살포 예고(2026-09-05 후속 #1): 공격 예고와 같은 <b>붉은 사선 해치</b> 어휘이되 ①주황 기운
                    // ②또렷한 테두리 ③느린 맥동으로 갈린다 — 「이번 턴 밟으면 아프다」가 아니라 「다음 적 턴에
                    // 여기 철조각이 선다」라, 칸의 <b>자리</b>가 읽혀야 한다(테두리). 해치 각도는 공격 예고(45°)와
                    // 같고 사슬 예고(135°)와 반대 — 살포와 사슬이 한 판에 겹쳐도 각도로 갈린다.
                    return new CombatOverlayStyle(
                        new Color(1f, 0.30f, 0.12f, 1.0f),
                        new Color(1.25f, 0.42f, 0.18f, 0.95f),
                        0.12f,
                        useFill: true,
                        useBoundary: true,
                        fillPatternMode: 1,
                        patternScale: 4.0f,
                        patternAngle: 45f,
                        patternScroll: 0.35f,
                        patternOpacity: 0.62f,
                        edgeGlowColor: new Color(1.4f, 0.5f, 0.2f, 0.9f),
                        edgeGlowWidth: 0.45f,
                        edgeFeather: 0.14f,
                        pulseSpeed: 3.2f,
                        pulseAlphaMin: 0.55f,
                        pulseAlphaMax: 1.0f,
                        fillRadiusScale: 0.94f);

                case HexOverlayLayer.BossPropBlastReach:
                    // 철조각 폭발 범위(2026-09-05 결정 4): 손을 얹은 동안만 뜨는 <b>주황빛 붉은 채움 + 또렷한 테두리</b>.
                    // 뒤끝 반경(보라)과 같은 형태 문법이되 위험 어휘라 붉은 계열. 성숙 마지막 턴 맥동은 후속.
                    return new CombatOverlayStyle(
                        new Color(1f, 0.38f, 0.16f, 0.42f),
                        new Color(1.3f, 0.5f, 0.2f, 0.95f),
                        0.12f,
                        useFill: true,
                        useBoundary: true,
                        edgeGlowColor: new Color(1.5f, 0.6f, 0.25f, 1f),
                        edgeGlowWidth: 0.5f,
                        edgeFeather: 0.14f);

                case HexOverlayLayer.AftermathReach:
                    // 특성 범위(2026-09-04 · 옛 뒤끝 반경): <b>보라</b> 채움 + 또렷한 테두리(사용자 확정 색).
                    // 뒤끝·담력 시험·홀림이 같은 레이어를 쓴다 — 셋 다 「손을 얹은 동안에만 뜨는,
                    // 이 특성이 미치는 자리」라 어휘가 하나다. 보라는 판에서
                    // 추격 반경(테두리만·채움 없음)이 이미 쓰지만 형태가 달라 겹쳐 읽히지 않는다 —
                    // 이쪽은 채움이 있고, 무엇보다 <b>손을 얹은 동안에만</b> 뜬다.
                    return new CombatOverlayStyle(
                        new Color(0.62f, 0.30f, 0.86f, 0.40f),
                        new Color(0.78f, 0.45f, 1f, 0.95f),
                        0.12f,
                        useFill: true,
                        useBoundary: true,
                        edgeGlowColor: new Color(0.85f, 0.50f, 1.15f, 1f),
                        edgeGlowWidth: 0.5f,
                        edgeFeather: 0.14f);

                case HexOverlayLayer.RupturedGround:
                    // 부서진 땅(#18 · 2026-09-05 균열 셰이더로 개정). 종전에는 마른 흙빛 <b>채움 한 겹</b>
                    // (알파 0.42)이었고, 사용자 판정은 "발동한 건지 아닌지 구분이 안 간다"였다 —
                    // 낮은 채도의 평면 채움은 타일 아트 위에서 그냥 그늘로 읽힌다.
                    //
                    // 🔑 이제 <b>형상</b>으로 말한다: 패턴 모드 3(균열)이 갈라진 땅의 그물을 그린다.
                    // 채움 알파를 크게 잡고 패턴 불투명도를 0.9로 두면 판때기 부분은 0.1배(≈0.09)만
                    // 남고 균열선만 진하게 찍힌다 — 「땅이 비치는데 금이 가 있다」가 된다.
                    //
                    // 🔑 붉은 사선(위험 예고)과 색·형태를 여전히 피한다 — 그 어휘는 「이번 턴」이고
                    // 이쪽은 「계속 이렇다」다. 균열은 무채에 가까운 어둠이라 조준·도달 힌트를 덮지 않는다.
                    return new CombatOverlayStyle(
                        new Color(0.11f, 0.07f, 0.05f, 0.92f),
                        new Color(0.34f, 0.24f, 0.17f, 0.90f),
                        0.06f,
                        useFill: true,
                        useBoundary: true,
                        edgeFeather: 0.10f,
                        fillPatternMode: 3,
                        patternScale: 1.5f,
                        patternOpacity: 0.90f);

                case HexOverlayLayer.TutorialTarget:
                    // Gold HDR border with a slow glow pulse to draw the eye during tutorials.
                    return new CombatOverlayStyle(
                        new Color(1f, 0.85f, 0.25f, 0.50f),
                        new Color(1.30f, 1.00f, 0.30f, 1.0f),
                        0.16f,
                        useFill: true,
                        useBoundary: true,
                        edgeGlowColor: new Color(1.40f, 1.10f, 0.35f, 1.0f),
                        edgeGlowWidth: 0.6f,
                        edgeFeather: 0.14f,
                        pulseSpeed: 2.0f,
                        pulseAlphaMin: 0.6f,
                        pulseAlphaMax: 1.0f);

                case HexOverlayLayer.BossArenaBoundary:
                    // 결계: 보스 레드의 <b>불투명한 벽</b>. 형태로 구분한다 — 붉은 사선 해치는 이미
                    // MonsterAttackIntent(위험 타일)의 어휘라, 결계까지 해치로 그리면 "밟으면 아픈 칸"과
                    // "지나갈 수 없는 칸"이 같은 그림이 된다. 결계는 무늬 없는 짙은 채움 + 두꺼운 HDR
                    // marching ants 테두리로 간다.
                    // fillRadiusScale 1.0: 인접한 링 셀이 하나의 실루엣으로 합쳐져 낱개 육각형 목걸이가
                    // 아니라 끊김 없는 담장으로 읽힌다(MonsterAttackIntent와 같은 이유의 같은 값).
                    return new CombatOverlayStyle(
                        new Color(0.62f, 0.05f, 0.03f, 0.78f),
                        new Color(1.50f, 0.32f, 0.12f, 1.0f),
                        0.22f,
                        useFill: true,
                        useBoundary: true,
                        edgeGlowColor: new Color(1.60f, 0.40f, 0.16f, 1.0f),
                        edgeGlowWidth: 0.7f,
                        edgeFeather: 0.10f,
                        antsSpeed: 0.45f,
                        antsDashLength: 0.4f,
                        pulseSpeed: 2.4f,
                        pulseAlphaMin: 0.78f,
                        pulseAlphaMax: 1.0f,
                        fillRadiusScale: 1.0f,
                        // 🔴 결계는 <b>장애물 위에도 그린다</b>(2026-09-02 사용자 요구). 아레나 경계 링은
                        //    본래 벽을 따라 서므로 그 칸 상당수가 건물·울타리다 — Stage 1은 30칸 중 10칸이
                        //    그렇다. 깊이 검사를 그대로 두면 담장의 3분의 1이 건물 뒤로 사라져
                        //    「여기는 열려 있나?」로 읽힌다. 장애물 칸이야말로 결계가 말해야 하는 칸이다.
                        drawOverObstacles: true);

                case HexOverlayLayer.BossFootprint:
                    // 보스 점유(P6·V3): <b>무채색 면 + 보스 적색 윤곽</b>. 색상 축은 이미 전부 점유돼 있어
                    // (적색=공격 예고 해치, 주황=이동 예고/결계, 보라=추격…) 면을 적색으로 두면 해치와
                    // 구분에 한 박자 걸린다 — 실플레이 판정에서 "구분 안 됨"으로 기각됐다. 그래서 면은
                    // 무채색으로 빼 대비를 확보하고, 테두리만 보스 적색으로 굵게 남겨 정체성을 유지한다.
                    // "밟으면 아픈 칸"(해치)도 "지나갈 수 없는 벽"(불투명 담장)도 아니고, "여기는 보스
                    // 몸이라 설 수 없다"는 바닥 사실 표시다. 무늬·ants·맥동은 여전히 없다.
                    // ⚠️ 셰이더가 정상 알파 블렌딩이라 타일이 밝으면 검정 0.58도 회색으로 앉는다.
                    // 더 눌러야 한다는 판정이 나오면 면 알파를 0.70~0.85 구간에서 올린다(바닥 텍스처가
                    // 죽기 시작하므로 상한은 사용자 판정 사항).
                    // fillRadiusScale 1.0: 7칸이 하나의 몸 실루엣으로 합쳐져 읽힌다.
                    return new CombatOverlayStyle(
                        new Color(0.03f, 0.03f, 0.04f, 0.58f),
                        new Color(1.25f, 0.32f, 0.24f, 0.98f),
                        0.17f,
                        useFill: true,
                        useBoundary: true,
                        edgeGlowColor: new Color(1.30f, 0.35f, 0.26f, 0.85f),
                        edgeGlowWidth: 0.45f,
                        edgeFeather: 0.06f,
                        fillRadiusScale: 1.0f);

                case HexOverlayLayer.BossWeakSpot:
                    // 취약 부위(§20-A-7): <b>밝은 호박(≈45°)</b>. 색 제약은 상태 아이콘 트랙에서 확립된
                    // 둘을 그대로 적용한다 — ⓐ 배경(인디고)에서 멀 것 ⓑ <b>같은 칸을 순환하는 다른
                    // 오버레이에서 멀 것</b>. 이 칸에는 붉은 공격 예고 해치와 무채색 BossFootprint가
                    // 반드시 겹치므로(보스 몸통이다) 적색 계열은 쓸 수 없다.
                    // 맥동을 두는 이유는 만료 임박(마지막 턴)을 저채도 변형으로 알리기 위해서다 —
                    // 아무 예고 없이 사라지면 버그로 읽힌다.
                    return new CombatOverlayStyle(
                        new Color(1.00f, 0.78f, 0.16f, 0.42f),
                        new Color(1.60f, 1.20f, 0.30f, 1.0f),
                        0.20f,
                        useFill: true,
                        useBoundary: true,
                        edgeGlowColor: new Color(1.70f, 1.30f, 0.35f, 1.0f),
                        edgeGlowWidth: 0.6f,
                        edgeFeather: 0.10f,
                        pulseSpeed: 2.6f,
                        pulseAlphaMin: 0.55f,
                        pulseAlphaMax: 1.0f,
                        fillRadiusScale: 1.0f);

                case HexOverlayLayer.BossSafeZoneConfirmed:
                    // 정찰로 판명된 진짜 안전지대(§28 W5): <b>초록 확정</b>. 후보 청록과 달리 이 색은
                    // 판정을 말한다 — "여기는 확실히 안전하다". 붉은 예고 해치 위에서도 읽혀야 한다.
                    return new CombatOverlayStyle(
                        new Color(0.12f, 0.78f, 0.30f, 0.46f),
                        new Color(0.35f, 1.60f, 0.60f, 1.0f),
                        0.20f,
                        useFill: true,
                        useBoundary: true,
                        edgeGlowColor: new Color(0.40f, 1.70f, 0.70f, 1.0f),
                        edgeGlowWidth: 0.6f,
                        edgeFeather: 0.10f,
                        pulseSpeed: 1.6f,
                        pulseAlphaMin: 0.70f,
                        pulseAlphaMax: 1.0f,
                        fillRadiusScale: 1.0f);

                case HexOverlayLayer.BossSafeZoneCandidate:
                    // 전멸기 안전지대 후보(§20-B-6): <b>중립 청록</b>. 진짜인지 가짜인지를 색으로
                    // 말하면 안 되므로(그건 정찰이 답할 질문이다) 후보 셋이 전부 같은 색이다.
                    // 판별되면 이 레이어에서 빠지고, 가짜였다면 아래 깔린 붉은 예고가 드러난다.
                    // 붉은 예고 해치와 겹쳐 그려지므로 적색에서 멀어야 한다.
                    return new CombatOverlayStyle(
                        new Color(0.10f, 0.72f, 0.78f, 0.40f),
                        new Color(0.30f, 1.40f, 1.50f, 1.0f),
                        0.20f,
                        useFill: true,
                        useBoundary: true,
                        edgeGlowColor: new Color(0.35f, 1.50f, 1.60f, 1.0f),
                        edgeGlowWidth: 0.6f,
                        edgeFeather: 0.10f,
                        pulseSpeed: 1.8f,
                        pulseAlphaMin: 0.6f,
                        pulseAlphaMax: 1.0f,
                        fillRadiusScale: 1.0f);

                default:
                    return new CombatOverlayStyle(Color.clear, Color.white, 0.08f, false, true);
            }
        }
    }
}
