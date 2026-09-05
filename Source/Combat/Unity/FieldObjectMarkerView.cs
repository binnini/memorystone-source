using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Attached to the root of a spawned field-object marker. Holds the runtime <see cref="FieldObject"/>
    /// that backs the visual so a hover raycast can resolve the tooltip/overlay payload from the hit
    /// collider via <c>GetComponentInParent&lt;FieldObjectMarkerView&gt;()</c>.
    /// </summary>
    public sealed class FieldObjectMarkerView : MonoBehaviour
    {
        public FieldObject Data { get; private set; }

        public void SetData(FieldObject data)
        {
            Data = data;
        }
    }
}
