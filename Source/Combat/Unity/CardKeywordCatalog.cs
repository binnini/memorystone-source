using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Build-safe baked copy of game_keywords.csv. Lives under Resources so it loads in player builds
    /// (editor-only AssetDatabase paths return null at runtime — see the build-resource audit). Bake it
    /// from the CSV via the editor menu "Seoul Playup/Combat/Bake Game Keyword Catalog".
    /// </summary>
    [CreateAssetMenu(
        fileName = "DefaultCardKeywordCatalog",
        menuName = "Seoul Playup/Combat/Card Keyword Catalog")]
    public sealed class CardKeywordCatalog : ScriptableObject
    {
        public const string DefaultResourcesPath = "Combat/DefaultCardKeywordCatalog";

        [System.Serializable]
        public struct Entry
        {
            public string category;
            public string keyword;
            public string effect;
            public KeywordValueKind valueKind;
            public string value;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        public void SetEntries(IEnumerable<Entry> source)
        {
            entries = source != null ? new List<Entry>(source) : new List<Entry>();
        }

        public KeywordCatalogDefinition ToDefinition()
        {
            var defs = new List<KeywordDefinition>(entries.Count);
            foreach (var entry in entries)
            {
                defs.Add(new KeywordDefinition(entry.category, entry.keyword, entry.effect, entry.valueKind, entry.value));
            }

            return new KeywordCatalogDefinition(defs);
        }
    }

    /// <summary>
    /// Loads the baked keyword catalog from Resources and installs it into the pure
    /// <see cref="CardKeywordCatalogProvider"/> once per process, so the description decorator and the
    /// hover tooltip share one source. Runs automatically in play mode / builds; EditMode tests never
    /// trigger it, so <c>Describe</c> stays plain there.
    /// </summary>
    public static class CardKeywordRuntime
    {
        private static bool loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void EnsureLoaded()
        {
            if (loaded && CardKeywordCatalogProvider.Active != null)
            {
                return;
            }

            var asset = Resources.Load<CardKeywordCatalog>(CardKeywordCatalog.DefaultResourcesPath);
            if (asset != null)
            {
                CardKeywordCatalogProvider.Active = asset.ToDefinition();
            }

            loaded = true;
        }
    }
}
