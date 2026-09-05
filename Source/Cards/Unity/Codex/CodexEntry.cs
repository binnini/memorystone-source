using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 도감 한 항목의 표시 모델. 도메인 어댑터(<see cref="ICodexDomain"/>)가 저작 카탈로그를 읽어
    /// 이걸로 바꾸고, 뷰는 이것만 본다 — 뷰가 카탈로그 타입을 알지 못하므로 도메인을 늘려도
    /// 셸은 그대로다.
    /// <para>
    /// 계획 정본은 <c>docs/codex-plan.md</c>. 이 어셈블리(<c>SeoulPlayup.Cards.Unity</c>)에 있는 이유는
    /// 도감이 카드 UI라서가 아니라, <b>EditMode 테스트가 이미 참조하는 어셈블리</b>이면서
    /// <c>StatusEffectIconCatalog</c>가 여기 살기 때문이다(그 파일도 네임스페이스는 Combat.Unity다).
    /// 뷰는 <c>SeoulPlayup.Flow</c>에 있다.
    /// </para>
    /// </summary>
    public sealed class CodexEntry
    {
        private static readonly IReadOnlyList<string> NoChips = Array.Empty<string>();
        private static readonly IReadOnlyList<CodexDetailRow> NoRows = Array.Empty<CodexDetailRow>();

        public CodexEntry(
            string id,
            string displayName,
            CodexThumbnail thumbnail,
            string subtitle = "",
            string description = "",
            string filterChip = "",
            IReadOnlyList<string> metaChips = null,
            IReadOnlyList<CodexDetailRow> detailRows = null,
            IReadOnlyList<CodexDetailRow> debugRows = null,
            string authoringWarning = "",
            Combat.Runtime.CombatCardSnapshot? cardVisual = null,
            ICodexRangeSource rangeSource = null)
        {
            CardVisual = cardVisual;
            RangeSource = rangeSource;
            Id = id ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
            Thumbnail = thumbnail;
            Subtitle = subtitle ?? string.Empty;
            Description = description ?? string.Empty;
            FilterChip = filterChip ?? string.Empty;
            MetaChips = metaChips ?? NoChips;
            DetailRows = detailRows ?? NoRows;
            DebugRows = debugRows ?? NoRows;
            AuthoringWarning = authoringWarning ?? string.Empty;
        }

        /// <summary>안정 식별자. 해금 집합의 키이자 검색 대상이다.</summary>
        public string Id { get; }

        public string DisplayName { get; }

        /// <summary>상세 패널 제목 밑에 붙는 한 줄(예: <c>Poison · 상태이상</c>).</summary>
        public string Subtitle { get; }

        public string Description { get; }

        /// <summary>목록 필터 칩이 묶는 값. 빈 문자열이면 이 항목은 어떤 칩에도 걸리지 않는다.</summary>
        public string FilterChip { get; }

        /// <summary>격자 셀 아래에 붙는 짧은 수치들. 그림이 이미 말하는 값은 넣지 않는다.</summary>
        public IReadOnlyList<string> MetaChips { get; }

        public IReadOnlyList<CodexDetailRow> DetailRows { get; }

        /// <summary>디버그 뷰에서만 덧붙는 원값·배선 키. 도감 뷰는 이걸 그리지 않는다.</summary>
        public IReadOnlyList<CodexDetailRow> DebugRows { get; }

        /// <summary>
        /// 디버그 뷰에 ⚑로 뜨는 저작 경고(폐기된 값, 미바인딩 등). 빈 문자열이면 경고 없음.
        /// </summary>
        public string AuthoringWarning { get; }

        public CodexThumbnail Thumbnail { get; }

        /// <summary>
        /// 카드 도메인만 채운다. 값이 있으면 뷰는 <see cref="Thumbnail"/> 대신
        /// <c>CardFront</c> 프리팹을 세워 <b>카드 한 장 통째로</b> 그린다 — 일러만 잘라 쓰면
        /// 기력·사거리를 셀에 따로 적어야 하고, 그건 카드가 이미 하고 있는 말이다.
        /// </summary>
        public Combat.Runtime.CombatCardSnapshot? CardVisual { get; }

        /// <summary>
        /// 범위 도해를 낼 수 있는 항목만 채운다(카드·몬스터 패턴·함정·소모품). <c>null</c>이면
        /// 상세 패널에 도해 절이 아예 생기지 않는다 — 빈 격자를 그리면 "범위가 0"으로 읽힌다.
        /// <para>지연 계산이다. 목록을 여는 것만으로 59장의 범위를 다 풀지 않는다.</para>
        /// </summary>
        public ICodexRangeSource RangeSource { get; }

        /// <summary>
        /// 검색 대상 문자열. 이름·id·부제·설명을 합친다 — 사용자가 무엇으로 찾을지 모르기 때문이다.
        /// </summary>
        public string SearchHaystack =>
            searchHaystack ??= $"{DisplayName}\n{Id}\n{Subtitle}\n{Description}".ToLowerInvariant();

        private string searchHaystack;
    }

    /// <summary>상세 패널의 한 줄. 값은 이미 표시용으로 다듬어진 문자열이다.</summary>
    public readonly struct CodexDetailRow
    {
        public CodexDetailRow(string label, string value)
        {
            Label = label ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string Label { get; }
        public string Value { get; }

        public bool IsEmpty => string.IsNullOrWhiteSpace(Value);
    }

    /// <summary>
    /// 도감 도메인 하나(카드·상태이상·유물…). 뷰는 이 인터페이스만 알고, 어떤 CSV에서 왔는지는
    /// 모른다.
    /// </summary>
    public interface ICodexDomain
    {
        /// <summary>레일 버튼과 저장 키에 쓰는 안정 식별자.</summary>
        string Id { get; }

        string Label { get; }

        /// <summary>레일 표시등·썸네일 폴백 색조.</summary>
        Color Accent { get; }

        /// <summary>
        /// 해금을 타지 않고 항상 열려 있는 도메인인가. 상태이상·버프가 그렇다(Q6 확정) —
        /// 규칙 참조표를 잠그면 플레이어가 규칙을 못 읽는다.
        /// </summary>
        bool AlwaysUnlocked { get; }

        IReadOnlyList<CodexEntry> Entries { get; }
    }
}
