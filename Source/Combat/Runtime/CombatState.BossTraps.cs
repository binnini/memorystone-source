using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 런타임(보스 배치) 함정의 보드 측 규칙(§21.5 — 결정 4). 함정의 유일한 저작 표면이던
    /// <c>HexSparseMapAuthoringSource.trapRefs</c>(맵 데이터 = 불변) 위에 전투가 소유하는 가변 목록을
    /// 한 겹 얹는다. 트리거·주기 발동·발견·해체·소진(<c>consumedTrapIds</c>)은 전부
    /// <see cref="AllTrapRefs"/>를 읽으므로 두 출처의 함정이 완전히 같은 규칙을 탄다.
    ///
    /// <para>🔴 <b>보스 배치 함정도 정찰 전까지 숨긴다</b>(2026-09-03 사용자 확정 — 종전 "배치 즉시
    /// 발견"[§21.5 결정 4]의 번복, DECISIONS.md 참조). 정적 저작 함정과 같은 "정찰한 것만 보인다"
    /// 계약으로 통일한다. 단 <see cref="HexVisibilityRuntime.RevealTrapsInArea"/>는 맵 저작 함정만
    /// 순회하므로, 정찰 경로는 <see cref="RevealRuntimeTrapsInArea"/>를 함께 불러야 런타임 함정이
    /// 발견 축에서 조용히 빠지지 않는다. §21.5가 걱정한 "숨은 함정이 안전지대 경로를 막는다"는
    /// 함정 피해가 경상(착수값 2)이라 수용한다 — 밟으면 아프지만 회피 보장이 죽지는 않는다.</para>
    /// </summary>
    public sealed partial class CombatState
    {
        // ── 3-B(2026-09-04): 본문은 BossEncounterState.Traps.cs. 여기는 시그니처 불변 위임과 중첩 타입만 남는다.
        public string LastBossTrapVolleyReport => Boss.LastBossTrapVolleyReport;
        internal int SpawnBossTrapVolley( string ownerBossUnitId, int count, int ringRadius, int minSpacing, HexTrapEffectData effect) => Boss.SpawnBossTrapVolley(ownerBossUnitId, count, ringRadius, minSpacing, effect);
        internal void GrantBossGuardBlock(MonsterRuntime boss, int amount) => Boss.GrantBossGuardBlock(boss, amount);
        public string LastBossArenaTrapReport => Boss.LastBossArenaTrapReport;
        internal int SpawnBossArenaTraps(string ownerBossUnitId, IReadOnlyList<HexTrapEffectData> effects, int minSpacing) => Boss.SpawnBossArenaTraps(ownerBossUnitId, effects, minSpacing);
        internal int CountLivingMonstersOfDefinition(string definitionId) => monsters.Count(monster => !monster.Combatant.IsDead && string.Equals(monster.DefinitionId, definitionId, StringComparison.Ordinal));
        internal int NextBossPropRandom(int exclusiveMax) => Boss.NextBossPropRandom(exclusiveMax);
        internal void AnnounceBoss(MonsterRuntime boss, string sourceRef, int amount) => Boss.AnnounceBoss(boss, sourceRef, amount);

        /// <summary>함정 배치 계열 연출 <c>sourceRef</c> 스탬프(<see cref="BossAnnihilationSourceRefs"/>와 같은 규약).</summary>
        public static class BossTrapSourceRefs
        {
            /// <summary>배치 턴의 방어막 획득(보스 좌표 · §21.8 제안 4).</summary>
            public const string GuardBlock = "boss.trap.guard";
        }
    }
}
