using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 카드 연마(camper-workshop-plan.md P0): card_upgrades.csv 저작 → 정의 시점 치환 →
    /// <see cref="CombatState.TryRefineCard"/>까지의 계약. 치환은 실행 시점 배율이 아니라
    /// 정의 시점이라야 필드 카드(배치 시점 저작값)에도 먹는다 — 그 계약을 여기서 감시한다.
    /// </summary>
    public sealed class CardRefineTests
    {
        private const string UpgradeHeader =
            "cardId,cost,range,damage,shield,heal,duration,hitCount,shape,stateEffect,buff_debuff,scout,descriptionOverride,note";

        private CardCatalogAsset asset;

        [TearDown]
        public void TearDown()
        {
            if (asset != null)
            {
                Object.DestroyImmediate(asset);
                asset = null;
            }
        }

        // ── card_upgrades.csv 파싱·치환 (출하 cards.csv 기반) ───────────────────────────

        [Category("ShippingData")]
        [Test]
        public void UpgradedEntryReplacesAuthoredValuesAtDefinitionTime()
        {
            asset = LoadCsvAsset("A01,,,5,,,,,blast-2,,,,,피해 3→5·blast-1→2");
            Assert.That(asset.ValidateRows(out var reason), Is.True, reason);

            var entry = Entry("A01");
            Assert.That(entry.UpgradedEntry, Is.Not.Null);

            var level0 = entry.ToCardDefinition("test-source", "inst", 0);
            Assert.That(level0.Amount, Is.EqualTo(3));
            Assert.That(level0.AreaRadius, Is.EqualTo(1));
            Assert.That(level0.DisplayName, Is.EqualTo("휘둘러치기"));

            var level1 = entry.ToCardDefinition("test-source", "inst", 1);
            Assert.That(level1.Amount, Is.EqualTo(5), "damage 컬럼 치환이 Amount로 도착해야 한다.");
            Assert.That(level1.AreaRadius, Is.EqualTo(2), "shape 승급(blast-1→blast-2)이 명중 판정 값으로 도착해야 한다.");
            Assert.That(level1.Cost, Is.EqualTo(level0.Cost), "빈 칸 = 원본 유지.");
            Assert.That(level1.DisplayName, Is.EqualTo("휘둘러치기+"), "D-6: 연마 카드는 카드명 뒤 + 접미.");
            Assert.That(level1.UpgradeLevel, Is.EqualTo(1));
            Assert.That(level1.InstanceId, Is.EqualTo("inst"));
        }

        [Category("ShippingData")]
        [Test]
        public void RefinedFieldCardCarriesUpgradedPlacementValues()
        {
            // 🔴 필드 카드는 배치 시점 저작값을 그대로 쓴다 — 정의 자체가 연마값이어야
            // 장판 피해·지속이 연마를 따라간다(실행 시점 배율 보정은 여기 못 미친다).
            asset = LoadCsvAsset("F01,,,9,,,4,,,,,,,장판 강화");
            Assert.That(asset.ValidateRows(out var reason), Is.True, reason);

            var entry = Entry("F01");
            var refined = entry.ToCardDefinition("test-source", "inst", 1);
            Assert.That(refined.Amount, Is.EqualTo(9));
            Assert.That(refined.DurationTurns, Is.EqualTo(4));
            Assert.That(refined.FieldObjectKind, Is.EqualTo(CardFieldObjectKind.FieldDamage));

            var baseline = entry.ToCardDefinition("test-source", "inst", 0);
            Assert.That(baseline.Amount, Is.EqualTo(6));
            Assert.That(baseline.DurationTurns, Is.EqualTo(2));
        }

        [Category("ShippingData")]
        [Test]
        public void DescriptionOverrideIsPerCardWhileTokenTextUpdatesItself()
        {
            asset = LoadCsvAsset("D01,0,,,8,,,,,,,,,코스트 0·방어 8");
            Assert.That(asset.ValidateRows(out var reason), Is.True, reason);

            var refined = Entry("D01").ToCardDefinition("test-source", "inst", 1);
            Assert.That(refined.Cost, Is.EqualTo(0));
            Assert.That(refined.Amount, Is.EqualTo(8));
            // 토큰 문안({Shield})은 그대로 남는다 — 수치 치환만으로 문안이 자동 갱신되는 카드는
            // descriptionOverride가 필요 없다.
            Assert.That(refined.Description, Does.Contain("{Shield}"));

            Object.DestroyImmediate(asset);
            asset = LoadCsvAsset("D01,,,,8,,,,,,,,낡았지만 단단한 방어구를 얻습니다.,고정 문안 카드용");
            Assert.That(asset.ValidateRows(out reason), Is.True, reason);
            Assert.That(Entry("D01").ToCardDefinition("test-source", "inst", 1).Description,
                Is.EqualTo("낡았지만 단단한 방어구를 얻습니다."));
        }

        [Category("ShippingData")]
        [Test]
        public void UpgradeRowValidationRejectsAuthoringMistakes()
        {
            AssertRejected("A01,,,5,,,,,,,,,,\nA01,,,6,,,,,,,,,,", "duplicate");
            AssertRejected("ZZZ,,,5,,,,,,,,,,", "unknown cardId");
            AssertRejected("X04,0,,,,,,,,,,,,", "curse");
            AssertRejected("A01,,,,,,,,,,,,,메모만 있는 행", "substitutes nothing");
            AssertRejected("D01,,,4,,,,,,,,,,", "base card leaves it empty");
            AssertRejected("A01,,,abc,,,,,,,,,,", "invalid numeric token");
        }

        [Category("ShippingData")]
        [Test]
        public void ShippedUpgradesCsvParsesAndValidates()
        {
            var upgradesText = File.ReadAllText(CombatCsvPaths.CardUpgradesCsv);
            var rows = CardCatalogAsset.ParseUpgradesCsvText(upgradesText);
            asset = LoadCsvAsset();
            asset.SetUpgradeRows(rows);
            Assert.That(asset.ValidateRows(out var reason), Is.True, reason);
        }

        // ── CombatState.TryRefineCard (순수 C# 카탈로그) ────────────────────────────────

        [Test]
        public void TryRefineCardUpgradesTheLiveDeckAndTheRunDeckTogether()
        {
            var state = CreateState();

            Assert.That(state.CanRefineCard("A01"), Is.True);
            Assert.That(state.TryRefineCard("A01", out var reason), Is.True, reason);

            var live = FindAnywhere(state.ActionDeck, "A01");
            Assert.That(live, Is.Not.Null);
            Assert.That(live.UpgradeLevel, Is.EqualTo(1));
            Assert.That(live.Amount, Is.EqualTo(5), "라이브 전투 덱의 카드가 연마 정의로 바뀌어야 한다.");
            Assert.That(live.DisplayName, Does.EndWith("+"));

            var instance = state.PlayerDeck.ActionCards.Single(card => card.CardId == "A01");
            Assert.That(instance.UpgradeLevel, Is.EqualTo(1), "런 지속 덱에도 연마가 반영돼야 세이브 복원 후 유지된다.");
        }

        [Test]
        public void RefineIsOncePerCardByLogicNotBySchema()
        {
            var state = CreateState();
            Assert.That(state.TryRefineCard("A01", out var reason), Is.True, reason);

            Assert.That(state.TryRefineCard("A01", out reason), Is.False);
            Assert.That(reason, Does.Contain("이미 연마"));
        }

        [Test]
        public void CardsWithoutAnUpgradeRowAreNotRefinable()
        {
            var state = CreateState();

            Assert.That(state.CanRefineCard(ApprovedCardCatalogFactory.MoveBasicId, out var reason), Is.False);
            Assert.That(reason, Does.Contain("저작"));
        }

        [Test]
        public void TemporaryCardsAreNotRefinable()
        {
            var state = CreateState();
            var temporary = AttackEntry(amount: 3, upgraded: AttackEntry(amount: 5))
                .ToCardDefinition("refine-test", "tmp-inst", 0, isTemporary: true);

            Assert.That(state.CanRefineCard(temporary, out var reason), Is.False);
            Assert.That(reason, Does.Contain("임시"));
        }

        [Test]
        public void PreviewReturnsBeforeAfterPairWithoutMutating()
        {
            var state = CreateState();

            Assert.That(state.TryPreviewRefinedCard("A01", out var current, out var refined, out var reason), Is.True, reason);
            Assert.That(current.Amount, Is.EqualTo(3));
            Assert.That(refined.Amount, Is.EqualTo(5));
            Assert.That(refined.UpgradeLevel, Is.EqualTo(1));

            Assert.That(state.CanRefineCard("A01"), Is.True, "미리보기는 상태를 바꾸면 안 된다.");
            Assert.That(state.PlayerDeck.ActionCards.Single(card => card.CardId == "A01").UpgradeLevel, Is.EqualTo(0));
        }

        [Test]
        public void RefineSurvivesTheDeckSaveRoundtrip()
        {
            var state = CreateState();
            Assert.That(state.TryRefineCard("A01", out var reason), Is.True, reason);

            var restoredDeck = PlayerDeckSaveData.FromPlayerDeckData(state.PlayerDeck).ToPlayerDeckData();
            var instance = restoredDeck.ActionCards.Single(card => card.CardId == "A01");
            Assert.That(instance.UpgradeLevel, Is.EqualTo(1));

            var resolved = PlayerDeckData.ResolveCard(RefineCatalog(), CardCategory.Action, instance);
            Assert.That(resolved.Amount, Is.EqualTo(5), "복원 후 재해석해도 연마 정의가 나와야 한다.");
        }

        [Test]
        public void ReplaceAnywhereKeepsPileAndPosition()
        {
            var a = SimpleCard("a");
            var b = SimpleCard("b");
            var c = SimpleCard("c");
            var deck = new CardDeckState(new[] { a, b, c });
            var replacement = SimpleCard("b");

            Assert.That(deck.TryReplaceAnywhere(b, replacement), Is.True);
            Assert.That(deck.DrawPile, Is.EqualTo(new[] { a, replacement, c }),
                "치환은 순서를 보존해야 한다 — 제거 후 재삽입이면 손패가 이유 없이 재배열된다.");
            Assert.That(deck.TryReplaceAnywhere(b, replacement), Is.False, "이미 치환된 원본은 다시 못 찾는다.");
        }

        [Test]
        public void PlayerDeckUpgradeCardReplacesOnlyTheMatchingInstance()
        {
            var deck = new PlayerDeckData(
                actionCards: new[]
                {
                    new PlayerCardInstanceData("inst-1", "A01"),
                    new PlayerCardInstanceData("inst-2", "A01")
                });

            var upgraded = deck.UpgradeCard("inst-2", 1);
            Assert.That(upgraded.ActionCards.Single(card => card.InstanceId == "inst-1").UpgradeLevel, Is.EqualTo(0));
            Assert.That(upgraded.ActionCards.Single(card => card.InstanceId == "inst-2").UpgradeLevel, Is.EqualTo(1));
            Assert.That(deck.UpgradeCard("missing", 1), Is.SameAs(deck), "없는 인스턴스면 같은 덱을 돌려준다.");
        }

        // ── helpers ───────────────────────────────────────────────────────────────────

        private CardCatalogEntry Entry(string id)
        {
            return asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.Single(entry => entry.Id == id);
        }

        private void AssertRejected(string upgradeRows, string label)
        {
            var candidate = LoadCsvAsset(upgradeRows.Split('\n'));
            try
            {
                Assert.That(candidate.ValidateRows(out var reason), Is.False, $"{label}: 저작 오류가 통과했다.");
                Assert.That(reason, Is.Not.Empty);
            }
            finally
            {
                Object.DestroyImmediate(candidate);
            }
        }

        private CardCatalogAsset LoadCsvAsset(params string[] upgradeRows)
        {
            var loaded = ScriptableObject.CreateInstance<CardCatalogAsset>();
            loaded.SetRows(CardCatalogAsset.ParseCsvText(File.ReadAllText(CombatCsvPaths.CardsCsv)));
            if (File.Exists(CombatCsvPaths.CardChoiceOptionsCsv))
            {
                loaded.SetChoiceOptionRows(
                    CardCatalogAsset.ParseChoiceOptionsCsvText(File.ReadAllText(CombatCsvPaths.CardChoiceOptionsCsv)));
            }

            if (upgradeRows != null && upgradeRows.Length > 0)
            {
                var text = UpgradeHeader + "\n" + string.Join("\n", upgradeRows);
                loaded.SetUpgradeRows(CardCatalogAsset.ParseUpgradesCsvText(text));
            }

            asset = asset == null ? loaded : asset;
            return loaded;
        }

        private static CombatState CreateState()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 1);
            return new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new[] { new MonsterConfig("refine-monster", new HexCoord(3, 0), 10) },
                config,
                cardCatalog: RefineCatalog());
        }

        private static CardCatalogDefinition RefineCatalog()
        {
            return new CardCatalogDefinition(
                "refine-test",
                "Refine test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.MoveBasicId, "Move", CardCategory.Movement, CardEffectType.Move,
                        1, 2, 2, CardEffectRefs.MoveBasic, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    AttackEntry(amount: 3, upgraded: AttackEntry(amount: 5, namePlus: true))
                });
        }

        private static CardCatalogEntry AttackEntry(int amount, CardCatalogEntry upgraded = null, bool namePlus = false)
        {
            return new CardCatalogEntry(
                "A01", namePlus ? "테스트 공격+" : "테스트 공격", CardCategory.Action, CardEffectType.Attack,
                1, 1, amount, CardEffectRefs.AttackDamage, "living_monster_in_range",
                status: CardCatalogStatus.Approved, upgradedEntry: upgraded);
        }

        private static CardDefinition SimpleCard(string id)
        {
            return new CardDefinition(id, id, CardCategory.Action, CardEffectType.Attack, 1, 1, 1, instanceId: id + "-inst");
        }

        private static CardDefinition FindAnywhere(CardDeckState deck, string cardId)
        {
            return deck.Hand.Concat(deck.DrawPile).Concat(deck.DiscardPile)
                .FirstOrDefault(card => card.Id == cardId);
        }
    }
}
