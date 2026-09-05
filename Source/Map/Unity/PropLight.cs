using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// Tags a prefab-embedded prop light. There is no shared profile: the look is authored by hand
    /// directly on the sibling <see cref="Light"/> and tuned per prop (and per placed instance) in the
    /// scene, so what you see in the editor is what ships. Keep the Light unshadowed. This component
    /// carries no data — it only marks the GameObject as a prop light so designers and tools can find
    /// them.
    /// </summary>
    [RequireComponent(typeof(Light))]
    [DisallowMultipleComponent]
    public sealed class PropLight : MonoBehaviour
    {
    }
}
