using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Rename-safe anchor for the gameplay sidebar root. Attach to the sidebar RectTransform so systems that
    /// only need to locate the sidebar can resolve it by component type instead of by the "Sidebar" GameObject
    /// name (which the planned de-Prototype rename will change).
    ///
    /// This marker lives in the <c>SeoulPlayup.Cards.Unity</c> assembly on purpose: <see cref="DeckPileListOverlayView"/>
    /// is here and cannot reference the sidebar's own view types (<c>SidebarRuntimeView</c> et al.), which
    /// sit in <c>SeoulPlayup.Combat</c> — and <c>Combat</c> already depends on this assembly, so referencing back
    /// would be a circular assembly dependency. A neutral marker here is reachable from both sides.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SidebarRootMarker : MonoBehaviour
    {
    }
}
