#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// <see cref="CombatEffectSourceClassifier"/>의 카드 ID switch가 출하 <c>cards.csv</c>의
    /// <c>type</c> 컬럼과 <b>어긋나지 않는지</b> 전수 대조한다.
    ///
    /// 왜 필요한가: 분류기는 공격 카드를 판별하려고 ID 15개(A00~A14)를 switch에 손으로 나열한다.
    /// 같은 정보가 이미 <c>cards.csv</c>의 <c>type</c> 컬럼에 있으므로 이 switch는 <b>저작의 사본</b>이고,
    /// 사본은 원본과 어긋날 수 있다. 어긋나면 조용하다 — 컴파일도 되고 게임도 뜬다. 다만
    /// <see cref="CombatEffectSourceClassifier.IsPlayerAttackCardSource"/>를 읽는
    /// <c>CombatCameraController</c>(공격 카메라 흔들림)와 <c>CombatAudioPresenter</c>(공격 효과음)가
    /// 그 카드에 대해서만 침묵한다. 증상이 "연출이 좀 밋밋하다"로만 나타나 원인까지 오래 걸린다.
    ///
    /// 기존 <see cref="CombatEffectSourceClassifierTests"/>는 이 계약을 <b>이름으로만</b> 주장한다
    /// (<c>PlayerAttackSourcesUseCsvCardIdsOnly</c>). 본문은 A01·A06·A11 세 장만 확인하고 CSV를 열지
    /// 않으므로, A15가 저작돼도 초록색으로 통과한다. 그 테스트는 과거 슬러그 회귀를 잠그는 값어치가
    /// 있으니 남겨 두고, <b>저작과의 대조는 여기서</b> 한다.
    ///
    /// 이 프로젝트는 같은 종류의 사고를 이미 겪었다 — <see cref="ShippingCardCsvEffectTests"/> 참고
    /// (2026-08-02 실플레이, U04 호롱불). 그때 얻은 교훈이 <b>"픽스처는 저작과 어긋난 순간 거짓말을
    /// 시작한다"</b>였고, 이 파일은 그 교훈을 분류기에도 적용한 것이다.
    ///
    /// 양방향으로 본다. 정방향(모든 공격 카드 → true)은 <b>새 카드 누락</b>을 잡고,
    /// 역방향(공격이 아닌 카드 → false)은 <b>타입이 바뀌었는데 switch에 남은 ID</b>를 잡는다.
    /// </summary>
    [Category("ShippingData")]
    public sealed class ShippingCardSourceClassifierTests
    {
        [Test]
        public void EveryShippingAttackCardIsClassifiedAsAttackSource()
        {
            AssertClassifierCoversAuthoredType(
                CardEffectType.Attack,
                id => CombatEffectSourceClassifier.IsPlayerAttackCardSource(id, ShippingCatalog()),
                "IsPlayerAttackCardSource");
        }

        [Test]
        public void NoNonAttackShippingCardIsClassifiedAsAttackSource()
        {
            AssertClassifierRejectsOtherTypes(
                CardEffectType.Attack,
                id => CombatEffectSourceClassifier.IsPlayerAttackCardSource(id, ShippingCatalog()),
                "IsPlayerAttackCardSource");
        }

        [Test]
        public void EveryShippingScoutCardIsClassifiedAsScoutSource()
        {
            AssertClassifierCoversAuthoredType(
                CardEffectType.Scout,
                id => CombatEffectSourceClassifier.IsScoutCardSource(id, ShippingCatalog()),
                "IsScoutCardSource");
        }

        [Test]
        public void NoNonScoutShippingCardIsClassifiedAsScoutSource()
        {
            AssertClassifierRejectsOtherTypes(
                CardEffectType.Scout,
                id => CombatEffectSourceClassifier.IsScoutCardSource(id, ShippingCatalog()),
                "IsScoutCardSource");
        }

        // ------------------------------------------------------------------ 대조

        /// <summary>정방향: 저작이 그 타입이라고 말한 카드는 전부 분류기가 받아들여야 한다.</summary>
        private static void AssertClassifierCoversAuthoredType(
            CardEffectType authoredType,
            System.Func<string, bool> classifier,
            string classifierName)
        {
            var authored = ShippingEntriesOfType(authoredType);
            Assert.That(
                authored,
                Is.Not.Empty,
                $"출하 cards.csv에 {authoredType} 카드가 하나도 없다 — 대조가 빈 채로 통과하고 있다.");

            var missing = authored.Where(id => !classifier(id)).ToList();

            Assert.That(
                missing,
                Is.Empty,
                $"cards.csv가 {authoredType}로 저작한 카드를 {classifierName}가 모른다: "
                + $"{string.Join(", ", missing)}. "
                + "CombatEffectSourceClassifier의 switch에 추가하거나, switch를 지우고 카탈로그의 "
                + "ActionType을 직접 조회하도록 바꿔야 한다. 이대로 두면 그 카드만 공격 카메라 "
                + "흔들림과 공격 효과음이 조용히 빠진다.");
        }

        /// <summary>역방향: 저작이 다른 타입이라고 말한 카드를 분류기가 받아들이면 안 된다.</summary>
        private static void AssertClassifierRejectsOtherTypes(
            CardEffectType authoredType,
            System.Func<string, bool> classifier,
            string classifierName)
        {
            var others = ShippingCatalog().Entries
                .Where(entry => entry.ActionType != authoredType)
                .Select(entry => entry.Id)
                .ToList();

            Assert.That(
                others,
                Is.Not.Empty,
                $"출하 cards.csv가 전부 {authoredType}다 — 역방향 대조가 빈 채로 통과하고 있다.");

            var stale = others.Where(id => classifier(id)).ToList();

            Assert.That(
                stale,
                Is.Empty,
                $"{classifierName}가 {authoredType}가 아닌 카드를 {authoredType}로 분류한다: "
                + $"{string.Join(", ", stale)}. "
                + "cards.csv에서 타입이 바뀌었는데 switch에 ID가 남아 있는 경우다.");
        }

        // ------------------------------------------------------------------ 출하 저작 읽기

        private static System.Collections.Generic.List<string> ShippingEntriesOfType(CardEffectType type)
        {
            return ShippingCatalog().Entries
                .Where(entry => entry.ActionType == type)
                .Select(entry => entry.Id)
                .ToList();
        }

        private static CardCatalogDefinition ShippingCatalog()
        {
            return ShippingCardCatalogSource.Load();
        }
    }
}
#endif
