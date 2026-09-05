Shader "UI/ProceduralGlow"
{
    // Procedural rounded-rect glow for UGUI. Draws a uniform-thickness halo that hugs an inner rounded
    // rectangle and fades outward, computed per-fragment from a signed distance field. Because the SDF is
    // evaluated in pixel space (via _RectSize) the halo thickness stays uniform on any aspect ratio — unlike a
    // stretched bitmap glow, a wide button and a tall button both get an even outline. The inner edge is fixed
    // by _Pad, so increasing _Thickness fattens the halo strictly outward without shifting the glow's position.
    //
    // Driven entirely from script (TutorialUiGlow / CardHoverGlow): the Image needs no sprite. Set _RectSize to
    // the quad's pixel size, _Pad to the distance from the quad edge to the target's edge, and _Thickness for the
    // halo width. Additive output; fade via CanvasGroup alpha (arrives as vertex-colour alpha). Halo-only:
    // transparent inside the inner rect so button labels / card art are never washed.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Glow Color", Color) = (1, 0.5, 0.06, 1)
        _RectSize ("Rect Size (px)", Vector) = (100, 100, 0, 0)
        _Radius ("Corner Radius (px)", Float) = 12
        _Pad ("Edge Padding (px)", Float) = 22
        _Thickness ("Halo Thickness (px)", Float) = 16
        _Softness ("Inner Softness (px)", Float) = 3
        _Intensity ("Intensity", Float) = 1

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
        // Premultiplied-alpha (over) rather than additive, so the halo composites onto the background instead of
        // only adding light — it stays visible on bright/white backdrops (a skybox) as well as dark ones.
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

            fixed4 _Color;
            float4 _RectSize;
            float _Radius;
            float _Pad;
            float _Thickness;
            float _Softness;
            float _Intensity;

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
                // Fragment position in the quad's pixel space, centred.
                float2 p = (i.texcoord - 0.5) * _RectSize.xy;
                // Inner rectangle = the target (button/card): the quad shrunk by the padding on every side.
                float2 halfInner = max(_RectSize.xy * 0.5 - _Pad.xx, float2(0.001, 0.001));
                float radius = min(_Radius, min(halfInner.x, halfInner.y));
                float d = sdRoundBox(p, halfInner, radius);

                float thickness = max(_Thickness, 0.001);
                float softness = max(_Softness, 0.001);
                // outer: 1 at/inside the border, fading to 0 over _Thickness going outward.
                float outer = 1.0 - smoothstep(0.0, thickness, d);
                // inner: 0 deep inside, rising to 1 at the border — keeps the halo hugging the edge (halo-only).
                float inner = smoothstep(-softness, 0.0, d);
                float glow = saturate(outer * inner);

                // Premultiplied-alpha output: rgb is pre-multiplied by coverage so `Blend One OneMinusSrcAlpha`
                // composites the glow over any background. _Intensity mildly overdrives rgb on pulse peaks for a
                // subtle additive-glow accent. Alpha carries the CanvasGroup fade (i.color.a).
                float a = saturate(glow * _Color.a * i.color.a);
                fixed4 col;
                col.rgb = _Color.rgb * a * _Intensity;
                col.a = a;
                return col;
            }
            ENDCG
        }
    }
}
