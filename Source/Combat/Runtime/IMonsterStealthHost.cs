using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// <see cref="MonsterStealthState"/>가 호스트에서 필요로 하는 조각(4-C). 읽기 3·서비스 4. 쓰기는 없다 —
    /// 은신 규칙이 바꾸는 것은 MonsterRuntime의 필드와 점유 캐시(호스트 서비스) 뿐이다.
    /// </summary>
    internal interface IMonsterStealthHost
    {
        IReadOnlyList<MonsterRuntime> Monsters { get; }
        HexVisibilityRuntime Visibility { get; }
        int OverallTurnNumber { get; }
        bool TryGetEnemyGrammarEntry(MonsterRuntime monster, out MonsterCatalogEntry entry);
        void UpdateOccupancy();

        /// <summary>
        /// 힘 스택의 단일 기록자(2026-09-04). 협력자가 카운터를 직접 쓰면 힘 상태이상이 따라오지
        /// 않아 배지·피해가 조용히 갈라진다 — 쓰는 문을 호스트 하나로 모은다.
        /// </summary>
        void SetMonsterAgitationStacks(MonsterRuntime monster, int stacks);
        List<HexCoord> CollectTeleportCandidates(HexCoord origin, int radius);
        void RaiseEffect(
            EffectKind kind, HexCoord center, int radius, int amount, string targetUnitId, string sourceRef,
            string sourceUnitId = "", string sourceActorKind = "", string targetActorKind = "",
            string sourceCardId = "", string sourcePatternId = "", int hitIndex = 0, int hitCount = 0, string presentationGroupId = "");
    }
}
