using System;
using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Discovery
{
    public enum DiscoverableKind
    {
        Animal,
        Plant
    }

    /// <summary>
    /// Owns discovery state only. A later scan system can discover entities
    /// without knowing how each kind of entity presents its unknown state.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Discovery/Discoverable Entity")]
    public sealed class DiscoverableEntity : MonoBehaviour
    {
        private static readonly HashSet<DiscoverableEntity> ActiveEntities =
            new HashSet<DiscoverableEntity>();

        [SerializeField] private DiscoverableKind discoverableKind =
            DiscoverableKind.Animal;
        [SerializeField] private string discoveryId = "unknown";
        [SerializeField] private bool startDiscovered;
        [Tooltip("Biological-signal collision radius in logical map meters.")]
        [SerializeField, Min(0.01f)]
        private float biologicalScanCollisionRadiusMeters = 0.25f;

        private bool initialized;
        private bool isPermanentlyDiscovered;
        private float temporaryRevealRemaining;
        private HeightMapPlacedObject placedObject;

        public static IReadOnlyCollection<DiscoverableEntity> Active =>
            ActiveEntities;
        public DiscoverableKind Kind => discoverableKind;
        public string DiscoveryId => discoveryId;
        public bool IsPermanentlyDiscovered
        {
            get
            {
                EnsureInitialized();
                return isPermanentlyDiscovered;
            }
        }
        public bool IsTemporarilyRevealed => temporaryRevealRemaining > 0f;
        public bool IsDiscovered
        {
            get
            {
                EnsureInitialized();
                return isPermanentlyDiscovered
                       || temporaryRevealRemaining > 0f;
            }
        }

        public event Action<bool> DiscoveryChanged;

        private void Awake()
        {
            placedObject = GetComponent<HeightMapPlacedObject>();
            EnsureInitialized();
        }

        private void OnEnable()
        {
            EnsureInitialized();
            ActiveEntities.Add(this);
        }

        private void OnDisable()
        {
            ActiveEntities.Remove(this);
        }

        private void Update()
        {
            if (temporaryRevealRemaining <= 0f)
                return;

            bool wasDiscovered = IsDiscovered;
            temporaryRevealRemaining = Mathf.Max(
                0f,
                temporaryRevealRemaining - Time.deltaTime);
            if (wasDiscovered != IsDiscovered)
                DiscoveryChanged?.Invoke(IsDiscovered);
        }

        public void SetDiscovered(bool discovered = true)
        {
            EnsureInitialized();
            bool wasDiscovered = IsDiscovered;
            if (isPermanentlyDiscovered == discovered)
                return;

            isPermanentlyDiscovered = discovered;
            if (wasDiscovered != IsDiscovered)
                DiscoveryChanged?.Invoke(IsDiscovered);
        }

        /// <summary>
        /// Temporarily reveals an unknown entity without changing its permanent
        /// biological-discovery state. Repeated scans refresh the remaining time.
        /// </summary>
        public void RevealTemporarily(float duration)
        {
            EnsureInitialized();
            bool wasDiscovered = IsDiscovered;
            temporaryRevealRemaining = Mathf.Max(
                temporaryRevealRemaining,
                Mathf.Max(0f, duration));
            if (wasDiscovered != IsDiscovered)
                DiscoveryChanged?.Invoke(IsDiscovered);
        }

        public float GetBiologicalScanCollisionRadiusWorld()
        {
            float radiusMeters = Mathf.Max(
                0.01f,
                biologicalScanCollisionRadiusMeters);
            if (placedObject == null)
                placedObject = GetComponent<HeightMapPlacedObject>();

            MapTestSceneController map = placedObject != null
                ? placedObject.Map
                : null;
            return map != null && map.HasGeneratedMap
                ? map.MapMetersToWorldDistance(Vector2.right, radiusMeters)
                : radiusMeters;
        }

        [ContextMenu("Debug/Discover")]
        public void DebugDiscover()
        {
            SetDiscovered(true);
        }

        [ContextMenu("Debug/Reset To Unknown")]
        public void ResetToUnknown()
        {
            bool wasDiscovered = IsDiscovered;
            temporaryRevealRemaining = 0f;
            isPermanentlyDiscovered = false;
            if (wasDiscovered && !IsDiscovered)
                DiscoveryChanged?.Invoke(false);
        }

#if UNITY_EDITOR
        public void ConfigureEditorDefaults(
            DiscoverableKind kind,
            string id,
            bool discoveredByDefault)
        {
            discoverableKind = kind;
            discoveryId = string.IsNullOrWhiteSpace(id) ? "unknown" : id;
            startDiscovered = discoveredByDefault;
            initialized = false;
        }
#endif

        private void EnsureInitialized()
        {
            if (initialized)
                return;

            isPermanentlyDiscovered = startDiscovered;
            temporaryRevealRemaining = 0f;
            initialized = true;
        }

        private void OnValidate()
        {
            biologicalScanCollisionRadiusMeters = Mathf.Max(
                0.01f,
                biologicalScanCollisionRadiusMeters);
        }
    }
}
