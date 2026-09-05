using System.Collections.Generic;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Inspector-authored overlay theme. Holds a per-layer style table plus the overlay shader
    /// reference. Living under <c>Resources/</c> and referencing the shader by field keeps the shader
    /// in player builds automatically (no Always Included Shaders entry needed) and lets designers
    /// retune colors/alpha/speeds without a recompile.
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/Combat/Overlay Theme", fileName = "CombatOverlayTheme")]
    public sealed class CombatOverlayThemeAsset : ScriptableObject
    {
        [Tooltip("SeoulPlayup/Combat/Tile Overlay shader. Referenced here so it ships with the build.")]
        [SerializeField] private Shader overlayShader;

        [SerializeField] private List<LayerStyleEntry> layers = new List<LayerStyleEntry>();

        public Shader OverlayShader => overlayShader;

        public bool TryResolve(HexOverlayLayer layer, out CombatOverlayStyle style, out int renderPriority)
        {
            foreach (var entry in layers)
            {
                if (entry != null && entry.Layer == layer)
                {
                    style = entry.ToStyle();
                    renderPriority = entry.RenderPriority;
                    return true;
                }
            }

            style = default;
            renderPriority = 0;
            return false;
        }

        [System.Serializable]
        public sealed class LayerStyleEntry
        {
            [SerializeField] private HexOverlayLayer layer;
            [Tooltip("Render-queue offset above the base combat overlay queue. Higher draws on top.")]
            [SerializeField] private int renderPriority;

            [Header("Fill / Boundary")]
            [SerializeField] private Color fillColor = new Color(0.08f, 0.82f, 0.95f, 0.25f);
            [SerializeField] private Color boundaryColor = new Color(0.08f, 0.82f, 0.95f, 0.75f);
            [SerializeField] private float boundaryThickness = 0.08f;
            [SerializeField] private bool useFill = true;
            [SerializeField] private bool useBoundary = true;
            [Range(0.1f, 1f)][SerializeField] private float fillRadiusScale = CombatOverlayMeshBuilder.DefaultFillRadiusScale;

            [Header("Edge Glow")]
            [ColorUsage(true, true)][SerializeField] private Color edgeGlowColor = Color.clear;
            [Range(0f, 1f)][SerializeField] private float edgeGlowWidth = 0.6f;
            [Tooltip("Soft feathered outer edge (fraction of tile radius). 0 = hard edge. Keep 0 for merged/border-only layers.")]
            [Range(0f, 0.9f)][SerializeField] private float edgeFeather;

            [Header("Fill Pattern (0 Solid / 1 Stripe / 2 Dot)")]
            [SerializeField] private int fillPatternMode;
            [SerializeField] private float patternScale = 3f;
            [SerializeField] private float patternAngle = 45f;
            [SerializeField] private float patternScroll;
            [Range(0f, 1f)][SerializeField] private float patternOpacity;

            [Header("Alpha Pulse (speed 0 = static)")]
            [SerializeField] private float pulseSpeed;
            [Range(0f, 1f)][SerializeField] private float pulseAlphaMin = 1f;
            [Range(0f, 1f)][SerializeField] private float pulseAlphaMax = 1f;

            [Header("Marching Ants (dash length 0 = solid)")]
            [SerializeField] private float antsSpeed;
            [SerializeField] private float antsDashLength;

            [Header("Depth")]
            [Tooltip("건물·울타리 같은 장애물 위에도 그린다(ZTest Always). 결계처럼 장애물 칸 자체가 의미의 일부인 층에만 켤 것 — 이동·공격 범위에 켜면 갈 수 없는 칸이 갈 수 있어 보인다.")]
            [SerializeField] private bool drawOverObstacles;

            public HexOverlayLayer Layer => layer;
            public int RenderPriority => renderPriority;

            public CombatOverlayStyle ToStyle()
            {
                return new CombatOverlayStyle(
                    fillColor,
                    boundaryColor,
                    boundaryThickness,
                    useFill,
                    useBoundary,
                    CombatOverlayPattern.Solid,
                    edgeGlowColor,
                    edgeGlowWidth,
                    edgeFeather,
                    fillPatternMode,
                    patternScale,
                    patternAngle,
                    patternScroll,
                    patternOpacity,
                    pulseSpeed,
                    pulseAlphaMin,
                    pulseAlphaMax,
                    antsSpeed,
                    antsDashLength,
                    fillRadiusScale,
                    drawOverObstacles);
            }
        }
    }
}
