using System.IO;
using System.Linq;
using SeoulPlayup.Combat.Unity;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.EditorTools.UI
{
    /// <summary>
    /// 보스 HUD 프리팹 저작 도구. 배치·크기·색의 정본은 <b>프리팹 에셋</b>이고 런타임
    /// (<see cref="BossHudView"/>)은 값만 채운다 — 런타임이 레이아웃을 하드코딩하면 디자이너가
    /// 프리팹에서 보는 모습과 실제 플레이가 갈라진다.
    ///
    /// 이 빌더는 <b>초기 저작본을 만드는 용도</b>다. 한 번 만든 뒤 디자이너가 프리팹을 직접 손보므로,
    /// 재실행은 저작을 덮어쓴다(그래서 확인 대화상자를 띄운다).
    /// </summary>
    public static class BossHudPrefabBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/BossHud.prefab";
        private const string MediumFontPath = "Assets/Resources/Fonts/DNFForgedBlade-Medium SDF.asset";
        private const string LightFontPath = "Assets/Resources/Fonts/DNFForgedBlade-Light SDF.asset";

        // 테마 토큰: 인디고 판 + 금테(TurnPhaseDockView의 배너 스킨과 같은 계열).
        private static readonly Color PlateColor = new Color32(0x1B, 0x24, 0x38, 0xC4);
        private static readonly Color BorderColor = new Color32(0xC9, 0xA2, 0x27, 0xB3);
        private static readonly Color HealthTrackColor = new Color32(0x0E, 0x12, 0x1C, 0xF0);
        private static readonly Color HealthFillColor = new Color32(0xE0, 0x3A, 0x24, 0xFF);
        private static readonly Color MetricTrackColor = new Color32(0x11, 0x18, 0x28, 0xE6);
        private static readonly Color MetricFillColor = new Color32(0x7E, 0xC8, 0xFF, 0xFF);
        private static readonly Color BossTagColor = new Color(1f, 0.08f, 0.04f, 1f);
        private static readonly Color NameColor = new Color32(0xFF, 0xF2, 0xD0, 0xFF);
        private static readonly Color ValueColor = new Color32(0xE8, 0xEE, 0xF8, 0xFF);
        private static readonly Color PipOffColor = new Color32(0x3A, 0x42, 0x55, 0xFF);

        private const float BandWidth = 760f;
        private const float BandHeight = 96f;
        private const float BandTopMargin = 18f;
        private const float HealthBarWidth = 700f;
        private const float HealthBarHeight = 22f;
        private const float MetricBarWidth = 700f;
        private const float MetricBarHeight = 8f;
        private const int PhasePipCount = 3;
        private const float PipSize = 12f;
        private const float PipSpacing = 6f;

        [MenuItem("Tools/UI/Boss/Rebuild Boss HUD Prefab")]
        public static void RebuildPrefab()
        {
            if (File.Exists(PrefabPath) &&
                !EditorUtility.DisplayDialog(
                    "보스 HUD 프리팹 재생성",
                    $"{PrefabPath}를 생성 기본값으로 되돌립니다.\n프리팹에서 직접 조정한 배치·색·폰트는 사라집니다.\n계속할까요?",
                    "재생성", "취소"))
            {
                return;
            }

            BuildAndSave();
        }

        /// <summary>
        /// 확인 대화상자 없이 프리팹을 생성 기본값으로 저장한다. 같은 경로에 저장하므로 에셋 GUID가 유지되고
        /// 씬 인스턴스의 프리팹 링크도 끊기지 않는다(레이아웃 수정이 씬에 그대로 전파된다).
        /// </summary>
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
            Debug.Log($"[BossHud] Rebuilt prefab at {PrefabPath}.");
        }

        /// <summary>
        /// 열려 있는 씬의 <c>Gameplay UI Layers</c> 하위에 프리팹 인스턴스를 넣는다. 이미 있으면 아무것도
        /// 하지 않는다(중복 인스턴스가 두 개의 보스바로 보이는 사고를 막는다).
        /// </summary>
        [MenuItem("Tools/UI/Boss/Add Boss HUD To Open Scene")]
        public static void AddToOpenScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[BossHud] Prefab missing at {PrefabPath}. Run 'Rebuild Boss HUD Prefab' first.");
                return;
            }

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var gameplayLayers = scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<RectTransform>(includeInactive: true))
                .FirstOrDefault(rect => rect.name == GameplaySceneContract.GameplayLayerRootName);
            if (gameplayLayers == null)
            {
                Debug.LogError($"[BossHud] '{GameplaySceneContract.GameplayLayerRootName}' not found in the open scene.");
                return;
            }

            var existing = gameplayLayers.GetComponentsInChildren<BossHudView>(includeInactive: true).FirstOrDefault();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[BossHud] Scene already has a Boss HUD instance; nothing added.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, gameplayLayers);
            instance.name = BossHudView.RootObjectName;
            // 시네마틱 UI 하이더는 게임플레이 레이어 루트의 '직계 자식'만 관리하므로 여기에 붙여야
            // 인트로/승리 연출에서 함께 숨었다가 복원된다.
            instance.transform.SetAsLastSibling();
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[BossHud] Added Boss HUD instance under '{GameplaySceneContract.GameplayLayerRootName}'.");
        }

        private static GameObject BuildHierarchy()
        {
            var mediumFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MediumFontPath);
            var lightFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LightFontPath);

            var root = CreateRect(BossHudView.RootObjectName, null, out var rootRect);
            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, -BandTopMargin);
            rootRect.sizeDelta = new Vector2(BandWidth, BandHeight);
            root.AddComponent<CanvasGroup>();
            root.AddComponent<BossHudView>();

            // 배경 판(장식) — 인디고 판 + 금테 한 줄.
            CreateImage("BossHud_Plate", rootRect, PlateColor, out var plateRect);
            Stretch(plateRect);
            CreateImage("BossHud_Border", rootRect, BorderColor, out var borderRect);
            borderRect.anchorMin = new Vector2(0f, 0f);
            borderRect.anchorMax = new Vector2(1f, 0f);
            borderRect.pivot = new Vector2(0.5f, 0f);
            borderRect.offsetMin = new Vector2(0f, 0f);
            borderRect.offsetMax = new Vector2(0f, 2f);

            // 이름 + "보스" 태그(좌상단).
            // "보스" 태그와 이름은 <b>같은 줄</b>에 둔다. 아래 줄에 두면 바로 밑의 체력바에 가려 보이지 않는다
            // (첫 저작본에서 실제로 그렇게 되어 갤러리 캡처로 발견했다).
            var tag = CreateText(BossHudView.BossTagLabelName, rootRect, "보스", mediumFont, 18f, BossTagColor, TextAlignmentOptions.MidlineLeft, out var tagRect);
            tagRect.anchorMin = new Vector2(0f, 1f);
            tagRect.anchorMax = new Vector2(0f, 1f);
            tagRect.pivot = new Vector2(0f, 1f);
            tagRect.anchoredPosition = new Vector2(30f, -10f);
            tagRect.sizeDelta = new Vector2(56f, 28f);

            var name = CreateText(BossHudView.NameLabelName, rootRect, "보스 이름", mediumFont, 26f, NameColor, TextAlignmentOptions.MidlineLeft, out var nameRect);
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(0f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.anchoredPosition = new Vector2(94f, -8f);
            nameRect.sizeDelta = new Vector2(400f, 32f);

            // 페이즈 핍(우상단). 개수는 저작이 정본 — 런타임은 색만 바꾼다.
            var pipRoot = CreateRect(BossHudView.PhasePipRootName, rootRect, out var pipRootRect);
            pipRootRect.anchorMin = new Vector2(1f, 1f);
            pipRootRect.anchorMax = new Vector2(1f, 1f);
            pipRootRect.pivot = new Vector2(1f, 1f);
            pipRootRect.anchoredPosition = new Vector2(-30f, -12f);
            pipRootRect.sizeDelta = new Vector2(PhasePipCount * PipSize + (PhasePipCount - 1) * PipSpacing, PipSize);
            for (var i = 0; i < PhasePipCount; i++)
            {
                CreateImage($"BossHud_PhasePip{i + 1}", pipRootRect, PipOffColor, out var pipRect);
                pipRect.anchorMin = new Vector2(0f, 0.5f);
                pipRect.anchorMax = new Vector2(0f, 0.5f);
                pipRect.pivot = new Vector2(0f, 0.5f);
                pipRect.anchoredPosition = new Vector2(i * (PipSize + PipSpacing), 0f);
                pipRect.sizeDelta = new Vector2(PipSize, PipSize);
            }

            // 체력바: track에 MonsterHealthBarView가 붙어 셰이더를 구동하고, fill은 셰이더 부재 시 폴백.
            var healthTrack = CreateImage(BossHudView.HealthTrackName, rootRect, HealthTrackColor, out var healthTrackRect);
            healthTrackRect.anchorMin = new Vector2(0.5f, 1f);
            healthTrackRect.anchorMax = new Vector2(0.5f, 1f);
            healthTrackRect.pivot = new Vector2(0.5f, 1f);
            healthTrackRect.anchoredPosition = new Vector2(0f, -46f);
            healthTrackRect.sizeDelta = new Vector2(HealthBarWidth, HealthBarHeight);

            var healthFill = CreateImage(BossHudView.HealthFillName, healthTrackRect, HealthFillColor, out var healthFillRect);
            healthFillRect.anchorMin = new Vector2(0f, 0f);
            healthFillRect.anchorMax = new Vector2(0f, 1f);
            healthFillRect.pivot = new Vector2(0f, 0.5f);
            healthFillRect.anchoredPosition = Vector2.zero;
            healthFillRect.sizeDelta = new Vector2(HealthBarWidth, 0f);

            var healthValue = CreateText(BossHudView.HealthValueLabelName, healthTrackRect, "0 / 0", lightFont, 16f, ValueColor, TextAlignmentOptions.Midline, out var healthValueRect);
            Stretch(healthValueRect);

            // 페이즈 지표 게이지(체력바 아래 얇은 줄).
            var metricTrack = CreateImage(BossHudView.MetricTrackName, rootRect, MetricTrackColor, out var metricTrackRect);
            metricTrackRect.anchorMin = new Vector2(0.5f, 1f);
            metricTrackRect.anchorMax = new Vector2(0.5f, 1f);
            metricTrackRect.pivot = new Vector2(0.5f, 1f);
            metricTrackRect.anchoredPosition = new Vector2(0f, -72f);
            metricTrackRect.sizeDelta = new Vector2(MetricBarWidth, MetricBarHeight);

            var metricFill = CreateImage(BossHudView.MetricFillName, metricTrackRect, MetricFillColor, out var metricFillRect);
            metricFillRect.anchorMin = new Vector2(0f, 0f);
            metricFillRect.anchorMax = new Vector2(0f, 1f);
            metricFillRect.pivot = new Vector2(0f, 0.5f);
            metricFillRect.anchoredPosition = Vector2.zero;
            metricFillRect.sizeDelta = new Vector2(0f, 0f);

            var metricLabel = CreateText(BossHudView.MetricLabelName, rootRect, string.Empty, lightFont, 14f, ValueColor, TextAlignmentOptions.MidlineRight, out var metricLabelRect);
            metricLabelRect.anchorMin = new Vector2(1f, 1f);
            metricLabelRect.anchorMax = new Vector2(1f, 1f);
            metricLabelRect.pivot = new Vector2(1f, 1f);
            metricLabelRect.anchoredPosition = new Vector2(-30f, -80f);
            metricLabelRect.sizeDelta = new Vector2(260f, 20f);

            // 전멸기 카운트다운(§17). 지표 라벨과 같은 줄의 반대쪽 끝에 둔다 — 둘 다 "다음에 무슨 일이
            // 일어나는가"를 재는 값이고, 지표 라벨은 우측 정렬이라 자리가 겹치지 않는다.
            var annihilation = CreateText(
                BossHudView.AnnihilationLabelName, rootRect, string.Empty, mediumFont, 15f, ValueColor,
                TextAlignmentOptions.MidlineLeft, out var annihilationRect);
            annihilationRect.anchorMin = new Vector2(0f, 1f);
            annihilationRect.anchorMax = new Vector2(0f, 1f);
            annihilationRect.pivot = new Vector2(0f, 1f);
            annihilationRect.anchoredPosition = new Vector2(30f, -80f);
            annihilationRect.sizeDelta = new Vector2(360f, 20f);

            // 보스바는 클릭 대상이 아니다. 상단 밴드가 raycast를 먹으면 그 아래 맵 클릭이 죽는다.
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(includeInactive: true))
            {
                graphic.raycastTarget = false;
            }

            WireSerializedRefs(root, healthTrack, healthFill, healthValue, name, tag, pipRootRect, metricTrack, metricFill, metricLabel, annihilation);
            return root;
        }

        /// <summary>
        /// 직렬화 슬롯을 채운다. 런타임에도 이름 폴백이 있지만, 슬롯이 채워져 있으면 프리팹을 열었을 때
        /// 배선이 눈에 보이고 이름 변경에도 견딘다.
        /// </summary>
        private static void WireSerializedRefs(
            GameObject root,
            Image healthTrack,
            Image healthFill,
            TMP_Text healthValue,
            TMP_Text nameLabel,
            TMP_Text tagLabel,
            RectTransform pipRoot,
            Image metricTrack,
            Image metricFill,
            TMP_Text metricLabel,
            TMP_Text annihilationLabel)
        {
            var view = root.GetComponent<BossHudView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("rootGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
            serialized.FindProperty("nameLabel").objectReferenceValue = nameLabel;
            serialized.FindProperty("bossTagLabel").objectReferenceValue = tagLabel;
            serialized.FindProperty("healthTrackImage").objectReferenceValue = healthTrack;
            serialized.FindProperty("healthFillImage").objectReferenceValue = healthFill;
            serialized.FindProperty("healthValueLabel").objectReferenceValue = healthValue;
            serialized.FindProperty("phasePipRoot").objectReferenceValue = pipRoot;
            serialized.FindProperty("metricTrackImage").objectReferenceValue = metricTrack;
            serialized.FindProperty("metricFillRect").objectReferenceValue = metricFill.rectTransform;
            serialized.FindProperty("metricLabel").objectReferenceValue = metricLabel;
            serialized.FindProperty("annihilationLabel").objectReferenceValue = annihilationLabel;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreateRect(string name, RectTransform parent, out RectTransform rect)
        {
            var go = new GameObject(name, typeof(RectTransform));
            rect = (RectTransform)go.transform;
            if (parent != null)
            {
                rect.SetParent(parent, worldPositionStays: false);
            }

            rect.localScale = Vector3.one;
            return go;
        }

        private static Image CreateImage(string name, RectTransform parent, Color color, out RectTransform rect)
        {
            var go = CreateRect(name, parent, out rect);
            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static TMP_Text CreateText(
            string name,
            RectTransform parent,
            string text,
            TMP_FontAsset font,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment,
            out RectTransform rect)
        {
            var go = CreateRect(name, parent, out rect);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = alignment;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            if (font != null)
            {
                label.font = font;
            }

            return label;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
