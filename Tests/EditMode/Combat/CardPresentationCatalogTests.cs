using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Combat.Unity;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CardPresentationCatalogTests
    {
        [Test]
        public void CardPresentationCatalogReturnsPlaceholderFrameWhenMissingArt()
        {
            var card = DemoCardCatalog.Create(CombatConfig.Default)
                .CreateDeck(CardCategory.Action)
                .Single(candidate => candidate.Id == CardIds.HolyLight);
            var presentation = new CardPresentationCatalog(null).Resolve(card);

            Assert.That(presentation.PresentationId, Is.EqualTo(card.PresentationRef.PresentationId));
            Assert.That(presentation.FrameId, Is.EqualTo(CardPresentationRef.PlaceholderFrameId));
            Assert.That(presentation.UsesPlaceholderFrame, Is.True);
            Assert.That(presentation.IllustrationId, Is.Empty);
            Assert.That(presentation.VfxId, Is.Not.Empty);
            Assert.That(typeof(CardPresentationCatalog).Assembly.GetReferencedAssemblies().Any(name => name.Name == "SeoulPlayup.CardCore"), Is.True);
        }

        [Test]
        public void ChoiceCardPanelCanRenderOptionSpecificCardTexts()
        {
            var card = new CombatCardSnapshot(
                CardIds.HolyLight,
                CombatCardKind.Attack,
                "성스러운 빛",
                string.Empty,
                4,
                true,
                false,
                "Ready",
                cost: 1,
                range: 3,
                playMode: CardPlayMode.Choice,
                choiceOptionTexts: "heal|성스러운 빛|자신을 {Heal} 회복합니다.;attack|성스러운 빛|사거리 {Range} 내 적에게 피해 {Damage}를 줍니다.");

            var model = ChoiceCardPanelModel.ForCard(card);

            Assert.That(model.SourceCardId, Is.EqualTo(CardIds.HolyLight));
            Assert.That(model.Options.Count, Is.EqualTo(2));
            Assert.That(model.Options.Select(option => option.OptionId), Is.EqualTo(new[] { "heal", "attack" }));
            Assert.That(model.Options[0].DisplayName, Is.EqualTo("성스러운 빛"));
            Assert.That(model.Options[0].CardText, Is.EqualTo("자신을 4 회복합니다."));
            Assert.That(model.Options[1].DisplayName, Is.EqualTo("성스러운 빛"));
            Assert.That(model.Options[1].CardText, Is.EqualTo("사거리 3 내 적에게 피해 4를 줍니다."));
            Assert.That(model.Options[0].CardText, Is.Not.EqualTo(model.Options[1].CardText));
        }

        [Test]
        public void UiCatalogOmitsDraftCards()
        {
            var visibleIds = DemoCardCatalog.Create(CombatConfig.Default)
                .GetVisibleCatalogEntries()
                .Select(entry => entry.Id)
                .ToList();

            Assert.That(visibleIds, Has.Member(CardIds.TargetShot));
            Assert.That(visibleIds, Does.Contain(CardIds.Redraw));
        }
    }
}


