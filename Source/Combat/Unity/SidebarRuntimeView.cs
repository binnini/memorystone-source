using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    [DisallowMultipleComponent]
    public sealed class SidebarRuntimeView : MonoBehaviour
    {
        private const string RuntimeRootName = "Sidebar Runtime Content";
        private const string CurrencyButtonName = "Sidebar Button currency";

        [SerializeField] private SidebarCalloutPanelController controller;
        [SerializeField] private SidebarCalloutPanelSettings settings;
        [SerializeField] private SidebarBagPanelView bagPanelView;
        [SerializeField] private SidebarDeckPanelView deckPanelView;
        [SerializeField] private SidebarRelicCursePanelView relicCursePanelView;
        [SerializeField] private SidebarSettingsPanelView settingsPanelView;

        // Rename-safe reference to the sidebar's turn-counter label. When wired in the prefab this survives a
        // rename of the "CurTurnText" GameObject; when null it falls back to the legacy by-name lookup so the
        // view keeps working un-wired (identical behaviour).
        [SerializeField] private TMP_Text curTurnTextRef;

        // Same rename-safe pattern for the sidebar's currency (엽전) readout. The authored label under
        // "Sidebar Button currency" ships with a placeholder ("1234"); without this binder it stays frozen.
        [SerializeField] private TMP_Text currencyTextRef;

        private readonly Dictionary<string, string> signatures = new Dictionary<string, string>();
        private TMP_FontAsset uiFont;
        private TMP_Text curTurnText;
        private TMP_Text currencyText;

        public void Refresh(CombatState state, PlayerStateSnapshot snapshot, TMP_FontAsset font)
        {
            ResolveReferences();
            uiFont = font;

            RefreshBag(state);
            RefreshDeck(state);
            RefreshCurrency(state);
            RefreshRelicsAndCurses(state);
            RefreshSettings(state);
            RefreshCurrentTurn(state);
        }

        // Drive the sidebar's currency button with the live wallet balance. The button is display-only
        // (non-interactable, no callout panel), so this label is the only thing it does.
        private void RefreshCurrency(CombatState state)
        {
            if (currencyText == null)
            {
                currencyText = currencyTextRef != null
                    ? currencyTextRef
                    : GetComponentsInChildren<TMP_Text>(includeInactive: true)
                        .FirstOrDefault(label => label != null
                            && label.transform.parent != null
                            && label.transform.parent.name == CurrencyButtonName);
            }

            if (currencyText == null)
            {
                return;
            }

            var balance = state?.PlayerInventory?.Wallet?.Balance ?? 0;
            var signature = $"currency|{balance}";
            if (signatures.TryGetValue("currency", out var previous) && previous == signature)
            {
                return;
            }

            if (uiFont != null)
            {
                currencyText.font = uiFont;
            }

            currencyText.text = balance.ToString("N0");
            RememberSignature("currency", signature);
        }

        // Drive the sidebar's CurTurnText dock with the live overall turn number ("{N}턴"). The dock is an
        // authored child with a placeholder ("16턴") and no other binder, so it would otherwise stay frozen.
        private void RefreshCurrentTurn(CombatState state)
        {
            if (curTurnText == null)
            {
                curTurnText = curTurnTextRef != null
                    ? curTurnTextRef
                    : GetComponentsInChildren<TMP_Text>(includeInactive: true)
                        .FirstOrDefault(label => label != null && label.name == "CurTurnText");
            }

            if (curTurnText == null)
            {
                return;
            }

            var turn = state?.OverallTurnNumber ?? 0;
            var signature = $"curturn|{turn}";
            if (signatures.TryGetValue("curturn", out var previous) && previous == signature)
            {
                return;
            }

            if (uiFont != null)
            {
                curTurnText.font = uiFont;
            }

            curTurnText.text = $"{turn}턴";
            RememberSignature("curturn", signature);
        }

        // In-game ESC opens (and toggles) the settings callout directly, so the player doesn't have to find
        // the sidebar button. Polls the Input System device directly (legacy UnityEngine.Input throws here).
        private void Update()
        {
            // P5: when the scene hosts a pause menu (MainGameplay flow), ESC belongs to it — the
            // pause menu embeds the sound settings. This legacy shortcut stays only for scenes
            // without one (PrototypeTest sandbox).
            if (CombatPauseMenuController.ActiveInstance != null)
            {
                return;
            }

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            // Don't open settings when a deck/discard pile overlay is using this ESC to close itself. Checking
            // both its live open-state and the frame it consumed ESC makes this independent of Update order.
            if (DeckPileListOverlayView.EscConsumedFrame == Time.frameCount)
            {
                return;
            }

            var pileOverlay = FindObjectOfType<DeckPileListOverlayView>();
            if (pileOverlay != null && pileOverlay.IsOpen)
            {
                return;
            }

            // 상점 모달도 같은 우선순위: 열려 있는 동안의 ESC는 상점이 소비한다.
            if (ShopPopupView.EscConsumedFrame == Time.frameCount)
            {
                return;
            }

            var shopPopup = FindObjectOfType<ShopPopupView>();
            if (shopPopup != null && shopPopup.IsOpen)
            {
                return;
            }

            // 서비스 모달(캠핑카·공작소)도 같은 우선순위: 열려 있는 동안의 ESC는 모달이 소비한다.
            if (ServiceObjectPopupView.EscConsumedFrame == Time.frameCount)
            {
                return;
            }

            var servicePopup = FindObjectOfType<ServiceObjectPopupView>();
            if (servicePopup != null && servicePopup.IsOpen)
            {
                return;
            }

            if (controller == null)
            {
                ResolveReferences();
            }

            controller?.TogglePanel("settings");
        }

        /// <summary>가방 패널 뷰(T4-1 클릭-사용 라우팅용). 지연 해석되므로 호출 시점에 해석을 보장한다.</summary>
        public SidebarBagPanelView BagPanelView
        {
            get
            {
                if (bagPanelView == null)
                {
                    ResolveReferences();
                }

                return bagPanelView;
            }
        }

        /// <summary>이 사이드바의 콜아웃 패널 컨트롤러(열고 닫는 주인). 타게팅을 시작하는 쪽이
        /// 패널을 닫아야 맵 클릭이 도착한다(2026-09-05).</summary>
        public SidebarCalloutPanelController PanelController => controller;

        public void Bind(SidebarCalloutPanelController panelController, SidebarCalloutPanelSettings panelSettings = null)
        {
            controller = panelController;
            settings = panelSettings;
            ResolveReferences();
            signatures.Clear();
        }

        private void ResolveReferences()
        {
            controller = controller != null ? controller : GetComponent<SidebarCalloutPanelController>();
            settings = settings != null ? settings : GetComponent<SidebarCalloutPanelSettings>();
            bagPanelView = bagPanelView != null ? bagPanelView : GetComponentInChildren<SidebarBagPanelView>(includeInactive: true);
            if (bagPanelView == null)
            {
                var bagPanel = FindPanel("bag");
                bagPanelView = bagPanel != null
                    ? bagPanel.GetComponentInChildren<SidebarBagPanelView>(includeInactive: true)
                    : null;
            }

            deckPanelView = ResolvePanelView("deck", deckPanelView);
            relicCursePanelView = ResolvePanelView("relic_curse", relicCursePanelView);
            settingsPanelView = ResolvePanelView("settings", settingsPanelView);
        }

        private void RefreshBag(CombatState state)
        {
            var stacks = state?.PlayerInventory?.Bag?.Stacks ?? Array.Empty<PlayerBagItemStack>();
            // 슬롯 상한(멜빵 유물)도 시그니처에 넣는다 — 동적 값이 시그니처에 없으면 갱신이 죽는다
            // (유물 칩 배지의 교훈).
            var slotLimit = state?.GetEffectiveBagSlotLimit() ?? -1;
            var signature = $"bag|{slotLimit}|" + string.Join("|", stacks.Select(stack => $"{stack.ItemId}:{stack.Count}"));
            if (bagPanelView != null)
            {
                if (!signatures.TryGetValue("bag", out var previous) || previous != signature)
                {
                    DisableLegacyRuntimeRoot("bag");
                    bagPanelView.Refresh(state?.PlayerInventory?.Bag, uiFont, slotLimit);
                    RememberSignature("bag", signature);
                }

                return;
            }

            DisableLegacyRuntimeRoot("bag");
            RememberSignature("bag", signature);
        }

        private void RefreshDeck(CombatState state)
        {
            var signature = state == null
                ? "deck|null"
                : "deck|"
                    + FormatDeckSignature("M", state.MovementDeck)
                    + "|"
                    + FormatDeckSignature("A", state.ActionDeck);
            if (deckPanelView != null)
            {
                if (!signatures.TryGetValue("deck", out var previous) || previous != signature)
                {
                    DisableLegacyRuntimeRoot("deck");
                    deckPanelView.Refresh(state, uiFont);
                    RememberSignature("deck", signature);
                }

                return;
            }

            DisableLegacyRuntimeRoot("deck");
            RememberSignature("deck", signature);
        }


        private void RefreshRelicsAndCurses(CombatState state)
        {
            var items = state?.PlayerInventory?.RelicsAndCurses?.Items
                .OrderBy(item => item.Kind)
                .ThenBy(item => item.DisplayName)
                .ToArray() ?? Array.Empty<PlayerPermanentItemState>();
            // 배지가 읽는 동적 값(청사초롱 남은 턴·코인 세탁기 누적 카운터)도 시그니처에 넣는다 —
            // 아이템 목록이 그대로여도 카운터가 움직이면 칩을 다시 그려야 한다(T2 페이즈 C).
            var signature = "relic_curse|"
                + string.Join("|", items.Select(item => $"{item.Kind}:{item.Id}:{item.EffectAmount}:{item.RemainingActiveTurns}"))
                + "|used:" + (state?.TotalActionCardsUsed ?? 0);
            if (relicCursePanelView != null)
            {
                if (!signatures.TryGetValue("relic_curse", out var previous) || previous != signature)
                {
                    DisableLegacyRuntimeRoot("relic_curse");
                    relicCursePanelView.Refresh(state, uiFont);
                    RememberSignature("relic_curse", signature);
                }

                return;
            }

            DisableLegacyRuntimeRoot("relic_curse");
            RememberSignature("relic_curse", signature);
        }

        private void RefreshSettings(CombatState state)
        {
            var signature = state == null ? "settings|null" : $"settings|{state.Phase}|{state.ActionCostRemaining}";
            if (settingsPanelView != null)
            {
                if (!signatures.TryGetValue("settings", out var previous) || previous != signature)
                {
                    DisableLegacyRuntimeRoot("settings");
                    settingsPanelView.Refresh(state, uiFont);
                    RememberSignature("settings", signature);
                }

                return;
            }

            DisableLegacyRuntimeRoot("settings");
            RememberSignature("settings", signature);
        }

        private T ResolvePanelView<T>(string key, T cached) where T : Component
        {
            if (cached != null)
            {
                return cached;
            }

            var panel = FindPanel(key);
            return panel != null ? panel.GetComponentInChildren<T>(includeInactive: true) : null;
        }

        private void RememberSignature(string key, string signature)
        {
            signatures[key] = signature;
        }

        private RectTransform FindRuntimeRoot(string key)
        {
            return FindPanel(key)?.Find(RuntimeRootName) as RectTransform;
        }

        private void DisableLegacyRuntimeRoot(string key)
        {
            var root = FindRuntimeRoot(key);
            if (root == null)
            {
                return;
            }

            root.gameObject.SetActive(false);
        }

        private RectTransform FindPanel(string key)
        {
            if (controller?.Panels == null)
            {
                return null;
            }

            for (var i = 0; i < controller.Panels.Count; i++)
            {
                var entry = controller.Panels[i];
                if (entry != null && entry.Key == key)
                {
                    return entry.Panel;
                }
            }

            return null;
        }

        private static string FormatDeckSignature(string prefix, CardDeckState deck)
        {
            if (deck == null)
            {
                return prefix + ":null";
            }

            return prefix
                + ":H=" + string.Join(",", deck.Hand.Select(CardSignature))
                + ":D=" + string.Join(",", deck.DrawPile.Select(CardSignature))
                + ":X=" + string.Join(",", deck.DiscardPile.Select(CardSignature));
        }

        private static string CardSignature(CardDefinition card) => card == null ? string.Empty : $"{card.InstanceId}:{card.Id}";
    }
}
