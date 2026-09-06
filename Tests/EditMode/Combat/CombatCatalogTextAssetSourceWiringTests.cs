using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEditor;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 출하 <c>CombatCatalogTextAssetSource.asset</c> 배선 가드. 플레이어 빌드는 <c>Assets/Data</c> CSV를
    /// 디스크에서 읽지 못하므로 이 에셋의 TextAsset 슬롯이 유일한 데이터 경로다. 슬롯이 비면 에디터는
    /// 소스 CSV로 조용히 폴백해 증상이 안 보이고, 빌드에서만 데이터가 사라진다(2026-09-06 불가살 보스
    /// 페이즈·철조각 살포 소실이 그 사례). 여기서는 "빌드 경로가 에디터 경로와 같은 데이터를 만든다"만 잠근다.
    /// </summary>
    public sealed class CombatCatalogTextAssetSourceWiringTests
    {
        private const string ShippingSourcePath = "Assets/Data/Combat/Catalogs/CombatCatalogTextAssetSource.asset";

        private static CombatCatalogTextAssetSource LoadShippingSource()
        {
            var source = AssetDatabase.LoadAssetAtPath<CombatCatalogTextAssetSource>(ShippingSourcePath);
            Assert.That(source, Is.Not.Null, "Shipping combat catalog TextAsset source asset should exist.");
            return source;
        }

        [Category("ShippingData")]
        [Test]
        public void EveryTextAssetSlotIsAssignedSoPlayerBuildsNeverFallBack()
        {
            var source = LoadShippingSource();

            var missing = typeof(CombatCatalogTextAssetSource)
                .GetProperties()
                .Where(property => property.PropertyType == typeof(bool) && property.Name.StartsWith("Has"))
                .Where(property => !(bool)property.GetValue(source))
                .Select(property => property.Name)
                .ToArray();

            Assert.That(missing, Is.Empty,
                "Every TextAsset slot must be assigned; an empty slot silently falls back in the editor and drops data in player builds.");
        }

        [Category("ShippingData")]
        [Test]
        public void BossCatalogFromTextAssetsMatchesSourceCsvBossCatalog()
        {
            var source = LoadShippingSource();
            var expected = CombatCatalogFactory.CreateBossCatalog();

            var actual = source.CreateBossCatalog();

            Assert.That(actual.Profiles.Select(profile => profile.BossId),
                Is.EquivalentTo(expected.Profiles.Select(profile => profile.BossId)),
                "The build-path boss catalog should carry every boss authored in boss_profiles.csv.");
            foreach (var expectedProfile in expected.Profiles)
            {
                Assert.That(actual.TryGetProfile(expectedProfile.BossId, out var actualProfile), Is.True);
                Assert.That(actualProfile.Phases.Count, Is.EqualTo(expectedProfile.Phases.Count),
                    $"Boss {expectedProfile.BossId} should reach the build with all authored phases.");
            }
        }

        [Category("ShippingData")]
        [Test]
        public void KillDropRatesFromTextAssetMatchSourceCsv()
        {
            var source = LoadShippingSource();
            var expected = KillDropRatesCsvConverter.ConvertFile(CombatCsvPaths.KillDropRatesCsv);

            var actual = source.CreateKillDropRates();

            Assert.That(
                actual.Entries.Select(entry => (entry.Tier, entry.Kind, entry.Percent, entry.MinAmount, entry.MaxAmount)),
                Is.EquivalentTo(expected.Entries.Select(entry => (entry.Tier, entry.Kind, entry.Percent, entry.MinAmount, entry.MaxAmount))),
                "Kill drop rates authored in kill_drop_rates.csv should reach the build unchanged instead of the code default.");
        }
    }
}
