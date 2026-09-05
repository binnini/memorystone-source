using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity.Tutorial;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// <see cref="CombatRewardFlow"/>가 호스트(<see cref="MapCombatController"/>)에서 필요로 하는 조각(4B-B). 상태 읽기·
    /// 직렬화 팝업/타일 뷰/카탈로그 소스 읽기·HUD/오디오/튜토리얼 서비스 호출뿐이다. 쓰기는 <c>LastInputMessage</c> 하나
    /// (private setter라 명시적 구현으로 잇는다 — 2단계 <c>ICombatDebugHost</c> 선례).
    /// </summary>
    internal interface ICombatRewardFlowHost
    {
        // ── 상태 ──
        CombatState State { get; }
        HexMapData LoadedMap { get; }
        string LastInputMessage { get; set; }
        bool IsSequencePlaying { get; }
        /// <summary>경고 로그의 컨텍스트 오브젝트(호스트 MonoBehaviour).</summary>
        Object LogContext { get; }

        // ── 직렬화 뷰·소스(호스트 소유 — 읽기만) ──
        /// <summary>null일 수 있다(씬 미배선·테스트 픽스처). Unity 가짜 null 판정을 위해 구체 타입으로 노출한다.</summary>
        CardRewardPopupView CardRewardPopupView { get; }
        ICardRewardPopupView CardRewardPresentation { get; }
        void EnsureCardRewardPopupView();
        /// <summary>null일 수 있다.</summary>
        AtlasTilePresentationView TileView { get; }
        CombatCatalogTextAssetSource CatalogSource { get; }

        // ── 호스트 서비스 ──
        void RefreshHudOnly();
        void RequestAudioCue(string cueId, string context);
        void CommitPresentationFromState(bool immediateCamera);
        bool TryGetTileWorldPosition(HexCoord coord, out Vector3 worldPosition);
        EffectPresentationController ResolveMovementEffectPresentation();

        // ── 튜토리얼 ──
        bool CanSelectRewardForTutorial(string cardId, out string reason);
        void ShowTutorialBlockedFeedback(string reason);
        void NotifyTutorialRewardSelected(string cardId);
        void NotifyTutorialCombatEvent(string eventId);
        TutorialDirector ResolveTutorialDirector(bool createIfMissing = false);
    }
}
