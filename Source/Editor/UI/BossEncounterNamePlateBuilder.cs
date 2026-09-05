using System.IO;
using SeoulPlayup.Combat.Unity;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.EditorTools.UI
{
    /// <summary>
    /// 보스 조우 연출의 <b>이름 대형 표기</b> 프리팹 저작 도구. 배치·크기·색·폰트의 정본은 이 프리팹 에셋이고,
    /// 런타임(<see cref="BossEncounterNameView"/>)은 이름 문자열과 알파만 채운다(BossHudPrefabBuilder와 같은 규약).
    ///
    /// 조우 연출이 <see cref="Resources.Load"/>로 이 프리팹을 로드해 런타임에 인스턴스화하므로 전용 Canvas를
    /// 품는다(암전 커튼과 같은 방식이나, 이쪽은 저작된 텍스트 스타일이 있으니 프리팹으로 만든다 — P2 교훈).
    ///
    /// 초기 저작본 생성용. 재실행은 저작을 덮어쓰므로 확인 대화상자를 띄운다.
    /// </summary>
    public static class BossEncounterNamePlateBuilder
    {
        private const string PrefabPath = "Assets/Resources/UI/BossEncounterNamePlate.prefab";
        private const string MediumFontPath = "Assets/Resources/Fonts/DNFForgedBlade-Medium SDF.asset";

        // 암전 커튼(30000) 위에 그려 이름이 항상 최상단에 온다.
        private const int SortingOrder = 30001;

        // 테마: "보스" 태그는 마커 네임플레이트와 같은 보스 레드, 이름은 BossHud와 같은 크림.
        private static readonly Color BossTagColor = new Color(1f, 0.08f, 0.04f, 1f);
        private static readonly Color NameColor = new Color32(0xFF, 0xF2, 0xD0, 0xFF);
        private static readonly Color ScrimColor = new Color(0f, 0f, 0f, 0.42f);

        [MenuItem("Tools/UI/Boss/Rebuild Boss Encounter Name Plate")]
        public static void RebuildPrefab()
        {
            if (File.Exists(PrefabPath) &&
                !EditorUtility.DisplayDialog(
                    "보스 조우 이름 표기 프리팹 재생성",
                    $"{PrefabPath}를 생성 기본값으로 되돌립니다.\n프리팹에서 직접 조정한 배치·색·폰트는 사라집니다.\n계속할까요?",
                    "재생성", "취소"))
            {
                return;
            }

            BuildAndSave();
        }

        public static void BuildAndSave()
        {
            var directory = Path.GetDirectoryName(PrefabPath);
            if (!AssetDatabase.IsValidFolder(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            var root = BuildHierarchy();
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[BossEncounterNamePlate] Rebuilt prefab at {PrefabPath}.");
        }

        private static GameObject BuildHierarchy()
        {
            var mediumFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MediumFontPath);

            var root = new GameObject(BossEncounterNameView.RootObjectName, typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<CanvasGroup>();
            root.AddComponent<BossEncounterNameView>();
            var rootRect = (RectTransform)root.transform;

            // 가독성 스크림: 상단 전폭 어두운 띠(보스 위 밝은 배경에서도 이름이 읽히도록). 상단 보스 HUD
            // 밴드(위 ~114px)와 겹치지 않게 그 아래에서 시작한다.
            CreateImage("BossEncounterName_Scrim", rootRect, ScrimColor, out var scrimRect);
            scrimRect.anchorMin = new Vector2(0f, 1f);
            scrimRect.anchorMax = new Vector2(1f, 1f);
            scrimRect.pivot = new Vector2(0.5f, 1f);
            scrimRect.anchoredPosition = new Vector2(0f, -120f);
            scrimRect.sizeDelta = new Vector2(0f, 210f);

            // "보스" 태그(이름 위, 보스 레드). 상단 보스 HUD 바 아래에 둔다.
            var tag = CreateText(BossEncounterNameView.TagLabelName, rootRect, "보스", mediumFont, 34f, BossTagColor, out var tagRect);
            tagRect.anchorMin = new Vector2(0.5f, 1f);
            tagRect.anchorMax = new Vector2(0.5f, 1f);
            tagRect.pivot = new Vector2(0.5f, 1f);
            tagRect.anchoredPosition = new Vector2(0f, -140f);
            tagRect.sizeDelta = new Vector2(400f, 46f);

            // 보스 이름(대형).
            var name = CreateText(BossEncounterNameView.NameLabelName, rootRect, "보스 이름", mediumFont, 96f, NameColor, out var nameRect);
            nameRect.anchorMin = new Vector2(0.5f, 1f);
            nameRect.anchorMax = new Vector2(0.5f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.anchoredPosition = new Vector2(0f, -178f);
            nameRect.sizeDelta = new Vector2(1400f, 130f);

            // 이름 표기는 클릭 대상이 아니다.
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(includeInactive: true))
            {
                graphic.raycastTarget = false;
            }

            var view = root.GetComponent<BossEncounterNameView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("rootGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
            serialized.FindProperty("nameLabel").objectReferenceValue = name;
            serialized.FindProperty("tagLabel").objectReferenceValue = tag;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 로드 직후엔 숨김.
            root.GetComponent<CanvasGroup>().alpha = 0f;
            return root;
        }

        private static Image CreateImage(string name, RectTransform parent, Color color, out RectTransform rect)
        {
            var go = new GameObject(name, typeof(RectTransform));
            rect = (RectTransform)go.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.localScale = Vector3.one;
            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static TMP_Text CreateText(
            string name, RectTransform parent, string text, TMP_FontAsset font, float fontSize, Color color, out RectTransform rect)
        {
            var go = new GameObject(name, typeof(RectTransform));
            rect = (RectTransform)go.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.localScale = Vector3.one;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            if (font != null)
            {
                label.font = font;
            }

            return label;
        }
    }
}
