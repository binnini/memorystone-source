using System.Collections.Generic;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MapObjectFadeTarget : MonoBehaviour
    {
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int SurfacePropertyId = Shader.PropertyToID("_Surface");
        private static readonly int BlendPropertyId = Shader.PropertyToID("_Blend");
        private static readonly int ModePropertyId = Shader.PropertyToID("_Mode");
        private static readonly int SrcBlendPropertyId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendPropertyId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWritePropertyId = Shader.PropertyToID("_ZWrite");

        [SerializeField] [Range(0.05f, 1f)] private float fadedAlpha = 0.35f;
        [Tooltip("Tint that objects on non-Revealed (fogged) cells are pushed toward, so they sit as dark as the surrounding fogged tiles.")]
        [SerializeField] private Color visibilityDarkenColor = new Color(0.05f, 0.06f, 0.07f, 1f);
        [Tooltip("How strongly fogged-cell objects are darkened toward visibilityDarkenColor (0 = none, 1 = fully replaced).")]
        [SerializeField] [Range(0f, 1f)] private float visibilityDarkenStrength = 0.85f;

        private readonly List<Renderer> renderers = new List<Renderer>();
        private readonly List<Material> fadeMaterialInstances = new List<Material>();
        private readonly Dictionary<Material, MaterialRenderState> originalMaterialStates = new Dictionary<Material, MaterialRenderState>();
        private readonly Dictionary<Renderer, Material[]> originalRendererMaterials = new Dictionary<Renderer, Material[]>();
        private MaterialPropertyBlock propertyBlock;
        private bool isOcclusionFaded;
        private bool isVisibilityDarkened;
        private bool materialsInstanced;
        private bool materialsAreTransparent;

        public bool IsPointerHovered { get; private set; }

        private void Awake()
        {
            CacheRenderers();
        }

        private void OnEnable()
        {
            IsPointerHovered = false;
            CacheRenderers();
            ApplyCombinedFade();
        }

        private void OnDisable()
        {
            IsPointerHovered = false;
            ApplyCombinedFade();
        }

        private void OnDestroy()
        {
            RestoreRendererMaterials();
            foreach (var material in fadeMaterialInstances)
            {
                DestroyMaterialInstance(material);
            }

            fadeMaterialInstances.Clear();
            originalMaterialStates.Clear();
            originalRendererMaterials.Clear();
        }

        private void OnMouseEnter()
        {
            SetPointerHovered(true);
        }

        private void OnMouseExit()
        {
            SetPointerHovered(false);
        }

        public void SetPointerHovered(bool hovered)
        {
            IsPointerHovered = hovered;
            ApplyCombinedFade();
        }

        public void SetPointerHoveredForTests(bool hovered)
        {
            SetPointerHovered(hovered);
        }

        public void ApplyFade(bool fade)
        {
            isOcclusionFaded = fade;
            ApplyCombinedFade();
        }

        public void ApplyVisibilityDarkening(bool darken)
        {
            isVisibilityDarkened = darken;
            ApplyCombinedFade();
        }

        private void ApplyCombinedFade()
        {
            CacheRenderers();
            EnsurePropertyBlock();
            var fadeIsActive = isOcclusionFaded || IsPointerHovered;
            if (fadeIsActive)
            {
                EnsureTransparentMaterials();
            }
            else
            {
                RestoreOpaqueMaterials();
            }

            var alpha = fadeIsActive ? Mathf.Clamp01(fadedAlpha) : 1f;
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                propertyBlock.Clear();
                renderer.GetPropertyBlock(propertyBlock);
                var color = ResolveRendererColor(renderer);
                if (isVisibilityDarkened)
                {
                    var darkTarget = new Color(visibilityDarkenColor.r, visibilityDarkenColor.g, visibilityDarkenColor.b, color.a);
                    color = Color.Lerp(color, darkTarget, Mathf.Clamp01(visibilityDarkenStrength));
                }

                color.a *= alpha;
                propertyBlock.SetColor(BaseColorPropertyId, color);
                propertyBlock.SetColor(ColorPropertyId, color);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void CacheRenderers()
        {
            if (renderers.Count > 0)
            {
                return;
            }

            GetComponentsInChildren(includeInactive: true, renderers);
        }

        private void EnsureTransparentMaterials()
        {
            EnsureMaterialInstances();
            if (materialsAreTransparent)
            {
                return;
            }

            materialsAreTransparent = true;
            foreach (var material in fadeMaterialInstances)
            {
                PrepareMaterialForAlphaFade(material);
            }
        }

        private void RestoreOpaqueMaterials()
        {
            if (!materialsAreTransparent)
            {
                return;
            }

            materialsAreTransparent = false;
            foreach (var material in fadeMaterialInstances)
            {
                if (material != null && originalMaterialStates.TryGetValue(material, out var state))
                {
                    state.Restore(material);
                }
            }
        }

        private void EnsureMaterialInstances()
        {
            if (materialsInstanced)
            {
                return;
            }

            materialsInstanced = true;
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                var sharedMaterials = renderer.sharedMaterials;
                if (sharedMaterials == null || sharedMaterials.Length == 0)
                {
                    continue;
                }

                originalRendererMaterials[renderer] = sharedMaterials;
                var materialInstances = new Material[sharedMaterials.Length];
                for (var i = 0; i < sharedMaterials.Length; i++)
                {
                    var source = sharedMaterials[i];
                    if (source == null)
                    {
                        continue;
                    }

                    var material = new Material(source);
                    material.name = source.name + " (Map Object Fade Instance)";
                    materialInstances[i] = material;
                    originalMaterialStates.Add(material, MaterialRenderState.Capture(material));
                    fadeMaterialInstances.Add(material);
                }

                renderer.sharedMaterials = materialInstances;
            }
        }


        private void RestoreRendererMaterials()
        {
            foreach (var entry in originalRendererMaterials)
            {
                if (entry.Key != null)
                {
                    entry.Key.sharedMaterials = entry.Value;
                }
            }
        }

        private static void PrepareMaterialForAlphaFade(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty(SurfacePropertyId))
            {
                material.SetFloat(SurfacePropertyId, 1f);
            }

            if (material.HasProperty(BlendPropertyId))
            {
                material.SetFloat(BlendPropertyId, 0f);
            }

            if (material.HasProperty(ModePropertyId))
            {
                material.SetFloat(ModePropertyId, 3f);
            }

            if (material.HasProperty(SrcBlendPropertyId))
            {
                material.SetFloat(SrcBlendPropertyId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }

            if (material.HasProperty(DstBlendPropertyId))
            {
                material.SetFloat(DstBlendPropertyId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty(ZWritePropertyId))
            {
                material.SetFloat(ZWritePropertyId, 0f);
            }

            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        private void EnsurePropertyBlock()
        {
            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }
        }

        private static Color ResolveRendererColor(Renderer renderer)
        {
            var material = renderer.sharedMaterial;
            if (material == null)
            {
                return Color.white;
            }

            if (material.HasProperty(BaseColorPropertyId))
            {
                return material.GetColor(BaseColorPropertyId);
            }

            return material.HasProperty(ColorPropertyId) ? material.GetColor(ColorPropertyId) : Color.white;
        }

        private static void DestroyMaterialInstance(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }

        private readonly struct MaterialRenderState
        {
            private readonly int renderQueue;
            private readonly bool hasSurface;
            private readonly float surface;
            private readonly bool hasBlend;
            private readonly float blend;
            private readonly bool hasMode;
            private readonly float mode;
            private readonly bool hasSrcBlend;
            private readonly float srcBlend;
            private readonly bool hasDstBlend;
            private readonly float dstBlend;
            private readonly bool hasZWrite;
            private readonly float zWrite;
            private readonly string[] shaderKeywords;

            private MaterialRenderState(
                int renderQueue,
                bool hasSurface,
                float surface,
                bool hasBlend,
                float blend,
                bool hasMode,
                float mode,
                bool hasSrcBlend,
                float srcBlend,
                bool hasDstBlend,
                float dstBlend,
                bool hasZWrite,
                float zWrite,
                string[] shaderKeywords)
            {
                this.renderQueue = renderQueue;
                this.hasSurface = hasSurface;
                this.surface = surface;
                this.hasBlend = hasBlend;
                this.blend = blend;
                this.hasMode = hasMode;
                this.mode = mode;
                this.hasSrcBlend = hasSrcBlend;
                this.srcBlend = srcBlend;
                this.hasDstBlend = hasDstBlend;
                this.dstBlend = dstBlend;
                this.hasZWrite = hasZWrite;
                this.zWrite = zWrite;
                this.shaderKeywords = shaderKeywords;
            }

            public static MaterialRenderState Capture(Material material)
            {
                return new MaterialRenderState(
                    material.renderQueue,
                    material.HasProperty(SurfacePropertyId),
                    material.HasProperty(SurfacePropertyId) ? material.GetFloat(SurfacePropertyId) : 0f,
                    material.HasProperty(BlendPropertyId),
                    material.HasProperty(BlendPropertyId) ? material.GetFloat(BlendPropertyId) : 0f,
                    material.HasProperty(ModePropertyId),
                    material.HasProperty(ModePropertyId) ? material.GetFloat(ModePropertyId) : 0f,
                    material.HasProperty(SrcBlendPropertyId),
                    material.HasProperty(SrcBlendPropertyId) ? material.GetFloat(SrcBlendPropertyId) : 0f,
                    material.HasProperty(DstBlendPropertyId),
                    material.HasProperty(DstBlendPropertyId) ? material.GetFloat(DstBlendPropertyId) : 0f,
                    material.HasProperty(ZWritePropertyId),
                    material.HasProperty(ZWritePropertyId) ? material.GetFloat(ZWritePropertyId) : 0f,
                    material.shaderKeywords ?? new string[0]);
            }

            public void Restore(Material material)
            {
                if (hasSurface)
                {
                    material.SetFloat(SurfacePropertyId, surface);
                }

                if (hasBlend)
                {
                    material.SetFloat(BlendPropertyId, blend);
                }

                if (hasMode)
                {
                    material.SetFloat(ModePropertyId, mode);
                }

                if (hasSrcBlend)
                {
                    material.SetFloat(SrcBlendPropertyId, srcBlend);
                }

                if (hasDstBlend)
                {
                    material.SetFloat(DstBlendPropertyId, dstBlend);
                }

                if (hasZWrite)
                {
                    material.SetFloat(ZWritePropertyId, zWrite);
                }

                material.renderQueue = renderQueue;
                material.shaderKeywords = shaderKeywords;
            }
        }
    }
}
