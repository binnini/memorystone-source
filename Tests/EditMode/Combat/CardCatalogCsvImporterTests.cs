using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime.Cards;
using System.Collections.Generic;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    [Category("ShippingData")]
    public sealed class CardCatalogCsvImporterTests
    {
        private const string CsvPath = CombatCsvPaths.CardsCsv;

        private CardCatalogAsset asset;
        private KeywordCatalogDefinition savedKeywordCatalog;

        [SetUp]
        public void SetUp()
        {
            // Play-mode bootstrap assigns the process-wide keyword catalog and the static survives
            // play-mode exit (no domain reload). These tests assert the undecorated Describe output,
            // so isolate them from whatever a prior play-mode run left behind.
            savedKeywordCatalog = CardKeywordCatalogProvider.Active;
            CardKeywordCatalogProvider.Active = null;
        }

        [TearDown]
        public void TearDown()
        {
            CardKeywordCatalogProvider.Active = savedKeywordCatalog;
            if (asset != null)
            {
                Object.DestroyImmediate(asset);
                asset = null;
            }
        }

        /// <summary>
        /// 🔑 <b>이동 카드도 연마된다</b>(2026-09-02 #14 · 사용자 확정: "이동 칸수 증가").
        ///
        /// <para>이동 카드의 「칸수」 축은 <c>range</c> 하나다 — damage/shield/heal이 전부 비어 있어
        /// 그 축에 저작하면 <c>ValidateAmountAxis</c>가 거부한다(amount 우선순위 함정). 그래서
        /// 이 시험은 「연마가 열렸는가」가 아니라 <b>range가 실제로 올랐는가</b>를 잰다 — 행만 있고
        /// 값이 안 붙으면 연마 버튼은 눌리는데 카드는 그대로인, 가장 나쁜 모양이 된다.</para>
        ///
        /// <para>range가 0인 이동 카드(추진력·도착지를 모르는 여행·전력 질주)는 칸수 축 자체가 없어
        /// <b>일부러</b> 저작하지 않았다 — 그쪽은 민첩·형상·기력이 각자의 축이다.</para>
        /// </summary>
        [Test]
        public void MovementCardsWithARangeAxisAreRefinableAndGainCells()
        {
            asset = LoadCsvAsset();
            // 연마 값은 카드 클래스(CardBehavior.Upgrade)가 준다(P4) — 출하 저작 정의에 클래스 연마를 적용해 본다.
            var catalog = asset.ToCardCatalogDefinition(CombatConfig.Default);

            var refinableMovement = catalog.Entries
                .Where(entry => entry.DeckType == CardCategory.Movement)
                .Select(entry => (entry, upgraded: CardBehaviorRegistry.Get(entry.Id).Upgrade(entry.ToCardDefinition("test", entry.Id + "#inst", 1), 1)))
                .Where(pair => pair.upgraded != null)
                .ToList();

            Assert.That(refinableMovement, Is.Not.Empty,
                "이동 카드에 연마가 하나도 선언되지 않았다 — 연마 후보 화면에서 이동 카드가 통째로 빠진다.");

            foreach (var (entry, upgraded) in refinableMovement)
            {
                if (entry.Range <= 0)
                {
                    // 칸수 축이 없는 이동 카드(M05 추진력 같은 자기 버프)는 다른 축으로 연마한다 — 「무엇이 좋아지는가」는
                    // CardRefineTests.EveryUpgradeChangesWhatTheCardSays가 문안으로 잰다(효과 연마 2차).
                    continue;
                }

                Assert.That(upgraded.Range, Is.GreaterThan(entry.Range),
                    $"{entry.Id}: 연마해도 이동 칸수가 늘지 않는다({entry.Range} → {upgraded.Range}).");
                Assert.That(upgraded.Amount, Is.EqualTo(upgraded.Range),
                    $"{entry.Id}: 이동 카드의 amount는 range 파생이다 — 연마가 둘을 같이 올려야 한다.");
            }
        }

        [Test]
        public void CardsCsvBuildsValidCatalog()
        {
            asset = LoadCsvAsset();

            Assert.That(asset.ValidateRows(out var reason), Is.True, reason);
            var catalog = asset.ToCardCatalogDefinition(CombatConfig.Default);
            Assert.That(catalog.Validate(out reason), Is.True, reason);
            // 🔑 상수 59가 아니라 **파싱된 행 수**와 맞춘다(2026-08-31 T4). 여기서 지키는 계약은
            // "오늘 카드가 59장"이 아니라 **"변환이 행을 조용히 흘리지 않는다"**이다 — 상수였을 때는
            // 카드를 추가할 때마다 이 줄이 거짓 경보로 깨졌고(위 변경 이력이 그 흔적이다),
            // 정작 파서가 한 줄을 건너뛰어도 상수를 같이 고쳐 버리면 아무도 몰랐다.
            Assert.That(asset.Rows, Is.Not.Empty, "출하 CSV를 한 줄도 못 읽었다 — 아래 대조가 빈 채로 통과한다.");
            Assert.That(
                catalog.Entries, Has.Count.EqualTo(asset.Rows.Count),
                "CSV 행 하나가 카탈로그 엔트리 하나다 — 수가 다르면 변환이 행을 흘렸다.");
            Assert.That(catalog.Entries.Single(entry => entry.Id == "F01").Amount, Is.EqualTo(6));
            Assert.That(catalog.Entries.Single(entry => entry.Id == "F01").Description, Does.Contain("{Damage}"));
            Assert.That(catalog.Entries.Single(entry => entry.Id == "A07").Range, Is.EqualTo(2));
            Assert.That(catalog.Entries.Single(entry => entry.Id == "A07").Targeting, Is.EqualTo("living_monster_in_range_2"));
        }

        /// <summary>
        /// WS-I I-08(DEC-2026-08-19-08): Amount는 damage→shield→heal 우선으로 접히므로, damage와
        /// heal을 동시에 저작한 A03의 회복량(4)은 별도 축(HealAmount)에 보존돼야 한다 — 예전엔
        /// 죽은 데이터라 표시·집행 모두 damage(3)로 회복했다.
        /// </summary>
        [Test]
        public void HealColumnSurvivesTheAmountCollapse()
        {
            asset = LoadCsvAsset();
            var catalog = asset.ToCardCatalogDefinition(CombatConfig.Default);

            var holyLight = catalog.Entries.Single(entry => entry.Id == "A03");
            Assert.That(holyLight.Amount, Is.EqualTo(3), "Amount는 damage 우선으로 접힌다(기존 계약 유지).");
            Assert.That(holyLight.HealAmount, Is.EqualTo(4), "heal 컬럼의 저작값은 HealAmount에 보존된다.");
            Assert.That(holyLight.ToCardDefinition(holyLight.Id).EffectiveHealAmount, Is.EqualTo(4), "회복 표시·집행은 heal 축을 쓴다.");

            var basicHeal = catalog.Entries.Single(entry => entry.Id == "S02");
            Assert.That(basicHeal.ToCardDefinition(basicHeal.Id).EffectiveHealAmount, Is.EqualTo(basicHeal.Amount), "heal 단일 축 카드는 접힌 Amount와 같다(폴백).");
        }

        [Test]
        public void ScoutBasicImportsAsRevealOnlyScoutCard()
        {
            asset = LoadCsvAsset();
            var entry = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries
                .Single(card => card.Id == CardIds.BasicScout);

            // S00 exercises the whole CSV → whitelist → import path with no new behavior: scout.reveal has no
            // handler, so ApplyAfterScoutReveal no-ops and the card reveals its blast area and nothing else.
            Assert.That(entry.ActionType, Is.EqualTo(CardEffectType.Scout));
            Assert.That(entry.DeckType, Is.EqualTo(CardCategory.Action));
            Assert.That(entry.TargetMode, Is.EqualTo(CardTargetMode.Tile));
            Assert.That(entry.Targeting, Is.EqualTo("walkable_map_cell"));
            Assert.That(entry.Cost, Is.EqualTo(1));
            Assert.That(entry.Range, Is.EqualTo(3));
            Assert.That(entry.AreaRadius, Is.EqualTo(1));
            Assert.That(entry.Amount, Is.EqualTo(0));
            Assert.That(entry.Status, Is.EqualTo(CardCatalogStatus.Approved));
            Assert.That(entry.IncludeInGameplayDecks, Is.True);
        }

        [Test]
        public void CleanseCardsAuthorTheirStunExemptionAndDelayedPenalty()
        {
            asset = LoadCsvAsset();
            var entries = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.ToDictionary(entry => entry.Id);

            // The 기절 exemption is the reason U03/D06 exist: a cleanse you cannot play while stunned is
            // useless. D04 is a plain block card and stays gated. Since P3 the exemption is the card class
            // declaration (CardBehavior.UsableWhileStunned), not a CSV column.
            Assert.That(CardBehaviorRegistry.Get(CardIds.CleanseDraw).UsableWhileStunned, Is.True);
            Assert.That(CardBehaviorRegistry.Get(CardIds.Hospitalization).UsableWhileStunned, Is.True);
            Assert.That(CardBehaviorRegistry.Get(CardIds.HeavyArmor).UsableWhileStunned, Is.False);

            // Design rule (2026-07-25): the stun exemption is exclusive to the two cleanse cards. Any other
            // card declaring it is a rule change, not a new feature.
            Assert.That(
                entries.Keys.Where(id => CardBehaviorRegistry.Get(id).UsableWhileStunned),
                Is.EquivalentTo(new[]
                {
                    CardIds.CleanseDraw,
                    CardIds.Hospitalization
                }));


            // shield → Amount, duration → the booked 속박 length. Both are card data, not handler constants.
            Assert.That(entries[CardIds.Hospitalization].Amount, Is.EqualTo(5));
            Assert.That(entries[CardIds.HeavyArmor].Amount, Is.EqualTo(8));
            Assert.That(entries[CardIds.HeavyArmor].Cost, Is.EqualTo(0));
            Assert.That(entries[CardIds.Hospitalization].DurationTurns, Is.EqualTo(1));
            Assert.That(entries[CardIds.HeavyArmor].DurationTurns, Is.EqualTo(1));
        }

        [Test]
        public void ScoutExtensionCardsAuthorTheirScalarsInCsv()
        {
            asset = LoadCsvAsset();
            var entries = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.ToDictionary(entry => entry.Id);

            var stunFlash = entries[CardIds.StunFlash];
            Assert.That(stunFlash.ActionType, Is.EqualTo(CardEffectType.Scout));
            // The reveal radius IS the stun radius (the handler stuns whatever the scout just revealed), so
            // this single number governs both. Narrowed from 2 to 1.
            Assert.That(stunFlash.AreaRadius, Is.EqualTo(1));
            // duration is the 기절 length the handler reads — the only place it exists.
            Assert.That(stunFlash.DurationTurns, Is.EqualTo(1));

            var bingo = entries[CardIds.Bingo];
            Assert.That(bingo.Amount, Is.EqualTo(5), "heal → Amount.");
            // The 3-enemy threshold is the card class's rule constant (DEC-2026-09-06-01 superseded D11); the
            // description text says "3명 이상", so the constant and the sentence have to agree.
            Assert.That(bingo.Description, Does.Contain(S04_Bingo.Threshold.ToString()));
        }

        [Test]
        public void FieldExtensionCardsResolveTheirKindFromTheirCardClass()
        {
            asset = LoadCsvAsset();
            var entries = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.ToDictionary(entry => entry.Id);

            var lifesteal = entries[CardIds.LifestealZone];
            Assert.That(lifesteal.FieldObjectKind, Is.EqualTo(CardFieldObjectKind.LifestealDamage));
            Assert.That(lifesteal.DurationTurns, Is.EqualTo(2), "A field row with a non-positive duration fails the import outright.");
            Assert.That(lifesteal.Amount, Is.EqualTo(2));

            // F05 콩콩탄탄 is plain field.damage, but it authors hitCount=2: one tick lands 2 damage twice.
            // DEC-2026-07-24-01 reinstates the split P4 had folded away — the pass-through damage is
            // deliberately unchanged (block is a pool), so this is a 타격감 change, not a balance one.
            var bounceBomb = entries[CardIds.BounceBomb];
            Assert.That(bounceBomb.FieldObjectKind, Is.EqualTo(CardFieldObjectKind.FieldDamage));
            Assert.That(bounceBomb.Amount, Is.EqualTo(2), "2 damage per hit.");
            Assert.That(bounceBomb.HitCount, Is.EqualTo(2), "Two hits per tick — the field reads this as HitsPerTick.");
            Assert.That(bounceBomb.AreaRadius, Is.EqualTo(2));
            Assert.That(bounceBomb.Description, Does.Contain("{HitCount}"), "The text must print the hit count, not imply it.");
            // 2026-07-25 밸런스 조정으로 다시 게임에 등장(Rare): deck/reward 대상.
            Assert.That(bounceBomb.IncludeInGameplayDecks, Is.True);
            Assert.That(bounceBomb.VisibleInCatalog, Is.True);
        }

        [Test]
        public void RemnantAttackAuthorsItsScalingModeInCsv()
        {
            asset = LoadCsvAsset();
            var entry = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries
                .Single(card => card.Id == CardIds.Remnant);

            // A13 needs no handler: attack.damage plus a scaling mode is the whole card.
            Assert.That(entry.ScalingMode, Is.EqualTo(CardScalingMode.ExiledCards));
            Assert.That(entry.Amount, Is.EqualTo(3));
            Assert.That(entry.Description, Does.Contain("{HitCount}"), "The repeat count must be printed, not implied.");
        }

        [Test]
        public void DrawOrRecoverAuthorsBothChoiceOptionsAndItsDrawCount()
        {
            asset = LoadCsvAsset();
            var entry = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries
                .Single(card => card.Id == CardIds.DrawOrRecover);

            // The card class declaring choices is what flips the row to PlayMode.Choice, which is what makes a
            // utility card reachable through the choice panel instead of the utility path.
            Assert.That(entry.PlayMode, Is.EqualTo(CardPlayMode.Choice));
            Assert.That(
                CardBehaviorRegistry.Get(entry.Id).Choices.Select(option => option.OptionId),
                Is.EqualTo(new[] { "draw", "recover" }));
            Assert.That(entry.Description, Does.Contain(U02_DrawOrRecover.DrawCount.ToString()), "The rule constant and the sentence must agree.");
            // Declaration and prose must line up or ValidateRows rejects the import.
            Assert.That(entry.ChoiceOptionTexts, Does.Contain("draw"));
            Assert.That(entry.ChoiceOptionTexts, Does.Contain("recover"));
        }

        [Test]
        public void PlagueAndTalismanShieldAuthorTheirBehaviorAndPostAction()
        {
            asset = LoadCsvAsset();
            var entries = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.ToDictionary(entry => entry.Id);

            var plague = entries[CardIds.Plague];
            Assert.That(plague.ActionType, Is.EqualTo(CardEffectType.Attack));
            // 반경 2 = 2026-08-20 #18(인접 → 2칸 내 최근접). 저작면은 카드 클래스 A12_Plague.SpreadRadius 한 곳이다.
            Assert.That(
                CardBehaviorRegistry.Get(plague.Id).PostActions.Select(action => action.ActionId),
                Does.Contain(CardBehaviorMetadata.PostActionSpreadStatus));
            Assert.That(A12_Plague.SpreadRadius, Is.EqualTo(2));
            Assert.That(plague.Amount, Is.EqualTo(3));

            var shield = entries[CardIds.TalismanShield];
            Assert.That(shield.ActionType, Is.EqualTo(CardEffectType.Defend));
            Assert.That(shield.PlayMode, Is.EqualTo(CardPlayMode.Self));
            Assert.That(shield.Amount, Is.EqualTo(0), "It grants immunity, not block — a shield value would be a lie.");
        }

        [Test]
        public void CardsCsvAssignsRewardRarityGrades()
        {
            asset = LoadCsvAsset();
            var entries = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.ToDictionary(entry => entry.Id);

            // Basic (never offered as a reward).
            Assert.That(entries["M01"].Rarity, Is.EqualTo(CardRarity.Basic));
            Assert.That(entries["A00"].Rarity, Is.EqualTo(CardRarity.Basic));
            Assert.That(entries["D00"].Rarity, Is.EqualTo(CardRarity.Basic));
            // Rare / Epic / Legendary samples across types.
            Assert.That(entries["A01"].Rarity, Is.EqualTo(CardRarity.Rare));
            Assert.That(entries["U01"].Rarity, Is.EqualTo(CardRarity.Rare));
            Assert.That(entries["F03"].Rarity, Is.EqualTo(CardRarity.Rare));
            Assert.That(entries["A10"].Rarity, Is.EqualTo(CardRarity.Rare));
            Assert.That(entries["F04"].Rarity, Is.EqualTo(CardRarity.Epic));
            Assert.That(entries["A13"].Rarity, Is.EqualTo(CardRarity.Legendary));
            Assert.That(entries["A04"].Rarity, Is.EqualTo(CardRarity.Legendary));
            Assert.That(entries["S01"].Rarity, Is.EqualTo(CardRarity.Legendary));
            Assert.That(entries["F02"].Rarity, Is.EqualTo(CardRarity.Legendary));

            // No reward-eligible (includeInDecks) card may stay Basic except the explicit basic starters.
            var basicStarters = new[] { "M01", "M02", "M03", "M04", "A00", "D00" };
            var orphanBasics = entries.Values
                .Where(entry => entry.IncludeInGameplayDecks
                    && entry.Rarity == CardRarity.Basic
                    && !basicStarters.Contains(entry.Id))
                .Select(entry => entry.Id)
                .ToArray();
            Assert.That(orphanBasics, Is.Empty,
                "Reward-eligible cards missing a rarity grade: " + string.Join(", ", orphanBasics));
        }

        [Test]
        public void ChoiceTextsColumnFeedsChoiceOptionUiTexts()
        {
            asset = LoadCsvAsset();
            var entry = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.Single(card => card.Id == "A03");

            Assert.That(entry.ChoiceOptionTexts, Does.Contain("heal|\uC131\uC2A4\uB7EC\uC6B4 \uBE5B|\uC790\uC2E0\uC744 {Heal} \uD68C\uBCF5\uD569\uB2C8\uB2E4."));
            Assert.That(entry.ChoiceOptionTexts, Does.Contain("attack|\uC131\uC2A4\uB7EC\uC6B4 \uBE5B|\uC0AC\uAC70\uB9AC {Range} \uB0B4 \uC801\uC5D0\uAC8C \uD53C\uD574 {Damage}\uB97C \uC90D\uB2C8\uB2E4."));
            Assert.That(entry.ChoiceOptionTexts, Does.Not.Contain("Ki {Cost}"));
        }
        [Test]
        public void TokenColumnsFollowCombatConfigAtBuildTime()
        {
            asset = LoadCsvAsset();
            var config = new CombatConfig(
                playerMaxHp: 20,
                enemyMaxHp: 10,
                playerMovePoints: 2,
                attackRange: 9,
                attackDamage: 17,
                defenseBlock: 4,
                enemyChaseRange: 5,
                enemyAttackRange: 1,
                enemyAttackDamage: 3,
                actionBudget: 8);

            var entries = asset.ToCardCatalogDefinition(config).Entries.ToDictionary(entry => entry.Id);

            Assert.That(entries["A01"].Range, Is.EqualTo(0));
            Assert.That(entries["A01"].Amount, Is.EqualTo(3));
            Assert.That(entries["A05"].Cost, Is.EqualTo(8));
        }

        [Test]
        public void CardsCsvDescriptionsUseDataTokensInsteadOfDerivedOrHardcodedRanges()
        {
            var csvText = File.ReadAllText(CsvPath);

            Assert.That(csvText, Does.Contain("{Shape} \uB0B4\uC758"));
            Assert.That(csvText, Does.Contain("{Shape}\uB97C \uD0D0\uC0C9"));
            Assert.That(csvText, Does.Not.Contain("\uBC94\uC704 {Shape}"));
            Assert.That(csvText, Does.Contain("{HitCount}\uBC88"));
            Assert.That(csvText, Does.Not.Contain("\uC0AC\uAC70\uB9AC {AttackRange}"));
            Assert.That(csvText, Does.Not.Contain("{AreaRadius}"));
            Assert.That(csvText, Does.Not.Contain("\uC0AC\uAC70\uB9AC 3 \uB0B4"));
        }

        [Test]
        public void CardsCsvDescriptionFeedsRuntimeCardSnapshots()
        {
            asset = LoadCsvAsset();
            var catalog = asset.ToCardCatalogDefinition(CombatConfig.Default);
            var describe = typeof(CombatState).GetMethod("Describe", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(CardDefinition) }, null);
            Assert.That(describe, Is.Not.Null);

            string Describe(string cardId) => (string)describe.Invoke(
                null,
                new object[] { catalog.Entries.Single(card => card.Id == cardId).ToCardDefinition(catalog.SourceId) });

            // "\uD53C\uD574 3\uC744" not "3\uB97C": the template author writes "{Damage}\uB97C", and KoreanParticle
            // agrees the particle with the substituted value (3 reads \uC0BC, which closes with \u3141).
            Assert.That(Describe("A03"), Is.EqualTo("\uAC08\uB9BC\uAE38 \u2014 \uC790\uC2E0\uC744 4 \uD68C\uBCF5\uD558\uAC70\uB098, \uC120\uD0DD\uD55C \uC801\uC5D0\uAC8C \uD53C\uD574 3\uC744 \uC90D\uB2C8\uB2E4."));
            Assert.That(Describe("A06"), Is.EqualTo("\uC120\uD0DD\uD55C \uC801\uC5D0\uAC8C \uD53C\uD574 2\uB97C 2\uBC88 \uBC18\uBCF5\uD569\uB2C8\uB2E4."));
            Assert.That(Describe("F02"), Does.Contain("회복 5"));
            Assert.That(Describe("F02"), Does.Not.Contain("{Heal}"));
            Assert.That(Describe("F03"), Is.EqualTo("\uD134 \uC2DC\uC791 \uC2DC \uBC94\uC704 2\uC5D0 2\uD134 \uB3D9\uC548 \uC18D\uBC15 \uC7A5\uD310\uC744 \uBC30\uCE58\uD569\uB2C8\uB2E4."));
            // P0.6 \uC6A9\uC5B4: '\uC561\uC158' \u2192 '\uD589\uB3D9', \uADF8\uB9AC\uACE0 A10 \uD55C\uC815\uC73C\uB85C '\uCE74\uB4DC' \u2192 '\uBD80\uC801'(D12).
            Assert.That(Describe("A10"), Does.Contain("\uD589\uB3D9 \uBD80\uC801"));
            Assert.That(Describe("A10"), Does.Not.Contain("\uC561\uC158"));
            Assert.That(Describe("A10"), Does.Contain("\uC18C\uBA78"));
        }

        [Test]
        public void AttackCardsInHandDescriptionUsesCurrentHandHitCount()
        {
            asset = LoadCsvAsset();
            var catalog = asset.ToCardCatalogDefinition(CombatConfig.Default);
            var cards = catalog.Entries.ToDictionary(entry => entry.Id);
            CardDefinition ToCard(string cardId) => cards[cardId].ToCardDefinition(catalog.SourceId);
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                CombatConfig.Default,
                cardCatalog: catalog,
                actionDeck: new CardDeckState(
                    drawPile: null,
                    hand: new[] { ToCard("A04"), ToCard("A01"), ToCard("A03") },
                    discardPile: null,
                    removedPile: null),
                drawOpeningHands: false);

            var snapshot = state.GetCombatCards().Single(card => card.Id == "A04");

            Assert.That(snapshot.Description, Does.Contain("3\uBC88"));
        }

        [Test]
        public void CardRewardDescriptionUsesFullCatalogDescriptionText()
        {
            asset = LoadCsvAsset();
            var catalog = asset.ToCardCatalogDefinition(CombatConfig.Default);
            var describeReward = typeof(MapCombatController).GetMethod("DescribeRewardCatalogEntry", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(describeReward, Is.Not.Null);

            var description = (string)describeReward.Invoke(
                null,
                new object[] { catalog.Entries.Single(card => card.Id == "A06"), catalog.SourceId });

            Assert.That(description, Is.EqualTo("\uC120\uD0DD\uD55C \uC801\uC5D0\uAC8C \uD53C\uD574 2\uB97C 2\uBC88 \uBC18\uBCF5\uD569\uB2C8\uB2E4."));
            Assert.That(description, Does.Not.Contain("\uBE44\uC6A9"));
        }

        [Test]
        public void CardsCsvRegistersIllustrationIdsForCardFront()
        {
            asset = LoadCsvAsset();
            var entries = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.ToDictionary(entry => entry.Id);

            Assert.That(entries["A01"].PresentationRef.IllustrationId, Is.EqualTo("card_illust_A01"));
            Assert.That(entries["M01"].PresentationRef.IllustrationId, Is.EqualTo("card_illust_M01"));
            Assert.That(entries.Values.Select(entry => entry.PresentationRef.IllustrationId), Is.All.Not.Empty);
        }

        [Test]
        public void CardsCsvRejectsACardIdWithoutARegisteredClass()
        {
            // A card row is only as real as its CardBehavior class: an id nobody registered has no rules, so the
            // import refuses it instead of shipping a card that silently runs the generic path.
            var csvText = File.ReadAllText(CsvPath).Replace("\nA00,", "\nA99,");
            Assume.That(csvText, Does.Contain("\nA99,"));
            asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            asset.SetRows(CardCatalogAsset.ParseCsvText(csvText));

            Assert.That(asset.ValidateRows(out var reason), Is.False);
            Assert.That(reason, Does.Contain("A99").And.Contain("CardBehavior"));
        }

        [Test]
        public void CardsCsvRejectsChoiceTextsThatDoNotMatchTheCardClassChoices()
        {
            // A03's class declares heal + attack; describing a different option id is an authoring error.
            var csvText = File.ReadAllText(CsvPath).Replace("heal|성스러운 빛|", "mend|성스러운 빛|");
            Assume.That(csvText, Does.Contain("mend|"));
            asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            asset.SetRows(CardCatalogAsset.ParseCsvText(csvText));

            Assert.That(asset.ValidateRows(out var reason), Is.False);
            Assert.That(reason, Does.Contain("choiceTexts").And.Contain("A03_HolyLight"));
        }

        [Test]
        public void ColumnsAreReadByHeaderNameNotByPosition()
        {
            // Header-name mapping: the same row parses identically when two columns swap places.
            var values = SampleRowValues(illustrationId: "card_illust_swap");
            var header = ExpectedHeader.ToArray();
            var shieldIndex = System.Array.IndexOf(header, "shield");
            var rarityIndex = System.Array.IndexOf(header, "rarity");
            (header[shieldIndex], header[rarityIndex]) = (header[rarityIndex], header[shieldIndex]);
            var row = CardCatalogAsset.ParseCsvText(
                string.Join(",", header) + "\n" + string.Join(",", header.Select(name => values[name]))).Single();

            Assert.That(row.Shield, Is.EqualTo("4"));
            Assert.That(row.Rarity, Is.EqualTo("Basic"));
            Assert.That(row.IllustrationId, Is.EqualTo("card_illust_swap"));
        }

        [Test]
        public void MissingOrUnknownColumnsFailTheParse()
        {
            var values = SampleRowValues();
            var withoutRarity = ExpectedHeader.Where(name => name != "rarity").ToArray();
            Assert.That(
                () => CardCatalogAsset.ParseCsvText(string.Join(",", withoutRarity) + "\n" + string.Join(",", withoutRarity.Select(name => values[name]))),
                Throws.TypeOf<System.FormatException>().With.Message.Contains("rarity"));

            var withStray = ExpectedHeader.Concat(new[] { "behaviorId" }).ToArray();
            Assert.That(
                () => CardCatalogAsset.ParseCsvText(string.Join(",", withStray) + "\n" + string.Join(",", ExpectedHeader.Select(name => values[name])) + ",attack.damage"),
                Throws.TypeOf<System.FormatException>().With.Message.Contains("behaviorId"));
        }

        [Test]
        public void TrailingColumnsReachTheEntries()
        {
            asset = LoadCsvAsset();
            var entries = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.ToDictionary(entry => entry.Id);

            Assert.That(entries["M01"].PresentationRef.IllustrationId, Is.EqualTo("card_illust_M01"));
            Assert.That(entries["A10"].Rarity, Is.EqualTo(CardRarity.Rare));
            // M05 is the one card whose display category (gameplayType) is authored rather than derived from type.
            Assert.That(entries["M05"].GameplayType, Is.EqualTo(CardGameplayType.Buff));
            Assert.That(entries["M06"].TargetMode, Is.EqualTo(CardTargetMode.RandomReachable), "target=random_tile");
        }

        [Test]
        public void BuffColumnsCarryTheAuthoredMagnitudeAndDuration()
        {
            // P0.5: stateEffect/buff_debuff were validated but never read — M05's `Agility:2` and D03's
            // `Reflect:50` were decorative while the real values came from importer fallbacks. Editing the
            // CSV changed nothing. These assertions are what makes that regression loud.
            asset = LoadCsvAsset();
            var entries = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.ToDictionary(entry => entry.Id);

            Assert.That(entries["M05"].BuffDebuff, Is.EqualTo("Agility:2"));
            Assert.That(entries["M05"].DurationTurns, Is.EqualTo(1));
            Assert.That(entries["D03"].BuffDebuff, Is.EqualTo("Reflect:50"));
            Assert.That(entries["D03"].DurationTurns, Is.EqualTo(1));
            // D02's 강화는 WS-I I-14(DEC-2026-08-19-08)에서 「다음 턴 1턴」으로 재저작됐다 —
            // 즉시 부여가 이번 턴 몬스터 행동에 새던 것을 지연 예약으로 바꾸면서 지속도 문안(다음 턴)에 맞췄다.
            Assert.That(entries["D02"].BuffDebuff, Is.EqualTo("Strength:100"));
            Assert.That(entries["D02"].DurationTurns, Is.EqualTo(1));

            Assert.That(CardBehaviorMetadata.GetEffectAmount(entries["D02"].BuffDebuff, "Strength", 0), Is.EqualTo(100));
            Assert.That(CardBehaviorMetadata.GetEffectAmount(entries["D02"].BuffDebuff, "Reflect", -1), Is.EqualTo(-1),
                "An absent kind must report the fallback, not another kind's value.");
        }

        [Test]
        public void PromotedCardsNoLongerDependOnImporterAmountFallbacks()
        {
            // The importer used to hand M05/D03 their magnitude through behaviorId-keyed fallbacks. Amount is
            // now 0 for both: the value lives in buff_debuff. A06/S02 keep authoring their own columns.
            asset = LoadCsvAsset();
            var entries = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.ToDictionary(entry => entry.Id);

            Assert.That(entries["M05"].Amount, Is.EqualTo(0));
            Assert.That(entries["D03"].Amount, Is.EqualTo(0));
            Assert.That(entries["A06"].HitCount, Is.EqualTo(2), "A06 authors hitCount; nothing infers it from the behaviorId.");
            Assert.That(entries["S02"].Amount, Is.EqualTo(2), "S02 authors heal=2.");
            // The surviving derivation: a plain move card's amount is its range.
            Assert.That(entries["M02"].Amount, Is.EqualTo(entries["M02"].Range));
        }

        [Test]
        public void ShieldColumnReachesTheRuntimeCardDefinition()
        {
            var row = ParseSingleRow(RowWith(id: "D06", shield: "5"));

            asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            asset.SetRows(new[] { row });
            var card = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.Single().ToCardDefinition("test");

            Assert.That(card.Amount, Is.EqualTo(5), "shield must be read by its header name.");
        }

        [Test]
        public void MalformedGameplayTypeFailsTheImport()
        {
            asset = ScriptableObject.CreateInstance<CardCatalogAsset>();

            asset.SetRows(new[] { ParseSingleRow(RowWith(gameplayType: "Sparkle")) });
            Assert.That(asset.ValidateRows(out var reason), Is.False, "gameplayType must be a CardGameplayType name or empty.");
            Assert.That(reason, Does.Contain("gameplayType"));
        }

        private static string[] ExpectedHeader => (string[])typeof(CardCatalogAsset)
            .GetField("ExpectedHeader", BindingFlags.Static | BindingFlags.NonPublic)
            .GetValue(null);

        // A minimal 방어 row, keyed by column name so the tests never depend on column positions.
        private static Dictionary<string, string> SampleRowValues(
            string id = "D00", string shield = "4", string gameplayType = "", string illustrationId = null)
        {
            var values = ExpectedHeader.ToDictionary(name => name, _ => string.Empty, System.StringComparer.Ordinal);
            values["id"] = id;
            values["name"] = "방어";
            values["description"] = "테스트";
            values["type"] = "방어";
            values["target"] = "self";
            values["cost"] = "1";
            values["range"] = "0";
            values["shield"] = shield;
            values["targeting"] = "self";
            values["gameplayType"] = gameplayType;
            values["status"] = "Approved";
            values["includeInDecks"] = "TRUE";
            values["visibleInCatalog"] = "TRUE";
            values["illustrationId"] = illustrationId ?? "card_illust_" + id;
            values["rarity"] = "Basic";
            return values;
        }

        private static string RowWith(string id = "D00", string shield = "4", string gameplayType = "")
        {
            var values = SampleRowValues(id, shield, gameplayType);
            return string.Join(",", ExpectedHeader.Select(name => values[name]));
        }

        private static CardCatalogCsvRow ParseSingleRow(string row)
        {
            return CardCatalogAsset.ParseCsvText(string.Join(",", ExpectedHeader) + "\n" + row).Single();
        }

        private static CardCatalogAsset LoadCsvAsset()
        {
            var csvText = File.ReadAllText(CsvPath);
            var parsedRows = CardCatalogAsset.ParseCsvText(csvText);
            var asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            asset.SetRows(parsedRows);
            return asset;
        }
    }
}


