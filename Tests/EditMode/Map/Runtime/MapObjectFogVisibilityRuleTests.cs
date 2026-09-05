using NUnit.Framework;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Map.Runtime.Tests.EditMode
{
    /// <summary>
    /// 암시야 노출 규칙(2026-09-01). 「보이느냐」와 「또렷하냐」는 <b>다른 두 축</b>이고, 서비스
    /// 오브젝트는 앞에만 들어가고 뒤에는 들어가지 않는다(사용자 확정: 보이되 어둡게).
    ///
    /// <para>
    /// 이 테스트가 무는 것은 두 축이 <b>갈라져 있다</b>는 사실이다 — 규칙이 한 클래스 안에 나란히
    /// 있어서 「짝인 줄 알고」 함께 넓히기 쉽고, 그러면 잡화점이 기억석처럼 또렷해진다. 반대로
    /// 앞쪽만 되돌리면 서비스가 미탐색 칸에서 사라져 측면 배치가 통째로 무너진다.
    /// </para>
    /// </summary>
    public sealed class MapObjectFogVisibilityRuleTests
    {
        private static HexMapObjectData Make(string objectType) =>
            new HexMapObjectData(
                objectId: "test-object",
                objectType: objectType,
                objectRef: "test_prefab",
                coord: new HexCoord(0, 0));

        [TestCase("Shop")]
        [TestCase("CamperVan")]
        [TestCase("Building")]
        [TestCase("Prop")]
        [TestCase("MemoryStone")]
        public void TheseSurviveUnexploredFog(string objectType)
        {
            Assert.That(Make(objectType).IsVisibleInUnexploredFog, Is.True,
                $"{objectType}: 미탐색 칸에서 사라진다 — 플레이어가 경로를 미리 정할 수 없다.");
        }

        [TestCase("TreasureChest")]
        [TestCase("CursedGachaMachine")]
        [TestCase("Landmark")]
        public void TheseStayHiddenUntilExplored(string objectType)
        {
            // 보상·랜드마크까지 처음부터 보이면 탐색이 무의미해진다 — 서비스만 예외로 열었다는
            // 사실을 여기서 못 박는다.
            Assert.That(Make(objectType).IsVisibleInUnexploredFog, Is.False,
                $"{objectType}: 미탐색 칸에서 보인다 — 확정 범위를 넘었다.");
        }

        /// <summary>
        /// 어둠 면제는 <b>찾아가야 하는 것</b>에만 준다.
        ///
        /// <para>⚠️ 2026-09-01에는 서비스도 「보이되 어둡게」였고 이 테스트가 그 확정을 잠그고 있었다.
        /// 2026-09-05 실플레이 판정으로 <b>뒤집혔다</b> — 어두운 잡화점·캠핑카가 「찾아갈 것」으로
        /// 안 읽혔다. 되돌린 것이 실수가 아니라 판정 결과임을 기록해 둔다.</para>
        /// </summary>
        [TestCase("Shop")]
        [TestCase("CamperVan")]
        [TestCase("MemoryStone")]
        public void LandmarksAndServicesEscapeDarkening(string objectType)
        {
            Assert.That(Make(objectType).IgnoresVisibilityDarkening, Is.True,
                $"{objectType}: 어둡게 깔린다 — 멀리서 「저기 있다」가 안 읽힌다(2026-09-05 확정).");
        }

        /// <summary>
        /// 🔑 두 축은 <b>여전히 합치지 않는다</b>: 상자·인형뽑기는 미탐색 칸에서 안 보이고,
        /// 건물은 보이되 어둡다. 「보이느냐」와 「또렷하냐」를 한 덩어리로 만들면 그 저작이 사라진다.
        /// </summary>
        [TestCase("Building")]
        [TestCase("Prop")]
        [TestCase("TreasureChest")]
        [TestCase("CursedGachaMachine")]
        public void TheseStillDarkenInFog(string objectType)
        {
            Assert.That(Make(objectType).IgnoresVisibilityDarkening, Is.False,
                $"{objectType}: 어둠 면제를 받았다 — 배경·보상까지 또렷해지면 시야의 값이 사라진다.");
        }

        [Test]
        public void MemoryStoneKeepsItsDarkeningExemption()
        {
            var memoryStone = Make("MemoryStone");
            Assert.That(memoryStone.IsVisibleInUnexploredFog, Is.True);
            Assert.That(memoryStone.IgnoresVisibilityDarkening, Is.True,
                "기억석이 어둡게 깔린다 — 기존 계약이 깨졌다.");
        }
    }
}
