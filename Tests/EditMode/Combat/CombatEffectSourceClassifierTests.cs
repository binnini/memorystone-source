using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 분류기가 <b>과거 슬러그를 받아들이지 않는지</b> 잠근다 — 카드 출처 ref는 CSV 카탈로그 ID
    /// (A01·S01…)뿐이고 <c>attack.damage</c>·<c>monster.pattern.*</c> 같은 것은 카드가 아니다.
    ///
    /// ⚠️ <b>이름과 달리 이 스위트는 전수 대조가 아니다.</b> 아래 <c>PlayerAttackSourcesUseCsvCardIdsOnly</c>는
    /// A01·A06·A11 세 장만 확인한다. <b>저작(cards.csv)과의 전수 대조는
    /// <see cref="ShippingCardSourceClassifierTests"/>가 맡는다</b> — 새 공격 카드를 저작했는데
    /// 분류가 빠지는 사고는 그쪽이 잡는다. 여기는 값어치가 다르다(회귀 슬러그 잠금).
    /// </summary>
    public sealed class CombatEffectSourceClassifierTests
    {
        private static CardCatalogDefinition Catalog => ShippingCardCatalogSource.Load();

        [Test]
        public void PlayerAttackSourcesUseCsvCardIdsOnly()
        {
            Assert.That(CombatEffectSourceClassifier.IsPlayerAttackCardSource("A01", Catalog), Is.True);
            Assert.That(CombatEffectSourceClassifier.IsPlayerAttackCardSource("A06", Catalog), Is.True);
            Assert.That(CombatEffectSourceClassifier.IsPlayerAttackCardSource("A11", Catalog), Is.True);

            Assert.That(CombatEffectSourceClassifier.IsPlayerAttackCardSource("old-attack-slug", Catalog), Is.False);
            Assert.That(CombatEffectSourceClassifier.IsPlayerAttackCardSource("old-double-hit-slug", Catalog), Is.False);
            Assert.That(CombatEffectSourceClassifier.IsPlayerAttackCardSource("attack.damage", Catalog), Is.False);
            Assert.That(CombatEffectSourceClassifier.IsPlayerAttackCardSource("old-m2-attack-slug", Catalog), Is.False);
            Assert.That(CombatEffectSourceClassifier.IsPlayerAttackCardSource("monster.pattern.A001", Catalog), Is.False);
        }

        [Test]
        public void ScoutSourcesUseCsvCardIdsOnly()
        {
            Assert.That(CombatEffectSourceClassifier.IsScoutCardSource("S01", Catalog), Is.True);
            Assert.That(CombatEffectSourceClassifier.IsScoutCardSource("S02", Catalog), Is.True);

            Assert.That(CombatEffectSourceClassifier.IsScoutCardSource("old-scout-slug", Catalog), Is.False);
            Assert.That(CombatEffectSourceClassifier.IsScoutCardSource("old-m2-scout-slug", Catalog), Is.False);
            Assert.That(CombatEffectSourceClassifier.IsScoutCardSource("scout.enemy_count_damage", Catalog), Is.False);
        }
    }
}


