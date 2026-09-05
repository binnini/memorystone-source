#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CardLaneStatusEffectDockViewTests
    {
        private static readonly StatusEffectKind[] DistinctKinds =
        {
            StatusEffectKind.Immobilize,
            StatusEffectKind.Poison,
            StatusEffectKind.Stun,
            StatusEffectKind.Slow,
            StatusEffectKind.Rupture,
            StatusEffectKind.Reflect,
            StatusEffectKind.Agility,
        };

        [Test]
        public void RefreshFillsPrimarySlotsAndHidesOverflowWhenAtOrBelowCapacity()
        {
            var dock = CreateDock(out var view);
            try
            {
                view.Refresh(BuildEffects(3), null);

                Assert.That(view.CountActivePrimarySlotsForTests(), Is.EqualTo(3));
                Assert.That(view.ExtraCountForTests, Is.EqualTo(0));
                Assert.That(view.IsExtraCountVisibleForTests, Is.False);
                Assert.That(view.IsExpandedForTests, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(dock);
            }
        }

        [Test]
        public void RefreshSummarizesOverflowWithPlusCount()
        {
            var dock = CreateDock(out var view);
            try
            {
                view.Refresh(BuildEffects(7), null);

                Assert.That(view.CountActivePrimarySlotsForTests(), Is.EqualTo(CardLaneStatusEffectDockView.PrimaryCapacity));
                Assert.That(view.ExtraCountForTests, Is.EqualTo(2));
                Assert.That(view.CountActiveExtraSlotsForTests(), Is.EqualTo(2));
                Assert.That(view.IsExtraCountVisibleForTests, Is.True);
                Assert.That(view.ExtraCountTextForTests, Is.EqualTo("+2"));
            }
            finally
            {
                Object.DestroyImmediate(dock);
            }
        }

        [Test]
        public void HoverExpandsPanelOnlyWhenOverflowExists()
        {
            var dock = CreateDock(out var view);
            try
            {
                view.Refresh(BuildEffects(3), null);
                view.SetHoverForTests(true);
                Assert.That(view.IsExpandedForTests, Is.False, "No overflow should never expand on hover.");

                view.Refresh(BuildEffects(7), null);
                view.SetHoverForTests(false);
                Assert.That(view.IsExpandedForTests, Is.False);

                view.SetHoverForTests(true);
                Assert.That(view.IsExpandedForTests, Is.True);
                Assert.That(view.ExpandedPanelHeightForTests, Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(dock);
            }
        }

        [Test]
        public void ExpandedHeightGrowsWithOverflowRowCount()
        {
            var metrics = new CardLaneStatusEffectDockView.GridMetrics(
                new Vector2(50f, 50f), new Vector2(10f, 10f), 10f, 10f);

            var oneRow = CardLaneStatusEffectDockView.ComputeExpandedHeight(1, metrics);
            var twoRows = CardLaneStatusEffectDockView.ComputeExpandedHeight(2, metrics);

            Assert.That(CardLaneStatusEffectDockView.ComputeExpandedHeight(0, metrics), Is.EqualTo(0f));
            Assert.That(oneRow, Is.GreaterThan(0f));
            Assert.That(twoRows, Is.GreaterThan(oneRow));
        }

        [Test]
        public void MappedSpriteShowsIconImageAndHidesGlyphFallback()
        {
            var dock = CreateDock(out var view);
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            try
            {
                // Effect index 0 maps to DistinctKinds[0] == Immobilize.
                view.SetIconCatalogForTests(StatusEffectIconCatalog.CreateForTests((StatusEffectKind.Immobilize, sprite)));
                view.Refresh(BuildEffects(1), null);

                var slot = FindFirstPrimarySlot(dock);
                var icon = slot.Find("StatusIcon").GetComponent<Image>();
                var glyph = slot.Find("StatusIcon_TMP").GetComponent<TMP_Text>();

                Assert.That(icon.enabled, Is.True);
                Assert.That(icon.sprite, Is.EqualTo(sprite));
                Assert.That(glyph.enabled, Is.False, "Glyph fallback must hide when a sprite is mapped.");
            }
            finally
            {
                Object.DestroyImmediate(dock);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void RefreshBindsHoverRelayToVisibleStatusSlots()
        {
            var dock = CreateDock(out var view);
            try
            {
                view.Refresh(BuildEffects(1), null);

                var slot = FindFirstPrimarySlot(dock);
                var relay = slot.GetComponent<CardLaneStatusEffectDockView.HoverRelay>();

                Assert.That(relay, Is.Not.Null);
                Assert.That(relay.HasEffect, Is.True);
                Assert.That(relay.Effect.Kind, Is.EqualTo(StatusEffectKind.Immobilize));

                view.Refresh(BuildEffects(0), null);

                Assert.That(relay.HasEffect, Is.False, "Empty slots must not keep stale tooltip content.");
            }
            finally
            {
                Object.DestroyImmediate(dock);
            }
        }

        [Test]
        public void HoveringStatusSlotDoesNotTriggerOverflowExpansion()
        {
            var dock = CreateDock(out var view);
            try
            {
                view.Refresh(BuildEffects(7), null);

                var slot = FindFirstPrimarySlot(dock);
                var relay = slot.GetComponent<CardLaneStatusEffectDockView.HoverRelay>();
                view.SetHover(relay, true);

                Assert.That(view.IsExpandedForTests, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(dock);
            }
        }

        private static Transform FindFirstPrimarySlot(GameObject dock)
        {
            foreach (var rect in dock.GetComponentsInChildren<RectTransform>(includeInactive: true))
            {
                if (rect.name == "StatusLayout")
                {
                    return rect.GetChild(0);
                }
            }

            return null;
        }

        private static IReadOnlyList<ActiveEffect> BuildEffects(int count)
        {
            var effects = new List<ActiveEffect>();
            for (var i = 0; i < count; i++)
            {
                effects.Add(new ActiveEffect(
                    EffectType.Duration,
                    DistinctKinds[i % DistinctKinds.Length],
                    "player",
                    remainingTurns: i + 1,
                    amount: 1,
                    sourceRef: $"src-{i}"));
            }

            return effects;
        }

        private static GameObject CreateDock(out CardLaneStatusEffectDockView view)
        {
            var dock = new GameObject("StatusEffectDock", typeof(RectTransform));
            CreateGridLayout("StatusLayout", dock.transform, slots: CardLaneStatusEffectDockView.PrimaryCapacity);
            CreateImage("StatusExtraPanel", dock.transform);
            CreateGridLayout("StatusExtraLayout", dock.transform, slots: CardLaneStatusEffectDockView.PrimaryCapacity);

            var count = new GameObject("StatusExtraCount", typeof(RectTransform)).GetComponent<RectTransform>();
            count.SetParent(dock.transform, false);
            count.gameObject.AddComponent<TextMeshProUGUI>();

            view = dock.AddComponent<CardLaneStatusEffectDockView>();
            return dock;
        }

        private static void CreateGridLayout(string name, Transform parent, int slots)
        {
            var layout = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            layout.SetParent(parent, false);
            var grid = layout.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(50f, 50f);
            grid.spacing = new Vector2(10f, 10f);
            grid.padding = new RectOffset(10, 0, 0, 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = CardLaneStatusEffectDockView.PrimaryCapacity;

            for (var i = 0; i < slots; i++)
            {
                CreateSlot($"StatusIconSlot ({i})", layout);
            }
        }

        private static void CreateSlot(string name, Transform parent)
        {
            var root = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.gameObject.AddComponent<Image>();

            var icon = new GameObject("StatusIcon", typeof(RectTransform)).GetComponent<RectTransform>();
            icon.SetParent(root, false);
            var iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.enabled = false;

            CreateChildText("StatusIcon_TMP", root);
            CreateChildText("StatusTurn_TMP", root);
        }

        private static void CreateChildText(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.gameObject.AddComponent<TextMeshProUGUI>();
        }

        private static void CreateImage(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.gameObject.AddComponent<Image>();
        }
    }
}
#endif




