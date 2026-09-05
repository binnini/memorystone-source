using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    [Serializable]
    internal sealed class SidebarRowBinding
    {
        [SerializeField] private RectTransform root;
        [SerializeField] private TMP_Text leftText;
        [SerializeField] private TMP_Text rightText;

        public RectTransform Root => root;

        public static IEnumerable<SidebarRowBinding> FindRows(Transform root)
        {
            return root.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .Where(rect => rect.name.IndexOf("Row", StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(rect => rect.GetComponentsInChildren<TMP_Text>(includeInactive: true).Length >= 2)
                .Select(From);
        }

        public static SidebarRowBinding From(RectTransform rowRoot)
        {
            var binding = new SidebarRowBinding { root = rowRoot };
            var texts = rowRoot != null ? rowRoot.GetComponentsInChildren<TMP_Text>(includeInactive: true) : Array.Empty<TMP_Text>();
            binding.leftText = texts.FirstOrDefault(text => text.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? texts.FirstOrDefault(text => text.name.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? texts.FirstOrDefault(text => text.name.IndexOf("Label", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? texts.FirstOrDefault();
            binding.rightText = texts.FirstOrDefault(text => text.name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? texts.FirstOrDefault(text => text.name.IndexOf("Meta", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? texts.FirstOrDefault(text => text.name.IndexOf("Count", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? texts.Skip(1).FirstOrDefault();
            return binding;
        }

        public void Set(string left, string right, TMP_FontAsset font)
        {
            if (root != null)
            {
                root.gameObject.SetActive(true);
            }

            SidebarAuthoredPanelViewUtility.SetText(leftText, left, font);
            SidebarAuthoredPanelViewUtility.SetText(rightText, right, font);
        }

        public void SetInactive()
        {
            if (root != null)
            {
                root.gameObject.SetActive(false);
            }
        }
    }

    [Serializable]
    internal sealed class SidebarSlotBinding
    {
        [SerializeField] private RectTransform root;
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private TMP_Text metaText;

        public RectTransform Root => root;

        public static SidebarSlotBinding From(RectTransform slotRoot)
        {
            var binding = new SidebarSlotBinding
            {
                root = slotRoot,
                background = slotRoot != null ? slotRoot.GetComponent<Image>() : null
            };
            var texts = slotRoot != null ? slotRoot.GetComponentsInChildren<TMP_Text>(includeInactive: true) : Array.Empty<TMP_Text>();
            binding.labelText = texts.FirstOrDefault(text => text.name.IndexOf("Label", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? texts.FirstOrDefault(text => text.name.IndexOf("Plus", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? texts.FirstOrDefault();
            binding.metaText = texts.FirstOrDefault(text => text.name.IndexOf("Meta", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? texts.Skip(1).FirstOrDefault();
            return binding;
        }

        public void Set(string label, string meta, Color backgroundColor, Color labelColor, Color metaColor, TMP_FontAsset font)
        {
            if (root != null)
            {
                root.gameObject.SetActive(true);
            }

            if (background != null)
            {
                background.color = backgroundColor;
            }

            SidebarAuthoredPanelViewUtility.SetText(labelText, label, font, labelColor);
            SidebarAuthoredPanelViewUtility.SetText(metaText, meta, font, metaColor);
        }

        public void SetInactive()
        {
            if (root != null)
            {
                root.gameObject.SetActive(false);
            }
        }
    }

    internal static class SidebarAuthoredPanelViewUtility
    {
        public static string PhaseLabel(CombatPhase phase)
        {
            switch (phase)
            {
                case CombatPhase.PlayerMovement: return "이동";
                case CombatPhase.MonsterMovement: return "몬스터 이동";
                case CombatPhase.PlayerAction: return "행동";
                case CombatPhase.MonsterAction: return "몬스터 행동";
                case CombatPhase.Victory: return "승리";
                case CombatPhase.Defeat: return "패배";
                default: return phase.ToString();
            }
        }

        public static void RefreshRows(SidebarRowBinding[] rows, IReadOnlyList<(string Left, string Right)> entries, TMP_FontAsset font)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                if (i < entries.Count)
                {
                    rows[i].Set(entries[i].Left, entries[i].Right, font);
                }
                else
                {
                    rows[i].SetInactive();
                }
            }
        }

        public static void SetText(TMP_Text text, string value, TMP_FontAsset font, Color? color = null)
        {
            if (text == null)
            {
                return;
            }

            text.text = value ?? string.Empty;
            if (font != null)
            {
                text.font = font;
            }

            if (color.HasValue)
            {
                text.color = color.Value;
            }
        }
    }
}
