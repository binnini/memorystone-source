using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 보스 기물(철조각)의 <b>표현용 사건 기록</b>과 흡수 폭발. 기물의 수명 관리 자체는
    /// <c>CombatState.MonsterSpawn.cs</c>에 있고, 여기는 "이번 결의에서 무엇이 일어났는가"를
    /// 표현 계층에 넘기는 투영과, 흡수에 딸린 규칙 효과(반경 피해)를 갖는다.
    ///
    /// <see cref="LastBossPhaseTransitions"/>와 같은 규약이다: 리스트는 기믹 스텝 진입 시 비워지므로
    /// 항상 <b>직전 결의</b>만 담고, 타임라인 조립기가 결의 직후에 읽어 연출 비트로 바꾼다.
    /// </summary>
    public sealed partial class CombatState
    {
        // ── 3-B(2026-09-04): 본문은 BossEncounterState.Props.cs. 여기는 시그니처 불변 위임과 중첩 타입만 남는다.
        // 4-A: 기물 수명(옛 MonsterSpawn.cs) — 기믹 컨텍스트·테스트·랩이 부르는 표면만 위임으로.
        internal IReadOnlyList<BossPropView> GetLivingBossProps(string ownerBossUnitId, string propDefinitionId) => Boss.GetLivingBossProps(ownerBossUnitId, propDefinitionId);
        internal bool AbsorbBossProp(string ownerBossUnitId, HexCoord bossCoord, string propUnitId, int blastRadius, int blastDamage) => Boss.AbsorbBossProp(ownerBossUnitId, bossCoord, propUnitId, blastRadius, blastDamage);
        internal void ConfigureBossPropRandomForTests(System.Random random) => Boss.ConfigureBossPropRandom(random);
        public string LastBossPropVolleyReport => Boss.LastBossPropVolleyReport;
        internal int SpawnBossPropVolley(string ownerBossUnitId, string propDefinitionId, int count, int ringRadius, int minSpacing) => Boss.SpawnBossPropVolley(ownerBossUnitId, propDefinitionId, count, ringRadius, minSpacing);
        public IReadOnlyList<BossPropVolleyCast> LastBossPropVolleyCasts => Boss.LastBossPropVolleyCasts;
        internal int PlanBossPropVolley(string ownerBossUnitId, int count, int ringRadius, int minSpacing) => Boss.PlanBossPropVolley(ownerBossUnitId, count, ringRadius, minSpacing);
        /// <summary>다음 몬스터 페이즈에 철조각이 놓일 칸(살포 예고 · 2026-09-05). 오버레이 빌더가 읽는다.</summary>
        public IReadOnlyList<HexCoord> GetBossPropVolleyTelegraphCells() => Boss.GetBossPropVolleyTelegraphCells();
        public IReadOnlyList<BossPropAbsorption> LastBossPropAbsorptions => Boss.LastBossPropAbsorptions;
        private void RecordBossPropVolleyCast(string bossUnitId, HexCoord bossCoord, IReadOnlyList<HexCoord> placedCoords) => Boss.RecordBossPropVolleyCast(bossUnitId, bossCoord, placedCoords);
        private void RecordBossPropAbsorption( string bossUnitId, HexCoord bossCoord, string propUnitId, HexCoord propCoord, int blastRadius, int blastDamage) => Boss.RecordBossPropAbsorption(bossUnitId, bossCoord, propUnitId, propCoord, blastRadius, blastDamage);
        public bool TryGetBossPropAbsorptionCountdown(string propUnitId, out int turnsRemaining, out int maturityTurns) => Boss.TryGetBossPropAbsorptionCountdown(propUnitId, out turnsRemaining, out maturityTurns);
        public bool TryGetBossPropBlastAuthoring(string propUnitId, out int blastRadius, out int blastDamage) => Boss.TryGetBossPropBlastAuthoring(propUnitId, out blastRadius, out blastDamage);
        private bool IsMonsterAttackReplacedByBossMechanic(MonsterRuntime monster) => Boss.IsMonsterAttackReplacedByBossMechanic(monster);
        internal IReadOnlyList<string> GetBossActionReplacingMechanicIds(MonsterRuntime monster) => Boss.GetBossActionReplacingMechanicIds(monster);
    }

    /// <summary>기물 살포 한 번의 공개 투영(표현 전용).</summary>
    public readonly struct BossPropVolleyCast
    {
        public BossPropVolleyCast(string bossUnitId, HexCoord bossCoord, IReadOnlyList<HexCoord> placedCoords)
        {
            BossUnitId = bossUnitId ?? string.Empty;
            BossCoord = bossCoord;
            PlacedCoords = placedCoords ?? Array.Empty<HexCoord>();
        }

        public string BossUnitId { get; }

        /// <summary>살포 시점의 보스 위치(캐스트 연출의 프레이밍·VFX 기준점).</summary>
        public HexCoord BossCoord { get; }

        /// <summary>실제로 기물이 놓인 칸들. 요청보다 적을 수 있다(자리 부족).</summary>
        public IReadOnlyList<HexCoord> PlacedCoords { get; }
    }

    /// <summary>기물 흡수 한 건의 공개 투영(표현 전용).</summary>
    public readonly struct BossPropAbsorption
    {
        public BossPropAbsorption(
            string bossUnitId,
            HexCoord bossCoord,
            string propUnitId,
            HexCoord propCoord,
            IReadOnlyList<HexCoord> blastCoords,
            int damageAppliedToPlayer)
        {
            BossUnitId = bossUnitId ?? string.Empty;
            BossCoord = bossCoord;
            PropUnitId = propUnitId ?? string.Empty;
            PropCoord = propCoord;
            BlastCoords = blastCoords ?? Array.Empty<HexCoord>();
            DamageAppliedToPlayer = damageAppliedToPlayer;
        }

        public string BossUnitId { get; }

        /// <summary>흡수 시점의 보스 위치. 파티클이 빨려 들어갈 목적지다.</summary>
        public HexCoord BossCoord { get; }

        public string PropUnitId { get; }

        /// <summary>기물이 서 있던 칸 = 폭발의 원점.</summary>
        public HexCoord PropCoord { get; }

        /// <summary>폭발이 덮은 칸들(반경 0이면 원점 한 칸). 타일 플래시 연출에 쓴다.</summary>
        public IReadOnlyList<HexCoord> BlastCoords { get; }

        /// <summary>플레이어가 실제로 입은 피해(방어로 전부 막히면 0).</summary>
        public int DamageAppliedToPlayer { get; }
    }

    /// <summary>
    /// 기물 연출이 VFX/SFX 카탈로그에서 해석될 때 쓰는 <c>sourceRef</c> 스탬프. 보스 페이즈 전환 버스트와
    /// 같은 규약이다 — <see cref="EffectKind"/>를 늘리지 않고 전용 큐를 고를 수 있게 한다.
    /// </summary>
    public static class BossPropSourceRefs
    {
        /// <summary>보스가 기물을 살포하는 캐스트 순간(보스 위치).</summary>
        public const string VolleyCast = "boss.prop.volley.cast";

        /// <summary>기물 하나가 판에 꽂히는 순간(기물 위치).</summary>
        public const string Placed = "boss.prop.placed";

        /// <summary>기물이 터지며 보스에게 흡수되는 순간(기물 위치).</summary>
        public const string AbsorbBlast = "boss.prop.absorb";
    }
}
