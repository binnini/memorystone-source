using System;
using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Combat.Unity
{
    public enum GameplayScreenId
    {
        Title,
        Briefing,
        Gameplay,
        Pause,
        GameOver,
        Clear
    }

    public enum CardHandLayoutMode
    {
        Drawer,
        Rail
    }

    public sealed class GameplaySceneContract : MonoBehaviour
    {
        public const string RootName = "UIUX Prototype Root";
        // Name of the dev-only PlayerState debug text object (created here in the DevOnly layer).
        // Moved from the now-deleted MapCombatHudView so the const outlives that legacy view.
        public const string PlayerStateDebugTextName = "M2 Integration HUD PlayerState Debug Text";
        public const string CanvasName = "UIUX Prototype Canvas";
        public const string ScreenTitleName = "SCREEN_TITLE";
        public const string ScreenBriefingName = "SCREEN_BRIEFING";
        public const string ScreenGameplayName = "SCREEN_GAMEPLAY";
        public const string ScreenPauseName = "SCREEN_PAUSE";
        public const string ScreenGameOverName = "SCREEN_GAMEOVER";
        public const string ScreenClearName = "SCREEN_CLEAR";
        public const string GameplayLayerRootName = "Gameplay UI Layers";
        public const string PlayerUiRootName = "Player UI Root";
        public const string DevUiRootName = "Dev UI Root";
        public const string WorldMapLayerName = "World Map Scene Window";
        public const string LegendLayerName = "Map Legend UI Hint";
        public const string HudLayerName = "Top HUD Layer";
        public const string TopLaneHudName = "Top Lane HUD";
        public const string PlayerStatusClusterName = "Player Status Cluster";
        public const string BagQuickSlotClusterName = "Bag Quick Slot Cluster";
        public const string MapInfoClusterName = "Map Info Cluster";
        public const string SystemButtonClusterName = "System Button Cluster";
        public const string RelicCurseRailName = "Relic Curse Icon Rail";
        public const string RelicCurseRailTextName = "Relic Curse Rail Text";
        public const string CardHandLayerName = "Hover Card Drawers Layer";
        public const string CardRailRootName = "Card Rail Root";
        public const string SystemUiLayerName = "Bottom Right Resource Layer";
        public const string DevOnlyLayerName = "DevOnly Removable Log Layer";
        public const string TacticalOverlayLayerName = "Tactical Overlay Layer";
        public const string EntityMarkerLayerName = "Entity Marker Layer";
        public const string DebugEvidenceName = "UIUX Debug Evidence";
        public const string CurrentRegionKey = "dong_seoul";
        public const string CurrentObjectiveCopy = "롯데타워 기억결 탈환";
        public const string CurrentBriefingCopy = "동서울 권역 / 광진구 어린이대공원 출발 / 송파구 롯데타워 기억결 탈환";
        public const string KoreanFontAssetPath = "Assets/Resources/Fonts/DNFForgedBlade-Light SDF.asset";
        public const float GameplayUiReferenceWidth = 2200f;
        public const float GameplayUiReferenceHeight = 1238f;
        public const float GameplayUiCanvasMatch = 0.5f;
        public static Vector2 GameplayUiReferenceResolution => new Vector2(GameplayUiReferenceWidth, GameplayUiReferenceHeight);

        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform[] screenRoots = Array.Empty<RectTransform>();
        [SerializeField] private RectTransform[] gameplayLayers = Array.Empty<RectTransform>();
        [SerializeField] private TMP_Text briefingText;
        [SerializeField] private TMP_Text objectiveText;
        [SerializeField] private TMP_Text debugEvidenceText;
        [SerializeField] private TMP_Text topHudText;
        [SerializeField] private TMP_Text bottomResourceText;
        [SerializeField] private TMP_Text devLogText;
        [SerializeField] private GameplayCardDrawer moveCardDrawer;
        [SerializeField] private GameplayCardDrawer actionCardDrawer;
        [SerializeField] private RectTransform playerUiRoot;
        [SerializeField] private RectTransform devUiRoot;
        [SerializeField] private RectTransform cardRailRoot;

        public Canvas Canvas => canvas;
        public IReadOnlyList<RectTransform> ScreenRoots => screenRoots;
        public IReadOnlyList<RectTransform> GameplayLayers => gameplayLayers;
        public TMP_Text BriefingText => briefingText;
        public TMP_Text ObjectiveText => objectiveText;
        public TMP_Text DebugEvidenceText => debugEvidenceText;
        public TMP_Text TopHudText => topHudText;
        public TMP_Text BottomResourceText => bottomResourceText;
        public TMP_Text DevLogText => devLogText;
        public GameplayCardDrawer MoveCardDrawer => moveCardDrawer;
        public GameplayCardDrawer ActionCardDrawer => actionCardDrawer;
        public RectTransform PlayerUiRoot => playerUiRoot != null ? playerUiRoot : FindRectTransform(PlayerUiRootName);
        public RectTransform DevUiRoot => devUiRoot != null ? devUiRoot : FindRectTransform(DevUiRootName);
        public RectTransform CardRailRoot => cardRailRoot != null ? cardRailRoot : FindRectTransform(CardRailRootName);

        private static readonly string[] MapClickThroughPanelNames =
        {
            ScreenGameplayName,
            GameplayLayerRootName,
            WorldMapLayerName,
            LegendLayerName,
            HudLayerName,
            PlayerUiRootName,
            TopLaneHudName,
            PlayerStatusClusterName,
            BagQuickSlotClusterName,
            MapInfoClusterName,
            SystemButtonClusterName,
            RelicCurseRailName,
            SystemUiLayerName,
            CardRailRootName,
            DevUiRootName,
            DevOnlyLayerName,
            DebugEvidenceName
        };

        public void Bind(
            Canvas sceneCanvas,
            RectTransform[] screens,
            RectTransform[] layers,
            TMP_Text briefing,
            TMP_Text objective,
            TMP_Text debugEvidence,
            TMP_Text topHud,
            TMP_Text bottomResource,
            TMP_Text devLog,
            GameplayCardDrawer moveDrawer,
            GameplayCardDrawer actionDrawer,
            RectTransform playerRoot = null,
            RectTransform devRoot = null,
            RectTransform railRoot = null)
        {
            canvas = sceneCanvas;
            screenRoots = screens ?? Array.Empty<RectTransform>();
            gameplayLayers = layers ?? Array.Empty<RectTransform>();
            briefingText = briefing;
            objectiveText = objective;
            debugEvidenceText = debugEvidence;
            topHudText = topHud;
            bottomResourceText = bottomResource;
            devLogText = devLog;
            moveCardDrawer = moveDrawer;
            actionCardDrawer = actionDrawer;
            playerUiRoot = playerRoot;
            devUiRoot = devRoot;
            cardRailRoot = railRoot;
            EnsurePlayerDevAndRailRoots();
            ConfigureMapClickThroughRaycastTargets();
        }

        public void EnsurePlayerDevAndRailRoots()
        {
            // PrototypeTest now uses external gameplay UI roots as the authored UI surface.
            // Keep this legacy entry point as a non-mutating reference refresh so older callers
            // cannot recreate/reparent the removed UIUX Prototype HUD scaffolds.
            playerUiRoot = FindRectTransform(PlayerUiRootName);
            devUiRoot = FindRectTransform(DevUiRootName);
            cardRailRoot = FindRectTransform(CardRailRootName);
        }

        public void ConfigureMapClickThroughRaycastTargets()
        {
            for (var i = 0; i < MapClickThroughPanelNames.Length; i++)
            {
                SetOwnImageRaycastTarget(MapClickThroughPanelNames[i], false);
            }

            DisableTextRaycastTargets();
            SetDrawerRaycastTarget(moveCardDrawer, true);
            SetDrawerRaycastTarget(actionCardDrawer, true);
        }

        private void DisableTextRaycastTargets()
        {
            var labels = GetComponentsInChildren<TMP_Text>(includeInactive: true);
            for (var i = 0; i < labels.Length; i++)
            {
                if (labels[i] != null)
                {
                    labels[i].raycastTarget = false;
                }
            }
        }

        private void SetOwnImageRaycastTarget(string objectName, bool raycastTarget)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return;
            }

            var transforms = GetComponentsInChildren<Transform>(includeInactive: true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var current = transforms[i];
                if (current == null || current.name != objectName)
                {
                    continue;
                }

                var image = current.GetComponent<Image>();
                if (image != null)
                {
                    image.raycastTarget = raycastTarget;
                }
            }
        }

        private static void SetDrawerRaycastTarget(GameplayCardDrawer drawer, bool raycastTarget)
        {
            if (drawer == null)
            {
                return;
            }

            var image = drawer.GetComponent<Image>();
            if (image != null)
            {
                image.raycastTarget = raycastTarget;
            }
        }

        public bool HasRequiredScreen(string screenName)
        {
            if (screenRoots == null)
            {
                return false;
            }

            for (var i = 0; i < screenRoots.Length; i++)
            {
                if (screenRoots[i] != null && screenRoots[i].name == screenName)
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasRequiredGameplayLayer(string layerName)
        {
            if (gameplayLayers == null)
            {
                return false;
            }

            for (var i = 0; i < gameplayLayers.Length; i++)
            {
                if (gameplayLayers[i] != null && gameplayLayers[i].name == layerName)
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasRequiredGameplayRoot(string rootName)
        {
            return FindRectTransform(rootName) != null;
        }

        private RectTransform FindRectTransform(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return null;
            }

            var transforms = GetComponentsInChildren<RectTransform>(includeInactive: true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null && transforms[i].name == objectName)
                {
                    return transforms[i];
                }
            }

            return null;
        }

    }

    // Prototype-only UI generator used by the current integration scenes.
    // Do not treat this as the production UI composition contract; if this scene graduates,
    // move layout/copy into prefabs or explicit UI data assets instead of growing this helper.
    internal static class GameplaySceneFactory
    {
        private static readonly Color TitleColor = new Color(0.04f, 0.06f, 0.09f, 0.96f);
        private static readonly Color BriefingColor = new Color(0.07f, 0.08f, 0.11f, 0.96f);
        private static readonly Color GameplayColor = new Color(0f, 0f, 0f, 0f);
        private static readonly Color PauseColor = new Color(0.05f, 0.05f, 0.07f, 0.96f);
        private static readonly Color FailColor = new Color(0.13f, 0.04f, 0.04f, 0.96f);
        private static readonly Color ClearColor = new Color(0.04f, 0.11f, 0.08f, 0.96f);
        private static readonly Color PrimaryTextColor = new Color(0.88f, 0.95f, 1f, 1f);
        private static readonly Color MutedTextColor = new Color(0.62f, 0.75f, 0.82f, 1f);
        private static readonly Color HudPanelColor = new Color(1f, 1f, 1f, 0.82f);
        private static readonly Color LightPanelColor = new Color(1f, 1f, 1f, 0.66f);
        private static readonly Color DrawerPanelColor = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color CardPrimaryTextColor = new Color(0.09f, 0.1f, 0.11f, 1f);
        private static readonly Color CardMutedTextColor = new Color(0.34f, 0.37f, 0.38f, 1f);
        private static readonly Color CardAccentColor = new Color(0.13f, 0.38f, 0.56f, 1f);
        private static readonly Color CardTitleBackdropColor = new Color(0.02f, 0.025f, 0.035f, 0.72f);
        private static readonly Color CardTitleTextColor = new Color(0.96f, 0.99f, 1f, 1f);
        private const string MoveCardBackgroundPath = "UI/Cards/Exorcism/card_front_move";
        private const string AttackCardBackgroundPath = "UI/Cards/Exorcism/card_front_attack";
        private const string HeavyAttackCardBackgroundPath = "UI/Cards/Exorcism/card_front_heavy_attack";
        private const string DefendCardBackgroundPath = "UI/Cards/Exorcism/card_front_defend";
        private const string ScoutInvestigateCardBackgroundPath = "UI/Cards/Exorcism/card_front_scout_investigate";
        private const string CardSlotBackgroundPath = "UI/HUD/card_slot_bg";
        private const float SampleCardWidth = 106f;
        private const float SampleCardHeight = 150f;
        private const float SampleCardStride = 70f;
        private static TMP_FontAsset koreanFontAsset;
        private static TMP_FontAsset titleFontAsset;
        private static Sprite cardSlotBackground;
        private static Sprite moveCardBackground;
        private static Sprite attackCardBackground;
        private static Sprite heavyAttackCardBackground;
        private static Sprite defendCardBackground;
        private static Sprite scoutInvestigateCardBackground;

        private static readonly SampleCard[] TestMoveDeck =
        {
            new SampleCard(ApprovedCardCatalogFactory.MoveBasicId, "2칸 이동", "Move", "최대 2칸 이동합니다", 1, "Ready", MoveCardBackgroundPath, true),
            new SampleCard(ApprovedCardCatalogFactory.MoveMomentumId, "추진력", "Move", "이번 턴 이동 -1 / 다음 턴 이동 +2", 1, "Playable", MoveCardBackgroundPath, true),
            new SampleCard(ApprovedCardCatalogFactory.MoveRandomJourneyId, "도착지를 모르는 여행", "Move", "범위 2칸 중 무작위 이동", 1, "Need target", MoveCardBackgroundPath, true)
        };

        private static readonly SampleCard[] TestActionDeck =
        {
            new SampleCard(ApprovedCardCatalogFactory.AttackSweepId, "휩쓸기", "Attack", "주위 1칸 광역 피해", 1, CombatCardStatusText.AttackOutOfRange, HeavyAttackCardBackgroundPath, true),
            new SampleCard(ApprovedCardCatalogFactory.AttackMoveLinkedId, "파발꾼도 공격하고 싶어", "Attack", "이번 턴 이동 칸 수만큼 피해", 1, "Ready", AttackCardBackgroundPath, true),
            new SampleCard(ApprovedCardCatalogFactory.DefendOldSuitId, "낡은 방호복", "Defend", "다음 받을 피해 -5", 1, "Playable", DefendCardBackgroundPath, true),
            new SampleCard(ApprovedCardCatalogFactory.ScoutMinefinderId, "지뢰탐지기", "Scout", "반경 범위 정찰 후 N 피해", 1, "Playable", ScoutInvestigateCardBackgroundPath, true),
            new SampleCard(ApprovedCardCatalogFactory.FieldSacredCampfireId, "신성한 모닥불", "Field", "방해 + 위치 조건 회복", 1, "Playable", ScoutInvestigateCardBackgroundPath, true),
            new SampleCard(ApprovedCardCatalogFactory.FieldFlashbangId, "섬광탄", "Field", "방해 + 공격 감소 + 이동불능", 1, "Playable", ScoutInvestigateCardBackgroundPath, true)
        };

        public static GameplaySceneContract Build(Transform parent = null)
        {
            var root = new GameObject(GameplaySceneContract.RootName);
            if (parent != null)
            {
                root.transform.SetParent(parent, false);
            }

            var contract = root.AddComponent<GameplaySceneContract>();
            var eventSystemObject = new GameObject("UIUX Prototype EventSystem", typeof(EventSystem));
            eventSystemObject.transform.SetParent(root.transform, false);
            eventSystemObject.AddComponent<InputSystemUIInputModule>();

            var canvasObject = new GameObject(GameplaySceneContract.CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(root.transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = GameplaySceneContract.GameplayUiReferenceResolution;
            scaler.matchWidthOrHeight = GameplaySceneContract.GameplayUiCanvasMatch;

            var canvasRect = canvasObject.GetComponent<RectTransform>();
            Stretch(canvasRect);

            var title = CreateScreen(canvasRect, GameplaySceneContract.ScreenTitleName, TitleColor);
            CreateHeader(title, "SEOUL PLAYUP", "새 게임 시작  /  설정  /  종료");

            var briefing = CreateScreen(canvasRect, GameplaySceneContract.ScreenBriefingName, BriefingColor);
            var briefingText = CreateHeader(briefing, "미션 브리핑", GameplaySceneContract.CurrentBriefingCopy + "\n보스 출현 전 현재 목표를 로데이터 기록에 반영합니다.");

            var gameplay = CreateScreen(canvasRect, GameplaySceneContract.ScreenGameplayName, GameplayColor);
            var layers = BuildGameplayLayers(
                gameplay,
                out var objectiveText,
                out var debugText,
                out var topHudText,
                out var bottomResourceText,
                out var devLogText,
                out var moveDrawer,
                out var actionDrawer,
                out var playerRoot,
                out var devRoot,
                out var railRoot);

            var pause = CreateScreen(canvasRect, GameplaySceneContract.ScreenPauseName, PauseColor);
            CreateHeader(pause, "일시정지", "게임 재개  /  설정(TBD)  /  메인 메뉴  /  종료");

            var gameOver = CreateScreen(canvasRect, GameplaySceneContract.ScreenGameOverName, FailColor);
            CreateHeader(gameOver, "게임오버", "실패 원인: HP 0, 몬스터 공격, 잘못된 행동 등을 표시합니다.");

            var clear = CreateScreen(canvasRect, GameplaySceneContract.ScreenClearName, ClearColor);
            CreateHeader(clear, "클리어", "로데이터 기록 성공: 목표 Revealed + 유효 위치/사거리 + Investigate");

            contract.Bind(
                canvas,
                new[] { title, briefing, gameplay, pause, gameOver, clear },
                layers,
                briefingText,
                objectiveText,
                debugText,
                topHudText,
                bottomResourceText,
                devLogText,
                moveDrawer,
                actionDrawer,
                playerRoot,
                devRoot,
                railRoot);

            return contract;
        }

        private static RectTransform[] BuildGameplayLayers(
            RectTransform gameplay,
            out TMP_Text objectiveText,
            out TMP_Text debugText,
            out TMP_Text topHudText,
            out TMP_Text bottomResourceText,
            out TMP_Text devLogText,
            out GameplayCardDrawer moveDrawer,
            out GameplayCardDrawer actionDrawer,
            out RectTransform playerRoot,
            out RectTransform devRoot,
            out RectTransform railRoot)
        {
            var layerRoot = CreatePanel(GameplaySceneContract.GameplayLayerRootName, gameplay, Color.clear);
            Stretch(layerRoot);

            playerRoot = CreatePanel(GameplaySceneContract.PlayerUiRootName, layerRoot, Color.clear);
            Stretch(playerRoot);

            devRoot = CreatePanel(GameplaySceneContract.DevUiRootName, layerRoot, Color.clear);
            Stretch(devRoot);

            var worldMap = CreatePanel(GameplaySceneContract.WorldMapLayerName, playerRoot, new Color(1f, 1f, 1f, 0.05f));
            Stretch(worldMap, new Vector2(0f, 94f), new Vector2(0f, -74f));
            var worldMapLabel = CreateText("World Map Scene Label", worldMap, "실제 Hex Map Scene 영역\nCanvas UI는 맵 위에 오버레이를 그리지 않음", 28, FontStyles.Bold, new Color(0.22f, 0.24f, 0.26f, 0.55f), TextAlignmentOptions.Center, Vector2.zero, new Vector2(720f, 96f));
            worldMapLabel.gameObject.SetActive(false);

            var legend = CreatePanel(GameplaySceneContract.LegendLayerName, playerRoot, LightPanelColor);
            Anchor(legend, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(245f, 232f), new Vector2(18f, 32f));
            var legendText = CreateText("Legend Text", legend, "범례\n● 이동 가능\n● 행동 가능\n◇ 몬스터 이동\n◇ 몬스터 공격\n◆ 맵 오버레이 해당", 15, FontStyles.Normal, PrimaryTextColor, TextAlignmentOptions.TopLeft, new Vector2(14f, -14f), new Vector2(214f, 202f));
            legendText.textWrappingMode = TextWrappingModes.NoWrap;
            legendText.overflowMode = TextOverflowModes.Truncate;

            var hud = CreatePanel(GameplaySceneContract.HudLayerName, playerRoot, Color.clear);
            Stretch(hud, new Vector2(12f, -98f), new Vector2(-12f, -12f), new Vector2(0f, 1f), new Vector2(1f, 1f));

            var topLane = CreatePanel(GameplaySceneContract.TopLaneHudName, hud, HudPanelColor);
            Anchor(topLane, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 92f), new Vector2(0f, 0f));

            var playerStatus = CreatePanel(GameplaySceneContract.PlayerStatusClusterName, topLane, Color.clear);
            Anchor(playerStatus, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(310f, 48f), new Vector2(12f, -8f));
            topHudText = CreateText("Top HUD Text", playerStatus, "초상화  HP --/--  Coin 0", 17, FontStyles.Bold, PrimaryTextColor, TextAlignmentOptions.Left, new Vector2(8f, -8f), new Vector2(292f, 30f));
            topHudText.textWrappingMode = TextWrappingModes.NoWrap;

            var bagCluster = CreatePanel(GameplaySceneContract.BagQuickSlotClusterName, topLane, new Color(0f, 0f, 0f, 0.1f));
            Anchor(bagCluster, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(190f, 48f), new Vector2(330f, -8f));
            CreateText("Bag Quick Slot Text", bagCluster, "가방  물약  빈칸", 15, FontStyles.Bold, PrimaryTextColor, TextAlignmentOptions.Center, new Vector2(8f, -10f), new Vector2(174f, 24f));

            var mapInfo = CreatePanel(GameplaySceneContract.MapInfoClusterName, topLane, Color.clear);
            Anchor(mapInfo, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(250f, 48f), new Vector2(0f, -8f));
            CreateText("Map Info Stub Text", mapInfo, "계단  --", 16, FontStyles.Bold, MutedTextColor, TextAlignmentOptions.Center, new Vector2(8f, -10f), new Vector2(234f, 24f));

            var systemButtons = CreatePanel(GameplaySceneContract.SystemButtonClusterName, topLane, Color.clear);
            Anchor(systemButtons, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(390f, 48f), new Vector2(-12f, -8f));
            // No clock glyph: U+23F1 is outside the DNFForgedBlade faces and would render as tofu.
            CreateText("System Button Text", systemButtons, "00:00   지도  --   설정", 16, FontStyles.Bold, PrimaryTextColor, TextAlignmentOptions.Right, new Vector2(8f, -10f), new Vector2(374f, 24f));

            var relicRail = CreatePanel(GameplaySceneContract.RelicCurseRailName, topLane, new Color(0f, 0f, 0f, 0.08f));
            Anchor(relicRail, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 32f), new Vector2(12f, 8f));
            var relicRailText = CreateText(GameplaySceneContract.RelicCurseRailTextName, relicRail, "유물 없음", 13, FontStyles.Bold, MutedTextColor, TextAlignmentOptions.Left, new Vector2(8f, -6f), new Vector2(400f, 20f));
            relicRailText.textWrappingMode = TextWrappingModes.NoWrap;
            objectiveText = null;

            var cardHand = CreatePanel(GameplaySceneContract.CardHandLayerName, playerRoot, Color.clear);
            Stretch(cardHand);
            moveDrawer = CreateCardDrawer(cardHand, "MoveCardDrawer", "이동 카드", TestMoveDeck, "MoveDeck 12 / discard 0", new Vector2(-250f, 42f), new Vector2(-250f, 158f));
            actionDrawer = CreateCardDrawer(cardHand, "ActionCardDrawer", "행동 카드", TestActionDeck, "ActionDeck 10 / discard 1", new Vector2(245f, 42f), new Vector2(245f, 158f));
            railRoot = CreateCardRailRoot(cardHand);

            var systemUi = CreatePanel(GameplaySceneContract.SystemUiLayerName, playerRoot, LightPanelColor);
            Anchor(systemUi, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(346f, 122f), new Vector2(-18f, 22f));
            bottomResourceText = CreateText("Bottom Resource Text", systemUi, "기력 3 / 4    코인 0\n이동덱 12   행동덱 10   버린 더미 1\n테스트덱 Move 4 / Action 4", 17, FontStyles.Bold, PrimaryTextColor, TextAlignmentOptions.Center, new Vector2(16f, -18f), new Vector2(298f, 86f));
            Stretch(bottomResourceText.GetComponent<RectTransform>(), new Vector2(14f, 42f), new Vector2(-14f, -10f));
            bottomResourceText.fontSize = 16f;
            bottomResourceText.alignment = TextAlignmentOptions.TopLeft;
            bottomResourceText.textWrappingMode = TextWrappingModes.NoWrap;
            bottomResourceText.overflowMode = TextOverflowModes.Truncate;

            var devOnly = CreatePanel(GameplaySceneContract.DevOnlyLayerName, devRoot, new Color(0f, 0f, 0f, 0.18f));
            Anchor(devOnly, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(365f, 136f), new Vector2(-18f, 346f));
            devLogText = CreateText("DevOnly Log Text", devOnly, "DevOnly removable log: [10:21] 광진구 이동 / 오버레이 이벤트 추적", 14, FontStyles.Normal, new Color(1f, 1f, 1f, 0.78f), TextAlignmentOptions.TopLeft, new Vector2(12f, -10f), new Vector2(340f, 42f));
            CreateText(GameplaySceneContract.PlayerStateDebugTextName, devOnly, string.Empty, 9, FontStyles.Normal, new Color(0.62f, 0.75f, 0.82f, 1f), TextAlignmentOptions.TopLeft, new Vector2(12f, -58f), new Vector2(336f, 68f));

            var debug = CreatePanel(GameplaySceneContract.DebugEvidenceName, devRoot, new Color(0f, 0f, 0f, 0.22f));
            Anchor(debug, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(365f, 46f), new Vector2(-18f, 294f));
            debugText = CreateText("Debug Evidence Text", debug, "debug: region_key=dong_seoul / objective_refs=lotte_tower_memory_gyeol / map_overlay_owned_by=map_scene_system / dev_log_removable=true", 12, FontStyles.Normal, new Color(1f, 1f, 1f, 0.64f), TextAlignmentOptions.TopLeft, new Vector2(12f, -9f), new Vector2(340f, 28f));
            debugText.overflowMode = TextOverflowModes.Truncate;

            playerRoot.SetSiblingIndex(0);
            devRoot.SetSiblingIndex(1);

            return new[] { worldMap, legend, hud, cardHand, systemUi, devOnly };
        }

        private static RectTransform CreateCardRailRoot(Transform parent)
        {
            var rail = CreatePanel(GameplaySceneContract.CardRailRootName, parent, DrawerPanelColor);
            Anchor(rail, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(780f, 196f), new Vector2(0f, 20f));
            rail.GetComponent<Image>().raycastTarget = false;
            var label = CreateText("Card Rail Label", rail, "카드 레일  ·  현재 페이즈 카드", 14, FontStyles.Bold, PrimaryTextColor, TextAlignmentOptions.Center, new Vector2(14f, -8f), new Vector2(752f, 22f));
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Truncate;
            var status = CreateText("Card Rail Deck Status", rail, "Rail waiting", 10, FontStyles.Normal, MutedTextColor, TextAlignmentOptions.Center, new Vector2(14f, -30f), new Vector2(752f, 16f));
            status.textWrappingMode = TextWrappingModes.NoWrap;
            status.overflowMode = TextOverflowModes.Truncate;
            rail.gameObject.SetActive(false);
            return rail;
        }

        private static GameplayCardDrawer CreateCardDrawer(Transform parent, string name, string title, IReadOnlyList<SampleCard> cards, string deckStatus, Vector2 collapsed, Vector2 expanded)
        {
            var drawer = CreatePanel(name, parent, DrawerPanelColor);
            Anchor(drawer, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(410f, 206f), collapsed);
            drawer.GetComponent<Image>().raycastTarget = true;
            var drawerState = drawer.gameObject.AddComponent<GameplayCardDrawer>();
            drawerState.Bind(title, collapsed, expanded, true);
            var label = CreateText(name + " Label", drawer, title + "  · hover", 14, FontStyles.Bold, PrimaryTextColor, TextAlignmentOptions.Center, new Vector2(14f, -8f), new Vector2(382f, 22f));
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Truncate;
            var statusText = CreateText(name + " Deck Status", drawer, deckStatus + " / 기력 비용 표시", 13, FontStyles.Normal, MutedTextColor, TextAlignmentOptions.Center, new Vector2(14f, -30f), new Vector2(382f, 16f));
            statusText.textWrappingMode = TextWrappingModes.NoWrap;
            statusText.overflowMode = TextOverflowModes.Truncate;
            var count = cards == null ? 0 : cards.Count;
            for (var i = 0; i < count; i++)
            {
                CreateSampleCard(drawer, name, cards[i], i, count);
            }

            return drawerState;
        }

        private static void CreateSampleCard(RectTransform parent, string drawerName, SampleCard card, int index, int count)
        {
            var root = CreatePanel($"{drawerName} Test Card {card.Id}", parent, card.IsPlayable ? Color.white : new Color(0.76f, 0.76f, 0.8f, 0.86f));
            root.anchorMin = new Vector2(0.5f, 0f);
            root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(SampleCardWidth, SampleCardHeight);
            var offset = count <= 1 ? 0f : index - (count - 1) * 0.5f;
            root.anchoredPosition = new Vector2(offset * SampleCardStride, -2f + Mathf.Abs(offset) * 5f);
            root.localEulerAngles = new Vector3(0f, 0f, Mathf.Clamp(-offset * 4f, -8f, 8f));

            var background = root.GetComponent<Image>();
            ApplyCardSprite(background, card);
            background.raycastTarget = true;

            var titleBackdrop = CreatePanel($"{drawerName} Test Card {card.Id} Title Backdrop", root, CardTitleBackdropColor);
            titleBackdrop.anchorMin = new Vector2(0f, 1f);
            titleBackdrop.anchorMax = new Vector2(0f, 1f);
            titleBackdrop.pivot = new Vector2(0f, 1f);
            titleBackdrop.anchoredPosition = new Vector2(5f, -7f);
            titleBackdrop.sizeDelta = new Vector2(96f, 34f);
            titleBackdrop.GetComponent<Image>().raycastTarget = false;

            var title = CreateText($"{drawerName} Test Card {card.Id} Title", root, card.Name, 15, FontStyles.Bold, CardTitleTextColor, TextAlignmentOptions.Center, new Vector2(6f, -9f), new Vector2(94f, 32f));
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Overflow;
            CreateText($"{drawerName} Test Card {card.Id} Type", root, card.TypeLabel, 9, FontStyles.Bold, CardAccentColor, TextAlignmentOptions.Center, new Vector2(12f, -104f), new Vector2(82f, 14f));
            CreateText($"{drawerName} Test Card {card.Id} Description", root, card.Description, 8, FontStyles.Normal, CardPrimaryTextColor, TextAlignmentOptions.Center, new Vector2(10f, -120f), new Vector2(86f, 22f));
            CreateText($"{drawerName} Test Card {card.Id} Status", root, $"기력 {card.Cost} · {card.Status}", 7, FontStyles.Normal, card.IsPlayable ? CardMutedTextColor : new Color(0.58f, 0.12f, 0.12f, 1f), TextAlignmentOptions.Center, new Vector2(8f, -142f), new Vector2(90f, 12f));

            root.gameObject.AddComponent<HandCardInteraction>();
            root.gameObject.AddComponent<GameplayCardInteractionBinder>()
                .Bind(card.Id, card.ToCombatCardKind(), card.Name, card.Description, card.Cost, card.Status, card.IsPlayable);
        }

        private static void ApplyCardSprite(Image image, SampleCard card)
        {
            var sprite = GetCardSprite(card.SpritePath);
            if (sprite == null)
            {
                image.sprite = GetCardSprite(CardSlotBackgroundPath);
                image.color = card.IsPlayable ? new Color(1f, 1f, 1f, 0.92f) : new Color(0.72f, 0.72f, 0.76f, 0.86f);
                return;
            }

            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = card.IsPlayable ? Color.white : new Color(0.58f, 0.58f, 0.62f, 0.86f);
        }

        private static Sprite GetCardSprite(string spritePath)
        {
            switch (spritePath)
            {
                case MoveCardBackgroundPath:
                    return moveCardBackground ?? (moveCardBackground = Resources.Load<Sprite>(MoveCardBackgroundPath));
                case AttackCardBackgroundPath:
                    return attackCardBackground ?? (attackCardBackground = Resources.Load<Sprite>(AttackCardBackgroundPath));
                case HeavyAttackCardBackgroundPath:
                    return heavyAttackCardBackground ?? (heavyAttackCardBackground = Resources.Load<Sprite>(HeavyAttackCardBackgroundPath));
                case DefendCardBackgroundPath:
                    return defendCardBackground ?? (defendCardBackground = Resources.Load<Sprite>(DefendCardBackgroundPath));
                case ScoutInvestigateCardBackgroundPath:
                    return scoutInvestigateCardBackground ?? (scoutInvestigateCardBackground = Resources.Load<Sprite>(ScoutInvestigateCardBackgroundPath));
                case CardSlotBackgroundPath:
                    return cardSlotBackground ?? (cardSlotBackground = Resources.Load<Sprite>(CardSlotBackgroundPath));
                default:
                    return null;
            }
        }

        private static RectTransform CreateScreen(RectTransform parent, string name, Color color)
        {
            var rect = CreatePanel(name, parent, color);
            Stretch(rect);
            rect.gameObject.SetActive(name == GameplaySceneContract.ScreenGameplayName);
            return rect;
        }

        private static TMP_Text CreateHeader(RectTransform parent, string title, string body)
        {
            // Full-screen headers are the title bucket: Medium face, not the Light UI body face.
            CreateText(title + " Title", parent, title, 42, FontStyles.Bold, new Color(0.94f, 0.95f, 0.9f, 1f), TextAlignmentOptions.TopLeft, new Vector2(80f, -72f), new Vector2(900f, 64f), useTitleFont: true);
            return CreateText(title + " Body", parent, body, 24, FontStyles.Normal, new Color(0.94f, 0.95f, 0.9f, 1f), TextAlignmentOptions.TopLeft, new Vector2(84f, -152f), new Vector2(980f, 220f), useTitleFont: true);
        }

        private static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            var image = panel.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = color.a > 0.2f;
            return panel.GetComponent<RectTransform>();
        }

        private static TMP_Text CreateText(
            string name,
            Transform parent,
            string text,
            int fontSize,
            FontStyles style,
            Color color,
            TextAlignmentOptions alignment,
            Vector2? anchoredPosition = null,
            Vector2? sizeDelta = null,
            bool useTitleFont = false)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition ?? new Vector2(20f, -20f);
            rect.sizeDelta = sizeDelta ?? new Vector2(720f, 120f);

            var label = textObject.GetComponent<TMP_Text>();
            label.font = useTitleFont ? LoadTitleFontAsset() : LoadKoreanFontAsset();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            return label;
        }

        private static TMP_FontAsset LoadKoreanFontAsset()
        {
            if (koreanFontAsset != null)
            {
                return koreanFontAsset;
            }

            koreanFontAsset = KoreanFontProvider.Load();
            return koreanFontAsset;
        }

        private static TMP_FontAsset LoadTitleFontAsset()
        {
            if (titleFontAsset != null)
            {
                return titleFontAsset;
            }

            titleFontAsset = KoreanFontProvider.LoadTitle();
            return titleFontAsset;
        }

        private static void Stretch(RectTransform rect)
        {
            Stretch(rect, Vector2.zero, Vector2.zero);
        }

        private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            Stretch(rect, offsetMin, offsetMax, Vector2.zero, Vector2.one);
        }

        private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
        }

        private static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Vector2 anchoredPosition)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
            rect.localScale = Vector3.one;
        }

        private readonly struct SampleCard
        {
            public SampleCard(string id, string name, string typeLabel, string description, int cost, string status, string spritePath, bool isPlayable)
            {
                Id = id;
                Name = name;
                TypeLabel = typeLabel;
                Description = description;
                Cost = cost;
                Status = status;
                SpritePath = spritePath;
                IsPlayable = isPlayable;
            }

            public string Id { get; }
            public string Name { get; }
            public string TypeLabel { get; }
            public string Description { get; }
            public int Cost { get; }
            public string Status { get; }
            public string SpritePath { get; }
            public bool IsPlayable { get; }

            public CombatCardKind ToCombatCardKind()
            {
                return ToCombatCardKind(TypeLabel);
            }

            private static CombatCardKind ToCombatCardKind(string typeLabel)
            {
                switch (typeLabel)
                {
                    case "Move":
                        return CombatCardKind.Move;
                    case "Attack":
                        return CombatCardKind.Attack;
                    case "Defend":
                        return CombatCardKind.Defend;
                    case "Scout":
                        return CombatCardKind.Scout;
                    case "Investigate":
                        return CombatCardKind.Investigate;
                    case "Field":
                        return CombatCardKind.FieldObject;
                    case "Buff":
                        return CombatCardKind.Buff;
                    case "Utility":
                        return CombatCardKind.Utility;
                    default:
                        return CombatCardKind.Move;
                }
            }
        }
    }
}


