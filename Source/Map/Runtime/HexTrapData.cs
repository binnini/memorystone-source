using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Map.Runtime
{
    [Serializable]
    public readonly struct HexTrapData
    {
        public HexTrapData(
            string trapId,
            HexCoord coord,
            int radius,
            IEnumerable<HexTrapEffectData> effects,
            bool affectsPlayer = true,
            bool affectsMonsters = false,
            bool oneShot = true,
            bool triggerOnEnter = true,
            int periodTurns = 0,
            string presetId = null)
        {
            PeriodTurns = Math.Max(0, periodTurns);
            TrapId = string.IsNullOrWhiteSpace(trapId) ? string.Empty : trapId.Trim();
            PresetId = string.IsNullOrWhiteSpace(presetId) ? string.Empty : presetId.Trim();
            Coord = coord;
            Radius = Math.Max(0, radius);
            Effects = effects == null
                ? Array.Empty<HexTrapEffectData>()
                : effects.Where(effect => effect.IsConfigured).ToArray();
            AffectsPlayer = affectsPlayer;
            AffectsMonsters = affectsMonsters;
            OneShot = oneShot;
            TriggerOnEnter = triggerOnEnter;
        }

        public string TrapId { get; }

        /// <summary>
        /// 이 함정이 어느 저작 프리셋(<c>TrapPresetCatalog</c>)에서 왔는가. <see cref="TrapId"/>가
        /// 판 위의 <b>이 한 개</b>를 가리키는 인스턴스 id인 것과 달리, 이쪽은 <b>종류</b>다.
        /// <para>
        /// 🔑도감 해금이 이 값을 키로 쓴다(<c>CodexTrapDomain</c>의 항목 id = <c>preset.PresetId</c>).
        /// 원래 이 필드가 없어서 저작 시점의 presetId가 런타임 변환에서 버려졌고, 전투는 밟은 함정이
        /// 무슨 <b>종류</b>였는지 말할 방법이 없었다 — P3에서 뚫은 배관이다.
        /// </para>
        /// <para>비어 있으면 프리셋 없이 만들어진 함정이다(런타임 배치 · 인라인 저작 폴백).</para>
        /// </summary>
        public string PresetId { get; }

        public HexCoord Coord { get; }
        public int Radius { get; }
        public IReadOnlyList<HexTrapEffectData> Effects { get; }
        public bool AffectsPlayer { get; }
        public bool AffectsMonsters { get; }
        public bool OneShot { get; }
        public bool TriggerOnEnter { get; }

        /// <summary>
        /// 주기 발동(C-11 / D-13). 0이면 기존 <b>밟기형</b>, N&gt;0이면 N턴마다 스스로 터지는 <b>예고형</b>이다.
        /// 예고형은 <see cref="TriggerOnEnter"/>와 무관하게 밟아도 터지지 않는다 — 두 트리거를 겸하는
        /// 저작은 성립하지 않으며, 그 조합은 <c>ShippingMapTrapAuditTests</c>가 막는다.
        /// 발동 시점은 런타임 카운터가 아니라 <c>OverallTurnNumber</c>에서 유도한다(저장 상태 없음).
        /// </summary>
        public int PeriodTurns { get; }

        public bool IsPeriodic => PeriodTurns > 0;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(TrapId) && Effects.Count > 0 && (AffectsPlayer || AffectsMonsters);
        public bool Contains(HexCoord coord) => Coord.DistanceTo(coord) <= Radius;
    }
}
