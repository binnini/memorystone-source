// NOTE(공개 발췌): 이 파일은 서드파티 에셋 「Cartoon FX Remaster (JMO Assets)」의 프리팹·타입을 경로/이름으로만 참조한다. 해당 에셋은 이 리포에 포함되지 않는다.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SeoulPlayup.Combat.Unity.Dev;
using SeoulPlayup.EditorTools.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SeoulPlayup.EditorTools.Dev
{
    /// <summary>
    /// 스타일 <b>비교</b> 벤치 — 같은 연출을 여러 룩으로 구워 나란히 놓는다.
    ///
    /// <para>왜 <see cref="VfxLabCapture"/>와 따로 있는가: 저쪽은 "출하 상태가 어떻게 보이나"를
    /// 굽는 도구라 패턴당 한 장이다. 여기서 묻는 것은 <b>"어느 룩으로 갈 것인가"</b>이고,
    /// 그러려면 <b>한 가지만 다른</b> 그림이 여러 장 필요하다. 룩 결정이 끝나면 이 도구의 역할은
    /// 끝나고 결정된 값은 <see cref="MonsterAttackVfxBuilder"/>의 프리셋으로 옮겨 간다.</para>
    ///
    /// <para>두 벤치가 있고 <b>각각 다른 축</b>을 격리한다:</para>
    /// <list type="bullet">
    ///   <item><b>표면 벤치</b>(<see cref="CaptureSurfaceMatrix"/>) — 출하 프리팹을 그대로 두고
    ///   머티리얼만 갈아 끼운다. 형상·타이밍이 고정이므로 차이는 전부 표면에서 온다.
    ///   키라인·포스터라이즈·글로우·먹 코어 질문(Q2·Q3·Q4·Q5)이 여기 걸린다.</item>
    ///   <item><b>형태 벤치</b>(<see cref="CaptureShapeMatrix"/>) — 파티클 하나에 <b>절차적으로 구운
    ///   마스크</b>만 물린다. 텍스처를 생성하지 않고도 "형태를 코드로 어디까지 만들 수 있나"를
    ///   눈으로 확인하는 것이 목적이다(Q1).</item>
    /// </list>
    ///
    /// <para>🔴 <b>이 도구는 에셋을 만들지 않는다.</b> 머티리얼도 마스크도 메모리에만 두고 캡처 후
    /// 버린다. 룩이 확정되기 전에 <c>.mat</c>·<c>.png</c>가 리포지토리에 쌓이면 어느 것이 확정값인지
    /// 알 수 없게 되고, 실제로 갈래 1에서 프리팹 수치가 여기저기 흩어져 곤란을 겪었다.</para>
    ///
    /// <para>🔑 <b>InkParticle의 테두리 밴드는 알파 <i>경사</i>를 먹고 산다</b>(셰이더 frag의
    /// <c>edge = 1 - smoothstep(0, _EdgeWidth, banded - _AlphaCut)</c>). 알파가 0/1로 딱 떨어지는
    /// 하드 마스크를 물리면 <b>키라인이 아예 안 나온다.</b> 그래서 아래 마스크는 전부 실루엣
    /// 안쪽으로 들어가며 알파가 오르는 <b>거리장(distance field)</b>으로 굽는다 — 그래야
    /// <c>_EdgeWidth</c>가 "키라인 두께" 손잡이로 실제 동작한다.</para>
    /// </summary>
    public static class VfxStyleVariantLab
    {
        private const string InkShaderName = "SeoulPlayup/Ink Particle";
        private const string OutputFolder = "Temp/VfxStyleVariants";
        private const string FallbackScenePath = "Assets/Scenes/Dev/PrototypeTest.unity";

        /// <summary>출하 전투 씬(MainGameplay)이 실제로 쓰는 볼륨 프로파일.
        /// 블룸 문턱 0.75가 여기 들어 있고, 확정 사양의 네온 코어는 그 문턱을 넘으라고 만든 값이다.</summary>
        private const string ShippingVolumeProfile = "Assets/Art/Lookdev/ArtLookdevVolumeProfile.asset";

        // 프로젝트 팔레트 — 상태 아이콘·글리프 표식과 같은 값을 쓴다.
        private static readonly Color Keyline = Hex("#070A12");   // 글리프 표식의 다크 네이비 키라인
        private static readonly Color InkSoot = Hex("#0A0C14");   // 먹 코어
        private static readonly Color Mint = Hex("#4AE0C0");
        private static readonly Color PaperWhite = Hex("#E8F4FF");

        [MenuItem("Tools/Seoul Playup/Combat/Capture VFX Style Variants (A단계 비교)")]
        public static void CaptureAllMenu()
        {
            var report = CaptureAll(OutputFolder);
            Debug.Log(report);
        }

        /// <summary>두 벤치를 한 번에 굽는다. 에이전트가 <c>script-execute</c>로 부르는 진입점.</summary>
        public static string CaptureAll(string outputFolder)
        {
            outputFolder = string.IsNullOrWhiteSpace(outputFolder) ? OutputFolder : outputFolder;
            Directory.CreateDirectory(outputFolder);

            var previousScene = SceneManager.GetActiveScene().path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var shots = new List<Shot>();
            try
            {
                var stageGo = new GameObject("Style Variant Stage");
                var stage = stageGo.AddComponent<VfxLabStage>();
                stage.EnsureBoard();

                var camGo = new GameObject("Capture Camera");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Hex("#0B111F");
                cam.fieldOfView = 45f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;

                // 🔴🔴 <b>블룸이 없는 카메라로 HDR을 판정하면 안 된다.</b> 확정 사양의 네온 코어는
                // 밝기를 알파가 아니라 <b>HDR 값</b>으로 내고, 그 값이 그림이 되는 것은 <b>블룸이 받을 때</b>다.
                // 벤치가 맨 카메라면 HDR은 그냥 흰색으로 잘려 보이고 <b>번짐이 통째로 빠진다</b> —
                // 실기보다 못한 그림을 보며 "부족하다"고 판정하게 된다(실제로 한 번 밟았다).
                // 그래서 출하 전투 씬과 <b>같은 프로파일</b>을 이 씬에도 세운다.
                var stack = camGo.AddComponent<UniversalAdditionalCameraData>();
                stack.renderPostProcessing = true;
                cam.allowHDR = true;

                var volumeProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ShippingVolumeProfile);
                GameObject volumeGo = null;
                if (volumeProfile != null)
                {
                    volumeGo = new GameObject("Shipping Volume");
                    var volume = volumeGo.AddComponent<Volume>();
                    volume.isGlobal = true;
                    volume.priority = 1f;
                    volume.sharedProfile = volumeProfile;
                }
                else
                {
                    Debug.LogWarning("[VfxStyleVariantLab] 출하 볼륨 프로파일을 못 찾았다 — " +
                                     "블룸 없이 찍힌다. 경로 확인: " + ShippingVolumeProfile);
                }

                CaptureMotionStrips(cam, outputFolder, shots);
                CaptureReferenceMatrix(cam, outputFolder, shots);
                CaptureShippedMatrix(cam, outputFolder, shots);
                CaptureGrainCandidateMatrix(cam, outputFolder, shots);
                CaptureSpecMatrix(cam, outputFolder, shots);
                CaptureShapeMatrix(cam, outputFolder, shots);
                CaptureSurfaceMatrix(cam, outputFolder, shots);

                UnityEngine.Object.DestroyImmediate(camGo);
                if (volumeGo != null)
                {
                    UnityEngine.Object.DestroyImmediate(volumeGo);
                }

                UnityEngine.Object.DestroyImmediate(stageGo);
            }
            finally
            {
                // 🔴 이름 없는 씬으로는 돌아갈 수 없다 — 알려진 씬으로 복귀시킨다(VfxLabCapture와 같은 계약).
                var restoreTo = !string.IsNullOrEmpty(previousScene) && File.Exists(previousScene)
                    ? previousScene
                    : FallbackScenePath;
                if (File.Exists(restoreTo))
                {
                    EditorSceneManager.OpenScene(restoreTo, OpenSceneMode.Single);
                }
            }

            var jsonPath = Path.Combine(outputFolder, "report.json");
            File.WriteAllText(jsonPath, BuildJson(shots), new UTF8Encoding(false));
            return $"VfxStyleVariantLab: {shots.Count}장 → {outputFolder} (report.json 포함)";
        }

        // ─────────────────────────────────────────────────────────────────────
        // 움직임 벤치 — 🔴 지금까지 판정 축이 아예 없던 자리
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 한 큐를 <b>시각을 옮겨 가며 여러 장</b> 찍어 가로로 이어 붙인다(컨택트 시트).
        ///
        /// <para>🔴🔴 <b>왜 필요한가.</b> 이 벤치의 다른 열은 전부 <b>정지 컷 한 장</b>이다. 그래서
        /// "휘두르는 방향을 따라 획이 그려지는가 · 지나간 자리에 잔상이 남는가 · 겹이 시간차로
        /// 터지는가" 같은 <b>시간 축의 품질을 물을 방법이 아예 없었다.</b> 실제로 그 결함을 도구가
        /// 잡지 못했고 사람이 짚어 줘야 했다 — A단계 벤치에 팩 원본 열이 없어 품질 착각이 생긴 것과
        /// <b>같은 종류의 실수</b>다(§11.3). 판정 축이 없는 벤치는 통과를 증명하지 못한다.</para>
        ///
        /// <para>🔴 <b>난수 시드를 고정하지 않으면 이 도구는 무의미하다.</b> 프레임마다
        /// <c>Simulate</c>를 0부터 다시 돌리므로, 시드가 자동이면 <b>매 프레임이 다른 추첨</b>이 되어
        /// 파편이 프레임마다 딴 곳에 있다 — 움직임이 아니라 노이즈가 찍힌다. 그래서 모든 시스템의
        /// <c>useAutoRandomSeed</c>를 끄고 같은 시드를 박는다.</para>
        ///
        /// <para>⚠️ 누적 시뮬레이션(<c>Simulate(dt, ..., restart:false)</c>)이 아니라 <b>매번 0에서
        /// t까지</b> 다시 돌린다. 누적은 에디터에서 프레임 간격이 들쭉날쭉해 재현되지 않는다.</para>
        /// </summary>
        private static void CaptureMotionStrips(Camera cam, string outputFolder, List<Shot> shots)
        {
            // 팩 원본이 <b>먼저</b> 온다 — 비교 대상이 없으면 우리 것만 보고 "이 정도면 됐다"가 된다.
            const string namu = "Assets/ThirdParty/NamuFX/Simple Stylized Slash vol2/Prefabs/";
            const string cfxr = "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/";

            // ⚠️ 높이는 <b>움직임이 보이도록</b> 조인다 — 정지 컷 벤치보다 가깝게 붙는다.
            // 멀리서 찍으면 획이 몇 픽셀이라 "자라나는가"를 눈으로 못 가른다(첫 판에서 실측).
            var subjects = new (string Id, string Label, string Path, float Height)[]
            {
                ("ref_a001", "🎯 A001 물기 — 충격파 기준(링 + 스파이크 + 0.05s 지연 버스트)",
                    cfxr + "Impacts/CFXR Hit D 3D (Yellow).prefab", 4f),
                ("ref_slashink", "🎯 A008·A009 먹 참격 기준(아크 리본 메시 + 디졸브)",
                    namu + "Slash_Ink.prefab", 4.5f),
                ("clawslash", "A003·A004 우리 참격", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_ClawSlash.prefab", 4.5f),
                ("roarripple", "A029 우리 파문", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_RoarRipple.prefab", 4f),
                ("fissuretile", "A006 우리 균열", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_FissureTile.prefab", 4f),
                // §17 ④ 범위인데 <b>움직임 벤치에 열이 없었다</b> — 판정 축이 없으면 통과를 증명하지 못한다(§14.1).
                ("artillerytile", "A022 우리 착탄", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_ArtilleryTile.prefab", 4f),
                ("heavypunch", "A014 우리 강펀치", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_HeavyPunch.prefab", 6f),
            };

            // 앞을 촘촘히 — 어택이 앞 절반에 몰려 있다. 뒤를 촘촘히 깔면 빈 판만 늘어난다.
            //
            // 🔴 <b>수명을 바꾸면 이 배열도 같이 바꿔야 한다.</b> Q42-가로 아크 수명이
            // 0.28s → <b>0.50s</b>가 됐다(R1 기준표 §3.1 — 레퍼런스 총 가시 길이 0.52s).
            // 옛 배열(~0.28s)로 찍으면 <b>꼬리 지워짐 구간이 통째로 안 담긴다</b>.
            // ⚠️ 팩 기준선은 1.0s/0.7s라 이 배열에서도 <b>끝까지 안 간다</b> — 기준선 행은
            // 「들어옴 → 자람」까지만 읽고, 꼬리 판정은 우리 행에서 할 것.
            var times = new[] { 0.02f, 0.07f, 0.13f, 0.20f, 0.27f, 0.34f, 0.42f, 0.50f };

            // 한 줄 8칸은 너무 납작해 웹에서 칸이 뭉갠다 — 4×2 격자로 접는다.
            const int cols = 4;
            const int frameWidth = 420;
            const int frameHeight = 280;
            const int gutter = 3;
            var rows = (times.Length + cols - 1) / cols;
            var sheetWidth = cols * frameWidth + (cols - 1) * gutter;
            var sheetHeight = rows * frameHeight + (rows - 1) * gutter;

            foreach (var subject in subjects)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(subject.Path);
                if (prefab == null)
                {
                    shots.Add(new Shot
                    {
                        Bench = "motion", Id = subject.Id, Subject = subject.Label,
                        Treatment = "프리팹을 못 찾았다", Image = string.Empty, Error = subject.Path,
                    });
                    continue;
                }

                var strip = new Texture2D(sheetWidth, sheetHeight, TextureFormat.RGB24, false);

                // 칸 사이 홈을 눈에 보이게 — 안 그러면 배경이 균일해서 어디서 프레임이 갈리는지 모른다.
                var gutterFill = new Color[sheetWidth * sheetHeight];
                var gutterColor = Hex("#26324C");
                for (var i = 0; i < gutterFill.Length; i++)
                {
                    gutterFill[i] = gutterColor;
                }

                strip.SetPixels(gutterFill);

                for (var f = 0; f < times.Length; f++)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, 0f), Quaternion.identity);

                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        // 🔴 시드 고정 — 프레임끼리 이어져 보이게 하는 유일한 장치다.
                        ps.useAutoRandomSeed = false;
                        ps.randomSeed = 0x5EED;
                    }

                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        if (ps.transform.parent != null &&
                            ps.transform.parent.GetComponentInParent<ParticleSystem>() != null)
                        {
                            continue;
                        }

                        ps.Simulate(times[f], true, true);
                    }

                    // 🔴 셰이더의 시간 항(boiling·그레인 흐름)을 스트립의 시간 축에 태운다.
                    // _Time.y는 에디터 실시간이라 이 값 없이는 여덟 칸이 같은 boiling 시드로 찍히고,
                    // 실행 간 A/B 픽셀 통계에 잡음이 낀다(팩 기준선 열이 6.8% 흔들리는 실측).
                    Shader.SetGlobalFloat("_VfxSimTime", times[f]);

                    var frame = RenderToTexture(cam, frameWidth, frameHeight, subject.Height, Vector3.zero);

                    // ⚠️ Texture2D의 원점은 <b>좌하단</b>이다 — 첫 줄을 위에 놓으려면 y를 뒤집는다.
                    var col = f % cols;
                    var row = f / cols;
                    var x = col * (frameWidth + gutter);
                    var y = (rows - 1 - row) * (frameHeight + gutter);
                    strip.SetPixels(x, y, frameWidth, frameHeight, frame.GetPixels());

                    UnityEngine.Object.DestroyImmediate(frame);
                    UnityEngine.Object.DestroyImmediate(go);
                }

                strip.Apply(false, false);
                var file = $"motion_{subject.Id}.png";
                File.WriteAllBytes(Path.Combine(outputFolder, file), strip.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(strip);

                shots.Add(new Shot
                {
                    Bench = "motion", Id = subject.Id, Subject = subject.Label,
                    Treatment = "t = " + string.Join(" / ", times) + " (초, 왼쪽부터)",
                    Image = Path.Combine(outputFolder, file),
                });
            }

            // 0 = 게임과 같은 _Time.y로 복귀. 되돌리지 않으면 이후 벤치·씬 뷰가 이 시각에 얼어붙는다.
            Shader.SetGlobalFloat("_VfxSimTime", 0f);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 기준선 벤치 — 팩 원본. 🔴 A단계 벤치가 빠뜨렸던 열이다
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 🔴🔴 <b>이 열이 없어서 C단계가 품질 미달로 갔다.</b> A단계 형태 벤치는 절차 마스크끼리만
        /// 비교했다(등폭 리본 vs 붓형 vs 파편). <b>팩 원본과 나란히 놓은 칸이 하나도 없었으므로</b>
        /// "코드로 붓처럼 읽히는 형태를 만들 수 있다"가 <b>"코드로 만든 것이 팩만큼 좋다"</b>로 조용히
        /// 승격됐고, 그 위에서 「B단계(질감) 불필요」 결론이 나왔다.
        ///
        /// <para>기준선은 사용자가 직접 지목한 것이다 — <b>A008·A009·A010</b>이 쓰는 큐 V008~V010의
        /// 프리팹 <c>Slash_Ink</c>는 <b>우리가 한 번도 손대지 않은 팩 원본</b>이고, 동시에
        /// <b>A003·A004가 개조 전에 쓰던 바로 그 프리팹</b>이다. 그래서 같은 참격 하나를 두고
        /// 「원본 / 재입힘」을 직접 겹쳐 볼 수 있다.</para>
        ///
        /// <para>⚠️ <b>비교가 성립하려면 촬영 조건이 같아야 한다</b> — 높이·시뮬레이션 시각을
        /// <see cref="CaptureShippedMatrix"/>의 같은 <c>Id</c> 행과 반드시 일치시킬 것. 다른 높이에서
        /// 찍은 두 장을 나란히 놓으면 크기 차이가 품질 차이로 오독된다.</para>
        /// </summary>
        private static void CaptureReferenceMatrix(Camera cam, string outputFolder, List<Shot> shots)
        {
            const string namu = "Assets/ThirdParty/NamuFX/Simple Stylized Slash vol2/Prefabs/";
            const string cfxr = "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/";
            const string hovl = "Assets/ThirdParty/Hovl Studio/Magic effects pack/Prefabs/";

            // Id는 shipped 열과 짝이 되도록 맞춘다(리포트에서 같은 Id 두 장을 나란히 놓는다).
            // qualitybar = 사용자가 "이 정도를 목표로 하고 싶다"고 지목한 무개조 원본.
            var subjects = new (string Id, string Label, string Path, float Height, float Time)[]
            {
                ("qualitybar", "🎯 A008·A009·A010 기준선 — Slash_Ink 무개조", namu + "Slash_Ink.prefab", 9f, 0.25f),
                ("clawslash", "A003·A004 발톱 참격", namu + "Slash_Ink.prefab", 9f, 0.25f),
                ("firebreath", "A007 화염 브레스", cfxr + "Fire/CFXR Fire Breath.prefab", 11f, 0.45f),
                ("firebreathlong", "A020 화염 방사·장", cfxr + "Fire/CFXR Fire Breath.prefab", 14f, 0.5f),
                ("pressurecross", "A005 위압 십자", hovl + "AoE effects/AoE slash blue.prefab", 11f, 0.2f),
                ("fissuretile", "A006 균열 모듈", cfxr + "Impacts/CFXR2 Ground Hit.prefab", 4.5f, 0.2f),
                ("fissureringtile", "A018 균열 모듈 · 여백판", cfxr + "Impacts/CFXR2 Ground Hit.prefab", 4.5f, 0.2f),
                ("artillerytile", "A022 착탄 모듈", cfxr + "Explosions/CFXR Explosion 1.prefab", 4.5f, 0.25f),
                ("roarripple", "A029 포효 파문", cfxr + "Impacts/CFXR Impact Glowing HDR (Blue).prefab", 6f, 0.4f),
                ("horncharge", "A015 뿔 돌진", hovl + "Smoke effects/Dust ground.prefab", 11f, 0.3f),
                ("heavypunch", "A014 강펀치", cfxr + "Impacts/CFXR Hit A (Red).prefab", 11f, 0.25f),
            };

            foreach (var subject in subjects)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(subject.Path);
                if (prefab == null)
                {
                    shots.Add(new Shot
                    {
                        Bench = "reference", Id = subject.Id, Subject = subject.Label,
                        Treatment = "팩 원본을 못 찾았다", Image = string.Empty, Error = subject.Path,
                    });
                    continue;
                }

                // 🔴 팩 원본은 <b>절대 만지지 않는다</b> — 인스턴스만 세우고 시뮬레이션 후 버린다.
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, 0f), Quaternion.identity);

                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps.transform.parent != null &&
                        ps.transform.parent.GetComponentInParent<ParticleSystem>() != null)
                    {
                        continue;
                    }

                    ps.Simulate(subject.Time, true, true);
                }

                var file = $"reference_{subject.Id}.png";
                Render(cam, Path.Combine(outputFolder, file), subject.Height, Vector3.zero);
                shots.Add(new Shot
                {
                    Bench = "reference", Id = subject.Id, Subject = subject.Label,
                    Treatment = "팩 원본 무개조 (개조 전에 쓰던 그것)",
                    Image = Path.Combine(outputFolder, file),
                });

                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 출하 상태 벤치 — C단계 재입힘이 실제로 프리팹에 들어갔는지 본다
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 🔴 <b>다른 두 벤치로는 재입힘을 검증할 수 없다.</b> 저것들은 렌더러의 머티리얼을 자기
        /// 것으로 <b>갈아 끼운 뒤</b> 찍기 때문에(그게 "한 가지만 다른 그림"을 만드는 방법이다),
        /// 프리팹에 무엇이 저장돼 있든 같은 그림이 나온다 — C단계에서 프리팹을 고쳐 놓고 저 벤치를
        /// 보면 <b>고치기 전과 똑같은 그림을 보며 통과했다고 믿게 된다.</b> 여기서는 인스턴스화만
        /// 하고 <b>아무것도 만지지 않는다.</b>
        /// </summary>
        private static void CaptureShippedMatrix(Camera cam, string outputFolder, List<Shot> shots)
        {
            // 높이는 규격마다 다르다 — 한 칸 모듈(1.7u)과 4칸까지 뻗는 콘을 같은 거리에서 찍으면
            // 한쪽은 점이고 한쪽은 화면 밖이다.
            var subjects = new (string Id, string Label, string File, float Height, float Time)[]
            {
                ("clawslash", "A003·A004 발톱 참격", "MonsterAttack_ClawSlash", 9f, 0.25f),
                ("firebreath", "A007 화염 브레스", "MonsterAttack_FireBreath", 11f, 0.45f),
                ("firebreathlong", "A020 화염 방사·장", "MonsterAttack_FireBreathLong", 14f, 0.5f),
                ("pressurecross", "A005 위압 십자", "MonsterAttack_PressureCross", 11f, 0.2f),
                ("fissuretile", "A006 균열 모듈", "MonsterAttack_FissureTile", 4.5f, 0.2f),
                ("fissureringtile", "A018 균열 모듈 · 여백판", "MonsterAttack_FissureRingTile", 4.5f, 0.2f),
                ("artillerytile", "A022 착탄 모듈", "MonsterAttack_ArtilleryTile", 4.5f, 0.25f),
                ("roarripple", "A029 포효 파문 · 재제작", "MonsterAttack_RoarRipple", 6f, 0.4f),
                ("horncharge", "A015 뿔 돌진", "MonsterAttack_HornCharge", 11f, 0.3f),
                ("heavypunch", "A014 강펀치", "MonsterAttack_HeavyPunch", 11f, 0.25f),
            };

            foreach (var subject in subjects)
            {
                var path = "Assets/Prefabs/Vfx/Combat/" + subject.File + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    shots.Add(new Shot
                    {
                        Bench = "shipped", Id = subject.Id, Subject = subject.Label,
                        Treatment = "프리팹을 못 찾았다", Image = string.Empty, Error = path,
                    });
                    continue;
                }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, 0f), Quaternion.identity);

                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps.transform.parent != null &&
                        ps.transform.parent.GetComponentInParent<ParticleSystem>() != null)
                    {
                        continue;
                    }

                    ps.Simulate(subject.Time, true, true);
                }

                var file = $"shipped_{subject.Id}.png";
                Render(cam, Path.Combine(outputFolder, file), subject.Height, Vector3.zero);
                shots.Add(new Shot
                {
                    Bench = "shipped", Id = subject.Id, Subject = subject.Label,
                    Treatment = "출하 프리팹 그대로 (C단계 재입힘 결과)",
                    Image = Path.Combine(outputFolder, file),
                });

                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Q22-나 후보 벤치 — 출하(가) 위에 가산 발광 겹을 한 장 더 얹는다
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 출하 프리팹을 그대로 세운 뒤 <b>가산 발광 겹만</b> 한 장 더 얹는다 — 형상·타이밍·수명 램프가
        /// 모두 고정이라 차이는 전부 "가산을 더했는가"에서 온다.
        ///
        /// <para>왜 이 열이 필요한가: <b>Q22-가는 코어만 빛나게 한다.</b> 발광 림은 Q20-다로 12.5%
        /// 불투명도에 남아 있어서 HDR을 실어도 블룸 문턱(0.75)에 닿지 못한다(넘기려면 HDR이 6을 넘어야 한다).
        /// 코어만으로 번짐이 충분한지, 아니면 가산 겹까지 필요한지 — 그 판정을 이 열이 한다.</para>
        ///
        /// <para>🔴 <b>에셋을 만들지 않는다.</b> 머티리얼은 인스턴스 사본이고 발광 겹도 임시
        /// 인스턴스라 캡처 후 사라진다 — 판정 전에 <c>.mat</c>가 리포지토리에 들어가면 어느 것이
        /// 확정값인지 알 수 없게 된다.</para>
        /// </summary>
        private static void CaptureGrainCandidateMatrix(Camera cam, string outputFolder, List<Shot> shots)
        {
            var subjects = new (string Id, string Label, string File, float Height, float Time)[]
            {
                ("clawslash", "A003·A004 발톱 참격", "MonsterAttack_ClawSlash", 9f, 0.25f),
                ("firebreath", "A007 화염 브레스", "MonsterAttack_FireBreath", 11f, 0.45f),
                ("fissuretile", "A006 균열 모듈", "MonsterAttack_FissureTile", 4.5f, 0.2f),
                ("artillerytile", "A022 착탄 모듈", "MonsterAttack_ArtilleryTile", 4.5f, 0.25f),
                ("roarripple", "A029 포효 파문", "MonsterAttack_RoarRipple", 6f, 0.4f),
                ("heavypunch", "A014 강펀치", "MonsterAttack_HeavyPunch", 11f, 0.25f),
            };

            foreach (var subject in subjects)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Vfx/Combat/" + subject.File + ".prefab");
                if (prefab == null)
                {
                    continue;
                }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, 0f), Quaternion.identity);

                // 🔑 <b>Q22-나를 흉내 내는 방법 = 같은 프리팹을 두 번 세운다.</b> 팩이 하는 일이
                // 정확히 그것이다(<c>Slash_Ink</c>는 <c>M_InkSlash_Alp</c>와 <c>M_InkSlash_Add</c>를
                // 같은 프리팹 안에 나란히 둔다). 아래 사본은 <b>가산 발광 전용</b>이라 키라인·먹을
                // 버리고 형상만 남긴다 — 같은 마스크를 쓰므로 <b>형태를 따라가는</b> 발광이고,
                // Q3-가가 기각한 "형체 없는 소프트 방사 글로우"와는 다르다.
                var glow = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                glow.transform.SetPositionAndRotation(new Vector3(0f, 0.06f, 0f), Quaternion.identity);

                foreach (var renderer in glow.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    var source = renderer.sharedMaterial;
                    if (source == null || source.shader == null || source.shader.name != InkShaderName)
                    {
                        continue;
                    }

                    // sharedMaterial을 고치면 <b>에셋이 바뀐다</b> — 반드시 사본을 물린다.
                    var copy = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
                    // 가산에서는 어두운 겹이 그려지지 않는다(더해도 0이다) — 그래서 아예 끈다.
                    copy.SetFloat("_EdgeWidth", 0f);
                    copy.SetFloat("_InkBlend", 0f);
                    copy.SetFloat("_Posterize", 4f);
                    copy.SetFloat("_RimBoost", 1.2f);
                    copy.SetFloat("_CoreBoost", 1.2f);
                    copy.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    copy.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    renderer.sharedMaterial = copy;
                }

                foreach (var root in new[] { go, glow })
                {
                    foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        if (ps.transform.parent != null &&
                            ps.transform.parent.GetComponentInParent<ParticleSystem>() != null)
                        {
                            continue;
                        }

                        ps.Simulate(subject.Time, true, true);
                    }
                }

                var file = $"grain_{subject.Id}.png";
                Render(cam, Path.Combine(outputFolder, file), subject.Height, Vector3.zero);
                shots.Add(new Shot
                {
                    Bench = "grain", Id = subject.Id, Subject = subject.Label,
                    Treatment = "Q22-나 — 출하(가) 위에 가산 발광 겹을 한 장 더",
                    Image = Path.Combine(outputFolder, file),
                });

                UnityEngine.Object.DestroyImmediate(glow);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 확정 사양 벤치 — 2026-08-07 판정(Q2-나 · Q3-가 · Q4-나 · Q5-나)을 한 벌로 굽는다
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 확정 사양 = <b>바깥부터 [발광 림 · 다크 키라인 · 짙은 먹 몸통 · 네온 코어]</b> 4겹.
        ///
        /// <para>🔴 <b>겹 두께는 포스터라이즈 단수에 물려 있다.</b> 알파를 4단으로 끊으면 화면에 존재하는
        /// 값이 0.25·0.5·0.75·1 넷뿐이라, 각 겹의 폭을 그 간격에 맞춰야 <b>겹이 하나씩 정확히</b> 잡힌다.
        /// 폭을 임의로 줄이면 그 겹을 차지할 알파 값이 없어 <b>조용히 사라진다</b> — 화면에서 겹이
        /// 안 보이면 색이 아니라 이 대응부터 볼 것.</para>
        /// </summary>
        private static void ApplyConfirmedSpec(Material m, Color family, bool keyline)
        {
            // 🔴 확정 수치는 <b>여기 없다</b>. C단계에서 <see cref="MonsterAttackVfxBuilder"/>로
            // 이관했고(출하 프리팹이 그걸 쓴다), 벤치가 자기 사본을 들면 <b>벤치에서 통과한 그림과
            // 실제로 출하되는 그림이 조용히 갈라진다</b>. 그래서 벤치가 출하 코드를 부른다.
            MonsterAttackVfxBuilder.ApplyConfirmedSpec(m, family, keyline);
        }

        private static void CaptureSpecMatrix(Camera cam, string outputFolder, List<Shot> shots)
        {
            var shader = Shader.Find(InkShaderName);
            var teal = Hex("#35C8B0");
            var red = Hex("#E0524E");

            // (1) 절차 마스크에 확정 사양 — 4겹이 실제로 하나씩 잡히는지부터 본다.
            var psGo = new GameObject("Spec Probe");
            psGo.transform.position = new Vector3(0f, 0.05f, 2f);
            psGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var ps = psGo.AddComponent<ParticleSystem>();
            ConfigureProbe(ps);
            var probeRenderer = psGo.GetComponent<ParticleSystemRenderer>();

            foreach (var (kind, label) in new[]
                     {
                         (VfxInkMaskBaker.InkMaskKind.BrushSlash, "붓형 테이퍼"),
                         (VfxInkMaskBaker.InkMaskKind.Shard, "각진 파편"),
                         (VfxInkMaskBaker.InkMaskKind.Ring, "붓 링"),
                         (VfxInkMaskBaker.InkMaskKind.Crack, "균열선"),
                         (VfxInkMaskBaker.InkMaskKind.Streak, "스트레치 획"),
                         (VfxInkMaskBaker.InkMaskKind.Flame, "불꽃 혀"),
                     })
            {
                var mask = BakeMask(kind, 0.115f);
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetTexture("_MainTex", mask);
                material.SetColor("_Color", Color.white);
                material.SetFloat("_InkStrength", 0f);
                ApplyConfirmedSpec(material, teal, keyline: true);
                probeRenderer.sharedMaterial = material;

                ps.Clear(true);
                ps.Simulate(0.2f, true, true);

                var file = $"spec_mask_{kind.ToString().ToLowerInvariant()}.png";
                Render(cam, Path.Combine(outputFolder, file));
                shots.Add(new Shot
                {
                    Bench = "spec", Id = "mask_" + kind.ToString().ToLowerInvariant(),
                    Subject = label, Treatment = "확정 사양 4겹",
                    Image = Path.Combine(outputFolder, file),
                });
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(mask);
            }

            UnityEngine.Object.DestroyImmediate(psGo);

            // (2) 출하 프리팹에 확정 사양. 세 번째는 <b>키라인 면제</b> 대상(Q2-나)이라 짝으로 굽는다.
            var subjects = new (string Id, string Label, string Path, Color Family, bool Keyline)[]
            {
                ("clawslash", "A003 발톱 참격", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_ClawSlash.prefab", teal, true),
                ("heavypunch", "A014 강펀치", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_HeavyPunch.prefab", red, true),
                ("horncharge", "A015 뿔 돌진 (연기 = 키라인 면제)", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_HornCharge.prefab", red, false),
                ("firebreath", "A007 화염 브레스", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_FireBreath.prefab", Hex("#F08A3C"), true),
                ("pressurecross", "A005 위압 십자", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_PressureCross.prefab", red, true),
                ("fissuretile", "A006 균열 모듈", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_FissureTile.prefab", red, true),
                ("fissureringtile", "A018 균열 모듈 (여백판)", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_FissureRingTile.prefab", red, true),
                ("artillerytile", "A022 착탄 모듈", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_ArtilleryTile.prefab", red, true),
                ("roarripple", "A029 포효 파문 (재제작)", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_RoarRipple.prefab", red, true),
                ("heavypunch2", "A014 강펀치", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_HeavyPunch.prefab", red, true),
            };

            foreach (var subject in subjects)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(subject.Path);
                if (prefab == null)
                {
                    continue;
                }

                foreach (var keyline in subject.Keyline ? new[] { true } : new[] { true, false })
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, 2f), Quaternion.identity);
                    var made = new List<Material>();
                    foreach (var renderer in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    {
                        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                        material.SetTexture("_MainTex", ResolveMainTexture(renderer.sharedMaterial));
                        material.SetColor("_Color", subject.Family);
                        material.SetFloat("_InkStrength", 0f);
                        material.SetFloat("_LumaAlpha", 1f);
                        ApplyConfirmedSpec(material, subject.Family, keyline);
                        renderer.sharedMaterial = material;
                        made.Add(material);
                    }

                    foreach (var particle in go.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        if (particle.transform.parent != null &&
                            particle.transform.parent.GetComponentInParent<ParticleSystem>() != null)
                        {
                            continue;
                        }

                        particle.Simulate(0.25f, true, true);
                    }

                    var suffix = subject.Keyline ? "spec" : (keyline ? "withkeyline" : "nokeyline");
                    var file = $"spec_{subject.Id}_{suffix}.png";
                    Render(cam, Path.Combine(outputFolder, file));
                    shots.Add(new Shot
                    {
                        Bench = "spec", Id = subject.Id + "_" + suffix, Subject = subject.Label,
                        Treatment = keyline ? "확정 사양 (키라인 O)" : "확정 사양 (키라인 면제)",
                        Image = Path.Combine(outputFolder, file),
                    });

                    UnityEngine.Object.DestroyImmediate(go);
                    foreach (var material in made)
                    {
                        UnityEngine.Object.DestroyImmediate(material);
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 형태 벤치 — 절차 마스크 × 표면 처리
        // ─────────────────────────────────────────────────────────────────────

        // 🔴 절차 마스크의 정의는 <b>여기 없다</b>. C단계에서 <see cref="VfxInkMaskBaker"/>로
        // 승격시켰고(출하 마스크를 굽는 도구다), 벤치가 자기 사본을 들면 벤치에서 판정한 형상과
        // 실제로 구워지는 형상이 갈라진다. 벤치는 같은 함수를 메모리 모드로 부를 뿐이다.
        private static Texture2D BakeMask(VfxInkMaskBaker.InkMaskKind kind, float ramp)
        {
            return VfxInkMaskBaker.Bake(kind, ramp);
        }

        private static void CaptureShapeMatrix(Camera cam, string outputFolder, List<Shot> shots)
        {
            var masks = new (VfxInkMaskBaker.InkMaskKind Kind, string Label)[]
            {
                (VfxInkMaskBaker.InkMaskKind.Ribbon, "등폭 리본 · 부드러운 페이드"),
                (VfxInkMaskBaker.InkMaskKind.BrushSlash, "붓형 테이퍼 · 하드 실루엣"),
                (VfxInkMaskBaker.InkMaskKind.BrushDry, "붓형 + 절차 비백"),
                (VfxInkMaskBaker.InkMaskKind.Shard, "각진 파편 다발"),
            };

            // 🔑 키라인 두께는 <b>셰이더 파라미터만으로 정해지지 않는다</b> — 테두리 밴드가 알파 경사를
            // 먹고 살기 때문에 <b>마스크를 구울 때의 경사 폭(ramp)</b>이 같이 굵기를 정한다.
            // 그래서 표면 변주마다 자기 ramp를 들고 있고, 마스크는 (형태 × ramp)로 굽는다.
            var surfaces = new (string Id, string Label, float Ramp, Action<Material> Apply)[]
            {
                ("plain", "키라인 없음 · 가산", 0.02f, m =>
                {
                    Configure(m, posterize: 1f, edgeWidth: 0f, edgeColor: Color.white, edgeBoost: 1f,
                        inkBlend: 0f, additive: true);
                }),
                ("celthin", "셀 + 얇은 다크 키라인", 0.05f, m =>
                {
                    Configure(m, posterize: 4f, edgeWidth: 0.3f, edgeColor: Keyline, edgeBoost: 1f,
                        inkBlend: 0f, additive: false);
                }),
                ("celthick", "셀 + 굵은 다크 키라인", 0.115f, m =>
                {
                    Configure(m, posterize: 4f, edgeWidth: 0.55f, edgeColor: Keyline, edgeBoost: 1f,
                        inkBlend: 0f, additive: false);
                }),
                ("celneon", "굵은 키라인 + 네온 코어", 0.115f, m =>
                {
                    Configure(m, posterize: 4f, edgeWidth: 0.55f, edgeColor: Keyline, edgeBoost: 1f,
                        inkBlend: 0f, additive: false);
                    // 네온은 몸통 안쪽에서만 나오게 한다 — 카드 일러가 발광을 다루는 방식이다.
                    m.SetColor("_Color", new Color(Mint.r * 2.1f, Mint.g * 2.1f, Mint.b * 2.1f, 1f));
                }),
            };

            var shader = Shader.Find(InkShaderName);
            var textures = new Dictionary<(VfxInkMaskBaker.InkMaskKind, float), Texture2D>();
            foreach (var mask in masks)
            {
                foreach (var surface in surfaces)
                {
                    var key = (mask.Kind, surface.Ramp);
                    if (!textures.ContainsKey(key))
                    {
                        textures[key] = BakeMask(mask.Kind, surface.Ramp);
                    }
                }
            }

            var psGo = new GameObject("Shape Probe");
            psGo.transform.position = new Vector3(0f, 0.05f, 2f);
            psGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 판이 위(Top 뷰)를 보게 눕힌다
            var ps = psGo.AddComponent<ParticleSystem>();
            ConfigureProbe(ps);
            var renderer = psGo.GetComponent<ParticleSystemRenderer>();

            foreach (var mask in masks)
            {
                foreach (var surface in surfaces)
                {
                    var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    material.SetTexture("_MainTex", textures[(mask.Kind, surface.Ramp)]);
                    material.SetColor("_Color", Color.white);
                    material.SetColor("_InkColor", InkSoot);
                    material.SetFloat("_InkStrength", 0f);
                    material.SetFloat("_AlphaCut", 0.04f);
                    surface.Apply(material);
                    renderer.sharedMaterial = material;

                    ps.Clear(true);
                    ps.Simulate(0.2f, true, true);

                    var name = $"shape_{mask.Kind.ToString().ToLowerInvariant()}_{surface.Id}.png";
                    Render(cam, Path.Combine(outputFolder, name));
                    shots.Add(new Shot
                    {
                        Bench = "shape",
                        Id = $"{mask.Kind.ToString().ToLowerInvariant()}_{surface.Id}",
                        Subject = mask.Label,
                        Treatment = surface.Label,
                        Image = Path.Combine(outputFolder, name),
                    });
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }

            UnityEngine.Object.DestroyImmediate(psGo);
            foreach (var texture in textures.Values)
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>마스크 한 장만 크게 띄우는 프로브. 형상 외의 변수를 전부 없앤다.</summary>
        private static void ConfigureProbe(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 5f;
            main.loop = false;
            main.startLifetime = 5f;
            main.startSpeed = 0f;
            main.startSize = 5.2f;
            main.startColor = Mint;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = false;
            main.maxParticles = 4;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var shape = ps.shape;
            shape.enabled = false;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = BuildQuad();
            renderer.alignment = ParticleSystemRenderSpace.Local;
        }

        private static Mesh _quad;

        private static Mesh BuildQuad()
        {
            if (_quad != null)
            {
                return _quad;
            }

            _quad = new Mesh
            {
                name = "StyleLabQuad",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            _quad.RecalculateNormals();
            return _quad;
        }

        // ─────────────────────────────────────────────────────────────────────
        // 표면 벤치 — 출하 프리팹 × 머티리얼 프리셋
        // ─────────────────────────────────────────────────────────────────────

        private static void CaptureSurfaceMatrix(Camera cam, string outputFolder, List<Shot> shots)
        {
            // 대표 둘: 형태가 이미 읽히는 것(발톱)과 가장자리가 아예 없는 것(포효).
            // ⚠️ 여기서는 카탈로그 배치 로직을 타지 않는다 — 원점에 하나만 띄운다.
            //    묻는 것이 "어디에 뜨나"가 아니라 "어떤 표면인가"이기 때문이다.
            // 🔴 틴트를 여기서 들려 보내야 한다 — NAMU 계열은 파티클 startColor로 물들지 않아
            // (Custom Data에서 색을 가져간다) 우리 셰이더로 갈아 끼우면 정점 색이 흰색으로 온다.
            // 그대로 두면 전 변주가 무채색으로 나와 색 판정이 아예 불가능하다(실측으로 밟음).
            var subjects = new (string Id, string Label, string Path, float Time, Color Tint)[]
            {
                ("clawslash", "A003 발톱 참격", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_ClawSlash.prefab",
                    0.25f, Hex("#35C8B0")),
                ("heavypunch", "A014 강펀치", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_HeavyPunch.prefab",
                    0.25f, Hex("#E0524E")),
                // ⚠️ A029 포효 파문은 <b>일부러 뺐다</b>. 그 큐의 붉은 방사는 알파가 전면 1이고 RGB도
                // 가장자리까지 중간 밝기라, 어떤 표면 처리를 걸어도 <b>사각 판이 남는다</b>(세 번 실측).
                // 즉 파라미터로 고칠 수 있는 대상이 아니라 <b>다시 만들어야 하는 대상</b>이고,
                // 그 사실 자체가 Q3의 답 재료다 — 비교 격자에 끼우면 나머지 판정을 방해한다.
                ("roarripple", "A029 포효 파문 (참고)", "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_RoarRipple.prefab",
                    0.25f, Hex("#E0524E")),
            };

            var presets = new (string Id, string Label, Action<Material> Apply)[]
            {
                ("s1plain", "셰이더만 교체 · 중립", m =>
                    Configure(m, 1f, 0f, Color.white, 1f, 0f, additive: true)),
                ("s2cel", "셀(3단) + 얇은 다크 키라인", m =>
                    Configure(m, 3f, 0.16f, Keyline, 1f, 0f, additive: false, lumaAlpha: 1f)),
                ("s3celthick", "셀(3단) + 굵은 다크 키라인", m =>
                    Configure(m, 3f, 0.34f, Keyline, 1f, 0f, additive: false, lumaAlpha: 1f)),
                ("s4neonrim", "셀 + 밝은 네온 림", m =>
                    Configure(m, 3f, 0.22f, PaperWhite, 2.2f, 0f, additive: true)),
                ("s5inkbody", "어두운 먹 몸통 + 발광 림", m =>
                    Configure(m, 3f, 0.24f, Mint, 2.6f, 0.72f, additive: false, lumaAlpha: 1f)),
                // 🔴 "소프트 글로우를 알파 블렌딩으로 옮긴다"는 성립하지 않는다 — 가산으로 그려진
                // 방사 그라디언트는 가장자리도 검지 않아서(중간 밝기 붉은색) 알파로 합성하면
                // <b>불투명한 사각 판</b>이 된다(두 번 실측). 그래서 두 갈래로 나눠 묻는다:
                //   s6 = 가산은 유지하고 <b>옅은 꼬리만 잘라 낸다</b>(clip).
                //   s7 = <b>글로우 파티클 자체를 끈다</b> — 이쪽이 Q3 "금지"의 실제 모습이다.
                ("s6trim", "가산 유지 + 옅은 헤일로 잘라 내기", m =>
                {
                    Configure(m, 2f, 0.2f, PaperWhite, 1.2f, 0.1f, additive: true);
                    m.SetFloat("_AlphaCut", 0.45f);
                }),
            };

            var shader = Shader.Find(InkShaderName);
            foreach (var subject in subjects)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(subject.Path);
                if (prefab == null)
                {
                    shots.Add(new Shot
                    {
                        Bench = "surface", Id = subject.Id + "_missing", Subject = subject.Label,
                        Treatment = "프리팹을 못 찾았다", Image = string.Empty, Error = subject.Path,
                    });
                    continue;
                }

                // s0 = 현행(원본 머티리얼 그대로). 기준선이 없으면 나머지를 읽을 수 없다.
                CaptureSurfaceShot(cam, prefab, subject, ("s0current", "현행 반입분", null), shader, outputFolder, shots,
                    killLargestGlow: false);
                foreach (var preset in presets)
                {
                    CaptureSurfaceShot(cam, prefab, subject, preset, shader, outputFolder, shots,
                        killLargestGlow: false);
                }

                CaptureSurfaceShot(cam, prefab, subject,
                    ("s7killglow", "셀 + 글로우 파티클 자체를 제거", m =>
                        Configure(m, 3f, 0.3f, Keyline, 1f, 0f, additive: true)),
                    shader, outputFolder, shots, killLargestGlow: true);
            }
        }

        private static void CaptureSurfaceShot(
            Camera cam,
            GameObject prefab,
            (string Id, string Label, string Path, float Time, Color Tint) subject,
            (string Id, string Label, Action<Material> Apply) preset,
            Shader shader,
            string outputFolder,
            List<Shot> shots,
            bool killLargestGlow)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, 2f), Quaternion.identity);

            if (killLargestGlow)
            {
                // 가장 큰 파티클 하나가 소프트 글로우인 경우가 압도적이다(A029의 붉은 방사가 그렇다).
                // 정교한 판별 대신 이 휴리스틱을 쓰는 이유: 여기서 묻는 것은 "어느 시스템을 끌 것인가"가
                // 아니라 "끄면 그림이 어떻게 되는가"이기 때문이다.
                ParticleSystemRenderer biggest = null;
                var biggestSize = -1f;
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var size = ps.main.startSize.constantMax * ps.transform.lossyScale.x;
                    if (size > biggestSize)
                    {
                        biggestSize = size;
                        biggest = ps.GetComponent<ParticleSystemRenderer>();
                    }
                }

                if (biggest != null)
                {
                    biggest.enabled = false;
                }
            }

            var made = new List<Material>();
            if (preset.Apply != null)
            {
                foreach (var renderer in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    material.SetTexture("_MainTex", ResolveMainTexture(renderer.sharedMaterial));
                    material.SetColor("_Color", subject.Tint);
                    material.SetColor("_InkColor", InkSoot);
                    material.SetFloat("_InkStrength", 0f);
                    material.SetFloat("_AlphaCut", 0.04f);
                    preset.Apply(material);
                    renderer.sharedMaterial = material;
                    made.Add(material);
                }
            }

            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.transform.parent != null && ps.transform.parent.GetComponentInParent<ParticleSystem>() != null)
                {
                    continue;
                }

                ps.Simulate(Mathf.Max(0.0001f, subject.Time), true, true);
            }

            var name = $"surface_{subject.Id}_{preset.Id}.png";
            Render(cam, Path.Combine(outputFolder, name));
            shots.Add(new Shot
            {
                Bench = "surface",
                Id = $"{subject.Id}_{preset.Id}",
                Subject = subject.Label,
                Treatment = preset.Label,
                Image = Path.Combine(outputFolder, name),
            });

            UnityEngine.Object.DestroyImmediate(go);
            foreach (var material in made)
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        /// <summary>팩마다 메인 텍스처 프로퍼티 이름이 다르다 — <c>_MainTex</c> 우선, 없으면 값 있는 첫 텍스처.</summary>
        private static Texture ResolveMainTexture(Material source)
        {
            if (source == null)
            {
                return null;
            }

            if (source.HasProperty("_MainTex") && source.GetTexture("_MainTex") != null)
            {
                return source.GetTexture("_MainTex");
            }

            var shader = source.shader;
            for (var i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture)
                {
                    continue;
                }

                var texture = source.GetTexture(shader.GetPropertyName(i));
                if (texture != null)
                {
                    return texture;
                }
            }

            return null;
        }

        private static void Configure(
            Material m,
            float posterize,
            float edgeWidth,
            Color edgeColor,
            float edgeBoost,
            float inkBlend,
            bool additive,
            float lumaAlpha = 0f)
        {
            // 🔴 팩 텍스처는 가산 전제라 알파 채널이 쓸모없다 — 알파 블렌딩으로 갈아탈 때 이 값을
            // 1로 두지 않으면 쿼드 전체가 불투명한 검은 판으로 찍힌다(첫 캡처에서 실측).
            m.SetFloat("_LumaAlpha", lumaAlpha);
            m.SetFloat("_Posterize", posterize);
            m.SetFloat("_EdgeWidth", edgeWidth);
            m.SetColor("_EdgeColor", edgeColor);
            m.SetFloat("_EdgeBoost", edgeBoost);
            m.SetFloat("_InkBlend", inkBlend);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive
                ? UnityEngine.Rendering.BlendMode.One
                : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        }

        // ─────────────────────────────────────────────────────────────────────

        private static void Render(Camera cam, string path)
        {
            Render(cam, path, 11f, new Vector3(0f, 0f, 2f));
        }

        private static void Render(Camera cam, string path, float height, Vector3 center)
        {
            var tex = RenderToTexture(cam, 960, 640, height, center);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>한 장 렌더해서 <see cref="Texture2D"/>로 돌려준다 — 호출자가 파일로 쓰든
        /// 컨택트 시트에 이어 붙이든 정한다. 반환된 텍스처는 <b>호출자가 파괴한다.</b></summary>
        private static Texture2D RenderToTexture(Camera cam, int width, int height, float camHeight, Vector3 center)
        {
            cam.transform.position = center + new Vector3(0f, camHeight, -0.001f);
            cam.transform.LookAt(center);

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            return tex;
        }

        private static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var color) ? color : Color.magenta;
        }

        private sealed class Shot
        {
            public string Bench = string.Empty;
            public string Id = string.Empty;
            public string Subject = string.Empty;
            public string Treatment = string.Empty;
            public string Image = string.Empty;
            public string Error = string.Empty;
        }

        private static string BuildJson(List<Shot> shots)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"shots\": [\n");
            for (var i = 0; i < shots.Count; i++)
            {
                var s = shots[i];
                sb.Append("    {\"bench\":").Append(Json(s.Bench));
                sb.Append(",\"id\":").Append(Json(s.Id));
                sb.Append(",\"subject\":").Append(Json(s.Subject));
                sb.Append(",\"treatment\":").Append(Json(s.Treatment));
                sb.Append(",\"image\":").Append(Json(s.Image.Replace('\\', '/')));
                sb.Append(",\"error\":").Append(Json(s.Error)).Append('}');
                sb.Append(i == shots.Count - 1 ? "\n" : ",\n");
            }

            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        private static string Json(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            var sb = new StringBuilder("\"");
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    default:
                        if (ch < 32)
                        {
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(ch);
                        }

                        break;
                }
            }

            return sb.Append('"').ToString();
        }
    }
}
