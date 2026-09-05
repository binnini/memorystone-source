using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Build-safe loader for the Korean-capable UI fonts used by runtime-created text.
    /// AssetDatabase path loading is editor-only, so in player builds a path-only loader
    /// falls back to <see cref="TMP_Settings.defaultFontAsset"/> and renders Korean as empty
    /// boxes. Both faces live under a Resources folder so they ship in the build and can be
    /// resolved at runtime via Resources.Load.
    /// <para>
    /// Weight split: titles and headlines use the Medium face (<see cref="ApplyTitle"/>);
    /// every other label uses Light (<see cref="Apply"/>). Both DNFForgedBlade faces cover the
    /// full Hangul syllable block, so no Korean fallback font is required.
    /// </para>
    /// </summary>
    internal static class KoreanFontProvider
    {
        public const string ResourcesPath = "Fonts/DNFForgedBlade-Light SDF";
        public const string TitleResourcesPath = "Fonts/DNFForgedBlade-Medium SDF";
#if UNITY_EDITOR
        private const string EditorAssetPath = "Assets/Resources/Fonts/DNFForgedBlade-Light SDF.asset";
        private const string TitleEditorAssetPath = "Assets/Resources/Fonts/DNFForgedBlade-Medium SDF.asset";
#endif

        private static TMP_FontAsset cached;
        private static TMP_FontAsset cachedTitle;

        public static TMP_FontAsset Load()
        {
            if (cached != null)
            {
                return cached;
            }

            cached = Resolve(ResourcesPath
#if UNITY_EDITOR
                , EditorAssetPath
#endif
            );
            return cached != null ? cached : TMP_Settings.defaultFontAsset;
        }

        public static TMP_FontAsset LoadTitle()
        {
            if (cachedTitle != null)
            {
                return cachedTitle;
            }

            cachedTitle = Resolve(TitleResourcesPath
#if UNITY_EDITOR
                , TitleEditorAssetPath
#endif
            );
            // The Light face is the safe stand-in: same family, same Hangul coverage.
            return cachedTitle != null ? cachedTitle : Load();
        }

        public static void Apply(TMP_Text label)
        {
            ApplyFont(label, Load());
        }

        public static void ApplyTitle(TMP_Text label)
        {
            ApplyFont(label, LoadTitle());
        }

        private static TMP_FontAsset Resolve(string resourcesPath
#if UNITY_EDITOR
            , string editorAssetPath
#endif
        )
        {
            var font = Resources.Load<TMP_FontAsset>(resourcesPath);
#if UNITY_EDITOR
            if (font == null)
            {
                font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(editorAssetPath);
            }
#endif
            return font;
        }

        private static void ApplyFont(TMP_Text label, TMP_FontAsset font)
        {
            if (label == null || font == null)
            {
                return;
            }

            label.font = font;
            if (font.material != null)
            {
                label.fontSharedMaterial = font.material;
            }
        }
    }
}
