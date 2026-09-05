using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    [DisallowMultipleComponent]
    public sealed class SidebarDeckPanelView : MonoBehaviour
    {
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private SidebarRowBinding[] rows = Array.Empty<SidebarRowBinding>();

        public void AutoBindFromHierarchy()
        {
            contentRoot = FindContentRoot();
            rows = contentRoot == null ? Array.Empty<SidebarRowBinding>() : SidebarRowBinding.FindRows(contentRoot).ToArray();
        }

        public void Refresh(CombatState state, TMP_FontAsset font)
        {
            EnsureBound();
            SidebarAuthoredPanelViewUtility.RefreshRows(rows, BuildEntries(state).ToArray(), font);
        }

        private void EnsureBound()
        {
            if (contentRoot == null || rows == null || rows.Length == 0 || rows.Any(row => row.Root == null))
            {
                AutoBindFromHierarchy();
            }
        }

        private RectTransform FindContentRoot()
        {
            var scroll = GetComponentsInChildren<ScrollRect>(includeInactive: true).FirstOrDefault();
            return scroll?.content
                ?? GetComponentsInChildren<RectTransform>(includeInactive: true).FirstOrDefault(rect => rect.name == "Content")
                ?? transform as RectTransform;
        }

        // Player-facing panel: Korean labels only. This panel shipped with English debug strings because its
        // authored content root was left inactive, so nothing here was ever visible in game (P6 T2).
        private static IEnumerable<(string Left, string Right)> BuildEntries(CombatState state)
        {
            if (state == null)
            {
                yield return ("덱", "정보 없음");
                yield break;
            }

            yield return ("이동 덱", DeckSummary(state.MovementDeck));
            foreach (var entry in CardEntries("이동 손패", state.MovementDeck.Hand))
            {
                yield return entry;
            }

            yield return ("행동 덱", DeckSummary(state.ActionDeck));
            foreach (var entry in CardEntries("행동 손패", state.ActionDeck.Hand))
            {
                yield return entry;
            }
        }

        private static string DeckSummary(CardDeckState deck) =>
            $"뽑을 {deck.DrawCount} · 손패 {deck.HandCount} · 버림 {deck.DiscardCount}";

        private static IEnumerable<(string Left, string Right)> CardEntries(string emptyLabel, IReadOnlyList<CardDefinition> cards)
        {
            if (cards == null || cards.Count == 0)
            {
                yield return (emptyLabel, "카드 없음");
                yield break;
            }

            foreach (var card in cards)
            {
                yield return (card.DisplayName, $"비용 {card.Cost} · 사거리 {card.Range}");
            }
        }
    }
}
