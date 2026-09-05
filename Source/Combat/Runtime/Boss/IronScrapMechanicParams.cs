namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// <c>iron-scrap</c> 기믹의 <c>mechanicParams</c> 키 이름. 기믹 구현이 internal이라도 저작 표면(키 이름)은
    /// 공개되어야 한다 — 저작 도구·검증·테스트가 문자열을 다시 적지 않게 하는 것이 목적이다.
    /// </summary>
    public static class IronScrapMechanicParams
    {
        /// <summary>소환할 기물의 monsterId(예: <c>M901</c>).</summary>
        public const string PropId = "propId";

        /// <summary>기물 하나를 흡수할 때 얻는 페이즈 지표 스택.</summary>
        public const string StackPerProp = "stackPerProp";

        /// <summary>
        /// 배치 후 몇 턴이 지나면 흡수되는가. 플레이어가 부술 수 있는 창의 길이이며,
        /// 배치된 턴을 0으로 세므로 3이면 "배치 후 3턴째"에 흡수된다.
        /// </summary>
        public const string MaturityTurns = "maturityTurns";

        /// <summary>살포 주기(턴). 매 턴 흘리는 압박이 아니라 주기적인 큰 파도를 만든다.</summary>
        public const string VolleyIntervalTurns = "volleyIntervalTurns";

        /// <summary>
        /// 페이즈별 1회 살포 개수(<c>5|6|7</c> 형태의 리스트). 리스트 길이는 보스의 페이즈 수와
        /// 같아야 하며 파서가 그것을 검증한다.
        ///
        /// 왜 <c>boss_phases.csv</c> 컬럼이 아닌가: 살포 개수는 이 기믹에만 있는 개념이라 페이즈 표에
        /// 넣으면 철조각과 무관한 <b>모든</b> 보스가 그 컬럼을 갖게 된다(스키마 오염).
        /// </summary>
        public const string VolleyByPhase = "volleyByPhase";

        /// <summary>기물이 놓이는 링의 반경(아레나 중심 기준, 아레나가 없으면 보스 기준).</summary>
        public const string RingRadius = "ringRadius";

        /// <summary>기물끼리 유지할 최소 거리(칸). 뭉쳐 놓이면 한 번의 광역기로 같이 부서진다.</summary>
        public const string MinSpacing = "minSpacing";

        /// <summary>
        /// 동시에 살아 있을 수 있는 기물 수의 상한 — <b>게임플레이 노브가 아니라 저작 사고 방지 가드</b>다.
        /// 정상 저작에서는 살포 주기 &gt; 성숙 턴수라 살포 시점의 생존 기물이 항상 0이므로 절대 걸리지 않는다.
        /// 누군가 주기 &lt; 성숙으로 저작했을 때만 폭주를 막는다.
        /// </summary>
        public const string MaxAlive = "maxAlive";

        /// <summary>
        /// 흡수 폭발의 반경(칸). 성숙한 기물이 보스에게 빨려 들어갈 때 그 자리에서 터지며, 이 반경 안에
        /// 서 있는 플레이어가 <see cref="BlastDamage"/>만큼 맞는다. 0이면 폭발이 피해를 주지 않는다
        /// (연출만) — 필수 키가 아니므로 저작하지 않은 기존 보스는 그대로 무피해로 동작한다.
        /// </summary>
        public const string BlastRadius = "blastRadius";

        /// <summary>
        /// 흡수 폭발이 플레이어에게 주는 피해. 필드 피해 계약을 따라 Block이 먼저 깎인다.
        /// 몬스터와 다른 기물은 맞지 않는다 — 연쇄 폭발이 되면 "성숙 전에 부순다"는 대응이 무너진다.
        /// </summary>
        public const string BlastDamage = "blastDamage";

        // ── 페이즈별 주기 + 살포 동반 함정(2026-09-05 사용자 확정 · 결정 2) ──────────────────

        /// <summary>
        /// 페이즈별 살포 주기(<c>4|14|14</c> 형태). 있으면 <see cref="VolleyIntervalTurns"/>를 그 페이즈에서
        /// 덮어쓴다 — 1페이즈는 빨리 진화시키고(8~10턴에 2페이즈) 2페이즈는 여유를 두는(25~30턴에 3페이즈)
        /// 곡선이 사용자 확정값이다. 리스트 길이는 페이즈 수와 같아야 하고 각 값은 성숙 턴수보다 커야 한다.
        /// 없으면 모든 페이즈가 <see cref="VolleyIntervalTurns"/>를 쓴다(기존 저작 하위호환).
        /// </summary>
        public const string VolleyIntervalByPhase = "volleyIntervalByPhase";

        /// <summary>
        /// 살포와 <b>같은 턴</b>에 아레나 전역에 심는 함정 풀(페이즈별 · <c>|</c> 구분). 한 페이즈 항목은
        /// <c>종류:개수</c>를 <c>+</c>로 잇는다 — 예 <c>Slow:2+Curse:2+Stun:1+Turret:1</c>.
        /// 종류 어휘는 <see cref="TrapKindSlow"/>·<see cref="TrapKindStun"/>·<see cref="TrapKindCurse"/>·
        /// <see cref="TrapKindTurret"/> 넷뿐이다(사용자 확정: 「기절·둔화·저주 부여·터렛 소환 같은 강력한 것」).
        /// 빈 값 = 함정 없음(옛 별도 trap-volley 기믹은 이 키로 흡수됐다).
        /// <para>구분자가 <c>;</c>·<c>,</c>가 아닌 이유: <c>;</c>는 mechanicParams의 키 구분자이고
        /// <c>,</c>는 CSV 셀 구분자다. 둘 다 값 안에서는 쓸 수 없다.</para>
        /// </summary>
        public const string VolleyTrapPoolByPhase = "volleyTrapPoolByPhase";
        public const string TrapKindSlow = "Slow";
        public const string TrapKindStun = "Stun";
        public const string TrapKindCurse = "Curse";
        public const string TrapKindTurret = "Turret";

        /// <summary>함정끼리 최소 간격(칸). 살포 링 간격과 같은 의미.</summary>
        public const string VolleyTrapMinSpacing = "volleyTrapMinSpacing";
        /// <summary>동시에 무장 상태로 남을 수 있는 런타임 함정 상한(저작 사고 가드 — 아레나가 함정으로 덮이지 않게).</summary>
        public const string VolleyTrapMaxArmed = "volleyTrapMaxArmed";
        /// <summary>저주 함정이 덱에 섞는 카드 풀(<c>X07+X05+X02</c>). 함정 하나가 이 중 한 장을 고른다.</summary>
        public const string VolleyTrapCursePool = "volleyTrapCursePool";
        /// <summary>터렛 함정이 부르는 몬스터 id(페이즈별 · <c>M902|M903|M904</c>).</summary>
        public const string VolleyTurretByPhase = "volleyTurretByPhase";
        /// <summary>살아있는 터렛 상한 — 이 수 이상이면 터렛 함정은 그 볼리에서 심지 않는다(다른 종류로 대체하지 않는다).</summary>
        public const string VolleyTurretMaxAlive = "volleyTurretMaxAlive";
        /// <summary>둔화 함정의 지속 턴.</summary>
        public const string VolleyTrapSlowTurns = "volleyTrapSlowTurns";
        /// <summary>기절 함정의 지속 턴.</summary>
        public const string VolleyTrapStunTurns = "volleyTrapStunTurns";
        /// <summary>
        /// 살포가 <b>실제로</b> 일어난 턴에 보스가 얻는 방어막(옛 trapVolleyGuardBlock의 후계 · §21.8 제안 4).
        /// 견고 특성 덕에 부술 때까지 남는다 — 「다음 살포 전에 깎아라」 퍼즐.
        /// </summary>
        public const string VolleyGuardBlock = "volleyGuardBlock";
    }
}
