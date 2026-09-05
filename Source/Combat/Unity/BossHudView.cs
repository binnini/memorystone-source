using System.Linq;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 상단 보스 체력바(레이드 문법). 살아있는 보스가 있을 때만 보이고, 보스 이름 · "보스" 태그 ·
    /// 체력바 · 페이즈 핍(1/2/3) · 페이즈 지표 게이지를 표시한다.
    ///
    /// 저작 규약(이 파일이 지키는 것):
    /// <list type="bullet">
    /// <item><b>배치·크기·색은 프리팹 저작이 정본</b>. 런타임은 값만 채운다 —
    ///       <see cref="applyGeneratedStyle"/>가 false(기본)면 저작된 폰트 크기·색을 건드리지 않는다.
    ///       (런타임이 레이아웃을 하드코딩하면 디자이너가 프리팹에서 딴 모습을 본다.)</item>
    /// <item>숨김은 <b>CanvasGroup 알파</b>로 한다(SetActive 금지). 시네마틱 UI 하이더가 SetActive를
    ///       관리하고, 꺼진 오브젝트는 참조 해소·구독 갱신이 멈춘다.</item>
    /// <item>모든 그래픽 <c>raycastTarget=false</c> — 보스바는 클릭 대상이 아니고, 상단을 덮으면
    ///       그 아래 맵 클릭이 막힌다.</item>
    /// <item>참조는 직렬화 슬롯 우선 + <b>이름 폴백</b>. 프리팹을 다시 만들어도 배선이 끊기지 않는다.</item>
    /// </list>
    ///
    /// 체력 갱신은 <see cref="MonsterHealthBarView"/>를 <b>그대로 재사용</b>한다(중복 구현 금지):
    /// 벌크 <c>SetHealth</c> + 타격 프레임의 <c>FloatingTextPresented</c> 이중 경로, 고스트 트레일,
    /// 치명타 시 0 도달까지 몬스터 네임플레이트와 완전히 동일한 계약을 얻는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossHudView : MonoBehaviour
    {
        // 프리팹 재생성 후에도 배선이 살아남게 하는 이름 폴백 키(프리팹 빌더가 같은 이름으로 만든다).
        public const string RootObjectName = "Boss HUD Root";
        public const string NameLabelName = "BossHud_Name";
        public const string BossTagLabelName = "BossHud_Tag";
        public const string HealthTrackName = "BossHud_HealthTrack";
        public const string HealthFillName = "BossHud_HealthFill";
        public const string HealthValueLabelName = "BossHud_HealthValue";
        public const string PhasePipRootName = "BossHud_PhasePips";
        public const string MetricTrackName = "BossHud_MetricTrack";
        public const string MetricFillName = "BossHud_MetricFill";
        public const string MetricLabelName = "BossHud_MetricLabel";
        public const string AnnihilationLabelName = "BossHud_Annihilation";

        // CombatActorMarkerPresenter.BossLabelColor와 같은 보스 레드. 두 곳에 같은 색이 존재하는 것은
        // 의도적이다 — 마커 쪽은 월드 스페이스 네임플레이트, 이쪽은 스크린 HUD로 소유 계층이 다르다.
        private static readonly Color BossTagColor = new Color(1f, 0.08f, 0.04f, 1f);
        private static readonly Color PhasePipOnColor = new Color32(0xC9, 0xA2, 0x27, 0xFF);
        private static readonly Color PhasePipOffColor = new Color32(0x3A, 0x42, 0x55, 0xFF);
        private const float MetricFillSmoothTime = 0.12f;

        // 전멸기 카운트다운의 고조 색. 남은 턴이 줄수록 붉어진다 — 숫자만으로는 "곧이다"가 읽히지 않는다.
        private static readonly Color AnnihilationFarColor = new Color32(0xE8, 0xEE, 0xF8, 0xFF);
        private static readonly Color AnnihilationSoonColor = new Color32(0xF2, 0xB0, 0x3C, 0xFF);
        private static readonly Color AnnihilationImminentColor = new Color32(0xFF, 0x4A, 0x33, 0xFF);

        [Tooltip("false(기본)면 저작된 폰트 크기·색을 그대로 두고 런타임은 값만 채운다. 활성화하면 생성 스타일을 강제한다.")]
        [SerializeField] private bool applyGeneratedStyle;

        [Header("Refs (비우면 이름으로 폴백 해소)")]
        [SerializeField] private CanvasGroup rootGroup;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text bossTagLabel;
        [SerializeField] private Image healthTrackImage;
        [SerializeField] private Image healthFillImage;
        [SerializeField] private TMP_Text healthValueLabel;
        [SerializeField] private RectTransform phasePipRoot;
        [SerializeField] private Image metricTrackImage;
        [SerializeField] private RectTransform metricFillRect;
        [SerializeField] private TMP_Text metricLabel;
        [Tooltip("전멸기 예고 카운트다운. 비면 이름으로 폴백 해소하며, 프리팹에 없으면 조용히 표시하지 않는다.")]
        [SerializeField] private TMP_Text annihilationLabel;

        private MonsterHealthBarView healthBar;
        private Image[] phasePips;
        private bool built;
        private string boundBossUnitId = string.Empty;
        private float metricTrackWidth;
        private float metricDisplayedRatio;
        private float metricTargetRatio;
        private float metricFillVelocity;

        /// <summary>가장 최근에 활성화된 보스 HUD. 컨트롤러가 계층 탐색 없이 도달하기 위한 창구.</summary>
        public static BossHudView Active { get; private set; }

        /// <summary>현재 표시 중인 보스 유닛 id. 보스가 없으면 빈 문자열.</summary>
        public string BoundBossUnitId => boundBossUnitId;

        /// <summary>보스바가 화면에 보이는가(알파 기준). 테스트·감사용.</summary>
        public bool IsVisible => rootGroup != null && rootGroup.alpha > 0.001f;

        private void Awake()
        {
            EnsureBuilt();
            // 저작된 플레이스홀더 텍스트가 첫 프레임에 번쩍이지 않게 즉시 숨긴다.
            Hide();
        }

        private void OnEnable()
        {
            Active = this;
        }

        /// <summary>
        /// 전투 상태로부터 표시를 갱신한다. 살아있는 보스가 없으면 알파 0으로 숨긴다.
        /// 매 프레임 호출해도 안전하다(변화 없으면 값 대입만).
        /// </summary>
        public void Refresh(CombatState state)
        {
            EnsureBuilt();
            // 조우 게이트: 보스가 존재하기만 해서가 아니라 <b>조우</b>(아레나 봉인, 또는 아레나 없는 고정 보스)
            // 했을 때만 바를 띄운다 — 결계로 처음 만나기 전에 보스가 노출되지 않도록(§2-2.4 · 레이드 문법).
            if (state == null || !state.HasEncounteredLivingBoss)
            {
                Hide();
                return;
            }

            var boss = state.Monsters.FirstOrDefault(monster =>
                !monster.IsDead && MonsterSpawnRoles.IsBoss(monster.SpawnRole));
            if (string.IsNullOrEmpty(boss.Id))
            {
                Hide();
                return;
            }

            // 프로필 게이트: <b>페이즈 트랙이 있는 보스</b>에만 바를 띄운다. 페이즈 트랙은 보스 카탈로그에
            // 프로필이 있는 몬스터에만 생기므로(EnsureBossPhaseTracks), 맵 저작이 평범한 몬스터에 role=boss를
            // 달아도 레이드 보스바가 새지 않는다 — 조우 게이트만으로는 막을 수 없다. 아레나에 묶이지 않은
            // 보스는 IsBossEncountered가 "존재만으로 조우"로 폴백하기 때문이다(실제로 Stage_1의 M002가 이렇게 샜다).
            if (!state.TryGetBossPhaseState(boss.Id, out var phase))
            {
                Hide();
                return;
            }

            Bind(boss.Id);
            ApplyName(!string.IsNullOrWhiteSpace(phase.DisplayName) ? phase.DisplayName : boss.DefinitionId);
            ApplyHealth(boss.Hp, boss.MaxHp, boss.Block);
            ApplyPhasePips(phase.CurrentPhase, phase.PhaseCount);
            ApplyMetricGauge(true, phase);
            ApplyAnnihilationCountdown(state, boss.Id);

            if (rootGroup != null)
            {
                rootGroup.alpha = 1f;
            }
        }

        /// <summary>
        /// 페이즈 전환 알림. 지금은 핍만 즉시 갱신한다 — 펀치인·셰이크·아우라·BGM 크로스페이드는
        /// 연출 페이즈(P4)에서 이 지점에 붙는다.
        /// </summary>
        public void NotifyPhaseChanged(string bossUnitId, int from, int to)
        {
            if (!string.Equals(bossUnitId, boundBossUnitId, System.StringComparison.Ordinal))
            {
                return;
            }

            EnsureBuilt();
            ApplyPhasePips(to, phasePips?.Length ?? to);
        }

        /// <summary>보스가 죽거나 전투가 끝났을 때. 알파 0(비활성화 아님).</summary>
        public void Hide()
        {
            if (rootGroup != null)
            {
                rootGroup.alpha = 0f;
            }

            boundBossUnitId = string.Empty;
        }

        private void Bind(string bossUnitId)
        {
            if (string.Equals(boundBossUnitId, bossUnitId, System.StringComparison.Ordinal))
            {
                return;
            }

            boundBossUnitId = bossUnitId ?? string.Empty;
            // 타격 프레임 동기화(FloatingTextPresented)는 MonsterHealthBarView가 unitId로 필터한다.
            healthBar?.Bind(boundBossUnitId);
            metricDisplayedRatio = 0f;
            metricTargetRatio = 0f;
            metricFillVelocity = 0f;
        }

        private void ApplyName(string displayName)
        {
            if (nameLabel != null)
            {
                nameLabel.text = displayName ?? string.Empty;
                KoreanFontProvider.Apply(nameLabel);
            }

            if (bossTagLabel != null)
            {
                // 마커 네임플레이트와 같은 문자열·색을 재사용해 "보스"라는 표기가 두 곳에서 갈라지지 않게 한다.
                bossTagLabel.text = "보스";
                if (applyGeneratedStyle)
                {
                    bossTagLabel.color = BossTagColor;
                }

                KoreanFontProvider.Apply(bossTagLabel);
            }
        }

        private void ApplyHealth(int hp, int maxHp, int block)
        {
            healthBar?.SetHealth(hp, maxHp);
            if (healthValueLabel != null)
            {
                // 방어막(§21.8 제안 4 — trap-volley 배치 턴에 얻고 부술 때까지 남는다)은 화면에 있어야
                // 한다: 표시가 없으면 "왜 피해가 안 들어가지"가 화면 밖의 규칙이 된다. 신규 UI 요소를
                // 저작하지 않고 기존 수치 라벨에 덧붙인다(프리팹 저작 함정 회피 — 텍스트만 바뀐다).
                healthValueLabel.text = block > 0
                    ? $"{Mathf.Max(0, hp)} / {Mathf.Max(1, maxHp)} · 방어막 {block}"
                    : $"{Mathf.Max(0, hp)} / {Mathf.Max(1, maxHp)}";
                KoreanFontProvider.Apply(healthValueLabel);
            }
        }

        private void ApplyPhasePips(int currentPhase, int phaseCount)
        {
            if (phasePips == null || phasePips.Length == 0)
            {
                return;
            }

            // 저작된 핍 개수가 정본이다. 페이즈가 더 많으면 남는 페이즈는 표시되지 않을 뿐,
            // 런타임이 핍을 만들지는 않는다(생성하면 프리팹과 실제 모습이 갈라진다).
            for (var i = 0; i < phasePips.Length; i++)
            {
                var pip = phasePips[i];
                if (pip == null)
                {
                    continue;
                }

                var withinPhaseCount = i < Mathf.Max(1, phaseCount);
                pip.enabled = withinPhaseCount;
                if (!withinPhaseCount)
                {
                    continue;
                }

                pip.color = i < currentPhase ? PhasePipOnColor : PhasePipOffColor;
                pip.raycastTarget = false;
            }
        }

        private void ApplyMetricGauge(bool hasPhase, BossPhaseState phase)
        {
            var showGauge = hasPhase && phase.NextPhaseThreshold > 0;
            if (metricLabel != null)
            {
                metricLabel.text = showGauge
                    ? $"{MetricCaption(phase.PhaseMetric)} {phase.MetricProgress} / {phase.NextPhaseThreshold}"
                    : string.Empty;
                KoreanFontProvider.Apply(metricLabel);
            }

            if (metricTrackImage != null)
            {
                metricTrackImage.enabled = showGauge;
                metricTrackImage.raycastTarget = false;
            }

            metricTargetRatio = showGauge
                ? Mathf.Clamp01((float)phase.MetricProgress / phase.NextPhaseThreshold)
                : 0f;
            if (metricFillRect != null)
            {
                metricFillRect.gameObject.SetActive(showGauge);
            }
        }

        /// <summary>
        /// 전멸기 예고 카운트다운(§17). 지금까지 남은 턴을 아는 방법은 <b>타일의 <c>!</c> 표식을 보드에서
        /// 눈으로 세는 것</b>뿐이었다 — 아레나 전역에 피해가 들어오는 기믹인데 "언제"가 HUD에 없었다.
        /// <see cref="BossAnnihilationTelegraphState"/>가 남은 턴과 피해를 이미 투영하고 있으므로
        /// 규칙은 건드리지 않고 읽기만 한다.
        ///
        /// 라벨은 <b>선택 저작</b>이다: 프리팹에 노드가 없으면 조용히 표시하지 않는다(런타임이 노드를
        /// 만들면 디자이너가 프리팹에서 보는 모습과 실제 플레이가 갈라진다).
        /// </summary>
        private void ApplyAnnihilationCountdown(CombatState state, string bossUnitId)
        {
            if (annihilationLabel == null)
            {
                return;
            }

            var telegraph = default(BossAnnihilationTelegraphState);
            var found = false;
            foreach (var candidate in state.GetBossAnnihilationTelegraphs())
            {
                if (!string.Equals(candidate.BossUnitId, bossUnitId, System.StringComparison.Ordinal))
                {
                    continue;
                }

                telegraph = candidate;
                found = true;
                break;
            }

            if (!found)
            {
                // 전멸기 예고가 없는 동안 같은 슬롯이 주기 기믹 카운트다운을 맡는다(2026-09-03 ⑥ —
                // "살포/함정이 언제 오는가"가 HUD에 없었다). 전멸기 시퀀스 중에는 살포·함정이 양보하므로
                // 예고가 뜨면 그쪽을 우선하는 것이 곧 사실이다.
                ApplyGimmickCountdowns(state, bossUnitId);
                return;
            }

            var turns = telegraph.TurnsRemaining;
            annihilationLabel.text = turns <= 1
                ? $"전멸기 임박!  피해 {telegraph.Damage}"
                : $"전멸기 {turns}턴  ·  피해 {telegraph.Damage}";
            annihilationLabel.color = turns <= 1
                ? AnnihilationImminentColor
                : turns <= 2 ? AnnihilationSoonColor : AnnihilationFarColor;
            KoreanFontProvider.Apply(annihilationLabel);
        }

        /// <summary>
        /// 주기 기믹(철조각 살포·함정 배치) 카운트다운. 값은 규칙층 투영
        /// (<see cref="CombatState.GetBossGimmickCountdowns"/> — 1 = 다가오는 몬스터 페이즈)만 읽는다.
        /// </summary>
        private void ApplyGimmickCountdowns(CombatState state, string bossUnitId)
        {
            state.GetBossGimmickCountdowns(bossUnitId, out var propVolleyTurns, out var trapVolleyTurns);
            if (propVolleyTurns < 0 && trapVolleyTurns < 0)
            {
                annihilationLabel.text = string.Empty;
                return;
            }

            var parts = new System.Collections.Generic.List<string>(2);
            if (propVolleyTurns >= 0)
            {
                parts.Add(propVolleyTurns <= 1 ? "살포 이번 턴" : $"살포 {propVolleyTurns}턴");
            }

            if (trapVolleyTurns >= 0)
            {
                parts.Add(trapVolleyTurns <= 1 ? "함정 이번 턴" : $"함정 {trapVolleyTurns}턴");
            }

            var soonest = System.Math.Min(
                propVolleyTurns < 0 ? int.MaxValue : propVolleyTurns,
                trapVolleyTurns < 0 ? int.MaxValue : trapVolleyTurns);
            annihilationLabel.text = string.Join("  ·  ", parts);
            annihilationLabel.color = soonest <= 1 ? AnnihilationSoonColor : AnnihilationFarColor;
            KoreanFontProvider.Apply(annihilationLabel);
        }

        /// <summary>지표별 게이지 캡션. 지표를 추가하면 여기에 한 줄 늘어난다.</summary>
        private static string MetricCaption(BossPhaseMetricKind metric)
        {
            switch (metric)
            {
                case BossPhaseMetricKind.AbsorbedStacks:
                    return "흡수";
                case BossPhaseMetricKind.HpRatioBelow:
                    return "피해";
                case BossPhaseMetricKind.TurnCount:
                    return "경과";
                default:
                    return string.Empty;
            }
        }

        private void Update()
        {
            if (metricFillRect == null)
            {
                return;
            }

            // 트랙 폭은 지연 해소한다: Awake는 첫 레이아웃 패스 전이라 rect.width가 0으로 읽힐 수 있고,
            // 그러면 게이지가 영원히 0폭으로 남는다.
            if (metricTrackWidth <= 0f)
            {
                metricTrackWidth = metricTrackImage != null ? metricTrackImage.rectTransform.rect.width : 0f;
                if (metricTrackWidth <= 0f)
                {
                    return;
                }
            }

            metricDisplayedRatio = Mathf.SmoothDamp(
                metricDisplayedRatio, metricTargetRatio, ref metricFillVelocity, MetricFillSmoothTime, Mathf.Infinity, Time.deltaTime);
            if (Mathf.Abs(metricDisplayedRatio - metricTargetRatio) < 0.0005f)
            {
                metricDisplayedRatio = metricTargetRatio;
                metricFillVelocity = 0f;
            }

            var size = metricFillRect.sizeDelta;
            metricFillRect.sizeDelta = new Vector2(Mathf.Max(0f, metricTrackWidth * metricDisplayedRatio), size.y);
        }

        private void EnsureBuilt()
        {
            if (built)
            {
                return;
            }

            built = true;
            rootGroup ??= GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            nameLabel ??= FindText(NameLabelName);
            bossTagLabel ??= FindText(BossTagLabelName);
            healthValueLabel ??= FindText(HealthValueLabelName);
            metricLabel ??= FindText(MetricLabelName);
            annihilationLabel ??= FindText(AnnihilationLabelName);
            healthTrackImage ??= FindImage(HealthTrackName);
            healthFillImage ??= FindImage(HealthFillName);
            metricTrackImage ??= FindImage(MetricTrackName);
            phasePipRoot ??= FindRect(PhasePipRootName);
            if (metricFillRect == null)
            {
                var fill = FindImage(MetricFillName);
                metricFillRect = fill != null ? fill.rectTransform : null;
            }

            // 보스바는 어떤 그래픽도 클릭을 먹지 않아야 한다(상단 밴드가 맵 클릭을 삼키면 조작이 죽는다).
            foreach (var graphic in GetComponentsInChildren<Graphic>(includeInactive: true))
            {
                graphic.raycastTarget = false;
            }

            phasePips = phasePipRoot != null
                ? phasePipRoot.GetComponentsInChildren<Image>(includeInactive: true)
                : System.Array.Empty<Image>();

            metricTrackWidth = metricTrackImage != null ? metricTrackImage.rectTransform.rect.width : 0f;

            if (healthTrackImage != null)
            {
                healthBar = healthTrackImage.GetComponent<MonsterHealthBarView>()
                            ?? healthTrackImage.gameObject.AddComponent<MonsterHealthBarView>();
                var trackRect = healthTrackImage.rectTransform.rect;
                healthBar.Initialize(
                    healthTrackImage,
                    healthFillImage,
                    healthFillImage != null ? healthFillImage.rectTransform : null,
                    trackRect.width,
                    trackRect.height,
                    screenSpaceCanvas: true);
            }
        }

        private TMP_Text FindText(string objectName) => FindChild(objectName)?.GetComponent<TMP_Text>();

        private Image FindImage(string objectName) => FindChild(objectName)?.GetComponent<Image>();

        private RectTransform FindRect(string objectName) => FindChild(objectName) as RectTransform;

        private Transform FindChild(string objectName)
        {
            foreach (var child in GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (child != null && string.Equals(child.name, objectName, System.StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }
    }
}
