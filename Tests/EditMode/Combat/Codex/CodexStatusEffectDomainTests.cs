using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Codex;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode.Codex
{
    /// <summary>
    /// 도감 상태이상 도메인(<c>docs/codex-plan.md</c> P0). 출하 <c>status_effects.csv</c>를 실제로 읽어
    /// 도감이 내놓는 항목이 저작과 일치하는지, 아이콘 없는 kind가 <b>빈 칸이 아니라 이름 전체
    /// 폴백</b>으로 떨어지는지 확인한다.
    /// </summary>
    public sealed class CodexStatusEffectDomainTests
    {
        private static readonly Color Accent = new Color(0.725f, 0.545f, 0.961f, 1f);

        private static StatusEffectCatalogDefinition LoadShippingCatalog()
        {
            return StatusEffectCatalogCsv.ConvertFile(CombatCsvPaths.StatusEffectsCsv);
        }

        [Test]
        public void Entries_MirrorShippingCsv()
        {
            var catalog = LoadShippingCatalog();
            var domain = new CodexStatusEffectDomain(catalog, icons: null, Accent);

            Assert.That(domain.Entries.Count, Is.EqualTo(catalog.Entries.Count),
                "도감이 저작보다 적거나 많은 항목을 내놓으면 어느 쪽이 정본인지 알 수 없게 된다.");
            CollectionAssert.AreEqual(
                catalog.Entries.Select(entry => entry.Kind.ToString()).ToList(),
                domain.Entries.Select(entry => entry.Id).ToList(),
                "id는 kind 이름이고 순서는 저작 순서를 따른다.");
        }

        [Test]
        public void StatusDomain_IsAlwaysUnlocked()
        {
            var domain = new CodexStatusEffectDomain(LoadShippingCatalog(), icons: null, Accent);

            // Q6 확정 — 규칙 참조표를 잠그면 플레이어가 규칙을 못 읽는다.
            Assert.That(domain.AlwaysUnlocked, Is.True);
        }

        [Test]
        public void MissingIcon_FallsBackToFullDisplayName()
        {
            var catalog = LoadShippingCatalog();
            var domain = new CodexStatusEffectDomain(catalog, icons: null, Accent);

            foreach (var entry in domain.Entries)
            {
                Assert.That(entry.Thumbnail.Layer, Is.EqualTo(CodexThumbnailLayer.ProceduralLabel),
                    $"{entry.Id}: 아이콘 카탈로그가 없으면 절차 폴백으로 떨어져야 한다.");
                Assert.That(entry.Thumbnail.Label, Is.EqualTo(entry.DisplayName),
                    $"{entry.Id}: 폴백은 첫 글자가 아니라 이름 전체다(Q9 확정).");
            }
        }

        [Test]
        public void DedicatedIcon_WinsOverFallback()
        {
            var catalog = LoadShippingCatalog();
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), Vector2.one * 0.5f);
            try
            {
                var icons = StatusEffectIconCatalog.CreateForTests((StatusEffectKind.Poison, sprite));
                var domain = new CodexStatusEffectDomain(catalog, icons, Accent);

                var poison = domain.Entries.Single(entry => entry.Id == nameof(StatusEffectKind.Poison));
                Assert.That(poison.Thumbnail.Layer, Is.EqualTo(CodexThumbnailLayer.Dedicated));
                Assert.That(poison.Thumbnail.Sprite, Is.SameAs(sprite));
                Assert.That(poison.Thumbnail.IsFallback, Is.False);

                var stun = domain.Entries.Single(entry => entry.Id == nameof(StatusEffectKind.Stun));
                Assert.That(stun.Thumbnail.IsFallback, Is.True,
                    "아이콘을 준 kind만 1층으로 올라가야 한다.");

                Object.DestroyImmediate(icons);
            }
            finally
            {
                Object.DestroyImmediate(sprite);
            }
        }

        [Test]
        public void ValueModeNone_DoesNotAdvertiseAnAmount()
        {
            var catalog = LoadShippingCatalog();
            var domain = new CodexStatusEffectDomain(catalog, icons: null, Accent);

            foreach (var definition in catalog.Entries.Where(e => e.ValueMode == StatusEffectValueMode.None))
            {
                var entry = domain.Entries.Single(candidate => candidate.Id == definition.Kind.ToString());

                // Amount를 읽지 않는 종류에 "값 0"을 띄우면 0이 의미 있는 수치처럼 보인다.
                Assert.That(entry.MetaChips.Any(chip => chip.StartsWith("값")), Is.False,
                    $"{definition.Kind}: ValueMode=None인데 값 칩이 붙었다.");
                Assert.That(entry.DetailRows.Any(row => row.Label == "기본 값"), Is.False,
                    $"{definition.Kind}: ValueMode=None인데 기본 값 줄이 붙었다.");
            }
        }

        [Test]
        public void DebugRows_CarryAuthoringColumnsPlayersDoNotNeed()
        {
            var domain = new CodexStatusEffectDomain(LoadShippingCatalog(), icons: null, Accent);
            var entry = domain.Entries.First();

            var debugLabels = entry.DebugRows.Select(row => row.Label).ToList();
            CollectionAssert.Contains(debugLabels, "valueMode");
            CollectionAssert.Contains(debugLabels, "timing");
            CollectionAssert.Contains(debugLabels, "expirePolicy");

            // 도감 뷰가 그리는 줄에는 저작 축이 새어 나오면 안 된다.
            CollectionAssert.DoesNotContain(entry.DetailRows.Select(row => row.Label).ToList(), "valueMode");
        }
    }
}
