using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 연마 비교 화면의 「전 → 후」 요약(D-6). 효과 연마 2차(DEC-2026-09-06-08) 전에는 수치 축만 비교해 효과 연마 카드가
    /// 「문안이 달라집니다」로만 보였다 — 회복·타수·버프/상태 축과 문안 구절 diff가 여기서 잠긴다. 저작값 핀이 아니라
    /// 합성 스냅샷/정의로 빌더 자체를 잰다.
    /// </summary>
    public sealed class RefineDiffSummaryTests
    {
        private const string Improved = "6FD17A";
        private const string Muted = "B8C2D6";

        [Test]
        public void NumericAxisIsSummarisedAsBeforeArrowAfter()
        {
            var summary = RefineDiffSummary.Build(Snapshot(value: 3), Snapshot(value: 5), null, null, Improved, Muted);

            Assert.That(summary, Is.EqualTo($"수치 <color=#{Muted}>3</color> → <color=#{Improved}>5</color>"));
        }

        [Test]
        public void HealFoldedIntoValueIsNotReportedTwice()
        {
            // S02·F02: heal 축이 Amount로 접혀 HealValue == Value — 같은 변화를 「수치」와 「회복」 두 줄로 찍지 않는다.
            var summary = RefineDiffSummary.Build(Snapshot(value: 2, heal: 2), Snapshot(value: 3, heal: 3), null, null, Improved, Muted);

            Assert.That(summary, Does.Contain("수치"));
            Assert.That(summary, Does.Not.Contain("회복"));
        }

        [Test]
        public void DistinctHealAxisIsReported()
        {
            // A03: 피해 3→5와 회복 4→6이 따로 있다.
            var summary = RefineDiffSummary.Build(Snapshot(value: 3, heal: 4), Snapshot(value: 5, heal: 6), null, null, Improved, Muted);

            Assert.That(summary, Does.Contain($"수치 <color=#{Muted}>3</color> → <color=#{Improved}>5</color>"));
            Assert.That(summary, Does.Contain($"회복 <color=#{Muted}>4</color> → <color=#{Improved}>6</color>"));
        }

        [Test]
        public void DefinitionAxesHitCountAndBuffAreReportedWhenDefinitionsAreGiven()
        {
            var before = Definition().With(hitCount: 2, buffDebuff: "Agility:2");
            var after = before.With(hitCount: 3, buffDebuff: "Agility:3");

            var summary = RefineDiffSummary.Build(Snapshot(value: 2), Snapshot(value: 2), before, after, Improved, Muted);

            Assert.That(summary, Does.Contain($"타수 <color=#{Muted}>2</color> → <color=#{Improved}>3</color>"));
            // 버프 종류 이름은 상태이상 표시 이름(카탈로그 유무에 따라 다름)이라 수치 쌍만 잠근다.
            Assert.That(summary, Does.Contain($"<color=#{Muted}>2</color> → <color=#{Improved}>3</color>"));
            Assert.That(summary, Does.Not.Contain("Agility:"), "원시 토큰이 아니라 표시 이름으로 말한다.");
        }

        [Test]
        public void EffectUpgradeShowsTheChangedPhraseOfTheDescription()
        {
            // S04 빙고!+: 수치 축은 그대로고 문안의 「3명」만 「2명」이 된다.
            var summary = RefineDiffSummary.Build(
                Snapshot(value: 5, description: "{Shape}를 탐색하고 드러난 적이 3명 이상일 경우 {Heal} 회복합니다."),
                Snapshot(value: 5, description: "{Shape}를 탐색하고 드러난 적이 2명 이상일 경우 {Heal} 회복합니다."),
                null, null, Improved, Muted);

            Assert.That(summary, Is.EqualTo($"문안 <color=#{Muted}>3명</color> → <color=#{Improved}>2명</color>"));
        }

        [Test]
        public void PureInsertionAndPureDeletionAreSaidAsSuch()
        {
            var added = RefineDiffSummary.Build(
                Snapshot(description: "{Shape}를 탐색합니다."),
                Snapshot(description: "{Shape}를 탐색하고 행동 부적을 1장 뽑습니다."),
                null, null, Improved, Muted);
            Assert.That(added, Does.StartWith("문안 "));
            Assert.That(added, Does.Contain($"<color=#{Improved}>"));

            var removed = RefineDiffSummary.Build(
                Snapshot(description: "이번 턴 무적이 됩니다. 다음 턴 모든 적이 강화됩니다."),
                Snapshot(description: "이번 턴 무적이 됩니다."),
                null, null, Improved, Muted);
            Assert.That(removed, Does.Contain("삭제"));
            Assert.That(removed, Does.Contain("다음 턴 모든 적이 강화됩니다."));
        }

        [Test]
        public void NothingChangedSaysSo()
        {
            var summary = RefineDiffSummary.Build(Snapshot(value: 3), Snapshot(value: 3), null, null, Improved, Muted);

            Assert.That(summary, Is.EqualTo("달라지는 것이 없습니다."));
        }

        private static CombatCardSnapshot Snapshot(int value = 1, int heal = int.MinValue, string description = "문안")
        {
            return new CombatCardSnapshot("A00", CombatCardKind.Attack, "카드", description, value, true, false, string.Empty, cost: 1, healValue: heal);
        }

        private static CardDefinition Definition()
        {
            return new CardDefinition("A00", "카드", CardCategory.Action, CardEffectType.Attack, 1, 1, 1);
        }
    }
}
