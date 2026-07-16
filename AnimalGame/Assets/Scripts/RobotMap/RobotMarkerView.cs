using UnityEngine;

namespace AnimalGame.RobotMap
{
    public sealed class RobotMarkerView : MonoBehaviour
    {
        [Header("Body")]
        [SerializeField] private Sprite robotBodySprite;
        [SerializeField, Min(0.1f)] private float bodyDiameter = 0.72f;
        [Tooltip("Visible ring diameter in the source robot_body sprite, excluding transparent padding.")]
        [SerializeField, Min(1f)] private float bodyArtworkVisibleDiameterPixels = 85.4f;
        [SerializeField, Range(0.1f, 1f)] private float bodyFillDiameterRatio = 0.92f;
        [SerializeField] private Color bodyFillColor = new Color(0.008f, 0.011f, 0.014f, 0.94f);
        [SerializeField] private Color bodyOutlineColor = new Color(0.92f, 0.98f, 1f, 1f);

        [Header("Direction Indicator")]
        [SerializeField] private Sprite indicatorSprite;
        [SerializeField, Min(0.1f)] private float indicatorScale = 1.35f;
        [SerializeField] private float indicatorRotationOffsetDegrees;
        [SerializeField] private Color indicatorColor = new Color(0.92f, 0.98f, 1f, 1f);

        [Header("Motion Tail")]
        [SerializeField] private bool showMotionTail = true;

        private Transform bodyVisualRoot;
        private SpriteRenderer bodyFill;
        private SpriteRenderer bodyArtwork;
        private SpriteRenderer directionIndicator;
        private LineRenderer tail;
        private Sprite generatedBodySprite;
        private Texture2D generatedBodyTexture;

        private void Awake()
        {
            CreateBodySpriteRenderer();
            CreateDirectionIndicatorRenderer();

            tail = RobotMapDemo.CreateLine(transform, "Motion Tail", new[]
            {
                new Vector3(0f, -0.38f), new Vector3(0f, -0.38f)
            }, 0.05f, new Color(0.35f, 0.82f, 0.9f, 0.7f), 18);
            ApplyMotionTailVisibility();
        }

        public bool ShowMotionTail => showMotionTail;

        public void SetMotionTailVisible(bool shouldShow)
        {
            showMotionTail = shouldShow;
            ApplyMotionTailVisibility();
        }

        private void Update()
        {
            float pulse = 1f + Mathf.Sin(Time.time * 3.2f) * 0.035f;
            if (bodyVisualRoot != null)
                bodyVisualRoot.localScale = Vector3.one * pulse;

            RobotMover mover = GetComponent<RobotMover>();
            if (showMotionTail && tail != null && mover != null)
            {
                float tailLength = Mathf.Clamp(Mathf.Abs(mover.CurrentSpeed) * 0.2f, 0f, 1.1f);
                tail.SetPosition(1, new Vector3(0f, -0.38f - tailLength));
            }
        }

        private void ApplyMotionTailVisibility()
        {
            if (tail != null)
                tail.enabled = showMotionTail;
        }

        private void CreateBodySpriteRenderer()
        {
            var bodyVisualObject = new GameObject("Body Visual");
            bodyVisualObject.transform.SetParent(transform, false);
            bodyVisualRoot = bodyVisualObject.transform;

            generatedBodySprite = CreateCircularFillSprite(out generatedBodyTexture);

            var fillObject = new GameObject("Body Fill");
            fillObject.transform.SetParent(bodyVisualRoot, false);
            bodyFill = fillObject.AddComponent<SpriteRenderer>();
            bodyFill.sprite = generatedBodySprite;
            bodyFill.color = bodyFillColor;
            bodyFill.sortingOrder = 19;
            bodyFill.transform.localScale = Vector3.one * (bodyDiameter * bodyFillDiameterRatio);

            var artworkObject = new GameObject("Body Artwork");
            artworkObject.transform.SetParent(bodyVisualRoot, false);
            bodyArtwork = artworkObject.AddComponent<SpriteRenderer>();
            bodyArtwork.sprite = robotBodySprite;
            bodyArtwork.color = bodyOutlineColor;
            bodyArtwork.sortingOrder = 20;

            if (robotBodySprite != null)
            {
                float artworkDiameter = bodyArtworkVisibleDiameterPixels /
                    Mathf.Max(1f, robotBodySprite.pixelsPerUnit);
                float artworkScale = bodyDiameter / artworkDiameter;
                bodyArtwork.transform.localScale = Vector3.one * artworkScale;
            }
            else
            {
                Debug.LogWarning(
                    "RobotMarkerView is missing its Arts/robot_body Sprite.",
                    this);
            }
        }

        private void CreateDirectionIndicatorRenderer()
        {
            var indicatorObject = new GameObject("Direction Indicator");
            indicatorObject.transform.SetParent(transform, false);
            indicatorObject.transform.localRotation = Quaternion.Euler(
                0f,
                0f,
                indicatorRotationOffsetDegrees);
            indicatorObject.transform.localScale = Vector3.one * indicatorScale;

            directionIndicator = indicatorObject.AddComponent<SpriteRenderer>();
            directionIndicator.sprite = indicatorSprite;
            directionIndicator.color = indicatorColor;
            directionIndicator.sortingOrder = 21;

            if (indicatorSprite == null)
            {
                Debug.LogWarning(
                    "RobotMarkerView is missing its direction Indicator Sprite.",
                    this);
            }
        }

        private static Sprite CreateCircularFillSprite(out Texture2D texture)
        {
            const int Resolution = 128;
            const float OuterFadeStart = 0.94f;

            texture = new Texture2D(
                Resolution,
                Resolution,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Runtime Robot Body Fill",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color[Resolution * Resolution];
            Vector2 center = Vector2.one * (Resolution * 0.5f);
            float radius = Resolution * 0.5f;

            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    Vector2 pixelCenter = new Vector2(x + 0.5f, y + 0.5f);
                    float normalizedDistance = Vector2.Distance(pixelCenter, center) / radius;
                    float outerCoverage = 1f - Mathf.SmoothStep(
                        OuterFadeStart,
                        1f,
                        normalizedDistance);

                    if (outerCoverage <= 0f)
                    {
                        pixels[y * Resolution + x] = Color.clear;
                        continue;
                    }

                    pixels[y * Resolution + x] = new Color(1f, 1f, 1f, outerCoverage);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, Resolution, Resolution),
                Vector2.one * 0.5f,
                Resolution,
                0,
                SpriteMeshType.FullRect);
            sprite.name = "Runtime Robot Body Fill";
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private void OnDestroy()
        {
            if (generatedBodySprite != null)
                Destroy(generatedBodySprite);

            if (generatedBodyTexture != null)
                Destroy(generatedBodyTexture);
        }

        private void OnValidate()
        {
            bodyDiameter = Mathf.Max(0.1f, bodyDiameter);
            bodyArtworkVisibleDiameterPixels = Mathf.Max(1f, bodyArtworkVisibleDiameterPixels);
            bodyFillDiameterRatio = Mathf.Clamp(bodyFillDiameterRatio, 0.1f, 1f);
            indicatorScale = Mathf.Max(0.1f, indicatorScale);
            ApplyMotionTailVisibility();
        }
    }
}
