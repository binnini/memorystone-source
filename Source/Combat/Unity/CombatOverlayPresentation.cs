using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Unity;

namespace SeoulPlayup.Combat.Unity
{
    public sealed class CombatOverlayPresentation
    {
        /// <summary>
        /// 호버 경로·튜토리얼이 <c>Show*/Clear*</c>로 직접 수명을 쥐는 레이어(빌더가 내보내지 않는다).
        /// 전술 rebuild가 건드리면 「호버 중인데 다음 refresh에 꺼지는」 사고가 나므로 여기 명시한다.
        /// </summary>
        public static readonly HexOverlayLayer[] HoverOwnedLayers =
        {
            HexOverlayLayer.PlayerHoverMove,
            HexOverlayLayer.PlayerHoverAction,
            HexOverlayLayer.FieldObjectRange,
            HexOverlayLayer.TutorialTarget
        };

        /// <summary>
        /// 빌더가 매 refresh마다 다시 내보내는 <b>전술 레이어</b> — 이 목록에 있는 레이어만 「이번 refresh에
        /// 안 나왔으면 지운다」 루프를 탄다. 2026-09-05 후속 #2까지 <b>다섯 번째</b>로 새 레이어(철조각 폭발
        /// 범위 호버)가 손 목록에서 빠져 화면에 남았다(그 전에 결계·점유·취약부위/전멸기·특성범위/부서진땅).
        /// 그래서 이제는 손으로 늘리지 않는다: <b>열거형 전체에서 호버·튜토리얼이 직접 켜고 끄는 레이어
        /// (<see cref="HoverOwnedLayers"/>)만 뺀 나머지</b>가 전술 레이어다. 새 레이어는 기본이 「전술」이라
        /// 빼먹을 수가 없고, 호버 소유로 두고 싶으면 그쪽 목록에 명시해야 한다(반대 방향의 실수는 「호버가
        /// 켠 레이어가 다음 refresh에 지워진다」로 즉시 눈에 띈다).
        /// </summary>
        public static readonly HexOverlayLayer[] TacticalLayers = System.Enum
            .GetValues(typeof(HexOverlayLayer))
            .Cast<HexOverlayLayer>()
            .Where(layer => System.Array.IndexOf(HoverOwnedLayers, layer) < 0)
            .ToArray();

        private readonly IReadOnlyList<CombatOverlayLayerState> layers;
        private readonly IReadOnlyList<CombatOverlayIconAnnotation> annotations;

        public CombatOverlayPresentation(IEnumerable<CombatOverlayLayerState> layers)
            : this(layers, null)
        {
        }

        public CombatOverlayPresentation(
            IEnumerable<CombatOverlayLayerState> layers,
            IEnumerable<CombatOverlayIconAnnotation> annotations)
        {
            // 같은 레이어가 여러 번 들어오면 <b>좌표를 합친다</b>(예전에는 마지막 것만 남기고 나머지를
            // 조용히 버렸다). MonsterAttackIntent는 "이 칸은 위험"이라는 같은 어휘를 세 소스 — 주기 함정
            // 예고 · 전멸기 예고 · 몬스터 공격 예고 — 가 의도적으로 공유하므로 중복 입력이 정상이고,
            // 마지막 하나만 살아남는 바람에 전멸기 예고가 화면에 아예 도달하지 못했다.
            // 스타일은 먼저 들어온 것을 쓴다(같은 레이어면 ResolveDefaultStyle 결과가 같다).
            this.layers = layers != null
                ? layers
                    .GroupBy(layer => layer.Layer)
                    .Select(group => new CombatOverlayLayerState(
                        group.Key,
                        group.SelectMany(layer => layer.Coords),
                        group.First().Style))
                    .ToArray()
                : System.Array.Empty<CombatOverlayLayerState>();
            this.annotations = annotations != null
                ? annotations.ToArray()
                : System.Array.Empty<CombatOverlayIconAnnotation>();
        }

        public IReadOnlyList<CombatOverlayLayerState> Layers => layers;

        /// <summary>
        /// Per-tile icon overlay payloads (status effects telegraphed on a tile). Consumed by a
        /// dedicated icon renderer, separate from the fill/boundary <see cref="Layers"/> channel.
        /// </summary>
        public IReadOnlyList<CombatOverlayIconAnnotation> Annotations => annotations;
    }
}
