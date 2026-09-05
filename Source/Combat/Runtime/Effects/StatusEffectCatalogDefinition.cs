using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    public sealed class StatusEffectCatalogDefinition
    {
        private readonly Dictionary<StatusEffectKind, StatusEffectDefinition> byKind;

        public StatusEffectCatalogDefinition(IEnumerable<StatusEffectDefinition> entries)
        {
            Entries = (entries ?? Array.Empty<StatusEffectDefinition>()).ToList();
            byKind = Entries.ToDictionary(entry => entry.Kind);
        }

        public IReadOnlyList<StatusEffectDefinition> Entries { get; }

        public bool TryGet(StatusEffectKind kind, out StatusEffectDefinition definition)
        {
            return byKind.TryGetValue(kind, out definition);
        }
    }

    /// <summary>
    /// Process-wide active status-effect catalog, assigned by the Unity bootstrap from
    /// <c>status_effects.csv</c>. Mirrors <see cref="CardKeywordCatalogProvider"/>: the pure-C# combat
    /// layers read defaults through <see cref="StatusEffectInfo.DefaultAmount"/> without taking a
    /// dependency on Unity asset loading, and stay on the hardcoded fallback when nothing is assigned.
    /// </summary>
    public static class StatusEffectCatalogProvider
    {
        public static StatusEffectCatalogDefinition Active { get; set; }
    }

    public sealed class StatusEffectDefinition
    {
        public StatusEffectDefinition(
            StatusEffectKind kind,
            string displayNameKo,
            string descriptionKo,
            int defaultAmount,
            int defaultDurationTurns,
            StatusEffectValueMode valueMode,
            StatusEffectTiming timing,
            StatusEffectStackPolicy stackPolicy,
            int maxStacks,
            StatusEffectExpirePolicy expirePolicy)
        {
            Kind = kind;
            DisplayNameKo = displayNameKo ?? string.Empty;
            DescriptionKo = descriptionKo ?? string.Empty;
            DefaultAmount = Math.Max(0, defaultAmount);
            DefaultDurationTurns = Math.Max(0, defaultDurationTurns);
            ValueMode = valueMode;
            Timing = timing;
            StackPolicy = stackPolicy;
            MaxStacks = Math.Max(1, maxStacks);
            ExpirePolicy = expirePolicy;

            // 표현 속성 4개는 위치 인자에 끼우지 않는다 — 이 저장소는 「위치 인자 재조립이 새 컬럼을
            // 0으로 떨어뜨린」 사고를 세 번 겪었다. 빌더(WithPresentation)로 붙이고, 안 붙인 정의는
            // 0/빈 문자열이 아니라 <b>폴백 switch 값</b>을 갖는다(카탈로그 없는 표면과 같은 값).
            Glyph = StatusEffectInfo.FallbackGlyph(kind);
            BadgeSortRank = StatusEffectInfo.FallbackBadgeSortRank(kind);
            FloatingTextTemplate = StatusEffectInfo.FallbackFloatingTextTemplate(kind);
            ApplyAudioCueId = StatusEffectInfo.FallbackApplyAudioCueId(kind);
        }

        private StatusEffectDefinition(StatusEffectDefinition source, string glyph, int badgeSortRank, string floatingTextTemplate, string applyAudioCueId)
        {
            Kind = source.Kind;
            DisplayNameKo = source.DisplayNameKo;
            DescriptionKo = source.DescriptionKo;
            DefaultAmount = source.DefaultAmount;
            DefaultDurationTurns = source.DefaultDurationTurns;
            ValueMode = source.ValueMode;
            Timing = source.Timing;
            StackPolicy = source.StackPolicy;
            MaxStacks = source.MaxStacks;
            ExpirePolicy = source.ExpirePolicy;
            Glyph = glyph ?? string.Empty;
            BadgeSortRank = badgeSortRank;
            FloatingTextTemplate = floatingTextTemplate ?? string.Empty;
            ApplyAudioCueId = applyAudioCueId ?? string.Empty;
        }

        /// <summary>
        /// 표현 레지스트리(1단계 구조 리팩토링): 글리프·배지 정렬 순위·플로팅 문안 템플릿·부여 SFX 큐 id.
        /// 규칙이 아니라 <b>표현</b>이라 색(UnityEngine.Color)은 여기 두지 않는다 — 색 3벌은 사용자 결정으로
        /// 각 표면의 switch에 존치한다. 값 의미는 <see cref="StatusEffectInfo.Glyph"/> 등 접근자 문서 참고.
        /// </summary>
        public StatusEffectDefinition WithPresentation(string glyph, int badgeSortRank, string floatingTextTemplate, string applyAudioCueId)
        {
            return new StatusEffectDefinition(this, glyph, badgeSortRank, floatingTextTemplate, applyAudioCueId);
        }

        public StatusEffectKind Kind { get; }
        public string DisplayNameKo { get; }
        public string DescriptionKo { get; }
        public int DefaultAmount { get; }
        public int DefaultDurationTurns { get; }
        public StatusEffectValueMode ValueMode { get; }
        public StatusEffectTiming Timing { get; }
        public StatusEffectStackPolicy StackPolicy { get; }

        /// <summary>
        /// 한 유닛이 동시에 가질 수 있는 이 종류의 인스턴스 수. 현행 두 stackPolicy가 모두 하나로
        /// 병합하므로 실효값은 항상 1이고, 런타임 클램프는 <b>발동하지 않는 백스톱</b>이다 —
        /// "배선했지만 동작은 안 변했다"의 증거이자, 다중 인스턴스 정책이 생기는 날 값을 받을 자리.
        /// </summary>
        public int MaxStacks { get; }

        public StatusEffectExpirePolicy ExpirePolicy { get; }

        /// <summary>스프라이트가 없을 때 아이콘 칸에 쓰는 한 글자 폴백(CSV <c>glyph</c>).</summary>
        public string Glyph { get; }

        /// <summary>네임플레이트 배지 정렬 순위(CSV <c>badgeSortRank</c>, 작을수록 앞). 제어 4 · 지속피해 5 · 기타 디버프 6 · 버프 7.</summary>
        public int BadgeSortRank { get; }

        /// <summary>부여 플로팅 텍스트 템플릿(CSV <c>floatingTextTemplate</c>). 문법은 <see cref="StatusEffectInfo.FormatFloatingText"/>.</summary>
        public string FloatingTextTemplate { get; }

        /// <summary>부여 시 SFX 큐 id(CSV <c>applyAudioCueId</c>). 빈 값 = 무음(결함이 아니라 현행 저작).</summary>
        public string ApplyAudioCueId { get; }
    }
}
