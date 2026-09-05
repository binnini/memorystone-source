using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SeoulPlayup.Cards.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.EditorTools.UI
{
    /// <summary>
    /// 유물·소모품 아이콘 39장을 <see cref="RuntimeUiAssetCatalog"/>의 <c>itemIcons</c>에 굽는다.
    ///
    /// <para>
    /// 🔴 <b>스프라이트를 Resources 안에 두지 않는다.</b> <c>Resources.Load</c>로 얻는 것은 작은
    /// 카탈로그 에셋 하나뿐이고 아이콘은 그 에셋의 <b>직접 참조</b>로 실린다 —
    /// <see cref="SeoulPlayup.Codex.CodexThumbnailCatalog"/>가 지키는 것과 같은 "Resources 다이어트"
    /// 규약이다. 그래서 손으로 채우는 대신 이 도구가 폴더를 훑어 참조를 심는다.
    /// </para>
    ///
    /// <para>
    /// 🔑 키는 <b>스프라이트 파일 이름</b>이고 그것이 곧 CSV의 <c>iconId</c>다. 유물이 늘면
    /// CSV에 행을 더하고 같은 이름의 PNG를 폴더에 넣고 이 메뉴를 다시 돌리면 끝이다 —
    /// 코드도 저작 표면도 건드릴 것이 없다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ 이 도구는 <b>배선만</b> 한다. PNG가 Sprite로 임포트돼 있지 않으면(Texture Type이
    /// Default면) 아무것도 줍지 못한다 — 그때는 임포트 설정부터 고칠 것.
    /// </para>
    /// </summary>
    public static class RelicConsumableIconBaker
    {
        public const string RelicIconFolder = "Assets/Art/UI/Relics";
        public const string ConsumableIconFolder = "Assets/Art/UI/Consumables";

        private const string MenuPath = "Seoul Playup/UI/Bake Relic & Consumable Icons";

        [MenuItem(MenuPath)]
        public static void BakeFromMenu()
        {
            EditorUtility.DisplayDialog("아이콘 굽기", Bake(), "확인");
        }

        /// <summary>
        /// ⚠️ 여기서 <see cref="EditorUtility.DisplayDialog"/>를 부르지 않는다 — 이 도구는
        /// <c>script-execute</c>·테스트에서도 돌고, 모달 대화상자는 사람이 없는 그 경로에서
        /// <b>에디터를 통째로 멈춰 세운다</b>(실측: MCP 요청이 504로 죽었다). 결과는 문자열로
        /// 돌려주고, 대화상자는 메뉴 진입점만 띄운다.
        /// </summary>
        public static string Bake()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<RuntimeUiAssetCatalog>(RuntimeUiAssetCatalog.AssetPath);
            if (catalog == null)
            {
                return $"런타임 UI 에셋 카탈로그를 찾지 못했다: {RuntimeUiAssetCatalog.AssetPath}";
            }

            var sprites = CollectSprites(out var missingFolders);

            var serialized = new SerializedObject(catalog);
            var list = serialized.FindProperty("itemIcons");
            list.arraySize = sprites.Count;
            for (var i = 0; i < sprites.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            RuntimeUiAssetCatalog.InvalidateCache();

            var message = $"아이콘 {sprites.Count}장을 카탈로그에 배선했다.";
            if (missingFolders.Count > 0)
            {
                message += " 없는 폴더: " + string.Join(", ", missingFolders);
            }

            Debug.Log($"[RelicConsumableIconBaker] {message}", catalog);
            return message;
        }

        /// <summary>
        /// 두 폴더의 Sprite를 이름순으로 모은다. 이름이 겹치면 먼저 만난 쪽이 이긴다 —
        /// 카탈로그 해소기와 같은 규칙이라 목록과 화면이 갈라질 수 없다.
        /// </summary>
        public static List<Sprite> CollectSprites(out List<string> missingFolders)
        {
            missingFolders = new List<string>();
            var folders = new List<string>();
            foreach (var folder in new[] { RelicIconFolder, ConsumableIconFolder })
            {
                if (AssetDatabase.IsValidFolder(folder))
                {
                    folders.Add(folder);
                }
                else
                {
                    missingFolders.Add(folder);
                }
            }

            if (folders.Count == 0)
            {
                return new List<Sprite>();
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sprites = new List<Sprite>();
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", folders.ToArray()))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                // 한 PNG가 여러 Sprite로 잘려 있을 수 있으므로 서브에셋을 전부 훑는다.
                foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>())
                {
                    if (sprite == null || string.IsNullOrWhiteSpace(sprite.name) || !seen.Add(sprite.name.Trim()))
                    {
                        continue;
                    }

                    sprites.Add(sprite);
                }
            }

            return sprites.OrderBy(sprite => sprite.name, StringComparer.Ordinal).ToList();
        }
    }
}
