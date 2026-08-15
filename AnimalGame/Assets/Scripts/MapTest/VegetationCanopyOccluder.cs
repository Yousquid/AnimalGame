using UnityEngine;

namespace AnimalGame.MapTest
{
    public enum VegetationCanopyShape
    {
        Diamond,
        Circle
    }

    /// <summary>
    /// Creates a solid backdrop beneath outline-only canopy artwork so sprites
    /// below the canopy cannot show through its transparent interior.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    [AddComponentMenu("Animal Game/Level/Vegetation Canopy Occluder")]
    public sealed class VegetationCanopyOccluder : MonoBehaviour
    {
        private const int TextureResolution = 64;
        private const float ShapeRadius = 0.94f;

        private static Sprite diamondSprite;
        private static Sprite circleSprite;

        [SerializeField] private VegetationCanopyShape shape =
            VegetationCanopyShape.Diamond;
        [SerializeField] private Color occlusionColor = Color.black;
        [SerializeField] private int sortingOrder = 1099;

        private SpriteRenderer spriteRenderer;

        private void OnEnable()
        {
            ApplyOcclusionAppearance();
        }

        private void OnValidate()
        {
            ApplyOcclusionAppearance();
        }

        private void ApplyOcclusionAppearance()
        {
            // Prefab assets do not render. Their scene instances will receive
            // the generated transient sprite as soon as they are enabled.
            if (!gameObject.scene.IsValid())
                return;

            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
                return;

            spriteRenderer.sprite = GetOrCreateSprite(shape);
            spriteRenderer.color = occlusionColor;
            spriteRenderer.sortingOrder = sortingOrder;
        }

        private static Sprite GetOrCreateSprite(VegetationCanopyShape requestedShape)
        {
            if (requestedShape == VegetationCanopyShape.Circle)
            {
                if (circleSprite == null)
                    circleSprite = CreateSprite(VegetationCanopyShape.Circle);
                return circleSprite;
            }

            if (diamondSprite == null)
                diamondSprite = CreateSprite(VegetationCanopyShape.Diamond);
            return diamondSprite;
        }

        private static Sprite CreateSprite(VegetationCanopyShape requestedShape)
        {
            Texture2D texture = new Texture2D(
                TextureResolution,
                TextureResolution,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = $"Generated Canopy {requestedShape} Occluder",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[TextureResolution * TextureResolution];
            float edgeWidth = 2f / TextureResolution;
            for (int y = 0; y < TextureResolution; y++)
            {
                float normalizedY = ((y + 0.5f) / TextureResolution) * 2f - 1f;
                for (int x = 0; x < TextureResolution; x++)
                {
                    float normalizedX = ((x + 0.5f) / TextureResolution) * 2f - 1f;
                    float distance = requestedShape == VegetationCanopyShape.Circle
                        ? Mathf.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY)
                        : Mathf.Abs(normalizedX) + Mathf.Abs(normalizedY);
                    byte alpha = (byte)Mathf.RoundToInt(
                        255f * Mathf.Clamp01(
                            (ShapeRadius - distance) / edgeWidth + 0.5f));
                    pixels[y * TextureResolution + x] =
                        new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, TextureResolution, TextureResolution),
                new Vector2(0.5f, 0.5f),
                TextureResolution,
                0,
                SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
