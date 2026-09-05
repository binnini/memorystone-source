Shader "SeoulPlayup/Combat/Tile Overlay"
{
    Properties
    {
        [HDR] _FillColor("Fill Color", Color) = (0.08, 0.82, 0.95, 0.25)
        [HDR] _EdgeColor("Edge Glow Color (HDR)", Color) = (0.08, 0.82, 0.95, 0.0)
        _EdgeGlowWidth("Edge Glow Width", Range(0.0, 1.0)) = 0.6
        // Soft feathered outer edge (fraction of tile radius). 0 = hard polygon edge.
        _EdgeFeather("Edge Feather", Range(0.0, 0.9)) = 0.0

        // Procedural fill pattern driven by world XZ. 0 = Solid, 1 = Stripe, 2 = Dot, 3 = Crack.
        _PatternMode("Pattern Mode", Float) = 0
        _PatternScale("Pattern Scale", Float) = 3.0
        _PatternAngle("Pattern Angle (deg)", Float) = 45.0
        _PatternScroll("Pattern Scroll Speed", Float) = 0.0
        _PatternOpacity("Pattern Opacity", Range(0.0, 1.0)) = 0.0

        // Alpha "breathing". Speed 0 = static (uses _FillColor alpha unchanged).
        _PulseSpeed("Pulse Speed", Float) = 0.0
        _PulseAlphaMin("Pulse Alpha Min", Range(0.0, 1.0)) = 1.0
        _PulseAlphaMax("Pulse Alpha Max", Range(0.0, 1.0)) = 1.0

        // Marching-ants dashes along boundary UV.u (world length). DashLength 0 = solid line.
        _AntsSpeed("Ants Speed", Float) = 0.0
        _AntsDashLength("Ants Dash Length", Float) = 0.0

        // Global fade multiplier for show/hide transitions, animated by the renderer. 1 = fully shown.
        _Fade("Fade", Range(0.0, 1.0)) = 1.0

        // Depth test. 4 = LEqual (occluded by geometry in front, so buildings/actors stay opaque and
        // are NOT tinted by the overlay), 8 = Always (renders through everything). Tunable per material.
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4

        // Occluder fade (meaningful only with ZTest Always). Fragments hidden behind scene geometry
        // fade out with the *distance* between the occluder and the tile surface, in world units along
        // the view ray: a building standing on the tile itself (small distance) is drawn through, an
        // actor several cells in front of the tile (large distance) hides the overlay as if ZTest were
        // LEqual. End 0 = disabled (draw through everything).
        _OccluderFadeStart("Occluder Fade Start", Float) = 0.0
        _OccluderFadeEnd("Occluder Fade End", Float) = 0.0

        [HideInInspector] _SrcBlend("__src", Float) = 5.0
        [HideInInspector] _DstBlend("__dst", Float) = 10.0
        [HideInInspector] _ZWrite("__zw", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CombatTileOverlayUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 color : COLOR;
                float2 uv : TEXCOORD1;
                // Positive view-space depth of this fragment (distance along the camera forward axis).
                float viewDepth : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _FillColor;
                half4 _EdgeColor;
                float _EdgeGlowWidth;
                float _EdgeFeather;
                float _PatternMode;
                float _PatternScale;
                float _PatternAngle;
                float _PatternScroll;
                float _PatternOpacity;
                float _PulseSpeed;
                float _PulseAlphaMin;
                float _PulseAlphaMax;
                float _AntsSpeed;
                float _AntsDashLength;
                float _Fade;
                float _OccluderFadeStart;
                float _OccluderFadeEnd;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = input.color;
                output.uv = input.uv;
                output.viewDepth = -TransformWorldToView(positionWS).z;
                return output;
            }

            // Eye-space depth of whatever the opaque pass wrote at this pixel (perspective or ortho).
            float SceneEyeDepth(float4 positionCS)
            {
                float2 screenUV = GetNormalizedScreenSpaceUV(positionCS);
                float rawDepth = SampleSceneDepth(screenUV);
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                    rawDepth = 1.0 - rawDepth;
                    #endif
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
                }

                return LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            // Soft symmetric band from a repeating coordinate; 1 on the bar, 0 in the gap.
            float StripeMask(float coordinate)
            {
                float tri = abs(frac(coordinate) - 0.5) * 2.0;
                return smoothstep(0.35, 0.65, tri);
            }

            // Circular dots on a unit lattice; 1 inside the dot, 0 outside.
            float DotMask(float2 lattice)
            {
                float2 cell = frac(lattice) - 0.5;
                float d = length(cell);
                return 1.0 - smoothstep(0.22, 0.30, d);
            }

            float2 CrackHash(float2 cell)
            {
                float2 h = float2(dot(cell, float2(127.1, 311.7)), dot(cell, float2(269.5, 183.3)));
                return frac(sin(h) * 43758.5453);
            }

            // Cracked-earth network: a jittered Voronoi whose *cell borders* are the crack.
            // We keep the two nearest feature distances (F1, F2); the seam is where they are equal,
            // so (F2 - F1) is small exactly along the border and grows into the plate interiors.
            // Returns 1 on the crack line, 0 on the plate — the inverse of a "fill" mask, which is
            // what we want: the ground shows through and only the fractures are painted.
            float CrackMask(float2 lattice)
            {
                float2 baseCell = floor(lattice);
                float2 local = frac(lattice);
                float f1 = 8.0;
                float f2 = 8.0;
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 neighbor = float2(x, y);
                        float2 feature = neighbor + CrackHash(baseCell + neighbor) - local;
                        float d = dot(feature, feature);
                        if (d < f1)
                        {
                            f2 = f1;
                            f1 = d;
                        }
                        else if (d < f2)
                        {
                            f2 = d;
                        }
                    }
                }

                float seam = sqrt(f2) - sqrt(f1);
                // Two tiers so the fracture reads as broken ground rather than a hairline net:
                // a dark core line, plus a soft halo that shades the ground falling away into it.
                float core = 1.0 - smoothstep(0.10, 0.24, seam);
                float halo = 1.0 - smoothstep(0.0, 0.50, seam);
                return saturate(core + halo * 0.45);
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half3 rgb = _FillColor.rgb;
                half alpha = _FillColor.a;

                // --- Procedural pattern (world XZ, so it stays put as the camera pans) ---
                float ang = radians(_PatternAngle);
                float2 dir = float2(cos(ang), sin(ang));
                float2 worldXZ = input.positionWS.xz;
                float scroll = _Time.y * _PatternScroll;
                float patternMask = 1.0;
                if (_PatternMode > 2.5)
                {
                    patternMask = CrackMask(worldXZ * _PatternScale + scroll);
                }
                else if (_PatternMode > 1.5)
                {
                    patternMask = DotMask(worldXZ * _PatternScale + scroll);
                }
                else if (_PatternMode > 0.5)
                {
                    patternMask = StripeMask(dot(worldXZ, dir) * _PatternScale + scroll);
                }
                // Solid (mode 0) keeps patternMask = 1 → no modulation regardless of opacity.
                alpha *= lerp(1.0, patternMask, _PatternOpacity);

                // Distance from the tile's outer edge, from the radial hex field in vertex color .g
                // (0 at center, 1 at the perimeter). edge = 0 at the rim, growing inward.
                float edge = 1.0 - input.color.g;

                // --- Soft feathered outer edge (0 feather = hard polygon edge) ---
                float fillMask = _EdgeFeather > 1e-4 ? smoothstep(0.0, _EdgeFeather, edge) : 1.0;
                alpha *= fillMask;

                // --- Edge glow: smooth radial band hugging the perimeter, gated to the region's outer
                // boundary (vertex color .r) so interior tiles stay dark. Multiplying the band by the
                // feather mask fades the glow into the background at the silhouette instead of a hard cut.
                float glowBand = 1.0 - smoothstep(0.0, _EdgeGlowWidth, edge);
                float glow = glowBand * fillMask * input.color.r;
                rgb += _EdgeColor.rgb * glow;
                alpha = max(alpha, glow * _EdgeColor.a);

                // --- Alpha pulse (speed 0 → factor 1, i.e. static) ---
                half pulse = 1.0h;
                if (_PulseSpeed > 0.0)
                {
                    half wave = 0.5h + 0.5h * sin(_Time.y * _PulseSpeed);
                    pulse = lerp(_PulseAlphaMin, _PulseAlphaMax, wave);
                }
                alpha *= pulse;

                // --- Marching ants along boundary UV.u (dash length 0 → solid) ---
                if (_AntsDashLength > 0.0)
                {
                    float phase = frac(input.uv.x / _AntsDashLength - _Time.y * _AntsSpeed);
                    alpha *= step(0.5, phase);
                }

                // --- Occluder fade (ZTest Always only; end 0 = off). Positive = something is in front. ---
                if (_OccluderFadeEnd > 0.0)
                {
                    float occluderDistance = input.viewDepth - SceneEyeDepth(input.positionCS);
                    alpha *= 1.0 - smoothstep(_OccluderFadeStart, _OccluderFadeEnd, occluderDistance);
                }

                return half4(rgb, saturate(alpha) * _Fade);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
