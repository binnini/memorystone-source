using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 🔴 <b>같은 「2」가 세는 대상이 종류마다 다르다</b>(2026-09-05). 중독의 2는 남은 발동 횟수,
    /// 둔화의 2는 지속 구간, 수호의 저작 지속은 아무 의미도 없다(소비로만 사라진다). 셋을 전부
    /// "남은 N턴"으로 찍으면 플레이어가 배지를 보고 다음 턴을 예측할 수 없다.
    ///
    /// <para>이 스위트는 <b>문면이 세는 대상을 말하는지</b>를 잠근다. 문안 자체를 단언하는 것은
    /// 취약하지만, 여기서 지키려는 것이 정확히 <b>「무엇을 세는지가 드러나는가」</b>라서
    /// 세 갈래가 서로 구별되는지를 재는 형태로 쓴다.</para>
    /// </summary>
    public sealed class StatusEffectDurationDisplayTests
    {
        private static ActiveEffect Effect(StatusEffectKind kind, int turns, int amount = 0)
            => new ActiveEffect(EffectType.Duration, kind, "player", turns, amount, "test");

        [Test]
        public void TickingEffectSaysHowManyTimesItWillFireAgain()
        {
            var line = StatusEffectTooltipContent.RemainingTurnsLine(Effect(StatusEffectKind.Poison, 2, 3));

            Assert.That(line, Does.Contain("2"));
            Assert.That(line, Does.Contain("발동"),
                "중독의 남은 수는 지속 구간이 아니라 앞으로 아플 횟수다 — 문면이 그것을 말해야 한다.");
        }

        [Test]
        public void PresenceEffectSaysItStaysForThatManyTurns()
        {
            var line = StatusEffectTooltipContent.RemainingTurnsLine(Effect(StatusEffectKind.Slow, 2, 1));

            Assert.That(line, Does.Contain("2"));
            Assert.That(line, Does.Contain("유지"),
                "둔화의 2는 붙어 있는 구간이다 — 발동 횟수로 읽히면 안 된다.");
        }

        [Test]
        public void ConsumableEffectCountsChargesNotTurns()
        {
            // 수호: 저작 지속은 1이지만 턴으로 만료되지 않는다. 남은 것은 막을 수 있는 횟수다.
            var line = StatusEffectTooltipContent.RemainingTurnsLine(Effect(StatusEffectKind.Guard, 1, 3));

            Assert.That(line, Does.Contain("3"), "찍어야 하는 수는 저작 지속 1이 아니라 남은 횟수 3이다.");
            Assert.That(line, Does.Contain("회"));
            Assert.That(line, Does.Not.Contain("턴"),
                "소비형에 「턴」을 찍으면 곧 사라진다는 거짓말이 된다.");
        }

        [Test]
        public void ThreeLifetimeShapesAreDistinguishable()
        {
            var ticking = StatusEffectTooltipContent.RemainingTurnsLine(Effect(StatusEffectKind.Poison, 2, 3));
            var presence = StatusEffectTooltipContent.RemainingTurnsLine(Effect(StatusEffectKind.Slow, 2, 1));
            var consumable = StatusEffectTooltipContent.RemainingTurnsLine(Effect(StatusEffectKind.Guard, 2, 2));

            Assert.That(ticking, Is.Not.EqualTo(presence),
                "같은 숫자 2인데 문면이 같으면 이 수정이 아무 일도 안 한 것이다.");
            Assert.That(presence, Is.Not.EqualTo(consumable));
            Assert.That(ticking, Is.Not.EqualTo(consumable));
        }

        [Test]
        public void BadgeNumberFollowsTheSameJudgementAsTheTooltip()
        {
            // 배지와 툴팁이 다른 규칙을 쓰면 반드시 갈린다 — 판정이 한 곳에 있는지를 잠근다.
            Assert.That(StatusEffectTooltipContent.BadgeNumber(Effect(StatusEffectKind.Guard, 1, 3)),
                Is.EqualTo("3"), "소비형 배지는 저작 지속이 아니라 남은 횟수를 찍는다.");
            Assert.That(StatusEffectTooltipContent.BadgeNumber(Effect(StatusEffectKind.Slow, 2, 1)),
                Is.EqualTo("2"), "그 외는 남은 턴을 찍는다.");
        }

        [Test]
        public void PermanentEffectStillSaysPermanent()
        {
            // 기존 계약(MightTests) 보존 — 만료가 없는 효과에 "0턴"을 찍으면 거짓말이 된다.
            Assert.That(StatusEffectTooltipContent.RemainingTurnsLine(Effect(StatusEffectKind.Might, 0, 4)),
                Is.EqualTo("지속: 영구"));
        }
    }
}
