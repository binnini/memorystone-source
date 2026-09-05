using SeoulPlayup.Map.Runtime;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// stage_randomization*.csv 로더(placement-randomization-plan §2-3b). 정본 CSV는
    /// Resources 폴더에 있어 빌드에서도 직독된다(TrapPresetCatalog.LoadDefault와 같은 패턴 —
    /// 에디터에서는 AssetDatabase 폴백). 파싱은 순수 <see cref="StageRandomizationCsv"/>가 한다.
    /// </summary>
    public static class StageRandomizationProfileSource
    {
        private const string StagesResourcesPath = "stage_randomization";
        private const string PoolsResourcesPath = "stage_randomization_pools";
        private const string BansResourcesPath = "stage_randomization_bans";
        private const string StagesAssetPath = "Assets/Data/Map/Randomization/Resources/stage_randomization.csv";
        private const string PoolsAssetPath = "Assets/Data/Map/Randomization/Resources/stage_randomization_pools.csv";
        private const string BansAssetPath = "Assets/Data/Map/Randomization/Resources/stage_randomization_bans.csv";

        public static bool TryLoadProfile(string stageId, out StageRandomizationProfile profile, out string error)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(stageId))
            {
                error = "Stage id is empty.";
                return false;
            }

            var stagesText = LoadCsvText(StagesResourcesPath, StagesAssetPath);
            var poolsText = LoadCsvText(PoolsResourcesPath, PoolsAssetPath);
            if (stagesText == null || poolsText == null)
            {
                error = "stage_randomization CSVs not found (Resources).";
                return false;
            }

            // bans는 P2 선택적 표 — 없으면 금지 규칙 없이 진행한다(TryBuildProfile이 null을 허용).
            var bansText = LoadCsvText(BansResourcesPath, BansAssetPath);
            return StageRandomizationCsv.TryBuildProfile(stagesText, poolsText, bansText, stageId.Trim(), out profile, out error);
        }

        private static string LoadCsvText(string resourcesPath, string assetPath)
        {
            var textAsset = Resources.Load<TextAsset>(resourcesPath);
            if (textAsset != null)
            {
                return textAsset.text;
            }

#if UNITY_EDITOR
            var editorAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            return editorAsset != null ? editorAsset.text : null;
#else
            return null;
#endif
        }
    }
}
