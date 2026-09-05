#nullable enable
using System;
using System.ComponentModel;
using AIGD;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using SeoulPlayup.MapDesign.Editor;
using UnityEditor;

namespace SeoulPlayup.Map.Unity.Editor.AiTools
{
    [AiToolType]
    public partial class Tool_HexMapValidate
    {
        [AiTool
        (
            "hex-map-validate",
            Title = "Hex Map / Validate",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [Description("맵 authoring 소스 에셋을 검증한다(목표 landmark·시작→목표 BFS 도달성·고립 walkable 섬·PlayerSpawn·" +
            "몬스터 스폰 참조·모든 셀 atlasVisualId 해소·atlas top prefab 완전성). 기존 HexMapValidationUtility를 " +
            "실제 출하 맵 에셋에 실행해 severity별 메시지로 반환하는 읽기 전용 감사. 씬 로드 없음.")]
        public HexMapValidateResult Validate
        (
            [Description("맵 authoring 소스 에셋 경로. 기본은 출하 맵.")]
            string mapSourceAssetPath = "Assets/Data/Map/Authoring/EastSeoulSource.asset",
            [Description("AtlasTileCatalog 에셋 경로. 빈 문자열이면 atlas visual 검증 생략.")]
            string atlasCatalogAssetPath = "Assets/Data/Map/Catalogs/AtlasTileCatalog.asset",
            [Description("HexTerrainPalette 에셋 경로. 빈 문자열이면 terrain id 검증 생략.")]
            string terrainPaletteAssetPath = "Assets/Data/Map/Authoring/DefaultHexTerrainPalette.asset"
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var result = new HexMapValidateResult
                {
                    mapSourcePath = mapSourceAssetPath,
                    atlasCatalogPath = atlasCatalogAssetPath,
                    terrainPalettePath = terrainPaletteAssetPath
                };

                var source = AssetDatabase.LoadAssetAtPath<HexSparseMapAuthoringSource>(mapSourceAssetPath);
                if (source == null)
                {
                    throw new InvalidOperationException(
                        $"No HexSparseMapAuthoringSource asset at '{mapSourceAssetPath}'.");
                }

                var palette = string.IsNullOrEmpty(terrainPaletteAssetPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<HexTerrainPalette>(terrainPaletteAssetPath);
                var knownTerrainIds = palette != null
                    ? HexMapValidationUtility.BuildKnownTerrainIds(palette)
                    : null;

                var atlas = string.IsNullOrEmpty(atlasCatalogAssetPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<AtlasTileCatalog>(atlasCatalogAssetPath);

                var report = atlas != null
                    ? HexMapValidationUtility.Validate(source, atlas, knownTerrainIds)
                    : HexMapValidationUtility.Validate(source, knownTerrainIds);

                foreach (var item in report.Items)
                {
                    switch (item.Severity)
                    {
                        case HexMapValidationSeverity.Error:
                            result.errors.Add(item.Message);
                            break;
                        case HexMapValidationSeverity.Warning:
                            result.warnings.Add(item.Message);
                            break;
                        default:
                            result.info.Add(item.Message);
                            break;
                    }
                }

                result.errorCount = result.errors.Count;
                result.warningCount = result.warnings.Count;
                result.infoCount = result.info.Count;
                result.hasErrors = report.HasErrors;
                result.hasWarnings = report.HasWarnings;
                return result;
            });
        }
    }
}
