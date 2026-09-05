#if UNITY_EDITOR
using System.IO;
using SeoulPlayup.Combat.Unity.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SeoulPlayup.EditorTools.Dev
{
    /// <summary>
    /// <b>손으로 VFX를 저작하는 작업 씬</b>을 만든다(2026-08-10 신설).
    ///
    /// <para>🔴🔴 <b>왜 이 씬이 필요한가.</b> <c>Assets/Prefabs/Vfx/Combat/</c>의 프리팹 11개는
    /// <see cref="SeoulPlayup.EditorTools.Combat.MonsterAttackVfxBuilder"/>가 <b>생성</b>하는 산출물이라,
    /// 리빌드 메뉴를 한 번 돌리면 손으로 고친 내용이 통째로 덮어써지고
    /// <c>PruneOrphanMaterials</c>가 그 폴더의 「이번 리빌드가 만들지 않은 머티리얼」을 <b>지운다</b>
    /// (실측 2회). 그래서 손 저작물은 <b>반드시 <see cref="AuthoredFolder"/> 아래</b>에 둔다 —
    /// 빌더는 그 경로를 쓰지도 읽지도 않는다.</para>
    ///
    /// <para>🔑 <b>이 씬이 실게임과 같게 맞춰 두는 것 셋.</b> 저작 중에 보이는 그림이 전투 화면과
    /// 달라지면 저작이 무의미해진다.
    /// ① <b>카메라</b> pitch 45° · yaw −30° · FOV 50(출하 전투 씬 실측값) ·
    /// ② <b>블룸</b> 출하 볼륨 프로파일(문턱 0.75 — 이 위로 올라간 밝기만 빛난다) ·
    /// ③ <b>보드</b> 셀 피치 1.732u의 실제 헥스판(한 칸 크기의 기준).</para>
    ///
    /// <para>⚠️ 조명은 세우지 않는다. VFX 셰이더는 언릿이라 조명을 타지 않고, 손으로 세운 조명은
    /// 출하 화면과 다른 그림을 만들어 판단을 흐린다. 피격 더미가 어둡게 보이는 것은 정상이다.</para>
    /// </summary>
    public static class VfxAuthoringSceneBuilder
    {
        public const string AuthoredFolder = "Assets/Prefabs/Vfx/Authored";

        private const string ScenePath = "Assets/Scenes/Dev/VfxAuthoring.unity";
        private const string ShippingVolumeProfile = "Assets/Art/Lookdev/ArtLookdevVolumeProfile.asset";
        private const string DummyPlayerPrefab = "Assets/Art/Characters/player/Prefabs/Player.prefab";

        /// <summary>저작 루트의 이름. 여섯 방향 점검(<see cref="CycleHexDirection"/>)이 이걸 찾아 돌린다.</summary>
        private const string AuthorRootName = "AUTHOR HERE (+Z = 공격 방향)";

        /// <summary>실게임 전투 카메라 실측값 — 벤치(<see cref="VfxStyleRefineBench"/>)와 같은 자를 쓴다.</summary>
        private const float CameraPitch = 45f;
        private const float CameraYaw = -30f;
        private const float CameraFov = 50f;
        private const float CameraDistance = 8.2f;

        /// <summary>헥스 이웃 셀 중심 거리 = √3 × tileRadius(1). 한 칸 모듈의 크기 기준이다.</summary>
        private const float CellPitch = 1.7320508f;

        [MenuItem("Seoul Playup/Dev/Create VFX Authoring Scene")]
        public static void CreateOrUpdateScene()
        {
            EnsureAuthoredFolders();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 판 — 실제 헥스 보드. 한 칸이 얼마인지 눈으로 재는 자다.
            var stageGo = new GameObject("VFX Authoring Stage");
            stageGo.AddComponent<VfxLabStage>().EnsureBoard();

            // 카메라 — 출하 전투 씬과 같은 각·화각. 여기서 안 읽히면 게임에서도 안 읽힌다.
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.043f, 0.067f, 0.122f);
            cam.fieldOfView = CameraFov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.allowHDR = true;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;

            var rotation = Quaternion.Euler(CameraPitch, CameraYaw, 0f);
            var target = new Vector3(0f, 0.4f, CellPitch);
            camGo.transform.SetPositionAndRotation(
                target - rotation * Vector3.forward * CameraDistance, rotation);

            // 블룸 — 출하 프로파일 그대로. 🔴이게 없으면 「빛나는가」를 판단할 수 없다(문턱 0.75).
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ShippingVolumeProfile);
            if (profile != null)
            {
                var volumeGo = new GameObject("Shipping Volume (블룸 문턱 0.75)");
                var volume = volumeGo.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 1f;
                volume.sharedProfile = profile;
            }
            else
            {
                Debug.LogWarning("[VfxAuthoring] 출하 볼륨 프로파일을 못 찾았다 — 블룸 없이 만든다: "
                    + ShippingVolumeProfile);
            }

            // 더미 둘 — 연출의 크기·도달을 <b>사람 몸에 대고</b> 잰다(판정 영상 규약과 같은 이유).
            // 시전자 자리가 없으면 「어디서 출발하는 연출인지」가 눈에 안 보인다.
            var dummyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DummyPlayerPrefab);
            if (dummyPrefab != null)
            {
                var caster = (GameObject)PrefabUtility.InstantiatePrefab(dummyPrefab);
                caster.name = "Caster Dummy (원점 · 시전자 자리)";
                caster.transform.SetPositionAndRotation(
                    new Vector3(0f, 0.02f, 0f), Quaternion.identity); // +Z를 본다 = 공격 방향.

                var dummy = (GameObject)PrefabUtility.InstantiatePrefab(dummyPrefab);
                dummy.name = "Hit Dummy (1칸 앞)";
                dummy.transform.SetPositionAndRotation(
                    new Vector3(0f, 0.02f, CellPitch), Quaternion.Euler(0f, 180f, 0f));
            }

            // 저작 루트 — 만드는 파티클 시스템을 «이 아래»에 둔다. 로컬 +Z가 공격 방향이다.
            var authorRoot = new GameObject(AuthorRootName);
            authorRoot.transform.position = Vector3.zero;

            // 환경광 — 더미와 판이 형체로 보일 만큼만. 조명을 «세우는» 것이 아니라 씬 설정이라
            // 출하 화면에 없는 그림자·하이라이트를 만들지 않는다.
            // 🔑 <b>VFX 셰이더는 언릿이라 이 값에 전혀 영향받지 않는다</b> — 이펙트의 밝기 판단은
            // 이 값과 무관하게 유효하고, 바뀌는 것은 «배경과 더미»의 밝기뿐이다.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.30f, 0.34f, 0.44f);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes/Dev");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log("[VfxAuthoring] 저작 씬을 만들었다: " + ScenePath
                + "\n  저작물 보관 경로(빌더가 건드리지 않는다): " + AuthoredFolder
                + "\n  카메라 = 실게임 실측(pitch 45 · yaw -30 · FOV 50) · 블룸 문턱 0.75 · 셀 피치 1.732u");
        }

        /// <summary>
        /// 🔴🔴 <b>저작 중 반드시 한 번은 돌려 볼 것.</b> 공격 방향은 <b>여섯</b>인데 카메라는 고정이다 —
        /// 진행 방향으로 «세운» 평면(리본·쿼드)은 <b>절반의 방향에서 선으로 접혀 사라진다</b>(실측).
        /// 이 메뉴는 저작 루트를 60°씩 돌린다. 여섯 자리에서 다 읽히면 통과다.
        /// </summary>
        [MenuItem("Seoul Playup/Dev/VFX Authoring: 다음 헥스 방향으로 돌리기 (60도)")]
        public static void CycleHexDirection()
        {
            var root = GameObject.Find(AuthorRootName);
            if (root == null)
            {
                Debug.LogWarning("[VfxAuthoring] 저작 루트를 못 찾았다 — 저작 씬을 열었는지 확인할 것: "
                    + AuthorRootName);
                return;
            }

            Undo.RecordObject(root.transform, "Cycle Hex Direction");
            var yaw = Mathf.Round(root.transform.eulerAngles.y / 60f) * 60f + 60f;
            root.transform.rotation = Quaternion.Euler(0f, Mathf.Repeat(yaw, 360f), 0f);
            Debug.Log("[VfxAuthoring] 저작 루트 방향 = " + Mathf.Repeat(yaw, 360f) + "° (여섯 방향 중 하나)");
        }

        private static void EnsureAuthoredFolders()
        {
            EnsureFolder(AuthoredFolder);
            EnsureFolder(AuthoredFolder + "/Materials");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
