using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 몬스터 은신(요괴 트랙 §4-1 · 어둑시니)과 그 위에 얹힌 성장 「어둠 먹기」(§4-2).
    ///
    /// <para>🔴 <b>가장 중요한 전제</b>: 시야 밖 몬스터는 <b>지금도</b> 안 보인다 — 표현층의 마지막 판정이
    /// <c>GetVisibility(coord) == Revealed</c> 하나이기 때문이다. 은신이 더하는 것은 <b>「시야 안인데도
    /// 안 보임」</b> 하나뿐이고, 그래서 이 파일의 술어는 안개 술어와 <b>따로</b> 서 있다. 둘을 하나로
    /// 합치면 노출 규칙이 안개 규칙을 덮어써서, 드러난 몬스터가 안개 속에서도 보이게 된다.</para>
    ///
    /// <para>🔴 <b>예고도 감춘다</b>(2026-09-01 사용자 확정 — 종전 규칙 반전). 은신 몬스터는 마커·이름표·
    /// 체력바에 더해 이동 예고와 공격 예고까지 내지 않는다. 종전에는 「예고=명중」을 지키려고 예고를
    /// 남겨 두었으나, <b>마커 없는 위험 칸</b>은 누가 겨누는지 읽을 수 없어 오히려 정보가 아니었다.
    /// 그 자리를 메우는 것이 맞는 순간의 "기습!"과 노출 4턴이다.
    /// 집행은 <c>IsMonsterIntentHidden</c>(미지와 같은 은폐 축)에 합류시켰다 — 예고 산출부와 오버레이
    /// 서명이 이미 그 술어를 보고 있어, 은폐 판정이 두 벌로 갈라지지 않는다.</para>
    ///
    /// <para>🔴 <b>연출 가시성은 안개 세 플래그로 안 잡힌다.</b> 기록(<c>MonsterActionResolutionRecord</c>)의
    /// 가시성은 셀 가시성만 보므로, 시야 안에 선 은신 몬스터는 「보이는 공격자」로 분류돼 걷기·휘두름을
    /// 연출하면서 마커만 감춰졌다 — 아무도 없는데 피해만 뜨는 화면. 그래서 기록에
    /// <c>HiddenByStealthDuringAction</c>을 따로 찍는다.</para>
    /// </summary>
    public sealed partial class CombatState : IMonsterStealthHost
    {
        // ── 4-C(2026-09-04): 본문은 MonsterStealthState.cs. 여기는 시그니처 불변 위임(사설 포함 — 리플렉션 테스트가 이름을 잡는다)과 호스트 구현.
        private MonsterStealthState stealth;

        private MonsterStealthState Stealth => stealth ??= new MonsterStealthState(this);

        public bool IsMonsterHiddenByStealth(string monsterId) => Stealth.IsMonsterHiddenByStealth(monsterId);
        private bool IsMonsterHiddenByStealth(MonsterRuntime monster) => Stealth.IsMonsterHiddenByStealth(monster);
        private bool TryGetHiddenTraitSpec(MonsterRuntime monster, out MonsterHiddenTraitSpec spec) => Stealth.TryGetHiddenTraitSpec(monster, out spec);
        private void RevealStealthMonster(MonsterRuntime monster, string sourceRef, string presentationGroupId = "") => Stealth.RevealStealthMonster(monster, sourceRef, presentationGroupId);
        private void RevealStealthMonstersInScoutArea(HexCoord center, int radius) => Stealth.RevealStealthMonstersInScoutArea(center, radius);
        private void RaiseMonsterTraitAnnouncement(MonsterRuntime monster, string traitRef, int amount = 0, bool allowWhileHidden = false, string presentationGroupId = "") => Stealth.RaiseMonsterTraitAnnouncement(monster, traitRef, amount, allowWhileHidden, presentationGroupId);
        private bool IsMonsterCoordVisible(MonsterRuntime monster) => Stealth.IsMonsterCoordVisible(monster);
        internal const string StealthAttackRevealRef = MonsterStealthState.StealthAttackRevealRef;
        internal const string StealthScoutRevealRef = MonsterStealthState.StealthScoutRevealRef;
        private void TickMonsterStealthPerTurn() => Stealth.TickMonsterStealthPerTurn();
        private void RevealStealthMonsterAfterAttack(MonsterRuntime monster, string presentationGroupId = "") => Stealth.RevealStealthMonsterAfterAttack(monster, presentationGroupId);
        private void RevealStealthMonsterOnHit(MonsterRuntime monster) => Stealth.RevealStealthMonsterOnHit(monster);
        private bool ResolvePatternSelfTeleport(MonsterRuntime monster, MonsterAttackPattern pattern, out HexCoord from, out HexCoord to) => Stealth.ResolvePatternSelfTeleport(monster, pattern, out from, out to);
        private void TeleportMonsterDeterministically(MonsterRuntime monster, string patternId, int radius) => Stealth.TeleportMonsterDeterministically(monster, patternId, radius);

        IReadOnlyList<MonsterRuntime> IMonsterStealthHost.Monsters => monsters;
        HexVisibilityRuntime IMonsterStealthHost.Visibility => visibilityRuntime;
        bool IMonsterStealthHost.TryGetEnemyGrammarEntry(MonsterRuntime monster, out MonsterCatalogEntry entry) => TryGetEnemyGrammarEntry(monster, out entry);
        void IMonsterStealthHost.UpdateOccupancy() => UpdateOccupancy();
        void IMonsterStealthHost.SetMonsterAgitationStacks(MonsterRuntime monster, int stacks) => SetMonsterAgitationStacks(monster, stacks);
        List<HexCoord> IMonsterStealthHost.CollectTeleportCandidates(HexCoord origin, int radius) => CollectTeleportCandidates(origin, radius);
        void IMonsterStealthHost.RaiseEffect(
            EffectKind kind, HexCoord center, int radius, int amount, string targetUnitId, string sourceRef,
            string sourceUnitId, string sourceActorKind, string targetActorKind,
            string sourceCardId, string sourcePatternId, int hitIndex, int hitCount, string presentationGroupId)
            => RaiseEffect(kind, center, radius, amount, targetUnitId, sourceRef, sourceUnitId, sourceActorKind, targetActorKind, sourceCardId, sourcePatternId, hitIndex, hitCount, presentationGroupId);
    }
}
