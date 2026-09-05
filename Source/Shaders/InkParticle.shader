// NOTE(공개 발췌): 이 파일은 서드파티 에셋 「Cartoon FX Remaster (JMO Assets)」의 프리팹·타입을 경로/이름으로만 참조한다. 해당 에셋은 이 리포에 포함되지 않는다.
// 먹+카툰 파티클 셰이더 — 서드파티 팩의 사실적/네온 룩을 우리 게임 톤으로 끌어오기 위한 것.
//
// 왜 필요한가: 팩 셰이더(CFXR 우버셰이더·NAMU 마스터셰이더)는 부드러운 그라디언트와 HDR 발광을
// 전제로 만들어져 있어, 색만 바꿔서는 "서울 야경 수묵"이라는 우리 톤에 묻지 않는다. 그렇다고
// 팩 셰이더를 고칠 수는 없다(패키지 갱신 시 소실 · 다른 큐 오염 · NAMU는 .shadergraph라 텍스트
// 편집이 비현실적 · CFXR는 .cfxrshader 자체 포맷). 그래서 <b>우리 셰이더를 쓰고 우리 프리팹의
// 머티리얼만 갈아 끼운다</b> — 프리팹 복제와 같은 원칙이고, ThirdParty는 참조만 남는다.
//
// 세 가지로 룩을 만든다:
//   ① 알파 포스터라이즈 — 부드러운 페이드를 계단으로 끊어 카툰 셀 느낌을 만든다.
//   ② 테두리 밴드 — 알파 경계에 별도 색을 넣어 먹선/네온 림이 된다(붓 테두리의 핵심).
//   ③ 먹 그레인 곱 — 종이/먹 번짐 텍스처를 곱해 균일한 발광을 부순다.
//
// 정점 색(파티클 startColor)을 그대로 곱하므로 기존 틴트 저작이 그대로 살아 있다.
Shader "SeoulPlayup/Ink Particle"
{
    Properties
    {
        _MainTex ("Particle Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _InkColor ("Ink Core", Color) = (0.03, 0.04, 0.08, 1)
        _InkBlend ("Ink Core Blend", Range(0,1)) = 0.35
        _InkTex ("Ink Grain", 2D) = "white" {}
        _InkStrength ("Ink Grain Strength", Range(0,1)) = 0.55
        // 🔑 <b>팩의 "손맛"은 텍스처에 그려져 있지 않다 — 런타임에 흐른다.</b> 사용자가 품질 기준으로
        // 지목한 NAMU `Slash_Ink`를 뜯어 보면 형상은 `_MaskTex`가 들고, 표면은 <b>순수 노이즈</b>
        // (noise11)를 메인으로 깔아 `_Scroll 0.28`로 흘리고 그 위에 서브마스크(Noise17)를
        // `_SubmaskScroll 0.75`로 <b>더 빠르게</b> 흘린다. 비백·농담이 획을 가로질러 이동하는 그 느낌은
        // 손으로 그린 결이 아니라 <b>두 속도로 흐르는 노이즈의 곱</b>이다.
        //
        // 그래서 "붓 질감은 코드로 못 만든다 → imagegen 텍스처가 필수"라는 전제는 틀렸다.
        // 필요한 것은 그림이 아니라 <b>움직임</b>이고, 그건 여기서 공짜로 나온다.
        // 기본값 0이라 켜지 않으면 기존 머티리얼과 완전히 같게 동작한다.
        _InkScroll ("Ink Grain Scroll (xy)", Vector) = (0,0,0,0)

        // 🔑 <b>boiling — 손그림이 "살아 보이는" 제1 요인 (R3 · 판정 Q38-ⓐ 확정).</b>
        // 카툰 이펙트는 실루엣 자체가 프레임마다 변한다(레퍼런스 실측: LoL 먹그림자·나인솔즈의
        // 먹 방울은 8~12fps로 계단 변형된다). 우리 파티클은 마스크 한 장이 수명 내내 같은 그림이라
        // "같은 스티커가 커지는" 인상이었다 — 그 갭을 여기서 메운다.
        //
        // 원리: 시간을 _BoilFps로 <b>양자화</b>해 그 계단마다 다른 시드로 절차 노이즈를 뜨고,
        // 그 노이즈로 <b>마스크 샘플 UV만</b> 비튼다. 계단 사이에는 그림이 완전히 정지하므로
        // "부드럽게 일렁이는" 것이 아니라 "손으로 다시 그린" 것으로 읽힌다 — 연속 시간으로 흔들면
        // 물결/아지랑이가 되지 흙 붓이 되지 않는다(애니 관례가 8~12fps인 이유).
        //
        // ⚠️ 훑기(sweep)의 진행도 축(u)은 비틀지 않는다 — 훑기 스케줄은 §15~§16에서 실측으로
        // 확정한 값이라 boiling이 타이밍을 건드리면 회귀다. 실루엣은 마스크 쪽에서만 끓는다.
        // <b>기본값 0이라 켜지 않으면 기존 머티리얼과 완전히 같게</b> 동작한다.
        _BoilFps ("Boil Steps Per Second", Range(0,30)) = 10
        _BoilStrength ("Boil UV Distortion", Range(0,0.2)) = 0
        _BoilScale ("Boil Noise Scale", Range(0.5,20)) = 6

        // 🔴🔴 <b>궤적 훑기(sweep) — 잔상의 정체.</b> 팩의 참격은 Trail 모듈을 쓰지 않는다.
        // 휘두른 궤적 모양의 <b>아크 리본 메시가 이미 거기 있고</b>, 디졸브가 그 리본을 <b>길이
        // 방향(UV의 U축)으로 그려 나갔다 지우는</b> 것이 잔상으로 읽힌다.
        //
        // 세 구간으로 나뉜다: 앞날이 들어오고(in) · 다 그려진 채 머물고(hold) · 꼬리가 지워진다(out).
        // 팩 기준선을 프레임으로 재면 정확히 이 모양이다 — 0.05에 들어와 0.12에 꽉 차고
        // 0.17부터 꼬리가 남는다(§14).
        //
        // ⚠️ 진행도는 <b>Custom Data(Custom1X) 스트림</b>으로 <c>TEXCOORD0.z</c>에 온다 —
        // 이 채널은 추측이 아니라 <c>ParticleSystemRenderer</c>가 표시하는 라벨에서 읽은 것이다
        // (appdata의 주석 참조). 스트림을 안 실어 보내는 시스템에서는 값이 없으므로
        // <c>_SweepEnable</c>로 통째로 가둔다.
        // <b>기본값 0이라 켜지 않으면 기존 머티리얼과 완전히 같게</b> 동작한다.
        _SweepEnable ("Sweep Enable", Range(0,1)) = 0
        _SweepIn ("Sweep In (age)", Range(0.01,1)) = 0.28
        _SweepHold ("Sweep Hold (age)", Range(0,1)) = 0.30
        _SweepOut ("Sweep Out (age)", Range(0.01,1)) = 0.42
        _SweepSoft ("Sweep Edge Softness", Range(0.001,0.5)) = 0.12
        // 진단 전용 — 출하 머티리얼은 0이다.
        // 0=끔 · 1=훑기 입력(R=호 U · G=수명 진행도) · 2=<b>생 채널 덤프</b>(R=TEXCOORD0.z ·
        // G=TEXCOORD0.w) · 3=메시 UV 점검(R=uv.x · G=uv.y) · 4=훑기 항(R=나이 · G=앞날 · B=꼬리).
        // 🔑 2번이 "채널을 추측하지 않기 위한" 도구다 — 후보를 한 장에 같이 찍어 놓고 고른다.
        // 판정 기준은 둘: <b>(a) 파티클 안에서 균일한가 (b) 프레임마다 커지는가.</b>
        // 둘 다 참인 채널만 진행도다(<c>.z</c>는 참, <c>.w</c>는 1.0 고정이라 거짓).
        _SweepDebug ("Sweep Debug View", Range(0,4)) = 0
        _Posterize ("Posterize Steps", Range(1,8)) = 3
        _EdgeWidth ("Edge Band Width", Range(0,0.6)) = 0.18
        _EdgeColor ("Edge Color", Color) = (1,1,1,1)
        _EdgeBoost ("Edge Boost", Range(0,4)) = 1.6
        // 아래 두 겹은 기본값 0이라 <b>켜지 않으면 기존 머티리얼과 완전히 같게</b> 동작한다.
        _RimWidth ("Outer Rim Width", Range(0,0.6)) = 0
        _RimColor ("Outer Rim Color", Color) = (1,1,1,1)
        _RimBoost ("Outer Rim Boost", Range(0,4)) = 2
        _CoreWidth ("Inner Core Width", Range(0,1)) = 0
        _CoreColor ("Inner Core Color", Color) = (1,1,1,1)
        _CoreBoost ("Inner Core Boost", Range(0,4)) = 1.6
        _AlphaCut ("Alpha Cutoff", Range(0,0.9)) = 0.04
        // 🔴🔴 <b>겹의 정체와 화면 불투명도가 같은 값에 묶여 있었다.</b> 4겹은 알파 값으로 갈리는데
        // (<c>banded</c>가 낮을수록 바깥 겹) 출력 알파도 같은 <c>banded</c>였다. 그래서 <b>바깥 겹일수록
        // 투명하다</b> — 8단에서 발광 림은 <b>12.5%</b>, 다크 키라인은 <b>25%</b> 불투명도로 찍힌다.
        // 가장 밝아야 할 겹이 가장 지워지는 구조라 <c>_RimBoost</c>를 아무리 올려도 상쇄된다
        // (실측: 팩 원본 p99 휘도 0.84 대 우리 0.31).
        //
        // 이 값은 <b>"어느 알파부터 불투명하게 볼 것인가"</b>다. 0.25로 두면 키라인 안쪽이 전부 불투명해지고
        // 실루엣 가장자리만 부드럽게 남는다. <b>기본값 1이면 옛 동작과 완전히 같다.</b>
        _OpaqueFrom ("Opaque From Alpha", Range(0.05,1)) = 1
        // 🔴 가산 전제로 만들어진 팩 텍스처는 알파 채널이 대개 쓸모없다(전면 1이거나 비어 있다).
        // 그런 텍스처로 알파 블렌딩을 켜면 쿼드 전체가 <b>불투명한 검은 판</b>으로 찍힌다(실측).
        // 이 값을 1로 두면 알파를 RGB 휘도에서 만들어 낸다 — 밝은 곳이 불투명, 검은 곳이 투명.
        _LumaAlpha ("Alpha From Luminance", Range(0,1)) = 0
        // 가산(5,1) / 알파(5,10) 를 머티리얼에서 고른다 — 원본이 어느 쪽이든 갈아 끼울 수 있게.
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.5
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                // .xy = 메시 UV · <b>.z = 수명 진행도(Custom1X 스트림)</b>.
                //
                // 🔑 <b>채널은 추측하지 말고 Unity가 표시하는 라벨을 읽을 것.</b>
                // <c>ParticleSystemRenderer</c>의 Vertex Streams 목록은 각 스트림 옆에 실제 채널을
                // 직접 써 준다. <c>EnableSweepStreams</c>가 거는 순서
                // [Position · Normal · Color · UV · Custom1X]에 대해 Unity가 내놓는 배치는
                // POSITION.xyz / NORMAL.xyz / COLOR.xyzw / <b>TEXCOORD0.xy</b> / <b>TEXCOORD0.z</b> 다.
                // (텍스처 좌표가 아닌 스트림은 자기 시맨틱으로 빠지고, 나머지는 TEXCOORD0부터
                //  순서대로 채워지되 한 스트림이 두 TEXCOORD에 걸치지는 않는다.)
                //
                // 🔴 <c>.w</c>를 읽으면 <b>항상 1.0</b>이다 — 채널이 실려 오지 않는 자리라 진행도가
                // 상수가 되고, 훑기 경계가 프레임마다 같은 데 서서 <b>여덟 프레임이 전부 같은 그림</b>
                // 또는 <b>직선 모서리 반투명 판</b>이 된다. 셋 다 같은 뿌리다(.z→Custom1X→.w로 세 번 헛짚었다).
                // 실측(<c>_SweepDebug=2</c>): sim 0.02/0.12/0.34/0.48에서
                // <c>.z</c> = 0.000 / 0.416 / 0.706 / 0.831 (파티클 안에서 min==max), <c>.w</c> = 1.0 고정.
                float4 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 inkUv : TEXCOORD1;
                float age : TEXCOORD2;
                float4 raw : TEXCOORD3;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _InkTex;
            float4 _InkTex_ST;
            fixed4 _Color;
            fixed4 _InkColor;
            fixed4 _EdgeColor;
            fixed4 _RimColor;
            fixed4 _CoreColor;
            half _RimWidth;
            half _RimBoost;
            half _CoreWidth;
            half _CoreBoost;
            half _InkBlend;
            half _InkStrength;
            half _Posterize;
            half _EdgeWidth;
            half _EdgeBoost;
            half _AlphaCut;
            half _LumaAlpha;
            half _OpaqueFrom;
            float4 _InkScroll;
            half _BoilFps;
            half _BoilStrength;
            half _BoilScale;
            // 🔴 벤치 전용 전역(Properties에 없음 — Shader.SetGlobalFloat로만 온다).
            //
            // <c>_Time.y</c>는 시뮬 시간이 아니라 <b>에디터 실시간</b>이다. 랩 스트립은 파티클을
            // 0→t로 재시뮬해 찍는데 시간 항(boiling·그레인 흐름)이 실시간을 읽으면 ①스트립의
            // 시간 축을 따라가지 않고 ②실행할 때마다 다른 그림이 나와 <b>A/B 픽셀 통계에 잡음이
            // 낀다</b>(실측: 아무것도 안 바꾼 팩 기준선 열이 6.8% 변했다). 랩이 패널마다 이 값을
            // 시뮬 시각으로 세팅하고 끝나면 0으로 되돌린다 — 0이면 게임과 똑같이 <c>_Time.y</c>다.
            float _VfxSimTime;
            half _SweepEnable;
            half _SweepIn;
            half _SweepHold;
            half _SweepOut;
            half _SweepSoft;
            half _SweepDebug;

            // boiling용 절차 노이즈 — 텍스처에 기대지 않는다(_InkTex는 흰색일 수 있는 슬롯이다).
            // sin 기반 해시는 half 정밀도에서 줄무늬가 지므로 float로 계산한다.
            float boilHash(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float boilNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = boilHash(cell);
                float b = boilHash(cell + float2(1.0, 0.0));
                float c = boilHash(cell + float2(0.0, 1.0));
                float d = boilHash(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv.xy, _MainTex);
                o.inkUv = TRANSFORM_TEX(v.uv.xy, _InkTex);
                o.age = v.uv.z;
                o.raw = float4(v.uv.z, v.uv.w, 0.0, 0.0);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 🔑 boiling — 마스크 샘플 UV만 비튼다(프로퍼티 블록의 설명 참조).
                //
                // 시드는 계단 인덱스마다 노이즈 격자를 통째로 밀어(큰 오프셋) 서로 무관한 장을
                // 만든다 — 같은 계단 안에서는 완전히 정지, 계단이 바뀌는 순간만 실루엣이 바뀐다.
                // ⚠️ 훑기의 u(i.uv.x 원본)와 그레인(inkUv)은 건드리지 않는다.
                float timeY = _VfxSimTime > 0.0001 ? _VfxSimTime : _Time.y;
                float2 maskUv = i.uv;
                if (_BoilStrength > 0.0001h)
                {
                    float stepT = _BoilFps > 0.01h ? floor(timeY * _BoilFps) / _BoilFps : timeY;
                    float2 seed = stepT * float2(61.7, 43.1);
                    float2 p = maskUv * _BoilScale + seed;
                    float nx = boilNoise(p);
                    float ny = boilNoise(p + float2(31.4, 17.9));
                    maskUv += (float2(nx, ny) - 0.5) * _BoilStrength;
                }

                fixed4 tex = tex2D(_MainTex, maskUv);
                // 정점 색 = 파티클 startColor. 기존 틴트 저작을 그대로 살린다.
                fixed4 tint = i.color * _Color;

                // 가산 텍스처는 알파가 무의미하므로 휘도에서 만들어 쓴다(_LumaAlpha 주석 참조).
                // min을 쓰는 이유: 팩 텍스처는 둘 중 하나다 — 알파가 전면 1이고 RGB가 형상을 들었거나,
                // 알파가 형상을 들고 RGB가 평평하거나. min이면 어느 쪽이든 형상을 든 채널이 이긴다.
                // (max나 단순 교체로 하면 나머지 한쪽이 통째로 불투명한 판이 된다 — 둘 다 실측했다.)
                half luma = max(tex.r, max(tex.g, tex.b));
                half srcAlpha = lerp(tex.a, min(tex.a, luma), _LumaAlpha);
                half alpha = srcAlpha * tint.a;

                // 🔴 궤적 훑기 — <b>알파를 깎기만 한다.</b> 겹 판정(banded)은 이 아래에서 하므로
                // 여기서 알파를 깎으면 <b>아직 안 그려진 구간이 통째로 사라지고</b>, 남은 구간의
                // 4겹 구조는 그대로 선다. 훑기를 겹 판정 뒤에 걸면 테두리만 남은 유령이 된다.
                if (_SweepEnable > 0.0001h)
                {
                    half u = i.uv.x;
                    half age = saturate(i.age);

                    // 앞날: 나이가 _SweepIn에 이를 때까지 u=0 → 1로 전진한다.
                    half head = saturate(age / _SweepIn);
                    // 꼬리: _SweepIn + _SweepHold를 지난 뒤부터 같은 방향으로 뒤따라온다.
                    half tail = saturate((age - _SweepIn - _SweepHold) / _SweepOut);

                    half soft = max(0.001h, _SweepSoft);

                    // 🔴 <b>경계를 뒤집어 넣지 말 것</b>(<c>smoothstep(head, head-soft, u)</c>).
                    // 역방향 경계는 정의가 모호해 아직 안 그려진 구간이 <b>0이 아니라 아주 작은 값</b>으로
                    // 남는데, 아래 <c>ceil</c> 계단화가 <b>0이 아닌 값을 전부 0.125로 끌어올린다</b> —
                    // 그러면 그 구간이 통째로 <b>발광 림 색의 반투명 판</b>으로 찍힌다(실측: 참격 뒤에
                    // 사각 조각이 붙어 다녔다). 정방향 + <c>1 -</c> 로 쓰면 바깥이 <b>정확히 0</b>이다.
                    half drawn = 1.0h - smoothstep(head - soft, head, u);  // 앞날 안쪽인가
                    half erased = smoothstep(tail - soft, tail, u);        // 꼬리 바깥인가
                    alpha *= saturate(drawn * erased);

                    // 🔑 소멸은 「균일 훑기」가 아니라 「지수 페이드」다 — R1 실측(기준표 §3.1):
                    // 레퍼런스 11컷 전부 페이드였고, 강도 반감 시점에 면적이 절반 남는 지수 곡선이다.
                    // 위치 지우기(erased)에 시간 감쇠를 겹쳐, 꼬리가 지워지는 동안 남은 몸통도
                    // 같이 옅어진다. 감마 2.0 = 실측 곡선의 근사. ⚠️pow 밑은 max로 가둔다(t=1 NaN 교훈).
                    half outStart = _SweepIn + _SweepHold;
                    if (age > outStart)
                    {
                        half fadeT = saturate((age - outStart) / max(0.001h, _SweepOut));
                        alpha *= pow(max(0.0h, 1.0h - fadeT), 2.0h);
                    }

                    if (_SweepDebug > 3.5h)
                    {
                        // 훑기 항 자체를 찍는다 — R=나이 · G=앞날(drawn) · B=꼬리(erased).
                        return fixed4(age, drawn, erased, 1.0h);
                    }
                }

                // 🔴🔴 <b>계단화 「전」에 생 알파로 자른다 — 순서가 반대면 사각형이 남는다.</b>
                //
                // 아래 <c>ceil</c>은 <b>0이 아닌 값을 전부 첫 계단(1/steps = 0.125)으로 끌어올린다.</b>
                // 그래서 알파가 0.001인 픽셀도 0.125가 되고, 맨 아래 <c>clip(banded - _AlphaCut)</c>은
                // 그 <b>부풀려진 값</b>을 보므로 <c>_AlphaCut</c>(0.04)을 가볍게 통과한다 →
                // <b>거의 투명해야 할 영역이 통째로 균일한 반투명 판</b>으로 찍힌다.
                //
                // 훑기가 «아직 안 그린» 구간이 정확히 그 꼴이라, 리본의 미그려진 부분이
                // <b>직선 모서리 사각형</b>으로 남아 획을 따라다녔다(실측: t=0.05에서 발자국의 73%가
                // 그렇게 남았다). §14.6이 «반투명 사각 조각»으로 적어 둔 것이 이것이고,
                // splash·홀더·메시·UV를 차례로 배제하고도 안 잡혔던 이유는 <b>그리는 주체가 아크 자신</b>이라서다.
                //
                // 🔑 생 알파로 먼저 자르면 «투명한 곳은 투명하게» 남고, 살아남은 픽셀만 계단이 된다.
                clip(alpha - _AlphaCut);

                // ① 계단식 알파 — 부드러운 페이드를 끊어 셀 느낌을 만든다.
                half steps = max(1.0h, floor(_Posterize + 0.5h));
                half banded = ceil(saturate(alpha) * steps) / steps;

                // ② 4겹 구조 — 바깥부터 [발광 림 · 다크 키라인 · 짙은 먹 몸통 · 네온 코어].
                //
                // 🔑 이 순서는 우리가 발명한 게 아니라 <b>출하된 글리프 표식이 이미 쓰는 해부</b>다
                // (status_annihilation: 골드 몸통 + 다크 키라인 + 바깥 오렌지 글로우). 그리고 2026-08-07
                // 판정 Q2-나·Q3-가·Q4-나·Q5-나를 한 번에 성립시키는 유일한 배치이기도 하다 —
                // "다크 키라인"(Q2)과 "발광하는 가장자리"(Q5)는 겹이 하나뿐이면 서로를 지운다.
                //
                // <c>t</c>는 실루엣 경계에서 0이고 안쪽으로 갈수록 커진다(마스크를 거리장으로 굽는 이유).
                half t = banded - _AlphaCut;
                half inside = step(_AlphaCut, banded);

                fixed3 body = lerp(tex.rgb * tint.rgb, _InkColor.rgb, _InkBlend);

                // ③ 먹 그레인 — 균일한 발광을 부순다.
                //
                // 🔴 그레인은 <b>몸통에만</b> 먹인다. 예전에는 4겹을 다 칠한 뒤 마지막에 전체를 곱했는데,
                // 그러면 발광 림과 다크 키라인까지 노이즈가 갉아먹어 <b>테두리가 우글거린다</b>.
                // 팩 원본에서 테두리가 또렷한 채로 표면만 흐르는 이유가 이 순서다 — 그래서 겹을
                // 올리기 <b>전에</b> 여기서 곱한다.
                //
                // 두 속도로 두 번 뜨는 이유: 한 겹만 흐르면 <b>텍스처가 미끄러지는 티</b>가 난다.
                // 팩이 메인 0.28 · 서브마스크 0.75로 갈라 놓은 것과 같은 장치이고, 텍스처 한 장으로
                // 흉내 낼 수 있다(속도·타일링이 다르면 두 장처럼 보인다).
                half2 flow = _InkScroll.xy * timeY;
                half grainA = tex2D(_InkTex, i.inkUv + flow).r;
                half grainB = tex2D(_InkTex, i.inkUv * 1.7h - flow * 2.7h).r;
                half grain = saturate(grainA * grainB * 2.0h);
                body *= lerp(1.0h, grain, _InkStrength);

                // ②-a 안쪽 네온 코어 — 획 한가운데가 가장 밝다(Q4: 네온은 먹 형태 <b>안쪽</b>에서만).
                half core = _CoreWidth > 0.0001h ? smoothstep(1.0h - _CoreWidth, 1.0h, banded) : 0.0h;
                fixed3 rgb = lerp(body, _CoreColor.rgb * _CoreBoost, core);

                // ②-b·c 다크 키라인과 그 바깥의 발광 림.
                //
                // 🔴 겹은 <b>부드러운 보간이 아니라 문턱으로</b> 가른다. 알파는 위에서 이미 계단으로
                // 끊겼으므로 겹 사이 경계도 이미 하드하고, 여기에 smoothstep을 쓰면 <b>바깥 겹이 안쪽 겹에
                // 먹혀 사라진다</b>(첫 렌더에서 발광 림이 통째로 실종됐다). 문턱이면 겹이 정확히 하나씩 잡힌다.
                //
                // <c>_RimWidth</c>가 0이면 옛 수식 그대로 간다 — 아직 재입힘하지 않은 머티리얼이
                // 이 변경으로 달라지면 안 되기 때문이다.
                half useBands = step(0.0001h, _RimWidth);
                half edgeSoft = 1.0h - smoothstep(0.0h, max(0.0001h, _EdgeWidth), t);
                half rim = step(t, _RimWidth) * useBands;
                half edgeHard = step(t, _RimWidth + _EdgeWidth) * (1.0h - rim);
                half edge = lerp(edgeSoft, edgeHard, useBands) * inside;

                rgb = lerp(rgb, _EdgeColor.rgb * _EdgeBoost, saturate(edge));
                rgb = lerp(rgb, _RimColor.rgb * _RimBoost, saturate(rim * inside));

                // 겹을 고르는 데 쓴 <c>banded</c>와, 화면에 얼마나 진하게 얹을지는 <b>다른 질문</b>이다.
                // 위쪽 겹 판정은 전부 <c>banded</c> 그대로 두고 여기서만 갈라 낸다.
                half outAlpha = saturate(banded / max(0.0001h, _OpaqueFrom));

                // 진단용 — 켜면 R=uv.x(호 진행도), G=파티클 나이, B=0으로 출력한다.
                // 훑기가 이상할 때 "무엇이 잘못 들어오는지"를 추측 대신 눈으로 본다.
                if (_SweepDebug > 0.5h)
                {
                    // R=uv.x(호 진행도) · G=수명 진행도. 훑기가 이상할 때 추측 대신 눈으로 본다.
                    // 🔑 G가 <b>파티클 안에서 균일</b>해야 정상이다 — 아크를 따라 그라데이션이면
                    // 진행도가 안 실려 오고 있다는 뜻이고, 그때 사각 조각이 생긴다(실측).
                    if (_SweepDebug > 2.5h)
                    {
                        // 메시 UV 점검 — 아크를 따라 R이 0→1로 흘러야 정상이다.
                        return fixed4(i.uv.x, i.uv.y, 0.0h, 1.0h);
                    }
                    if (_SweepDebug > 1.5h)
                    {
                        // 🔑 생 채널 덤프 — 후보를 <b>한 장에 같이</b> 찍는다. 진행도인 채널은
                        // 파티클 안에서 균일하면서 프레임마다 커진다. 나머지는 탈락이다.
                        return fixed4(saturate(i.raw.x), saturate(i.raw.y), saturate(i.raw.z), 1.0h);
                    }
                    return fixed4(i.uv.x, saturate(i.age), 0.0h, 1.0h);
                }

                clip(banded - _AlphaCut);
                return fixed4(rgb, outAlpha);
            }
            ENDCG
        }
    }

    Fallback "Universal Render Pipeline/Particles/Unlit"
}
