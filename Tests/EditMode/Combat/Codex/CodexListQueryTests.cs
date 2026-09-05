using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Codex;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode.Codex
{
    /// <summary>
    /// 목록 격자의 해금·검색·필터 규칙(<c>docs/codex-plan.md</c> Q4·Q7). 셋이 만나는 지점이라
    /// 화면 없이 못 박아 둔다 — 특히 <b>잠긴 항목은 목록에는 남지만 검색에는 걸리지 않는다</b>는 계약.
    /// </summary>
    public sealed class CodexListQueryTests
    {
        private static CodexEntry Entry(string id, string name, string chip, string description = "")
        {
            return new CodexEntry(
                id,
                name,
                CodexThumbnail.Resolve(null, name, Color.white),
                filterChip: chip,
                description: description);
        }

        private static CodexEntry[] Sample()
        {
            return new[]
            {
                Entry("Poison", "중독", "해로움", "턴 시작 시 피해를 받습니다."),
                Entry("Stun", "기절", "해로움", "행동할 수 없습니다."),
                Entry("Agility", "민첩", "이로움", "이동력이 증가합니다."),
            };
        }

        [Test]
        public void NoFilters_ReturnsEverythingInAuthoredOrder()
        {
            var rows = CodexListQuery.Apply(Sample(), filterChip: null, search: null);

            CollectionAssert.AreEqual(new[] { "Poison", "Stun", "Agility" }, rows.Select(r => r.Id).ToList());
        }

        [Test]
        public void Chip_NarrowsToThatGroup()
        {
            var rows = CodexListQuery.Apply(Sample(), filterChip: "이로움", search: null);

            CollectionAssert.AreEqual(new[] { "Agility" }, rows.Select(r => r.Id).ToList());
        }

        [Test]
        public void Search_MatchesNameIdAndDescription()
        {
            var entries = Sample();

            Assert.That(CodexListQuery.Apply(entries, null, "중독").Single().Id, Is.EqualTo("Poison"));
            Assert.That(CodexListQuery.Apply(entries, null, "stun").Single().Id, Is.EqualTo("Stun"),
                "id 검색은 대소문자를 가리지 않아야 한다.");
            Assert.That(CodexListQuery.Apply(entries, null, "이동력").Single().Id, Is.EqualTo("Agility"),
                "설명으로도 찾을 수 있어야 한다 — 이름을 모를 때 쓰는 것이 검색이다.");
        }

        [Test]
        public void LockedEntries_StayInTheGridButNeverSurfaceInSearch()
        {
            var entries = Sample();
            bool IsUnlocked(CodexEntry entry) => entry.Id != "Stun";

            // Q7 — 자리를 남긴다. 잠겼다고 목록에서 빠지지는 않는다.
            var browsing = CodexListQuery.Apply(entries, null, null, IsUnlocked);
            CollectionAssert.AreEqual(new[] { "Poison", "Stun", "Agility" }, browsing.Select(r => r.Id).ToList());

            // 그러나 검색으로는 새어 나오면 안 된다 — 이름을 모르는 것이 해금의 전부다.
            var searched = CodexListQuery.Apply(entries, null, "기절", IsUnlocked);
            CollectionAssert.IsEmpty(searched);
        }

        [Test]
        public void DebugView_PassesNoPredicate_SoLockedEntriesAreSearchable()
        {
            // Q2·Q4 — 디버그 뷰는 전량 공개다. 술어를 넘기지 않는 것이 그 표현이다.
            var searched = CodexListQuery.Apply(Sample(), null, "기절", isUnlocked: null);

            Assert.That(searched.Single().Id, Is.EqualTo("Stun"));
        }

        [Test]
        public void CollectChips_KeepsAuthoredOrderAndDeduplicates()
        {
            var chips = CodexListQuery.CollectChips(Sample());

            CollectionAssert.AreEqual(new[] { "해로움", "이로움" }, chips,
                "알파벳 정렬하면 뜻이 있는 저작 순서가 흐트러진다.");
        }

        [Test]
        public void Tally_CountsUnlockedAgainstTotal()
        {
            var entries = Sample();

            Assert.That(CodexListQuery.Tally(entries), Is.EqualTo((3, 3)),
                "술어가 없으면 전량 공개다.");
            Assert.That(CodexListQuery.Tally(entries, e => e.Id != "Stun"), Is.EqualTo((2, 3)));
        }
    }
}
