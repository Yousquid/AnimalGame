using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    [Serializable]
    public sealed class AnimalResultPhoto
    {
        [SerializeField] private Sprite photo;
        [Tooltip("Normalized crop inside the source sprite. This lets a library image keep transparent layout padding outside the actual photograph.")]
        [SerializeField] private Rect normalizedCrop = new Rect(0f, 0f, 1f, 1f);

        public Sprite Photo => photo;
        public bool IsValid => photo != null && photo.texture != null;

        public Rect GetTextureUvRect()
        {
            if (!IsValid)
                return new Rect(0f, 0f, 1f, 1f);

            Rect spritePixels = photo.textureRect;
            float textureWidth = Mathf.Max(1f, photo.texture.width);
            float textureHeight = Mathf.Max(1f, photo.texture.height);
            Rect crop = ClampNormalizedRect(normalizedCrop);
            return new Rect(
                (spritePixels.x + spritePixels.width * crop.x)
                / textureWidth,
                (spritePixels.y + spritePixels.height * crop.y)
                / textureHeight,
                spritePixels.width * crop.width / textureWidth,
                spritePixels.height * crop.height / textureHeight);
        }

        private static Rect ClampNormalizedRect(Rect value)
        {
            float xMin = Mathf.Clamp(value.xMin, 0f, 0.9999f);
            float yMin = Mathf.Clamp(value.yMin, 0f, 0.9999f);
            float xMax = Mathf.Clamp(value.xMax, xMin + 0.0001f, 1f);
            float yMax = Mathf.Clamp(value.yMax, yMin + 0.0001f, 1f);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }

    /// <summary>
    /// Defines the photographable body and the authored result-photo library
    /// for one animal species prefab.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AnimalAgent))]
    [AddComponentMenu("Animal Game/Animals/Animal Photo Subject")]
    public sealed class AnimalPhotoSubject : MonoBehaviour
    {
        private static readonly HashSet<AnimalPhotoSubject> ActiveSubjects =
            new HashSet<AnimalPhotoSubject>();
        private static readonly Dictionary<string, int>
            LastPhotoIndexBySpecies = new Dictionary<string, int>();

        [Header("Species Result Identity")]
        [SerializeField] private string speciesId = "unknown";
        [SerializeField] private string displayName = "Unknown Animal";
        [SerializeField] private string englishName = "Unknown Animal";
        [SerializeField] private string scientificName = "Species unknown";
        [SerializeField] private string regionName = "Rocky Mountains";
        [SerializeField] private Color accentColor =
            new Color(1f, 0.82f, 0.18f, 1f);

        [Header("Recognition And Rewards")]
        [SerializeField, Min(0)] private int cognitionDegrees = 100;
        [SerializeField, Min(0)] private int baseReward = 10;
        [SerializeField, Min(0)] private int cognitionReward;

        [Header("Photo Detection")]
        [Tooltip("Only these authored body renderers define how much of the animal is inside the camera frame. Do not include the unknown-static field or direction indicator.")]
        [SerializeField] private SpriteRenderer[] photoBoundsRenderers =
            Array.Empty<SpriteRenderer>();

        [Header("Authored Result Photo Library")]
        [SerializeField] private AnimalResultPhoto[] resultPhotoLibrary =
            Array.Empty<AnimalResultPhoto>();

        private AnimalAgent agent;
        private SpriteRenderer[] visibilityRenderers =
            Array.Empty<SpriteRenderer>();

        public static IReadOnlyCollection<AnimalPhotoSubject> Active =>
            ActiveSubjects;
        public string SpeciesId => string.IsNullOrWhiteSpace(speciesId)
            ? gameObject.name
            : speciesId;
        public string DisplayName => displayName;
        public string EnglishName => englishName;
        public string ScientificName => scientificName;
        public string RegionName => regionName;
        public Color AccentColor => accentColor;
        public int CognitionDegrees => Mathf.Max(0, cognitionDegrees);
        public int BaseReward => Mathf.Max(0, baseReward);
        public int CognitionReward => Mathf.Max(0, cognitionReward);

        private void Awake()
        {
            CacheRuntimeReferences();
        }

        private void OnEnable()
        {
            CacheRuntimeReferences();
            ActiveSubjects.Add(this);
        }

        private void OnDisable()
        {
            ActiveSubjects.Remove(this);
        }

        private void OnDestroy()
        {
            ActiveSubjects.Remove(this);
        }

        public bool IsPhotographable()
        {
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
                return false;

            if (agent == null)
                agent = GetComponent<AnimalAgent>();
            if (agent != null && agent.CurrentState == AnimalState.Despawned)
                return false;

            for (int index = 0; index < visibilityRenderers.Length; index++)
            {
                SpriteRenderer renderer = visibilityRenderers[index];
                if (renderer != null
                    && renderer.enabled
                    && renderer.gameObject.activeInHierarchy
                    && renderer.color.a > 0.001f)
                {
                    return true;
                }
            }

            return visibilityRenderers.Length == 0;
        }

        public bool TryGetWorldBounds(out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            if (photoBoundsRenderers == null)
                return false;

            for (int index = 0; index < photoBoundsRenderers.Length; index++)
            {
                SpriteRenderer renderer = photoBoundsRenderers[index];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds && bounds.size.sqrMagnitude > 0.000001f;
        }

        public bool TryChooseResultPhoto(out AnimalResultPhoto resultPhoto)
        {
            resultPhoto = null;
            if (resultPhotoLibrary == null || resultPhotoLibrary.Length == 0)
                return false;

            var validIndices = new List<int>(resultPhotoLibrary.Length);
            for (int index = 0; index < resultPhotoLibrary.Length; index++)
            {
                if (resultPhotoLibrary[index] != null
                    && resultPhotoLibrary[index].IsValid)
                {
                    validIndices.Add(index);
                }
            }

            if (validIndices.Count == 0)
                return false;

            string key = SpeciesId;
            bool hasPrevious = LastPhotoIndexBySpecies.TryGetValue(
                key,
                out int previousIndex);
            int selectedListIndex = UnityEngine.Random.Range(
                0,
                validIndices.Count);
            if (hasPrevious
                && validIndices.Count > 1
                && validIndices[selectedListIndex] == previousIndex)
            {
                selectedListIndex = (selectedListIndex
                                     + UnityEngine.Random.Range(
                                         1,
                                         validIndices.Count))
                                    % validIndices.Count;
            }

            int selectedIndex = validIndices[selectedListIndex];
            LastPhotoIndexBySpecies[key] = selectedIndex;
            resultPhoto = resultPhotoLibrary[selectedIndex];
            return true;
        }

        private void CacheRuntimeReferences()
        {
            if (agent == null)
                agent = GetComponent<AnimalAgent>();
            visibilityRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        }

        private void OnValidate()
        {
            speciesId = string.IsNullOrWhiteSpace(speciesId)
                ? "unknown"
                : speciesId.Trim();
            displayName = string.IsNullOrWhiteSpace(displayName)
                ? "Unknown Animal"
                : displayName.Trim();
            englishName = string.IsNullOrWhiteSpace(englishName)
                ? displayName
                : englishName.Trim();
            scientificName = string.IsNullOrWhiteSpace(scientificName)
                ? "Species unknown"
                : scientificName.Trim();
            regionName = string.IsNullOrWhiteSpace(regionName)
                ? "Unknown Region"
                : regionName.Trim();
            cognitionDegrees = Mathf.Max(0, cognitionDegrees);
            baseReward = Mathf.Max(0, baseReward);
            cognitionReward = Mathf.Max(0, cognitionReward);
            if (photoBoundsRenderers == null)
                photoBoundsRenderers = Array.Empty<SpriteRenderer>();
            if (resultPhotoLibrary == null)
                resultPhotoLibrary = Array.Empty<AnimalResultPhoto>();
        }
    }
}
