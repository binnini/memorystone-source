using System.IO;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Flow.Unity;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SeoulPlayup.EditorTools.UI
{
    /// <summary>
    /// Authoring pass for the Lobby main menu. Generates the 9-sliced ink-plate / gold-rim sprites,
    /// restyles every <c>ButtonLayout</c> child to match the 기억결 title treatment, and makes sure the
    /// scene actually carries a wired <see cref="LobbyController"/>.
    ///
    /// Re-runnable: every step is idempotent, so running it again after hand-tweaking the layout only
    /// re-asserts the look, never duplicates objects.
    /// </summary>
    public static class LobbyMenuStyleBuilder
    {
        private const string ArtFolder = "Assets/Art/UI/Lobby";
        private const string PlateSpritePath = ArtFolder + "/ui_btn_plate.png";
        private const string BorderSpritePath = ArtFolder + "/ui_btn_border.png";
        private const string GlowSpritePath = ArtFolder + "/ui_btn_glow.png";
        private const string AdditiveMaterialPath = "Assets/Art/UI/typo logo/M_LogoEffect_Additive.mat";
        private const string StageCatalogPath = "Assets/Data/Stages/StageCatalog.asset";
        private const string TutorialStagePath = "Assets/Data/Stages/Stage_000_Tutorial.asset";
        private const string PrototypeStagePath = "Assets/Data/Stages/Stage_001_Prototype.asset";

        // Sprite geometry, in source pixels. The 9-slice border must exceed radius + margin so the
        // stretched middle section never eats into a corner arc.
        private const int SpriteSize = 96;
        private const float CornerRadius = 22f;
        private const float EdgeMargin = 4f;
        private const float BorderStroke = 3f;
        private const int SliceBorder = 30;
        // Slack left between the two corner bands so a stretched middle section always survives.
        private const float SliceGutter = 4f;
        // Breathing room between a slider handle at full travel and its value readout.
        private const float ValueGutter = 10f;

        [MenuItem("Tools/UI/Lobby/Rebuild Main Menu Style")]
        public static void RebuildAll()
        {
            var plate = EnsurePlateSprite();
            var border = EnsureBorderSprite();
            var glow = EnsureGlowSprite();
            var additive = AssetDatabase.LoadAssetAtPath<Material>(AdditiveMaterialPath);

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[LobbyMenuStyle] No active scene. Open Assets/Scenes/Game/Lobby.unity first.");
                return;
            }

            var layout = FindInScene(scene, "ButtonLayout");
            if (layout == null)
            {
                Debug.LogError("[LobbyMenuStyle] 'ButtonLayout' was not found in the active scene.");
                return;
            }

            TuneLayout(layout, FindInScene(scene, "TitleLogo") as RectTransform);

            var styled = 0;
            foreach (Transform child in layout)
            {
                if (child.GetComponent<Button>() == null)
                    continue;

                StyleButton(child, plate, border, glow, additive, new Vector2(360f, 72f));
                styled++;
            }

            // The docks open on top of this menu, so they get the same ink-and-gold treatment; left on
            // the default blue they read as a different game.
            styled += StyleDock(FindInScene(scene, "StageDock"), plate, border, glow, additive);
            styled += StyleDock(FindInScene(scene, "SettingDock"), plate, border, glow, additive);

            var wired = EnsureLobbyController(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LobbyMenuStyle] styled {styled} button(s); controller wiring = {wired}");
        }

        // ---------------------------------------------------------------- layout

        private static void TuneLayout(Transform layout, RectTransform titleLogo)
        {
            var rect = (RectTransform)layout;
            Undo.RecordObject(rect, "Lobby menu layout");

            // The stack reads as part of the title lockup, so it shares the logo's centre line rather
            // than sitting on an independently authored x.
            if (titleLogo != null && titleLogo.parent != null && rect.parent != null)
            {
                var logoCentre = rect.parent.InverseTransformPoint(titleLogo.TransformPoint(Vector3.zero));
                rect.anchoredPosition = new Vector2(logoCentre.x, rect.anchoredPosition.y);
            }

            var group = layout.GetComponent<VerticalLayoutGroup>();
            if (group != null)
            {
                Undo.RecordObject(group, "Lobby menu layout");
                // Tighter rhythm than the 50px default: the plates now read as one stacked column
                // instead of four unrelated slabs.
                group.spacing = 22f;
                group.childAlignment = TextAnchor.UpperCenter;
                group.childControlWidth = false;
                group.childControlHeight = false;
                group.childForceExpandWidth = false;
                group.childForceExpandHeight = false;
                EditorUtility.SetDirty(group);
            }

            EditorUtility.SetDirty(rect);
        }

        // ---------------------------------------------------------------- docks

        /// <summary>
        /// Restyles a modal dock (stage select / settings): its backing panel, title, buttons and any
        /// volume sliders. Returns the number of buttons styled.
        /// </summary>
        private static int StyleDock(Transform dock, Sprite plate, Sprite border, Sprite glowSprite, Material additive)
        {
            if (dock == null)
                return 0;

            var panel = dock.Find("Panel") as RectTransform;
            if (panel != null)
            {
                var panelImage = EnsureComponent<Image>(panel.gameObject);
                Undo.RecordObject(panelImage, "Lobby dock style");
                panelImage.sprite = plate;
                panelImage.type = Image.Type.Sliced;
                panelImage.pixelsPerUnitMultiplier = 1f;
                // Denser than a button plate: this one has to hold text against the night skyline.
                panelImage.color = new Color32(0x12, 0x0D, 0x1E, 0xF0);
                EditorUtility.SetDirty(panelImage);

                var panelRim = EnsureChild(panel, "Border", panel.childCount);
                var panelRimImage = EnsureComponent<Image>(panelRim.gameObject);
                Undo.RecordObject(panelRimImage, "Lobby dock style");
                panelRimImage.sprite = border;
                panelRimImage.type = Image.Type.Sliced;
                panelRimImage.pixelsPerUnitMultiplier = 1f;
                panelRimImage.color = new Color32(0xC9, 0xA2, 0x27, 0xD9);
                panelRimImage.raycastTarget = false;
                Stretch(panelRim, Vector2.zero);
                EditorUtility.SetDirty(panelRimImage);
            }

            var title = dock.Find("Title")?.GetComponent<TMP_Text>();
            if (title != null)
            {
                Undo.RecordObject(title, "Lobby dock style");
                title.color = new Color32(0xF0, 0xE2, 0xC4, 0xFF);
                title.characterSpacing = 8f;
                title.raycastTarget = false;
                EditorUtility.SetDirty(title);
            }

            var styled = 0;
            foreach (var button in dock.GetComponentsInChildren<Button>(true))
            {
                // Dock buttons keep their authored footprint — only the main menu has a uniform size.
                StyleButton(button.transform, plate, border, glowSprite, additive, null);
                styled++;
            }

            foreach (var slider in dock.GetComponentsInChildren<SoundVolumeSliderControl>(true))
                StyleSlider(slider.transform, plate);

            return styled;
        }

        private static void StyleSlider(Transform slider, Sprite plate)
        {
            var track = slider.Find("Track") as RectTransform;
            if (track != null)
            {
                var trackHeight = track.rect.height > 0f ? track.rect.height : track.sizeDelta.y;
                ApplySliced(EnsureComponent<Image>(track.gameObject), plate,
                    new Color32(0x0D, 0x09, 0x18, 0xE6), trackHeight);

                var fill = track.Find("Fill") as RectTransform;
                if (fill != null)
                {
                    // Fill stretches inside the track, so its own rect can still be zero at author
                    // time — size the 9-slice off the track instead.
                    ApplySliced(EnsureComponent<Image>(fill.gameObject), plate,
                        new Color32(0xC9, 0xA2, 0x27, 0xFF), trackHeight);
                }

                var handle = track.Find("Handle") as RectTransform;
                if (handle != null)
                {
                    var handleSide = Mathf.Min(
                        handle.rect.width > 0f ? handle.rect.width : handle.sizeDelta.x,
                        handle.rect.height > 0f ? handle.rect.height : handle.sizeDelta.y);
                    ApplySliced(EnsureComponent<Image>(handle.gameObject), plate,
                        new Color32(0xF5, 0xE7, 0xC8, 0xFF), handleSide);
                }
            }

            foreach (var childName in new[] { "Label", "Value" })
            {
                var text = slider.Find(childName)?.GetComponent<TMP_Text>();
                if (text == null)
                    continue;

                Undo.RecordObject(text, "Lobby dock style");
                text.color = new Color32(0xE4, 0xD6, 0xB8, 0xFF);
                text.raycastTarget = false;
                EditorUtility.SetDirty(text);
            }

            // At 100% the handle parks on the track's right edge and used to sit on top of the value
            // readout. Park the readout clear of the handle's full travel instead.
            var valueRect = slider.Find("Value") as RectTransform;
            if (valueRect != null && track != null)
            {
                var handleRect = track.Find("Handle") as RectTransform;
                var handleHalf = handleRect != null ? handleRect.sizeDelta.x * 0.5f : 0f;
                var trackRight = track.anchoredPosition.x + track.sizeDelta.x * 0.5f;

                Undo.RecordObject(valueRect, "Lobby dock style");
                valueRect.anchoredPosition = new Vector2(
                    trackRight + handleHalf + ValueGutter + valueRect.sizeDelta.x * 0.5f,
                    valueRect.anchoredPosition.y);
                EditorUtility.SetDirty(valueRect);
            }
        }

        // ---------------------------------------------------------------- per button

        private static void StyleButton(Transform buttonTransform, Sprite plate, Sprite border, Sprite glowSprite, Material additive, Vector2? size)
        {
            var go = buttonTransform.gameObject;
            var rect = (RectTransform)buttonTransform;
            Undo.RecordObject(rect, "Lobby button style");
            if (size.HasValue)
                rect.sizeDelta = size.Value;
            EditorUtility.SetDirty(rect);

            var height = rect.rect.height > 0f ? rect.rect.height : rect.sizeDelta.y;

            var plateImage = go.GetComponent<Image>();
            if (plateImage == null)
                plateImage = Undo.AddComponent<Image>(go);
            ApplySliced(plateImage, plate, new Color32(0x15, 0x0F, 0x22, 0xB8), height);
            plateImage.raycastTarget = true;

            var glow = EnsureChild(go.transform, "Glow", 0);
            var glowImage = EnsureComponent<Image>(glow.gameObject);
            // Additive ignores alpha, so intensity lives in the RGB channels — black means "off".
            ApplySliced(glowImage, glowSprite, new Color32(0x00, 0x00, 0x00, 0xFF), height);
            glowImage.material = additive;
            glowImage.raycastTarget = false;
            Stretch(glow, Vector2.zero);

            var rim = EnsureChild(go.transform, "Border", 1);
            var rimImage = EnsureComponent<Image>(rim.gameObject);
            ApplySliced(rimImage, border, new Color32(0xC9, 0xA2, 0x27, 0xBF), height);
            rimImage.raycastTarget = false;
            Stretch(rim, Vector2.zero);

            var label = go.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                Undo.RecordObject(label, "Lobby button style");
                label.color = new Color32(0xF0, 0xE2, 0xC4, 0xFF);
                // Keeps the 72px main-menu plates at 30pt and scales the shorter dock plates down.
                label.fontSize = Mathf.Round(height * 0.417f);
                label.characterSpacing = 6f;
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
                label.transform.SetAsLastSibling();
                Stretch((RectTransform)label.transform, Vector2.zero);
                EditorUtility.SetDirty(label);
            }

            var button = go.GetComponent<Button>();
            Undo.RecordObject(button, "Lobby button style");
            // The view lerps four graphics at once, which ColorTint cannot express.
            button.transition = Selectable.Transition.None;
            button.targetGraphic = plateImage;
            EditorUtility.SetDirty(button);

            // Recreated rather than reused: the palette lives in the component's field defaults, and a
            // component already serialized in the scene would keep whatever palette it was authored
            // with. Rebuilding is how this tool re-asserts the current look.
            var stale = go.GetComponent<LobbyMenuButtonView>();
            if (stale != null)
                Undo.DestroyObjectImmediate(stale);

            var view = Undo.AddComponent<LobbyMenuButtonView>(go);
            view.Bind(plateImage, rimImage, glowImage, label);
            EditorUtility.SetDirty(view);
        }

        /// <summary>
        /// Assigns a 9-sliced sprite, shrinking the slice in screen space when the target rect is
        /// shorter than the two corner bands combined. Without this a 16px slider track (or a 52px
        /// close button) would draw its top and bottom corners overlapping each other.
        /// </summary>
        private static void ApplySliced(Image image, Sprite sprite, Color color, float smallestSide)
        {
            Undo.RecordObject(image, "Lobby style");
            image.sprite = sprite;
            image.type = Image.Type.Sliced;

            var needed = SliceBorder * 2f + SliceGutter;
            image.pixelsPerUnitMultiplier = smallestSide > 0f && smallestSide < needed
                ? needed / smallestSide
                : 1f;

            image.color = color;
            EditorUtility.SetDirty(image);
        }

        private static RectTransform EnsureChild(Transform parent, string name, int siblingIndex)
        {
            var existing = parent.Find(name);
            RectTransform rect;
            if (existing == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "Lobby button style");
                rect = (RectTransform)go.transform;
                rect.SetParent(parent, false);
            }
            else
            {
                rect = (RectTransform)existing;
            }

            rect.SetSiblingIndex(siblingIndex);
            return rect;
        }

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(go);
        }

        private static void Stretch(RectTransform rect, Vector2 inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = inset;
            rect.offsetMax = -inset;
            rect.localScale = Vector3.one;
            EditorUtility.SetDirty(rect);
        }

        // ---------------------------------------------------------------- controller wiring

        private static string EnsureLobbyController(Scene scene)
        {
            var canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
                return "skipped (no Canvas)";

            // The controller auto-binds through GetComponentsInChildren, so it has to sit on an
            // ancestor of the menu — the canvas root is the only stable one here.
            var controller = canvas.GetComponent<LobbyController>();
            if (controller == null)
                controller = Undo.AddComponent<LobbyController>(canvas.gameObject);

            var serialized = new SerializedObject(controller);
            SetObject(serialized, "stageCatalog", AssetDatabase.LoadAssetAtPath<StageCatalog>(StageCatalogPath));
            SetObject(serialized, "defaultStage", AssetDatabase.LoadAssetAtPath<StageDefinition>(PrototypeStagePath));
            SetObject(serialized, "tutorialStage", AssetDatabase.LoadAssetAtPath<StageDefinition>(TutorialStagePath));
            SetObject(serialized, "stageOneStage", AssetDatabase.LoadAssetAtPath<StageDefinition>(PrototypeStagePath));
            SetObject(serialized, "startButton", FindButton(scene, "StartButton"));
            // The authored menu calls the resume entry "LoadButton"; ContinueButton is the legacy
            // scaffold's name for the same thing and stays inactive in the scene.
            SetObject(serialized, "continueButton", FindButton(scene, "LoadButton") ?? FindButton(scene, "ContinueButton"));
            SetObject(serialized, "settingButton", FindButton(scene, "SettingButton"));
            SetObject(serialized, "exitButton", FindButton(scene, "ExitButton"));

            // Every reference above is authored, so the runtime fallback canvas must stay off — it
            // would otherwise build a second, unstyled menu on top of this one.
            var fallback = serialized.FindProperty("buildFallbackUi");
            if (fallback != null)
                fallback.boolValue = false;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
            return "ok";
        }

        private static void SetObject(SerializedObject serialized, string propertyName, Object value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"[LobbyMenuStyle] LobbyController has no serialized field '{propertyName}'.");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static Button FindButton(Scene scene, string name)
        {
            var found = FindInScene(scene, name);
            return found != null ? found.GetComponent<Button>() : null;
        }

        private static Transform FindInScene(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var match = FindDeep(root.transform, name);
                if (match != null)
                    return match;
            }

            return null;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name)
                return t;

            foreach (Transform child in t)
            {
                var match = FindDeep(child, name);
                if (match != null)
                    return match;
            }

            return null;
        }

        // ---------------------------------------------------------------- sprite generation

        private static Sprite EnsurePlateSprite()
        {
            return EnsureSprite(PlateSpritePath, filled: true, premultiplied: false);
        }

        private static Sprite EnsureBorderSprite()
        {
            return EnsureSprite(BorderSpritePath, filled: false, premultiplied: false);
        }

        /// <summary>
        /// The glow renders through <c>UI/AdditiveGlow</c> (<c>Blend One One</c>), which adds RGB
        /// straight to the frame and never consults alpha. A plain white-RGB sprite would therefore
        /// add a full rectangle, corners and all — so this variant bakes coverage into RGB instead.
        /// </summary>
        private static Sprite EnsureGlowSprite()
        {
            return EnsureSprite(GlowSpritePath, filled: true, premultiplied: true);
        }

        private static Sprite EnsureSprite(string path, bool filled, bool premultiplied)
        {
            if (!AssetDatabase.IsValidFolder(ArtFolder))
                Directory.CreateDirectory(ArtFolder);

            File.WriteAllBytes(path, BuildRoundedRect(filled, premultiplied));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(SliceBorder, SliceBorder, SliceBorder, SliceBorder);
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// Rasterises a white rounded rectangle (filled, or a stroked ring) with analytic
        /// antialiasing, so the 9-sliced corners stay crisp at any button size.
        /// </summary>
        private static byte[] BuildRoundedRect(bool filled, bool premultiplied)
        {
            var texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false);
            var pixels = new Color32[SpriteSize * SpriteSize];
            var half = SpriteSize * 0.5f;
            var extent = half - EdgeMargin;

            for (var y = 0; y < SpriteSize; y++)
            {
                for (var x = 0; x < SpriteSize; x++)
                {
                    var px = x + 0.5f - half;
                    var py = y + 0.5f - half;

                    // Signed distance to a rounded rectangle: negative inside, positive outside.
                    var qx = Mathf.Abs(px) - (extent - CornerRadius);
                    var qy = Mathf.Abs(py) - (extent - CornerRadius);
                    var outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    var distance = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - CornerRadius;

                    float coverage;
                    if (filled)
                    {
                        coverage = Mathf.Clamp01(0.5f - distance);
                    }
                    else
                    {
                        // Ring: keep the band that straddles the outline.
                        var ring = Mathf.Abs(distance + BorderStroke * 0.5f) - BorderStroke * 0.5f;
                        coverage = Mathf.Clamp01(0.5f - ring);
                    }

                    var alpha = (byte)Mathf.RoundToInt(coverage * 255f);
                    var rgb = premultiplied ? alpha : (byte)255;
                    pixels[y * SpriteSize + x] = new Color32(rgb, rgb, rgb, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            var png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return png;
        }
    }
}
