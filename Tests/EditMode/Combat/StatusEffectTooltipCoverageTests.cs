#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Cards.Unity;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 몬스터 툴팁 HUD의 상태이상 표기 커버리지(2026-08-20 #6). "아이콘이나 텍스트가 설정 안 된"
    /// 상태이상이 화면에 빈칸·물음표로 뜨던 것을 게이트로 막는다.
    ///
    /// <para>🔑 <b>append 함정</b>: 아이콘 카탈로그·글리프 표는 kind가 추가될 때 함께 늘어나지 않는다.
    /// 실제로 스프라이트가 붙은 뒤에 append된 12종은 글리프가 통째로 기본값 <c>"!"</c>였고, 글리프는
    /// 스프라이트가 없을 때만 보이는 폴백이라 정확히 아트가 빠진 자리에서 <c>!</c>가 화면에 떴다.</para>
    /// </summary>
    public sealed class StatusEffectTooltipCoverageTests
    {
        private static IEnumerable<StatusEffectKind> AllKinds =>
            Enum.GetValues(typeof(StatusEffectKind)).Cast<StatusEffectKind>();

        [Test]
        public void EveryStatusKindHasItsOwnGlyphFallback()
        {
            var missing = AllKinds.Where(kind => StatusEffectIconStyle.Glyph(kind) == "!").ToList();
            Assert.That(missing, Is.Empty,
                $"글리프가 기본값 '!'인 상태이상: {string.Join(", ", missing)} — 스프라이트가 없을 때 화면에 그대로 뜬다.");
        }

        [Test]
        public void GlyphsAreUniqueSoTheLetterIsTheIdentity()
        {
            // 글리프는 색이 아니라 글자가 곧 식별자다 — 겹치면 두 상태가 같은 표식을 쓴다.
            var duplicates = AllKinds
                .GroupBy(StatusEffectIconStyle.Glyph)
                .Where(group => group.Count() > 1)
                .Select(group => $"'{group.Key}' = {string.Join("/", group)}")
                .ToList();
            Assert.That(duplicates, Is.Empty, $"글리프 충돌: {string.Join(", ", duplicates)}");
        }

        [Test]
        public void EveryStatusKindHasKeywordTextForTheTooltip()
        {
            CardKeywordRuntime.EnsureLoaded();
            Assert.That(CardKeywordCatalogProvider.Active, Is.Not.Null, "키워드 카탈로그를 못 읽었다.");

            var missing = AllKinds
                .Where(kind => !CardKeywordCatalogProvider.Active.TryGet(StatusEffectTooltipContent.KeywordFor(kind), out var def)
                               || string.IsNullOrWhiteSpace(def.Effect))
                .ToList();
            Assert.That(missing, Is.Empty,
                $"game_keywords.csv에 설명이 없는 상태이상: {string.Join(", ", missing)} — 툴팁 본문이 빈칸이 된다.");
        }

        /// <summary>
        /// 스프라이트 미저작은 <b>실패시키지 않는다</b> — 아트 발주가 답이지 코드가 아니다. 대신 어떤
        /// kind가 글리프 폴백으로 뜨고 있는지 로그로 남겨 발주 목록이 조용히 늘어나지 않게 한다.
        /// </summary>
        [Test]
        public void ReportsStatusKindsStillRenderingAsGlyphFallback()
        {
            var catalog = Resources.FindObjectsOfTypeAll<StatusEffectIconCatalog>().FirstOrDefault()
                          ?? UnityEditor.AssetDatabase.FindAssets("t:StatusEffectIconCatalog")
                              .Select(guid => UnityEditor.AssetDatabase.LoadAssetAtPath<StatusEffectIconCatalog>(
                                  UnityEditor.AssetDatabase.GUIDToAssetPath(guid)))
                              .FirstOrDefault(asset => asset != null);
            Assert.That(catalog, Is.Not.Null, "StatusEffectIconCatalog 에셋을 찾지 못했다.");

            var glyphOnly = AllKinds.Where(kind => catalog.GetSprite(kind) == null).ToList();
            if (glyphOnly.Count > 0)
            {
                Debug.Log($"[#6] 스프라이트 미저작(글리프 폴백으로 표시 중): {string.Join(", ", glyphOnly)}");
            }

            Assert.Pass();
        }
    }
}
#endif
