using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Codex;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode.Codex
{
    /// <summary>
    /// Q4·Q7 규칙이 <b>화면이 실제로 쓰는 조합</b>에서도 성립하는지(P3 완료 조건 3·5).
    /// <para>
    /// <c>CodexListQueryTests</c>는 규칙 자체를 손으로 만든 술어로 못 박아 뒀다. 여기서 다른 것은
    /// <b>술어의 출처</b>다 — <c>CodexOverlayView</c>는 <c>CodexProgress.PredicateFor(domain)</c>이
    /// 만든 것을 그대로 <c>Apply</c>·<c>Tally</c>에 넘긴다. P0에서 규칙이 서 있었는데도 화면이
    /// 전량 공개였던 이유가 바로 이 연결이 없어서였으므로, 규칙과 화면 사이의 이 한 칸을 잰다.
    /// </para>
    /// </summary>
    public sealed class CodexUnlockScreenRulesTests
    {
        private sealed class FakeDomain : ICodexDomain
        {
            public FakeDomain(string id, bool alwaysUnlocked, params CodexEntry[] entries)
            {
                Id = id;
                AlwaysUnlocked = alwaysUnlocked;
                Entries = entries;
            }

            public string Id { get; }
            public string Label => Id;
            public Color Accent => Color.white;
            public bool AlwaysUnlocked { get; }
            public IReadOnlyList<CodexEntry> Entries { get; }
        }

        private static CodexEntry Entry(string id, string name, string chip = "")
        {
            return new CodexEntry(id, name, CodexThumbnail.Resolve(null, name, Color.white), filterChip: chip);
        }

        private static FakeDomain Cards()
        {
            return new FakeDomain(
                CodexDomainIds.Card,
                alwaysUnlocked: false,
                Entry("C001", "발차기"),
                Entry("C002", "구르기"),
                Entry("C003", "숨돌리기"));
        }

        [Test]
        public void LockedEntriesKeepTheirPlaceInTheGrid()
        {
            // Q7 — 자리는 남긴다. 잠긴 칸이 목록에서 빠지면 "몇 개나 더 있나"를 알 길이 없다.
            var domain = Cards();
            var progress = new CodexProgress();
            progress.MarkSeen(CodexDomainIds.Card, "C002");

            var rows = CodexListQuery.Apply(domain.Entries, null, null, progress.PredicateFor(domain));

            CollectionAssert.AreEqual(new[] { "C001", "C002", "C003" }, rows.Select(row => row.Id).ToList());
        }

        [Test]
        public void SearchNeverSurfacesALockedEntry()
        {
            // Q4 — 이름을 모르는 것이 해금의 전부다. 검색으로 새어 나오면 잠금이 장식이 된다.
            var domain = Cards();
            var progress = new CodexProgress();
            progress.MarkSeen(CodexDomainIds.Card, "C002");

            var rows = CodexListQuery.Apply(domain.Entries, null, "구르기", progress.PredicateFor(domain));
            CollectionAssert.AreEqual(new[] { "C002" }, rows.Select(row => row.Id).ToList());

            var hidden = CodexListQuery.Apply(domain.Entries, null, "발차기", progress.PredicateFor(domain));
            Assert.That(hidden, Is.Empty, "잠긴 카드의 이름으로 검색이 걸렸다.");
        }

        [Test]
        public void TallyCountsWhatIsOpenOutOfEverything()
        {
            var domain = Cards();
            var progress = new CodexProgress();
            progress.MarkSeen(CodexDomainIds.Card, "C002");
            progress.MarkSeen(CodexDomainIds.Card, "C003");

            var counts = CodexListQuery.Tally(domain.Entries, progress.PredicateFor(domain));

            Assert.That(counts.Unlocked, Is.EqualTo(2));
            Assert.That(counts.Total, Is.EqualTo(3));
        }

        [Test]
        public void AlwaysUnlockedDomainsGetNoPredicateAtAll()
        {
            // Q6 — 상태이상은 규칙 참조표다. 잠그면 플레이어가 규칙을 못 읽는다. 술어가 null이면
            // Apply·Tally가 전량 공개 경로로 간다(디버그 뷰가 쓰는 것과 같은 규약).
            var statuses = new FakeDomain(CodexDomainIds.StatusEffect, alwaysUnlocked: true, Entry("Poison", "중독"));
            var progress = new CodexProgress();

            Assert.That(progress.PredicateFor(statuses), Is.Null);

            var counts = CodexListQuery.Tally(statuses.Entries, progress.PredicateFor(statuses));
            Assert.That(counts.Unlocked, Is.EqualTo(counts.Total));

            var rows = CodexListQuery.Apply(statuses.Entries, null, "중독", progress.PredicateFor(statuses));
            Assert.That(rows, Has.Count.EqualTo(1), "항상 열린 도메인은 검색도 열려 있어야 한다.");
        }

        [Test]
        public void NoProgressMeansEverythingOpen()
        {
            // 진행도 배선이 빠진 랩·디버그 뷰가 이 경로로 돈다 — 잠긴 것처럼 보이면 오진을 부른다.
            var domain = Cards();
            CodexProgress none = null;

            var rows = CodexListQuery.Apply(domain.Entries, null, "발차기", none?.PredicateFor(domain));

            Assert.That(rows, Has.Count.EqualTo(1));
        }

        [Test]
        public void ProgressFromOneDomainDoesNotUnlockAnother()
        {
            var cards = Cards();
            var monsters = new FakeDomain(
                CodexDomainIds.Monster, alwaysUnlocked: false, Entry("C001", "이름이 겹치는 몬스터"));

            var progress = new CodexProgress();
            progress.MarkSeen(CodexDomainIds.Card, "C001");

            Assert.That(progress.PredicateFor(cards)(cards.Entries[0]), Is.True);
            Assert.That(progress.PredicateFor(monsters)(monsters.Entries[0]), Is.False);
        }
    }
}
