using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Codex;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode.Codex
{
    /// <summary>
    /// 디버그 뷰(P4)의 계약. 화면 자체는 <c>SeoulPlayup.Flow</c>에 있어 여기서 세울 수 없지만,
    /// <b>토글이 무엇을 바꾸는가</b>는 뷰가 부르는 그 함수(<see cref="CodexUnlockRules.PredicateFor"/>)를
    /// 그대로 재면 잴 수 있다 — 뷰의 <c>UnlockPredicate</c>는 이 함수 한 줄이다.
    /// <para>
    /// 여기서 못박는 것은 셋이다: 토글이 술어를 걷어내는가(전량 공개) · 걷어낸 뒤 <b>검색이
    /// 잠긴 항목을 내놓는가</b>(Q4의 반대편 — 도감 뷰에서는 절대 안 되고 디버그 뷰에서는 돼야 한다) ·
    /// 원값 표가 도메인마다 실제로 저작돼 있는가.
    /// </para>
    /// </summary>
    public sealed class CodexP4DebugViewTests
    {
        private static ICodexDomain CardDomain() =>
            CodexShippingDomains.BuildAll().First(domain => domain.Id == CodexDomainIds.Card);

        private static ICodexDomain StatusDomain() =>
            CodexShippingDomains.BuildAll().First(domain => domain.Id == CodexDomainIds.StatusEffect);

        [Test]
        public void DebugToggleLiftsTheUnlockPredicateEntirely()
        {
            var domain = CardDomain();
            var progress = new CodexProgress();
            progress.MarkSeen(domain.Id, domain.Entries[0].Id);

            Assert.That(
                CodexUnlockRules.PredicateFor(progress, domain, isDebugView: false), Is.Not.Null,
                "도감 뷰에서는 해금 술어가 살아 있어야 한다.");
            Assert.That(
                CodexUnlockRules.PredicateFor(progress, domain, isDebugView: true), Is.Null,
                "디버그 뷰는 해금을 무시한다(Q4) — 술어가 null이어야 전량 공개 경로로 간다.");
        }

        [Test]
        public void DebugViewShowsEverythingOpenInTheTally()
        {
            // 완료 조건 2 — 잠긴 칸이 사라지고 수집률이 n/n이 된다.
            var domain = CardDomain();
            var progress = new CodexProgress();
            progress.MarkSeen(domain.Id, domain.Entries[0].Id);

            var counts = CodexListQuery.Tally(
                domain.Entries, CodexUnlockRules.PredicateFor(progress, domain, isDebugView: true));

            Assert.That(counts.Unlocked, Is.EqualTo(counts.Total));
            Assert.That(counts.Total, Is.EqualTo(domain.Entries.Count));
        }

        [Test]
        public void SearchReachesLockedEntriesOnlyInTheDebugView()
        {
            // Q4의 양면을 한자리에서 잰다 — 도감 뷰에서 새면 잠금이 장식이고,
            // 디버그 뷰에서 안 걸리면 저작을 찾을 방법이 없다.
            var domain = CardDomain();
            var progress = new CodexProgress();

            // 아무것도 안 열린 상태에서, 잠긴 항목 하나를 그 이름으로 찾아본다.
            var locked = domain.Entries[0];

            var inCodex = CodexListQuery.Apply(
                domain.Entries, null, locked.DisplayName,
                CodexUnlockRules.PredicateFor(progress, domain, isDebugView: false));
            Assert.That(
                inCodex.Any(row => row.Id == locked.Id), Is.False,
                $"{locked.Id}: 잠긴 항목이 도감 뷰 검색에 걸렸다.");

            var inDebug = CodexListQuery.Apply(
                domain.Entries, null, locked.DisplayName,
                CodexUnlockRules.PredicateFor(progress, domain, isDebugView: true));
            Assert.That(
                inDebug.Any(row => row.Id == locked.Id), Is.True,
                $"{locked.Id}: 디버그 뷰 검색이 잠긴 항목을 못 찾았다.");
        }

        [Test]
        public void TurningTheToggleOffRestoresTheSameLock()
        {
            // 디버그 뷰는 진행도를 <b>지우는</b> 것이 아니라 술어를 걷어낼 뿐이다 — 토글을 끄면
            // 열려 있던 것만 열린 채로 돌아와야 한다.
            var domain = CardDomain();
            var progress = new CodexProgress();
            progress.MarkSeen(domain.Id, domain.Entries[0].Id);

            _ = CodexUnlockRules.PredicateFor(progress, domain, isDebugView: true);

            var predicate = CodexUnlockRules.PredicateFor(progress, domain, isDebugView: false);
            Assert.That(predicate, Is.Not.Null);
            Assert.That(predicate(domain.Entries[0]), Is.True);
            Assert.That(predicate(domain.Entries[1]), Is.False);
        }

        [Test]
        public void AlwaysUnlockedDomainsLookTheSameInBothViews()
        {
            // Q6 도메인은 원래 술어가 없다 — 토글이 여기서 바꿀 것은 아무것도 없어야 한다.
            var domain = StatusDomain();
            var progress = new CodexProgress();

            Assert.That(CodexUnlockRules.PredicateFor(progress, domain, isDebugView: false), Is.Null);
            Assert.That(CodexUnlockRules.PredicateFor(progress, domain, isDebugView: true), Is.Null);
        }

        [Test]
        [Category("ShippingData")]
        public void EveryShippingDomainAuthorsItsRawValueTable()
        {
            // 완료 조건 3 — 출하 도메인 <b>전부</b>에서 원값 행이 뜬다(P6에서 오브젝트가 늘어 일곱). 한 도메인이라도 비면
            // 디버그 뷰가 그 도메인에서는 아무 말도 못 한다.
            foreach (var domain in CodexShippingDomains.BuildAll())
            {
                Assert.That(domain.Entries, Is.Not.Empty, domain.Id);

                foreach (var entry in domain.Entries)
                {
                    Assert.That(
                        entry.DebugRows, Is.Not.Empty,
                        $"{domain.Id}/{entry.Id}: 원값 표가 비었다 — 디버그 뷰에 덧붙일 것이 없다.");
                    Assert.That(
                        entry.DebugRows.Any(row => !row.IsEmpty), Is.True,
                        $"{domain.Id}/{entry.Id}: 원값 행이 전부 빈 값이다.");
                }
            }
        }

        [Test]
        [Category("ShippingData")]
        public void AttackShapeDomainAlsoAuthorsRawValues()
        {
            // §7-3 판단 — 형상 도메인은 개발 전용이라 디버그 뷰가 사실상 유일한 소비처다.
            // 상세 절이 <b>푼 결과</b>를 적으므로 원값 표는 CSV에 적힌 것(patternId · offsets)을 적는다.
            var domain = CodexShippingDomains.BuildAttackShapeDomain();
            Assert.That(domain.Entries, Is.Not.Empty);

            foreach (var entry in domain.Entries)
            {
                var labels = entry.DebugRows.Select(row => row.Label).ToList();
                CollectionAssert.Contains(labels, "patternId", entry.Id);
                CollectionAssert.Contains(labels, "offsets", entry.Id);

                Assert.That(
                    entry.DebugRows.First(row => row.Label == "patternId").Value, Is.EqualTo(entry.Id),
                    "원값 표의 patternId가 항목 id와 다르면 저작을 찾아갈 수 없다.");
            }
        }
    }
}
