using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    [Serializable]
    public sealed class HexTrapEffectRef
    {
        [SerializeField] private HexTrapEffectKind kind = HexTrapEffectKind.Damage;
        [SerializeField] private int amount = 1;
        [SerializeField] private int durationTurns;
        [Tooltip("SpawnMonsters 전용: 무엇을 부를지(monster_catalog.csv의 monsterId). 다른 kind에서는 비워 둔다. " +
                 "Amount가 스폰 수다.")]
        [SerializeField] private string monsterDefinitionId;
        [Tooltip("InjectStatusCard 전용: 어떤 상태 카드를 넣을지(cards.csv의 id). Amount가 삽입 장수다.")]
        [SerializeField] private string statusCardId;

        public HexTrapEffectRef()
        {
        }

        public HexTrapEffectRef(HexTrapEffectKind kind, int amount, int durationTurns = 0, string monsterDefinitionId = null, string statusCardId = null)
        {
            this.statusCardId = statusCardId;
            this.kind = kind;
            this.amount = Math.Max(0, amount);
            this.durationTurns = Math.Max(0, durationTurns);
            this.monsterDefinitionId = monsterDefinitionId;
        }

        public HexTrapEffectKind Kind => kind;
        public int Amount => Math.Max(0, amount);
        public int DurationTurns => Math.Max(0, durationTurns);
        public string MonsterDefinitionId => monsterDefinitionId ?? string.Empty;
        public string StatusCardId => statusCardId ?? string.Empty;
        public HexTrapEffectData ToRuntimeEffectData() => new HexTrapEffectData(Kind, Amount, DurationTurns, MonsterDefinitionId, StatusCardId);
    }

    [Serializable]
    public sealed class HexTrapRef
    {
        [SerializeField] private string trapId = "trap-ref";
        [SerializeField] private int column;
        [SerializeField] private int row;
        [SerializeField] private int radius;
        [SerializeField] private bool affectsPlayer = true;
        [SerializeField] private bool affectsMonsters;
        [SerializeField] private bool oneShot = true;
        [SerializeField] private bool triggerOnEnter = true;
        [Tooltip("0 = 밟기형(기존). N>0 = N턴마다 스스로 터지는 예고형(C-11) — 밟아도 터지지 않으며 " +
                 "발동 예정인 턴에 위험 오버레이로 예고된다. 예고형은 triggerOnEnter를 켜도 밟기로 터지지 않는다.")]
        [SerializeField] private int periodTurns;
        [SerializeField] private List<HexTrapEffectRef> effects = new List<HexTrapEffectRef> { new HexTrapEffectRef(HexTrapEffectKind.Damage, 1) };
        [Tooltip("Optional TrapPresetCatalog preset id. When set and resolvable, the preset's effect/radius/" +
                 "targeting overrides the inline values at map build time. The inline values are kept as a " +
                 "snapshot fallback so traps still work when the preset catalog is unavailable.")]
        [SerializeField] private string presetId;
        [Tooltip("배치 랜덤화 그룹 태그(placement-randomization-plan §2-3a). 빈 문자열 = 현행 고정 배치. " +
                 "P2에서 함정 풀 추첨의 슬롯 태그로 쓴다(함정은 presetId 치환).")]
        [SerializeField] private string randomizationGroup = "";

        public HexTrapRef()
        {
        }

        public HexTrapRef(
            string trapId,
            int column,
            int row,
            int radius,
            IEnumerable<HexTrapEffectRef> effects,
            bool affectsPlayer = true,
            bool affectsMonsters = false,
            bool oneShot = true,
            bool triggerOnEnter = true,
            string presetId = null,
            int periodTurns = 0,
            string randomizationGroup = "")
        {
            this.randomizationGroup = randomizationGroup ?? string.Empty;
            this.periodTurns = Math.Max(0, periodTurns);
            this.trapId = trapId;
            this.column = column;
            this.row = row;
            this.radius = Math.Max(0, radius);
            this.effects = effects == null ? new List<HexTrapEffectRef>() : new List<HexTrapEffectRef>(effects.Where(effect => effect != null));
            this.affectsPlayer = affectsPlayer;
            this.affectsMonsters = affectsMonsters;
            this.oneShot = oneShot;
            this.triggerOnEnter = triggerOnEnter;
            this.presetId = presetId;
        }

        /// <summary>
        /// 그룹 태그만 바꾼 전(全)필드 사본(HexMapObjectRef.WithRandomizationGroup과 같은 계약).
        /// 필드를 골라 다시 조립하면 빠뜨린 필드가 조용히 초기화된다 — P2 태깅은 반드시 이 사본을 쓴다.
        /// </summary>
        public HexTrapRef WithRandomizationGroup(string nextRandomizationGroup)
        {
            return new HexTrapRef(
                trapId,
                column,
                row,
                radius,
                effects,
                affectsPlayer,
                affectsMonsters,
                oneShot,
                triggerOnEnter,
                presetId,
                periodTurns,
                nextRandomizationGroup ?? string.Empty);
        }

        /// <summary>
        /// P2 함정 예비 슬롯(Q10, 몬스터와 동형): 그룹 태그는 있으나 presetId도 effects도 없다.
        /// 맵 빌드는 건너뛰고(TryBuildTrapRefs), 랜덤화 브리지가 위치 후보로만 읽어 간다.
        /// 태그 없는 빈 함정은 여전히 빌드 에러다(안전망 유지, §6-4).
        /// </summary>
        public bool IsRandomizationSpareSlot =>
            !string.IsNullOrWhiteSpace(RandomizationGroup) &&
            string.IsNullOrWhiteSpace(PresetId) &&
            Effects.Count == 0;

        public string TrapId => trapId ?? string.Empty;
        public HexCoord Coord => new HexCoord(column, row);
        public int Radius => Math.Max(0, radius);
        public bool AffectsPlayer => affectsPlayer;
        public bool AffectsMonsters => affectsMonsters;
        public bool OneShot => oneShot;
        public bool TriggerOnEnter => triggerOnEnter;
        public string PresetId => presetId ?? string.Empty;
        /// <summary>배치 랜덤화 그룹(빈 문자열 = 고정 배치). §2-3a — 함정 추첨은 P2.</summary>
        public string RandomizationGroup => randomizationGroup ?? string.Empty;
        public int PeriodTurns => Math.Max(0, periodTurns);
        public IReadOnlyList<HexTrapEffectRef> Effects => effects ?? (IReadOnlyList<HexTrapEffectRef>)Array.Empty<HexTrapEffectRef>();

        /// <summary>
        /// Builds the runtime trap. When <paramref name="presetCatalog"/> is supplied and this trap
        /// references a resolvable preset, the preset's effect/radius/targeting is authoritative
        /// (single source of truth); otherwise the inline values are used as a fallback snapshot.
        /// </summary>
        public HexTrapData ToRuntimeTrapData(TrapPresetCatalog presetCatalog = null)
        {
            if (presetCatalog != null &&
                !string.IsNullOrWhiteSpace(presetId) &&
                presetCatalog.TryGet(presetId, out var preset))
            {
                return new HexTrapData(
                    TrapId,
                    Coord,
                    preset.Radius,
                    new[] { new HexTrapEffectData(preset.EffectKind, preset.EffectAmount, preset.DurationTurns, preset.MonsterDefinitionId, preset.StatusCardId) },
                    preset.AffectsPlayer,
                    preset.AffectsMonsters,
                    preset.OneShot,
                    preset.TriggerOnEnter,
                    preset.PeriodTurns,
                    // 종류를 런타임까지 들려 보낸다 — 도감 해금이 이 값을 키로 쓴다(P3).
                    preset.PresetId);
            }

            return new HexTrapData(
                TrapId,
                Coord,
                Radius,
                Effects.Where(effect => effect != null).Select(effect => effect.ToRuntimeEffectData()),
                AffectsPlayer,
                AffectsMonsters,
                OneShot,
                TriggerOnEnter,
                PeriodTurns,
                // 프리셋을 못 찾은 인라인 폴백에서도 저작된 presetId는 그대로 실어 보낸다 —
                // 카탈로그가 없는 것과 종류를 모르는 것은 다른 일이다.
                PresetId);
        }
    }
}
