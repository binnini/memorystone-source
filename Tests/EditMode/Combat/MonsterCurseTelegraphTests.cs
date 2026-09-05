using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 「저주 부여」 타일 표식 축(2026-09-01 W2)을 체인 다섯 마디에서 각각 고정한다.
    /// 규칙 → 예고(<see cref="MonsterIntentPreview.InjectsCurse"/>) → 좌표 집계
    /// (<see cref="CombatOverlayQuery.BuildMonsterAttackStatusIconCells"/>) → 주석
    /// (<see cref="CombatOverlayIconAnnotation.InjectsCurse"/>) → 렌더러 아이콘·툴팁.
    ///
    /// <para>🔑 <b>저주는 상태이상이 아니다.</b> 턴이 지나 풀리는 것이 아니라 덱에 영구히 남는 오염이라
    /// 자기 축으로 탄다 — 넉백이 상태이상 목록에 안 실리는 것과 같은 이유다. 저주만 주는 패턴
    /// (A028 쇳가루 휩쓸기는 X08 고정, A035 속삭임은 풀 추첨)이 상태이상도 넉백도 없이 오는데,
    /// 축을 만들기 전에는 그 예고가 <b>타일에서 통째로 사라졌다</b>.</para>
    /// </summary>
    public sealed class MonsterCurseTelegraphTests
    {
        // ---- 마디 1·2: 규칙 → 예고 --------------------------------------------------------

        [Test]
        public void FixedCurseCardPatternTelegraphsCurseOnThePreview()
        {
            var state = BuildStateWithCursePattern(injectStatusCardId: "X08");

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true).Single();

            Assert.That(preview.InjectsCurse, Is.True,
                "고정 저주 한 장(injectStatusCardId · A028 쇳가루 휩쓸기)도 예고에 실려야 한다.");
        }

        [Test]
        public void CursePoolPatternTelegraphsCurseOnThePreview()
        {
            var state = BuildStateWithCursePattern(injectStatusCardPool: new[] { "X05", "X07", "X12" });

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true).Single();

            Assert.That(preview.InjectsCurse, Is.True,
                "풀 추첨(injectStatusCardPool · A035 속삭임)도 화면이 말하는 것은 같다 — 「덱이 더러워진다」.");
        }

        [Test]
        public void APatternWithoutCurseAuthoringDoesNotTelegraphCurse()
        {
            var state = BuildStateWithCursePattern();

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true).Single();

            Assert.That(preview.InjectsCurse, Is.False, "저주 저작이 없는 패턴은 표식을 띄우면 안 된다.");
        }

        [Test]
        public void AStunnedMonsterDropsTheCurseTelegraph()
        {
            // 기절은 공격 자체가 취소된 상태다 — 예고가 남으면 "이 칸에 서면 저주"라고 거짓말한다.
            // 위 축들(공격 범위·소환·전진)과 <b>같은 술어</b>(IsMonsterAttackBlocked)로 걸러야 한다.
            var state = BuildStateWithCursePattern(injectStatusCardId: "X08");
            Assert.That(state.GetMonsterIntentPreviews(includeUnrevealed: true).Single().InjectsCurse, Is.True,
                "전제: 기절 전에는 저주가 예고된다.");

            InjectEffect(state, StatusEffectKind.Stun, state.Monsters.Single().Id, remainingTurns: 1);

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true).Single();
            Assert.That(preview.InjectsCurse, Is.False, "기절한 몬스터는 이번 턴 저주를 주지 않는다.");
        }

        // ---- 마디 3·4: 좌표 집계 → 주석 ----------------------------------------------------

        [Test]
        public void CurseOnlyPreviewStillProducesTileAnnotations()
        {
            // 🔴 이 테스트가 결함의 핵이다. 축을 만들기 전 집계는 「상태이상도 넉백도 없으면 건너뛴다」라
            //    저주만 주는 패턴의 예고가 타일에서 통째로 사라졌다.
            var preview = CreatePreview(
                new HexCoord(0, 0),
                new[] { new HexCoord(1, 0), new HexCoord(2, 0) },
                injectsCurse: true);

            var annotations = CombatOverlayQuery.BuildMonsterAttackStatusIconCells(new[] { preview });

            Assert.That(annotations.Count, Is.EqualTo(2));
            CollectionAssert.AreEquivalent(
                new[] { new HexCoord(1, 0), new HexCoord(2, 0) },
                annotations.Select(annotation => annotation.Coord));
            foreach (var annotation in annotations)
            {
                Assert.That(annotation.InjectsCurse, Is.True);
                Assert.That(annotation.Effects, Is.Empty, "저주는 상태이상 목록을 빌려 쓰지 않는다.");
                Assert.That(annotation.Knockback, Is.False);
            }
        }

        [Test]
        public void CurseMergesAcrossOverlappingPreviews()
        {
            var withStatus = CreatePreview(new HexCoord(0, 0), new[] { new HexCoord(1, 0) },
                effects: new[] { StatusEffectKind.Poison });
            var withCurse = CreatePreview(new HexCoord(3, 0), new[] { new HexCoord(1, 0) }, injectsCurse: true);

            var merged = CombatOverlayQuery
                .BuildMonsterAttackStatusIconCells(new[] { withStatus, withCurse })
                .Single();

            Assert.That(merged.Coord, Is.EqualTo(new HexCoord(1, 0)));
            CollectionAssert.AreEqual(new[] { StatusEffectKind.Poison }, merged.Effects);
            Assert.That(merged.InjectsCurse, Is.True, "겹치면 OR — 어느 예고든 덱이 더러워지는 것은 같다.");
        }

        [Test]
        public void APreviewWithoutCurseLeavesTheAnnotationClean()
        {
            var preview = CreatePreview(new HexCoord(0, 0), new[] { new HexCoord(1, 0) },
                effects: new[] { StatusEffectKind.Poison });

            var annotation = CombatOverlayQuery.BuildMonsterAttackStatusIconCells(new[] { preview }).Single();

            Assert.That(annotation.InjectsCurse, Is.False);
        }

        [Test]
        public void HiddenIntentDoesNotLeakTheCurseTelegraph()
        {
            // 미지(D-5 완전 은폐): 예고가 통째로 비므로 '?' 하나만 남아야 한다 — 저주가 새면
            // 숨긴 것을 화면이 그대로 말한다.
            var hidden = new MonsterIntentPreview(
                monsterId: "hidden",
                definitionId: "def",
                spawnRefId: "spawn",
                currentCoord: new HexCoord(0, 0),
                predictedMoveCoord: new HexCoord(0, 0),
                attackRangeCoords: System.Array.Empty<HexCoord>(),
                intentType: EnemyIntentType.Attack,
                isIntentHidden: true,
                // '?' 표식은 <b>「미지」 상태이상</b> 축이다 — 은신은 이 축이 아니다(아래 테스트).
                isIntentVeiledByStatus: true);

            var annotation = CombatOverlayQuery.BuildMonsterAttackStatusIconCells(new[] { hidden }).Single();

            Assert.That(annotation.InjectsCurse, Is.False);
            CollectionAssert.AreEqual(new[] { StatusEffectKind.Unknown }, annotation.Effects);
        }

        /// <summary>
        /// 🔴 2026-09-05 실플레이 버그: 은신 몬스터는 마커가 감춰져 있는데 <b>자기 좌표에 물음표가
        /// 서서</b> 위치를 그대로 흘리고 있었다. 「저기 누가 있는지조차 모른다」가 은신이므로
        /// 표식 자체가 대상이 아니다 — 예고를 지우는 판정(IsIntentHidden)과 표식을 세우는
        /// 판정(IsIntentVeiledByStatus)이 갈려야 한다.
        /// </summary>
        [Test]
        public void StealthHiddenIntentDrawsNoTileIconAtAll()
        {
            var stealthed = new MonsterIntentPreview(
                monsterId: "stealthed",
                definitionId: "def",
                spawnRefId: "spawn",
                currentCoord: new HexCoord(0, 0),
                predictedMoveCoord: new HexCoord(0, 0),
                attackRangeCoords: System.Array.Empty<HexCoord>(),
                intentType: EnemyIntentType.Attack,
                isIntentHidden: true,
                isIntentVeiledByStatus: false);

            Assert.That(
                CombatOverlayQuery.BuildMonsterAttackStatusIconCells(new[] { stealthed }), Is.Empty,
                "은신 몬스터의 좌표에 아이콘이 하나라도 서면 감춘 위치가 그대로 샌다.");
        }

        // ---- 마디 5: 렌더러 -----------------------------------------------------------------

        [Test]
        public void CurseOnlyAnnotationSpawnsAnIconAndHoversItsOwnTooltip()
        {
            var rendererGo = new GameObject("StatusIconOverlayRenderer");
            var cameraGo = new GameObject("CurseHoverCamera");
            StatusEffectIconCatalog catalog = null;
            Sprite curseSprite = null;
            Texture2D texture = null;
            try
            {
                var renderer = rendererGo.AddComponent<StatusIconOverlayRenderer>();
                renderer.Configure(new PassThroughProjector());
                renderer.SetRemoveWhiteBackgroundForTesting(false);

                texture = new Texture2D(2, 2);
                curseSprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
                curseSprite.name = "curse";
                catalog = StatusEffectIconCatalog.CreateForTests();
                catalog.SetCurseSpriteForTests(curseSprite);
                renderer.SetIconCatalogForTesting(catalog);

                renderer.ApplyAnnotations(new[]
                {
                    new CombatOverlayIconAnnotation(
                        new HexCoord(0, 0), System.Array.Empty<StatusEffectKind>(), injectsCurse: true)
                });

                Assert.That(renderer.ActiveIconCount, Is.EqualTo(1),
                    "저주만 있는 칸에도 아이콘이 하나 떠야 한다.");

                var spriteRenderer = rendererGo.GetComponentsInChildren<SpriteRenderer>(true).Single();
                Assert.That(spriteRenderer.sprite, Is.SameAs(curseSprite),
                    "저주 표식은 카탈로그의 전용 슬롯을 쓴다 — 남의 그림을 빌리지 않는다.");

                var camera = cameraGo.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.transform.rotation = Quaternion.identity;

                var screenPos = (Vector2)camera.WorldToScreenPoint(new Vector3(0f, 0.03f, 0f));
                Assert.That(renderer.TryGetHoveredTooltip(camera, screenPos, out var payload), Is.True);
                Assert.That(payload.IsCurse, Is.True, "호버는 저주 전용 툴팁으로 갈라져야 한다.");
                Assert.That(payload.IsKnockback, Is.False);
                Assert.That(payload.IsAnnihilation, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(rendererGo);
                Object.DestroyImmediate(cameraGo);
                if (catalog != null) Object.DestroyImmediate(catalog);
                if (curseSprite != null) Object.DestroyImmediate(curseSprite);
                if (texture != null) Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void CurseWithoutAuthoredArtFallsBackToItsOwnChipInsteadOfBorrowingAStatusColour()
        {
            // 아트 반입 전에도 화면이 읽혀야 한다. 폴백이 ToColor(Kind)로 떨어지면 StatusEffectKind가
            // 없는 저주가 None의 색·글자를 빌려 「무엇인지 알 수 없는 칩」이 된다.
            var rendererGo = new GameObject("StatusIconOverlayRenderer");
            try
            {
                var renderer = rendererGo.AddComponent<StatusIconOverlayRenderer>();
                renderer.Configure(new PassThroughProjector());
                renderer.SetRemoveWhiteBackgroundForTesting(false);
                renderer.SetIconCatalogForTesting(null);

                renderer.ApplyAnnotations(new[]
                {
                    new CombatOverlayIconAnnotation(
                        new HexCoord(0, 0), System.Array.Empty<StatusEffectKind>(), injectsCurse: true)
                });

                Assert.That(renderer.ActiveIconCount, Is.EqualTo(1));
                var label = rendererGo.GetComponentsInChildren<TMPro.TextMeshPro>(true).Single();
                Assert.That(label.text, Is.EqualTo("저주"));
            }
            finally
            {
                Object.DestroyImmediate(rendererGo);
            }
        }

        /// <summary>
        /// 출하 저작: 씬에 배선된 카탈로그가 저주 표식 스프라이트를 들고 있는가(2026-09-02 반입).
        /// 위 렌더러 테스트는 로컬 픽스처를 쓰므로 <b>배선만</b> 재고 출하 상태는 못 본다 — 아트가
        /// 카탈로그에서 빠지면 화면은 조용히 「저주」 글자 칩으로 돌아가고 아무도 안 빨개진다.
        /// </summary>
        [Test]
        public void ShippedCatalogAuthorsTheCurseMarker()
        {
            var previousScene = EditorSceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(TestAssetPaths.PrototypeTestScene, OpenSceneMode.Additive);
            try
            {
                var renderer = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<StatusIconOverlayRenderer>(true))
                    .SingleOrDefault();
                Assert.That(renderer, Is.Not.Null, "The prototype scene should contain the status icon renderer.");
                Assert.That(renderer.IconCatalog, Is.Not.Null,
                    "The prototype scene should wire the shared status-effect icon catalog.");
                Assert.That(renderer.IconCatalog.CurseSprite, Is.Not.Null,
                    "The shipped icon catalog should author the curse tile marker (status_curse.png).");
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

        // --- fixtures ----------------------------------------------------------------------

        private sealed class PassThroughProjector : IHexMapOverlaySurfaceProjector
        {
            public Vector3 ProjectOverlaySurface(HexCoord coord) => new Vector3(coord.Q, 0f, coord.R);
        }

        private static MonsterIntentPreview CreatePreview(
            HexCoord coord,
            HexCoord[] attackRange,
            StatusEffectKind[] effects = null,
            bool injectsCurse = false)
        {
            return new MonsterIntentPreview(
                monsterId: "m-" + coord.Q + "-" + coord.R,
                definitionId: "def",
                spawnRefId: "spawn",
                currentCoord: coord,
                predictedMoveCoord: coord,
                attackRangeCoords: attackRange,
                intentType: EnemyIntentType.Attack,
                attackPatternStatusEffects: effects != null && effects.Length > 0 ? effects : null,
                injectsCurse: injectsCurse);
        }

        private static CombatState BuildStateWithCursePattern(
            string injectStatusCardId = null,
            string[] injectStatusCardPool = null)
        {
            var config = CombatConfig.Default;
            var catalog = new MonsterCatalogDefinition(
                "curse-telegraph-monsters",
                "Curse Telegraph Monsters",
                new[]
                {
                    new MonsterCatalogEntry(
                        CombatCatalogFactory.ThreeEyeDogMonsterId,
                        "Curse Monster",
                        "test",
                        "test.curse",
                        config.EnemyMaxHp,
                        config.EnemyChaseRange,
                        10,
                        attackPatterns: new[]
                        {
                            new MonsterAttackPattern(
                                "curse-attack",
                                "Curse Attack",
                                config.EnemyAttackRange,
                                0,
                                config.EnemyAttackDamage,
                                injectStatusCardId: injectStatusCardId,
                                injectStatusCardPool: injectStatusCardPool)
                        })
                });

            var state = new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                config,
                monsterCatalog: catalog);
            typeof(CombatState)
                .GetMethod("RefreshMonsterIntentStep", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(state, null);
            return state;
        }

        private static void InjectEffect(CombatState state, StatusEffectKind kind, string unitId, int remainingTurns)
        {
            ActiveEffectProbe.Registry(state).Add(
                new ActiveEffect(EffectType.Duration, kind, unitId, remainingTurns, 0, "test"));
        }
    }
}
