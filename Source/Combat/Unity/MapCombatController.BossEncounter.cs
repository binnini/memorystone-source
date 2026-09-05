using System.Collections;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    // 보스 아레나 조우(입장) 연출. 결계가 닫힌 직후 이동 표현(RunMoveTimeline)이 이 시퀀스를 재생한다:
    // 화면 암전 → (암전 뒤) 플레이어를 전투 시작 좌표로 스냅·카메라 리센터 → 홀드 → 페이드 인.
    // 규칙 측(봉인·좌표 확정·아레나 공개)은 이 연출 이전에 이미 끝나 있다(§8-9 P5·계획 §12).
    //
    // 지금은 뼈대(암전·좌표 반영·홀드)만 있다. 보스 리빌 비트(로우앵글 아크 + 낙하·포효 애니)와 이름 대형
    // 표기는 홀드 구간에 들어갈 다음 단계다.
    public sealed partial class MapCombatController
    {
        [Header("Boss Arena Encounter")]
        [Tooltip("결계 진입 직후 화면을 검게 덮는 시간(초).")]
        [SerializeField] private float bossEncounterFadeInSeconds = 0.35f;
        [Tooltip("암전 상태로 보스를 드러내며 머무는 시간(초). 리빌 비트·이름 표기가 이 구간에 들어간다.")]
        [SerializeField] private float bossEncounterHoldSeconds = 0.8f;
        [Tooltip("전투 화면으로 돌아오며 암전을 걷는 시간(초).")]
        [SerializeField] private float bossEncounterFadeOutSeconds = 0.5f;
        [Tooltip("보스 이름 대형 표기가 나타나고 사라지는 페이드 시간(초).")]
        [SerializeField] private float bossEncounterNameFadeSeconds = 0.3f;

        [Header("Boss Reveal Arc (로우앵글)")]
        [Tooltip("리빌 카메라 피치(도). 낮을수록 보스를 더 올려다본다(로우앵글).")]
        [SerializeField] private float bossRevealArcPitchDegrees = 15f;
        [Tooltip("리빌 카메라의 보스 기준 궤도 반경(월드 유닛). 클수록 멀리서 넓게 잡는다.")]
        [SerializeField] private float bossRevealArcDistance = 10f;
        [Tooltip("리빌 아크가 그리는 방위각 폭(도). 짧은 호로 보스를 스쳐 지난다.")]
        [SerializeField] private float bossRevealArcSweepDegrees = 42f;
        [Tooltip("리빌 아크의 중심 방위각(도). 보스를 어느 쪽에서 볼지.")]
        [SerializeField] private float bossRevealArcAzimuthDegrees = 200f;
        [Tooltip("리빌 룩앳을 보스 타일 위로 얼마나 띄울지(월드 유닛). 큰 보스의 상체·머리를 잡으려면 올린다.")]
        [SerializeField] private float bossRevealArcLookAtHeight = 2.6f;
        [Tooltip("리빌 아크를 그리는 시간(초).")]
        [SerializeField] private float bossRevealArcSeconds = 1.8f;

        private CombatScreenFadeCurtain bossEncounterCurtain;
        private BossEncounterNameView bossEncounterNamePlate;
        private bool bossEncounterViewActive;

        /// <summary>조우 연출이 화면을 소유하는 동안 참. IsCinematicViewActive OR 목록에 들어간다.</summary>
        internal bool IsBossEncounterViewActive => bossEncounterViewActive;

        private CombatScreenFadeCurtain EnsureBossEncounterCurtain()
        {
            if (bossEncounterCurtain == null)
            {
                bossEncounterCurtain = CombatScreenFadeCurtain.Create(transform);
            }

            return bossEncounterCurtain;
        }

        /// <summary>
        /// 조우(입장) 연출. <see cref="RunMoveTimeline"/>이 걷기 뒤에, 이 이동에서 결계가 닫혔을 때만 호출한다.
        /// 규칙상 <c>PlayerCoord</c>는 이미 전투 시작 좌표(중앙 전방)로 확정돼 있으므로(§8-9 P5), 여기서는
        /// 암전으로 가린 뒤 <see cref="RefreshView"/>로 마커를 그 좌표에 스냅하고 카메라를 리센터한다 —
        /// 걸어 들어온 경계 칸에서 중앙으로 순간이동하는 것이 보이지 않는다.
        /// try/finally로 중단(코루틴 Stop 포함)돼도 암전은 반드시 걷힌다.
        /// </summary>
        private IEnumerator PlayBossArenaEncounterCinematic(CombatPresentationSnapshot after)
        {
            var curtain = EnsureBossEncounterCurtain();
            var binder = ResolveCinemachineCombatCameraBinder();
            var bossFocused = false;
            var arcCameraBegun = false;
            bossEncounterViewActive = true;
            try
            {
                // 1) 화면 암전.
                yield return curtain.FadeTo(1f, bossEncounterFadeInSeconds, () => CinematicDeltaTime);

                // 2) 암전 뒤: 플레이어 마커를 전투 시작 좌표(=State.PlayerCoord)로 스냅한다. 그 다음 카메라를
                //    보스로 옮긴다. IsCinematicViewActive가 참이라 오버레이/툴팁은 이 RefreshView에서 안 샌다.
                RefreshView();

                // 시네마틱 동안 마커 UI(월드 네임플레이트·체력바)를 숨긴다 — 인트로 컷과 같은 규약. 안 그러면
                // 보스 머리 위 네임플레이트가 대형 이름 표기와 겹쳐 이름이 두 번 뜬다(캡처로 발견).
                actorMarkerPresenter?.SetMarkerUiVisible(false);

                var bossUnitId = ResolveEncounterBossUnitId();
                var bossCoord = ResolveBossUnitCoord(bossUnitId);
                var arc = default(CinematicShot);
                if (bossCoord.HasValue && TryGetTileWorldPosition(bossCoord.Value, out var bossWorld))
                {
                    if (prototype3DCamera != null)
                    {
                        // 로우앵글 아크: 추출한 공용 샷 플레이어(AnimateCinematicShot)로 보스를 올려다보며 짧은
                        // 호를 그린다. 인트로/승리 컷과 같은 시네마틱 카메라를 잠깐 빌린다(§2.8 추출의 목적).
                        arc = BuildBossRevealArcShot(bossWorld);
                        CameraController.BeginStageIntroCinemachineCamera(
                            arc.FromPos, arc.FromLookAt, prototype3DCamera, transform, stageIntroCameraFieldOfView);
                        arcCameraBegun = true;
                    }
                    else if (binder != null && !CameraController.IsCameraBlending)
                    {
                        // 폴백(카메라 없음/헤드리스): 게임플레이 카메라 정적 포커스. 게이트를 타지 않는다.
                        bossFocused = binder.TrySetActionFocus(bossWorld);
                    }

                    // 낙하·포효: 보스의 기존 공격 애니메이션을 튼다(빈 트리거 = 마커 컨트롤러가 자동 선택).
                    // 애니메이터 .controller 손편집 없이 기존 클립을 재생하는 규약(§9.7·계획 §12.5).
                    actorMarkerPresenter?.TriggerAttack(bossUnitId, string.Empty);
                }

                // 이름 대형 표기 준비: 암전 중 텍스트만 채우고 숨겨 둔다. 리빌과 함께 페이드로 드러낸다.
                var namePlate = EnsureBossEncounterNamePlate();
                if (namePlate != null)
                {
                    namePlate.SetBossName(ResolveEncounterBossDisplayName());
                    namePlate.Alpha = 0f;
                }

                // 3) 페이드 인 → 보스 컷씬 공개(낙하·포효).
                yield return curtain.FadeTo(0f, bossEncounterFadeOutSeconds, () => CinematicDeltaTime);

                // 4) 이름 대형 표기를 드러내고, 리빌(아크 있으면 아크, 없으면 정적 홀드)을 재생 → 이름을 걷는다.
                if (namePlate != null)
                {
                    yield return FadeBossEncounterNamePlate(namePlate, 1f, bossEncounterNameFadeSeconds);
                }

                if (arcCameraBegun)
                {
                    yield return Cinematics.AnimateCinematicShot(arc);
                }
                else
                {
                    yield return HoldForBossEncounter(bossEncounterHoldSeconds);
                }

                if (namePlate != null)
                {
                    yield return FadeBossEncounterNamePlate(namePlate, 0f, bossEncounterNameFadeSeconds);
                }
            }
            finally
            {
                // 중단(코루틴 Stop 포함)돼도: 암전을 걷고, 시네마틱 카메라를 끄고(게임플레이로 블렌드 복귀),
                // 보스 포커스를 풀고, 카메라를 플레이어(중앙 전방)로 되돌린다 — 어떤 경로로 끝나도 복귀한다.
                bossEncounterViewActive = false;
                // 마커 UI(네임플레이트·체력바)를 다시 켠다 — 시네마틱 시작에서 숨긴 것을 원복.
                actorMarkerPresenter?.SetMarkerUiVisible(true);
                if (arcCameraBegun)
                {
                    CameraController.EndStageIntroCinemachineCamera();
                }

                if (bossFocused && binder != null)
                {
                    binder.ClearActionFocus();
                }

                RecenterGameplayCameraOnPlayer(immediate: false);
                if (bossEncounterCurtain != null)
                {
                    bossEncounterCurtain.Alpha = 0f;
                }

                if (bossEncounterNamePlate != null)
                {
                    bossEncounterNamePlate.Alpha = 0f;
                }
            }
        }

        // 보스 리빌 아크 샷: 보스를 룩앳으로 잡고 로우앵글에서 짧은 방위각 호를 그린다. IntroOrbitCameraPosition
        // (인트로 포커스 샷과 같은 궤도 헬퍼)으로 양 끝 포즈를 잡는다.
        private CinematicShot BuildBossRevealArcShot(Vector3 bossWorld)
        {
            var focus = bossWorld + Vector3.up * bossRevealArcLookAtHeight;
            var half = bossRevealArcSweepDegrees * 0.5f;
            var fromPos = CombatCameraController.IntroOrbitCameraPosition(focus, bossRevealArcAzimuthDegrees - half, bossRevealArcPitchDegrees, bossRevealArcDistance);
            var toPos = CombatCameraController.IntroOrbitCameraPosition(focus, bossRevealArcAzimuthDegrees + half, bossRevealArcPitchDegrees, bossRevealArcDistance);
            return new CinematicShot(fromPos, toPos, focus, focus, Mathf.Max(0.05f, bossRevealArcSeconds));
        }

        // 이름 표기 프리팹을 Resources에서 로드해 한 번만 인스턴스화한다(전용 Canvas를 품는다). 프리팹이
        // 없으면(빌더 미실행) null을 돌려주고 조우 연출은 이름 없이 진행한다.
        private BossEncounterNameView EnsureBossEncounterNamePlate()
        {
            if (bossEncounterNamePlate == null)
            {
                var prefab = Resources.Load<GameObject>(BossEncounterNameView.ResourcesPath);
                if (prefab != null)
                {
                    var instance = Instantiate(prefab, transform);
                    instance.name = BossEncounterNameView.RootObjectName;
                    bossEncounterNamePlate = instance.GetComponent<BossEncounterNameView>();
                }
            }

            return bossEncounterNamePlate;
        }

        private IEnumerator FadeBossEncounterNamePlate(BossEncounterNameView plate, float target, float seconds)
        {
            if (plate == null)
            {
                yield break;
            }

            if (seconds <= 0.0001f)
            {
                plate.Alpha = target;
                yield break;
            }

            var from = plate.Alpha;
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += CinematicDeltaTime;
                plate.Alpha = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / seconds));
                yield return null;
            }

            plate.Alpha = target;
        }

        // 표기할 보스 이름: 조우가 프레이밍하는 보스의 페이즈 트랙 DisplayName(boss_profiles.csv 저작).
        // 특정 보스 이름을 코드에 박지 않는다.
        private string ResolveEncounterBossDisplayName()
        {
            if (State == null || !State.HasBossPhaseTrack)
            {
                return string.Empty;
            }

            var bossUnitId = ResolveEncounterBossUnitId();
            foreach (var phase in State.BossPhases)
            {
                if (string.Equals(phase.BossUnitId, bossUnitId, System.StringComparison.Ordinal))
                {
                    return phase.DisplayName;
                }
            }

            return State.BossPhases.Count > 0 ? State.BossPhases[0].DisplayName : string.Empty;
        }

        // 조우가 프레이밍할 보스: 봉인된 아레나에 묶인 보스가 정본이고, 없으면(디버그 원클릭 등) 첫 페이즈
        // 트랙 보스로 폴백한다. 특정 보스 이름을 코드에 박지 않는다(다중 보스 규약).
        private string ResolveEncounterBossUnitId()
        {
            var sealedBoss = State != null ? State.SealedBossArenaBossUnitId : null;
            if (!string.IsNullOrEmpty(sealedBoss))
            {
                return sealedBoss;
            }

            return State != null && State.HasBossPhaseTrack && State.BossPhases.Count > 0
                ? State.BossPhases[0].BossUnitId
                : null;
        }

        // unscaled/캡처 델타로 도는 홀드(히트스톱·timeScale과 무관하게 진행). 시네마틱 시간 계약 재사용.
        private IEnumerator HoldForBossEncounter(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += CinematicDeltaTime;
                yield return null;
            }
        }
    }
}
