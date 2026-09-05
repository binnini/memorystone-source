using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 철조각 사슬(scrap-chain · §21.8 제안 2)의 보드 측 규칙: 스포크 경로 산출, 예고 저장,
    /// 명중 해소, 표현 투영. 순서·수치는 <see cref="ScrapChainMechanic"/>이 갖는다.
    ///
    /// <para>🔑 <b>예고와 명중이 같은 생존 필터를 공유한다</b>(<see cref="EnumerateLiveScrapChainCells"/>) —
    /// 예고 후 철조각을 부수면 그 가닥의 붉은 칸이 화면에서 꺼지고, 명중도 정확히 같은 칸을 건너뛴다.
    /// 필터가 둘이면 "꺼진 칸에 맞는" 화면 거짓말이 가능해진다.</para>
    /// </summary>
    public sealed partial class CombatState
    {
        // ── 3-B(2026-09-04): 본문은 BossEncounterState.ScrapChain.cs. 여기는 시그니처 불변 위임과 중첩 타입만 남는다.
        internal void BeginBossScrapChainTelegraph(BossPhaseTrack track, MonsterRuntime boss, string propDefinitionId) => Boss.BeginBossScrapChainTelegraph(track, boss, propDefinitionId);
        internal void ResolveBossScrapChainHit(BossPhaseTrack track, MonsterRuntime boss, int damage, int rootTurns) => Boss.ResolveBossScrapChainHit(track, boss, damage, rootTurns);
        public IReadOnlyList<HexCoord> GetBossScrapChainTelegraphCells() => Boss.GetBossScrapChainTelegraphCells();
        /// <summary>직전 몬스터 행동 결의에서 명중 해소된 사슬(표현 전용 · <see cref="LastBossPropAbsorptions"/> 규약).</summary>
        public IReadOnlyList<BossScrapChainHit> LastBossScrapChainHits => Boss.LastBossScrapChainHits;

        /// <summary>사슬 연출 <c>sourceRef</c> 스탬프(<see cref="BossAnnihilationSourceRefs"/>와 같은 규약).</summary>
        public static class BossScrapChainSourceRefs
        {
            /// <summary>사슬 명중(플레이어 위치).</summary>
            public const string Hit = "boss.scrap-chain";
        }
    }

    /// <summary>
    /// 사슬 명중 결의 한 건의 투영(2026-09-05 후속 #7). <see cref="Strands"/>는 <b>살아 있던 가닥</b>(철조각 생존)의 셀
    /// 목록 — 보스에서 철조각까지 순서대로라 라인 렌더러가 그대로 따라 긋는다. 명중 여부와 무관하게 기록된다(빗나간
    /// 사슬도 선은 그어진다 — 「피했다」가 화면에 있어야 예고를 읽은 보상이 보인다). 규칙은 이 뒤에 가닥을 지우므로
    /// 이 기록이 연출의 유일한 원천이다.
    /// </summary>
    public readonly struct BossScrapChainHit
    {
        public BossScrapChainHit(string bossUnitId, HexCoord bossCoord, IReadOnlyList<IReadOnlyList<HexCoord>> strands, bool hitPlayer, HexCoord playerCoord)
        {
            BossUnitId = bossUnitId ?? string.Empty;
            BossCoord = bossCoord;
            Strands = strands ?? Array.Empty<IReadOnlyList<HexCoord>>();
            HitPlayer = hitPlayer;
            PlayerCoord = playerCoord;
        }

        public string BossUnitId { get; }
        public HexCoord BossCoord { get; }
        public IReadOnlyList<IReadOnlyList<HexCoord>> Strands { get; }
        public bool HitPlayer { get; }
        public HexCoord PlayerCoord { get; }
    }
}
