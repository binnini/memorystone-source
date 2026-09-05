using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    public sealed class HexSparseMapSourceLoader : MonoBehaviour
    {
        [SerializeField] private HexSparseMapAuthoringSource sparseSource;
        [SerializeField] private AtlasTilePresentationView view;

        public HexSparseMapAuthoringSource SparseSource => sparseSource;

        private void Start()
        {
            RenderCurrentSource();
        }

        public void ApplySource(HexSparseMapAuthoringSource source, bool renderImmediately = false)
        {
            if (source == null)
            {
                Debug.LogWarning("Ignoring null sparse map source assignment.", this);
                return;
            }

            sparseSource = source;
            if (renderImmediately)
                RenderCurrentSource();
        }

#if UNITY_EDITOR
        public void PreviewInEditor()
        {
            RenderCurrentSource();
        }
#endif

        private void RenderCurrentSource()
        {
            if (sparseSource != null && view != null)
                view.Render(sparseSource);
        }
    }
}
