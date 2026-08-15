using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>
    /// Circular solid footprint for a placed map prop. Active scene instances
    /// register automatically so traversal and tumble sweeps can block against
    /// the core without treating the visual canopy as solid.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Level/Height Map Obstacle Footprint")]
    public sealed class HeightMapObstacleFootprint : MonoBehaviour
    {
        private static readonly HashSet<HeightMapObstacleFootprint>
            activeFootprints = new HashSet<HeightMapObstacleFootprint>();

        [Tooltip("When enabled, normal traversal and tumble sweeps treat this footprint as a hard obstacle.")]
        [SerializeField] private bool blocksTraversal = true;

        [Tooltip("Circular solid-core radius in logical map meters, excluding leaves and canopy artwork.")]
        [SerializeField, Min(0f)] private float radiusMeters = 0.3f;
        [SerializeField] private Color gizmoColor =
            new Color(1f, 0.48f, 0.16f, 0.9f);

        public bool BlocksTraversal => blocksTraversal;
        public float RadiusMeters => radiusMeters;
        internal static IEnumerable<HeightMapObstacleFootprint>
            ActiveFootprints => activeFootprints;

        private void OnEnable()
        {
            // Prefab assets do not belong to a valid scene and must never act
            // like an obstacle at world origin while they are being imported.
            if (gameObject.scene.IsValid())
                activeFootprints.Add(this);
        }

        private void OnDisable()
        {
            activeFootprints.Remove(this);
        }

        private void OnDestroy()
        {
            activeFootprints.Remove(this);
        }

        private void OnValidate()
        {
            radiusMeters = Mathf.Max(0f, radiusMeters);
        }

        private void OnDrawGizmosSelected()
        {
            if (!blocksTraversal || radiusMeters <= 0f)
                return;

            HeightMapPlacedObject placedObject =
                GetComponentInParent<HeightMapPlacedObject>();
            MapTestSceneController map = placedObject != null
                ? placedObject.Map
                : null;
            if (map == null)
                map = FindObjectOfType<MapTestSceneController>();
            if (map == null || !map.HasGeneratedMap)
                return;

            float worldRadiusX = map.MapMetersToWorldDistance(
                Vector2.right,
                radiusMeters);
            float worldRadiusY = map.MapMetersToWorldDistance(
                Vector2.up,
                radiusMeters);

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = Matrix4x4.TRS(
                transform.position,
                Quaternion.identity,
                new Vector3(worldRadiusX, worldRadiusY, 1f));
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(Vector3.zero, 1f);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
