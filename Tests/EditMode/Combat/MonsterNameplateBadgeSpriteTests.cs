#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 몬스터 네임플레이트 배지의 <b>전용 스프라이트 슬롯</b> 게이트. 상태이상은 kind 키 표
    /// (<see cref="StatusEffectIconCatalog.GetSprite"/>)로 <see cref="CombatOverlayIconRendererSceneTests"/>가
    /// 이미 전수 커버를 강제하지만, 배지 전용 슬롯(공격 의도·약오름·맷집·견고·밀치기·끌어당김·
    /// 방어막·뒤끝)은 kind 표 밖에 있어 <b>게이트가 하나도 없었다</b>(2026-09-01 실측: 참조 테스트 0건).
    ///
    /// <para>🔑 게이트를 <b>둘로 가른다</b>. 배선(어느 슬롯이 어느 배지로 가는가)은 로컬 픽스처로 재고,
    /// 출하 저작(그 슬롯에 그림이 들어와 있는가)은 씬에 배선된 카탈로그로 잰다. 하나로 합치면
    /// 아직 아트가 없는 배지가 <see cref="PendingArtBadgeKinds"/>에 실려 "null이어야 정상"이 되는 바람에
    /// <b>배선을 되돌려도 테스트가 안 빨개진다</b> — 안 무는 테스트는 없는 것보다 나쁘다.</para>
    /// </summary>
    public sealed class MonsterNameplateBadgeSpriteTests
    {
        /// <summary>
        /// 전용 슬롯을 가진 배지 종류. 여기 있으면 "그림이 붙어야 하는 자리"라는 뜻이다.
        /// 새 배지에 슬롯을 만들면 여기 한 줄을 더한다.
        /// </summary>
        private static readonly MonsterNameplateBadgeKind[] SlotBackedBadgeKinds =
        {
            MonsterNameplateBadgeKind.Attack,
            MonsterNameplateBadgeKind.Knockback,
            MonsterNameplateBadgeKind.Shield,
            // ⚠️ 아래 넷은 2026-09-04에 발행이 멈춘 레거시 슬롯이다(특성 배지가 대체). 슬롯과 아트는
            // 남겨 둔다 — 이 게이트가 계속 물어야 「아트가 조용히 사라지는」 경로가 막힌다.
            MonsterNameplateBadgeKind.Agitation,
            MonsterNameplateBadgeKind.Toughness,
            MonsterNameplateBadgeKind.Sturdy,
            MonsterNameplateBadgeKind.Aftermath,
            MonsterNameplateBadgeKind.SelfBuffShield,
        };

        /// <summary>
        /// 전용 슬롯을 <b>일부러</b> 두지 않는 종류.
        /// <list type="bullet">
        /// <item><see cref="MonsterNameplateBadgeKind.MultiHit"/> — 발주하지 않기로 확정(공격 배지 옆 ×N 텍스트로 읽힌다).</item>
        /// <item><see cref="MonsterNameplateBadgeKind.Status"/> · <see cref="MonsterNameplateBadgeKind.SelfBuffStatus"/>
        /// — kind 키 표에서 해소되므로 커버리지는 <see cref="CombatOverlayIconRendererSceneTests"/> 소관이다.</item>
        /// </list>
        /// </summary>
        private static readonly MonsterNameplateBadgeKind[] SlotlessBadgeKinds =
        {
            MonsterNameplateBadgeKind.MultiHit,
            MonsterNameplateBadgeKind.Status,
            MonsterNameplateBadgeKind.SelfBuffStatus,
            // 보스 기믹 의도·기물 성숙(2026-09-03 ⑥) — 한글 글리프 칩으로 저작(살/함/멸/흡).
            // 전용 아트를 발주하게 되면 SlotBackedBadgeKinds + PendingArtBadgeKinds로 옮긴다.
            MonsterNameplateBadgeKind.BossGimmick,
            MonsterNameplateBadgeKind.PropMaturity,
            // 특성 배지(2026-09-04)는 kind 하나에 특성 아홉이 실린다 — 커버리지가 kind가 아니라
            // <b>traitId</b> 단위라 이 표에서 잴 수 없다. 대신 아래 두 테스트가 traitId 전수를 문다
            // (배선 = ResolveBadgeSpriteMapsEachTraitToItsOwnSlot, 출하 = ShippedCatalogAuthorsEveryTraitExceptPendingArt).
            MonsterNameplateBadgeKind.Trait,
        };

        /// <summary>
        /// 아트가 아직 없는 특성. 그림이 카탈로그에 배선되는 순간 테스트가 먼저 빨개져서 이 목록을
        /// 지우게 만든다 — <see cref="PendingArtBadgeKinds"/>와 같은 규약이라 조용히 미저작으로 남지 않는다.
        /// </summary>
        private static readonly string[] PendingArtTraitIds =
        {
            // 2026-09-05 반입으로 비었다(cs 미정 · 특성 배지 4종 생성 + 은신 기존 아트 재사용).
            // 🔑 은신은 발주하지 않았다 — status_stealth.png가 2026-08-10에 이미 반입돼 있는데
            //    플레이어 은신 버프의 부여 경로가 0이라 화면 어디에도 안 뜨고 있었다. 같은 개념이라
            //    방어막이 ui_shield를 재사용한 선례를 따른다.
            // 목록은 지우지 않고 빈 채로 남긴다 — 새 특성을 만들면 아트가 오기 전까지 여기 넣어야
            // 「미저작인데 조용히 통과」가 막힌다.
            // 2026-09-05 수호 재충전(guardCycle · 결정 1) — 배지 아트 발주 대기. 글리프 「수」 폴백으로 출하.
            MonsterTraitIds.GuardCycle,
        };

        /// <summary>
        /// 슬롯은 있으나 아직 아트가 안 들어온 배지. <see cref="PendingArtStatusKinds"/> 선례 그대로,
        /// 아트가 반입되어 카탈로그에 배선되는 순간 이 테스트가 먼저 빨개져서 목록을 지우게 만든다 —
        /// 조용히 미저작으로 남는 경로가 없다.
        /// </summary>
        private static readonly MonsterNameplateBadgeKind[] PendingArtBadgeKinds =
        {
            // 2026-09-02 뒤끝 아트(status_aftermath.png · cracked-onggi)가 들어와 카탈로그
            // aftermathSprite에 배선되면서 목록이 비었다. 이제 SlotBackedBadgeKinds 전부가
            // 전수 커버 단언의 대상이다. 목록은 지우지 않고 빈 채로 남긴다 — 새 배지에 슬롯을
            // 만들면 아트가 오기 전까지 여기 넣어야 "미저작인데 조용히 통과"가 막힌다.
        };

        [Test]
        public void EveryBadgeKindIsEitherSlotBackedOrDeliberatelySlotless()
        {
            var covered = SlotBackedBadgeKinds.Concat(SlotlessBadgeKinds).ToArray();
            Assert.That(covered.Distinct().Count(), Is.EqualTo(covered.Length),
                "A badge kind must not appear in both the slot-backed and slotless tables.");

            foreach (MonsterNameplateBadgeKind kind in Enum.GetValues(typeof(MonsterNameplateBadgeKind)))
            {
                Assert.That(covered, Does.Contain(kind),
                    $"{kind} is a new badge kind — decide whether it gets a dedicated catalog slot " +
                    "(add it to SlotBackedBadgeKinds, plus PendingArtBadgeKinds until the art lands) " +
                    "or is deliberately slotless, so it cannot ship silently unauthored.");
            }
        }

        /// <summary>
        /// 배선: 각 배지가 <b>자기 슬롯</b>을 집는가. 로컬 픽스처에 슬롯마다 다른 스프라이트를 꽂아
        /// 되돌리거나 뒤바꾸면 반드시 빨개지게 만든다(출하 저작 상태와 무관하게 항상 문다).
        /// </summary>
        [Test]
        public void ResolveBadgeSpriteMapsEachSlotBackedKindToItsOwnSlot()
        {
            var textures = new List<Texture2D>();
            var sprites = new List<Sprite>();

            Sprite MakeSprite(string name)
            {
                var texture = new Texture2D(2, 2);
                textures.Add(texture);
                var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
                sprite.name = name;
                sprites.Add(sprite);
                return sprite;
            }

            var catalog = StatusEffectIconCatalog.CreateForTests();
            try
            {
                var attackIntent = MakeSprite("attackIntent");
                var agitation = MakeSprite("agitation");
                var toughness = MakeSprite("toughness");
                var sturdy = MakeSprite("sturdy");
                var block = MakeSprite("block");
                var aftermath = MakeSprite("aftermath");
                var knockback = MakeSprite("knockback");
                var pull = MakeSprite("pull");

                catalog.SetBadgeSpritesForTests(attackIntent, agitation, toughness, sturdy, block, aftermath);
                catalog.SetKnockbackSpriteForTests(knockback);
                catalog.SetPullSpriteForTests(pull);

                void AssertResolves(MonsterNameplateBadge badge, Sprite expected, string because)
                {
                    Assert.That(
                        CombatActorMarkerPresenter.ResolveBadgeSpriteForTests(badge, catalog),
                        Is.SameAs(expected), because);
                }

                AssertResolves(MonsterNameplateBadge.Attack(3), attackIntent, "공격 의도 배지는 attackIntentSprite를 쓴다.");
                AssertResolves(MonsterNameplateBadge.Agitation(2), agitation, "약오름 배지는 agitationSprite를 쓴다.");
                AssertResolves(MonsterNameplateBadge.Toughness(true), toughness, "맷집 배지는 toughnessSprite를 쓴다.");
                AssertResolves(MonsterNameplateBadge.Sturdy(), sturdy, "견고 배지는 sturdySprite를 쓴다.");
                AssertResolves(MonsterNameplateBadge.Shield(4), block, "방어막 배지는 플레이어 방어도 그림을 재사용한다(§H-1).");
                AssertResolves(MonsterNameplateBadge.SelfBuffShield(4), block, "방어막 예고도 같은 그림을 쓴다.");
                AssertResolves(MonsterNameplateBadge.Aftermath(), aftermath, "뒤끝 배지는 aftermathSprite를 쓴다(2026-09-01 W1).");

                // 부호가 방향이다(§16.1) — 밀치기와 끌어당김이 같은 그림으로 떨어지면 방향을 거짓말한다.
                AssertResolves(MonsterNameplateBadge.Knockback(1), knockback, "양수 밀치기는 knockbackSprite를 쓴다.");
                AssertResolves(MonsterNameplateBadge.Knockback(-1), pull, "음수는 끌어당김이라 pullSprite로 갈라진다.");

                // 일부러 슬롯이 없는 종류는 폴백(글리프 칩)으로 떨어져야 한다.
                Assert.That(
                    CombatActorMarkerPresenter.ResolveBadgeSpriteForTests(MonsterNameplateBadge.MultiHit(2), catalog),
                    Is.Null, "다단 히트는 전용 아트를 발주하지 않기로 확정됐다 — ×N 텍스트로 읽힌다.");
            }
            finally
            {
                foreach (var sprite in sprites)
                {
                    UnityEngine.Object.DestroyImmediate(sprite);
                }

                foreach (var texture in textures)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }

                UnityEngine.Object.DestroyImmediate(catalog);
            }
        }

        /// <summary>
        /// 출하 저작: 씬에 배선된 카탈로그에서 슬롯 있는 배지가 전부 그림을 가졌는가.
        /// 배지 카탈로그는 <c>MapCombatController</c>가 <c>statusIconOverlayRenderer.IconCatalog</c>에서
        /// 꺼내 넘기므로(MapCombatController.MapView.cs), 상태 아이콘 게이트와 <b>같은 에셋</b>을 잰다.
        /// </summary>
        [Test]
        public void ShippedCatalogAuthorsEverySlotBackedBadgeExceptPendingArt()
        {
            var previousScene = EditorSceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(TestAssetPaths.PrototypeTestScene, OpenSceneMode.Additive);
            try
            {
                var renderer = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<StatusIconOverlayRenderer>(true))
                    .SingleOrDefault();
                Assert.That(renderer, Is.Not.Null, "The prototype scene should contain the status icon renderer.");

                var catalog = renderer.IconCatalog;
                Assert.That(catalog, Is.Not.Null, "The prototype scene should wire the shared status-effect icon catalog.");

                foreach (var kind in SlotBackedBadgeKinds)
                {
                    var sprite = CombatActorMarkerPresenter.ResolveBadgeSpriteForTests(SampleBadge(kind), catalog);
                    if (Array.IndexOf(PendingArtBadgeKinds, kind) >= 0)
                    {
                        Assert.That(sprite, Is.Null,
                            $"{kind} now has authored art — remove it from PendingArtBadgeKinds so the gate covers it again.");
                        continue;
                    }

                    Assert.That(sprite, Is.Not.Null,
                        $"The shipped icon catalog should author a sprite for the {kind} nameplate badge.");
                }

                // 끌어당김은 밀치기와 같은 종류(부호만 다름)라 위 순회가 못 잡는다 — 따로 잰다.
                Assert.That(
                    CombatActorMarkerPresenter.ResolveBadgeSpriteForTests(MonsterNameplateBadge.Knockback(-1), catalog),
                    Is.Not.Null, "The shipped icon catalog should author the pull (negative knockback) sprite.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid())
                {
                    EditorSceneManager.SetActiveScene(previousScene);
                }
            }
        }

        /// <summary>
        /// 배선: 특성 배지가 <b>자기 traitId 슬롯</b>을 집는가. 슬롯을 뒤바꾸면 반드시 빨개진다.
        /// </summary>
        [Test]
        public void ResolveBadgeSpriteMapsEachTraitToItsOwnSlot()
        {
            var textures = new List<Texture2D>();
            var sprites = new List<Sprite>();

            Sprite MakeSprite(string name)
            {
                var texture = new Texture2D(2, 2);
                textures.Add(texture);
                var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
                sprite.name = name;
                sprites.Add(sprite);
                return sprite;
            }

            var catalog = StatusEffectIconCatalog.CreateForTests();
            try
            {
                var sturdy = MakeSprite("sturdy");
                var toughness = MakeSprite("toughness");
                catalog.SetTraitSpritesForTests(
                    (MonsterTraitIds.Sturdy, sturdy),
                    (MonsterTraitIds.Toughness, toughness));

                Assert.That(
                    CombatActorMarkerPresenter.ResolveBadgeSpriteForTests(
                        MonsterNameplateBadge.Trait(MonsterTraitIds.Sturdy), catalog),
                    Is.SameAs(sturdy), "견고 배지는 sturdy 슬롯을 집는다.");
                Assert.That(
                    CombatActorMarkerPresenter.ResolveBadgeSpriteForTests(
                        MonsterNameplateBadge.Trait(MonsterTraitIds.Toughness), catalog),
                    Is.SameAs(toughness), "맷집 배지는 toughness 슬롯을 집는다.");
                Assert.That(
                    CombatActorMarkerPresenter.ResolveBadgeSpriteForTests(
                        MonsterNameplateBadge.Trait(MonsterTraitIds.AuraSeal), catalog),
                    Is.Null, "슬롯이 없는 특성은 null로 떨어져 글리프 칩이 그린다 — 남의 그림을 빌리지 않는다.");
            }
            finally
            {
                foreach (var sprite in sprites)
                {
                    UnityEngine.Object.DestroyImmediate(sprite);
                }

                foreach (var texture in textures)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }

                UnityEngine.Object.DestroyImmediate(catalog);
            }
        }

        /// <summary>
        /// 출하 저작: monster_traits.csv에 있는 특성 전부가 씬 카탈로그에 그림을 가졌는가
        /// (아직 발주 전인 것은 <see cref="PendingArtTraitIds"/>에 명시적으로 실려야 한다).
        /// </summary>
        [Test]
        [Category("ShippingData")]
        public void ShippedCatalogAuthorsEveryTraitExceptPendingArt()
        {
            var traits = MonsterTraitCatalogCsv.ConvertFile(CombatCsvPaths.MonsterTraitsCsv);
            var previousScene = EditorSceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(TestAssetPaths.PrototypeTestScene, OpenSceneMode.Additive);
            try
            {
                var renderer = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<StatusIconOverlayRenderer>(true))
                    .SingleOrDefault();
                Assert.That(renderer, Is.Not.Null);
                var catalog = renderer.IconCatalog;
                Assert.That(catalog, Is.Not.Null);

                foreach (var trait in traits.Entries)
                {
                    var sprite = catalog.GetTraitSprite(trait.TraitId);
                    if (Array.IndexOf(PendingArtTraitIds, trait.TraitId) >= 0)
                    {
                        Assert.That(sprite, Is.Null,
                            $"특성 '{trait.TraitId}'에 아트가 들어왔다 — PendingArtTraitIds에서 지워 게이트가 다시 물게 한다.");
                        continue;
                    }

                    Assert.That(sprite, Is.Not.Null,
                        $"출하 아이콘 카탈로그가 특성 '{trait.TraitId}'의 그림을 저작해야 한다.");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid())
                {
                    EditorSceneManager.SetActiveScene(previousScene);
                }
            }
        }

        private static MonsterNameplateBadge SampleBadge(MonsterNameplateBadgeKind kind)
        {
            switch (kind)
            {
                case MonsterNameplateBadgeKind.Attack:
                    return MonsterNameplateBadge.Attack(3);
                case MonsterNameplateBadgeKind.Knockback:
                    return MonsterNameplateBadge.Knockback(1);
                case MonsterNameplateBadgeKind.Shield:
                    return MonsterNameplateBadge.Shield(4);
                case MonsterNameplateBadgeKind.Agitation:
                    return MonsterNameplateBadge.Agitation(2);
                case MonsterNameplateBadgeKind.Toughness:
                    return MonsterNameplateBadge.Toughness(true);
                case MonsterNameplateBadgeKind.Sturdy:
                    return MonsterNameplateBadge.Sturdy();
                case MonsterNameplateBadgeKind.Aftermath:
                    return MonsterNameplateBadge.Aftermath();
                case MonsterNameplateBadgeKind.SelfBuffShield:
                    return MonsterNameplateBadge.SelfBuffShield(4);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind), kind, "Slot-backed badge kinds need a sample badge here.");
            }
        }
    }
}
#endif
