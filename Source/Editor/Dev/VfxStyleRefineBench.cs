using System.Collections.Generic;
using System.IO;
using SeoulPlayup.Combat.Unity.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SeoulPlayup.EditorTools.Dev
{
    /// <summary>
    /// 스타일 축 조사(Q53~) 전용 벤치 — 후보 ⓐ「현행 먹 정제」의 실물 A/B.
    ///
    /// <para>출하 참격(현행 P1 반입분)과 「정제판」(전처리로 잔가닥을 정리하고 닫기를 강화해
    /// 다시 구운 <c>ink_gen_stroke_r_*</c> + 그레인·boiling 하향)을 <b>같은 씬·카메라·시드·시각</b>으로
    /// 굽는다. 프리팹·머티리얼 에셋은 건드리지 않는다 — 정제판은 클론 인스턴스의 머티리얼만
    /// 메모리에서 갈아 끼우고 캡처 후 버린다(<see cref="VfxStyleVariantLab"/>과 같은 계약).</para>
    ///
    /// <para>산출물: 4×2 모션 스트립(<c>motion_*.png</c>, 랩과 같은 8시각) +
    /// 60fps 연속 프레임(<c>seq_*/f_###.png</c>, 0~0.60s — 비교 영상 조립용).
    /// 판정이 끝나면 이 파일은 지우거나, ⓐ 채택 시 튜닝값을 빌더 프리셋으로 옮긴다.</para>
    /// </summary>
    public static class VfxStyleRefineBench
    {
        private const string InkShaderName = "SeoulPlayup/Ink Particle";
        private const string OutputFolder = "Temp/VfxStyleRefine";
        private const string FallbackScenePath = "Assets/Scenes/Dev/PrototypeTest.unity";
        private const string ShippingVolumeProfile = "Assets/Art/Lookdev/ArtLookdevVolumeProfile.asset";

        private const string ClawSlashPrefab = "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_ClawSlash.prefab";

        /// <summary>출하 아크 머티리얼의 _MainTex 이름 — 변주 스왑의 매칭 키. 🔴 빌더가 아크
        /// 그림을 갈면 여기도 따라와야 한다(안 맞으면 변주 열이 조용히 ship과 같아진다).
        /// Q58 재판정(ⓒ 셀 완성형)부터는 cel이 출하다. ⚠️변주 열은 아크 머티리얼만 갈므로
        /// ship에 추가된 셀 문법(임팩트 플래시·방사 속도선)은 변주 열에도 남는다 — 변주 열은
        /// 이제 「아크 그림 차이」만 읽을 것.</summary>
        private const string ShipStrokeDfName = "ink_gen_stroke_c_df";

        /// <summary>벤치 변주 — 아크 머티리얼의 텍스처·파라미터만 갈아 끼운다(시간 문법 무변경).</summary>
        private readonly struct Variant
        {
            public readonly string Id;
            public readonly string DfPath;      // null = 스왑 없음(출하 그대로)
            public readonly string GrainPath;   // null = 그레인 유지
            public readonly float InkBlend;     // < 0 = 유지
            public readonly float BoilStrength; // < 0 = 유지
            public readonly float Posterize;    // < 0 = 유지

            public Variant(string id, string dfPath, string grainPath,
                float inkBlend, float boilStrength, float posterize)
            {
                Id = id;
                DfPath = dfPath;
                GrainPath = grainPath;
                InkBlend = inkBlend;
                BoilStrength = boilStrength;
                Posterize = posterize;
            }
        }

        // 1R = ⓐ 정제(Q54-다 기각, 기록용으로 유지) · 2R = ⓑ볼드&클린·ⓒ셀 카툰(Q55-나).
        private static readonly Variant[] Variants =
        {
            new Variant("clawslash_ship", null, null, -1f, -1f, -1f),
            new Variant("clawslash_refined",
                "Assets/Art/VFX/InkMasks/ink_gen_stroke_r_df.png",
                "Assets/Art/VFX/InkMasks/ink_gen_stroke_r_grain.png",
                0.18f, 0.05f, -1f),
            new Variant("clawslash_b_bold",
                "Assets/Art/VFX/InkMasks/ink_gen_stroke_b_df.png",
                "Assets/Art/VFX/InkMasks/ink_gen_stroke_b_grain.png",
                0.22f, 0.07f, -1f),
            // ⓒ: 그레인 0(평면 셀), boiling 유지, 계단 8→5(4단이면 키라인이 채움이 된다 — 금지선 위)
            new Variant("clawslash_c_cel",
                "Assets/Art/VFX/InkMasks/ink_gen_stroke_c_df.png",
                null, 0f, -1f, 5f),
            // ⓑ 파일럿(Q57-가) 단면 보강판 — 채움율 21.8%→41.7%. 반입 전 벤치 검증 열.
            new Variant("clawslash_bold2",
                "Assets/Art/VFX/InkMasks/ink_gen_stroke_bold_df.png",
                "Assets/Art/VFX/InkMasks/ink_gen_stroke_bold_grain.png",
                0.22f, 0.07f, -1f),
            // 구 P1(비백 획) — ⓑ 반입 후 ship이 bold가 되면서 사라진 「현행이던 것」의 기록 열.
            // 값은 cs:1089 반입 당시 아크 블록(_InkBlend 0.34 · _BoilStrength 0.10) 그대로.
            new Variant("clawslash_p1",
                "Assets/Art/VFX/InkMasks/ink_gen_stroke_df.png",
                "Assets/Art/VFX/InkMasks/ink_gen_stroke_grain.png",
                0.34f, 0.10f, -1f),
        };

        [MenuItem("Tools/Seoul Playup/Combat/Capture VFX Refine Bench (스타일 축 ⓐ A/B)")]
        public static void CaptureAllMenu()
        {
            Debug.Log(CaptureAll(OutputFolder));
        }

        /// <summary>에이전트가 <c>script-execute</c>로 부르는 진입점.</summary>
        public static string CaptureAll(string outputFolder)
        {
            outputFolder = string.IsNullOrWhiteSpace(outputFolder) ? OutputFolder : outputFolder;
            Directory.CreateDirectory(outputFolder);

            var previousScene = SceneManager.GetActiveScene().path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var written = new List<string>();
            try
            {
                var stageGo = new GameObject("Refine Bench Stage");
                var stage = stageGo.AddComponent<VfxLabStage>();
                stage.EnsureBoard();

                var camGo = new GameObject("Capture Camera");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.043f, 0.067f, 0.122f); // #0B111F
                cam.fieldOfView = 45f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;
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

                foreach (var variant in Variants)
                {
                    CaptureColumn(cam, outputFolder, variant, written);
                }

                Object.DestroyImmediate(camGo);
                if (volumeGo != null)
                {
                    Object.DestroyImmediate(volumeGo);
                }

                Object.DestroyImmediate(stageGo);
            }
            finally
            {
                Shader.SetGlobalFloat("_VfxSimTime", 0f);
                var restoreTo = !string.IsNullOrEmpty(previousScene) && File.Exists(previousScene)
                    ? previousScene
                    : FallbackScenePath;
                if (File.Exists(restoreTo))
                {
                    EditorSceneManager.OpenScene(restoreTo, OpenSceneMode.Single);
                }
            }

            return $"VfxStyleRefineBench: {written.Count}장 → {outputFolder}";
        }

        private static void CaptureColumn(
            Camera cam, string outputFolder, Variant variant, List<string> written)
        {
            var id = variant.Id;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClawSlashPrefab);
            if (prefab == null)
            {
                Debug.LogError("[VfxStyleRefineBench] 프리팹을 못 찾았다: " + ClawSlashPrefab);
                return;
            }

            // 4×2 모션 스트립 — 랩과 같은 8시각(Q42 수명 0.50s 배열).
            var stripTimes = new[] { 0.02f, 0.07f, 0.13f, 0.20f, 0.27f, 0.34f, 0.42f, 0.50f };
            const int cols = 4;
            const int frameWidth = 420;
            const int frameHeight = 280;
            const int gutter = 3;
            var rows = (stripTimes.Length + cols - 1) / cols;
            var sheetWidth = cols * frameWidth + (cols - 1) * gutter;
            var sheetHeight = rows * frameHeight + (rows - 1) * gutter;

            var strip = new Texture2D(sheetWidth, sheetHeight, TextureFormat.RGB24, false);
            var gutterFill = new Color[sheetWidth * sheetHeight];
            var gutterColor = new Color(0.149f, 0.196f, 0.298f); // #26324C
            for (var i = 0; i < gutterFill.Length; i++)
            {
                gutterFill[i] = gutterColor;
            }

            strip.SetPixels(gutterFill);

            for (var f = 0; f < stripTimes.Length; f++)
            {
                var frame = CaptureFrame(cam, prefab, variant, stripTimes[f], frameWidth, frameHeight);
                var col = f % cols;
                var row = f / cols;
                strip.SetPixels(col * (frameWidth + gutter),
                    (rows - 1 - row) * (frameHeight + gutter),
                    frameWidth, frameHeight, frame.GetPixels());
                Object.DestroyImmediate(frame);
            }

            strip.Apply(false, false);
            var stripFile = Path.Combine(outputFolder, $"motion_{id}.png");
            File.WriteAllBytes(stripFile, strip.EncodeToPNG());
            Object.DestroyImmediate(strip);
            written.Add(stripFile);

            // 60fps 연속 프레임 — 비교 영상(그리드 WebP) 조립용. 0~0.60s(수명 0.50s + 여유).
            var seqDir = Path.Combine(outputFolder, $"seq_{id}");
            Directory.CreateDirectory(seqDir);
            const int seqFrames = 37; // 0/60 ~ 36/60 = 0.60s
            for (var f = 0; f < seqFrames; f++)
            {
                var t = f / 60f;
                var frame = CaptureFrame(cam, prefab, variant, t, frameWidth, frameHeight);
                var file = Path.Combine(seqDir, $"f_{f:000}.png");
                File.WriteAllBytes(file, frame.EncodeToPNG());
                Object.DestroyImmediate(frame);
                written.Add(file);
            }
        }

        private static Texture2D CaptureFrame(
            Camera cam, GameObject prefab, Variant variant, float time, int width, int height)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, 0f), Quaternion.identity);

            var swapped = new List<Material>();
            if (variant.DfPath != null)
            {
                SwapArcMaterials(go, variant, swapped);
            }

            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
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

                ps.Simulate(time, true, true);
            }

            Shader.SetGlobalFloat("_VfxSimTime", time);

            cam.transform.position = new Vector3(0f, 4.5f, -0.001f);
            cam.transform.LookAt(Vector3.zero);
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);

            Object.DestroyImmediate(go);
            foreach (var m in swapped)
            {
                Object.DestroyImmediate(m);
            }

            return tex;
        }

        /// <summary>클론 인스턴스의 아크 머티리얼(현행 <c>ink_gen_stroke_df</c> 사용분)만
        /// 변주 사본으로 바꾼다 — 에셋은 무접촉, 사본은 캡처 후 파기.</summary>
        private static void SwapArcMaterials(GameObject go, Variant variant, List<Material> created)
        {
            var df = AssetDatabase.LoadAssetAtPath<Texture>(variant.DfPath);
            var grain = variant.GrainPath != null
                ? AssetDatabase.LoadAssetAtPath<Texture>(variant.GrainPath)
                : null;
            if (df == null || (variant.GrainPath != null && grain == null))
            {
                Debug.LogError("[VfxStyleRefineBench] 변주 텍스처가 없다 — 먼저 구울 것: "
                               + variant.DfPath + " / " + variant.GrainPath);
                return;
            }

            foreach (var renderer in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                var mats = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader == null || m.shader.name != InkShaderName)
                    {
                        continue;
                    }

                    var main = m.GetTexture("_MainTex");
                    if (main == null || main.name != ShipStrokeDfName)
                    {
                        continue;
                    }

                    var clone = new Material(m);
                    clone.name = m.name + " (" + variant.Id + ")";
                    clone.SetTexture("_MainTex", df);
                    if (grain != null)
                    {
                        clone.SetTexture("_InkTex", grain);
                    }

                    if (variant.InkBlend >= 0f)
                    {
                        clone.SetFloat("_InkBlend", variant.InkBlend);
                    }

                    if (variant.BoilStrength >= 0f)
                    {
                        clone.SetFloat("_BoilStrength", variant.BoilStrength);
                    }

                    if (variant.Posterize >= 0f)
                    {
                        clone.SetFloat("_Posterize", variant.Posterize);
                    }

                    mats[i] = clone;
                    created.Add(clone);
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = mats;
                }
            }
        }

        // ── 튜닝 절(인계문 vfx-style-cel-tuning-handoff §2·§3 — Q60~ 판정 재료) ──────────
        //
        // 색감(검붉은 몸통·검은 코어·쨍함 분해)과 카메라 각도 축. 위 변주 절과 같은 계약:
        // 에셋 무접촉 — 클론 인스턴스의 머티리얼 사본만 인메모리로 덮어쓰고 캡처 후 버린다.

        /// <summary>색·각도 튜닝 변주. 색은 아크·속도선(_Soft 면제 계열 제외)과 섬광을 나눠 덮어쓴다.
        /// Color 필드는 a > 0일 때만 적용, float 필드는 &lt; 0 = 유지.</summary>
        private readonly struct TuningSpec
        {
            public readonly string Id;
            public readonly Color BodyColor;     // _Color(몸통 틴트 — _InkBlend 0이라 몸통색 그 자체)
            public readonly Color RimColor;      // _RimColor(발광 림)
            public readonly Color CoreColor;     // _CoreColor(코어 — 검게 = 명암 반전)
            public readonly float CoreBoost;
            public readonly float CoreWidth;     // 0 = 코어 제거(분해용)
            public readonly float RimBoost;      // 1 = 림 발광 끔(분해용)
            public readonly Color FlashColor;    // Ink_ImpactFlash _Color(출하 = 흰색 HDR 1.6)
            public readonly float FlashLifetime; // 출하 0.28s — 잔광 공 하향 후보 0.18s(갤러리 §10)
            public readonly bool DisableFlash;
            public readonly bool DisableBursts;  // 방사 속도선 6가닥
            public readonly bool DisableBloom;   // 출하 볼륨 끔(분해용)

            public TuningSpec(string id,
                Color bodyColor = default, Color rimColor = default, Color coreColor = default,
                float coreBoost = -1f, float coreWidth = -1f, float rimBoost = -1f,
                Color flashColor = default, float flashLifetime = -1f,
                bool disableFlash = false, bool disableBursts = false, bool disableBloom = false)
            {
                Id = id;
                BodyColor = bodyColor;
                RimColor = rimColor;
                CoreColor = coreColor;
                CoreBoost = coreBoost;
                CoreWidth = coreWidth;
                RimBoost = rimBoost;
                FlashColor = flashColor;
                FlashLifetime = flashLifetime;
                DisableFlash = disableFlash;
                DisableBursts = disableBursts;
                DisableBloom = disableBloom;
            }
        }

        // 가족색 #E0524E 기준의 튜닝 색들. 검붉은 몸통은 가족색을 만지지 않고(다른 큐 불변)
        // 아크 전용 덮어쓰기다 — 인계문 §2의 지시 그대로.
        private static readonly Color TuneBodyDarkRed = new Color(0.557f, 0.137f, 0.125f);  // #8E2320
        private static readonly Color TuneRimDarkRed = new Color(0.651f, 0.212f, 0.184f);   // #A6362F
        private static readonly Color TuneCoreBlack = new Color(0.08f, 0.06f, 0.07f);       // 명암 반전 코어
        private static readonly Color TuneFlashInk = new Color(0.12f, 0.09f, 0.10f);        // 먹 섬광(HDR 없음)
        private static readonly Color TuneBodyDesat = new Color(0.690f, 0.412f, 0.404f);    // 채도 절반(분해용)

        /// <summary>쨍함 분해 — 임팩트 피크 한 컷에서 손잡이를 하나씩 뺀다(인계문 §2: 「무엇이
        /// 쨍함을 만드는지 한 컷 분해를 먼저」). 코어 면적/블룸/섬광/속도선/림/채도 후보 전부.</summary>
        private static readonly TuningSpec[] DecompSpecs =
        {
            new TuningSpec("ship"),
            new TuningSpec("no_bloom", disableBloom: true),
            new TuningSpec("no_core", coreWidth: 0f),
            new TuningSpec("no_flash", disableFlash: true),
            new TuningSpec("no_streaks", disableBursts: true),
            new TuningSpec("rim_flat", rimBoost: 1f),
            new TuningSpec("body_desat", bodyColor: TuneBodyDesat),
        };

        /// <summary>색 A/B — 사용자 지시(검붉은 몸통·검은 코어)를 손잡이별로 떼었다 붙인다.
        /// combo에는 준비된 한 줄 튜닝(섬광 0.28→0.18s)도 같이 태운다 — 같은 라운드가 싸다.</summary>
        private static readonly TuningSpec[] ColorSpecs =
        {
            new TuningSpec("ship"),
            new TuningSpec("body_darkred", bodyColor: TuneBodyDarkRed),
            new TuningSpec("core_black", coreColor: TuneCoreBlack, coreBoost: 1f),
            new TuningSpec("combo",
                bodyColor: TuneBodyDarkRed, rimColor: TuneRimDarkRed, rimBoost: 1.8f,
                coreColor: TuneCoreBlack, coreBoost: 1f, flashLifetime: 0.18f),
            new TuningSpec("combo_inkflash",
                bodyColor: TuneBodyDarkRed, rimColor: TuneRimDarkRed, rimBoost: 1.8f,
                coreColor: TuneCoreBlack, coreBoost: 1f, flashLifetime: 0.18f,
                flashColor: TuneFlashInk),
        };

        /// <summary>카메라 각도 축(인계문 §3). 실게임 값은 MainGameplay Main Camera 실측 —
        /// pitch 45° · yaw −30° · FOV 50(perspective). 추측 아님(씬 YAML에서 읽음).</summary>
        private readonly struct AngleSpec
        {
            public readonly string Id;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly float Fov;

            public AngleSpec(string id, Vector3 position, Quaternion rotation, float fov)
            {
                Id = id;
                Position = position;
                Rotation = rotation;
                Fov = fov;
            }

            public static AngleSpec FromOrbit(string id, float pitch, float yaw, float distance,
                Vector3 target, float fov)
            {
                var rotation = Quaternion.Euler(pitch, yaw, 0f);
                return new AngleSpec(id, target - rotation * Vector3.forward * distance, rotation, fov);
            }
        }

        private static readonly AngleSpec[] Angles =
        {
            // 현 벤치 그대로(부감 ~90°) — 위 CaptureFrame과 같은 값이라 기존 산출물과 이어 읽힌다.
            new AngleSpec("topdown", new Vector3(0f, 4.5f, -0.001f),
                Quaternion.LookRotation(new Vector3(0f, -4.5f, 0.001f).normalized), 45f),
            // 실게임(출하 전투 씬 실측). 거리는 FOV 차(45→50)를 보정해 프레이밍을 비슷하게.
            AngleSpec.FromOrbit("gameplay", 45f, -30f, 4.0f, new Vector3(0f, 0.3f, 0f), 50f),
            // 측면 저각 — 평면 리본이 옆에서 얼마나 「종잇장」으로 읽히는지 보는 열.
            AngleSpec.FromOrbit("sidelow", 12f, 90f, 4.2f, new Vector3(0f, 0.5f, 0f), 50f),
        };

        [MenuItem("Tools/Seoul Playup/Combat/Capture VFX Cel Tuning Bench (색·각도 Q60~)")]
        public static void CaptureTuningMenu()
        {
            Debug.Log(CaptureTuning("Temp/VfxCelTuning"));
        }

        /// <summary>에이전트가 <c>script-execute</c>로 부르는 진입점(튜닝 절).</summary>
        public static string CaptureTuning(string outputFolder)
        {
            outputFolder = string.IsNullOrWhiteSpace(outputFolder) ? "Temp/VfxCelTuning" : outputFolder;
            Directory.CreateDirectory(outputFolder);

            var previousScene = SceneManager.GetActiveScene().path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var written = new List<string>();
            try
            {
                var stageGo = new GameObject("Cel Tuning Stage");
                var stage = stageGo.AddComponent<VfxLabStage>();
                stage.EnsureBoard();

                var camGo = new GameObject("Capture Camera");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.043f, 0.067f, 0.122f); // #0B111F
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;
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

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClawSlashPrefab);
                if (prefab == null)
                {
                    return "VfxStyleRefineBench: 프리팹을 못 찾았다 — " + ClawSlashPrefab;
                }

                const int frameWidth = 420;
                const int frameHeight = 280;
                var topdown = Angles[0];

                // ① 쨍함 분해 — t=0.30(아크 홀드 + 섬광 피크) 한 컷 × 손잡이별.
                foreach (var spec in DecompSpecs)
                {
                    var frame = CaptureTuningFrame(cam, prefab, spec, topdown, 0.30f,
                        frameWidth, frameHeight, volumeGo);
                    var file = Path.Combine(outputFolder, $"decomp_{spec.Id}.png");
                    File.WriteAllBytes(file, frame.EncodeToPNG());
                    Object.DestroyImmediate(frame);
                    written.Add(file);
                }

                // ② 색 A/B — 60fps 연속 프레임(0~0.60s), 비교 영상 조립용.
                const int seqFrames = 37;
                foreach (var spec in ColorSpecs)
                {
                    var seqDir = Path.Combine(outputFolder, $"seq_color_{spec.Id}");
                    Directory.CreateDirectory(seqDir);
                    for (var f = 0; f < seqFrames; f++)
                    {
                        var frame = CaptureTuningFrame(cam, prefab, spec, f / 60f,
                            frameWidth, frameHeight, volumeGo, topdown);
                        var file = Path.Combine(seqDir, $"f_{f:000}.png");
                        File.WriteAllBytes(file, frame.EncodeToPNG());
                        Object.DestroyImmediate(frame);
                        written.Add(file);
                    }
                }

                // ③ 각도 축 — 출하(ship)와 요청 조합(combo)을 3각도로.
                var angleStyles = new[] { ColorSpecs[0], ColorSpecs[3] };
                foreach (var angle in Angles)
                {
                    foreach (var spec in angleStyles)
                    {
                        var seqDir = Path.Combine(outputFolder, $"seq_angle_{angle.Id}_{spec.Id}");
                        Directory.CreateDirectory(seqDir);
                        for (var f = 0; f < seqFrames; f++)
                        {
                            var frame = CaptureTuningFrame(cam, prefab, spec, f / 60f,
                                frameWidth, frameHeight, volumeGo, angle);
                            var file = Path.Combine(seqDir, $"f_{f:000}.png");
                            File.WriteAllBytes(file, frame.EncodeToPNG());
                            Object.DestroyImmediate(frame);
                            written.Add(file);
                        }
                    }
                }

                Object.DestroyImmediate(camGo);
                if (volumeGo != null)
                {
                    Object.DestroyImmediate(volumeGo);
                }

                Object.DestroyImmediate(stageGo);
            }
            finally
            {
                Shader.SetGlobalFloat("_VfxSimTime", 0f);
                var restoreTo = !string.IsNullOrEmpty(previousScene) && File.Exists(previousScene)
                    ? previousScene
                    : FallbackScenePath;
                if (File.Exists(restoreTo))
                {
                    EditorSceneManager.OpenScene(restoreTo, OpenSceneMode.Single);
                }
            }

            return $"VfxStyleRefineBench(튜닝): {written.Count}장 → {outputFolder}";
        }

        private static Texture2D CaptureTuningFrame(
            Camera cam, GameObject prefab, in TuningSpec spec, AngleSpec angle, float time,
            int width, int height, GameObject volumeGo)
        {
            return CaptureTuningFrame(cam, prefab, spec, time, width, height, volumeGo, angle);
        }

        private static Texture2D CaptureTuningFrame(
            Camera cam, GameObject prefab, in TuningSpec spec, float time,
            int width, int height, GameObject volumeGo, AngleSpec angle)
        {
            if (volumeGo != null && spec.DisableBloom)
            {
                volumeGo.SetActive(false);
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, 0f), Quaternion.identity);

            var created = new List<Material>();
            ApplyTuningOverrides(go, spec, created);

            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var psName = ps.gameObject.name;
                if (spec.DisableFlash && psName == "ImpactFlash")
                {
                    ps.gameObject.SetActive(false);
                }

                if (spec.DisableBursts && psName.StartsWith("Burst"))
                {
                    ps.gameObject.SetActive(false);
                }

                if (spec.FlashLifetime >= 0f && psName == "ImpactFlash")
                {
                    var main = ps.main;
                    main.startLifetime = spec.FlashLifetime;
                }

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

                ps.Simulate(time, true, true);
            }

            Shader.SetGlobalFloat("_VfxSimTime", time);

            cam.transform.SetPositionAndRotation(angle.Position, angle.Rotation);
            cam.fieldOfView = angle.Fov;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);

            Object.DestroyImmediate(go);
            foreach (var m in created)
            {
                Object.DestroyImmediate(m);
            }

            if (volumeGo != null && spec.DisableBloom)
            {
                volumeGo.SetActive(true);
            }

            return tex;
        }

        // ── 입체감 절(사용자 2026-08-09: "2D 평면 느낌 — 입체적이게 보일 수 있나" + "피격 더미를
        //    같이 보여 달라"). 교차 평면(cross-plane)은 스타일라이즈드 참격의 표준 볼륨 기법 —
        //    아크 리본을 코드(궤적 축) 둘레로 회전 복제해 어느 각도에서도 면이 선다.
        //    여기서는 클론 계층을 인메모리로 복제만 하는 목업이다(에셋 무접촉·채택 시 빌더로 이식).

        private const string DummyPlayerPrefab = "Assets/Art/Characters/player/Prefabs/Player.prefab";

        /// <summary>입체감 변주 — 아크 두 시스템(slash_alp/slash_add)을 로컬 X(코드 축) 둘레로
        /// 회전 복제한다. rolls가 비면 출하 그대로(평면 1장).</summary>
        private static readonly (string Id, float[] Rolls)[] SolidityVariants =
        {
            ("flat", new float[0]),
            ("crossed2", new[] { 90f }),
            ("crossed3", new[] { 60f, 120f }),
        };

        /// <summary>입체감 절 전용 각도. 🔴프로브 실측: 교차판은 전부 코드(궤적 chord ≈ 월드 X)를
        /// 품으므로 <b>코드 축을 따라 보는 yaw 90°에서는 전부 모서리로 서서 안 보인다</b>(0.00%) —
        /// 그래서 코드와 직교로 보는 yaw 0° 열(sideacross)을 함께 둔다. 부감은 수직판이 선으로
        /// 접혀 차이가 안 보이므로(0.00%) 이 절에서 뺀다.</summary>
        private static readonly AngleSpec[] SolidityAngles =
        {
            AngleSpec.FromOrbit("gameplay", 45f, -30f, 4.0f, new Vector3(0f, 0.3f, 0f), 50f),
            AngleSpec.FromOrbit("sidealong", 12f, 90f, 4.2f, new Vector3(0f, 0.5f, 0f), 50f),
            AngleSpec.FromOrbit("sideacross", 12f, 0f, 4.2f, new Vector3(0f, 0.5f, 0f), 50f),
        };

        [MenuItem("Tools/Seoul Playup/Combat/Capture VFX Solidity Bench (입체감·더미 Q63~)")]
        public static void CaptureSolidityMenu()
        {
            Debug.Log(CaptureSolidity("Temp/VfxCelTuning", false));
        }

        /// <summary>probe=true면 각 조합의 정지 컷 2장만(t 0.14/0.30 — 교차축 방향 검증용),
        /// false면 60fps 37프레임 시퀀스. 더미(출하 Player 프리팹)가 임팩트 지점에 선다.</summary>
        public static string CaptureSolidity(string outputFolder, bool probe)
        {
            outputFolder = string.IsNullOrWhiteSpace(outputFolder) ? "Temp/VfxCelTuning" : outputFolder;
            Directory.CreateDirectory(outputFolder);

            var previousScene = SceneManager.GetActiveScene().path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var written = new List<string>();
            try
            {
                var stageGo = new GameObject("Solidity Bench Stage");
                var stage = stageGo.AddComponent<VfxLabStage>();
                stage.EnsureBoard();

                var camGo = new GameObject("Capture Camera");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.043f, 0.067f, 0.122f);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;
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

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClawSlashPrefab);
                var dummyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DummyPlayerPrefab);
                if (prefab == null || dummyPrefab == null)
                {
                    return "VfxStyleRefineBench: 프리팹을 못 찾았다 — "
                        + (prefab == null ? ClawSlashPrefab : DummyPlayerPrefab);
                }

                // 피격 더미 — 임팩트 지점(원점 칸)에 세우고 실게임 카메라 쪽을 보게 한다.
                var dummy = (GameObject)PrefabUtility.InstantiatePrefab(dummyPrefab);
                dummy.transform.SetPositionAndRotation(
                    new Vector3(0f, 0.02f, 0f), Quaternion.Euler(0f, 150f, 0f));

                const int frameWidth = 420;
                const int frameHeight = 280;

                foreach (var angle in SolidityAngles)
                {
                    foreach (var (variantId, rolls) in SolidityVariants)
                    {
                        var ship = ColorSpecs[0];
                        if (probe)
                        {
                            foreach (var t in new[] { 0.14f, 0.30f })
                            {
                                var frame = CaptureSolidityFrame(cam, prefab, ship, rolls, t,
                                    frameWidth, frameHeight, volumeGo, angle);
                                var file = Path.Combine(outputFolder,
                                    $"probe_{angle.Id}_{variantId}_t{(int)(t * 100):00}.png");
                                File.WriteAllBytes(file, frame.EncodeToPNG());
                                Object.DestroyImmediate(frame);
                                written.Add(file);
                            }

                            continue;
                        }

                        var seqDir = Path.Combine(outputFolder, $"seq_solid_{angle.Id}_{variantId}");
                        Directory.CreateDirectory(seqDir);
                        for (var f = 0; f < 37; f++)
                        {
                            var frame = CaptureSolidityFrame(cam, prefab, ship, rolls, f / 60f,
                                frameWidth, frameHeight, volumeGo, angle);
                            var file = Path.Combine(seqDir, $"f_{f:000}.png");
                            File.WriteAllBytes(file, frame.EncodeToPNG());
                            Object.DestroyImmediate(frame);
                            written.Add(file);
                        }
                    }
                }

                Object.DestroyImmediate(dummy);
                Object.DestroyImmediate(camGo);
                if (volumeGo != null)
                {
                    Object.DestroyImmediate(volumeGo);
                }

                Object.DestroyImmediate(stageGo);
            }
            finally
            {
                Shader.SetGlobalFloat("_VfxSimTime", 0f);
                var restoreTo = !string.IsNullOrEmpty(previousScene) && File.Exists(previousScene)
                    ? previousScene
                    : FallbackScenePath;
                if (File.Exists(restoreTo))
                {
                    EditorSceneManager.OpenScene(restoreTo, OpenSceneMode.Single);
                }
            }

            return $"VfxStyleRefineBench(입체감): {written.Count}장 → {outputFolder}";
        }

        private static Texture2D CaptureSolidityFrame(
            Camera cam, GameObject prefab, in TuningSpec spec, float[] rolls, float time,
            int width, int height, GameObject volumeGo, AngleSpec angle)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, 0f), Quaternion.identity);

            if (rolls.Length > 0)
            {
                AddCrossedArcs(go, rolls);
            }

            var created = new List<Material>();
            ApplyTuningOverrides(go, spec, created);

            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
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

                ps.Simulate(time, true, true);
            }

            Shader.SetGlobalFloat("_VfxSimTime", time);

            cam.transform.SetPositionAndRotation(angle.Position, angle.Rotation);
            cam.fieldOfView = angle.Fov;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);

            Object.DestroyImmediate(go);
            foreach (var m in created)
            {
                Object.DestroyImmediate(m);
            }

            return tex;
        }

        // ── 색 조합 매트릭스 절(Q65 — 사용자: "색감을 여러가지 조합해서 비교").
        //    몸통 3단(현행 가족색/중간 검붉음/검붉음) × 코어 3단(흰/회색/검정) = 9조합.
        //    림은 몸통 단계에 연동(현행 2.4 / #B03A35 2.0 / #A6362F 1.8) — 몸통만 어둡고 림이
        //    쨍하면 조합이 아니라 테두리 판정이 된다. 터짐 분리(Q63 후속) 「이후」의 참격이라
        //    §11 구 영상(섬광 포함)과 달리 획 색만 남는다. 피격 더미 포함(규약).

        private static readonly (string Id, Color Body, Color Rim, float RimBoost)[] MatrixBodies =
        {
            ("a", default, default, -1f),                                          // 현행 #E0524E
            ("b", new Color(0.702f, 0.227f, 0.208f), new Color(0.690f, 0.227f, 0.208f), 2.0f), // #B33A35
            ("c", new Color(0.557f, 0.137f, 0.125f), new Color(0.651f, 0.212f, 0.184f), 1.8f), // #8E2320
        };

        private static readonly (string Id, Color Core, float CoreBoost)[] MatrixCores =
        {
            ("white", default, -1f),                                  // 현행(흰쪽 30%·부스트 1.5)
            ("gray", new Color(0.62f, 0.58f, 0.57f), 1.0f),           // 중간 명도 — 블룸 문턱 아래
            ("black", new Color(0.08f, 0.06f, 0.07f), 1.0f),          // 명암 반전
        };

        [MenuItem("Tools/Seoul Playup/Combat/Capture VFX Color Matrix (색 조합 Q65~)")]
        public static void CaptureColorMatrixMenu()
        {
            Debug.Log(CaptureColorMatrix("Temp/VfxCelTuning", false));
        }

        /// <summary>probe=true면 각 조합 정지 컷(t 0.30)만. false면 실게임 각도 37f 시퀀스 +
        /// 부감 정지 컷. 시퀀스 폴더 = seq_matrix_&lt;body&gt;_&lt;core&gt;.</summary>
        public static string CaptureColorMatrix(string outputFolder, bool probe)
        {
            outputFolder = string.IsNullOrWhiteSpace(outputFolder) ? "Temp/VfxCelTuning" : outputFolder;
            Directory.CreateDirectory(outputFolder);

            var previousScene = SceneManager.GetActiveScene().path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var written = new List<string>();
            try
            {
                var stageGo = new GameObject("Color Matrix Stage");
                var stage = stageGo.AddComponent<VfxLabStage>();
                stage.EnsureBoard();

                var camGo = new GameObject("Capture Camera");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.043f, 0.067f, 0.122f);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;
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

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClawSlashPrefab);
                var dummyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DummyPlayerPrefab);
                if (prefab == null || dummyPrefab == null)
                {
                    return "VfxStyleRefineBench: 프리팹을 못 찾았다 — "
                        + (prefab == null ? ClawSlashPrefab : DummyPlayerPrefab);
                }

                var dummy = (GameObject)PrefabUtility.InstantiatePrefab(dummyPrefab);
                dummy.transform.SetPositionAndRotation(
                    new Vector3(0f, 0.02f, 0f), Quaternion.Euler(0f, 150f, 0f));

                const int frameWidth = 420;
                const int frameHeight = 280;
                var gameplay = SolidityAngles[0];
                var topdown = Angles[0];

                foreach (var (bodyId, body, rim, rimBoost) in MatrixBodies)
                {
                    foreach (var (coreId, core, coreBoost) in MatrixCores)
                    {
                        var spec = new TuningSpec(bodyId + "_" + coreId,
                            bodyColor: body, rimColor: rim, coreColor: core,
                            coreBoost: coreBoost, rimBoost: rimBoost);

                        // 부감 정지 컷(t=0.30 — 획이 꽉 찬 홀드 구간, 색이 가장 잘 읽힌다).
                        var still = CaptureTuningFrame(cam, prefab, spec, 0.30f,
                            frameWidth, frameHeight, volumeGo, topdown);
                        var stillFile = Path.Combine(outputFolder, $"matrix_{spec.Id}_top.png");
                        File.WriteAllBytes(stillFile, still.EncodeToPNG());
                        Object.DestroyImmediate(still);
                        written.Add(stillFile);

                        if (probe)
                        {
                            continue;
                        }

                        var seqDir = Path.Combine(outputFolder, $"seq_matrix_{spec.Id}");
                        Directory.CreateDirectory(seqDir);
                        for (var f = 0; f < 37; f++)
                        {
                            var frame = CaptureTuningFrame(cam, prefab, spec, f / 60f,
                                frameWidth, frameHeight, volumeGo, gameplay);
                            var file = Path.Combine(seqDir, $"f_{f:000}.png");
                            File.WriteAllBytes(file, frame.EncodeToPNG());
                            Object.DestroyImmediate(frame);
                            written.Add(file);
                        }
                    }
                }

                Object.DestroyImmediate(dummy);
                Object.DestroyImmediate(camGo);
                if (volumeGo != null)
                {
                    Object.DestroyImmediate(volumeGo);
                }

                Object.DestroyImmediate(stageGo);
            }
            finally
            {
                Shader.SetGlobalFloat("_VfxSimTime", 0f);
                var restoreTo = !string.IsNullOrEmpty(previousScene) && File.Exists(previousScene)
                    ? previousScene
                    : FallbackScenePath;
                if (File.Exists(restoreTo))
                {
                    EditorSceneManager.OpenScene(restoreTo, OpenSceneMode.Single);
                }
            }

            return $"VfxStyleRefineBench(색 매트릭스): {written.Count}장 → {outputFolder}";
        }

        // ── 3D 셸(스윕 곡면) 절(Q63 후속 — 교차 평면은 「한 번에 하나만」 원칙으로 기각).
        //    평면 리본의 단면(UV.y 축)에 아치 깊이를 줘 홈통형 3D 곡면으로 부풀린다 —
        //    VfxInkArcMeshBaker 단면 확장의 인메모리 목업. 면이 하나뿐이라 과밀하지 않으면서
        //    저각에서도 부피가 선다. 채택 시 베이커에 단면 프로파일로 이식.

        private static readonly (string Id, float Depth)[] ShellVariants =
        {
            ("flat", 0f),
            ("shell_low", 0.10f),
            ("shell_high", 0.22f),
        };

        [MenuItem("Tools/Seoul Playup/Combat/Capture VFX Shell Bench (3D 곡면 Q64~)")]
        public static void CaptureShellMenu()
        {
            Debug.Log(CaptureShell("Temp/VfxCelTuning", false));
        }

        /// <summary>probe=true면 정지 컷 2장(t 0.14/0.30)만. 더미 포함, 각도는 입체감 절과 동일.</summary>
        public static string CaptureShell(string outputFolder, bool probe)
        {
            outputFolder = string.IsNullOrWhiteSpace(outputFolder) ? "Temp/VfxCelTuning" : outputFolder;
            Directory.CreateDirectory(outputFolder);

            var previousScene = SceneManager.GetActiveScene().path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var written = new List<string>();
            try
            {
                var stageGo = new GameObject("Shell Bench Stage");
                var stage = stageGo.AddComponent<VfxLabStage>();
                stage.EnsureBoard();

                var camGo = new GameObject("Capture Camera");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.043f, 0.067f, 0.122f);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;
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

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClawSlashPrefab);
                var dummyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DummyPlayerPrefab);
                if (prefab == null || dummyPrefab == null)
                {
                    return "VfxStyleRefineBench: 프리팹을 못 찾았다 — "
                        + (prefab == null ? ClawSlashPrefab : DummyPlayerPrefab);
                }

                var dummy = (GameObject)PrefabUtility.InstantiatePrefab(dummyPrefab);
                dummy.transform.SetPositionAndRotation(
                    new Vector3(0f, 0.02f, 0f), Quaternion.Euler(0f, 150f, 0f));

                const int frameWidth = 420;
                const int frameHeight = 280;

                foreach (var angle in SolidityAngles)
                {
                    foreach (var (variantId, depth) in ShellVariants)
                    {
                        if (probe)
                        {
                            foreach (var t in new[] { 0.14f, 0.30f })
                            {
                                var frame = CaptureShellFrame(cam, prefab, depth, t,
                                    frameWidth, frameHeight, angle);
                                var file = Path.Combine(outputFolder,
                                    $"shellprobe_{angle.Id}_{variantId}_t{(int)(t * 100):00}.png");
                                File.WriteAllBytes(file, frame.EncodeToPNG());
                                Object.DestroyImmediate(frame);
                                written.Add(file);
                            }

                            continue;
                        }

                        var seqDir = Path.Combine(outputFolder, $"seq_shell_{angle.Id}_{variantId}");
                        Directory.CreateDirectory(seqDir);
                        for (var f = 0; f < 37; f++)
                        {
                            var frame = CaptureShellFrame(cam, prefab, depth, f / 60f,
                                frameWidth, frameHeight, angle);
                            var file = Path.Combine(seqDir, $"f_{f:000}.png");
                            File.WriteAllBytes(file, frame.EncodeToPNG());
                            Object.DestroyImmediate(frame);
                            written.Add(file);
                        }
                    }
                }

                Object.DestroyImmediate(dummy);
                Object.DestroyImmediate(camGo);
                if (volumeGo != null)
                {
                    Object.DestroyImmediate(volumeGo);
                }

                Object.DestroyImmediate(stageGo);
            }
            finally
            {
                Shader.SetGlobalFloat("_VfxSimTime", 0f);
                var restoreTo = !string.IsNullOrEmpty(previousScene) && File.Exists(previousScene)
                    ? previousScene
                    : FallbackScenePath;
                if (File.Exists(restoreTo))
                {
                    EditorSceneManager.OpenScene(restoreTo, OpenSceneMode.Single);
                }
            }

            return $"VfxStyleRefineBench(3D 셸): {written.Count}장 → {outputFolder}";
        }

        private static Texture2D CaptureShellFrame(
            Camera cam, GameObject prefab, float depth, float time,
            int width, int height, AngleSpec angle)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, 0f), Quaternion.identity);

            var createdMeshes = new List<Mesh>();
            if (depth > 0f)
            {
                ApplyShellMesh(go, depth, createdMeshes);
            }

            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
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

                ps.Simulate(time, true, true);
            }

            Shader.SetGlobalFloat("_VfxSimTime", time);

            cam.transform.SetPositionAndRotation(angle.Position, angle.Rotation);
            cam.fieldOfView = angle.Fov;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);

            Object.DestroyImmediate(go);
            foreach (var m in createdMeshes)
            {
                Object.DestroyImmediate(m);
            }

            return tex;
        }

        /// <summary>클론의 아크 메시(ink_arc_*)를 단면 경사(bank)로 기울인 사본으로 바꾼다.
        /// 🔴 실측: 리본 단면은 v ∈ {0, 1} <b>가장자리 두 줄뿐</b>(내부 정점 없음)이라
        /// sin(v·π) 아치는 양끝이 0이 되어 <b>원리적으로 무효</b>다(첫 시도 diff 0.0%로 확인).
        /// 그래서 파일럿은 경사 — 단면 중심선 둘레로 리본을 비틀어 세운다. 아크 곡률과 결합하면
        /// 갓(lampshade)형 3D 곡면이 된다. 정식 아치 단면은 베이커에서 단면 정점을 추가해야
        /// 한다(채택 시 이식 항목). UV.y = 단면 축(Band 규약), 로컬 +Z = 월드 위쪽.
        /// 에셋 무접촉: 사본 메시는 캡처 후 파기. 🔴Bounds 재계산 필수(안 하면 컬링).</summary>
        private static void ApplyShellMesh(GameObject go, float depth, List<Mesh> created)
        {
            foreach (var r in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (r.renderMode != ParticleSystemRenderMode.Mesh || r.mesh == null)
                {
                    continue;
                }

                if (!r.mesh.name.StartsWith("ink_arc"))
                {
                    continue;
                }

                var clone = Object.Instantiate(r.mesh);
                clone.name = r.mesh.name + " (shell)";
                var verts = clone.vertices;
                var uvs = clone.uv;
                for (var i = 0; i < verts.Length; i++)
                {
                    verts[i].z += (Mathf.Clamp01(uvs[i].y) - 0.5f) * depth * 2f;
                }

                clone.vertices = verts;
                clone.RecalculateNormals();
                clone.RecalculateBounds();
                r.mesh = clone;
                created.Add(clone);
            }
        }

        /// <summary>아크 두 시스템을 로컬 X 둘레로 회전 복제한다. 🔴시드 고정 «앞»에 불러야
        /// 복제본도 같은 추첨을 받는다. 겹침부는 밝기가 겹으로 오르는 목업 한계가 있다(캡션에 명기).</summary>
        private static void AddCrossedArcs(GameObject go, float[] rolls)
        {
            var targets = new List<Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "slash_alp" || t.name == "slash_add")
                {
                    targets.Add(t);
                }
            }

            foreach (var t in targets)
            {
                foreach (var roll in rolls)
                {
                    var copy = Object.Instantiate(t.gameObject, t.parent);
                    copy.name = t.name + "_x" + (int)roll;
                    copy.transform.localPosition = t.localPosition;
                    copy.transform.localRotation = t.localRotation * Quaternion.Euler(roll, 0f, 0f);
                    copy.transform.localScale = t.localScale;
                }
            }
        }

        /// <summary>클론 인스턴스의 먹 머티리얼을 튜닝 사본으로 바꾼다. 분류는 머티리얼 이름으로:
        /// <c>Ink_ImpactFlash</c> = 섬광 · <c>_Soft</c> 포함 = 면제 계열(이미 가라앉아 있어 건너뜀) ·
        /// 나머지(아크·속도선) = 몸통 계열. 에셋 무접촉 — 사본은 캡처 후 파기.</summary>
        private static void ApplyTuningOverrides(GameObject go, in TuningSpec spec, List<Material> created)
        {
            foreach (var renderer in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                var mats = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader == null || m.shader.name != InkShaderName)
                    {
                        continue;
                    }

                    if (m.name.Contains("_Soft"))
                    {
                        continue;
                    }

                    var clone = new Material(m);
                    clone.name = m.name + " (" + spec.Id + ")";
                    if (m.name.Contains("ImpactFlash"))
                    {
                        if (spec.FlashColor.a > 0f)
                        {
                            clone.SetColor("_Color", spec.FlashColor);
                        }
                    }
                    else
                    {
                        if (spec.BodyColor.a > 0f)
                        {
                            clone.SetColor("_Color", spec.BodyColor);
                        }

                        if (spec.RimColor.a > 0f)
                        {
                            clone.SetColor("_RimColor", spec.RimColor);
                        }

                        if (spec.CoreColor.a > 0f)
                        {
                            clone.SetColor("_CoreColor", spec.CoreColor);
                        }

                        if (spec.CoreBoost >= 0f)
                        {
                            clone.SetFloat("_CoreBoost", spec.CoreBoost);
                        }

                        if (spec.CoreWidth >= 0f)
                        {
                            clone.SetFloat("_CoreWidth", spec.CoreWidth);
                        }

                        if (spec.RimBoost >= 0f)
                        {
                            clone.SetFloat("_RimBoost", spec.RimBoost);
                        }
                    }

                    mats[i] = clone;
                    created.Add(clone);
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = mats;
                }
            }
        }
    }
}
