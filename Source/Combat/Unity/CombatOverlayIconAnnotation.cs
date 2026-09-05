using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Per-tile icon overlay payload for a combat overlay presentation. Unlike
    /// <see cref="CombatOverlayLayerState"/> (which carries a coord set + fill/boundary style),
    /// an annotation carries a coordinate-specific payload (the status effects and/or knockback
    /// telegraphed on that tile) that a dedicated icon renderer consumes. Knockback is not a
    /// <see cref="StatusEffectKind"/> (it is an instant displacement), so it rides alongside the
    /// effect list as a signed distance and the renderer adds it to the same per-tile icon cycle.
    ///
    /// <para>🔑 <b>부호가 방향이다</b>(§16.1) — 양수=밀치기 · 음수=끌어당김 · 0=변위 없음. 예전에는
    /// bool이라 타일이 방향을 몰랐고, 끌어당기는 공격에도 미는 그림이 떴다.</para>
    /// </summary>
    public readonly struct CombatOverlayIconAnnotation
    {
        public CombatOverlayIconAnnotation(
            HexCoord coord,
            IEnumerable<StatusEffectKind> effects,
            int knockbackDistance = 0,
            int annihilationTurnsRemaining = 0,
            int annihilationDamage = 0,
            bool safeZoneCandidate = false,
            int weakSpotDamagePercent = 0,
            int weakSpotTurnsRemaining = 0,
            bool safeZoneConfirmed = false,
            bool injectsCurse = false)
            : this(
                coord,
                effects != null
                    ? effects.Distinct().Select(kind => new ActiveEffect(EffectType.Duration, kind, string.Empty, 0)).ToArray()
                    : Array.Empty<ActiveEffect>(),
                knockbackDistance,
                annihilationTurnsRemaining,
                annihilationDamage,
                safeZoneCandidate,
                weakSpotDamagePercent,
                weakSpotTurnsRemaining,
                safeZoneConfirmed,
                injectsCurse)
        {
        }

        public CombatOverlayIconAnnotation(
            HexCoord coord,
            IEnumerable<ActiveEffect> effects,
            int knockbackDistance = 0,
            int annihilationTurnsRemaining = 0,
            int annihilationDamage = 0,
            bool safeZoneCandidate = false,
            int weakSpotDamagePercent = 0,
            int weakSpotTurnsRemaining = 0,
            bool safeZoneConfirmed = false,
            bool injectsCurse = false)
        {
            Coord = coord;
            StatusEffects = effects != null
                ? effects
                    .GroupBy(effect => effect.Kind)
                    .Select(group => group.First())
                    .ToArray()
                : Array.Empty<ActiveEffect>();
            Effects = StatusEffects.Select(effect => effect.Kind).ToArray();
            KnockbackDistance = knockbackDistance;
            AnnihilationTurnsRemaining = Math.Max(0, annihilationTurnsRemaining);
            AnnihilationDamage = Math.Max(0, annihilationDamage);
            SafeZoneCandidate = safeZoneCandidate;
            WeakSpotDamagePercent = Math.Max(0, weakSpotDamagePercent);
            WeakSpotTurnsRemaining = Math.Max(0, weakSpotTurnsRemaining);
            SafeZoneConfirmed = safeZoneConfirmed;
            InjectsCurse = injectsCurse;
        }

        public HexCoord Coord { get; }
        public IReadOnlyList<StatusEffectKind> Effects { get; }
        public IReadOnlyList<ActiveEffect> StatusEffects { get; }

        /// <summary>
        /// 예고된 변위 칸 수 — <b>양수 = 밀치기 · 음수 = 끌어당김 · 0 = 없음</b>(§16.1).
        /// 정본은 카탈로그 패턴(<c>MonsterAttackPattern.KnockbackDistance</c>)이다.
        /// </summary>
        public int KnockbackDistance { get; }

        /// <summary>이 칸에 변위 표식을 띄워야 하는가(방향은 <see cref="KnockbackDistance"/>의 부호가 말한다).</summary>
        public bool Knockback => KnockbackDistance != 0;

        /// <summary>예고된 변위가 끌어당김인가 — 표식 그림이 밀치기와 갈린다.</summary>
        public bool IsPull => KnockbackDistance < 0;

        /// <summary>
        /// 전멸기 예고(§13.5)가 이 칸을 덮고 있을 때의 남은 몬스터 행동 수(0 = 예고 없음).
        /// 전멸기는 평범한 공격 예고와 <b>같은</b> 붉은 위험 해치를 공유하므로 — 둘 다 "이 칸은 위험"이다 —
        /// 화면에서 둘을 가르는 것은 이 플래그가 띄우는 경고 아이콘뿐이다.
        /// </summary>
        public int AnnihilationTurnsRemaining { get; }

        /// <summary>전멸기 폭발의 저작 피해(툴팁 표시용).</summary>
        public int AnnihilationDamage { get; }

        /// <summary>이 칸에 전멸기 경고 아이콘을 띄워야 하는가.</summary>
        public bool HasAnnihilationWarning => AnnihilationTurnsRemaining > 0;

        /// <summary>취약 부위 피해 증가율(%·0 = 표식 없음 · §28 W4). 아이콘과 호버 툴팁이 이 값을 읽는다.</summary>
        public int WeakSpotDamagePercent { get; }

        /// <summary>취약 부위가 유지되는 남은 턴(툴팁 표시용).</summary>
        public int WeakSpotTurnsRemaining { get; }

        /// <summary>이 칸에 취약 부위 아이콘을 띄워야 하는가(§28 W4).</summary>
        public bool HasWeakSpotMark => WeakSpotDamagePercent > 0;

        /// <summary>
        /// 이 칸이 전멸기 안전지대의 <b>미판별 후보</b>인가 = <c>?</c>를 띄운다(§20-B-6).
        /// 판별되면 꺼진다 — 진짜면 예고가 없다는 사실이 이미 안전을 뜻하고, 가짜면 아래 깔린
        /// 붉은 예고가 드러난다. 그래서 신규 기호는 <c>?</c> 하나뿐이다.
        ///
        /// <para>🔴 아이콘은 한 칸에 여럿이면 <c>cycleInterval</c>(1초)로 <b>순환</b>한다
        /// (<c>StatusIconOverlayRenderer</c>). 후보 칸이 함정과 겹치면(§20-B-4 4~6단계)
        /// <c>?</c>와 함정 아이콘이 번갈아 뜨므로, 상태 아이콘 트랙에서 확립된 제약 —
        /// 같은 칸을 순환하는 아이콘끼리 색이 멀어야 한다 — 이 그대로 적용된다.</para>
        /// </summary>
        public bool SafeZoneCandidate { get; }

        /// <summary>
        /// 이 칸이 정찰로 판명된 <b>진짜 안전지대</b>인가(§28 W5 후속 T1). 시각 신호는 초록 확정
        /// 채움(<c>HexOverlayLayer.BossSafeZoneConfirmed</c>)이 이미 맡고 있으므로, 이 플래그는
        /// 새 아이콘을 그리지 않고 <b>호버 툴팁 대상</b>만 만든다 — 레이어 채움은 호버를 못 받아서
        /// (아이콘 채널만 <c>StatusIconOverlayRenderer</c>가 판정한다) 이 주석이 없으면 초록 칸은
        /// "왜 초록인가"를 물을 방법이 없다.
        /// </summary>
        public bool SafeZoneConfirmed { get; }

        /// <summary>
        /// 이 칸에 서면 <b>덱에 저주 카드가 섞이는가</b>(2026-09-01 W2 · A035 속삭임 · A028 쇳가루 휩쓸기).
        ///
        /// <para>🔑 상태이상 목록(<see cref="Effects"/>)에 끼워 넣지 않는 이유: 저주는 턴이 지나 풀리는
        /// 것이 아니라 덱에 영구히 남는 오염이라, 같은 채널에 실으면 "몇 턴짜리"라는 어휘를 빌려 쓰게 된다.
        /// 넉백이 상태이상이 아니라서 자기 축으로 타는 것과 같은 이유다.</para>
        /// </summary>
        public bool InjectsCurse { get; }
    }
}
