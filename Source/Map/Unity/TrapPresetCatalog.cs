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
    /// Source-of-truth catalog of reusable trap presets (effect kind/amount/duration, radius, targeting
    /// flags). This is an authoring convenience layer only: traps are still stored inline on the map as
    /// <c>HexTrapRef</c>/<c>HexTrapData</c>; applying a preset just fills the trap brush fields. It does
    /// not change the runtime trap path and is intentionally separate from the visual object catalog set.
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/Map/Trap Preset Catalog")]
    public sealed class TrapPresetCatalog : ScriptableObject
    {
        public const string DefaultResourcesPath = "TrapPresetCatalog";
        public const string DefaultCatalogAssetPath = "Assets/Data/Object/Trap/Resources/TrapPresetCatalog.asset";

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

        public static TrapPresetCatalog LoadDefault()
        {
            var catalog = Resources.Load<TrapPresetCatalog>(DefaultResourcesPath);
            if (catalog != null)
            {
                return catalog;
            }

#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<TrapPresetCatalog>(DefaultCatalogAssetPath);
#else
            return null;
#endif
        }

        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string presetId;
            [SerializeField] private string displayName;
            [SerializeField] private HexTrapEffectKind effectKind = HexTrapEffectKind.Damage;
            [SerializeField] private int effectAmount = 1;
            [SerializeField] private int durationTurns = 1;
            [SerializeField] private int radius;
            [SerializeField] private bool affectsPlayer = true;
            [SerializeField] private bool affectsMonsters;
            [SerializeField] private bool oneShot = true;
            [SerializeField] private bool triggerOnEnter = true;
            [Tooltip("SpawnMonsters 전용: 무엇을 부를지(monster_catalog.csv의 monsterId). effectAmount가 스폰 수다.")]
            [SerializeField] private string monsterDefinitionId;
            [Tooltip("0 = 밟기형. N>0 = N턴마다 스스로 터지는 예고형(C-11). 예고형은 밟아도 터지지 않는다.")]
            [SerializeField] private int periodTurns;
            [Tooltip("InjectStatusCard 전용: 어떤 상태 카드를 넣을지(cards.csv의 id). effectAmount가 삽입 장수다.")]
            [SerializeField] private string statusCardId;

            public Entry()
            {
            }

            public Entry(
                string presetId,
                string displayName,
                HexTrapEffectKind effectKind,
                int effectAmount,
                int durationTurns,
                int radius,
                bool affectsPlayer,
                bool affectsMonsters,
                bool oneShot,
                bool triggerOnEnter,
                string monsterDefinitionId = null,
                int periodTurns = 0,
                string statusCardId = null)
            {
                this.statusCardId = statusCardId;
                this.monsterDefinitionId = monsterDefinitionId;
                this.periodTurns = Mathf.Max(0, periodTurns);
                this.presetId = presetId;
                this.displayName = displayName;
                this.effectKind = effectKind;
                this.effectAmount = effectAmount;
                this.durationTurns = durationTurns;
                this.radius = radius;
                this.affectsPlayer = affectsPlayer;
                this.affectsMonsters = affectsMonsters;
                this.oneShot = oneShot;
                this.triggerOnEnter = triggerOnEnter;
            }

            public string PresetId => presetId ?? string.Empty;
            public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? PresetId : displayName;
            public HexTrapEffectKind EffectKind => effectKind;
            public int EffectAmount => Mathf.Max(0, effectAmount);
            public int DurationTurns => Mathf.Max(1, durationTurns);
            public int Radius => Mathf.Max(0, radius);
            public bool AffectsPlayer => affectsPlayer;
            public bool AffectsMonsters => affectsMonsters;
            public bool OneShot => oneShot;
            public bool TriggerOnEnter => triggerOnEnter;
            public string MonsterDefinitionId => monsterDefinitionId ?? string.Empty;
            public int PeriodTurns => Mathf.Max(0, periodTurns);
            public string StatusCardId => statusCardId ?? string.Empty;
        }
    }
}
