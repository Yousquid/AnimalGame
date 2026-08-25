using System;
using System.Collections.Generic;
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

        private bool initialized;
        private bool isDiscovered;

        public static IReadOnlyCollection<DiscoverableEntity> Active =>
            ActiveEntities;
        public DiscoverableKind Kind => discoverableKind;
        public string DiscoveryId => discoveryId;
        public bool IsDiscovered
        {
            get
            {
                EnsureInitialized();
                return isDiscovered;
            }
        }

        public event Action<bool> DiscoveryChanged;

        private void Awake()
        {
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

        public void SetDiscovered(bool discovered = true)
        {
            EnsureInitialized();
            if (isDiscovered == discovered)
                return;

            isDiscovered = discovered;
            DiscoveryChanged?.Invoke(isDiscovered);
        }

        [ContextMenu("Debug/Discover")]
        public void DebugDiscover()
        {
            SetDiscovered(true);
        }

        [ContextMenu("Debug/Reset To Unknown")]
        public void ResetToUnknown()
        {
            SetDiscovered(false);
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

            isDiscovered = startDiscovered;
            initialized = true;
        }
    }
}
