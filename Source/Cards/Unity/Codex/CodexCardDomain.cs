using System.Collections.Generic;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 카드 도메인. 저주(<see cref="CardEffectType.Status"/>)도 여기 산다 — 유물이 아니라
    /// <b>카드의 한 종류</b>라는 것이 확정 사항이고, 저작(<c>cards.csv</c>)도 그렇게 돼 있다.
    /// <para>
    /// 썸네일을 만들지 않고 <see cref="CodexEntry.CardVisual"/>만 채운다. 카드는 프레임째 그려야
    /// 하고 그 일은 <c>CardFront</c> 프리팹이 이미 한다 — 도감이 다시 그리면 전투 카드가 바뀔 때
    /// 도감 카드만 낡는다(<c>docs/codex-plan.md</c> §3-2).
    /// </para>
    /// </summary>
    public sealed class CodexCardDomain : ICodexDomain
    {
        /// <summary>전투가 신호를 보낼 때 쓰는 것과 <b>같은 문자열</b>이어야 한다 —
        /// 그래서 값을 여기 적지 않고 아래층 상수를 가리킨다(P3 §5-5 지뢰).</summary>
        public const string DomainId = Combat.Runtime.CodexDomainIds.Card;

        private readonly List<CodexEntry> entries = new List<CodexEntry>();

        /// <summary>
        /// 범위 도해용 가상 아레나. 도메인 하나가 <b>한 벌만</b> 갖고 카드 59장이 나눠 쓴다.
        /// 첫 도해를 볼 때까지 만들지 않는다 — 로비를 여는 값이 아니라 도감을 여는 값이어야 한다.
        /// </summary>
        private CodexRangeArena arena;

        private CardCatalogDefinition rangeCatalog;

        public CodexCardDomain(CardCatalogDefinition catalog, Color accent)
        {
            Accent = accent;
            rangeCatalog = catalog;
            Build(catalog);
        }

        private CodexRangeArena Arena => arena ??= CodexRangeArena.Create(rangeCatalog);

        public string Id => DomainId;

        public string Label => "카드";

        public Color Accent { get; }

        /// <summary>카드는 손에 들어왔을 때 열린다 — P3에서 해금 집합이 붙는다.</summary>
        public bool AlwaysUnlocked => false;

        public IReadOnlyList<CodexEntry> Entries => entries;

        private void Build(CardCatalogDefinition catalog)
        {
            if (catalog == null)
            {
                return;
            }

            // ⚠️ 여기서 GetVisibleCatalogEntries()를 쓰면 안 된다.
            // `visibleInCatalog=FALSE`가 붙은 카드는 출하 저작에서 **저주 12종이 전부**이고,
            // 그 뜻은 "덱·보상 목록에 내놓지 않는다"이지 "도감에서 감춘다"가 아니다
            // (플래그를 읽는 출하 코드가 아직 하나도 없어 뜻이 굳지 않았다).
            // 저주는 카드의 한 종류로 도감에 실린다는 것이 확정 사항이므로, 도감은
            // **저작 승인 여부**만 본다. 두 뜻이 갈라지면 컬럼을 새로 파야 한다.
            foreach (var entry in catalog.Entries)
            {
                if (entry == null || entry.Status != CardCatalogStatus.Approved)
                {
                    continue;
                }

                var snapshot = CodexCardSnapshotFactory.FromCatalogEntry(entry, catalog.SourceId);

                entries.Add(new CodexEntry(
                    id: entry.Id,
                    displayName: entry.DisplayName,
                    // 카드는 프레임째 그리므로 썸네일 층을 타지 않는다. 폴백 라벨은 카드 프리팹이
                    // 없을 때(배선 누락)만 보이는 최후 표시다.
                    thumbnail: CodexThumbnail.Resolve(null, entry.DisplayName, Accent),
                    subtitle: $"{entry.Id} · {KindLabel(entry)} · {entry.Rarity}",
                    // 상세 패널도 카드와 같은 문장을 보여야 한다 — 토큰이 남은 설명은 카드가 아니라
                    // 저작 원본이다.
                    description: snapshot.Description,
                    filterChip: KindLabel(entry),
                    metaChips: BuildMetaChips(entry),
                    detailRows: BuildDetailRows(entry),
                    debugRows: BuildDebugRows(entry),
                    cardVisual: snapshot,
                    rangeSource: new CodexCardRangeSource(() => Arena, entry)));
            }
        }

        /// <summary>
        /// 필터 칩과 부제에 쓰는 한글 종류. 🔴저주는 <see cref="CardEffectType.Status"/>이고
        /// <see cref="CombatCardKind"/>로 옮기면 <c>Move</c>로 뭉개지므로 <b>여기서 먼저 가른다</b>.
        /// </summary>
        private static string KindLabel(CardCatalogEntry entry)
        {
            if (entry.ActionType == CardEffectType.Status)
            {
                return "저주";
            }

            return CardFrontPresentationFormatting.KindToKorean(
                CodexCardSnapshotFactory.ToCombatCardKind(entry.ActionType));
        }

        /// <summary>
        /// 격자 셀 밑에 붙는 수치. <b>기력·사거리는 넣지 않는다</b> — 카드 그림이 이미 말한다.
        /// </summary>
        private static IReadOnlyList<string> BuildMetaChips(CardCatalogEntry entry)
        {
            var chips = new List<string>(2);

            if (entry.ExhaustOnPlay)
            {
                chips.Add("소멸");
            }

            if (entry.RetainOnTurnEnd)
            {
                chips.Add("유지");
            }

            return chips;
        }

        private static IReadOnlyList<CodexDetailRow> BuildDetailRows(CardCatalogEntry entry)
        {
            var rows = new List<CodexDetailRow>(6)
            {
                new CodexDetailRow("등급", entry.Rarity.ToString()),
            };

            if (entry.Amount > 0)
            {
                rows.Add(new CodexDetailRow("수치", entry.Amount.ToString()));
            }

            if (entry.HitCount > 1)
            {
                rows.Add(new CodexDetailRow("타격 수", entry.HitCount.ToString()));
            }

            if (entry.AreaRadius > 0)
            {
                rows.Add(new CodexDetailRow("범위 반경", entry.AreaRadius.ToString()));
            }

            if (entry.DurationTurns > 0)
            {
                rows.Add(new CodexDetailRow("지속", $"{entry.DurationTurns}턴"));
            }

            if (!string.IsNullOrWhiteSpace(entry.StateEffect))
            {
                rows.Add(new CodexDetailRow("상태 부여", entry.StateEffect));
            }

            if (entry.UsableWhileStunned)
            {
                rows.Add(new CodexDetailRow("기절 중 사용", "가능"));
            }

            return rows;
        }

        private static IReadOnlyList<CodexDetailRow> BuildDebugRows(CardCatalogEntry entry)
        {
            return new[]
            {
                new CodexDetailRow("effectRef", entry.EffectRef),
                new CodexDetailRow("targeting", entry.Targeting),
                new CodexDetailRow("targetMode", entry.TargetMode.ToString()),
                new CodexDetailRow("playMode", entry.PlayMode.ToString()),
                new CodexDetailRow("shapeId", entry.ShapeId),
                new CodexDetailRow("illustrationId", entry.PresentationRef.IllustrationId),
                new CodexDetailRow("behaviorParams", entry.BehaviorParams),
                new CodexDetailRow("choiceOptions", entry.ChoiceOptions),
                new CodexDetailRow("status", entry.Status.ToString()),
                new CodexDetailRow("includeInDecks", entry.IncludeInGameplayDecks ? "TRUE" : "FALSE"),
            };
        }
    }
}
