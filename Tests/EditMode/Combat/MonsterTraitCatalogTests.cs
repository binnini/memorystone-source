using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 특성 저작 표(monster_traits.csv)의 게이트. 이 트랙이 존재하는 이유가 D2였다 — 어휘 행이 없어
    /// 「담력 시험」 설명이 <b>조용히 생략</b>되고 있었고, 조회 실패가 예외가 아니라 침묵이라
    /// 아무도 몰랐다. 침묵하는 실패는 게이트로만 잡힌다.
    /// </summary>
    public sealed class MonsterTraitCatalogTests
    {
        private const string Header =
            "traitId,displayNameKo,keywordRef,glyph,badgeColorHex,badgeSortRank,badgeVisibility,reachKind,announceRef,designerNote";

        private static MonsterTraitCatalogDefinition LoadShipping()
            => MonsterTraitCatalogCsv.ConvertFile(CombatCsvPaths.MonsterTraitsCsv);

        private static KeywordCatalogDefinition LoadKeywords()
            => KeywordCatalogCsv.ConvertFile(CombatCsvPaths.GameKeywordsCsv);

        /// <summary>출하 몬스터 카탈로그 — 기존 스위트(MonsterCursePoolTests)와 <b>같은 조립 경로</b>를 쓴다.</summary>
        private static IReadOnlyList<MonsterCatalogEntry> ShippingMonsters()
        {
            AttackShapeLibrary.Initialize(AttackShapeCatalogCsv.ConvertText(
                File.ReadAllText(CombatCsvPaths.AttackShapesCsv, Encoding.UTF8)));
            return MonsterCatalogCsvConverter.Convert(new MonsterCatalogCsvSource(
                ReadMonsterCsv("monster_catalog.csv"),
                ReadMonsterCsv("monster_attack_patterns.csv"),
                ReadMonsterCsv("monster_pattern_bindings.csv"),
                File.ReadAllText(Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_vfx_cues.csv"), Encoding.UTF8),
                File.ReadAllText(Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_sound_cues.csv"), Encoding.UTF8)))
                .MonsterCatalog.Entries;
        }

        private static string ReadMonsterCsv(string fileName)
            => File.ReadAllText(Path.Combine(CombatCsvPaths.MonsterDirectory, fileName), Encoding.UTF8);

        // ── 파서 계약 ────────────────────────────────────────────────────────────

        [Test]
        public void ParserReadsEveryColumn()
        {
            var catalog = MonsterTraitCatalogCsv.ConvertText(
                Header + "\n"
                + "auraSeal,홀림,홀림,홀,7A4DA8,12,always,auraRadius,trait.aura.seal,반경 안에서만 잠근다, 쉼표 포함");

            Assert.That(catalog.TryGet("auraSeal", out var entry), Is.True);
            Assert.That(entry.DisplayName, Is.EqualTo("홀림"));
            Assert.That(entry.KeywordRef, Is.EqualTo("홀림"));
            Assert.That(entry.Glyph, Is.EqualTo("홀"));
            Assert.That(entry.BadgeColorHex, Is.EqualTo("7A4DA8"));
            Assert.That(entry.BadgeSortRank, Is.EqualTo(12));
            Assert.That(entry.Visibility, Is.EqualTo(MonsterTraitBadgeVisibility.Always));
            Assert.That(entry.Reach, Is.EqualTo(MonsterTraitReachKind.AuraRadius));
            Assert.That(entry.AnnounceRef, Is.EqualTo("trait.aura.seal"));
            Assert.That(
                entry.DesignerNote,
                Is.EqualTo("반경 안에서만 잠근다, 쉼표 포함"),
                "designerNote가 마지막 컬럼이라 남은 쉼표를 통째로 흡수해야 한다.");
        }

        [Test]
        public void ParserRejectsUnknownEnumsInsteadOfFallingBackSilently()
        {
            // 오타가 기본값으로 내려앉으면 「저작은 했는데 아무 일도 안 일어나는」 죽은 컬럼이 된다.
            Assert.That(
                () => MonsterTraitCatalogCsv.ConvertText(
                    Header + "\nsturdy,견고,견고,견,3D618C,8,always,auraRadus,trait.sturdy.kept,오타"),
                Throws.ArgumentException.With.Message.Contains("reachKind"));

            Assert.That(
                () => MonsterTraitCatalogCsv.ConvertText(
                    Header + "\nsturdy,견고,견고,견,3D618C,8,alwyas,none,trait.sturdy.kept,오타"),
                Throws.ArgumentException.With.Message.Contains("badgeVisibility"));

            Assert.That(
                () => MonsterTraitCatalogCsv.ConvertText(
                    Header + "\nsturdy,견고,견고,견,ZZZZZZ,8,always,none,trait.sturdy.kept,오타"),
                Throws.ArgumentException.With.Message.Contains("badgeColorHex"));
        }

        [Test]
        public void ParserRejectsDuplicateTraitIdAndSharedKeywordRow()
        {
            Assert.That(
                () => MonsterTraitCatalogCsv.ConvertText(
                    Header
                    + "\nsturdy,견고,견고,견,3D618C,8,always,none,,\n"
                    + "sturdy,견고2,맷집,견,3D618C,8,always,none,,"),
                Throws.ArgumentException.With.Message.Contains("duplicates traitId"));

            // 두 특성이 한 설명문 행을 공유하면 한쪽을 고칠 때 다른 쪽이 조용히 따라 바뀐다.
            Assert.That(
                () => MonsterTraitCatalogCsv.ConvertText(
                    Header
                    + "\nsturdy,견고,견고,견,3D618C,8,always,none,,\n"
                    + "toughness,맷집,견고,맷,B8AD3D,9,always,none,,"),
                Throws.ArgumentException.With.Message.Contains("reuses keywordRef"));
        }

        [Test]
        public void KeywordRefValidationNamesTheMissingRow()
        {
            var traits = MonsterTraitCatalogCsv.ConvertText(
                Header + "\nstrengthDistance,담력 시험,담력 시험,담,C46A17,10,always,distanceStrength,,");
            var keywords = new KeywordCatalogDefinition(new[]
            {
                new KeywordDefinition("특성", "견고", "이 적의 방어막은 턴이 지나도 사라지지 않습니다."),
            });

            var errors = traits.ValidateKeywordRefs(keywords);
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0], Does.Contain("담력 시험"));
        }

        // ── 출하 저작 게이트 ─────────────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void EveryShippingTraitHasADescriptionRow()
        {
            // D2 재발 방지: 이 한 줄이 「설명이 조용히 빠진 특성」을 구조적으로 불가능하게 만든다.
            var errors = LoadShipping().ValidateKeywordRefs(LoadKeywords());
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test]
        [Category("ShippingData")]
        public void EveryTraitAuthoredOnAShippingMonsterIsRegisteredInTheTable()
        {
            // 표에 없는 특성은 배지도 툴팁도 오버레이도 못 받는다 — 저작만 있고 화면에 없는 상태다.
            var traits = LoadShipping();
            var monsters = ShippingMonsters();

            var missing = new List<string>();
            foreach (var entry in monsters)
            {
                foreach (var traitId in MonsterTraitResolver.CollectTraitIds(entry))
                {
                    if (!traits.TryGet(traitId, out _))
                    {
                        missing.Add($"{entry.Id} '{entry.DisplayName}': traitId '{traitId}' 행이 monster_traits.csv에 없다.");
                    }
                }
            }

            Assert.That(missing, Is.Empty, string.Join("\n", missing));
        }

        [Test]
        [Category("ShippingData")]
        public void ShippingTraitsCoverTheTenAuthoredKinds()
        {
            var ids = LoadShipping().Entries.Select(e => e.TraitId).ToArray();
            Assert.That(ids, Is.EquivalentTo(new[]
            {
                MonsterTraitIds.Sturdy, MonsterTraitIds.Toughness, MonsterTraitIds.Agitation,
                MonsterTraitIds.StrengthDistance, MonsterTraitIds.Aftermath, MonsterTraitIds.Stealth,
                MonsterTraitIds.AuraSeal, MonsterTraitIds.Pickpocket, MonsterTraitIds.Advance,
                MonsterTraitIds.GuardCycle,
            }));
        }

        [Test]
        [Category("ShippingData")]
        public void AnnounceRefsPointAtTheAnnouncementTable()
        {
            // 알림 ref가 표에 없으면 발동해도 아무 글자가 안 뜬다 — 빈 값(알림 없는 특성)만 예외다.
            var orphans = LoadShipping().Entries
                .Where(e => !string.IsNullOrEmpty(e.AnnounceRef))
                .Where(e => !MonsterTraitAnnouncement.TryGetText(e.AnnounceRef, 1, out _))
                .Select(e => $"{e.TraitId}: announceRef '{e.AnnounceRef}' 문안이 없다.")
                .ToArray();

            Assert.That(orphans, Is.Empty, string.Join("\n", orphans));
        }

        // ── 소매치기 = 두 저작의 짝 ──────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void PickpocketNeedsBothTheStealAndTheReturn()
        {
            var monsters = ShippingMonsters();

            foreach (var entry in monsters)
            {
                var steals = MonsterTraitResolver.StealsMoney(entry);
                var returns = MonsterTraitResolver.RestoresMoneyOnDeath(entry);
                Assert.That(
                    steals, Is.EqualTo(returns),
                    $"{entry.Id} '{entry.DisplayName}': 훔치기({steals})와 반환({returns}) 중 한쪽만 저작됐다 — "
                    + "훔치기만 있으면 뒤끝이 반환을 약속하지 못하고, 반환만 있으면 훔친 적이 없다.");
            }

            var pickpockets = monsters
                .Where(e => MonsterTraitResolver.CollectTraitIds(e).Contains(MonsterTraitIds.Pickpocket))
                .Select(e => e.Id)
                .ToArray();
            Assert.That(pickpockets, Is.EqualTo(new[] { "M014" }), "출하 소매치기는 야광귀 하나다.");
        }

        [Test]
        [Category("ShippingData")]
        public void PickpocketIsNotADistanceBoundBranch()
        {
            // 🔴 2026-09-05 사용자 확정: "거리는 상관없음. 그냥 야광귀 공격을 적중당하면 항상 발동".
            //    종전에는 근접 패턴(A051) 하나에만 stealMoneyAmount가 있어서, 중거리·투척으로 맞으면
            //    아무것도 잃지 않았다 — 「쫓아가 잡을 이유」가 거리에 따라 켜졌다 꺼졌다 했다.
            //    소매치기 몬스터의 <b>모든</b> 공격 패턴이 훔쳐야 그 축이 성립한다.
            foreach (var entry in ShippingMonsters()
                         .Where(e => MonsterTraitResolver.CollectTraitIds(e).Contains(MonsterTraitIds.Pickpocket)))
            {
                var silent = entry.AttackPatterns
                    .Where(pattern => pattern.StealMoneyAmount <= 0)
                    .Select(pattern => $"{entry.Id} {pattern.Id} '{pattern.DisplayName}'")
                    .ToArray();
                Assert.That(silent, Is.Empty,
                    "소매치기 몬스터인데 훔치지 않는 공격이 남아 있다(거리로 갈리면 안 된다):\n"
                    + string.Join("\n", silent));
            }
        }
    }
}
