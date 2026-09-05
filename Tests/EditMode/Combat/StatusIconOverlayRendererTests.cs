using System.Collections.Generic;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class StatusIconOverlayRendererTests
    {
        private GameObject rendererGo;
        private StatusIconOverlayRenderer renderer;
        private readonly List<Object> spawnedAssets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            rendererGo = new GameObject("StatusIconOverlayRenderer");
            renderer = rendererGo.AddComponent<StatusIconOverlayRenderer>();
            renderer.Configure(new FakeProjector());
            // Keep the test off the texture/TMP path: real sprites + no white-background flood fill.
            renderer.SetRemoveWhiteBackgroundForTesting(false);
            renderer.SetEffectSpritesForTesting(CreateSpriteSet());
        }

        [TearDown]
        public void TearDown()
        {
            if (rendererGo != null)
            {
                Object.DestroyImmediate(rendererGo);
            }
            foreach (var asset in spawnedAssets)
            {
                if (asset != null)
                {
                    Object.DestroyImmediate(asset);
                }
            }
            spawnedAssets.Clear();
        }

        [Test]
        public void ApplyAnnotationsSpawnsOneIconPerAnnotation()
        {
            renderer.ApplyAnnotations(new[]
            {
                new CombatOverlayIconAnnotation(new HexCoord(1, 0), new[] { StatusEffectKind.Poison }),
                new CombatOverlayIconAnnotation(new HexCoord(2, 0), new[] { StatusEffectKind.Poison, StatusEffectKind.Stun })
            });

            Assert.That(renderer.ActiveIconCount, Is.EqualTo(2));
            Assert.That(rendererGo.transform.childCount, Is.EqualTo(2));
        }

        [Test]
        public void ClearRemovesAllIcons()
        {
            renderer.ApplyAnnotations(new[]
            {
                new CombatOverlayIconAnnotation(new HexCoord(1, 0), new[] { StatusEffectKind.Poison })
            });
            Assert.That(renderer.ActiveIconCount, Is.EqualTo(1));

            renderer.Clear();

            Assert.That(renderer.ActiveIconCount, Is.EqualTo(0));
            Assert.That(rendererGo.transform.childCount, Is.EqualTo(0));
        }

        [Test]
        public void EmptyAnnotationsClearExistingIcons()
        {
            renderer.ApplyAnnotations(new[]
            {
                new CombatOverlayIconAnnotation(new HexCoord(1, 0), new[] { StatusEffectKind.Poison })
            });

            renderer.ApplyAnnotations(System.Array.Empty<CombatOverlayIconAnnotation>());

            Assert.That(renderer.ActiveIconCount, Is.EqualTo(0));
            Assert.That(rendererGo.transform.childCount, Is.EqualTo(0));
        }

        [Test]
        public void MultiEffectIconCyclesIndexAndWraps()
        {
            renderer.ApplyAnnotations(new[]
            {
                new CombatOverlayIconAnnotation(
                    new HexCoord(0, 0),
                    new[] { StatusEffectKind.Poison, StatusEffectKind.Slow, StatusEffectKind.Stun })
            });
            Assert.That(renderer.CurrentCycleIndices, Is.EqualTo(new[] { 0 }));

            renderer.AdvanceCycleForTesting();
            Assert.That(renderer.CurrentCycleIndices, Is.EqualTo(new[] { 1 }));

            renderer.AdvanceCycleForTesting();
            Assert.That(renderer.CurrentCycleIndices, Is.EqualTo(new[] { 2 }));

            renderer.AdvanceCycleForTesting();
            Assert.That(renderer.CurrentCycleIndices, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void SingleEffectIconDoesNotCycle()
        {
            renderer.ApplyAnnotations(new[]
            {
                new CombatOverlayIconAnnotation(new HexCoord(0, 0), new[] { StatusEffectKind.Poison })
            });

            renderer.AdvanceCycleForTesting();

            Assert.That(renderer.CurrentCycleIndices, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void KnockbackOnlyAnnotationSpawnsIcon()
        {
            renderer.SetIconCatalogForTesting(CreateKnockbackCatalog());

            renderer.ApplyAnnotations(new[]
            {
                new CombatOverlayIconAnnotation(new HexCoord(1, 0), System.Array.Empty<StatusEffectKind>(), knockbackDistance: 1)
            });

            Assert.That(renderer.ActiveIconCount, Is.EqualTo(1));
            Assert.That(rendererGo.transform.childCount, Is.EqualTo(1));
        }

        [Test]
        public void StatusPlusKnockbackCyclesThroughBothIcons()
        {
            renderer.SetIconCatalogForTesting(CreateKnockbackCatalog());

            renderer.ApplyAnnotations(new[]
            {
                new CombatOverlayIconAnnotation(new HexCoord(0, 0), new[] { StatusEffectKind.Poison }, knockbackDistance: 1)
            });
            Assert.That(renderer.CurrentCycleIndices, Is.EqualTo(new[] { 0 }));

            renderer.AdvanceCycleForTesting();
            Assert.That(renderer.CurrentCycleIndices, Is.EqualTo(new[] { 1 }));

            renderer.AdvanceCycleForTesting();
            Assert.That(renderer.CurrentCycleIndices, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void IconStaysUprightAndUnmirroredAcrossCameraYaw()
        {
            // 2026-08-20 #15: 아이콘은 타일에 눕힌 데칼로 남되 카메라 yaw를 따라 돌아야 한다.
            // 예전 월드 고정 (90,0,0)은 ①yaw에 따라 화면상 회전 ②앞면이 바닥을 향해 뒷면(좌우 반전)
            // 노출이었다. 앞면(+Z)이 위를 보고, 아이콘의 위(+Y)가 카메라 수평 전방과 정렬되는지 잰다.
            var cameraGo = new GameObject("StatusIconYawCamera");
            try
            {
                var camera = cameraGo.AddComponent<Camera>();
                renderer.ApplyAnnotations(new[]
                {
                    new CombatOverlayIconAnnotation(new HexCoord(0, 0), new[] { StatusEffectKind.Poison })
                });

                foreach (var yaw in new[] { 0f, 90f, 180f, 270f })
                {
                    camera.transform.position = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 8f, -8f);
                    camera.transform.rotation = Quaternion.Euler(45f, yaw, 0f);
                    renderer.SetHoverCamera(camera); // SetCamera → AlignNow — 에디트 모드에선 LateUpdate가 안 돈다.

                    var icon = rendererGo.transform.GetChild(0);
                    Assert.That(Vector3.Dot(icon.forward, Vector3.up), Is.GreaterThan(0.99f),
                        $"yaw {yaw}: 앞면이 위를 봐야 한다(뒷면 반전 금지).");
                    var flatForward = camera.transform.forward;
                    flatForward.y = 0f;
                    Assert.That(Vector3.Dot(icon.up, flatForward.normalized), Is.GreaterThan(0.99f),
                        $"yaw {yaw}: 아이콘의 위가 화면 위쪽(카메라 수평 전방)과 정렬돼야 한다.");
                }
            }
            finally
            {
                Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void HoveredTileFreezesItsCycleWhileOthersKeepCycling()
        {
            // #15: 호버 중 순환 정지 — 얼림은 호버된 칸에만 걸린다.
            var cameraGo = new GameObject("StatusIconHoverCamera");
            try
            {
                var camera = cameraGo.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.transform.rotation = Quaternion.identity;

                renderer.ApplyAnnotations(new[]
                {
                    new CombatOverlayIconAnnotation(new HexCoord(0, 0), new[] { StatusEffectKind.Poison, StatusEffectKind.Stun }),
                    new CombatOverlayIconAnnotation(new HexCoord(3, 0), new[] { StatusEffectKind.Poison, StatusEffectKind.Slow })
                });

                var screenPos = (Vector2)camera.WorldToScreenPoint(new Vector3(0f, 0.03f, 0f));
                Assert.That(renderer.TryGetHoveredTooltip(camera, screenPos, out _), Is.True, "전제: 첫 칸 호버.");

                renderer.AdvanceCycleForTesting();
                Assert.That(renderer.CurrentCycleIndices, Is.EqualTo(new[] { 0, 1 }),
                    "호버된 칸은 얼고, 다른 칸은 계속 돈다.");

                renderer.ExpireHoverForTesting();
                renderer.AdvanceCycleForTesting();
                Assert.That(renderer.CurrentCycleIndices, Is.EqualTo(new[] { 1, 0 }),
                    "호버가 풀리면 다시 돈다.");
            }
            finally
            {
                Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void RebuildKeepsSurvivingCoordCycleIndex()
        {
            // #15: 오버레이 갱신(Rebuild)이 살아남는 좌표의 순환 위치를 0으로 되감으면, 호버로 읽던
            // 아이콘이 갱신 한 번에 다른 그림으로 바뀐다.
            var annotations = new[]
            {
                new CombatOverlayIconAnnotation(
                    new HexCoord(0, 0),
                    new[] { StatusEffectKind.Poison, StatusEffectKind.Slow, StatusEffectKind.Stun })
            };
            renderer.ApplyAnnotations(annotations);
            renderer.AdvanceCycleForTesting();
            Assert.That(renderer.CurrentCycleIndices, Is.EqualTo(new[] { 1 }));

            renderer.ApplyAnnotations(annotations);

            Assert.That(renderer.CurrentCycleIndices, Is.EqualTo(new[] { 1 }),
                "같은 좌표가 살아남으면 순환 위치가 보존돼야 한다.");
        }

        [Test]
        public void TryGetHoveredStatusUsesScreenSpaceIconPosition()
        {
            var cameraGo = new GameObject("StatusIconHoverCamera");
            try
            {
                var camera = cameraGo.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.transform.rotation = Quaternion.identity;

                renderer.ApplyAnnotations(new[]
                {
                    new CombatOverlayIconAnnotation(new HexCoord(0, 0), new[] { StatusEffectKind.Poison })
                });

                var screenPos = (Vector2)camera.WorldToScreenPoint(new Vector3(0f, 0.03f, 0f));

                Assert.That(renderer.TryGetHoveredStatus(camera, screenPos, out var effect), Is.True);
                Assert.That(effect.Kind, Is.EqualTo(StatusEffectKind.Poison));
                Assert.That(renderer.TryGetHoveredStatus(camera, screenPos + Vector2.right * 200f, out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void TryGetHoveredTooltipReturnsKnockbackPayload()
        {
            renderer.SetIconCatalogForTesting(CreateKnockbackCatalog());
            var cameraGo = new GameObject("StatusIconHoverCamera");
            try
            {
                var camera = cameraGo.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.transform.rotation = Quaternion.identity;

                renderer.ApplyAnnotations(new[]
                {
                    new CombatOverlayIconAnnotation(new HexCoord(0, 0), System.Array.Empty<StatusEffectKind>(), knockbackDistance: 1)
                });

                var screenPos = (Vector2)camera.WorldToScreenPoint(new Vector3(0f, 0.03f, 0f));

                Assert.That(renderer.TryGetHoveredTooltip(camera, screenPos, out var payload), Is.True);
                Assert.That(payload.IsKnockback, Is.True);
                Assert.That(renderer.TryGetHoveredStatus(camera, screenPos, out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void SafeZoneConfirmedAnnotationIsHoverOnlyAndYieldsToDrawnIcons()
        {
            // §28 W5 후속 T1: 확정 안전지대 주석은 그리지 않는 호버 전용 그룹을 세운다 — 초록 확정
            // 채움이 이미 시각 신호라 아이콘을 겹치지 않되, 호버 툴팁 대상은 있어야 한다.
            var cameraGo = new GameObject("StatusIconHoverCamera");
            try
            {
                var camera = cameraGo.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.transform.rotation = Quaternion.identity;

                renderer.ApplyAnnotations(new[]
                {
                    new CombatOverlayIconAnnotation(
                        new HexCoord(0, 0),
                        System.Array.Empty<StatusEffectKind>(),
                        safeZoneConfirmed: true)
                });

                Assert.That(renderer.ActiveIconCount, Is.EqualTo(1), "호버 전용 그룹이 서야 한다.");

                var screenPos = (Vector2)camera.WorldToScreenPoint(new Vector3(0f, 0.03f, 0f));
                Assert.That(renderer.TryGetHoveredTooltip(camera, screenPos, out var payload), Is.True);
                Assert.That(payload.IsSafeZoneConfirmed, Is.True);

                // 확정 칸에 그리는 표식(상태이상 예고)이 얹히면 그리는 쪽이 우선한다 — 빈 슬롯이
                // 사이클에 끼면 아이콘이 1초마다 통째로 깜빡여 보인다.
                renderer.ApplyAnnotations(new[]
                {
                    new CombatOverlayIconAnnotation(
                        new HexCoord(0, 0),
                        new[] { StatusEffectKind.Poison },
                        safeZoneConfirmed: true)
                });

                Assert.That(renderer.TryGetHoveredTooltip(camera, screenPos, out var mixed), Is.True);
                Assert.That(mixed.IsSafeZoneConfirmed, Is.False, "그리는 표식이 있으면 확정 툴팁은 양보한다.");
                Assert.That(mixed.Effect.Kind, Is.EqualTo(StatusEffectKind.Poison));
            }
            finally
            {
                Object.DestroyImmediate(cameraGo);
            }
        }

        private StatusEffectIconCatalog CreateKnockbackCatalog()
        {
            var catalog = StatusEffectIconCatalog.CreateForTests();
            catalog.SetKnockbackSpriteForTests(CreateSprite());
            spawnedAssets.Add(catalog);
            return catalog;
        }

        private Sprite[] CreateSpriteSet()
        {
            var sprites = new Sprite[7];
            for (var i = 0; i < sprites.Length; i++)
            {
                sprites[i] = CreateSprite();
            }
            return sprites;
        }

        private Sprite CreateSprite()
        {
            var tex = new Texture2D(2, 2);
            spawnedAssets.Add(tex);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
            spawnedAssets.Add(sprite);
            return sprite;
        }

        private sealed class FakeProjector : IHexMapOverlaySurfaceProjector
        {
            public Vector3 ProjectOverlaySurface(HexCoord coord) => new Vector3(coord.Q, 0f, coord.R);
        }
    }
}
