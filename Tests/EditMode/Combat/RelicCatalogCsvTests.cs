using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    // 유물·저주 CSV 컨버터(RelicCatalogCsv) + 카탈로그 façade 지연로딩 회귀.
    public sealed class RelicCatalogCsvTests
    {
        // 출하 CSV 전 행이 파싱되는지 + 원년 3종의 값이 보존되는지. 행 수는 콘텐츠가 늘 때마다 바뀌므로
        // 정확한 수를 고정하지 않고 하한만 지킨다 — 고정하면 유물을 추가할 때마다 관계없는 테스트가 깨진다.
        [Category("ShippingData")]
        [Test]
        public void ShippedRelicCsvParsesEveryRowAndPreservesTheOriginalThree()
        {
            var catalog = RelicCatalogCsv.ConvertFile(CombatCsvPaths.RelicsCsv);

            Assert.That(catalog.Entries.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(catalog.Entries.Select(entry => entry.Id).Distinct().Count(), Is.EqualTo(catalog.Entries.Count),
                "유물 id는 카탈로그 안에서 유일해야 한다.");

            Assert.That(catalog.TryGet(PlayerPermanentItemCatalog.TigerBadgeRelicId, out var tiger), Is.True);
            Assert.That(tiger.Kind, Is.EqualTo(PlayerPermanentItemKind.Relic));
            Assert.That(tiger.DisplayName, Is.EqualTo("호신 삼단봉"));
            Assert.That(tiger.EffectKind, Is.EqualTo(PlayerPermanentItemEffectKind.AttackDamageBonus));
            Assert.That(tiger.EffectAmount, Is.EqualTo(1));

            Assert.That(catalog.TryGet(PlayerPermanentItemCatalog.HanriverShoesRelicId, out var shoes), Is.True);
            Assert.That(shoes.EffectKind, Is.EqualTo(PlayerPermanentItemEffectKind.MovementRangeBonus));
            Assert.That(shoes.EffectAmount, Is.EqualTo(1));

            // T2(2026-08-06): 저주 유물 폐기 — 금 간 기억 행 삭제, Curse kind는 임포트가 거부한다.
            Assert.That(catalog.TryGet(PlayerPermanentItemCatalog.CrackedMemoryCurseId, out _), Is.False);
            Assert.That(catalog.Entries.All(entry => entry.Kind == PlayerPermanentItemKind.Relic), Is.True,
                "출하 유물 카탈로그에 Relic 외의 kind가 있으면 안 된다.");
        }

        [Test]
        public void CatalogFacadeResolvesShippedItemsFromCsv()
        {
            // PlayerPermanentItemCatalog는 Register 없이도 CSV를 지연 로딩해야 한다(순수 EditMode).
            Assert.That(PlayerPermanentItemCatalog.TryGet(PlayerPermanentItemCatalog.TigerBadgeRelicId, out var tiger), Is.True);
            Assert.That(tiger.EffectKind, Is.EqualTo(PlayerPermanentItemEffectKind.AttackDamageBonus));
            Assert.That(PlayerPermanentItemCatalog.Definitions.Count, Is.GreaterThanOrEqualTo(3));
        }

        // 저작된 모든 효과 축이 실제 enum 값으로 해소되는지. effectKind는 CSV에 이름으로 적히므로
        // 오타 한 줄이 조용히 None으로 떨어지면 유물이 아무 일도 하지 않는다.
        [Category("ShippingData")]
        [Test]
        public void ShippedRelicCsvHasNoSilentlyInertRows()
        {
            var catalog = RelicCatalogCsv.ConvertFile(CombatCsvPaths.RelicsCsv);

            // 트리거 유물(T2 페이즈 C)은 effectKind가 의도적으로 None이다 — 트리거가 곧 효과이므로
            // (수치는 effectAmount, 발동은 TriggerKind), inert 판정에서 제외한다.
            var inert = catalog.Entries
                .Where(entry => entry.TriggerKind == RelicTriggerKind.None)
                .Where(entry => entry.EffectKind == PlayerPermanentItemEffectKind.None || entry.EffectAmount == 0)
                .Select(entry => entry.Id)
                .ToArray();

            Assert.That(inert, Is.Empty, "효과 없는 유물 행: " + string.Join(", ", inert));
        }

        [Test]
        public void ConvertTextParsesKindAndEffectColumns()
        {
            const string csv =
                "id,kind,displayNameKo,descriptionKo,effectKind,effectAmount\n" +
                "relic-test,Relic,테스트 유물,설명,AttackDamageBonus,3\n";

            var catalog = RelicCatalogCsv.ConvertText(csv, "test.csv");

            Assert.That(catalog.Entries.Single().EffectAmount, Is.EqualTo(3));
            Assert.That(catalog.Entries.Single().Kind, Is.EqualTo(PlayerPermanentItemKind.Relic));
        }

        [Test]
        public void BlankEffectKindDefaultsToNone()
        {
            const string csv =
                "id,kind,displayNameKo,descriptionKo,effectKind,effectAmount\n" +
                "relic-flavor,Relic,장식 유물,,,0\n";

            var catalog = RelicCatalogCsv.ConvertText(csv, "test.csv");

            Assert.That(catalog.Entries.Single().EffectKind, Is.EqualTo(PlayerPermanentItemEffectKind.None));
        }

        [Test]
        public void DuplicateIdThrows()
        {
            const string csv =
                "id,kind,displayNameKo,descriptionKo,effectKind,effectAmount\n" +
                "relic-dup,Relic,하나,,AttackDamageBonus,1\n" +
                "relic-dup,Relic,둘,,AttackDamageBonus,1\n";

            Assert.That(() => RelicCatalogCsv.ConvertText(csv, "test.csv"), Throws.ArgumentException);
        }

        [Test]
        public void UnknownEnumThrows()
        {
            const string csv =
                "id,kind,displayNameKo,descriptionKo,effectKind,effectAmount\n" +
                "relic-bad,Relic,잘못,,NonexistentEffect,1\n";

            Assert.That(() => RelicCatalogCsv.ConvertText(csv, "test.csv"), Throws.ArgumentException);
        }
    }
}
