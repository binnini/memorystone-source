using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Codex;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode.Codex
{
    /// <summary>
    /// 도감 카드 도메인(<c>docs/codex-plan.md</c> P0.5). 출하 <c>cards.csv</c>를 실제로 읽어
    /// 카드 얼굴에 들어갈 스냅샷이 저작과 일치하는지, 특히 <b>저주 카드가 "이동"으로 뭉개지지
    /// 않는지</b> 확인한다 — 이미 한 번 밟은 함정이다.
    /// </summary>
    public sealed class CodexCardDomainTests
    {
        private static readonly Color Accent = new Color(0.941f, 0.725f, 0.231f, 1f);

        private static CardCatalogDefinition LoadShippingCatalog()
        {
            var asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            try
            {
                asset.SetRows(CardCatalogAsset.ParseCsvText(File.ReadAllText(CombatCsvPaths.CardsCsv)));
                return asset.ToCardCatalogDefinition(CombatConfig.Default);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Entries_CoverEveryCatalogVisibleCard()
        {
            var catalog = LoadShippingCatalog();
            var domain = new CodexCardDomain(catalog, Accent);

            var expected = catalog.Entries
                .Where(entry => entry.Status == CardCatalogStatus.Approved)
                .Select(entry => entry.Id)
                .ToList();
            CollectionAssert.AreEqual(expected, domain.Entries.Select(entry => entry.Id).ToList(),
                "도감은 승인된 카드를 저작 순서대로 싣는다 — visibleInCatalog는 덱·보상 노출 플래그이지 " +
                "도감 노출 플래그가 아니다.");
            Assert.That(domain.Entries.Count, Is.GreaterThan(0));
        }

        [Test]
        public void EveryEntry_CarriesACardVisual()
        {
            var domain = new CodexCardDomain(LoadShippingCatalog(), Accent);

            foreach (var entry in domain.Entries)
            {
                Assert.That(entry.CardVisual.HasValue, Is.True,
                    $"{entry.Id}: 카드는 썸네일이 아니라 카드 한 장으로 그려야 한다.");
            }
        }

        [Test]
        public void StatusCards_AreLabelledCurseNotMove()
        {
            var catalog = LoadShippingCatalog();
            var domain = new CodexCardDomain(catalog, Accent);

            var statusIds = catalog.Entries
                .Where(entry => entry.ActionType == CardEffectType.Status)
                .Select(entry => entry.Id)
                .ToList();
            Assert.That(statusIds, Is.Not.Empty, "출하 저작에 저주 카드가 있어야 이 테스트가 의미를 가진다.");

            foreach (var id in statusIds)
            {
                var entry = domain.Entries.Single(candidate => candidate.Id == id);

                // 🔴 CombatCardKind로 옮기면 Status는 Move로 뭉개진다. 칩·부제는 그 앞에서 갈려야 한다.
                Assert.That(entry.FilterChip, Is.EqualTo("저주"), $"{id}: 저주 카드에 '이동'이 붙었다.");
                Assert.That(entry.CardVisual.Value.IsStatusCard, Is.True,
                    $"{id}: 스냅샷이 상태 카드로 표시돼야 프레임이 저주 프레임으로 갈린다.");
            }
        }

        [Test]
        public void Snapshot_MirrorsAuthoredCostRangeAndIllustration()
        {
            var catalog = LoadShippingCatalog();
            var domain = new CodexCardDomain(catalog, Accent);

            foreach (var authored in catalog.Entries)
            {
                var snapshot = domain.Entries.Single(entry => entry.Id == authored.Id).CardVisual.Value;

                Assert.That(snapshot.Cost, Is.EqualTo(authored.Cost), $"{authored.Id}: 기력");
                Assert.That(snapshot.Range, Is.EqualTo(authored.Range), $"{authored.Id}: 사거리");
                Assert.That(snapshot.Name, Is.EqualTo(authored.DisplayName), $"{authored.Id}: 이름");
                Assert.That(snapshot.AreaRadius, Is.EqualTo(authored.AreaRadius), $"{authored.Id}: 범위 반경");
                Assert.That(snapshot.IllustrationId, Is.EqualTo(authored.PresentationRef.IllustrationId),
                    $"{authored.Id}: 일러 id가 어긋나면 카드가 빈 그림으로 뜬다.");
            }
        }

        [Test]
        public void Description_HasNoUnresolvedTokensLeft()
        {
            var catalog = LoadShippingCatalog();
            var domain = new CodexCardDomain(catalog, Accent);

            var authoredWithTokens = catalog.Entries
                .Count(entry => entry.Description != null && entry.Description.Contains("{"));
            Assert.That(authoredWithTokens, Is.GreaterThan(0),
                "저작에 토큰 쓰는 카드가 있어야 이 테스트가 의미를 가진다.");

            foreach (var entry in domain.Entries)
            {
                // 🔴 치환하지 않으면 카드에 "{Damage}"가 글자 그대로 뜬다(P0.5에서 실제로 밟았다).
                Assert.That(entry.CardVisual.Value.Description, Does.Not.Contain("{"),
                    $"{entry.Id}: 카드 설명에 치환되지 않은 토큰이 남았다.");
                Assert.That(entry.Description, Does.Not.Contain("{"),
                    $"{entry.Id}: 상세 패널 설명에 치환되지 않은 토큰이 남았다.");
            }
        }

        [Test]
        public void Description_IsNotKeywordDecoratedTwice()
        {
            var domain = new CodexCardDomain(LoadShippingCatalog(), Accent);

            foreach (var entry in domain.Entries)
            {
                // 카드 얼굴을 채우는 쪽이 그릴 때 한 번 장식한다. 스냅샷이 이미 장식돼 있으면 두 번 된다.
                Assert.That(entry.CardVisual.Value.Description, Does.Not.Contain("<link"),
                    $"{entry.Id}: 스냅샷 설명이 이미 장식돼 있다.");
            }
        }

        [Test]
        public void Snapshot_IsAlwaysUsable_BecauseCodexHasNoCombatState()
        {
            var domain = new CodexCardDomain(LoadShippingCatalog(), Accent);

            foreach (var entry in domain.Entries)
            {
                // '지금 낼 수 있는가'는 전투 상태가 정한다. 로비에서 회색으로 뜨면 거짓말이다.
                Assert.That(entry.CardVisual.Value.IsUsable, Is.True, $"{entry.Id}");
                Assert.That(entry.CardVisual.Value.IsDiscarded, Is.False, $"{entry.Id}");
            }
        }

        [Test]
        public void MetaChips_DoNotRepeatWhatTheCardFaceAlreadyShows()
        {
            var domain = new CodexCardDomain(LoadShippingCatalog(), Accent);

            foreach (var entry in domain.Entries)
            {
                foreach (var chip in entry.MetaChips)
                {
                    Assert.That(chip.Contains("기력"), Is.False, $"{entry.Id}: 기력은 카드가 이미 말한다.");
                    Assert.That(chip.Contains("사거리"), Is.False, $"{entry.Id}: 사거리는 카드가 이미 말한다.");
                }
            }
        }

        [Test]
        public void CardKindMapping_IsTotalForEveryAuthoredEffectType()
        {
            var catalog = LoadShippingCatalog();

            foreach (var authored in catalog.Entries)
            {
                var kind = CodexCardSnapshotFactory.ToCombatCardKind(authored.ActionType);

                if (authored.ActionType == CardEffectType.Status)
                {
                    // Status만 대응 kind가 없어 Move로 떨어진다 — 알려진 사실이라 여기서 못 박는다.
                    Assert.That(kind, Is.EqualTo(CombatCardKind.Move));
                    continue;
                }

                Assert.That(kind.ToString(), Is.EqualTo(authored.ActionType.ToString()),
                    $"{authored.Id}: {authored.ActionType}가 조용히 Move로 떨어지고 있다.");
            }
        }
    }
}
