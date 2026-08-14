using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>
    /// Stores editor-authored map coordinates and terrain metadata for a prefab
    /// placed in a fixed height-map level. The Transform remains authoritative
    /// while editing; the stored map position makes later map-scale changes safe.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Level/Height Map Placed Object")]
    public sealed class HeightMapPlacedObject : MonoBehaviour
    {
        [SerializeField] private MapTestSceneController map;
        [SerializeField] private Vector2 mapPositionMeters;
        [SerializeField] private float sampledSurfaceHeightMeters;
        [SerializeField, Min(0f)] private float footprintRadiusMeters = 1f;
        [SerializeField] private bool clampInsideMap = true;

        private Vector3 lastWorldPosition;

        public MapTestSceneController Map => map;
        public Vector2 MapPositionMeters => mapPositionMeters;
        public float SampledSurfaceHeightMeters => sampledSurfaceHeightMeters;
        public float FootprintRadiusMeters => footprintRadiusMeters;

        private void OnEnable()
        {
            ResolveMap();
            CaptureCurrentTransform();
            lastWorldPosition = transform.position;
        }

        private void Update()
        {
            if (Application.isPlaying)
                return;

            ResolveMap();
            if ((transform.position - lastWorldPosition).sqrMagnitude
                <= 0.0000001f)
            {
                return;
            }

            CaptureCurrentTransform();
            lastWorldPosition = transform.position;
        }

        public bool CaptureCurrentTransform()
        {
            ResolveMap();
            if (map == null || !map.HasGeneratedMap)
                return false;

            Vector3 position = transform.position;
            if (clampInsideMap)
            {
                Bounds bounds = map.WorldBounds;
                position.x = Mathf.Clamp(position.x, bounds.min.x, bounds.max.x);
                position.y = Mathf.Clamp(position.y, bounds.min.y, bounds.max.y);
                if ((position - transform.position).sqrMagnitude > 0.0000001f)
                    transform.position = position;
            }

            if (!map.TrySampleWorldPosition(
                    position,
                    out mapPositionMeters,
                    out sampledSurfaceHeightMeters))
            {
                return false;
            }

            lastWorldPosition = transform.position;
            return true;
        }

        public bool SnapToStoredMapPosition()
        {
            ResolveMap();
            if (map == null || !map.HasGeneratedMap)
                return false;

            Vector3 current = transform.position;
            Vector3 snapped = map.MapPositionToWorld(mapPositionMeters);
            snapped.z = current.z;
            transform.position = snapped;
            lastWorldPosition = snapped;
            map.TrySampleMapPosition(
                mapPositionMeters,
                out sampledSurfaceHeightMeters);
            return true;
        }

        private void ResolveMap()
        {
            if (map != null)
                return;

            map = FindObjectOfType<MapTestSceneController>();
        }

        private void OnDrawGizmosSelected()
        {
            ResolveMap();
            if (map == null || !map.HasGeneratedMap)
                return;

            float worldRadius = map.MapMetersToWorldDistance(
                Vector2.right,
                footprintRadiusMeters);
            Gizmos.color = new Color(0.25f, 0.9f, 1f, 0.85f);
            Gizmos.DrawWireSphere(transform.position, worldRadius);
        }

        private void OnValidate()
        {
            footprintRadiusMeters = Mathf.Max(0f, footprintRadiusMeters);
            if (!Application.isPlaying)
                CaptureCurrentTransform();
        }
    }
}
