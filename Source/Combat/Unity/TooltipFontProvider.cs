using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Build-safe loader for the DNFForgedBlade-Light SDF font used by runtime-created hover tooltips.
    /// Mirrors <see cref="KoreanFontProvider"/>: the asset lives under a Resources folder so it ships in
    /// player builds and resolves via <see cref="Resources.Load"/> (AssetDatabase path loading is
    /// editor-only and returns null in builds). The DNF face covers the full Hangul syllable block, so
    /// tooltips render Korean directly with no fallback table. Falls back to
    /// <see cref="KoreanFontProvider"/> if the DNF asset is ever missing so Korean never breaks.
    /// </summary>
    internal static class TooltipFontProvider
    {
        public const string ResourcesPath = "Fonts/DNFForgedBlade-Light SDF";
#if UNITY_EDITOR
        private const string EditorAssetPath = "Assets/Resources/Fonts/DNFForgedBlade-Light SDF.asset";
#endif

        private static TMP_FontAsset cached;

        public static TMP_FontAsset Load()
        {
            if (cached != null)
            {
                return cached;
            }

            var font = Resources.Load<TMP_FontAsset>(ResourcesPath);
#if UNITY_EDITOR
            if (font == null)
            {
                font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(EditorAssetPath);
            }
#endif
            if (font != null)
            {
                cached = font;
                return cached;
            }

            // Never leave Korean unrenderable: fall back to the shared Korean font.
            return KoreanFontProvider.Load();
        }

        public static void Apply(TMP_Text label)
        {
            if (label == null)
            {
                return;
            }

            var font = Load();
            if (font == null)
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
