#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 상태이상 바닥 링의 몸집 비례 계수(2026-08-20 WS-2) 계약.
    /// </summary>
    public sealed class StatusLoopBodyScaleTests
    {
        // 🔑 기준 체구 불변 잠금. 계수는 「플레이어 대비 몇 배」이므로 플레이어에서 정확히 1이어야
        // 하고, 그래야 사용자가 튜닝해 둔 현행 플레이어 화면이 이 수리로 흔들리지 않는다.
        [Test]
        public void ResolveReturnsOneForTheReferenceBody()
        {
            Assert.That(StatusLoopBodyScale.Resolve(2.042f, 2.042f), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void ResolveScalesLinearlyWithFootprintDiameter()
        {
            Assert.That(StatusLoopBodyScale.Resolve(2.586f, 2.042f), Is.EqualTo(1.2664f).Within(1e-3f));
            Assert.That(StatusLoopBodyScale.Resolve(1.389f, 2.042f), Is.EqualTo(0.6802f).Within(1e-3f));
        }

        // 실측 양 끝: 결계 구슬 0.605(0.30배)와 불가살 3페이즈 6.107(2.99배). 클램프가 없으면
        // 전자는 링이 점으로 사라지고 후자는 아레나를 덮는다.
        [Test]
        public void ResolveClampsBothExtremes()
        {
            Assert.That(StatusLoopBodyScale.Resolve(0.605f, 2.042f), Is.EqualTo(StatusLoopBodyScale.MinFactor).Within(1e-5f));
            Assert.That(StatusLoopBodyScale.Resolve(6.107f, 2.042f), Is.EqualTo(StatusLoopBodyScale.MaxFactor).Within(1e-5f));
        }

        // 못 잰 경우가 화면에서 크기 폭주로 나타나면 안 된다 — 현행 크기(1)로 물러난다.
        [Test]
        public void ResolveFallsBackToOneWhenEitherMeasurementIsMissing()
        {
            Assert.That(StatusLoopBodyScale.Resolve(0f, 2.042f), Is.EqualTo(1f));
            Assert.That(StatusLoopBodyScale.Resolve(2.042f, 0f), Is.EqualTo(1f));
            Assert.That(StatusLoopBodyScale.Resolve(-3f, 2.042f), Is.EqualTo(1f));
        }

        /// <summary>
        /// 같은 프리팹을 공유하는 루프 엔트리는 저작값이 갈라지면 안 된다(엔트리별 드리프트 금지).
        /// 디버프 5종·버프 3종이 각각 한 프리팹을 돌려 쓰므로, 한 엔트리만 손보면 같은 그림이 상태이상에
        /// 따라 다른 자리에 뜬다.
        /// </summary>
        [Test]
        public void GroundLoopEntriesSharingAPrefabShareTheirAuthoredPlacement()
        {
            var catalog = Resources.Load<EffectVfxCatalog>("Combat/DefaultEffectVfxCatalog");
            Assert.That(catalog, Is.Not.Null, "Combat/DefaultEffectVfxCatalog 를 찾지 못했다.");

            var groundLoops = catalog.Entries
                .Where(entry => entry != null
                                && entry.Loop
                                && entry.LoopAnchor == CharacterVfxAnchorKind.Ground
                                && entry.Prefabs.Length > 0
                                && entry.Prefabs[0] != null)
                .ToArray();
            Assert.That(groundLoops, Is.Not.Empty);

            foreach (var group in groundLoops.GroupBy(entry => entry.Prefabs[0]))
            {
                var first = group.First();
                foreach (var entry in group)
                {
                    Assert.That(
                        entry.PositionOffset,
                        Is.EqualTo(first.PositionOffset),
                        $"{entry.CueId} 와 {first.CueId} 는 같은 프리팹({group.Key.name})을 쓰는데 오프셋이 다르다.");
                    Assert.That(
                        entry.ScaleMultiplier,
                        Is.EqualTo(first.ScaleMultiplier).Within(1e-4f),
                        $"{entry.CueId} 와 {first.CueId} 는 같은 프리팹({group.Key.name})을 쓰는데 배율이 다르다.");
                }
            }
        }

        /// <summary>
        /// 버프와 디버프는 <b>각각</b> 저작한다(사용자 요구 2026-08-20). 두 계열이 같은 값으로 되돌아가면
        /// 「각각」이 조용히 사라진 것이다.
        /// </summary>
        [Test]
        public void BuffAndDebuffGroundLoopsAreAuthoredSeparately()
        {
            var catalog = Resources.Load<EffectVfxCatalog>("Combat/DefaultEffectVfxCatalog");
            Assert.That(catalog, Is.Not.Null);

            Assert.That(TryFindGroundLoop(catalog, StatusEffectKind.Poison, out var debuff), Is.True);
            Assert.That(TryFindGroundLoop(catalog, StatusEffectKind.Strength, out var buff), Is.True);
            Assert.That(debuff.PositionOffset.y, Is.Not.EqualTo(buff.PositionOffset.y).Within(1e-4f));
        }

        private static bool TryFindGroundLoop(EffectVfxCatalog catalog, StatusEffectKind kind, out EffectVfxCatalog.Entry entry)
        {
            entry = catalog.Entries.FirstOrDefault(candidate =>
                candidate != null
                && candidate.Loop
                && candidate.StatusKind == kind
                && candidate.LoopAnchor == CharacterVfxAnchorKind.Ground);
            return entry != null;
        }
    }
}
#endif
