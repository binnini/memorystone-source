using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 표현 레지스트리(1단계 구조 리팩토링)의 <b>소비자</b>가 정말 카탈로그를 읽는지 문다. 네임플레이트 배지
    /// 정렬·부여 플로팅 문안·부여 SFX·아이콘 글리프는 원래 각자 private switch를 들고 있었고, 그 switch가
    /// 남아 있으면 CSV는 장식이 된다(정확히 P0.5 이전 status_effects.csv의 상태). 출하값을 핀하지 않고
    /// <b>출하에 없는 값</b>을 주입해 소비자가 그 값을 내는지만 본다.
    /// </summary>
    public sealed class StatusEffectPresentationRegistryConsumerTests
    {
        private StatusEffectCatalogDefinition savedCatalog;

        [SetUp]
        public void SetUp()
        {
            savedCatalog = StatusEffectCatalogProvider.Active;
            StatusEffectCatalogProvider.Active = null;
        }

        [TearDown]
        public void TearDown()
        {
            StatusEffectCatalogProvider.Active = savedCatalog;
        }

        private static void InjectPoison(string glyph, int badgeSortRank, string floatingTextTemplate, string applyAudioCueId)
        {
            StatusEffectCatalogProvider.Active = new StatusEffectCatalogDefinition(new[]
            {
                new StatusEffectDefinition(StatusEffectKind.Poison, "중독", "", 3, 2,
                        StatusEffectValueMode.DamagePerTurn, StatusEffectTiming.TurnStart,
                        StatusEffectStackPolicy.Add, 1, StatusEffectExpirePolicy.TurnStartAfterTick)
                    .WithPresentation(glyph, badgeSortRank, floatingTextTemplate, applyAudioCueId),
            });
        }

        [Test]
        public void NameplateBadgeSortRankReadsTheActiveCatalog()
        {
            var poison = MonsterNameplateBadge.Status(new ActiveEffect(EffectType.Duration, StatusEffectKind.Poison, "m1", 2, 3));
            var fallback = StatusEffectInfo.FallbackBadgeSortRank(StatusEffectKind.Poison);
            Assert.That(CombatActorMarkerPresenter.BadgeSortRankForTests(poison), Is.EqualTo(fallback), "카탈로그 없음 → 폴백.");

            InjectPoison("독", fallback + 40, "{name} {amount}", "effect.poison");
            Assert.That(CombatActorMarkerPresenter.BadgeSortRankForTests(poison), Is.EqualTo(fallback + 40),
                "배지 정렬이 CSV badgeSortRank가 아니라 옛 switch를 읽고 있다.");
        }

        [Test]
        public void FloatingStatusTextReadsTheActiveCatalogTemplate()
        {
            var applied = new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "player", appliedAmount: 3, sourceRef: "card.x", statusKind: StatusEffectKind.Poison);
            var trap = new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "player", appliedAmount: 3, sourceRef: "trap.x", statusKind: StatusEffectKind.Poison);
            Assert.That(EffectPresentationController.FormatStatusTextForTests(applied),
                Is.EqualTo(StatusEffectInfo.FloatingText(StatusEffectKind.Poison, 3, isTrap: false)), "카탈로그 없음 → 폴백 템플릿.");

            InjectPoison("독", 5, "{amount}×{name}|{name}!!", "effect.poison");
            Assert.That(EffectPresentationController.FormatStatusTextForTests(applied), Is.EqualTo("3×중독"),
                "부여 문안이 CSV floatingTextTemplate이 아니라 옛 switch를 읽고 있다.");
            Assert.That(EffectPresentationController.FormatStatusTextForTests(trap), Is.EqualTo(StatusEffectInfo.TrapFloatingTextPrefix + "중독!!"),
                "함정 발동 분기(IsTrapEvent)가 템플릿의 '|' 뒤 본문과 접두를 함께 내야 한다.");
        }

        [Test]
        public void ApplyAudioCueReadsTheActiveCatalog()
        {
            var applied = new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "player", statusKind: StatusEffectKind.Poison);
            Assert.That(CombatAudioPresenter.MapEffectToCueIds(applied),
                Is.EqualTo(new[] { StatusEffectInfo.FallbackApplyAudioCueId(StatusEffectKind.Poison) }), "카탈로그 없음 → 폴백 큐.");

            InjectPoison("독", 5, "{name} {amount}", "test.registry.cue");
            Assert.That(CombatAudioPresenter.MapEffectToCueIds(applied), Is.EqualTo(new[] { "test.registry.cue" }),
                "부여 SFX가 CSV applyAudioCueId가 아니라 옛 switch를 읽고 있다.");

            InjectPoison("독", 5, "{name} {amount}", "");
            Assert.That(CombatAudioPresenter.MapEffectToCueIds(applied), Is.Empty, "빈 큐 id는 무음이다(옛 switch의 default 없음과 같다).");
        }

        [Test]
        public void IconGlyphReadsTheActiveCatalog()
        {
            Assert.That(StatusEffectIconStyle.Glyph(StatusEffectKind.Poison), Is.EqualTo(StatusEffectInfo.FallbackGlyph(StatusEffectKind.Poison)));
            InjectPoison("毒", 5, "{name} {amount}", "effect.poison");
            Assert.That(StatusEffectIconStyle.Glyph(StatusEffectKind.Poison), Is.EqualTo("毒"),
                "글리프가 CSV glyph가 아니라 옛 switch를 읽고 있다.");
        }

        /// <summary>
        /// 큐 id는 Runtime 어셈블리가 Audio를 참조할 수 없어 CSV·폴백 모두 리터럴이다. 오타는 런타임에 조용히
        /// 무음이 되므로 상수 표(<see cref="AudioCueIds"/>)와의 조인을 여기서 문다 — 표시명↔키워드 조인과 같은 계약.
        /// </summary>
        [Category("ShippingData")]
        [Test]
        public void EveryAuthoredApplyAudioCueIdIsAKnownCueConstant()
        {
            var known = typeof(AudioCueIds)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue())
                .ToHashSet(StringComparer.Ordinal);
            var catalog = StatusEffectCatalogCsv.ConvertFile(CombatCsvPaths.StatusEffectsCsv);

            foreach (StatusEffectKind kind in Enum.GetValues(typeof(StatusEffectKind)))
            {
                Assert.That(catalog.TryGet(kind, out var definition), Is.True, $"{kind} is missing from status_effects.csv.");
                foreach (var cue in new[] { definition.ApplyAudioCueId, StatusEffectInfo.FallbackApplyAudioCueId(kind) })
                {
                    if (string.IsNullOrEmpty(cue))
                    {
                        continue; // 무음은 저작이다.
                    }

                    Assert.That(known, Does.Contain(cue), $"{kind}: applyAudioCueId '{cue}'는 AudioCueIds에 없는 큐라 런타임에 조용히 무음이 된다.");
                }
            }
        }
    }
}
