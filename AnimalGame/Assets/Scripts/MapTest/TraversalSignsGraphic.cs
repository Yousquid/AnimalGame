using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace AnimalGame.MapTest
{
    public readonly struct TraversalSignRenderData
    {
        public TraversalSignRenderData(
            Vector2 screenPositionPixels,
            float sizePixels,
            float rotationDegrees,
            Color color)
        {
            ScreenPositionPixels = screenPositionPixels;
            SizePixels = sizePixels;
            RotationDegrees = rotationDegrees;
            Color = color;
        }

        public Vector2 ScreenPositionPixels { get; }
        public float SizePixels { get; }
        public float RotationDegrees { get; }
        public Color32 Color { get; }
    }

    /// <summary>
    /// Draws every sign that shares one sprite as a single UI mesh. This replaces
    /// hundreds of Image/CanvasRenderer objects and keeps every quad screen-aligned.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TraversalSignsGraphic : MaskableGraphic
    {
        private Sprite signSprite;
        private IReadOnlyList<TraversalSignRenderData> activeSigns;

        public override Texture mainTexture => signSprite != null
            ? signSprite.texture
            : s_WhiteTexture;

        public void Initialize(Sprite sprite)
        {
            raycastTarget = false;
            SetSprite(sprite);
        }

        public void SetSprite(Sprite sprite)
        {
            if (signSprite == sprite)
                return;

            signSprite = sprite;
            SetMaterialDirty();
            SetVerticesDirty();
        }

        public void SetSigns(IReadOnlyList<TraversalSignRenderData> signs)
        {
            activeSigns = signs;
            SetVerticesDirty();
        }

        public void ClearSigns()
        {
            activeSigns = null;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (signSprite == null || activeSigns == null || activeSigns.Count == 0)
                return;

            Vector4 uv = DataUtility.GetOuterUV(signSprite);
            float spriteWidth = Mathf.Max(1f, signSprite.rect.width);
            float spriteHeight = Mathf.Max(1f, signSprite.rect.height);
            float aspect = spriteWidth / spriteHeight;

            for (int index = 0; index < activeSigns.Count; index++)
            {
                TraversalSignRenderData sign = activeSigns[index];
                float width = Mathf.Max(0.01f, sign.SizePixels);
                float height = width;
                if (aspect > 1f)
                    height /= aspect;
                else
                    width *= aspect;

                float halfWidth = width * 0.5f;
                float halfHeight = height * 0.5f;
                float radians = sign.RotationDegrees * Mathf.Deg2Rad;
                float cosine = Mathf.Cos(radians);
                float sine = Mathf.Sin(radians);
                Vector2 centre = sign.ScreenPositionPixels;

                Vector2 bottomLeft = centre + Rotate(
                    new Vector2(-halfWidth, -halfHeight),
                    cosine,
                    sine);
                Vector2 topLeft = centre + Rotate(
                    new Vector2(-halfWidth, halfHeight),
                    cosine,
                    sine);
                Vector2 topRight = centre + Rotate(
                    new Vector2(halfWidth, halfHeight),
                    cosine,
                    sine);
                Vector2 bottomRight = centre + Rotate(
                    new Vector2(halfWidth, -halfHeight),
                    cosine,
                    sine);

                int firstVertex = vertexHelper.currentVertCount;
                Color32 vertexColor = sign.Color;
                vertexHelper.AddVert(bottomLeft, vertexColor, new Vector2(uv.x, uv.y));
                vertexHelper.AddVert(topLeft, vertexColor, new Vector2(uv.x, uv.w));
                vertexHelper.AddVert(topRight, vertexColor, new Vector2(uv.z, uv.w));
                vertexHelper.AddVert(bottomRight, vertexColor, new Vector2(uv.z, uv.y));
                vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
                vertexHelper.AddTriangle(firstVertex, firstVertex + 2, firstVertex + 3);
            }
        }

        private static Vector2 Rotate(Vector2 point, float cosine, float sine)
        {
            return new Vector2(
                point.x * cosine - point.y * sine,
                point.x * sine + point.y * cosine);
        }
    }
}