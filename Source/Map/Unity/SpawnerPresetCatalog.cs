using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// Source-of-truth catalog of monster spawn presets (monster ref + patrol radius / role / purpose
    /// defaults). This is an authoring convenience layer that sits on top of the combat monster catalog,
    /// not a replacement: <see cref="Entry.MonsterRef"/> must still resolve to a valid id in the combat
    /// monster catalog (e.g. <c>CombatState.CreateMonsterCatalog</c>). Applying a preset only fills the
    /// monster spawn brush fields; it does not change how spawns are stored or simulated.
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/Map/Spawner Preset Catalog")]
    public sealed class SpawnerPresetCatalog : ScriptableObject
    {
        public const string DefaultResourcesPath = "SpawnerPresetCatalog";
        public const string DefaultCatalogAssetPath = "Assets/Data/Object/Spawner/Resources/SpawnerPresetCatalog.asset";
        public const int DefaultPatrolRadius = 3;

        [SerializeField] private List<Entry> presets = new List<Entry>();

        public IReadOnlyList<Entry> Presets => presets ?? (IReadOnlyList<Entry>)Array.Empty<Entry>();

        public void ConfigureForTests(IEnumerable<Entry> presets)
        {
            this.presets = presets == null ? new List<Entry>() : new List<Entry>(presets);
        }

        public bool TryGet(string presetId, out Entry preset)
        {
            preset = null;
            if (string.IsNullOrWhiteSpace(presetId))
            {
                return false;
            }

            preset = Presets.FirstOrDefault(candidate =>
                candidate != null &&
                string.Equals(candidate.PresetId, presetId.Trim(), StringComparison.OrdinalIgnoreCase));
            return preset != null;
        }

        public static SpawnerPresetCatalog LoadDefault()
        {
            var catalog = Resources.Load<SpawnerPresetCatalog>(DefaultResourcesPath);
            if (catalog != null)
            {
                return catalog;
            }

#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<SpawnerPresetCatalog>(DefaultCatalogAssetPath);
#else
            return null;
#endif
        }

        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string presetId;
            [SerializeField] private string displayName;
            [SerializeField] private string monsterRef;
            [SerializeField] private int patrolRadius = DefaultPatrolRadius;
            [SerializeField] private string role = "primary_pressure";
            [SerializeField] private HexMapPurpose enabledForPurpose = HexMapPurpose.PlayableMap;

            public Entry()
            {
            }

            public Entry(
                string presetId,
                string displayName,
                string monsterRef,
                int patrolRadius,
                string role,
                HexMapPurpose enabledForPurpose)
            {
                this.presetId = presetId;
                this.displayName = displayName;
                this.monsterRef = monsterRef;
                this.patrolRadius = patrolRadius;
                this.role = role;
                this.enabledForPurpose = enabledForPurpose;
            }

            public string PresetId => presetId ?? string.Empty;
            public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? PresetId : displayName;
            public string MonsterRef => monsterRef ?? string.Empty;
            public int PatrolRadius => Mathf.Max(0, patrolRadius);
            public string Role => role ?? string.Empty;
            public HexMapPurpose EnabledForPurpose => enabledForPurpose;
        }
    }
}
