using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
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
        private const string ChoiceOptionsCsvPath = CombatCsvPaths.CardChoiceOptionsCsv;

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
            // 연마 행은 별도 CSV라 기본 로더가 싣지 않는다 — 이 시험의 대상이 바로 그 표라 직접 싣는다.
            Assert.That(File.Exists(CombatCsvPaths.CardUpgradesCsv), Is.True,
                "card_upgrades.csv가 없다 — 연마 저작 정본이 사라졌다.");
            asset.SetUpgradeRows(CardCatalogAsset.ParseUpgradesCsvText(
                File.ReadAllText(CombatCsvPaths.CardUpgradesCsv)));
            var catalog = asset.ToCardCatalogDefinition(CombatConfig.Default);

            var refinableMovement = catalog.Entries
                .Where(entry => entry.DeckType == CardCategory.Movement && entry.UpgradedEntry != null)
                .ToList();

            Assert.That(refinableMovement, Is.Not.Empty,
                "이동 카드에 연마 값이 하나도 저작되지 않았다 — 연마 후보 화면에서 이동 카드가 통째로 빠진다.");

            foreach (var entry in refinableMovement)
            {
                Assert.That(entry.Range, Is.GreaterThan(0),
                    $"{entry.Id}: 칸수 축(range)이 없는 이동 카드에 연마를 저작했다 — 무엇이 좋아지는지 말할 수 없다.");
                Assert.That(entry.UpgradedEntry.Range, Is.GreaterThan(entry.Range),
                    $"{entry.Id}: 연마해도 이동 칸수가 늘지 않는다({entry.Range} → {entry.UpgradedEntry.Range}).");
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
                .Single(card => card.Id == ApprovedCardCatalogFactory.ScoutBasicId);

            // S00 exercises the whole CSV → whitelist → import path with no new behavior: scout.reveal has no
            // handler, so ApplyAfterScoutReveal no-ops and the card reveals its blast area and nothing else.
            Assert.That(entry.EffectRef, Is.EqualTo(CardEffectRefs.ScoutReveal));
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
            // useless. D04 is a plain block card and stays gated.
            Assert.That(entries[ApprovedCardCatalogFactory.UtilityCleanseDrawId].UsableWhileStunned, Is.True);
            Assert.That(entries[ApprovedCardCatalogFactory.DefendHospitalizationId].UsableWhileStunned, Is.True);
            Assert.That(entries[ApprovedCardCatalogFactory.DefendHeavyArmorId].UsableWhileStunned, Is.False);

            // Design rule (2026-07-25): the stun exemption is exclusive to the two cleanse cards. Any other
            // card gaining usableWhileStunned in the CSV is an authoring error, not a new feature.
            Assert.That(
                entries.Values.Where(entry => entry.UsableWhileStunned).Select(entry => entry.Id),
                Is.EquivalentTo(new[]
                {
                    ApprovedCardCatalogFactory.UtilityCleanseDrawId,
                    ApprovedCardCatalogFactory.DefendHospitalizationId
                }));

            Assert.That(entries[ApprovedCardCatalogFactory.UtilityCleanseDrawId].EffectRef, Is.EqualTo(CardEffectRefs.UtilityCleanseDraw));
            Assert.That(entries[ApprovedCardCatalogFactory.DefendHospitalizationId].EffectRef, Is.EqualTo(CardEffectRefs.DefendCleanseBlock));
            Assert.That(entries[ApprovedCardCatalogFactory.DefendHeavyArmorId].EffectRef, Is.EqualTo(CardEffectRefs.DefendBlockDelayedImmobilize));

            // shield → Amount, duration → the booked 속박 length. Both are card data, not handler constants.
            Assert.That(entries[ApprovedCardCatalogFactory.DefendHospitalizationId].Amount, Is.EqualTo(5));
            Assert.That(entries[ApprovedCardCatalogFactory.DefendHeavyArmorId].Amount, Is.EqualTo(8));
            Assert.That(entries[ApprovedCardCatalogFactory.DefendHeavyArmorId].Cost, Is.EqualTo(0));
            Assert.That(entries[ApprovedCardCatalogFactory.DefendHospitalizationId].DurationTurns, Is.EqualTo(1));
            Assert.That(entries[ApprovedCardCatalogFactory.DefendHeavyArmorId].DurationTurns, Is.EqualTo(1));
        }

        [Test]
        public void ScoutExtensionCardsAuthorTheirScalarsInCsv()
        {
            asset = LoadCsvAsset();
            var entries = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.ToDictionary(entry => entry.Id);

            var stunFlash = entries[ApprovedCardCatalogFactory.ScoutStunFlashId];
            Assert.That(stunFlash.EffectRef, Is.EqualTo(CardEffectRefs.ScoutEnemyStun));
            Assert.That(stunFlash.ActionType, Is.EqualTo(CardEffectType.Scout));
            // The reveal radius IS the stun radius (the handler stuns whatever the scout just revealed), so
            // this single number governs both. Narrowed from 2 to 1.
            Assert.That(stunFlash.AreaRadius, Is.EqualTo(1));
            // duration is the 기절 length the handler reads — the only place it exists.
            Assert.That(stunFlash.DurationTurns, Is.EqualTo(1));

            var bingo = entries[ApprovedCardCatalogFactory.ScoutBingoId];
            Assert.That(bingo.EffectRef, Is.EqualTo(CardEffectRefs.ScoutEnemyCountHealThreshold));
            Assert.That(bingo.Amount, Is.EqualTo(5), "heal → Amount.");
            // D11: the 3-enemy threshold lives in behaviorParams, not in the handler. The description text
            // says "3명 이상", so the token and the sentence have to agree.
            Assert.That(bingo.BehaviorParams, Is.EqualTo("threshold:3"));
            Assert.That(bingo.Description, Does.Contain("3"));
        }

        [Test]
        public void FieldExtensionCardsResolveTheirKindFromTheBehaviorId()
        {
            asset = LoadCsvAsset();
            var entries = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.ToDictionary(entry => entry.Id);

            var lifesteal = entries[ApprovedCardCatalogFactory.FieldLifestealId];
            Assert.That(lifesteal.EffectRef, Is.EqualTo(CardEffectRefs.FieldLifesteal));
            Assert.That(lifesteal.FieldObjectKind, Is.EqualTo(CardFieldObjectKind.LifestealDamage));
            Assert.That(lifesteal.DurationTurns, Is.EqualTo(2), "A field row with a non-positive duration fails the import outright.");
            Assert.That(lifesteal.Amount, Is.EqualTo(2));

            // F05 콩콩탄탄 is plain field.damage, but it authors hitCount=2: one tick lands 2 damage twice.
            // DEC-2026-07-24-01 reinstates the split P4 had folded away — the pass-through damage is
            // deliberately unchanged (block is a pool), so this is a 타격감 change, not a balance one.
            var bounceBomb = entries[ApprovedCardCatalogFactory.FieldBounceBombId];
            Assert.That(bounceBomb.EffectRef, Is.EqualTo(CardEffectRefs.FieldDamage));
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
                .Single(card => card.Id == ApprovedCardCatalogFactory.AttackRemnantId);

            // A13 needs no handler: attack.damage plus a scaling mode is the whole card.
            Assert.That(entry.EffectRef, Is.EqualTo(CardEffectRefs.AttackDamage));
            Assert.That(entry.ScalingMode, Is.EqualTo(CardScalingMode.ExiledCards));
            Assert.That(entry.Amount, Is.EqualTo(3));
            Assert.That(entry.Description, Does.Contain("{HitCount}"), "The repeat count must be printed, not implied.");
        }

        [Test]
        public void DrawOrRecoverAuthorsBothChoiceOptionsAndItsDrawCount()
        {
            asset = LoadCsvAsset();
            var entry = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries
                .Single(card => card.Id == ApprovedCardCatalogFactory.UtilityDrawOrRecoverId);

            // choiceOptions alone flips a row to PlayMode.Choice, which is what makes a utility card
            // reachable through the choice panel instead of the utility path.
            Assert.That(entry.PlayMode, Is.EqualTo(CardPlayMode.Choice));
            Assert.That(entry.EffectRef, Is.EqualTo(CardEffectRefs.UtilityDrawOrRecover));
            Assert.That(
                entry.ChoiceOptions,
                Is.EqualTo($"draw:{CardBehaviorMetadata.ChoiceEffectDrawActionCards}:self;recover:{CardBehaviorMetadata.ChoiceEffectRecoverExiledCard}:self"));
            Assert.That(entry.BehaviorParams, Is.EqualTo("drawCount:2"));
            Assert.That(entry.Description, Does.Contain("2"), "The authored drawCount and the sentence must agree.");
            // Declaration and prose must line up or ValidateChoiceOptionRows rejects the import.
            Assert.That(entry.ChoiceOptionTexts, Does.Contain("draw"));
            Assert.That(entry.ChoiceOptionTexts, Does.Contain("recover"));
        }

        [Test]
        public void PlagueAndTalismanShieldAuthorTheirBehaviorAndPostAction()
        {
            asset = LoadCsvAsset();
            var entries = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.ToDictionary(entry => entry.Id);

            var plague = entries[ApprovedCardCatalogFactory.AttackPlagueId];
            Assert.That(plague.EffectRef, Is.EqualTo(CardEffectRefs.AttackPlague));
            Assert.That(plague.ActionType, Is.EqualTo(CardEffectType.Attack));
            // The contagion radius is authored, not baked into the handler.
            // 반경 2 = 2026-08-20 #18(인접 → 2칸 내 최근접). 저작면은 cards.csv의 postActions 한 곳이다.
            Assert.That(plague.PostActions, Is.EqualTo($"{CardBehaviorMetadata.PostActionSpreadStatus}:2"));
            Assert.That(plague.Amount, Is.EqualTo(3));

            var shield = entries[ApprovedCardCatalogFactory.DefendTalismanShieldId];
            Assert.That(shield.EffectRef, Is.EqualTo(CardEffectRefs.DefendExileRandomNegate));
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
        public void CardChoiceOptionsCsvFeedsChoiceOptionUiTexts()
        {
            asset = LoadCsvAsset();
            var entry = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.Single(card => card.Id == "A03");

            Assert.That(entry.ChoiceOptions, Is.EqualTo("heal:heal.player:self;attack:attack.damage:enemy"));
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
        public void CardsCsvRejectsUnknownBehaviorMetadataTokens()
        {
            var csvText = File.ReadAllText(CsvPath)
                .Replace("ApplyImmobilize", "UnknownPostAction");
            asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            asset.SetRows(CardCatalogAsset.ParseCsvText(csvText));

            Assert.That(asset.ValidateRows(out var reason), Is.False);
            Assert.That(reason, Does.Contain("invalid behavior metadata token"));
        }

        [Test]
        public void ThirtyColumnSchemaCarriesTheNewColumnsToEntries()
        {
            // The two columns added for the 정화 track (docs/new-cards-plan.md §5-5) sit mid-row, so every
            // index behind them shifted. A mis-shifted index still compiles and still imports — it just reads
            // the wrong column — hence asserting the trailing columns, not only the new ones.
            asset = LoadCsvAsset();
            var entries = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.ToDictionary(entry => entry.Id);

            Assert.That(entries["M01"].PresentationRef.IllustrationId, Is.EqualTo("card_illust_M01"));
            Assert.That(entries["A10"].Rarity, Is.EqualTo(CardRarity.Rare));
            Assert.That(entries["A10"].AdditionalCost, Is.EqualTo(CardBehaviorMetadata.AdditionalCostExileSelectedHandCards));
            Assert.That(entries["A03"].ChoiceOptions, Is.EqualTo("heal:heal.player:self;attack:attack.damage:enemy"));

            // usableWhileStunned belongs to the two 정화 cards P2 shipped and to nothing else; behaviorParams
            // is claimed by S04 (threshold) and U02 (drawCount).
            Assert.That(
                entries.Values.Where(entry => entry.UsableWhileStunned).Select(entry => entry.Id),
                Is.EquivalentTo(new[]
                {
                    ApprovedCardCatalogFactory.UtilityCleanseDrawId,
                    ApprovedCardCatalogFactory.DefendHospitalizationId
                }));
            Assert.That(
                entries.Values.Where(entry => !string.IsNullOrEmpty(entry.BehaviorParams)).Select(entry => entry.Id),
                Is.EquivalentTo(new[]
                {
                    ApprovedCardCatalogFactory.ScoutBingoId,
                    ApprovedCardCatalogFactory.UtilityDrawOrRecoverId
                }));
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
        public void NewColumnsReachTheRuntimeCardDefinition()
        {
            var row = ParseSingleRow(
                "D06,입원,테스트,방어,self,1,0,,,5,,,,,,defend.block,,,,self,,,,,Approved,TRUE,TRUE,TRUE,card_illust_D06,Rare,TRUE,TRUE");

            asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            asset.SetRows(new[] { row });
            var card = asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.Single().ToCardDefinition("test");

            Assert.That(card.UsableWhileStunned, Is.True, "usableWhileStunned must survive all the way to CardDefinition.");
            Assert.That(card.Amount, Is.EqualTo(5), "shield must still be read from its (shifted) column.");
            Assert.That(card.ExhaustOnPlay, Is.True, "exhaustOnPlay must survive all the way to CardDefinition.");
            Assert.That(card.RetainOnTurnEnd, Is.True, "retainOnTurnEnd must survive all the way to CardDefinition.");
        }

        [Test]
        public void MalformedNewColumnsFailTheImport()
        {
            asset = ScriptableObject.CreateInstance<CardCatalogAsset>();

            asset.SetRows(new[] { ParseSingleRow(RowWith(behaviorParams: string.Empty, usableWhileStunned: "yes")) });
            Assert.That(asset.ValidateRows(out var reason), Is.False, "A non-boolean usableWhileStunned must not default silently.");
            Assert.That(reason, Does.Contain("usableWhileStunned"));

            asset.SetRows(new[] { ParseSingleRow(RowWith(behaviorParams: "threshold", usableWhileStunned: string.Empty)) });
            Assert.That(asset.ValidateRows(out reason), Is.False, "behaviorParams must be 키:정수 pairs.");
            Assert.That(reason, Does.Contain("behaviorParams"));

            // Well-formed but unknown to the behaviorId: this is the typo the allow-list exists to catch.
            asset.SetRows(new[] { ParseSingleRow(RowWith(behaviorParams: "treshold:3", usableWhileStunned: string.Empty)) });
            Assert.That(asset.ValidateRows(out reason), Is.False);
            Assert.That(reason, Does.Contain("treshold"));

            asset.SetRows(new[] { ParseSingleRow(RowWith(behaviorParams: string.Empty, usableWhileStunned: string.Empty, exhaustOnPlay: "maybe")) });
            Assert.That(asset.ValidateRows(out reason), Is.False, "A non-boolean exhaustOnPlay must not default silently.");
            Assert.That(reason, Does.Contain("exhaustOnPlay"));

            asset.SetRows(new[] { ParseSingleRow(RowWith(behaviorParams: string.Empty, usableWhileStunned: string.Empty, retainOnTurnEnd: "maybe")) });
            Assert.That(asset.ValidateRows(out reason), Is.False, "A non-boolean retainOnTurnEnd must not default silently.");
            Assert.That(reason, Does.Contain("retainOnTurnEnd"));
        }

        private static string RowWith(string behaviorParams, string usableWhileStunned, string exhaustOnPlay = "", string retainOnTurnEnd = "") =>
            "D00,방어,테스트,방어,self,1,0,,,4,,,,,,defend.block,,,,self,,," +
            $",{behaviorParams},Approved,TRUE,TRUE,{usableWhileStunned},card_illust_D00,Basic,{exhaustOnPlay},{retainOnTurnEnd}";

        private static CardCatalogCsvRow ParseSingleRow(string row)
        {
            var header = string.Join(",", (string[])typeof(CardCatalogAsset)
                .GetField("ExpectedHeader", BindingFlags.Static | BindingFlags.NonPublic)
                .GetValue(null));
            return CardCatalogAsset.ParseCsvText(header + "\n" + row).Single();
        }

        private static CardCatalogAsset LoadCsvAsset()
        {
            var csvText = File.ReadAllText(CsvPath);
            var parsedRows = CardCatalogAsset.ParseCsvText(csvText);
            var asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            asset.SetRows(parsedRows);
            if (File.Exists(ChoiceOptionsCsvPath))
            {
                asset.SetChoiceOptionRows(CardCatalogAsset.ParseChoiceOptionsCsvText(File.ReadAllText(ChoiceOptionsCsvPath)));
            }
            return asset;
        }
    }
}


