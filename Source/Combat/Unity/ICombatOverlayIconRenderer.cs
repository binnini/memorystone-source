using System.Collections.Generic;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Renders the per-tile icon annotation channel of a combat overlay presentation.
    /// Kept separate from <see cref="ICombatOverlayRenderer"/> because icons are world-space
    /// sprites with camera billboarding and texture processing, orthogonal to the fill/boundary
    /// mesh that the fill renderer produces. Implementations are data-driven: they receive the
    /// annotations to display and own no combat-state querying.
    /// </summary>
    public interface ICombatOverlayIconRenderer
    {
        void ApplyAnnotations(IReadOnlyList<CombatOverlayIconAnnotation> annotations);
        void Clear();
    }
}
