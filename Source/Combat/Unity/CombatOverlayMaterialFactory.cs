using SeoulPlayup.Map.Unity;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeoulPlayup.Combat.Unity
{
    internal static class CombatOverlayMaterialFactory
    {
        private const string OverlayShaderName = "SeoulPlayup/Combat/Tile Overlay";

        // Custom overlay shader properties.
        private static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        private static readonly int EdgeGlowWidthId = Shader.PropertyToID("_EdgeGlowWidth");
        private static readonly int EdgeFeatherId = Shader.PropertyToID("_EdgeFeather");
        private static readonly int PatternModeId = Shader.PropertyToID("_PatternMode");
        private static readonly int PatternScaleId = Shader.PropertyToID("_PatternScale");
        private static readonly int PatternAngleId = Shader.PropertyToID("_PatternAngle");
        private static readonly int PatternScrollId = Shader.PropertyToID("_PatternScroll");
        private static readonly int PatternOpacityId = Shader.PropertyToID("_PatternOpacity");
        private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
        private static readonly int PulseAlphaMinId = Shader.PropertyToID("_PulseAlphaMin");
        private static readonly int PulseAlphaMaxId = Shader.PropertyToID("_PulseAlphaMax");
        private static readonly int AntsSpeedId = Shader.PropertyToID("_AntsSpeed");
        private static readonly int AntsDashLengthId = Shader.PropertyToID("_AntsDashLength");

        // Legacy URP-Unlit fallback properties.
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int SurfacePropertyId = Shader.PropertyToID("_Surface");
        private static readonly int BlendPropertyId = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlendPropertyId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendPropertyId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWritePropertyId = Shader.PropertyToID("_ZWrite");
        private static readonly int ZTestPropertyId = Shader.PropertyToID("_ZTest");
        private static readonly int OccluderFadeStartId = Shader.PropertyToID("_OccluderFadeStart");
        private static readonly int OccluderFadeEndId = Shader.PropertyToID("_OccluderFadeEnd");

        /// <summary>
        /// 🔴 「장애물 위에 그린다」는 <b>가까운</b> 가림막에 한한다(2026-09-06 실플레이). ZTest Always만으로는
        /// 결계가 불가살·플레이어 몸까지 뚫고 나와 보스 하반신을 담장이 덮었다. 셰이더는 깊이 텍스처로
        /// 가림막과 타일 면 사이의 거리를 재서, 그 칸에 선 건물(≈1칸 안, 시선 방향 1.1 이내)은 뚫고
        /// 그리되 몇 칸 앞에 선 배우(2.5 이상)에는 LEqual처럼 가려진다. 그 사이는 선형으로 잦아든다.
        /// 기준선: 육각 반지름 1·카메라 피치 37.9°에서 같은 칸 건물의 앞면이 1.1 이내에 온다.
        /// </summary>
        private const float DrawOverObstaclesFadeStart = 1.0f;
        private const float DrawOverObstaclesFadeEnd = 2.5f;

        /// <summary>
        /// Prefer the theme-referenced overlay shader (guaranteed in build), then a name lookup, then
        /// the URP Unlit fallback so the overlay still renders in its legacy flat look if the custom
        /// shader is unavailable.
        /// </summary>
        public static Shader ResolveShader()
        {
            var themeShader = CombatOverlayTheme.Asset != null ? CombatOverlayTheme.Asset.OverlayShader : null;
            return themeShader ??
                   Shader.Find(OverlayShaderName) ??
                   Shader.Find("Universal Render Pipeline/Unlit") ??
                   Shader.Find("Universal Render Pipeline/Lit") ??
                   Shader.Find("Sprites/Default") ??
                   Shader.Find("Standard");
        }

        /// <summary>
        /// Legacy flat transparent material (URP Unlit) for simple colored geometry — monster move-path
        /// lines and intent arrows — that must NOT use the tile overlay shader (which needs the fill
        /// vertex-color / boundary UV channels). Preserves the original renderer's behavior.
        /// </summary>
        public static Material Create(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Universal Render Pipeline/Lit") ??
                         Shader.Find("Sprites/Default") ??
                         Shader.Find("Standard");
            var material = new Material(shader) { name = name };
            ApplyLegacyUnlitProperties(material, color);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = HexOverlayRenderOrder.CombatOverlayRenderQueue;
            return material;
        }

        public static Material CreateOverlayMaterial(string name, HexOverlayLayer layer, CombatOverlayStyle style, bool boundary)
        {
            var material = new Material(ResolveShader()) { name = name };
            Apply(material, layer, style, boundary);
            return material;
        }

        /// <summary>
        /// Re-applies a style to an existing cached material (no destroy/recreate). Handles both the
        /// custom overlay shader and the legacy URP-Unlit fallback.
        /// </summary>
        public static void Apply(Material material, HexOverlayLayer layer, CombatOverlayStyle style, bool boundary)
        {
            if (material == null)
            {
                return;
            }

            var color = boundary ? style.BoundaryColor : style.FillColor;

            if (material.HasProperty(FillColorId))
            {
                ApplyOverlayShaderProperties(material, style, boundary, color);
            }
            else
            {
                ApplyLegacyUnlitProperties(material, color);
            }

            // 장애물 위에 그릴지(2026-09-02). 셰이더가 이미 `_ZTest`를 노출하고 `ZTest [_ZTest]`로 쓰므로
            // 새 셰이더·새 큐 없이 이 한 값이 갈라 준다. 폴백(URP Unlit)에는 이 프로퍼티가 없으므로
            // HasProperty로 거른다 — 폴백에서는 예전처럼 가려지되, 그때는 색·무늬도 이미 다르다.
            if (material.HasProperty(ZTestPropertyId))
            {
                material.SetFloat(ZTestPropertyId,
                    (float)(style.DrawOverObstacles ? CompareFunction.Always : CompareFunction.LessEqual));
            }

            if (material.HasProperty(OccluderFadeEndId))
            {
                material.SetFloat(OccluderFadeStartId, style.DrawOverObstacles ? DrawOverObstaclesFadeStart : 0f);
                material.SetFloat(OccluderFadeEndId, style.DrawOverObstacles ? DrawOverObstaclesFadeEnd : 0f);
            }

            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = HexOverlayRenderOrder.CombatOverlayRenderQueue + CombatOverlayTheme.ResolveRenderPriority(layer);
        }

        private static void ApplyOverlayShaderProperties(Material material, CombatOverlayStyle style, bool boundary, Color color)
        {
            material.SetColor(FillColorId, color);

            // Edge glow reads the fill vertex-color channel; the boundary mesh is uniformly exposed, so
            // suppress the glow there and let the boundary color act as a clean line instead.
            material.SetColor(EdgeColorId, boundary ? Color.clear : style.EdgeGlowColor);
            material.SetFloat(EdgeGlowWidthId, boundary ? 0f : style.EdgeGlowWidth);

            // Feather softens the fill's outer silhouette; never applied to the boundary line (its
            // vertex color .g is uniformly 1, which would otherwise erase it).
            material.SetFloat(EdgeFeatherId, boundary ? 0f : style.EdgeFeather);

            // Procedural fill pattern only applies to the fill mesh.
            material.SetFloat(PatternModeId, boundary ? 0f : style.FillPatternMode);
            material.SetFloat(PatternScaleId, style.PatternScale);
            material.SetFloat(PatternAngleId, style.PatternAngle);
            material.SetFloat(PatternScrollId, style.PatternScroll);
            material.SetFloat(PatternOpacityId, boundary ? 0f : style.PatternOpacity);

            // Pulse applies to whichever part is drawn (usually the fill).
            material.SetFloat(PulseSpeedId, style.PulseSpeed);
            material.SetFloat(PulseAlphaMinId, style.PulseAlphaMin);
            material.SetFloat(PulseAlphaMaxId, style.PulseAlphaMax);

            // Marching ants ride the boundary UV.u; the fill has no dash channel.
            material.SetFloat(AntsSpeedId, boundary ? style.AntsSpeed : 0f);
            material.SetFloat(AntsDashLengthId, boundary ? style.AntsDashLength : 0f);
        }

        private static void ApplyLegacyUnlitProperties(Material material, Color color)
        {
            material.color = color;
            if (material.HasProperty(BaseColorPropertyId)) material.SetColor(BaseColorPropertyId, color);
            if (material.HasProperty(ColorPropertyId)) material.SetColor(ColorPropertyId, color);
            if (material.HasProperty(SurfacePropertyId)) material.SetFloat(SurfacePropertyId, 1f);
            if (material.HasProperty(BlendPropertyId)) material.SetFloat(BlendPropertyId, 0f);
            if (material.HasProperty(SrcBlendPropertyId)) material.SetFloat(SrcBlendPropertyId, (float)BlendMode.SrcAlpha);
            if (material.HasProperty(DstBlendPropertyId)) material.SetFloat(DstBlendPropertyId, (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty(ZWritePropertyId)) material.SetFloat(ZWritePropertyId, 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }
}
