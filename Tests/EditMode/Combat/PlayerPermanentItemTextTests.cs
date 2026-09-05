using System;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 유물 어휘 정본(<see cref="PlayerPermanentItemText"/>) 게이트.
    /// <para>
    /// 🔴 이 스위트가 있는 이유: 2026-08-31까지 어휘가 두 벌이었고 도감 쪽이 네 개짜리였다 —
    /// 격자에 <c>BlockGainBonus +1</c>·<c>MoneyGrantOnce +40</c> 같은 <b>내부 이름</b>이 그대로 떴고,
    /// 트리거 유물 6종은 <c>None +1</c>로 떴다. 축을 늘리고 여기에 한 줄을 안 더하면 같은 일이
    /// 반복되므로, <b>enum 전수</b>를 훑어 못을 박는다.
    /// </para>
    /// </summary>
    public sealed class PlayerPermanentItemTextTests
    {
        [Test]
        public void EveryEffectAxisHasKoreanTextInsteadOfItsInternalName()
        {
            foreach (PlayerPermanentItemEffectKind kind in Enum.GetValues(typeof(PlayerPermanentItemEffectKind)))
            {
                if (kind == PlayerPermanentItemEffectKind.None)
                {
                    continue;
                }

                var text = PlayerPermanentItemText.Effect(kind, 1);
                Assert.That(text, Is.Not.EqualTo(PlayerPermanentItemText.Unknown),
                    $"효과 축 '{kind}'에 한글 문안이 없다 — PlayerPermanentItemText.Effect에 한 줄 더할 것.");
                Assert.That(text, Does.Not.Contain(kind.ToString()),
                    $"효과 축 '{kind}'가 내부 이름을 그대로 흘린다.");
            }
        }

        [Test]
        public void EveryTriggerHasKoreanTextInsteadOfItsInternalName()
        {
            foreach (RelicTriggerKind kind in Enum.GetValues(typeof(RelicTriggerKind)))
            {
                if (kind == RelicTriggerKind.None)
                {
                    continue;
                }

                var text = PlayerPermanentItemText.Trigger(kind, 3, 2);
                Assert.That(text, Is.Not.EqualTo(PlayerPermanentItemText.Unknown),
                    $"트리거 '{kind}'에 한글 문안이 없다.");
                Assert.That(text, Does.Not.Contain(kind.ToString()),
                    $"트리거 '{kind}'가 내부 이름을 그대로 흘린다.");
            }
        }

        [Test]
        public void TriggerRelicsReadAsTheirConditionInsteadOfNone()
        {
            // 트리거 유물은 EffectKind가 의도적으로 None이다(RC-9) — 그걸 스칼라로 읽으면 「None +1」이 된다.
            var headline = PlayerPermanentItemText.Headline(
                PlayerPermanentItemEffectKind.None, 1, RelicTriggerKind.DrawPerCardsUsed, 10);

            Assert.That(headline, Does.Not.Contain("None"));
            StringAssert.Contains("10", headline);
        }

        [Test]
        [Category("ShippingData")]
        public void NoShippedRelicShowsAnInternalNameOrFallsBackToNothing()
        {
            var catalog = RelicCatalogCsv.ConvertFile(CombatCsvPaths.RelicsCsv);
            Assert.That(catalog.Entries, Is.Not.Empty);

            foreach (var definition in catalog.Entries)
            {
                var headline = PlayerPermanentItemText.Headline(
                    definition.EffectKind, definition.EffectAmount, definition.TriggerKind, definition.TriggerParam);

                Assert.That(headline, Is.Not.EqualTo(PlayerPermanentItemText.Unknown),
                    $"«{definition.DisplayName}»이 화면에서 「효과 없음」으로 뜬다 — 저작 축이 어휘에 없다.");
                Assert.That(headline, Does.Not.Contain(definition.EffectKind.ToString()),
                    $"«{definition.DisplayName}»이 내부 이름을 흘린다: {headline}");
                Assert.That(headline, Does.Not.Contain("None"),
                    $"«{definition.DisplayName}»이 「None」을 흘린다: {headline}");
            }
        }
    }
}
