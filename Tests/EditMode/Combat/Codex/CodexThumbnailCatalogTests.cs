using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Codex;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// P5 1층 해소 경로. <b>굽기가 아니라 해소가 P5의 본체다</b> — P4까지 다섯 도메인이
    /// <c>CodexThumbnail.Resolve(null, …)</c>로 전용 아트 자리를 하드코딩하고 있어서,
    /// PNG를 아무리 잘 구워도 화면이 한 픽셀도 바뀌지 않았다. 여기서 재는 것은 그 경로가
    /// 다시 끊기지 않는가다.
    /// </summary>
    public sealed class CodexThumbnailCatalogTests
    {
        private CodexThumbnailCatalog catalog;
        private Sprite sprite;
        private Texture2D texture;

        [SetUp]
        public void SetUp()
        {
            texture = new Texture2D(4, 4);
            sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            sprite.name = CodexThumbnailCatalog.KeyFor("M001");

            catalog = ScriptableObject.CreateInstance<CodexThumbnailCatalog>();
            catalog.ConfigureForTests(new[] { sprite });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void Resolve_FindsSpriteByEntryId()
        {
            Assert.That(catalog.Resolve("M001"), Is.SameAs(sprite));
        }

        [Test]
        public void Resolve_ReturnsNullForUnknownId()
        {
            Assert.That(catalog.Resolve("M999"), Is.Null);
        }

        [Test]
        public void Resolve_ReturnsNullForBlankId()
        {
            Assert.That(catalog.Resolve(null), Is.Null);
            Assert.That(catalog.Resolve("   "), Is.Null);
        }

        /// <summary>
        /// 베이커와 해소기가 이름을 <b>같은 함수</b>로 짓는다는 계약. 둘이 갈리면 굽기는 성공하는데
        /// 화면만 안 바뀌고, 그 증상은 원인을 가리키지 않는다.
        /// </summary>
        [Test]
        public void KeyFor_UsesSharedPrefix()
        {
            Assert.That(CodexThumbnailCatalog.KeyFor("M905"), Is.EqualTo("codex_thumb_M905"));
        }

        [Test]
        public void Resolve_ReachesDedicatedLayerThroughThumbnail()
        {
            var thumbnail = CodexThumbnail.Resolve(catalog.Resolve("M001"), "삼목구", Color.white);

            Assert.That(thumbnail.Layer, Is.EqualTo(CodexThumbnailLayer.Dedicated));
            Assert.That(thumbnail.IsFallback, Is.False, "1층이 뚫렸으면 FB 배지가 붙으면 안 된다.");
            Assert.That(thumbnail.Sprite, Is.SameAs(sprite));
        }

        /// <summary>
        /// 🔴 몬스터 도메인이 카탈로그를 <b>실제로 들여다보는가</b>. 이 시험이 P5 이전에는
        /// 성립하지 않았다 — 생성자가 카탈로그를 받지도 않았다.
        /// </summary>
        [Test]
        public void MonsterDomain_UsesCatalogForDedicatedThumbnail()
        {
            CodexShippingDomains.EnsureAttackShapeLibrary();
            var monsterCatalog = CodexShippingDomains.LoadMonsterCatalog();

            var wired = new CodexMonsterDomain(monsterCatalog, Color.white, catalog);
            var m001 = wired.Entries.First(entry => entry.Id == "M001");

            Assert.That(m001.Thumbnail.Layer, Is.EqualTo(CodexThumbnailLayer.Dedicated));
            Assert.That(m001.Thumbnail.Sprite, Is.SameAs(sprite));
            Assert.That(
                m001.Thumbnail.PreserveAspect,
                Is.True,
                "구운 그림은 칸 비율째 렌더되므로 잘라내면 실루엣이 뭉개진다.");

            // 카탈로그에 없는 항목은 예전처럼 이름 전체 폴백으로 남아야 한다.
            var other = wired.Entries.First(entry => entry.Id != "M001");
            Assert.That(other.Thumbnail.Layer, Is.EqualTo(CodexThumbnailLayer.ProceduralLabel));
        }

        [Test]
        public void MonsterDomain_WithoutCatalogStaysOnFallback()
        {
            CodexShippingDomains.EnsureAttackShapeLibrary();
            var monsterCatalog = CodexShippingDomains.LoadMonsterCatalog();

            var bare = new CodexMonsterDomain(monsterCatalog, Color.white);

            Assert.That(
                bare.Entries.All(entry => entry.Thumbnail.IsFallback),
                Is.True,
                "카탈로그를 안 넘기면 P4까지의 모습 그대로여야 한다 — 시험 픽스처가 아트를 요구하면 안 된다.");
        }
    }
}
