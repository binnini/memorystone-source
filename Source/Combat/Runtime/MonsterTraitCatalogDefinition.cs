using System;
using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>배지를 언제 띄우는가. 저작만 있으면 상시(always) / 지금 발동 중일 때만(active).</summary>
    public enum MonsterTraitBadgeVisibility
    {
        Always,
        Active,
    }

    /// <summary>
    /// 배지 호버가 판에 그릴 「특성의 범위」를 무엇으로 유도하는가. 칸을 세는 규칙은 각각 다르지만
    /// 그리는 레이어는 하나다(보라 채움) — 어휘가 같기 때문이다: <b>손을 얹은 동안에만 뜨는,
    /// 이 특성이 미치는 자리</b>.
    /// </summary>
    public enum MonsterTraitReachKind
    {
        /// <summary>범위가 없는 특성(견고·맷집·약오름·은신·소매치기·밀어붙이기).</summary>
        None,

        /// <summary>뒤끝 — 죽은 자리를 중심으로 저작된 반경.</summary>
        Aftermath,

        /// <summary>담력 시험 — 힘이 상한에 닿는 거리 이상의 칸(「여기 서면 세진다」).</summary>
        DistanceStrength,

        /// <summary>홀림 — 오라 반경(그 안에 서 있으면 잠긴다).</summary>
        AuraRadius,
    }

    /// <summary>몬스터 특성 한 줄(monster_traits.csv). 설명문은 여기 들지 않는다 —
    /// <see cref="KeywordRef"/>가 game_keywords.csv 행을 가리키고, 문안 정본은 그 한 파일이다.</summary>
    public sealed class MonsterTraitDefinition
    {
        public MonsterTraitDefinition(
            string traitId,
            string displayName,
            string keywordRef,
            string glyph,
            string badgeColorHex,
            int badgeSortRank,
            MonsterTraitBadgeVisibility visibility,
            MonsterTraitReachKind reach,
            string announceRef,
            string designerNote)
        {
            TraitId = traitId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            KeywordRef = keywordRef ?? string.Empty;
            Glyph = glyph ?? string.Empty;
            BadgeColorHex = badgeColorHex ?? string.Empty;
            BadgeSortRank = badgeSortRank;
            Visibility = visibility;
            Reach = reach;
            AnnounceRef = announceRef ?? string.Empty;
            DesignerNote = designerNote ?? string.Empty;
        }

        public string TraitId { get; }

        /// <summary>배지 툴팁 제목. 어휘 절이 싣는 이름은 키워드 행이 들고 있다(보통 같은 낱말).</summary>
        public string DisplayName { get; }

        /// <summary>game_keywords.csv 「키워드」. 임포트 시 실재가 검증된다 — 오타가 「조용히 설명 없음」으로 새지 않는다.</summary>
        public string KeywordRef { get; }

        /// <summary>스프라이트가 아직 없을 때의 칩 글자.</summary>
        public string Glyph { get; }

        /// <summary>칩 배경색(RRGGBB). 코어는 UnityEngine을 모르므로 문자열로 든다 — 표현층이 푼다.</summary>
        public string BadgeColorHex { get; }

        public int BadgeSortRank { get; }
        public MonsterTraitBadgeVisibility Visibility { get; }
        public MonsterTraitReachKind Reach { get; }

        /// <summary>발동 알림 ref(<see cref="MonsterTraitAnnouncement"/>). 비어 있으면 알림이 없는 특성.</summary>
        public string AnnounceRef { get; }

        public string DesignerNote { get; }
    }

    /// <summary>
    /// 특성 <b>배선</b>의 정본(monster_traits.csv). <c>status_effects.csv</c>의 형제이며,
    /// 설명문은 <c>game_keywords.csv</c> 한 파일에 남는다 — 두 축을 나눠야 「같은 말을 두 곳에 적는」
    /// 사고가 구조적으로 안 난다.
    /// </summary>
    public sealed class MonsterTraitCatalogDefinition
    {
        private readonly Dictionary<string, MonsterTraitDefinition> byId;

        public MonsterTraitCatalogDefinition(IReadOnlyList<MonsterTraitDefinition> entries)
        {
            Entries = entries ?? Array.Empty<MonsterTraitDefinition>();
            byId = new Dictionary<string, MonsterTraitDefinition>(Entries.Count, StringComparer.Ordinal);
            for (var i = 0; i < Entries.Count; i++)
            {
                byId[Entries[i].TraitId] = Entries[i];
            }
        }

        public IReadOnlyList<MonsterTraitDefinition> Entries { get; }

        public bool TryGet(string traitId, out MonsterTraitDefinition definition)
        {
            definition = null;
            return !string.IsNullOrWhiteSpace(traitId) && byId.TryGetValue(traitId.Trim(), out definition);
        }

        /// <summary>
        /// 모든 <see cref="MonsterTraitDefinition.KeywordRef"/>가 키워드 카탈로그에 실재하는가.
        /// 코어는 카탈로그를 <b>인자로만</b> 받는다(전역 조회 금지 — 테스트가 픽스처를 넣을 수 있어야 한다).
        /// </summary>
        public IReadOnlyList<string> ValidateKeywordRefs(KeywordCatalogDefinition keywords)
        {
            var errors = new List<string>();
            if (keywords == null)
            {
                errors.Add("키워드 카탈로그가 없어 특성 설명문 참조를 검증할 수 없다.");
                return errors;
            }

            for (var i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                if (string.IsNullOrWhiteSpace(entry.KeywordRef))
                {
                    errors.Add($"특성 '{entry.TraitId}': keywordRef가 비어 있다 — 설명문이 없는 특성은 화면에서 이름만 뜬다.");
                    continue;
                }

                if (!keywords.TryGet(entry.KeywordRef, out _))
                {
                    errors.Add(
                        $"특성 '{entry.TraitId}': keywordRef '{entry.KeywordRef}' 행이 game_keywords.csv에 없다 "
                        + "— 조회 실패는 예외가 아니라 조용한 생략이라 화면에서 설명이 통째로 빠진다.");
                }
            }

            return errors;
        }
    }

    /// <summary>
    /// 프로세스 전역 특성 카탈로그. <see cref="CardKeywordCatalogProvider"/>·
    /// <see cref="StatusEffectCatalogProvider"/>와 같은 규약 — Unity 부트스트랩이 TextAsset에서 심고,
    /// 순수 계층은 여기를 통해 읽는다.
    ///
    /// <para>🔴 <b>폴백 표를 두지 않는다.</b> 「카탈로그가 없으면 코드에 박힌 기본값」은 이 저장소가
    /// 반복해서 밟은 두 벌 함정이고, 이번 트랙이 걷어내는 것이 정확히 그것이다. 미할당은 조용히
    /// 넘어가지 말아야 하므로 씬 배선 게이트가 잡는다(<c>MonsterTraitCatalogWiringTests</c>).</para>
    /// </summary>
    public static class MonsterTraitCatalogProvider
    {
        public static MonsterTraitCatalogDefinition Active { get; set; }
    }
}
