using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.EditorTools.Dev
{
    /// <summary>
    /// 스타일 축 실플레이 판정(Q58) 전용 — 참격 아크 스타일 전환 메뉴.
    ///
    /// <para>참격(ClawSlash) 아크 머티리얼을 [현행 P1 비백 / ⓑ 볼드&클린(반입판·정본) /
    /// ⓒ 셀 카툰] 셋 중 하나로 갈아 끼운다. <b>리빌드를 돌리지 않는다</b> — 플레이 사이에
    /// 리빌드를 끼우면 「플레이 직후 리빌드 금지」(인메모리 팩 오염) 지뢰를 밟는다.
    /// 바꾸는 것은 프리팹의 아크 렌더러 머티리얼 참조 2곳뿐이라 전환이 결정적이다.</para>
    ///
    /// <para>🔴 판정이 끝나면 <b>Use B Bold(정본)</b>로 되돌린 상태가 체크인 기준이다.
    /// 파일럿 머티리얼(PilotP1·PilotCel)은 판정 후 삭제 예정 — 🔴 <b>파일럿 상태로 빌더
    /// Rebuild를 돌리지 말 것</b>(Prune이 파일럿 머티리얼을 지워 참조가 끊긴다).</para>
    /// </summary>
    public static class VfxStylePilotSwitch
    {
        private const string InkShaderName = "SeoulPlayup/Ink Particle";
        private const string Prefab = "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_ClawSlash.prefab";
        private const string MaterialFolder = "Assets/Prefabs/Vfx/Combat/Materials";

        // Q58 재판정부터 정본 = ⓒ 셀(StrokeCel). 파일럿 사본은 여기서 파생한다.
        private const string CanonMat = MaterialFolder + "/Ink_ClawSlash_StrokeCel_K_Sweep.mat";
        private const string P1Mat = MaterialFolder + "/Ink_ClawSlash_PilotP1.mat";
        private const string BoldPilotMat = MaterialFolder + "/Ink_ClawSlash_PilotBold.mat";

        private const string P1Df = "Assets/Art/VFX/InkMasks/ink_gen_stroke_df.png";
        private const string P1Grain = "Assets/Art/VFX/InkMasks/ink_gen_stroke_grain.png";
        private const string BoldDf = "Assets/Art/VFX/InkMasks/ink_gen_stroke_bold_df.png";
        private const string BoldGrain = "Assets/Art/VFX/InkMasks/ink_gen_stroke_bold_grain.png";

        [MenuItem("Tools/Seoul Playup/Combat/Style Pilot/Use P1 (현행 비백)")]
        public static void UseP1()
        {
            Apply(EnsureP1());
        }

        [MenuItem("Tools/Seoul Playup/Combat/Style Pilot/Use B Bold (먹 볼드)")]
        public static void UseBold()
        {
            // ⓑ 파일럿 당시 값(cs:1095): bold 그림 + 그레인 0.22 + boiling 0.07 + 계단 8(확정 사양).
            Apply(EnsurePilot(BoldPilotMat, BoldDf, BoldGrain, inkBlend: 0.22f, boil: 0.07f, posterize: 8f));
        }

        [MenuItem("Tools/Seoul Playup/Combat/Style Pilot/Use C Cel (셀 완성형·정본)")]
        public static void UseCel()
        {
            Apply(AssetDatabase.LoadAssetAtPath<Material>(CanonMat));
        }

        [MenuItem("Tools/Seoul Playup/Combat/Style Pilot/Report Current")]
        public static void Report()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            foreach (var mat in ArcMaterials(prefab))
            {
                Debug.Log("[StylePilot] 아크 머티리얼 = " + mat.name
                          + " · _MainTex = " + (mat.GetTexture("_MainTex") ? mat.GetTexture("_MainTex").name : "없음"));
                return;
            }

            Debug.LogWarning("[StylePilot] 아크 머티리얼을 못 찾았다: " + Prefab);
        }

        private static Material EnsureP1()
        {
            // P1 원본(StrokeGen) 머티리얼은 반입 때 Prune으로 지워졌다 — 정본 사양은
            // 「정본 사본 + P1 그림 + _InkBlend 0.34 · _BoilStrength 0.10 · 계단 8」로 재구성
            // (cs:1089 반입 당시 아크 블록 값 그대로).
            return EnsurePilot(P1Mat, P1Df, P1Grain, inkBlend: 0.34f, boil: 0.10f, posterize: 8f);
        }

        private static Material EnsurePilot(
            string path, string dfPath, string grainPath, float inkBlend, float boil, float posterize)
        {
            var canon = AssetDatabase.LoadAssetAtPath<Material>(CanonMat);
            if (canon == null)
            {
                Debug.LogError("[StylePilot] 정본 머티리얼이 없다: " + CanonMat);
                return null;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(canon);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.CopyPropertiesFromMaterial(canon);
            }

            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture>(dfPath));
            if (grainPath != null)
            {
                mat.SetTexture("_InkTex", AssetDatabase.LoadAssetAtPath<Texture>(grainPath));
            }

            if (inkBlend >= 0f)
            {
                mat.SetFloat("_InkBlend", inkBlend);
            }

            if (boil >= 0f)
            {
                mat.SetFloat("_BoilStrength", boil);
            }

            if (posterize >= 0f)
            {
                mat.SetFloat("_Posterize", posterize);
            }

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        private static void Apply(Material target)
        {
            if (target == null)
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[StylePilot] 플레이 중 전환 금지 — 정지 후 전환할 것"
                               + "(프리팹 저장이 플레이 상태와 섞인다).");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(Prefab);
            try
            {
                var swapped = 0;
                foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    var mats = renderer.sharedMaterials;
                    var changed = false;
                    for (var i = 0; i < mats.Length; i++)
                    {
                        if (IsArcMaterial(mats[i]))
                        {
                            mats[i] = target;
                            changed = true;
                            swapped++;
                        }
                    }

                    if (changed)
                    {
                        renderer.sharedMaterials = mats;
                    }
                }

                if (swapped == 0)
                {
                    Debug.LogError("[StylePilot] 아크 렌더러를 못 찾았다 — 전환 실패.");
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, Prefab);
                Debug.Log("[StylePilot] 전환 완료 → " + target.name + " (참조 " + swapped + "곳). "
                          + "플레이로 확인할 것. 🔴 파일럿 상태로 Rebuild 금지.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static IEnumerable<Material> ArcMaterials(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (IsArcMaterial(mat))
                    {
                        yield return mat;
                    }
                }
            }
        }

        /// <summary>아크 머티리얼 판별 — 훑기가 켜진 잉크 머티리얼은 아크 두 시스템뿐이다.</summary>
        private static bool IsArcMaterial(Material mat)
        {
            return mat != null && mat.shader != null && mat.shader.name == InkShaderName
                   && mat.HasProperty("_SweepEnable") && mat.GetFloat("_SweepEnable") >= 0.5f;
        }
    }
}
