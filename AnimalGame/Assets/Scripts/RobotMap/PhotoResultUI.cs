using System;
using System.Collections.Generic;
using AnimalGame.Animals;
using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalGame.RobotMap
{
    [DefaultExecutionOrder(360)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/UI/Photo Result UI")]
    public sealed class PhotoResultUI : MonoBehaviour
    {
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;

        [Header("Authored Result Layout")]
        [Tooltip("Full-screen picture shown after photographing a supported animal.")]
        [SerializeField] private Sprite referenceLayoutSprite;
        [Tooltip("Temporary demonstration mode. When enabled, all generated result UI is sealed off and only the authored full-screen picture is shown until B is pressed.")]
        [InspectorName("Reference Picture Only Mode")]
        [SerializeField] private bool showReferenceLayoutOverlay;

        [Header("Subject Detection")]
        [Tooltip("Shrinks the visible camera frame slightly before evaluating subjects, preventing a barely touching animal from counting as photographed.")]
        [SerializeField, Range(0f, 0.2f)]
        private float frameInsetNormalized = 0.025f;
        [Tooltip("Minimum fraction of the animal's authored body bounds that must lie inside the camera frame.")]
        [SerializeField, Range(0.01f, 1f)]
        private float minimumSubjectCoverage = 0.18f;
        [SerializeField, Min(1f)]
        private float minimumSubjectLongestSidePixels = 24f;
        [SerializeField, Min(1f)]
        private float minimumSubjectAreaPixels = 400f;
        [SerializeField, Min(0f)] private float coverageScoreWeight = 0.55f;
        [SerializeField, Min(0f)] private float centerednessScoreWeight = 0.3f;
        [SerializeField, Min(0f)] private float sizeScoreWeight = 0.15f;

        [Header("Result Presentation")]
        [SerializeField] private Font resultFont;
        [SerializeField, Min(0.05f)] private float entryDuration = 0.45f;
        [SerializeField] private Color backgroundColor =
            new Color(0.008f, 0.012f, 0.014f, 1f);
        [SerializeField] private Color gridColor =
            new Color(0.34f, 0.38f, 0.4f, 0.24f);
        [SerializeField] private Color primaryColor =
            new Color(0.94f, 0.98f, 1f, 1f);
        [SerializeField] private Color defaultAccentColor =
            new Color(1f, 0.82f, 0.18f, 1f);

        [Header("Keyboard Result Controls")]
        [SerializeField] private KeyCode closeKey = KeyCode.B;
        [SerializeField] private KeyCode alternateCloseKey = KeyCode.Escape;
        [SerializeField] private KeyCode saveKey = KeyCode.Y;

        private readonly Vector2[] captureFrameCorners = new Vector2[4];
        private readonly Vector3[] subjectWorldCorners = new Vector3[4];
        private readonly List<Vector2> clipInput = new List<Vector2>(12);
        private readonly List<Vector2> clipOutput = new List<Vector2>(12);

        private PhotoModeController controller;
        private PhotoModeUI photoModeUi;
        private Camera mapCamera;
        private MapTestSceneController map;

        private RectTransform resultCanvasRoot;
        private Canvas resultCanvas;
        private CanvasGroup contentGroup;
        private RectTransform contentRoot;
        private RectTransform photoRoot;
        private RawImage photoImage;
        private Image referenceOverlay;
        private PhotoResultBackdropGraphic backdrop;
        private Text identityText;
        private Text recognitionText;
        private Text rewardText;
        private Text totalRewardText;
        private Text controlsText;
        private Text metadataText;
        private Text savedText;

        private PhotoResultSnapshot pendingResult;
        private PhotoResultSnapshot displayedResult;
        private bool visible;
        private bool saved;
        private float entryElapsed;
        private Vector2 photoRestPosition;

        public bool IsVisible => visible;

        private void Awake()
        {
            EnsureVisuals();
        }

        public void Initialize(
            PhotoModeController photoModeController,
            PhotoModeUI cameraUi,
            Camera camera,
            MapTestSceneController mapController)
        {
            if (controller != null)
                controller.PhotoCaptured -= HandlePhotoCaptured;

            controller = photoModeController;
            photoModeUi = cameraUi;
            mapCamera = camera;
            map = mapController;
            EnsureVisuals();

            if (controller != null)
                controller.PhotoCaptured += HandlePhotoCaptured;

            HideResult(false);
        }

        private void Update()
        {
            if (pendingResult != null
                && controller != null
                && controller.IsReviewing
                && !visible)
            {
                ShowPendingResult();
            }

            if (!visible)
                return;

            if (controller == null
                || !controller.IsActive
                || !controller.IsReviewing)
            {
                HideResult(false);
                return;
            }

            if (!IsReferencePictureOnlyMode)
                UpdateEntryAnimation();

            bool closePressed = Input.GetKeyDown(closeKey)
                                || Input.GetKeyDown(alternateCloseKey)
                                || AdaptiveLegacyGamepadInput
                                    .WasEastFaceButtonPressedThisFrame();
            if (closePressed)
            {
                HideResult(true);
                return;
            }

            if (!IsReferencePictureOnlyMode)
            {
                bool savePressed = Input.GetKeyDown(saveKey)
                                   || AdaptiveLegacyGamepadInput
                                       .WasNorthFaceButtonPressedThisFrame();
                if (savePressed)
                    SaveDisplayedResult();
            }
        }

        private void HandlePhotoCaptured()
        {
            pendingResult = null;
            if (!TrySelectMainSubject(
                    out AnimalPhotoSubject subject,
                    out float frameCoverage))
            {
                return;
            }

            AnimalResultPhoto selectedPhoto = null;
            bool hasLibraryPhoto = subject.TryChooseResultPhoto(
                out selectedPhoto);
            if (!IsReferencePictureOnlyMode && !hasLibraryPhoto)
            {
                Debug.LogWarning(
                    $"Photo result skipped because '{subject.name}' has no valid authored result photo.",
                    subject);
                return;
            }

            pendingResult = CreateSnapshot(
                subject,
                selectedPhoto,
                frameCoverage);
            if (controller == null || !controller.RequestPhotoReview())
                pendingResult = null;
        }

        private bool TrySelectMainSubject(
            out AnimalPhotoSubject selectedSubject,
            out float selectedCoverage)
        {
            selectedSubject = null;
            selectedCoverage = 0f;
            if (mapCamera == null
                || photoModeUi == null
                || !photoModeUi.TryGetCaptureFrameScreenCorners(
                    captureFrameCorners,
                    frameInsetNormalized))
            {
                return false;
            }

            Vector2 frameCenter = Vector2.zero;
            for (int index = 0; index < captureFrameCorners.Length; index++)
                frameCenter += captureFrameCorners[index];
            frameCenter *= 0.25f;

            float frameWidth = Mathf.Max(
                Vector2.Distance(
                    captureFrameCorners[0],
                    captureFrameCorners[3]),
                Vector2.Distance(
                    captureFrameCorners[1],
                    captureFrameCorners[2]));
            float frameHeight = Mathf.Max(
                Vector2.Distance(
                    captureFrameCorners[0],
                    captureFrameCorners[1]),
                Vector2.Distance(
                    captureFrameCorners[3],
                    captureFrameCorners[2]));
            float frameHalfDiagonal = Mathf.Max(
                1f,
                0.5f * Mathf.Sqrt(
                    frameWidth * frameWidth
                    + frameHeight * frameHeight));
            float frameLongestSide = Mathf.Max(1f, frameWidth, frameHeight);

            float bestScore = float.NegativeInfinity;
            foreach (AnimalPhotoSubject subject in AnimalPhotoSubject.Active)
            {
                if (subject == null
                    || !subject.IsPhotographable()
                    || !subject.TryGetWorldBounds(out Bounds worldBounds)
                    || !TryProjectBoundsToScreen(
                        worldBounds,
                        out Rect subjectScreenRect))
                {
                    continue;
                }

                float subjectArea = subjectScreenRect.width
                                    * subjectScreenRect.height;
                float subjectLongestSide = Mathf.Max(
                    subjectScreenRect.width,
                    subjectScreenRect.height);
                if (subjectArea < minimumSubjectAreaPixels
                    || subjectLongestSide
                    < minimumSubjectLongestSidePixels)
                {
                    continue;
                }

                float intersectionArea = CalculateRectFrameIntersectionArea(
                    subjectScreenRect,
                    captureFrameCorners);
                float coverage = subjectArea > 0.0001f
                    ? intersectionArea / subjectArea
                    : 0f;
                if (coverage < minimumSubjectCoverage)
                    continue;

                float centeredness = 1f - Mathf.Clamp01(
                    Vector2.Distance(
                        subjectScreenRect.center,
                        frameCenter)
                    / frameHalfDiagonal);
                float relativeSize = Mathf.Clamp01(
                    subjectLongestSide / frameLongestSide);
                float score = coverage * coverageScoreWeight
                              + centeredness * centerednessScoreWeight
                              + relativeSize * sizeScoreWeight;
                if (score <= bestScore)
                    continue;

                bestScore = score;
                selectedSubject = subject;
                selectedCoverage = coverage;
            }

            return selectedSubject != null;
        }

        private bool TryProjectBoundsToScreen(
            Bounds bounds,
            out Rect screenRect)
        {
            screenRect = default;
            subjectWorldCorners[0] = new Vector3(
                bounds.min.x,
                bounds.min.y,
                bounds.center.z);
            subjectWorldCorners[1] = new Vector3(
                bounds.min.x,
                bounds.max.y,
                bounds.center.z);
            subjectWorldCorners[2] = new Vector3(
                bounds.max.x,
                bounds.max.y,
                bounds.center.z);
            subjectWorldCorners[3] = new Vector3(
                bounds.max.x,
                bounds.min.y,
                bounds.center.z);

            float minimumX = float.PositiveInfinity;
            float minimumY = float.PositiveInfinity;
            float maximumX = float.NegativeInfinity;
            float maximumY = float.NegativeInfinity;
            for (int index = 0; index < subjectWorldCorners.Length; index++)
            {
                Vector3 screenPoint = mapCamera.WorldToScreenPoint(
                    subjectWorldCorners[index]);
                if (screenPoint.z <= 0f)
                    return false;

                minimumX = Mathf.Min(minimumX, screenPoint.x);
                minimumY = Mathf.Min(minimumY, screenPoint.y);
                maximumX = Mathf.Max(maximumX, screenPoint.x);
                maximumY = Mathf.Max(maximumY, screenPoint.y);
            }

            screenRect = Rect.MinMaxRect(
                minimumX,
                minimumY,
                maximumX,
                maximumY);
            return screenRect.width > 0.0001f
                   && screenRect.height > 0.0001f;
        }

        private float CalculateRectFrameIntersectionArea(
            Rect subjectRect,
            Vector2[] frameCorners)
        {
            clipInput.Clear();
            clipInput.Add(new Vector2(subjectRect.xMin, subjectRect.yMin));
            clipInput.Add(new Vector2(subjectRect.xMax, subjectRect.yMin));
            clipInput.Add(new Vector2(subjectRect.xMax, subjectRect.yMax));
            clipInput.Add(new Vector2(subjectRect.xMin, subjectRect.yMax));

            float frameOrientation = Mathf.Sign(
                CalculateSignedArea(frameCorners));
            if (Mathf.Approximately(frameOrientation, 0f))
                return 0f;

            List<Vector2> input = clipInput;
            List<Vector2> output = clipOutput;
            for (int edgeIndex = 0;
                 edgeIndex < frameCorners.Length;
                 edgeIndex++)
            {
                output.Clear();
                if (input.Count == 0)
                    return 0f;

                Vector2 edgeStart = frameCorners[edgeIndex];
                Vector2 edgeEnd = frameCorners[
                    (edgeIndex + 1) % frameCorners.Length];
                Vector2 previous = input[input.Count - 1];
                bool previousInside = IsInsideClipEdge(
                    previous,
                    edgeStart,
                    edgeEnd,
                    frameOrientation);

                for (int pointIndex = 0;
                     pointIndex < input.Count;
                     pointIndex++)
                {
                    Vector2 current = input[pointIndex];
                    bool currentInside = IsInsideClipEdge(
                        current,
                        edgeStart,
                        edgeEnd,
                        frameOrientation);
                    if (currentInside)
                    {
                        if (!previousInside)
                        {
                            output.Add(IntersectLines(
                                previous,
                                current,
                                edgeStart,
                                edgeEnd));
                        }

                        output.Add(current);
                    }
                    else if (previousInside)
                    {
                        output.Add(IntersectLines(
                            previous,
                            current,
                            edgeStart,
                            edgeEnd));
                    }

                    previous = current;
                    previousInside = currentInside;
                }

                List<Vector2> swap = input;
                input = output;
                output = swap;
            }

            return Mathf.Abs(CalculateSignedArea(input));
        }

        private static bool IsInsideClipEdge(
            Vector2 point,
            Vector2 edgeStart,
            Vector2 edgeEnd,
            float orientation)
        {
            float cross = Cross(edgeEnd - edgeStart, point - edgeStart);
            return cross * orientation >= -0.001f;
        }

        private static Vector2 IntersectLines(
            Vector2 segmentStart,
            Vector2 segmentEnd,
            Vector2 lineStart,
            Vector2 lineEnd)
        {
            Vector2 segment = segmentEnd - segmentStart;
            Vector2 line = lineEnd - lineStart;
            float denominator = Cross(segment, line);
            if (Mathf.Abs(denominator) <= 0.00001f)
                return segmentEnd;

            float time = Cross(lineStart - segmentStart, line)
                         / denominator;
            return segmentStart + segment * time;
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private static float CalculateSignedArea(IList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return 0f;

            float twiceArea = 0f;
            for (int index = 0; index < polygon.Count; index++)
            {
                Vector2 current = polygon[index];
                Vector2 next = polygon[(index + 1) % polygon.Count];
                twiceArea += current.x * next.y - next.x * current.y;
            }

            return twiceArea * 0.5f;
        }

        private PhotoResultSnapshot CreateSnapshot(
            AnimalPhotoSubject subject,
            AnimalResultPhoto selectedPhoto,
            float frameCoverage)
        {
            Vector2 mapPosition = Vector2.zero;
            float heightMeters = 0f;
            if (map != null)
            {
                map.TrySampleWorldPosition(
                    subject.transform.position,
                    out mapPosition,
                    out heightMeters);
            }

            string levelName = map != null && map.LevelAsset != null
                ? map.LevelAsset.name.Replace('_', ' ')
                : subject.RegionName;
            return new PhotoResultSnapshot(
                subject.SpeciesId,
                subject.DisplayName,
                subject.EnglishName,
                subject.ScientificName,
                string.IsNullOrWhiteSpace(subject.RegionName)
                    ? levelName
                    : subject.RegionName,
                subject.AccentColor,
                subject.CognitionDegrees,
                subject.BaseReward,
                subject.CognitionReward,
                selectedPhoto,
                mapPosition,
                heightMeters,
                Mathf.Clamp01(frameCoverage),
                DateTime.Now);
        }

        private void ShowPendingResult()
        {
            if (pendingResult == null)
                return;

            EnsureVisuals();
            displayedResult = pendingResult;
            pendingResult = null;
            visible = true;
            saved = false;
            entryElapsed = 0f;

            if (resultCanvasRoot != null)
                resultCanvasRoot.gameObject.SetActive(true);
            if (resultCanvas != null)
                resultCanvas.enabled = true;

            if (IsReferencePictureOnlyMode)
            {
                if (referenceOverlay != null)
                {
                    referenceOverlay.sprite = referenceLayoutSprite;
                    referenceOverlay.color = Color.white;
                    referenceOverlay.enabled = true;
                    referenceOverlay.transform.SetAsLastSibling();
                }

                if (backdrop != null)
                    backdrop.enabled = false;
                if (contentRoot != null)
                    contentRoot.gameObject.SetActive(false);

                Canvas.ForceUpdateCanvases();
                return;
            }

            if (contentRoot != null)
            {
                contentRoot.gameObject.SetActive(true);
                contentRoot.SetAsLastSibling();
            }
            if (referenceOverlay != null)
                referenceOverlay.enabled = false;
            if (backdrop != null)
                backdrop.enabled = true;

            SetDynamicTextVisibility(true);

            if (backdrop != null)
                backdrop.SetAccent(displayedResult.AccentColor);
            if (photoImage != null)
            {
                photoImage.texture = displayedResult.Photo.Photo.texture;
                photoImage.uvRect = displayedResult.Photo.GetTextureUvRect();
            }

            recognitionText.text =
                $"{displayedResult.CognitionDegrees}° 物种认知";
            identityText.text =
                $"{displayedResult.DisplayName}\n{displayedResult.EnglishName}";
            rewardText.text =
                $"基础奖励  +{displayedResult.BaseReward}\n"
                + $"认知奖励  +{displayedResult.CognitionReward}";
            controlsText.text = "图鉴   [MENU]\n[Y] 保存照片\n[B] 返回";
            metadataText.text = BuildMetadata(displayedResult);
            savedText.text = string.Empty;
            savedText.enabled = false;

            ApplyEntryAnimation(0f);
            Canvas.ForceUpdateCanvases();
        }

        private bool IsReferencePictureOnlyMode =>
            showReferenceLayoutOverlay && referenceLayoutSprite != null;

        private static string BuildMetadata(PhotoResultSnapshot result)
        {
            return $"{result.ScientificName}  /  "
                   + $"X:{result.MapPositionMeters.x:0.0}  "
                   + $"Y:{result.MapPositionMeters.y:0.0}  "
                   + $"H:{result.HeightMeters:0.0}m  /  "
                   + $"{result.RegionName}  /  "
                   + result.CapturedAt.ToString("yyyy.MM.dd  HH:mm:ss");
        }

        private void UpdateEntryAnimation()
        {
            entryElapsed = Mathf.Min(
                Mathf.Max(0.05f, entryDuration),
                entryElapsed + Mathf.Max(0f, Time.unscaledDeltaTime));
            float progress = Mathf.Clamp01(
                entryElapsed / Mathf.Max(0.05f, entryDuration));
            ApplyEntryAnimation(progress);
        }

        private void ApplyEntryAnimation(float progress)
        {
            float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(progress), 3f);
            if (contentGroup != null)
            {
                // Never start completely transparent. Some result canvases
                // can be activated after their owner's LateUpdate, which
                // otherwise leaves the first rendered review frame looking
                // like an empty black screen.
                contentGroup.alpha = Mathf.Lerp(0.82f, 1f, eased);
            }
            if (contentRoot != null)
                contentRoot.localScale = Vector3.one * Mathf.Lerp(0.985f, 1f, eased);
            if (photoRoot != null)
            {
                photoRoot.anchoredPosition = photoRestPosition
                                             + Vector2.Lerp(
                                                 new Vector2(-58f, 24f),
                                                 Vector2.zero,
                                                 eased);
                photoRoot.localScale = Vector3.one
                                       * Mathf.Lerp(0.92f, 1f, eased);
            }

            if (displayedResult != null && totalRewardText != null)
            {
                int displayedReward = Mathf.RoundToInt(
                    displayedResult.TotalReward * eased);
                totalRewardText.text = $"总计  {displayedReward}";
            }
        }

        private void SaveDisplayedResult()
        {
            if (saved || displayedResult == null)
                return;

            PhotoAlbumService.Save(displayedResult);
            saved = true;
            savedText.enabled = true;
            savedText.text = "已保存到图鉴";
            controlsText.text = "图鉴   [MENU]\n[Y] 已保存\n[B] 返回";
        }

        private void HideResult(bool returnToCamera)
        {
            visible = false;
            pendingResult = null;
            displayedResult = null;
            saved = false;
            if (resultCanvasRoot != null)
                resultCanvasRoot.gameObject.SetActive(false);

            if (returnToCamera)
                controller?.EndPhotoReview();
        }

        private void EnsureVisuals()
        {
            if (resultCanvasRoot != null)
                return;

            Canvas parentCanvas = GetComponentInParent<Canvas>();
            var canvasObject = new GameObject(
                "Animal Photo Result UI",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.layer = LayerMask.NameToLayer("UI");
            resultCanvasRoot = canvasObject.GetComponent<RectTransform>();
            resultCanvas = canvasObject.GetComponent<Canvas>();
            resultCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            resultCanvas.overrideSorting = true;
            resultCanvas.sortingLayerID = parentCanvas != null
                ? parentCanvas.sortingLayerID
                : 0;
            resultCanvas.sortingOrder = parentCanvas != null
                ? parentCanvas.sortingOrder + 20
                : 50;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(
                ReferenceWidth,
                ReferenceHeight);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;

            GameObject backgroundObject = CreateUiObject(
                "Opaque Result Background",
                resultCanvasRoot,
                typeof(Image));
            RectTransform backgroundRect =
                backgroundObject.GetComponent<RectTransform>();
            Stretch(backgroundRect);
            Image background = backgroundObject.GetComponent<Image>();
            background.color = backgroundColor;
            background.raycastTarget = false;

            GameObject backdropObject = CreateUiObject(
                "Technical Result Backdrop",
                resultCanvasRoot,
                typeof(PhotoResultBackdropGraphic));
            RectTransform backdropRect =
                backdropObject.GetComponent<RectTransform>();
            Stretch(backdropRect);
            backdrop = backdropObject.GetComponent<PhotoResultBackdropGraphic>();
            backdrop.raycastTarget = false;
            backdrop.Configure(gridColor, primaryColor, defaultAccentColor);

            GameObject contentObject = CreateUiObject(
                "Dynamic Result Content",
                resultCanvasRoot,
                typeof(CanvasGroup));
            contentRoot = contentObject.GetComponent<RectTransform>();
            Stretch(contentRoot);
            contentGroup = contentObject.GetComponent<CanvasGroup>();
            contentGroup.interactable = false;
            contentGroup.blocksRaycasts = false;

            CreatePhotoPresentation();
            CreateTextPresentation();
            CreateReferenceOverlay();
            resultCanvasRoot.gameObject.SetActive(false);
        }

        private void CreatePhotoPresentation()
        {
            GameObject photoObject = CreateUiObject(
                "Authored Animal Photo",
                contentRoot,
                typeof(Image));
            photoRoot = photoObject.GetComponent<RectTransform>();
            photoRoot.anchorMin = new Vector2(0.5f, 0.5f);
            photoRoot.anchorMax = new Vector2(0.5f, 0.5f);
            photoRoot.pivot = new Vector2(0.5f, 0.5f);
            photoRestPosition = new Vector2(-398f, 0f);
            photoRoot.anchoredPosition = photoRestPosition;
            photoRoot.sizeDelta = new Vector2(900f, 940f);
            photoRoot.localRotation = Quaternion.Euler(0f, 0f, -2.4f);
            Image backing = photoObject.GetComponent<Image>();
            backing.color = primaryColor;
            backing.raycastTarget = false;

            GameObject rawImageObject = CreateUiObject(
                "Selected Species Library Photo",
                photoRoot,
                typeof(RawImage));
            RectTransform rawRect =
                rawImageObject.GetComponent<RectTransform>();
            Stretch(rawRect);
            rawRect.offsetMin = new Vector2(6f, 6f);
            rawRect.offsetMax = new Vector2(-6f, -6f);
            photoImage = rawImageObject.GetComponent<RawImage>();
            photoImage.color = Color.white;
            photoImage.raycastTarget = false;
        }

        private void CreateTextPresentation()
        {
            Font font = ResolveFont();
            metadataText = CreateText(
                "Photo Metadata",
                contentRoot,
                font,
                21,
                TextAnchor.MiddleCenter,
                primaryColor,
                new Vector2(-906f, 0f),
                new Vector2(1000f, 38f),
                90f);
            recognitionText = CreateText(
                "Recognition Heading",
                contentRoot,
                font,
                53,
                TextAnchor.MiddleLeft,
                primaryColor,
                new Vector2(345f, 205f),
                new Vector2(660f, 90f));
            rewardText = CreateText(
                "Reward Breakdown",
                contentRoot,
                font,
                34,
                TextAnchor.UpperLeft,
                primaryColor,
                new Vector2(450f, -8f),
                new Vector2(510f, 120f));
            rewardText.lineSpacing = 1.25f;
            totalRewardText = CreateText(
                "Total Reward",
                contentRoot,
                font,
                37,
                TextAnchor.MiddleLeft,
                primaryColor,
                new Vector2(545f, -142f),
                new Vector2(390f, 65f));
            controlsText = CreateText(
                "Photo Result Controls",
                contentRoot,
                font,
                34,
                TextAnchor.UpperLeft,
                primaryColor,
                new Vector2(190f, -34f),
                new Vector2(430f, 190f));
            controlsText.lineSpacing = 1.12f;
            identityText = CreateText(
                "Species Identity",
                contentRoot,
                font,
                57,
                TextAnchor.UpperLeft,
                primaryColor,
                new Vector2(528f, -365f),
                new Vector2(650f, 150f));
            identityText.fontStyle = FontStyle.Normal;
            savedText = CreateText(
                "Saved Confirmation",
                contentRoot,
                font,
                25,
                TextAnchor.MiddleLeft,
                defaultAccentColor,
                new Vector2(285f, -170f),
                new Vector2(330f, 45f));
        }

        private void CreateReferenceOverlay()
        {
            GameObject overlayObject = CreateUiObject(
                "Optional Reference Layout Overlay",
                resultCanvasRoot,
                typeof(Image));
            RectTransform overlayRect =
                overlayObject.GetComponent<RectTransform>();
            Stretch(overlayRect);
            referenceOverlay = overlayObject.GetComponent<Image>();
            referenceOverlay.sprite = referenceLayoutSprite;
            referenceOverlay.color = Color.white;
            referenceOverlay.preserveAspect = true;
            referenceOverlay.raycastTarget = false;
            referenceOverlay.enabled = false;
        }

        private void SetDynamicTextVisibility(bool visibleState)
        {
            if (identityText != null)
                identityText.enabled = visibleState;
            if (recognitionText != null)
                recognitionText.enabled = visibleState;
            if (rewardText != null)
                rewardText.enabled = visibleState;
            if (totalRewardText != null)
                totalRewardText.enabled = visibleState;
            if (controlsText != null)
                controlsText.enabled = visibleState;
            if (metadataText != null)
                metadataText.enabled = visibleState;
        }

        private Font ResolveFont()
        {
            if (resultFont != null)
                return resultFont;

            string[] preferredFonts =
            {
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "SimHei",
                "Arial"
            };
            resultFont = Font.CreateDynamicFontFromOSFont(
                preferredFonts,
                36);
            if (resultFont == null)
            {
                resultFont = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            }

            return resultFont;
        }

        private static Text CreateText(
            string objectName,
            Transform parent,
            Font font,
            int fontSize,
            TextAnchor alignment,
            Color color,
            Vector2 anchoredPosition,
            Vector2 size,
            float rotationDegrees = 0f)
        {
            GameObject textObject = CreateUiObject(
                objectName,
                parent,
                typeof(Text));
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = anchoredPosition;
            textRect.sizeDelta = size;
            textRect.localRotation = Quaternion.Euler(
                0f,
                0f,
                rotationDegrees);

            Text text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static GameObject CreateUiObject(
            string objectName,
            Transform parent,
            params Type[] additionalComponents)
        {
            var componentTypes = new List<Type>
            {
                typeof(RectTransform),
                typeof(CanvasRenderer)
            };
            for (int index = 0; index < additionalComponents.Length; index++)
            {
                Type componentType = additionalComponents[index];
                if (componentType != null
                    && !componentTypes.Contains(componentType))
                {
                    componentTypes.Add(componentType);
                }
            }

            var result = new GameObject(
                objectName,
                componentTypes.ToArray());
            result.layer = parent.gameObject.layer;
            result.transform.SetParent(parent, false);
            return result;
        }

        private static void Stretch(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = Vector2.one * 0.5f;
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.localScale = Vector3.one;
        }

        private void OnDestroy()
        {
            if (controller != null)
                controller.PhotoCaptured -= HandlePhotoCaptured;
            if (resultCanvasRoot != null)
                Destroy(resultCanvasRoot.gameObject);
        }

        private void OnValidate()
        {
            frameInsetNormalized = Mathf.Clamp(
                frameInsetNormalized,
                0f,
                0.2f);
            minimumSubjectCoverage = Mathf.Clamp01(
                minimumSubjectCoverage);
            minimumSubjectLongestSidePixels = Mathf.Max(
                1f,
                minimumSubjectLongestSidePixels);
            minimumSubjectAreaPixels = Mathf.Max(
                1f,
                minimumSubjectAreaPixels);
            coverageScoreWeight = Mathf.Max(0f, coverageScoreWeight);
            centerednessScoreWeight = Mathf.Max(
                0f,
                centerednessScoreWeight);
            sizeScoreWeight = Mathf.Max(0f, sizeScoreWeight);
            entryDuration = Mathf.Max(0.05f, entryDuration);
        }
    }

    public sealed class PhotoResultSnapshot
    {
        public PhotoResultSnapshot(
            string speciesId,
            string displayName,
            string englishName,
            string scientificName,
            string regionName,
            Color accentColor,
            int cognitionDegrees,
            int baseReward,
            int cognitionReward,
            AnimalResultPhoto photo,
            Vector2 mapPositionMeters,
            float heightMeters,
            float frameCoverage,
            DateTime capturedAt)
        {
            SpeciesId = speciesId;
            DisplayName = displayName;
            EnglishName = englishName;
            ScientificName = scientificName;
            RegionName = regionName;
            AccentColor = accentColor;
            CognitionDegrees = cognitionDegrees;
            BaseReward = baseReward;
            CognitionReward = cognitionReward;
            Photo = photo;
            MapPositionMeters = mapPositionMeters;
            HeightMeters = heightMeters;
            FrameCoverage = frameCoverage;
            CapturedAt = capturedAt;
        }

        public string SpeciesId { get; }
        public string DisplayName { get; }
        public string EnglishName { get; }
        public string ScientificName { get; }
        public string RegionName { get; }
        public Color AccentColor { get; }
        public int CognitionDegrees { get; }
        public int BaseReward { get; }
        public int CognitionReward { get; }
        public int TotalReward => BaseReward + CognitionReward;
        public AnimalResultPhoto Photo { get; }
        public Vector2 MapPositionMeters { get; }
        public float HeightMeters { get; }
        public float FrameCoverage { get; }
        public DateTime CapturedAt { get; }
    }

    public sealed class SavedAnimalPhotoRecord
    {
        internal SavedAnimalPhotoRecord(PhotoResultSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public PhotoResultSnapshot Snapshot { get; }
    }

    public static class PhotoAlbumService
    {
        private static readonly List<SavedAnimalPhotoRecord> SavedPhotos =
            new List<SavedAnimalPhotoRecord>();

        public static IReadOnlyList<SavedAnimalPhotoRecord> Photos =>
            SavedPhotos;

        public static void Save(PhotoResultSnapshot snapshot)
        {
            if (snapshot != null)
                SavedPhotos.Add(new SavedAnimalPhotoRecord(snapshot));
        }
    }

    public sealed class PhotoResultBackdropGraphic : MaskableGraphic
    {
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;

        private Color grid = new Color(1f, 1f, 1f, 0.16f);
        private Color primary = Color.white;
        private Color accent = new Color(1f, 0.82f, 0.18f, 1f);

        public void Configure(
            Color gridColor,
            Color primaryColor,
            Color accentColor)
        {
            grid = gridColor;
            primary = primaryColor;
            accent = accentColor;
            SetVerticesDirty();
        }

        public void SetAccent(Color accentColor)
        {
            accent = accentColor;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = rectTransform.rect;
            float scale = Mathf.Min(
                rect.width / ReferenceWidth,
                rect.height / ReferenceHeight);
            Vector2 origin = rect.center - new Vector2(
                ReferenceWidth * scale,
                ReferenceHeight * scale) * 0.5f;

            for (float x = 0f; x <= ReferenceWidth; x += 52f)
            {
                AddLine(
                    vertexHelper,
                    ToLocal(origin, scale, new Vector2(x, 0f)),
                    ToLocal(origin, scale, new Vector2(x, ReferenceHeight)),
                    2f * scale,
                    grid);
            }

            for (float y = 0f; y <= ReferenceHeight; y += 52f)
            {
                AddLine(
                    vertexHelper,
                    ToLocal(origin, scale, new Vector2(0f, y)),
                    ToLocal(origin, scale, new Vector2(ReferenceWidth, y)),
                    2f * scale,
                    grid);
            }

            AddLine(vertexHelper, ToLocal(origin, scale, new Vector2(1015f, 0f)), ToLocal(origin, scale, new Vector2(1168f, 1080f)), 2f * scale, primary);
            AddLine(vertexHelper, ToLocal(origin, scale, new Vector2(1170f, 75f)), ToLocal(origin, scale, new Vector2(1170f, 1005f)), 2f * scale, primary);
            AddLine(vertexHelper, ToLocal(origin, scale, new Vector2(970f, 835f)), ToLocal(origin, scale, new Vector2(1820f, 900f)), 2f * scale, primary);
            AddLine(vertexHelper, ToLocal(origin, scale, new Vector2(990f, 120f)), ToLocal(origin, scale, new Vector2(1740f, 88f)), 3f * scale, primary);
            AddLine(vertexHelper, ToLocal(origin, scale, new Vector2(1090f, 280f)), ToLocal(origin, scale, new Vector2(1735f, 880f)), 1f * scale, new Color(primary.r, primary.g, primary.b, 0.22f));

            AddArc(vertexHelper, origin, scale, new Vector2(1425f, 500f), 385f, -48f, 172f, 3f, primary);
            AddArc(vertexHelper, origin, scale, new Vector2(1425f, 500f), 355f, -45f, 167f, 2f, accent);
            AddArc(vertexHelper, origin, scale, new Vector2(1170f, 165f), 160f, -40f, 215f, 3f, primary);
            AddArc(vertexHelper, origin, scale, new Vector2(1170f, 165f), 140f, 32f, 255f, 1.5f, new Color(primary.r, primary.g, primary.b, 0.45f));
        }

        private static Vector2 ToLocal(
            Vector2 origin,
            float scale,
            Vector2 referencePoint)
        {
            return origin + referencePoint * scale;
        }

        private static void AddArc(
            VertexHelper vertexHelper,
            Vector2 origin,
            float scale,
            Vector2 center,
            float radius,
            float startDegrees,
            float endDegrees,
            float width,
            Color color)
        {
            const int SegmentCount = 64;
            Vector2 previous = Vector2.zero;
            for (int index = 0; index <= SegmentCount; index++)
            {
                float progress = index / (float)SegmentCount;
                float radians = Mathf.Lerp(startDegrees, endDegrees, progress)
                                * Mathf.Deg2Rad;
                Vector2 current = ToLocal(
                    origin,
                    scale,
                    center + new Vector2(
                        Mathf.Cos(radians),
                        Mathf.Sin(radians)) * radius);
                if (index > 0)
                {
                    AddLine(
                        vertexHelper,
                        previous,
                        current,
                        width * scale,
                        color);
                }

                previous = current;
            }
        }

        private static void AddLine(
            VertexHelper vertexHelper,
            Vector2 start,
            Vector2 end,
            float width,
            Color color)
        {
            Vector2 difference = end - start;
            if (difference.sqrMagnitude <= 0.0001f)
                return;

            Vector2 perpendicular = new Vector2(
                -difference.y,
                difference.x).normalized * Mathf.Max(0.5f, width * 0.5f);
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            var quad = new UIVertex[4];
            vertex.position = start - perpendicular;
            quad[0] = vertex;
            vertex.position = start + perpendicular;
            quad[1] = vertex;
            vertex.position = end + perpendicular;
            quad[2] = vertex;
            vertex.position = end - perpendicular;
            quad[3] = vertex;
            vertexHelper.AddUIVertexQuad(quad);
        }
    }
}
