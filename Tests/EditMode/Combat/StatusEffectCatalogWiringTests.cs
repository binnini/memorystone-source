using System;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// Guards the status-effect value promotion (docs/new-cards-plan.md P0.5). `status_effects.csv` used to be
    /// authored but unread — the game ran on <see cref="StatusEffectInfo"/>'s hardcoded switch, and the two had
    /// silently drifted (중독 said 1, played as 3). The CSV is now the live source with the switch as fallback,
    /// so the two must agree: if they ever diverge again, that is a real behaviour change and this fails.
    /// </summary>
    public sealed class StatusEffectCatalogWiringTests
    {
        private StatusEffectCatalogDefinition savedCatalog;

        [SetUp]
        public void SetUp()
        {
            // The provider is process-wide and survives play-mode exit; isolate from whatever ran before.
            savedCatalog = StatusEffectCatalogProvider.Active;
            StatusEffectCatalogProvider.Active = null;
        }

        [TearDown]
        public void TearDown()
        {
            StatusEffectCatalogProvider.Active = savedCatalog;
        }

        [Category("ShippingData")]
        [Test]
        public void CsvDefaultAmountsMatchTheHardcodedFallback()
        {
            var catalog = StatusEffectCatalogCsv.ConvertFile(CombatCsvPaths.StatusEffectsCsv);

            foreach (StatusEffectKind kind in Enum.GetValues(typeof(StatusEffectKind)))
            {
                Assert.That(catalog.TryGet(kind, out var definition), Is.True, $"{kind} is missing from status_effects.csv.");
                Assert.That(
                    definition.DefaultAmount,
                    Is.EqualTo(StatusEffectInfo.FallbackDefaultAmount(kind)),
                    $"{kind}: status_effects.csv and the StatusEffectInfo fallback disagree. Changing a value here " +
                    "changes the game — update both together, deliberately.");
            }
        }

        /// <summary>
        /// Same contract as the default-amount guard, for the Korean name. Player-facing surfaces (HUD icon
        /// label, hover tooltip, monster info panel, floating text) used to carry private copies of this
        /// switch and had drifted — 강화 was missing from three of them. They now all read
        /// <see cref="StatusEffectInfo.DisplayName"/>, so this is the one place the CSV and the fallback can
        /// disagree, and it must not.
        /// </summary>
        [Category("ShippingData")]
        [Test]
        public void CsvDisplayNamesMatchTheHardcodedFallback()
        {
            var catalog = StatusEffectCatalogCsv.ConvertFile(CombatCsvPaths.StatusEffectsCsv);

            foreach (StatusEffectKind kind in Enum.GetValues(typeof(StatusEffectKind)))
            {
                Assert.That(catalog.TryGet(kind, out var definition), Is.True, $"{kind} is missing from status_effects.csv.");
                Assert.That(
                    definition.DisplayNameKo,
                    Is.EqualTo(StatusEffectInfo.FallbackDisplayName(kind)),
                    $"{kind}: status_effects.csv displayNameKo and the StatusEffectInfo fallback disagree. " +
                    "Renaming a status effect is player-visible — update both together, deliberately.");
                Assert.That(
                    StatusEffectInfo.FallbackDisplayName(kind),
                    Is.Not.EqualTo(kind.ToString()),
                    $"{kind} has no Korean name in the fallback switch and would leak the raw enum name to the player.");
            }
        }

        /// <summary>
        /// The display name doubles as the join key into game_keywords.csv (키워드), which is what the hover
        /// tooltip reads for 분류/효과. A rename that misses one of the two files silently empties the tooltip
        /// instead of failing, so pin the join here.
        /// </summary>
        [Category("ShippingData")]
        [Test]
        public void EveryStatusDisplayNameResolvesAGameKeyword()
        {
            var keywords = KeywordCatalogCsv.ConvertFile(CombatCsvPaths.GameKeywordsCsv);

            foreach (StatusEffectKind kind in Enum.GetValues(typeof(StatusEffectKind)))
            {
                var name = StatusEffectInfo.FallbackDisplayName(kind);
                Assert.That(
                    keywords.TryGet(name, out _),
                    Is.True,
                    $"{kind} ('{name}') has no matching 키워드 row in game_keywords.csv — the status tooltip would come up empty.");
            }
        }

        [Test]
        public void DisplayNamePrefersTheActiveCatalogOverTheFallback()
        {
            Assert.That(StatusEffectInfo.DisplayName(StatusEffectKind.Poison), Is.EqualTo("중독"), "No catalog assigned → fallback.");

            StatusEffectCatalogProvider.Active = new StatusEffectCatalogDefinition(new[]
            {
                new StatusEffectDefinition(StatusEffectKind.Poison, "맹독", "", 3, 2,
                    StatusEffectValueMode.DamagePerTurn, StatusEffectTiming.TurnStart,
                    StatusEffectStackPolicy.Add, 1, StatusEffectExpirePolicy.TurnStartAfterTick),
            });

            Assert.That(StatusEffectInfo.DisplayName(StatusEffectKind.Poison), Is.EqualTo("맹독"), "An authored catalog must win.");
            Assert.That(
                StatusEffectInfo.DisplayName(StatusEffectKind.Slow),
                Is.EqualTo("둔화"),
                "Kinds the catalog omits keep falling back rather than becoming empty.");
        }

        [Test]
        public void DefaultAmountPrefersTheActiveCatalogOverTheFallback()
        {
            Assert.That(StatusEffectInfo.DefaultAmount(StatusEffectKind.Poison), Is.EqualTo(3), "No catalog assigned → fallback.");

            StatusEffectCatalogProvider.Active = new StatusEffectCatalogDefinition(new[]
            {
                new StatusEffectDefinition(StatusEffectKind.Poison, "중독", "", 7, 2,
                    StatusEffectValueMode.DamagePerTurn, StatusEffectTiming.TurnStart,
                    StatusEffectStackPolicy.Add, 1, StatusEffectExpirePolicy.TurnStartAfterTick),
            });

            Assert.That(
                StatusEffectInfo.DefaultAmount(StatusEffectKind.Poison),
                Is.EqualTo(7),
                "An authored catalog must win — otherwise the CSV is decorative again.");
            Assert.That(
                StatusEffectInfo.DefaultAmount(StatusEffectKind.Slow),
                Is.EqualTo(StatusEffectInfo.FallbackDefaultAmount(StatusEffectKind.Slow)),
                "Kinds the catalog omits keep falling back rather than silently becoming 0.");
        }

        /// <summary>
        /// P1.5(D-6)로 동작 컬럼 4개가 실소비로 바뀌면서 폴백 switch도 4개 늘었다. 폴백은 카탈로그가
        /// 없는 표면(EditMode 픽스처·pure-C# 도구)이 쓰는 값이므로, CSV와 갈라지면 <b>같은 게임이
        /// 두 가지로 동작한다</b> — 정확히 P1.5 이전에 stackPolicy에서 벌어졌던 일이다.
        /// 이름·기본 수치와 같은 계약으로 잠근다.
        /// </summary>
        [Category("ShippingData")]
        [Test]
        public void CsvBehaviourColumnsMatchTheHardcodedFallbacks()
        {
            var catalog = StatusEffectCatalogCsv.ConvertFile(CombatCsvPaths.StatusEffectsCsv);

            foreach (StatusEffectKind kind in Enum.GetValues(typeof(StatusEffectKind)))
            {
                Assert.That(catalog.TryGet(kind, out var definition), Is.True, $"{kind} is missing from status_effects.csv.");
                Assert.That(
                    definition.ValueMode,
                    Is.EqualTo(StatusEffectInfo.FallbackValueMode(kind)),
                    $"{kind}: valueMode가 폴백과 다르다 — 카탈로그 없는 표면이 다른 축으로 합산된다.");
                Assert.That(
                    definition.StackPolicy,
                    Is.EqualTo(StatusEffectInfo.FallbackStackPolicy(kind)),
                    $"{kind}: stackPolicy가 폴백과 다르다 — 재중첩 수치가 표면마다 달라진다.");
                Assert.That(
                    definition.ExpirePolicy,
                    Is.EqualTo(StatusEffectInfo.FallbackExpirePolicy(kind)),
                    $"{kind}: expirePolicy가 폴백과 다르다 — 적용 턴 유예가 표면마다 달라진다.");
                Assert.That(
                    definition.Timing,
                    Is.EqualTo(StatusEffectInfo.FallbackTiming(kind)),
                    $"{kind}: timing 서술이 폴백과 다르다.");
            }
        }

        /// <summary>
        /// 표현 레지스트리(1단계 구조 리팩토링): 글리프·배지 정렬 순위·플로팅 문안 템플릿·부여 SFX 큐 id는
        /// 원래 소비자 4곳의 private switch였다. 이제 CSV가 정본이고 <see cref="StatusEffectInfo"/>의 폴백은
        /// 원 switch를 옮긴 것이라 둘이 갈라지면 카탈로그 없는 표면(EditMode 픽스처)과 출하가 다른 화면을
        /// 낸다 — 이름·기본 수치·동작 컬럼과 같은 계약으로 잠근다.
        /// </summary>
        [Category("ShippingData")]
        [Test]
        public void CsvPresentationColumnsMatchTheHardcodedFallbacks()
        {
            var catalog = StatusEffectCatalogCsv.ConvertFile(CombatCsvPaths.StatusEffectsCsv);

            foreach (StatusEffectKind kind in Enum.GetValues(typeof(StatusEffectKind)))
            {
                Assert.That(catalog.TryGet(kind, out var definition), Is.True, $"{kind} is missing from status_effects.csv.");
                Assert.That(definition.Glyph, Is.EqualTo(StatusEffectInfo.FallbackGlyph(kind)),
                    $"{kind}: glyph가 폴백과 다르다 — 스프라이트 없는 칸의 글자가 표면마다 달라진다.");
                Assert.That(definition.BadgeSortRank, Is.EqualTo(StatusEffectInfo.FallbackBadgeSortRank(kind)),
                    $"{kind}: badgeSortRank가 폴백과 다르다 — 네임플레이트 배지 순서가 표면마다 달라진다.");
                Assert.That(definition.FloatingTextTemplate, Is.EqualTo(StatusEffectInfo.FallbackFloatingTextTemplate(kind)),
                    $"{kind}: floatingTextTemplate이 폴백과 다르다 — 부여 문안이 표면마다 달라진다.");
                Assert.That(definition.ApplyAudioCueId, Is.EqualTo(StatusEffectInfo.FallbackApplyAudioCueId(kind)),
                    $"{kind}: applyAudioCueId가 폴백과 다르다 — 부여 SFX가 표면마다 달라진다(빈 값=무음).");
            }
        }

        [Test]
        public void PresentationAccessorsPreferTheActiveCatalogOverTheFallback()
        {
            var fallbackRank = StatusEffectInfo.FallbackBadgeSortRank(StatusEffectKind.Poison);
            Assert.That(StatusEffectInfo.BadgeSortRank(StatusEffectKind.Poison), Is.EqualTo(fallbackRank), "No catalog assigned → fallback.");

            StatusEffectCatalogProvider.Active = new StatusEffectCatalogDefinition(new[]
            {
                new StatusEffectDefinition(StatusEffectKind.Poison, "중독", "", 3, 2,
                        StatusEffectValueMode.DamagePerTurn, StatusEffectTiming.TurnStart,
                        StatusEffectStackPolicy.Add, 1, StatusEffectExpirePolicy.TurnStartAfterTick)
                    .WithPresentation("毒", fallbackRank + 40, "{name}!{amount}", "test.cue"),
            });

            Assert.That(StatusEffectInfo.Glyph(StatusEffectKind.Poison), Is.EqualTo("毒"));
            Assert.That(StatusEffectInfo.BadgeSortRank(StatusEffectKind.Poison), Is.EqualTo(fallbackRank + 40));
            Assert.That(StatusEffectInfo.FloatingText(StatusEffectKind.Poison, 3, isTrap: false), Is.EqualTo("중독!3"));
            Assert.That(StatusEffectInfo.ApplyAudioCueId(StatusEffectKind.Poison), Is.EqualTo("test.cue"));

            Assert.That(StatusEffectInfo.Glyph(StatusEffectKind.Slow), Is.EqualTo(StatusEffectInfo.FallbackGlyph(StatusEffectKind.Slow)),
                "Kinds the catalog omits keep falling back rather than becoming '!'.");
            Assert.That(StatusEffectInfo.ApplyAudioCueId(StatusEffectKind.Slow), Is.EqualTo(StatusEffectInfo.FallbackApplyAudioCueId(StatusEffectKind.Slow)),
                "Kinds the catalog omits keep falling back rather than going silent.");
        }

        /// <summary>
        /// 컬럼이 <b>헤더에 없는</b> CSV(옛 10컬럼·테스트 픽스처)는 폴백 값을 그대로 받는다 — 0/빈 문자열로
        /// 떨어지면 「위치 인자 재조립이 새 컬럼을 0으로 떨어뜨린」 사고의 CSV판이 된다. 반면 헤더에 있는
        /// 빈 값은 저작(무음·표시명만)이다.
        /// </summary>
        [Test]
        public void MissingPresentationColumnsFallBackWhileEmptyAuthoredValuesAreKept()
        {
            const string legacy =
                "effectKind,displayNameKo,descriptionKo,defaultAmount,defaultDurationTurns,valueMode,timing,stackPolicy,maxStacks,expirePolicy\n" +
                "Poison,중독,,3,2,DamagePerTurn,TurnStart,Add,1,TurnStartAfterTick\n";
            var legacyCatalog = StatusEffectCatalogCsv.ConvertText(legacy, "legacy.csv");
            Assert.That(legacyCatalog.TryGet(StatusEffectKind.Poison, out var legacyPoison), Is.True);
            Assert.That(legacyPoison.Glyph, Is.EqualTo(StatusEffectInfo.FallbackGlyph(StatusEffectKind.Poison)));
            Assert.That(legacyPoison.BadgeSortRank, Is.EqualTo(StatusEffectInfo.FallbackBadgeSortRank(StatusEffectKind.Poison)));
            Assert.That(legacyPoison.FloatingTextTemplate, Is.EqualTo(StatusEffectInfo.FallbackFloatingTextTemplate(StatusEffectKind.Poison)));
            Assert.That(legacyPoison.ApplyAudioCueId, Is.EqualTo(StatusEffectInfo.FallbackApplyAudioCueId(StatusEffectKind.Poison)));

            const string authoredEmpty =
                "effectKind,displayNameKo,descriptionKo,defaultAmount,defaultDurationTurns,valueMode,timing,stackPolicy,maxStacks,expirePolicy,glyph,badgeSortRank,floatingTextTemplate,applyAudioCueId\n" +
                "Poison,중독,,3,2,DamagePerTurn,TurnStart,Add,1,TurnStartAfterTick,독,5,,\n";
            var authoredCatalog = StatusEffectCatalogCsv.ConvertText(authoredEmpty, "authored.csv");
            Assert.That(authoredCatalog.TryGet(StatusEffectKind.Poison, out var authoredPoison), Is.True);
            Assert.That(authoredPoison.FloatingTextTemplate, Is.Empty, "헤더에 있는 빈 템플릿은 「표시명만」이라는 저작이다.");
            Assert.That(authoredPoison.ApplyAudioCueId, Is.Empty, "헤더에 있는 빈 큐 id는 「무음」이라는 저작이다.");

            const string emptyGlyph =
                "effectKind,displayNameKo,descriptionKo,defaultAmount,defaultDurationTurns,valueMode,timing,stackPolicy,maxStacks,expirePolicy,glyph\n" +
                "Poison,중독,,3,2,DamagePerTurn,TurnStart,Add,1,TurnStartAfterTick,\n";
            Assert.That(() => StatusEffectCatalogCsv.ConvertText(emptyGlyph, "glyph.csv"), Throws.ArgumentException,
                "빈 글리프는 저작이 아니라 누락이다 — 스프라이트 없는 칸이 빈칸이 된다.");
        }

        /// <summary>템플릿 문법의 계약: 토큰 치환·빈 값=표시명·<c>|</c> 함정 분기·함정 접두. 출하값을 핀하지 않는다.</summary>
        [Test]
        public void FloatingTextTemplateGrammar()
        {
            Assert.That(StatusEffectInfo.FormatFloatingText("{name} {amount}턴", "속박", 2, isTrap: false), Is.EqualTo("속박 2턴"));
            Assert.That(StatusEffectInfo.FormatFloatingText("{name} {amount}턴", "속박", 2, isTrap: true),
                Is.EqualTo(StatusEffectInfo.TrapFloatingTextPrefix + "속박 2턴"), "나누지 않은 템플릿은 함정 발동 시 접두만 붙는다.");
            Assert.That(StatusEffectInfo.FormatFloatingText("", "쇠약", 9, isTrap: false), Is.EqualTo("쇠약"), "빈 템플릿 = 표시명.");
            Assert.That(StatusEffectInfo.FormatFloatingText("{name} 부여|{name}", "반사", 50, isTrap: false), Is.EqualTo("반사 부여"));
            Assert.That(StatusEffectInfo.FormatFloatingText("{name} 부여|{name}", "반사", 50, isTrap: true),
                Is.EqualTo(StatusEffectInfo.TrapFloatingTextPrefix + "반사"), "'|' 뒤가 함정 발동 본문이다.");
            Assert.That(StatusEffectInfo.FormatFloatingText("{name} +{amount}", "민첩", 2, isTrap: false), Is.EqualTo("민첩 +2"));
        }

        /// <summary>
        /// U3 전수 게이트(1단계 구조 리팩토링): 새 <see cref="StatusEffectKind"/>를 append하고 CSV에 안 넣으면
        /// <b>여기</b>가 이름을 불러 빨개진다 — 옛날처럼 8파일의 switch가 default로 조용히 빠지는 대신.
        /// 한 kind = CSV 한 행 + 표현 4속성 완결이 계약이다. 「default로 빠졌다」를 값으로 판별할 수 있는 축은
        /// 글리프(<c>!</c>)뿐이고, 정렬 7·빈 템플릿·빈 큐 id는 정당한 저작이라 그 셋은 <b>CSV에 컬럼이 있고
        /// 행이 있다</b>로만 문다(CSV↔폴백 일치는 별도 게이트가 문다).
        /// </summary>
        [Category("ShippingData")]
        [Test]
        public void EveryKindIsFullyRegisteredInThePresentationRegistry()
        {
            var table = CsvTable.Parse(System.IO.File.ReadAllText(CombatCsvPaths.StatusEffectsCsv), "status_effects.csv");
            foreach (var column in new[] { "glyph", "badgeSortRank", "floatingTextTemplate", "applyAudioCueId" })
            {
                Assert.That(table.Headers, Does.Contain(column), $"status_effects.csv에 표현 컬럼 '{column}'이 없다 — 전부 폴백으로 떨어져 CSV가 장식이 된다.");
            }

            var catalog = StatusEffectCatalogCsv.ConvertFile(CombatCsvPaths.StatusEffectsCsv);
            var problems = new System.Collections.Generic.List<string>();
            foreach (StatusEffectKind kind in Enum.GetValues(typeof(StatusEffectKind)))
            {
                if (!catalog.TryGet(kind, out var definition))
                {
                    problems.Add($"{kind}: status_effects.csv에 행이 없다(표현 4속성 전부 폴백으로 떨어진다).");
                    continue;
                }

                if (definition.Glyph == "!" || StatusEffectInfo.FallbackGlyph(kind) == "!")
                {
                    problems.Add($"{kind}: 글리프가 기본값 '!'이다(CSV '{definition.Glyph}', 폴백 '{StatusEffectInfo.FallbackGlyph(kind)}').");
                }
            }

            var duplicateGlyphs = catalog.Entries
                .GroupBy(entry => entry.Glyph, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => $"글리프 '{group.Key}' 충돌: {string.Join("/", group.Select(entry => entry.Kind))}");
            problems.AddRange(duplicateGlyphs);

            Assert.That(problems, Is.Empty,
                "표현 레지스트리에 등록되지 않은 상태이상:\n  " + string.Join("\n  ", problems)
                + "\nstatus_effects.csv에 행을 추가하고 StatusEffectInfo.Fallback*에 같은 값을 넣어라.");
        }

        [Category("ShippingData")]
        [Test]
        public void EveryKindIsAuthoredWithAMatchingName()
        {
            var catalog = StatusEffectCatalogCsv.ConvertFile(CombatCsvPaths.StatusEffectsCsv);
            var authored = catalog.Entries.Select(entry => entry.Kind).ToList();

            Assert.That(authored, Is.Unique);
            Assert.That(
                authored,
                Is.EquivalentTo(Enum.GetValues(typeof(StatusEffectKind)).Cast<StatusEffectKind>()),
                "status_effects.csv must cover exactly the StatusEffectKind enum.");
        }
    }
}
