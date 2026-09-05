Shader "UI/ProceduralPanel"
{
    // Procedural rounded-rect panel skin for UGUI popups/buttons (UI improvement P4, approach A). Sibling of
    // UI/ProceduralGlow: reuses the same pixel-space signed-distance rounded box, but where the glow is
    // halo-only (transparent inside), this shader fills the rect and rims it with a uniform border — a
    // resolution-independent replacement for a 9-slice panel sprite. Because the SDF is evaluated in pixel
    // space (via _RectSize) the corner radius and border thickness stay uniform on any aspect ratio.
    //
    // Driven from script (UiProceduralPanel): the Image needs no sprite; set _RectSize to the quad's pixel
    // size whenever the RectTransform resizes. Premultiplied-alpha (over) blending so the translucent fill
    // composites correctly on any backdrop. The vertex colour (Image.color / CanvasGroup fade) multiplies
    // both rgb and alpha, so UGUI Selectable ColorTint state transitions tint the whole skin for free.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _FillColor ("Fill Color", Color) = (0.106, 0.141, 0.219, 0.97)
        _BorderColor ("Border Color", Color) = (0.79, 0.65, 0.35, 0.9)
        _RectSize ("Rect Size (px)", Vector) = (100, 100, 0, 0)
        _Radius ("Corner Radius (px)", Float) = 18
        _BorderThickness ("Border Thickness (px)", Float) = 2
        _Softness ("Edge AA Softness (px)", Float) = 1.25

        // T1 texture pass (P6). Purely additive over the P4 flat look: every parameter below defaults to a
        // zero-effect value, so a material that only sets _FillColor/_BorderColor (every shipped one) renders
        // byte-identical to before. _TopLift shades the fill vertically (top lit, bottom shaded) to fake the
        // lobby plate's rounded depth; the highlight params draw a thin lit line just inside the top edge.
        _TopLift ("Top Gradient Lift", Range(0, 1)) = 0
        _HighlightColor ("Top Highlight Color", Color) = (1, 1, 1, 1)
        _HighlightStrength ("Top Highlight Strength", Range(0, 2)) = 0
        _HighlightWidth ("Top Highlight Width (px)", Float) = 1.5

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            fixed4 _FillColor;
            fixed4 _BorderColor;
            float4 _RectSize;
            float _Radius;
            float _BorderThickness;
            float _Softness;
            float _TopLift;
            fixed4 _HighlightColor;
            float _HighlightStrength;
            float _HighlightWidth;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color;
                return o;
            }

            // Signed distance to an axis-aligned rounded box centred at the origin.
            // p: sample point, b: half-size, r: corner radius. <0 inside, 0 on edge, >0 outside.
            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float aa = max(_Softness, 0.001);
                // Fragment position in the quad's pixel space, centred. The panel edge is inset by the AA
                // width so the outward half of the smoothed edge never clips against the quad boundary.
                float2 p = (i.texcoord - 0.5) * _RectSize.xy;
                float2 halfSize = max(_RectSize.xy * 0.5 - aa.xx, float2(0.001, 0.001));
                float radius = min(_Radius, min(halfSize.x, halfSize.y));
                float d = sdRoundBox(p, halfSize, radius);

                // coverage: 1 inside the panel, smoothed to 0 across the outer edge.
                float coverage = 1.0 - smoothstep(-aa, 0.0, d);
                // borderT: 0 in the fill interior, rising to 1 inside the border band that hugs the edge.
                float thickness = max(_BorderThickness, 0.0);
                float borderT = smoothstep(-thickness - aa, -thickness, d);

                // Vertical fill gradient: brighten the fill toward the top and darken toward the bottom to fake
                // the rounded plate depth. Ranges (1 - _TopLift) at the bottom to (1 + _TopLift) at the top;
                // _TopLift == 0 leaves the fill flat. The border keeps its uniform colour.
                fixed4 fill = _FillColor;
                float gradMul = 1.0 + _TopLift * (i.texcoord.y - 0.5) * 2.0;
                fill.rgb *= gradMul;

                fixed4 src = lerp(fill, _BorderColor, borderT);

                // Top highlight line: a thin lit rim hugging the inside of the top border, fading downward over
                // _HighlightWidth. Confined to the fill (1 - borderT) so it never bleeds into the border/corners;
                // coverage clips it to the rounded rect. _HighlightStrength == 0 disables it.
                float topInner = halfSize.y - thickness;
                float topLine = smoothstep(topInner - max(_HighlightWidth, 0.001), topInner, p.y);
                src.rgb += _HighlightColor.rgb * (topLine * (1.0 - borderT) * _HighlightStrength);

                // Premultiplied-alpha output; vertex colour carries Image.color tint + CanvasGroup fade.
                float a = saturate(src.a * coverage * i.color.a);
                fixed4 col;
                col.rgb = src.rgb * i.color.rgb * a;
                col.a = a;
                return col;
            }
            ENDCG
        }
    }
}
