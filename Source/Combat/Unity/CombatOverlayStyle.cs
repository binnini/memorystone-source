using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Presentation intent for a combat overlay layer. The batched-mesh renderer feeds these fields
    /// into the <c>SeoulPlayup/Combat/Tile Overlay</c> shader (edge glow, procedural pattern, alpha
    /// pulse, marching ants). Every visual number is a tunable so the final aesthetic is authored in
    /// the theme asset / material inspector rather than in code.
    /// </summary>
    public readonly struct CombatOverlayStyle
    {
        private readonly int fillPatternModeRaw;

        public CombatOverlayStyle(
            Color fillColor,
            Color boundaryColor,
            float boundaryThickness,
            bool useFill,
            bool useBoundary,
            CombatOverlayPattern pattern = CombatOverlayPattern.Solid,
            Color? edgeGlowColor = null,
            float edgeGlowWidth = 0.6f,
            float edgeFeather = 0f,
            int fillPatternMode = -1,
            float patternScale = 3f,
            float patternAngle = 45f,
            float patternScroll = 0f,
            float patternOpacity = 0f,
            float pulseSpeed = 0f,
            float pulseAlphaMin = 1f,
            float pulseAlphaMax = 1f,
            float antsSpeed = 0f,
            float antsDashLength = 0f,
            float fillRadiusScale = CombatOverlayMeshBuilder.DefaultFillRadiusScale,
            bool drawOverObstacles = false)
        {
            FillColor = fillColor;
            BoundaryColor = boundaryColor;
            BoundaryThickness = Mathf.Max(0f, boundaryThickness);
            UseFill = useFill;
            UseBoundary = useBoundary;
            Pattern = pattern;
            EdgeGlowColor = edgeGlowColor ?? Color.clear;
            EdgeGlowWidth = Mathf.Clamp01(edgeGlowWidth);
            EdgeFeather = Mathf.Clamp(edgeFeather, 0f, 0.9f);
            fillPatternModeRaw = fillPatternMode;
            PatternScale = patternScale;
            PatternAngle = patternAngle;
            PatternScroll = patternScroll;
            PatternOpacity = Mathf.Clamp01(patternOpacity);
            PulseSpeed = Mathf.Max(0f, pulseSpeed);
            PulseAlphaMin = Mathf.Clamp01(pulseAlphaMin);
            PulseAlphaMax = Mathf.Clamp01(pulseAlphaMax);
            AntsSpeed = antsSpeed;
            AntsDashLength = Mathf.Max(0f, antsDashLength);
            FillRadiusScale = Mathf.Clamp(fillRadiusScale, 0.1f, 1f);
            DrawOverObstacles = drawOverObstacles;
        }

        public Color FillColor { get; }
        public Color BoundaryColor { get; }
        public float BoundaryThickness { get; }
        public bool UseFill { get; }
        public bool UseBoundary { get; }
        public CombatOverlayPattern Pattern { get; }

        /// <summary>
        /// 이 층은 <b>깊이 검사를 하지 않는다</b> — 위에 건물·울타리 같은 장애물이 서 있어도 그 위에 그린다
        /// (2026-09-02 사용자 요구).
        ///
        /// <para>🔑 <b>왜 층별 축인가</b>: 전부에 걸면 이동·공격 범위 오버레이까지 건물을 뚫고 나와
        /// 「저 칸에 갈 수 있다」는 거짓말이 된다. 반대로 결계처럼 <b>장애물 칸 자체가 의미의 일부인</b>
        /// 층은 가려지면 정보가 사라진다 — Stage 1 아레나는 경계 30칸 중 10칸이 건물이라
        /// 담장의 3분의 1이 안 보였다.</para>
        ///
        /// <para>셰이더는 이미 <c>_ZTest</c>를 노출하고 있으므로(<c>CombatTileOverlay.shader</c>)
        /// 새 셰이더나 새 큐가 필요 없다 — 이 값이 <c>LessEqual</c>과 <c>Always</c>를 가른다.</para>
        ///
        /// <para>🔴 다만 <c>Always</c>는 배우까지 뚫는다(2026-09-06: 불가살 하반신이 결계에 덮였다). 그래서
        /// 이 값은 <b>가까운 가림막</b>(그 칸의 건물)만 뚫고, 몇 칸 앞에 선 배우에는 가려지도록 깊이
        /// 텍스처 기반 잦아듦도 함께 켠다 — 거리 기준은 <c>CombatOverlayMaterialFactory</c>에 있다.</para>
        /// </summary>
        public bool DrawOverObstacles { get; }

        public Color EdgeGlowColor { get; }
        public float EdgeGlowWidth { get; }

        /// <summary>Soft feathered outer edge width as a fraction of tile radius (0 = hard edge).</summary>
        public float EdgeFeather { get; }

        /// <summary>
        /// Shader fill pattern: 0 = Solid, 1 = Stripe, 2 = Dot. When left unset (constructed with the
        /// default -1) it is derived from the legacy <see cref="CombatOverlayPattern"/> enum, which is
        /// how that previously-dead enum is finally consumed: Dashed maps to a Stripe fill, everything
        /// else to Solid.
        /// </summary>
        public int FillPatternMode => fillPatternModeRaw >= 0
            ? fillPatternModeRaw
            : Pattern == CombatOverlayPattern.Dashed ? 1 : 0;

        public float PatternScale { get; }
        public float PatternAngle { get; }
        public float PatternScroll { get; }
        public float PatternOpacity { get; }

        public float PulseSpeed { get; }
        public float PulseAlphaMin { get; }
        public float PulseAlphaMax { get; }

        public float AntsSpeed { get; }
        public float AntsDashLength { get; }

        public float FillRadiusScale { get; }
    }
}
