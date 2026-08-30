using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AnimalGame.MapTest
{
    public enum TreeHealthState
    {
        Healthy = 0,
        Dead = 1
    }

    /// <summary>
    /// Marks a placed tree as a habitat target and provides map-space perch
    /// positions around its solid trunk. Active scene trees register
    /// automatically for animal target selection.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Level/Tree Habitat")]
    public sealed class TreeHabitat : MonoBehaviour
    {
        private static readonly HashSet<TreeHabitat> activeTrees =
            new HashSet<TreeHabitat>();

        private static readonly float[] perchSearchAngleOffsets =
        {
            0f,
            30f,
            -30f,
            60f,
            -60f,
            90f,
            -90f,
            135f,
            -135f,
            180f
        };

        [SerializeField] private TreeHealthState health =
            TreeHealthState.Healthy;

        [Tooltip("Transform at the centre of the solid trunk. When unassigned, the habitat root is used.")]
        [SerializeField] private Transform trunkCenter;

        [Tooltip("Logical map-space distance from the trunk centre to a bird's base perch point. Animal body clearance is added by the caller.")]
        [SerializeField, Min(0f)] private float perchRadiusMeters = 0.6f;

        [System.NonSerialized] private HeightMapPlacedObject placedObject;

        public static IReadOnlyCollection<TreeHabitat> ActiveTrees =>
            activeTrees;

        public TreeHealthState Health => health;
        public TreeHealthState HealthState => health;
        public bool IsHealthy => health == TreeHealthState.Healthy;
        public bool IsDead => health == TreeHealthState.Dead;
        public float PerchRadiusMeters => perchRadiusMeters;
        public Transform TrunkCenter => trunkCenter != null
            ? trunkCenter
            : transform;
        public Vector3 TrunkWorldPosition => TrunkCenter.position;

        public HeightMapPlacedObject PlacedObject
        {
            get
            {
                if (placedObject == null)
                    placedObject = GetComponent<HeightMapPlacedObject>();
                return placedObject;
            }
        }

        public MapTestSceneController Map
        {
            get
            {
                HeightMapPlacedObject placement = PlacedObject;
                return placement != null ? placement.Map : null;
            }
        }

        public Vector2 MapPositionMeters
        {
            get
            {
                MapTestSceneController map = Map;
                if (TryGetMapPosition(map, out Vector2 mapPosition))
                    return mapPosition;

                HeightMapPlacedObject placement = PlacedObject;
                return placement != null
                    ? placement.MapPositionMeters
                    : Vector2.zero;
            }
        }

        private void OnEnable()
        {
            placedObject = GetComponent<HeightMapPlacedObject>();

            // Imported prefab assets have no valid scene and must not become
            // selectable targets at world origin.
            if (gameObject.scene.IsValid())
                activeTrees.Add(this);
        }

        private void OnDisable()
        {
            activeTrees.Remove(this);
        }

        private void OnDestroy()
        {
            activeTrees.Remove(this);
        }

        public bool TryGetMapPosition(
            MapTestSceneController map,
            out Vector2 mapPosition)
        {
            mapPosition = Vector2.zero;
            if (map == null)
                return false;

            HeightMapPlacedObject placement = PlacedObject;
            if (placement != null
                && placement.Map != null
                && placement.Map != map)
            {
                return false;
            }

            Vector3 trunkWorld = TrunkWorldPosition;
            if (map.TrySampleWorldPosition(
                    new Vector2(trunkWorld.x, trunkWorld.y),
                    out mapPosition,
                    out _))
            {
                return true;
            }

            if (placement == null || placement.Map != map)
                return false;

            Vector2 storedPosition = placement.MapPositionMeters;
            if (!map.TrySampleMapPosition(storedPosition, out _))
                return false;

            mapPosition = storedPosition;
            return true;
        }

        public Vector2 GetPerchMapPosition(
            MapTestSceneController map,
            Vector2 approachFromMapPosition,
            float additionalClearanceMeters = 0f)
        {
            if (TryGetPerchMapPosition(
                    map,
                    approachFromMapPosition,
                    additionalClearanceMeters,
                    out Vector2 perchMapPosition))
            {
                return perchMapPosition;
            }

            return TryGetMapPosition(map, out Vector2 trunkMapPosition)
                ? trunkMapPosition
                : Vector2.zero;
        }

        public bool TryGetPerchMapPosition(
            MapTestSceneController map,
            Vector2 approachFromMapPosition,
            float additionalClearanceMeters,
            out Vector2 perchMapPosition)
        {
            perchMapPosition = Vector2.zero;
            if (!TryGetMapPosition(map, out Vector2 trunkMapPosition))
                return false;

            Vector2 direction = approachFromMapPosition - trunkMapPosition;
            if (direction.sqrMagnitude <= 0.000001f)
            {
                direction = map.WorldDirectionToMapDirection(
                    TrunkCenter.up);
                if (direction.sqrMagnitude <= 0.000001f)
                    direction = Vector2.up;
            }
            else
            {
                direction.Normalize();
            }

            float distance = perchRadiusMeters
                             + Mathf.Max(0f, additionalClearanceMeters);
            if (distance <= 0.0001f)
            {
                perchMapPosition = trunkMapPosition;
                return map.TrySampleMapPosition(perchMapPosition, out _);
            }

            // Prefer the near side of the trunk. Trees at an irregular map
            // edge may not have playable ground there, so search around the
            // same radius before falling back to the trunk centre.
            for (int index = 0;
                 index < perchSearchAngleOffsets.Length;
                 index++)
            {
                Vector2 candidateDirection = Rotate(
                    direction,
                    perchSearchAngleOffsets[index]);
                Vector2 candidate = trunkMapPosition
                                    + candidateDirection * distance;
                if (!map.TrySampleMapPosition(candidate, out _))
                    continue;

                perchMapPosition = candidate;
                return true;
            }

            if (!map.TrySampleMapPosition(trunkMapPosition, out _))
                return false;

            perchMapPosition = trunkMapPosition;
            return true;
        }

        public Vector3 GetPerchWorldPosition(
            MapTestSceneController map,
            Vector3 approachFromWorldPosition,
            float additionalClearanceMeters = 0f)
        {
            return TryGetPerchWorldPosition(
                map,
                approachFromWorldPosition,
                additionalClearanceMeters,
                out Vector3 perchWorldPosition)
                ? perchWorldPosition
                : TrunkWorldPosition;
        }

        public bool TryGetPerchWorldPosition(
            MapTestSceneController map,
            Vector3 approachFromWorldPosition,
            float additionalClearanceMeters,
            out Vector3 perchWorldPosition)
        {
            perchWorldPosition = TrunkWorldPosition;
            if (!TryGetMapPosition(map, out Vector2 trunkMapPosition))
                return false;

            Vector2 approachMapPosition;
            if (!map.TrySampleWorldPosition(
                    new Vector2(
                        approachFromWorldPosition.x,
                        approachFromWorldPosition.y),
                    out approachMapPosition,
                    out _))
            {
                Vector2 worldDirection = new Vector2(
                    approachFromWorldPosition.x - TrunkWorldPosition.x,
                    approachFromWorldPosition.y - TrunkWorldPosition.y);
                Vector2 mapDirection = map.WorldDirectionToMapDirection(
                    worldDirection);
                approachMapPosition = trunkMapPosition
                                      + mapDirection
                                      * Mathf.Max(1f, perchRadiusMeters);
            }

            if (!TryGetPerchMapPosition(
                    map,
                    approachMapPosition,
                    additionalClearanceMeters,
                    out Vector2 perchMapPosition))
            {
                return false;
            }

            perchWorldPosition = map.MapPositionToWorld(perchMapPosition);
            perchWorldPosition.z = TrunkWorldPosition.z;
            return true;
        }

        private static Vector2 Rotate(Vector2 direction, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                direction.x * cosine - direction.y * sine,
                direction.x * sine + direction.y * cosine);
        }

        private void OnValidate()
        {
            perchRadiusMeters = Mathf.Max(0f, perchRadiusMeters);
            placedObject = GetComponent<HeightMapPlacedObject>();
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 centre = TrunkWorldPosition;
            MapTestSceneController map = Map;
            float radiusX = perchRadiusMeters;
            float radiusY = perchRadiusMeters;
            if (map != null && map.HasGeneratedMap)
            {
                radiusX = map.MapMetersToWorldDistance(
                    Vector2.right,
                    perchRadiusMeters);
                radiusY = map.MapMetersToWorldDistance(
                    Vector2.up,
                    perchRadiusMeters);
            }

            Color stateColor = IsDead
                ? new Color(0.72f, 0.48f, 0.24f, 0.95f)
                : new Color(0.25f, 0.9f, 0.4f, 0.95f);
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = Matrix4x4.TRS(
                centre,
                Quaternion.identity,
                new Vector3(radiusX, radiusY, 1f));
            Gizmos.color = stateColor;
            Gizmos.DrawWireSphere(Vector3.zero, 1f);
            Gizmos.DrawSphere(Vector3.zero, 0.06f);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;

#if UNITY_EDITOR
            Color previousHandlesColor = Handles.color;
            Handles.color = stateColor;
            Handles.Label(
                centre + Vector3.up * Mathf.Max(0.2f, radiusY),
                $"Tree Habitat: {health}");
            Handles.color = previousHandlesColor;
#endif
        }

#if UNITY_EDITOR
        public bool ConfigureEditorDefaults(
            TreeHealthState healthState,
            Transform treeTrunkCenter,
            float treePerchRadiusMeters)
        {
            float sanitizedRadius = Mathf.Max(0f, treePerchRadiusMeters);
            bool changed = health != healthState
                           || trunkCenter != treeTrunkCenter
                           || !Mathf.Approximately(
                               perchRadiusMeters,
                               sanitizedRadius);
            health = healthState;
            trunkCenter = treeTrunkCenter;
            perchRadiusMeters = sanitizedRadius;
            return changed;
        }
#endif
    }
}
