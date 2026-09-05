#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Cards.Unity;
using SeoulPlayup.Codex;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 유물·소모품 아이콘 배선 게이트(인계문 P1·P2). 지키는 것은 셋이다.
    /// <list type="number">
    /// <item>저작된 <c>iconId</c>가 <b>전부</b> 스프라이트로 해소된다 — 39장이 화면에 실제로 붙는다.</item>
    /// <item>해소는 <b>한 곳</b>(<see cref="RuntimeUiAssetCatalog"/>)에서만 일어난다. 세 소비처가
    ///   각자 로드하면 셋이 갈라지고, 에디터 전용 폴백은 빌드에서 통째로 null이 된다(cs:1178 선례).</item>
    /// <item>🔴 <b>등급이 어떤 UI 문자열에도 새지 않는다</b>(DEC-2026-08-31-01 Q1). 등급이 하는 일은
    ///   잡화점 기준가를 고르는 것뿐이고, 플레이어는 그 축이 있다는 것조차 몰라야 한다.</item>
    /// </list>
    /// </summary>
    public sealed class RelicConsumableIconWiringTests
    {
        private static readonly Color Accent = Color.white;

        /// <summary>
        /// 등급이 새는지 보는 바늘. enum 이름과 잡화점이 <b>카드</b>에 쓰는 한글 등급어를 함께 본다 —
        /// 카드는 등급을 드러내도 되지만(<c>FormatShopRarityLabel</c>) 유물·소모품은 안 된다.
        /// </summary>
        private static readonly string[] RarityNeedles =
            Enum.GetNames(typeof(CardRarity))
                .Concat(new[] { "희귀", "에픽", "전설", "등급" })
                .ToArray();

        // ── 1·2. 아이콘 해소 ─────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void EveryAuthoredRelicIconIdResolvesThroughTheSharedCatalog()
        {
            AssertEveryIconIdResolves(
                CodexShippingDomains.LoadRelicCatalog().Entries.Select(entry => (entry.Id, entry.IconId)),
                "유물");
        }

        [Test]
        [Category("ShippingData")]
        public void EveryAuthoredConsumableIconIdResolvesThroughTheSharedCatalog()
        {
            AssertEveryIconIdResolves(
                CodexShippingDomains.LoadConsumableCatalog().Entries.Select(entry => (entry.Id, entry.IconId)),
                "소모품");
        }

        [Test]
        public void MissingIconIdResolvesToNullSoConsumersKeepTheirCurrentFallback()
        {
            // 아트가 없는 항목이 생겨도 화면이 깨지지 않아야 한다는 것이 P1의 계약이다 —
            // 해소기는 예외를 던지지 않고 null을 주고, 소비처가 각자 폴백(칩 2글자·복주머니·이름 전체)한다.
            var catalog = LoadIconCatalog();
            Assert.That(catalog.ResolveItemIcon(null), Is.Null);
            Assert.That(catalog.ResolveItemIcon(string.Empty), Is.Null);
            Assert.That(catalog.ResolveItemIcon("   "), Is.Null);
            Assert.That(catalog.ResolveItemIcon("relic_icon_저작되지-않은-것"), Is.Null);
        }

        [Test]
        public void CodexFallsBackToTheNameLayerWhenNoIconCatalogIsInjected()
        {
            // 로비 배선이 빠져도 도감이 빈 칸을 내놓지 않는다(2층 = 이름 전체).
            var domain = new CodexRelicDomain(CodexShippingDomains.LoadRelicCatalog(), Accent, icons: null);
            Assert.That(domain.Entries, Is.Not.Empty);
            Assert.That(
                domain.Entries.All(entry => entry.Thumbnail.Layer == CodexThumbnailLayer.ProceduralLabel),
                Is.True,
                "해소기를 안 넘겼는데 전용 아트 층이 떴다 — 도메인이 카탈로그를 몰래 따로 로드하고 있다.");
        }

        [Test]
        [Category("ShippingData")]
        public void CodexShowsDedicatedArtForEveryRelicAndConsumableWhenTheCatalogIsInjected()
        {
            var icons = LoadIconCatalog();

            var relics = new CodexRelicDomain(CodexShippingDomains.LoadRelicCatalog(), Accent, icons);
            AssertNoFallbackThumbnails(relics.Entries, "유물");

            var consumables = new CodexConsumableItemDomain(CodexShippingDomains.LoadConsumableCatalog(), Accent, icons);
            AssertNoFallbackThumbnails(consumables.Entries, "소모품");
        }

        // ── 3. 등급 비노출 ───────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void RelicRarityNeverLeaksIntoAnyCodexString()
        {
            var domain = new CodexRelicDomain(CodexShippingDomains.LoadRelicCatalog(), Accent, LoadIconCatalog());
            foreach (var entry in domain.Entries)
            {
                AssertNoRarityLeak(CodexEntryStrings(entry), $"유물 도감 «{entry.DisplayName}»");
            }
        }

        [Test]
        [Category("ShippingData")]
        public void ConsumableRarityNeverLeaksIntoAnyCodexString()
        {
            var domain = new CodexConsumableItemDomain(CodexShippingDomains.LoadConsumableCatalog(), Accent, LoadIconCatalog());
            foreach (var entry in domain.Entries)
            {
                AssertNoRarityLeak(CodexEntryStrings(entry), $"소모품 도감 «{entry.DisplayName}»");
            }
        }

        [Test]
        [Category("ShippingData")]
        public void RelicAndConsumableShopTilesCarryAnIconIdAndNoRarityString()
        {
            // 잡화점이 유물·소모품 칸에 짓는 문안은 카탈로그의 표시명·설명뿐이어야 한다. 등급은
            // 값(가격)으로만 드러난다 — 그것이 priceDelta로 등급을 흐리는 이유다.
            foreach (var definition in CodexShippingDomains.LoadRelicCatalog().Entries)
            {
                var slot = new ShopOfferSlotModel(
                    ShopItemKind.Relic, definition.Id, definition.DisplayName, definition.Description, 100, definition.IconId);
                Assert.That(slot.IconId, Is.EqualTo(definition.IconId), $"«{definition.DisplayName}» 타일이 iconId를 잃었다.");
                AssertNoRarityLeak(new[] { slot.Title, slot.Detail }, $"잡화점 유물 타일 «{definition.DisplayName}»");
            }

            foreach (var definition in CodexShippingDomains.LoadConsumableCatalog().Entries)
            {
                var slot = new ShopOfferSlotModel(
                    ShopItemKind.Item, definition.Id, definition.DisplayName, definition.Description, 100, definition.IconId);
                Assert.That(slot.IconId, Is.EqualTo(definition.IconId), $"«{definition.DisplayName}» 타일이 iconId를 잃었다.");
                AssertNoRarityLeak(new[] { slot.Title, slot.Detail }, $"잡화점 소모품 타일 «{definition.DisplayName}»");
            }
        }

        [Test]
        [Category("ShippingData")]
        public void RelicSidebarChipTextNeverCarriesRarity()
        {
            // 칩은 아이콘 + 호버 detail 줄이 전부다. detail은 EffectSummary에서 오므로 그것을 본다.
            foreach (var definition in CodexShippingDomains.LoadRelicCatalog().Entries)
            {
                var state = definition.ToState();
                Assert.That(state.IconId, Is.EqualTo(definition.IconId),
                    $"«{definition.DisplayName}» 런타임 상태가 iconId를 못 받았다 — 칩이 2글자 폴백에 갇힌다.");
                AssertNoRarityLeak(
                    new[] { state.DisplayName, state.Description, state.EffectSummary },
                    $"사이드바 칩 «{definition.DisplayName}»");
            }
        }

        // ── 헬퍼 ─────────────────────────────────────────────────────────

        private static RuntimeUiAssetCatalog LoadIconCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<RuntimeUiAssetCatalog>(RuntimeUiAssetCatalog.AssetPath);
            Assert.That(catalog, Is.Not.Null, $"아이콘 카탈로그가 사라졌다 — {RuntimeUiAssetCatalog.AssetPath}");
            return catalog;
        }

        private static void AssertEveryIconIdResolves(IEnumerable<(string Id, string IconId)> rows, string label)
        {
            var catalog = LoadIconCatalog();
            var unresolved = new List<string>();
            var counted = 0;

            foreach (var (id, iconId) in rows)
            {
                counted++;
                Assert.That(iconId, Is.Not.Null.And.Not.Empty, $"{label} «{id}»에 iconId가 없다 — CSV 저작이 빠졌다.");
                if (catalog.ResolveItemIcon(iconId) == null)
                {
                    unresolved.Add($"{id} → {iconId}");
                }
            }

            Assert.That(counted, Is.GreaterThan(0), $"{label} 저작을 하나도 못 읽었다.");
            Assert.That(
                unresolved,
                Is.Empty,
                $"{label} 아이콘이 해소되지 않는다. PNG를 넣고 " +
                "'Seoul Playup/UI/Bake Relic & Consumable Icons'를 다시 돌릴 것:\n  " + string.Join("\n  ", unresolved));
        }

        private static void AssertNoFallbackThumbnails(IReadOnlyList<CodexEntry> entries, string label)
        {
            var fallbacks = entries.Where(entry => entry.Thumbnail.IsFallback).Select(entry => entry.DisplayName).ToArray();
            Assert.That(
                fallbacks,
                Is.Empty,
                $"{label} 도감이 폴백 썸네일로 떴다(아트 미반입 또는 iconId 불일치): {string.Join(", ", fallbacks)}");
        }

        private static IEnumerable<string> CodexEntryStrings(CodexEntry entry)
        {
            yield return entry.DisplayName;
            yield return entry.Subtitle;
            yield return entry.Description;
            yield return entry.FilterChip;

            foreach (var chip in entry.MetaChips ?? Array.Empty<string>())
            {
                yield return chip;
            }

            foreach (var row in entry.DetailRows ?? Array.Empty<CodexDetailRow>())
            {
                yield return row.Label;
                yield return row.Value;
            }

            // debugRows는 디버그 뷰 전용이라 플레이어에게 안 뜬다 — 일부러 보지 않는다.
        }

        private static void AssertNoRarityLeak(IEnumerable<string> strings, string where)
        {
            foreach (var value in strings)
            {
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                foreach (var needle in RarityNeedles)
                {
                    Assert.That(
                        value.IndexOf(needle, StringComparison.OrdinalIgnoreCase),
                        Is.LessThan(0),
                        $"{where}의 문안에 등급이 샜다(«{needle}» in «{value}»). " +
                        "DEC-2026-08-31-01 Q1 — 등급은 어떤 UI에도 노출하지 않는다.");
                }
            }
        }
    }
}
#endif
