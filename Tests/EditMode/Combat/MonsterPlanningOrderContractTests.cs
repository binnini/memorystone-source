using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 🔴🔴 <b>「예고한 대로만 실행」 — 몬스터는 예고한 칸에 선다.</b>
    ///
    /// 계획 §3 P4의 ⚠️가 「`IMonsterPlanningContext.cs:30`(계획부·해소부 동일 정렬)은 파이프라인으로
    /// 안 잡히니 별도 행동 테스트로 잠근다」고 지시한 자리다. 잠그러 와서 <b>지시의 전제가 틀렸음을
    /// 발견했다</b> — 아래에 그대로 적어 둔다.
    ///
    /// 🔎 <b>실측(T7-2와 같은 방식)</b>: 해소부만 계획부와 <b>반대 순서</b>로 돌게 바꿔 봐도
    /// 아무것도 달라지지 않는다. 이유는 계획부가 ①현재 몬스터가 서 있는 칸으로는 애초에 계획하지 않고
    /// ②세운 목적지를 <c>reservedDestinations</c>에 <b>예약</b>해 뒤 몬스터가 피하게 하기 때문이다.
    /// 그래서 모든 계획 목적지는 <b>서로 다르고 비어 있으며</b>, 해소는 이미 정해진 칸으로 옮기기만
    /// 하므로 순서가 결과를 바꿀 여지가 없다.
    /// ⇒ <b>약속을 지키는 장치는 「같은 정렬」이 아니라 「목적지 예약」이다.</b> 주석이 지목한 곳과
    /// 실제로 일하는 곳이 다르다(이 트랙에서 세 번째로 나온 형태다).
    ///
    /// 그래서 이 테스트는 정렬을 재지 않고 <b>약속 자체</b>를 잰다: 플레이어가 보는 예고
    /// (<c>GetMonsterIntentPreviews</c>의 <c>PredictedMoveCoord</c>)를 집행 직전에 찍어 두고,
    /// 이동 해소 뒤 실제 좌표와 맞춘다. 내부 정렬을 들여다보면 같은 코드를 두 번 부르는 것에 불과하다.
    ///
    /// ✅ <b>물림 실증</b>: 계획부의 목적지 예약 적립을 지우면 두 몬스터가 <b>같은 칸을 예고</b>하고
    /// (고유 목적지 4 → 3), 그중 하나는 반드시 예고와 다른 곳에 서게 된다 — 이 테스트가 그 자리에서 빨개진다.
    /// </summary>
    public sealed class MonsterPlanningOrderContractTests
    {
        /// <summary>
        /// 1칸 회랑에 추격자 둘을 세운다 — 앞 몬스터가 비켜 줘야 뒤 몬스터가 들어갈 자리가 생기므로
        /// <b>목적지 경쟁이 반드시 일어난다</b>. 두 단계가 같은 순서를 쓰면 예고대로 줄줄이 전진하고,
        /// 순서가 갈리면 뒤 몬스터가 먼저 움직이려다 막혀 예고와 다른 칸에 남는다.
        /// </summary>
        [Test]
        public void EveryMonsterEndsOnTheTileItsIntentPreviewPromised()
        {
            // 플레이어를 둘러싼 거리 2 고리에 넷을 세운다 — 인접 칸이 모자라 여럿이 같은 칸을
            // 노리므로 계획부의 목적지 예약이 반드시 발동한다.
            var state = CombatStateFixture.Arena(4)
                .WithMonsters(
                    new MonsterConfig("ring-a", new HexCoord(2, 0), 20),
                    new MonsterConfig("ring-b", new HexCoord(2, -1), 20),
                    new MonsterConfig("ring-c", new HexCoord(2, -2), 20),
                    new MonsterConfig("ring-d", new HexCoord(1, -2), 20))
                .Build();

            // 플레이어 이동을 소진해 예고를 확정시킨다(집행 직전 상태 = 플레이어가 보는 화면).
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True, state.LastFailureReason);
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);

            var promised = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .ToDictionary(preview => preview.MonsterId, preview => preview.PredictedMoveCoord);
            Assert.That(promised, Has.Count.EqualTo(4), "전제: 넷 다 예고를 낸다.");
            Assert.That(
                promised.Values.Distinct().Count(), Is.EqualTo(promised.Count),
                "🔴 전제이자 계약: 예고된 목적지에 중복이 없다 — 둘이 같은 칸을 예고하면 하나는 반드시 "
                + "예고와 다른 곳에 서게 된다. 이 중복을 막는 것이 계획부의 목적지 예약이다.");
            var movers = promised.Count(pair => pair.Value != StartCoordOf(pair.Key));
            Assert.That(movers, Is.GreaterThan(1),
                "전제: 둘 이상이 실제로 움직일 예정이어야 경쟁이 관찰된다.");

            state.ResolveMonsterMovement();

            foreach (var monster in state.Monsters)
            {
                Assert.That(
                    monster.Coord, Is.EqualTo(promised[monster.Id]),
                    $"{monster.Id}이(가) 예고한 칸이 아닌 곳에 섰다 — 계획부와 해소부가 다른 순서로 돌면 "
                    + "앞 몬스터가 비켜 주기 전에 뒤 몬스터가 움직여 예약이 어긋난다.");
            }
        }

        private static HexCoord StartCoordOf(string monsterId)
        {
            switch (monsterId)
            {
                case "ring-a": return new HexCoord(2, 0);
                case "ring-b": return new HexCoord(2, -1);
                case "ring-c": return new HexCoord(2, -2);
                case "ring-d": return new HexCoord(1, -2);
                default: throw new System.ArgumentOutOfRangeException(nameof(monsterId), monsterId);
            }
        }
    }
}
