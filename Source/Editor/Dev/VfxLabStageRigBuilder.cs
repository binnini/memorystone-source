using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Combat.Unity.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeoulPlayup.EditorTools.Dev
{
    /// <summary>
    /// VFX 랩 씬에 <b>무대 리그</b>(<see cref="VfxLabStage"/> + 헥스 셀 기준자 보드)를 설치한다. 멱등하다.
    ///
    /// <para>왜 "씬 전체 재생성"이 아니라 "리그 설치"인가: 다른 랩들과 달리 이 씬은 저작 자산을
    /// 참조로 들고 있다(플레이어·몬스터 프리팹 배열, VFX 카탈로그). 코드로 전부 다시 엮으면 잘못된
    /// 프리팹을 물릴 위험이 실익보다 크다. 반면 무대 리그는 순수 절차 생성이라 재생성이 안전하고,
    /// 씬에 없던 부분이 정확히 이것이다.</para>
    ///
    /// <para>랩 컨트롤러는 무대가 없으면 런타임에 스스로 만들어 붙이므로 이 메뉴 없이도 동작한다.
    /// 다만 그때는 씬에 저장되지 않아 매번 새로 생기므로, 저작해 두려면 여기를 한 번 돌린다.</para>
    /// </summary>
    public static class VfxLabStageRigBuilder
    {
        private const string LabScenePath = "Assets/Scenes/Dev/VfxAnimationLab.unity";

        [MenuItem("Seoul Playup/Dev/Ensure VFX Lab Stage Rig")]
        public static void EnsureRig()
        {
            if (SceneManager.GetActiveScene().path != LabScenePath)
            {
                // 무인 실행에서는 <b>취소</b>가 기본이다 — 열려 있는 씬을 대신 닫는 것은
                // 되돌릴 수 없고, 자동 실행이 스스로 승인할 일이 아니다.
                if (!SeoulPlayup.EditorTools.EditorReportDialog.Confirm(
                        "VFX Lab Stage Rig",
                        $"현재 씬이 랩 씬이 아니다.\n{LabScenePath}를 열고 진행할까?",
                        "열고 진행", "취소", silentAnswer: false))
                {
                    return;
                }

                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                EditorSceneManager.OpenScene(LabScenePath, OpenSceneMode.Single);
            }

            var stage = Object.FindObjectOfType<VfxLabStage>();
            if (stage == null)
            {
                var go = new GameObject("VFX Lab Stage");
                stage = go.AddComponent<VfxLabStage>();
                Undo.RegisterCreatedObjectUndo(go, "Create VFX Lab Stage");
            }

            var presentation = Object.FindObjectOfType<EffectPresentationController>();
            if (presentation != null)
            {
                stage.SetPresentation(presentation);
            }

            stage.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            stage.RebuildBoard();
            EditorUtility.SetDirty(stage);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log(
                "[VfxLabStageRigBuilder] 무대 리그 설치 완료.\n"
                + $"  presentation={(presentation == null ? "<없음 — 씬에 EffectPresentationController가 필요하다>" : presentation.name)}\n"
                + $"  보드 반경={stage.BoardRadius}칸 · 셀 피치={VfxLabStage.CellPitch:0.###}u\n"
                + "  씬을 저장해야 저작으로 남는다.");
        }
    }
}
