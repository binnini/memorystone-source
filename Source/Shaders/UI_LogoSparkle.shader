Shader "UI/LogoSparkle"
{
    // Dedicated shader for the lobby EffectLogo. Adds a per-fleck "gold dust" twinkle on
    // top of an additive UI sprite. NOT shared with any other UI so tuning here can never
    // affect cards/buttons. Additive (Blend One One), same UGUI setup as UI/AdditiveGlow.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // Per-fleck gold-dust twinkle. Each texture cell blinks on its own random phase so
        // the gold specks shimmer out of sync. Weighted by brightness + "goldness" so the
        // silvery smoke stays calm and only the warm flecks sparkle.
        _SparkleIntensity ("Sparkle Intensity", Float) = 1.5
        _SparkleSpeed ("Sparkle Speed", Float) = 3
        _SparkleDensity ("Sparkle Cell Density", Float) = 64
        _SparkleSharpness ("Sparkle Sharpness", Float) = 2.5
        _GoldBias ("Gold Bias (0=all bright,1=warm only)", Range(0,1)) = 0.7
        // Micro-shimmer: tiny per-region UV wobble so flecks waver in place (amp 0 = off).
        _ShimmerAmp ("Shimmer Amount", Float) = 0.0015
        _ShimmerSpeed ("Shimmer Speed", Float) = 2

        // UGUI mask support (no-op defaults; silences StencilMaterial.Add warnings under a Mask).
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
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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

        Blend One One
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _SparkleIntensity;
            float _SparkleSpeed;
            float _SparkleDensity;
            float _SparkleSharpness;
            float _GoldBias;
            float _ShimmerAmp;
            float _ShimmerSpeed;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            // Cheap per-cell hash -> [0,1). Gives each texture cell a stable random value.
            float hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;

                // Micro-shimmer: tiny per-region UV wobble so flecks appear to waver.
                float2 uv = i.texcoord;
                uv += _ShimmerAmp * float2(
                    sin(t * _ShimmerSpeed + i.texcoord.y * 40.0),
                    cos(t * _ShimmerSpeed * 1.3 + i.texcoord.x * 40.0));

                fixed4 tex = tex2D(_MainTex, uv);
                fixed4 col = tex * i.color;

                // Per-fleck gold-dust twinkle. Each UV cell gets a random phase so the gold
                // specks blink out of sync. Weight by brightness (only lit flecks) and by
                // "goldness" (warm chroma r-b) so the smoke stays calm and the dust sparkles.
                float2 cell = floor(i.texcoord * _SparkleDensity);
                float phase = hash21(cell) * 6.2831853;
                float blink = pow(0.5 + 0.5 * sin(t * _SparkleSpeed + phase), _SparkleSharpness);
                float lum = dot(tex.rgb, float3(0.299, 0.587, 0.114));
                float goldness = saturate(tex.r - tex.b);
                float fleck = lum * lerp(1.0, goldness, _GoldBias);
                col.rgb += _SparkleIntensity * blink * fleck * tex.rgb;

                return col;
            }
            ENDCG
        }
    }
}
