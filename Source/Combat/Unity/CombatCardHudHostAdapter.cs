using System.Linq;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    // P4 Stage 3 HUD seam service (mirrors the Stage 1 CombatAudioHostAdapter pattern): owns the
    // host-side HUD presentation wiring extracted from MapCombatController, starting with the
    // turn-phase dock (CombatState.PhaseChanged subscription + announcements). Plain class owned
    // by MapCombatController; state access arrives as delegates and serialized fields stay on the
    // host, so no scene reference moves.
    internal sealed class CombatCardHudHostAdapter
    {
        private readonly System.Func<CombatState> resolveState;
        private readonly System.Func<bool> deferVictoryOverlay;
        private readonly System.Action showGameVictoryOverlay;

        private CombatState phaseSubscribedState;
        private TurnPhaseDockView turnPhaseDockView;
        private CombatState bossPhaseSubscribedState;
        private BossHudView bossHudView;

        public CombatCardHudHostAdapter(
            System.Func<CombatState> resolveState,
            System.Func<bool> deferVictoryOverlay,
            System.Action showGameVictoryOverlay)
        {
            this.resolveState = resolveState;
            this.deferVictoryOverlay = deferVictoryOverlay;
            this.showGameVictoryOverlay = showGameVictoryOverlay;
        }

        // Set by the host right before playing a presentation sequence so the dock does not
        // announce the player turn twice (the sequence completion announces it itself).
        public bool SuppressNextPlayerTurnDockAnnouncement { get; set; }

        // Keep the turn-phase dock subscribed to the live CombatState.PhaseChanged event
        // (re-subscribes when the state instance changes, mirroring the camera-shake subscription
        // lifecycle in CombatCameraController).
        public void EnsureTurnPhaseSubscription()
        {
            var state = resolveState();
            if (phaseSubscribedState == state)
            {
                return;
            }

            if (phaseSubscribedState != null)
            {
                phaseSubscribedState.PhaseChanged -= OnPhaseChangedForDock;
            }

            phaseSubscribedState = state;
            if (phaseSubscribedState != null)
            {
                phaseSubscribedState.PhaseChanged += OnPhaseChangedForDock;
            }

            RefreshCurrentTurnPhaseDock();
        }

        public void UnsubscribeTurnPhase()
        {
            if (phaseSubscribedState != null)
            {
                phaseSubscribedState.PhaseChanged -= OnPhaseChangedForDock;
                phaseSubscribedState = null;
            }
        }

        private void OnPhaseChangedForDock(CombatPhase previous, CombatPhase current)
        {
            RefreshCurrentTurnPhaseDock(current);

            if (current == CombatPhase.Victory && !deferVictoryOverlay())
            {
                showGameVictoryOverlay();
            }

            if (current == CombatPhase.PlayerMovement && SuppressNextPlayerTurnDockAnnouncement)
            {
                SuppressNextPlayerTurnDockAnnouncement = false;
                return;
            }

            var dock = ResolveTurnPhaseDockView();
            if (dock == null)
            {
                return;
            }

            if (!dock.gameObject.activeSelf)
            {
                dock.gameObject.SetActive(true);
            }

            dock.Announce(previous, current);
        }

        public void AnnounceOverallTurnStartForDock()
        {
            var dock = ResolveTurnPhaseDockView();
            var state = resolveState();
            if (dock == null || state == null)
            {
                return;
            }

            if (!dock.gameObject.activeSelf)
            {
                dock.gameObject.SetActive(true);
            }

            dock.AnnounceOverallTurnStart(state.OverallTurnNumber);
        }

        public void AnnouncePlayerTurnStartForDock()
        {
            var dock = ResolveTurnPhaseDockView();
            if (dock == null)
            {
                return;
            }

            if (!dock.gameObject.activeSelf)
            {
                dock.gameObject.SetActive(true);
            }

            dock.AnnouncePlayerTurnStart();
        }

        public void AnnounceGameStartForDock()
        {
            var dock = ResolveTurnPhaseDockView();
            if (dock == null)
            {
                return;
            }

            if (!dock.gameObject.activeSelf)
            {
                dock.gameObject.SetActive(true);
            }

            dock.AnnounceGameStart();
        }

        public void RefreshCurrentTurnPhaseDock(CombatPhase? phaseOverride = null)
        {
            var dock = ResolveTurnPhaseDockView();
            if (dock == null)
            {
                return;
            }

            if (!dock.gameObject.activeSelf)
            {
                dock.gameObject.SetActive(true);
            }

            dock.SetCurrentPhase(phaseOverride ?? resolveState()?.Phase);
        }

        // --- 보스 HUD -----------------------------------------------------------------------------

        /// <summary>
        /// 보스 HUD를 살아있는 CombatState에 붙여 두고 매 프레임 표시를 갱신한다. 턴 페이즈 도크와 같은
        /// 수명 관리(상태 인스턴스가 바뀌면 재구독)를 쓴다.
        /// 갱신을 매 프레임 하는 이유: 보스 체력은 벌크 경로로도 들어오고(타격 프레임 동기화는
        /// MonsterHealthBarView가 자체 이벤트로 처리한다) 보스 사망·전투 종료도 여기서 자연히 숨겨진다.
        /// </summary>
        public void EnsureBossHud()
        {
            var state = resolveState();
            if (bossPhaseSubscribedState != state)
            {
                if (bossPhaseSubscribedState != null)
                {
                    bossPhaseSubscribedState.BossPhaseChanged -= OnBossPhaseChangedForHud;
                }

                bossPhaseSubscribedState = state;
                if (bossPhaseSubscribedState != null)
                {
                    bossPhaseSubscribedState.BossPhaseChanged += OnBossPhaseChangedForHud;
                }
            }

            ResolveBossHudView()?.Refresh(state);
        }

        public void UnsubscribeBossPhase()
        {
            if (bossPhaseSubscribedState != null)
            {
                bossPhaseSubscribedState.BossPhaseChanged -= OnBossPhaseChangedForHud;
                bossPhaseSubscribedState = null;
            }
        }

        private void OnBossPhaseChangedForHud(string bossUnitId, int from, int to)
        {
            ResolveBossHudView()?.NotifyPhaseChanged(bossUnitId, from, to);
        }

        private BossHudView ResolveBossHudView()
        {
            if (bossHudView != null)
            {
                return bossHudView;
            }

            bossHudView = BossHudView.Active
                ?? Object.FindObjectsByType<BossHudView>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            return bossHudView;
        }

        private TurnPhaseDockView ResolveTurnPhaseDockView()
        {
            if (turnPhaseDockView != null)
            {
                return turnPhaseDockView;
            }

            turnPhaseDockView = TurnPhaseDockView.Active
                ?? Object.FindObjectsByType<TurnPhaseDockView>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            return turnPhaseDockView;
        }
    }
}
