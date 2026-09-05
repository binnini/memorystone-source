using System.Collections.Generic;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    // Hide/restore bookkeeping for gameplay UI layers during cinematics (victory sweep, stage
    // intro), extracted from MapCombatController (P4 Stage 4). Deactivates the children of the
    // gameplay-layer root that the keep-visible predicate rejects, remembers exactly which ones it
    // turned off, and reactivates only those on restore. Plain class owned by MapCombatController;
    // scene access arrives as a delegate so no scene reference moves.
    internal sealed class CinematicUiHider
    {
        private readonly System.Func<RectTransform> resolveGameplayLayers;
        private readonly System.Func<string, bool> shouldKeepVisible;
        private readonly List<GameObject> hiddenUi = new List<GameObject>();

        public CinematicUiHider(
            System.Func<RectTransform> resolveGameplayLayers,
            System.Func<string, bool> shouldKeepVisible)
        {
            this.resolveGameplayLayers = resolveGameplayLayers;
            this.shouldKeepVisible = shouldKeepVisible;
        }

        public void Hide()
        {
            hiddenUi.Clear();
            var gameplayLayers = resolveGameplayLayers();
            if (gameplayLayers == null)
            {
                return;
            }

            for (var i = 0; i < gameplayLayers.childCount; i++)
            {
                var child = gameplayLayers.GetChild(i);
                if (child == null || shouldKeepVisible(child.name))
                {
                    continue;
                }

                var go = child.gameObject;
                if (go.activeSelf)
                {
                    hiddenUi.Add(go);
                    go.SetActive(false);
                }
            }
        }

        public void Restore()
        {
            foreach (var go in hiddenUi)
            {
                if (go != null)
                {
                    go.SetActive(true);
                }
            }

            hiddenUi.Clear();
        }
    }
}
