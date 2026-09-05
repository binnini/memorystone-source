using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 은신 술어(<see cref="MonsterCatalogEntry.HasStealthTrait"/>) — 배치 bans의 <c>monster:stealth</c> 태그가
    /// 「특성 슬롯이 채워졌는가」가 아니라 은신만 보게 하는 게이트(2026-09-05 #31).
    /// </summary>
    public sealed class MonsterStealthPredicateTests
    {
        [Test]
        public void StealthPredicateExcludesOtherHiddenTraits()
        {
            // 2026-09-05 #31: 배치 bans의 monster:stealth 태그가 HasHiddenTrait(특성 슬롯이 채워졌는가)로 걸려
            // 봉인 오라(aura.seal)의 그슨새까지 stealth로 잡혔다. 은신만 참이어야 한다.
            var stealth = new MonsterCatalogEntry("T-S", "s", "test-melee", "B001", 6, 1, 10, hiddenTraitRef: MonsterHiddenTrait.StealthRef, hiddenTraitParam: "revealTurns=4");
            var aura = new MonsterCatalogEntry("T-A", "a", "test-ranged", "B001", 6, 1, 10, hiddenTraitRef: "aura.seal", hiddenTraitParam: "radius=1");
            var none = new MonsterCatalogEntry("T-N", "n", "test-melee", "B001", 6, 1, 10);

            Assert.That(stealth.HasStealthTrait, Is.True);
            Assert.That(aura.HasHiddenTrait, Is.True, "특성 슬롯은 채워져 있다");
            Assert.That(aura.HasStealthTrait, Is.False, "봉인 오라는 은신이 아니다");
            Assert.That(none.HasStealthTrait, Is.False);
        }
    }
}
