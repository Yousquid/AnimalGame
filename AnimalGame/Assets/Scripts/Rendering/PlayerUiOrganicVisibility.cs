using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Rendering
{
    /// <summary>
    /// Shares one screen-space player-UI clip across vegetation, animals, and
    /// animal-related sprite visuals. This changes presentation only; objects
    /// continue simulating normally while outside the UI circle.
    /// </summary>
    public static class PlayerUiOrganicVisibility
    {
        private const string ClippedSpriteShaderName =
            "AnimalGame/Player UI Clipped Sprite";
        private const string UnknownAnimalShaderName =
            "Animal Game/Unknown Animal Static";

        private static readonly int ClipEnabledProperty =
            Shader.PropertyToID("_PlayerUiClipEnabled");
        private static readonly int ClipCenterPixelsProperty =
            Shader.PropertyToID("_PlayerUiClipCenterPixels");
        private static readonly int ClipRadiusPixelsProperty =
            Shader.PropertyToID("_PlayerUiClipRadiusPixels");
        private static readonly int ClipSoftnessPixelsProperty =
            Shader.PropertyToID("_PlayerUiClipSoftnessPixels");

        private static readonly HashSet<SpriteRenderer> RegisteredRenderers =
            new HashSet<SpriteRenderer>();

        private static Shader clippedSpriteShader;
        private static Material clippedSpriteMaterial;
        private static bool missingShaderReported;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            RegisteredRenderers.Clear();
            clippedSpriteShader = null;
            clippedSpriteMaterial = null;
            missingShaderReported = false;
            Shader.SetGlobalFloat(ClipEnabledProperty, 0f);
        }

        public static void ConfigureSpriteShader(Shader shader)
        {
            if (shader == null || shader == clippedSpriteShader)
                return;

            clippedSpriteShader = shader;
            if (clippedSpriteMaterial != null)
            {
                Object.Destroy(clippedSpriteMaterial);
                clippedSpriteMaterial = null;
            }
            missingShaderReported = false;
            ApplyClipMaterialToRegisteredRenderers();
        }

        public static void RegisterRenderers(SpriteRenderer[] renderers)
        {
            if (renderers == null)
                return;

            for (int index = 0; index < renderers.Length; index++)
            {
                SpriteRenderer renderer = renderers[index];
                if (renderer == null)
                    continue;

                RegisteredRenderers.Add(renderer);
                ApplyClipMaterial(renderer);
            }
        }

        public static void UnregisterRenderers(SpriteRenderer[] renderers)
        {
            if (renderers == null)
                return;

            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null)
                    RegisteredRenderers.Remove(renderers[index]);
            }
        }

        public static void SetClip(
            Vector2 centerPixels,
            float radiusPixels,
            float softnessPixels)
        {
            Shader.SetGlobalVector(
                ClipCenterPixelsProperty,
                new Vector4(centerPixels.x, centerPixels.y, 0f, 0f));
            Shader.SetGlobalFloat(
                ClipRadiusPixelsProperty,
                Mathf.Max(0f, radiusPixels));
            Shader.SetGlobalFloat(
                ClipSoftnessPixelsProperty,
                Mathf.Max(0f, softnessPixels));
            Shader.SetGlobalFloat(ClipEnabledProperty, 1f);
        }

        public static void DisableClip()
        {
            Shader.SetGlobalFloat(ClipEnabledProperty, 0f);
        }

        private static void ApplyClipMaterialToRegisteredRenderers()
        {
            RegisteredRenderers.RemoveWhere(renderer => renderer == null);
            foreach (SpriteRenderer renderer in RegisteredRenderers)
                ApplyClipMaterial(renderer);
        }

        private static void ApplyClipMaterial(SpriteRenderer renderer)
        {
            if (renderer == null)
                return;

            Shader currentShader = renderer.sharedMaterial != null
                ? renderer.sharedMaterial.shader
                : null;
            if (IsPlayerUiClipAware(currentShader))
                return;

            Material material = GetOrCreateClippedSpriteMaterial();
            if (material != null)
                renderer.sharedMaterial = material;
        }

        private static bool IsPlayerUiClipAware(Shader shader)
        {
            if (shader == null)
                return false;

            return shader.name == ClippedSpriteShaderName
                   || shader.name == UnknownAnimalShaderName;
        }

        private static Material GetOrCreateClippedSpriteMaterial()
        {
            if (clippedSpriteMaterial != null)
                return clippedSpriteMaterial;

            Shader shader = clippedSpriteShader != null
                ? clippedSpriteShader
                : Shader.Find(ClippedSpriteShaderName);
            if (shader == null)
            {
                if (!missingShaderReported)
                {
                    Debug.LogError(
                        "Player UI organic visibility could not find its clipped sprite shader.");
                    missingShaderReported = true;
                }
                return null;
            }

            clippedSpriteShader = shader;
            clippedSpriteMaterial = new Material(shader)
            {
                name = "Runtime Player UI Clipped Organic Sprite",
                hideFlags = HideFlags.HideAndDontSave
            };
            return clippedSpriteMaterial;
        }
    }
}
