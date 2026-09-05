using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SeoulPlayup.EditorTools.Dev
{
    /// <summary>
    /// 몬스터 공격 애니메이션과 VFX 큐를 <b>같은 시각으로 맞춰</b> 정지 프레임으로 굽는다.
    /// <b>플레이 모드가 필요 없다</b> — 플레이 모드에 들어가면 MCP 브리지가 끊겨 에이전트가
    /// 랩을 몰 수 없으므로, "애니와 함께 보며 각도·크기를 맞추는" 작업을 자동화하려면
    /// 에디트 모드 샘플링 경로가 필요하다.
    ///
    /// <para>🔴🔴 <b>클립 샘플링에 함정이 둘 있다(둘 다 실제로 밟았다).</b>
    /// ①이 리그의 클립은 <b>제네릭</b>이다(커브 630개가 <c>Armature</c> 기준, avatar=NULL).
    /// 휴머노이드용 경로는 아무 일도 하지 않고, <see cref="AnimationClip.SampleAnimation"/>은
    /// 리그를 <b>바인드 포즈에 얼린 채</b> 조용히 성공한다.
    /// ②<see cref="AnimationMode.SampleAnimationClip"/>에는 프리팹 루트가 아니라
    /// <b>Animator가 붙은 게임오브젝트</b>를 줘야 한다 — 커브 경로가 Animator 루트 기준이라
    /// 한 단계만 어긋나도 <b>아무것도 매칭되지 않고 역시 바인드 포즈로 얼어붙는다.</b>
    /// 두 경우 다 예외가 없으므로 "값이 안 변하는지" 직접 확인해야 알 수 있다.</para>
    ///
    /// <para>접촉 시점은 저작값을 믿지 않고 <b>애니메이션에서 실측</b>한다 —
    /// 때리는 손의 순간 속도가 최대인 프레임. 그 프레임에 큐의 타격 비트를 맞춘다.</para>
    /// </summary>
    public static class MonsterVfxFilmstrip
    {
        /// <summary>한 벌 촬영에 필요한 입력. 몬스터·클립·큐를 바꿔 다른 패턴에도 쓴다.</summary>
        public sealed class Request
        {
            public string FbxPath;
            public string MonsterPrefabPath;
            public string ClipName;
            public string CuePrefabPath;
            public string OutputFolder;
            /// <summary>때리는 본 이름. 접촉 프레임을 이 본의 최대 속도로 찾는다.</summary>
            public string StrikeBoneName = "mixamorig:RightHand";
            /// <summary>큐 자체 타임라인에서 타격이 나는 시각(<c>ClawReveal.strikeTime</c>).</summary>
            public float CueStrikeTime = 0.22f;
            public float CueScale = 1f;
            public float CueOffsetY = 0f;
            /// <summary>비우면 접촉 기준으로 자동 배치.</summary>
            public float[] SampleTimes;
            public int Width = 900, Height = 640;
            /// <summary>공격 방향이 화면을 가로지르는 측면 시점. 출하 카메라(pitch45·yaw−30)는
            /// 시전자 뒤통수라 정렬 판정에 못 쓴다.</summary>
            public Vector3 CameraEuler = new Vector3(28f, 68f, 0f);
            public float CameraDistance = 7.6f;
            /// <summary>저작 무대는 캄캄하다. 실루엣 판독용 보조광이며 게임 룩이 아니다.</summary>
            public float FillLight = 0.55f;
        }

        [MenuItem("Tools/Seoul Playup/Dev/Capture Claw + Bulgasal Attack1 Filmstrip")]
        public static void CaptureClawMenu()
        {
            var req = new Request
            {
                FbxPath = "Assets/Art/Characters/monster/Bulgasal/bulgasal.fbx",
                MonsterPrefabPath = "Assets/Art/Characters/monster/Bulgasal/Prefabs/Bulgasal.prefab",
                ClipName = "bulgasal_attack1",
                CuePrefabPath = "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_ClawSlash.prefab",
                OutputFolder = "Temp/ClawFilmstrip",
            };
            Debug.Log(Capture(req));
        }

        public static string Capture(Request req)
        {
            var sb = new StringBuilder();
            Directory.CreateDirectory(req.OutputFolder);

            var clip = LoadClip(req.FbxPath, req.ClipName);
            if (clip == null) return "clip not found: " + req.ClipName;
            var cuePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(req.CuePrefabPath);
            var monsterSrc = AssetDatabase.LoadAssetAtPath<GameObject>(req.MonsterPrefabPath);
            if (cuePrefab == null || monsterSrc == null) return "prefab not found";

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            GameObject caster = null, target = null;
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.name.StartsWith("Caster Dummy")) caster = r;
                if (r.name.StartsWith("Hit Dummy")) target = r;
            }
            Vector3 casterPos = caster != null ? caster.transform.position : Vector3.zero;
            Vector3 targetPos = target != null ? target.transform.position : new Vector3(0f, 0f, 1.73f);
            var dir = targetPos - casterPos; dir.y = 0f;
            var facing = Quaternion.LookRotation(dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward, Vector3.up);

            bool casterWasOn = caster != null && caster.activeSelf;
            if (caster != null) caster.SetActive(false);   // 몬스터가 시전자 자리를 대신한다

            var mon = (GameObject)PrefabUtility.InstantiatePrefab(monsterSrc);
            mon.name = "__filmstripMonster";
            mon.transform.SetPositionAndRotation(casterPos, facing);
            var animator = mon.GetComponentInChildren<Animator>(true);
            if (animator == null) { Object.DestroyImmediate(mon); return "no Animator on monster prefab"; }

            Transform strikeBone = null;
            foreach (var t in mon.GetComponentsInChildren<Transform>(true))
                if (t.name == req.StrikeBoneName) strikeBone = t;

            var camGo = new GameObject("__filmstripCam");
            var cam = camGo.AddComponent<Camera>();
            if (Camera.main != null) cam.CopyFrom(Camera.main);
            cam.fieldOfView = 50f; cam.allowHDR = true; cam.targetTexture = null;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var camRot = Quaternion.Euler(req.CameraEuler);
            var look = (casterPos + targetPos) * 0.5f + new Vector3(0f, 1.5f, 0f);
            camGo.transform.SetPositionAndRotation(look - (camRot * Vector3.forward) * req.CameraDistance, camRot);

            GameObject fillGo = null;
            if (req.FillLight > 0f)
            {
                fillGo = new GameObject("__filmstripFill");
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional;
                fill.intensity = req.FillLight;
                fill.color = new Color(0.72f, 0.78f, 0.95f);
                fillGo.transform.rotation = Quaternion.Euler(38f, 150f, 0f);
            }

            var rt = new RenderTexture(req.Width, req.Height, 24, RenderTextureFormat.DefaultHDR);
            var tex = new Texture2D(req.Width, req.Height, TextureFormat.RGB24, false);

            AnimationMode.StartAnimationMode();
            try
            {
                float contact = FindContact(animator.gameObject, clip, strikeBone, out float peakSpeed);
                sb.AppendLine("clip=" + clip.name + " len=" + clip.length.ToString("F2")
                    + " contact=" + contact.ToString("F2") + "s (peak strike-bone speed "
                    + peakSpeed.ToString("F1") + "u/s)");

                float cueStart = Mathf.Max(0f, contact - req.CueStrikeTime);
                var times = req.SampleTimes ?? new[]
                {
                    0f, contact * 0.5f, contact - 0.1f, contact,
                    contact + 0.12f, contact + 0.28f, contact + 0.45f, clip.length * 0.95f
                };

                for (int i = 0; i < times.Length; i++)
                {
                    float t = Mathf.Clamp(times[i], 0f, clip.length);
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(animator.gameObject, clip, t);
                    AnimationMode.EndSampling();

                    var cue = (GameObject)PrefabUtility.InstantiatePrefab(cuePrefab);
                    cue.name = "__filmstripCue";
                    cue.transform.SetPositionAndRotation(
                        targetPos + facing * new Vector3(0f, req.CueOffsetY, 0f), facing);
                    cue.transform.localScale = Vector3.one * req.CueScale;

                    float cueT = t - cueStart;
                    var reveal = cue.GetComponentInChildren<ClawReveal>(true);
                    if (reveal != null)
                    {
                        reveal.playing = false;
                        if (cueT < 0f)
                        {
                            foreach (var r in cue.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                        }
                        else reveal.ScrubAt(cueT);   // strokes AND particles at the same instant
                    }
                    else
                    {
                        // 큐가 순수 파티클이면 같은 시각으로 시뮬레이션한다
                        foreach (var ps in cue.GetComponentsInChildren<ParticleSystem>(true))
                            ps.Simulate(Mathf.Max(0.0001f, cueT), true, true, false);
                    }

                    Shoot(cam, rt, tex, Path.Combine(req.OutputFolder,
                        string.Format("f{0}_t{1:F2}.png", i, t)));
                    sb.AppendLine("  f" + i + " animT=" + t.ToString("F2") + " cueT=" + cueT.ToString("F2")
                        + (cueT < 0f ? " (cue not started)" : ""));
                    Object.DestroyImmediate(cue);
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                cam.targetTexture = null;
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(tex);
                if (fillGo != null) Object.DestroyImmediate(fillGo);
                rt.Release(); Object.DestroyImmediate(rt);
                Object.DestroyImmediate(mon);
                if (caster != null) caster.SetActive(casterWasOn);
            }
            sb.AppendLine("wrote to " + req.OutputFolder);
            return sb.ToString();
        }

        static AnimationClip LoadClip(string fbxPath, string clipName)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                var c = o as AnimationClip;
                if (c != null && c.name == clipName) return c;
            }
            return null;
        }

        /// <summary>접촉 = 때리는 본의 순간 속도가 최대인 프레임. 저작 메타(exitTime 등)는
        /// 전이용 값이라 "언제 맞았나"를 말해 주지 않는다.</summary>
        static float FindContact(GameObject animatorRoot, AnimationClip clip, Transform bone, out float peakSpeed)
        {
            peakSpeed = 0f;
            if (bone == null) return clip.length * 0.25f;
            float best = 0f, bestT = clip.length * 0.25f;
            Vector3 prev = Vector3.zero; bool first = true;
            const float step = 1f / 60f;
            for (float t = 0f; t <= clip.length; t += step)
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(animatorRoot, clip, t);
                AnimationMode.EndSampling();
                var p = bone.position;
                if (!first)
                {
                    float sp = (p - prev).magnitude / step;
                    if (sp > best) { best = sp; bestT = t; }
                }
                prev = p; first = false;
            }
            peakSpeed = best;
            return bestT;
        }

        static void Shoot(Camera cam, RenderTexture rt, Texture2D tex, string path)
        {
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
        }
    }
}
