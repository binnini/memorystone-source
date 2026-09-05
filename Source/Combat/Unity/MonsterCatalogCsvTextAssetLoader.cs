using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    public static class MonsterCatalogCsvTextAssetLoader
    {
        public static MonsterCatalogCsvBundle Convert(
            TextAsset monsterCatalog,
            TextAsset attackPatterns,
            TextAsset patternBindings,
            TextAsset vfxCues,
            TextAsset soundCues,
            TextAsset patternVfxBindings = null,
            string sourceId = "designer-monster-csv",
            string displayName = "Designer Monster CSV Catalog")
        {
            return MonsterCatalogCsvConverter.Convert(new MonsterCatalogCsvSource(
                RequiredText(monsterCatalog, nameof(monsterCatalog)),
                RequiredText(attackPatterns, nameof(attackPatterns)),
                RequiredText(patternBindings, nameof(patternBindings)),
                RequiredText(vfxCues, nameof(vfxCues)),
                RequiredText(soundCues, nameof(soundCues)),
                patternVfxBindingsCsv: patternVfxBindings != null ? patternVfxBindings.text : string.Empty,
                sourceId: sourceId,
                displayName: displayName));
        }

        private static string RequiredText(TextAsset asset, string label)
        {
            if (asset == null)
            {
                throw new System.ArgumentNullException(label);
            }

            return asset.text;
        }
    }
}
