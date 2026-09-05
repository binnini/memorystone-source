#if UNITY_EDITOR
using System;
using System.Linq;
using SeoulPlayup.Combat.Unity;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SeoulPlayup.Editor.UI
{
    public static class GameplayUiPrototypeTestApplier
    {
        private const string PrototypeScenePath = "Assets/Scenes/Dev/PrototypeTest.unity";
        private const string CardArtPrefabPath = "Assets/Prefabs/UI/Cards/CardFront.prefab";
        private const string MoveCardArtPrefabPath = "Assets/Prefabs/UI/Cards/CardFront_Move.prefab";
        private const string ActionCardArtPrefabPath = "Assets/Prefabs/UI/Cards/CardFront_Action.prefab";
        private const string CardIllustrationFolder = "Assets/Art/UI/Cards/Illust";
        private const string ChoiceOverlayRootName = "Choice Overlay Root";
        private const string GameplayEventSystemName = "PlayerState Gameplay EventSystem";

        [MenuItem("Seoul Playup/UI/Apply Gameplay UI To PrototypeTest")]
        public static void ApplyToPrototypeTest()
        {
            var scene = EditorSceneManager.OpenScene(PrototypeScenePath, OpenSceneMode.Single);
            var contract = Object.FindFirstObjectByType<GameplaySceneContract>(FindObjectsInactive.Include);
            if (contract == null)
            {
                throw new System.InvalidOperationException("PrototypeTest scene does not contain GameplaySceneContract.");
            }

            EnsureCanvasScaler(contract.Canvas);
            EnsureSingleInputSystemEventSystem();

            var gameplayLayers = FindRect(GameplaySceneContract.GameplayLayerRootName);
            if (gameplayLayers == null)
            {
                throw new System.InvalidOperationException("Gameplay UI Layers root was not found.");
            }

            RemoveObsoleteHudRoot(gameplayLayers);
            var cardLane = EnsureCardLaneRoot(gameplayLayers);
            ApplyKoreanFont(cardLane);
            EnsureChoiceOverlayRoot(gameplayLayers);
            if (cardLane != null)
            {
                EditorUtility.SetDirty(cardLane.gameObject);
            }
            EditorUtility.SetDirty(contract);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Applied Gameplay UI to {PrototypeScenePath}.");
        }

        private static void RemoveObsoleteHudRoot(RectTransform gameplayLayers)
        {
            var legacy = gameplayLayers.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == "Nacre HUD Root");
            if (legacy != null)
            {
                Object.DestroyImmediate(legacy.gameObject);
            }
        }

        private static RectTransform EnsureCardLaneRoot(RectTransform gameplayLayers)
        {
            var cardLane = gameplayLayers.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == "CardLane" && rect.parent == gameplayLayers)
                ?? gameplayLayers.GetComponentsInChildren<RectTransform>(includeInactive: true)
                    .FirstOrDefault(rect => rect.name == "UI_CardLane_Gameplay_Draft");
            if (cardLane == null)
            {
                return null;
            }

            cardLane.name = "CardLane";
            if (cardLane.parent != gameplayLayers && PrefabUtility.IsPartOfPrefabInstance(cardLane.gameObject))
            {
                var original = cardLane.gameObject;
                var clone = Object.Instantiate(original, gameplayLayers, false);
                clone.name = "CardLane";
                Selection.activeObject = null;
                Object.DestroyImmediate(original);
                cardLane = clone.GetComponent<RectTransform>() ?? clone.AddComponent<RectTransform>();
            }
            else
            {
                cardLane.SetParent(gameplayLayers, false);
            }

            ConfigureCardLaneRaycastPose(cardLane);
            cardLane.SetAsLastSibling();
            var cardLaneView = cardLane.GetComponent<GameplayCardLaneView>();
            if (cardLaneView == null)
            {
                cardLaneView = cardLane.gameObject.AddComponent<GameplayCardLaneView>();
            }

            var bottomCardHudView = cardLane.GetComponent<BottomCardHudView>();
            if (bottomCardHudView == null)
            {
                bottomCardHudView = cardLane.gameObject.AddComponent<BottomCardHudView>();
            }

            var cardFrontPrefab = ConfigureCardLaneViewAssets(cardLaneView);
            ConfigureBottomCardHudViewAssets(bottomCardHudView, cardLaneView);
            EnsureSceneCardSlotsUseCardFront(cardLane);
            RemoveStrayLaneCardFrontSlots(cardLane);
            cardLaneView.ConfigureLayout();
            EditorUtility.SetDirty(cardLane.gameObject);
            return cardLane;
        }

        private static void ConfigureBottomCardHudViewAssets(BottomCardHudView bottomCardHudView, GameplayCardLaneView cardLaneView)
        {
            if (bottomCardHudView == null)
            {
                return;
            }

            var so = new SerializedObject(bottomCardHudView);
            so.FindProperty("cardLaneView").objectReferenceValue = cardLaneView;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bottomCardHudView);
        }
        private static GameObject ConfigureCardLaneViewAssets(GameplayCardLaneView cardLaneView)
        {
            if (cardLaneView == null)
            {
                return null;
            }

            var cardFrontPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CardArtPrefabPath);
            if (cardFrontPrefab == null)
            {
                throw new InvalidOperationException($"CardFront prefab missing: {CardArtPrefabPath}");
            }

            var so = new SerializedObject(cardLaneView);
            so.FindProperty("autoLayoutEnabled").boolValue = true;
            // Legacy fallback slot prefab (base CardFront) is no longer used: move/action prefabs
            // below are always assigned, so leave cardSlotPrefab cleared to avoid re-binding base.
            so.FindProperty("cardSlotPrefab").objectReferenceValue = null;
            so.FindProperty("moveCardSlotPrefab").objectReferenceValue = LoadRequiredPrefab(MoveCardArtPrefabPath);
            so.FindProperty("actionCardSlotPrefab").objectReferenceValue = LoadRequiredPrefab(ActionCardArtPrefabPath);
            so.FindProperty("cardFanRestOffsetY").floatValue = 0f;
            so.FindProperty("defaultIllustration").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Sprite>($"{CardIllustrationFolder}/default_illust.png");
            ConfigureCardIllustrationBindings(so);
            so.ApplyModifiedPropertiesWithoutUndo();
            cardLaneView.ConfigureLayout();
            EditorUtility.SetDirty(cardLaneView);
            return cardFrontPrefab;
        }

        private static GameObject LoadRequiredPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Card prefab missing: {path}");
            }

            return prefab;
        }

        private static void ConfigureCardIllustrationBindings(SerializedObject cardLaneView)
        {
            var bindings = cardLaneView.FindProperty("cardIllustrations");
            if (bindings == null)
            {
                return;
            }

            var sprites = AssetDatabase.FindAssets("t:Sprite", new[] { CardIllustrationFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !path.EndsWith("/default_illust.png", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new
                {
                    IllustrationId = System.IO.Path.GetFileNameWithoutExtension(path),
                    Sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path),
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.IllustrationId) && entry.Sprite != null)
                .ToArray();

            bindings.arraySize = sprites.Length;
            for (var i = 0; i < sprites.Length; i++)
            {
                var binding = bindings.GetArrayElementAtIndex(i);
                binding.FindPropertyRelative("illustrationId").stringValue = sprites[i].IllustrationId;
                binding.FindPropertyRelative("sprite").objectReferenceValue = sprites[i].Sprite;
            }
        }

        private static void ConfigureCardLaneRaycastPose(RectTransform cardLane)
        {
            if (cardLane == null)
            {
                return;
            }

            cardLane.anchorMin = new Vector2(0.5f, 0f);
            cardLane.anchorMax = new Vector2(0.5f, 0f);
            cardLane.pivot = new Vector2(0.5f, 0f);
            cardLane.anchoredPosition = new Vector2(0f, 24f);
            cardLane.sizeDelta = new Vector2(1120f, 448f);
            cardLane.localScale = Vector3.one;
            EditorUtility.SetDirty(cardLane);
        }

        private static void EnsureSceneCardSlotsUseCardFront(RectTransform cardLane)
        {
            if (cardLane == null)
            {
                return;
            }

            var slots = cardLane.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .Where(IsCardSlotRoot)
                .OrderBy(rect => rect.GetSiblingIndex())
                .ToArray();
            foreach (var slot in slots)
            {
                var cardFrontPrefab = ResolveCardSlotPrefabForSlot(slot);
                ReplaceSlotWithCardFront(slot, cardFrontPrefab);
            }
        }

        private static void RemoveStrayLaneCardFrontSlots(RectTransform cardLane)
        {
            if (cardLane == null)
            {
                return;
            }

            foreach (var containerName in new[] { "MoveCards", "ActionCards" })
            {
                var container = cardLane.GetComponentsInChildren<RectTransform>(includeInactive: true)
                    .FirstOrDefault(rect => rect.name == containerName);
                if (container == null)
                {
                    continue;
                }

                // Managed slots are renamed to MoveCard_/ActionCard_/Card_ by EnsureSceneCardSlotsUseCardFront.
                // Anything left as a raw CardFront prefab instance (or otherwise unmanaged HandCardInteraction)
                // directly under the lane is a stray duplicate that the runtime would adopt as a pool slot.
                var strays = container.Cast<Transform>()
                    .Select(child => child as RectTransform)
                    .Where(rect => rect != null
                        && !IsCardSlotRoot(rect)
                        && (rect.GetComponent<HandCardInteraction>() != null
                            || IsCardFrontPrefabPath(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(rect.gameObject))))
                    .ToArray();

                foreach (var stray in strays)
                {
                    Object.DestroyImmediate(stray.gameObject);
                }

                if (strays.Length > 0)
                {
                    EditorUtility.SetDirty(container.gameObject);
                }
            }
        }

        private static void ReplaceSlotWithCardFront(RectTransform slot, GameObject cardFrontPrefab)
        {
            if (slot == null || cardFrontPrefab == null)
            {
                return;
            }

            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(slot.gameObject) == AssetDatabase.GetAssetPath(cardFrontPrefab))
            {
                EnsureCardSlotInteractionComponents(slot.gameObject);
                return;
            }

            var parent = slot.parent;
            var siblingIndex = slot.GetSiblingIndex();
            var name = slot.name;
            var activeSelf = slot.gameObject.activeSelf;
            var anchorMin = slot.anchorMin;
            var anchorMax = slot.anchorMax;
            var pivot = slot.pivot;
            var anchoredPosition = slot.anchoredPosition;
            var sizeDelta = slot.sizeDelta;
            var localRotation = slot.localRotation;
            var localScale = slot.localScale;

            var instance = PrefabUtility.InstantiatePrefab(cardFrontPrefab, parent) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Failed to instantiate {CardArtPrefabPath} for scene CardLane slot.");
            }

            instance.name = name;
            instance.SetActive(activeSelf);
            var instanceRect = instance.GetComponent<RectTransform>() ?? instance.AddComponent<RectTransform>();
            instanceRect.anchorMin = anchorMin;
            instanceRect.anchorMax = anchorMax;
            instanceRect.pivot = pivot;
            instanceRect.anchoredPosition = anchoredPosition;
            instanceRect.sizeDelta = sizeDelta;
            instanceRect.localRotation = localRotation;
            instanceRect.localScale = localScale;
            instanceRect.SetSiblingIndex(siblingIndex);
            EnsureCardSlotInteractionComponents(instance);

            Selection.activeObject = null;
            Object.DestroyImmediate(slot.gameObject);
            EditorUtility.SetDirty(parent);
            EditorUtility.SetDirty(instance);
        }

        private static GameObject ResolveCardSlotPrefabForSlot(RectTransform slot)
        {
            var parentName = slot != null && slot.parent != null ? slot.parent.name : string.Empty;
            if (parentName.Contains("MoveCards", StringComparison.Ordinal))
            {
                return LoadRequiredPrefab(MoveCardArtPrefabPath);
            }

            if (parentName.Contains("ActionCards", StringComparison.Ordinal))
            {
                return LoadRequiredPrefab(ActionCardArtPrefabPath);
            }

            return LoadRequiredPrefab(CardArtPrefabPath);
        }

        private static bool IsCardFrontPrefabPath(string path)
        {
            return path == CardArtPrefabPath
                || path == MoveCardArtPrefabPath
                || path == ActionCardArtPrefabPath;
        }

        private static void EnsureCardSlotInteractionComponents(GameObject slotObject)
        {
            if (slotObject == null)
            {
                return;
            }

            EnsureCardPointerRaycastTarget(slotObject);
            if (slotObject.GetComponent<HandCardInteraction>() == null)
            {
                slotObject.AddComponent<HandCardInteraction>();
            }
        }

        private static void EnsureCardPointerRaycastTarget(GameObject slotObject)
        {
            var rootImage = slotObject.GetComponent<Image>();
            if (rootImage != null)
            {
                rootImage.raycastTarget = true;
                return;
            }

            var overlay = slotObject.GetComponentsInChildren<Image>(includeInactive: true)
                .FirstOrDefault(image => image != null && image.name == "Card_Frame_Overlay");
            if (overlay != null)
            {
                overlay.raycastTarget = true;
                return;
            }

            var firstGraphic = slotObject.GetComponentInChildren<Graphic>(includeInactive: true);
            if (firstGraphic != null)
            {
                firstGraphic.raycastTarget = true;
                return;
            }

            var fallbackImage = slotObject.AddComponent<Image>();
            fallbackImage.color = new Color(1f, 1f, 1f, 0.02f);
            fallbackImage.raycastTarget = true;
        }

        private static bool IsCardSlotRoot(RectTransform rect)
        {
            return rect != null
                && (rect.name.StartsWith("Card_", StringComparison.Ordinal)
                    || rect.name.StartsWith("MoveCard_", StringComparison.Ordinal)
                    || rect.name.StartsWith("ActionCard_", StringComparison.Ordinal));
        }

        private static void EnsureCardArtSpritesImported()
        {
            foreach (var path in new[]
            {
                "Assets/Art/UI/Cards/colored_1_attack_공격_card_frame.png",
                "Assets/Art/UI/Cards/colored_2_move_이동_card_frame.png",
                "Assets/Art/UI/Cards/colored_3_defense_방어_card_frame.png",
                "Assets/Art/UI/Cards/colored_4_scout_정찰_card_frame.png",
                "Assets/Art/UI/Cards/colored_5_field_필드_card_frame.png",
                "Assets/Art/UI/Cards/ui_card_front_colored.png",
                "Assets/Art/UI/Cards/ui_card_front_ivory.png",
                "Assets/Art/UI/Cards/ui_card_back (2).png",
                "Assets/Art/UI/Cards/ui_deck_before.png",
                "Assets/Art/UI/Cards/ui_deck_after (2).png"
            })
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single || !importer.alphaIsTransparency)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                }
            }
        }

        private static Sprite LoadSprite(string filename)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/UI/Cards/{filename}");
        }

        private static void EnsureCanvasScaler(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = GameplaySceneContract.GameplayUiReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = GameplaySceneContract.GameplayUiCanvasMatch;
            EditorUtility.SetDirty(scaler);
        }

        private static void EnsureSingleInputSystemEventSystem()
        {
            var eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var primary = eventSystems.FirstOrDefault(eventSystem => eventSystem.name == GameplayEventSystemName)
                ?? eventSystems.FirstOrDefault(eventSystem => eventSystem.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() != null)
                ?? eventSystems.FirstOrDefault();

            if (primary == null)
            {
                primary = new GameObject(GameplayEventSystemName, typeof(EventSystem)).GetComponent<EventSystem>();
            }

            primary.name = GameplayEventSystemName;
            primary.gameObject.SetActive(true);
            if (primary.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
            {
                primary.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            foreach (var legacyModule in primary.GetComponents<StandaloneInputModule>())
            {
                Object.DestroyImmediate(legacyModule);
            }

            foreach (var duplicate in eventSystems)
            {
                if (duplicate != null && duplicate != primary)
                {
                    Object.DestroyImmediate(duplicate.gameObject);
                }
            }

            EditorUtility.SetDirty(primary.gameObject);
        }

        private static void ApplyKoreanFont(RectTransform root)
        {
            if (root == null)
            {
                return;
            }

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GameplaySceneContract.KoreanFontAssetPath);
            if (font == null)
            {
                return;
            }

            foreach (var label in root.GetComponentsInChildren<TMP_Text>(includeInactive: true))
            {
                label.font = font;
                EditorUtility.SetDirty(label);
            }
        }

        private static void EnsureChoiceOverlayRoot(RectTransform gameplayLayers)
        {
            var root = gameplayLayers.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == ChoiceOverlayRootName);
            if (root == null)
            {
                var rootObject = new GameObject(ChoiceOverlayRootName, typeof(RectTransform), typeof(Image));
                root = rootObject.GetComponent<RectTransform>();
                var image = rootObject.GetComponent<Image>();
                image.color = Color.clear;
                image.raycastTarget = false;
            }
            else
            {
                root.name = ChoiceOverlayRootName;
            }

            root.SetParent(gameplayLayers, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            root.SetAsLastSibling();
            root.gameObject.SetActive(true);
            EditorUtility.SetDirty(root.gameObject);

            var choicePanel = gameplayLayers.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == "PlayerState Choice Card Panel");
            if (choicePanel != null)
            {
                choicePanel.SetParent(root, false);
                EditorUtility.SetDirty(choicePanel.gameObject);
            }
        }

        private static RectTransform FindRect(string name)
        {
            return Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(rect => rect.name == name);
        }
    }
}
#endif
