Shader "UI/MonsterHealthBar"
{
    // Procedural monster health bar for the world-space nameplate canvas. One quad draws the whole
    // bar from a pixel-space SDF (via _RectSize): rounded capsule frame, dark inset track, a
    // vertically-graded fill, and a pale "ghost" segment that trails behind the fill so damage reads
    // as a bite being taken out of the bar. Driven entirely from MonsterHealthBarView — the Image
    // needs no sprite; _FillRatio/_GhostRatio animate per frame in script.
    //
    // Rendering matches the other nameplate graphics: ZTest comes from unity_GUIZTestMode (set to
    // Always by the view, mirroring CreateOverlayUiMaterial) so the bar draws over the 3D model, and
    // output is premultiplied alpha so it composites on any backdrop. Vertex-colour alpha carries
    // CanvasGroup fades.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _RectSize ("Rect Size (px)", Vector) = (150, 12, 0, 0)
        _Radius ("Corner Radius (px)", Float) = 6
        _BorderWidth ("Border Width (px)", Float) = 1.4
        _FillRatio ("Fill Ratio", Range(0, 1)) = 1
        _GhostRatio ("Ghost Ratio", Range(0, 1)) = 1
        _FillColor ("Fill Color (top)", Color) = (0.35, 1.0, 0.45, 1)
        _FillColorDark ("Fill Color (bottom)", Color) = (0.10, 0.55, 0.16, 1)
        _GhostColor ("Ghost Color", Color) = (1.0, 0.42, 0.30, 0.9)
        _BackColor ("Track Color", Color) = (0.055, 0.045, 0.06, 0.92)
        _BorderColor ("Border Color", Color) = (0.62, 0.58, 0.52, 0.85)
        _EdgeGlow ("Leading Edge Glow", Float) = 0.55

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

            float4 _RectSize;
            float _Radius;
            float _BorderWidth;
            float _FillRatio;
            float _GhostRatio;
            fixed4 _FillColor;
            fixed4 _FillColorDark;
            fixed4 _GhostColor;
            fixed4 _BackColor;
            fixed4 _BorderColor;
            float _EdgeGlow;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color;
                return o;
            }

            // Signed distance to an axis-aligned rounded box centred at the origin.
            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Fragment position in the quad's pixel space, centred.
                float2 p = (i.texcoord - 0.5) * _RectSize.xy;
                float2 halfOuter = _RectSize.xy * 0.5 - 0.75;
                float radius = min(_Radius, min(halfOuter.x, halfOuter.y));
                float dOuter = sdRoundBox(p, halfOuter, radius);

                float border = max(_BorderWidth, 0.25);
                float2 halfInner = max(halfOuter - border, float2(0.5, 0.5));
                float dInner = sdRoundBox(p, halfInner, max(radius - border, 0.0));

                // ~1px analytic antialiasing on every edge.
                float outerMask = 1.0 - smoothstep(-0.75, 0.75, dOuter);
                float innerMask = 1.0 - smoothstep(-0.75, 0.75, dInner);
                float borderMask = saturate(outerMask - innerMask);

                // Fill / ghost right edges in pixel space, aligned to the inner track.
                float innerWidth = halfInner.x * 2.0;
                float fillEdge = -halfInner.x + innerWidth * saturate(_FillRatio);
                float ghostEdge = -halfInner.x + innerWidth * saturate(max(_GhostRatio, _FillRatio));
                float fillMask = innerMask * (1.0 - smoothstep(-0.6, 0.6, p.x - fillEdge)) * step(0.0005, _FillRatio);
                float ghostMask = saturate(innerMask * (1.0 - smoothstep(-0.6, 0.6, p.x - ghostEdge)) - fillMask);

                // Vertical gradient + a soft top gloss band so the fill reads as a lit capsule.
                float vertical = saturate((i.texcoord.y - 0.12) / 0.76);
                fixed3 fillColor = lerp(_FillColorDark.rgb, _FillColor.rgb, vertical);
                float gloss = smoothstep(0.55, 0.95, i.texcoord.y) * 0.16;
                fillColor += gloss;

                // Subtle bright seam at the fill's leading edge so drain motion is easy to track.
                float edgeGlow = exp2(-abs(p.x - fillEdge) * 0.9) * fillMask * _EdgeGlow;
                fillColor += edgeGlow;

                // Composite: track, then ghost, then fill, then frame. Weights sum via masks.
                fixed3 rgb = _BackColor.rgb * innerMask;
                float alpha = _BackColor.a * innerMask;

                rgb = lerp(rgb, _GhostColor.rgb, ghostMask * _GhostColor.a);
                alpha = max(alpha, ghostMask * _GhostColor.a);

                rgb = lerp(rgb, fillColor, fillMask);
                alpha = max(alpha, fillMask);

                rgb = lerp(rgb, _BorderColor.rgb, borderMask * _BorderColor.a);
                alpha = max(alpha, borderMask * _BorderColor.a);

                // Premultiplied-alpha output; vertex colour carries tint + CanvasGroup fade.
                float outAlpha = saturate(alpha * i.color.a);
                fixed4 col;
                col.rgb = rgb * i.color.rgb * outAlpha;
                col.a = outAlpha;
                return col;
            }
            ENDCG
        }
    }
}
