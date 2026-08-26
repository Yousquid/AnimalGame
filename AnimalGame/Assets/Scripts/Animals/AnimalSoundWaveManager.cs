using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    [DefaultExecutionOrder(420)]
    [AddComponentMenu("")]
    public sealed class AnimalSoundWaveManager : MonoBehaviour
    {
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int ProgressProperty =
            Shader.PropertyToID("_Progress");
        private static readonly int OpacityProperty = Shader.PropertyToID("_Opacity");
        private static readonly int RingCountProperty =
            Shader.PropertyToID("_RingCount");
        private static readonly int InnerRadiusProperty =
            Shader.PropertyToID("_InnerRadiusRatio");
        private static readonly int LineWidthProperty =
            Shader.PropertyToID("_LineWidth");
        private static readonly int BreakupProperty = Shader.PropertyToID("_Breakup");
        private static readonly int IrregularityProperty =
            Shader.PropertyToID("_Irregularity");
        private static readonly int SeedProperty = Shader.PropertyToID("_Seed");

        private const int MaximumWaveCount = 256;
        private static AnimalSoundWaveManager instance;

        private readonly List<WaveInstance> waves = new List<WaveInstance>();
        private Material waveMaterial;
        private Mesh quadMesh;

        private sealed class WaveInstance
        {
            public GameObject GameObject;
            public Transform Transform;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Properties;
            public bool Active;
            public float Elapsed;
            public float Duration;
            public float Opacity;
            public float RingCount;
            public float InnerRadiusRatio;
            public float LineWidth;
            public float Breakup;
            public float Irregularity;
            public float Seed;
            public Color Color;
        }

        public static void Emit(
            Vector3 worldPosition,
            AnimalSoundWaveSettings settings,
            Shader shader,
            Color color,
            float breakup,
            float irregularity,
            int sortingOrder,
            float radiusMultiplier,
            float ringCountMultiplier)
        {
            if (settings == null || !settings.Enabled)
                return;

            AnimalSoundWaveManager manager = GetOrCreate(shader);
            if (manager == null || manager.waveMaterial == null)
                return;

            manager.EmitInternal(
                worldPosition,
                settings,
                color,
                breakup,
                irregularity,
                sortingOrder,
                radiusMultiplier,
                ringCountMultiplier);
        }

        private static AnimalSoundWaveManager GetOrCreate(Shader shader)
        {
            if (instance != null)
            {
                instance.CreateResources(shader);
                return instance;
            }

            instance = FindObjectOfType<AnimalSoundWaveManager>();
            if (instance != null)
            {
                instance.CreateResources(shader);
                return instance;
            }

            var managerObject = new GameObject("Animal Sound Wave Manager");
            instance = managerObject.AddComponent<AnimalSoundWaveManager>();
            instance.CreateResources(shader);
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
        }

        private void CreateResources(Shader configuredShader)
        {
            if (waveMaterial != null)
                return;

            Shader shader = configuredShader != null
                ? configuredShader
                : Shader.Find("AnimalGame/Animal Sound Wave");
            if (shader == null)
            {
                Debug.LogError(
                    "Animal sound waves could not find the AnimalGame/Animal Sound Wave shader.",
                    this);
                return;
            }

            waveMaterial = new Material(shader)
            {
                name = "Runtime Animal Sound Wave Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            quadMesh = CreateQuadMesh();
        }

        private void EmitInternal(
            Vector3 worldPosition,
            AnimalSoundWaveSettings settings,
            Color color,
            float breakup,
            float irregularity,
            int sortingOrder,
            float radiusMultiplier,
            float ringCountMultiplier)
        {
            WaveInstance wave = GetWaveInstance();
            float radius = settings.ChooseMaximumRadius()
                           * Mathf.Max(0.05f, radiusMultiplier);
            wave.Active = true;
            wave.Elapsed = 0f;
            wave.Duration = settings.DurationSeconds;
            wave.Opacity = settings.Opacity;
            wave.RingCount = Mathf.Max(
                1,
                Mathf.FloorToInt(
                    settings.RingCount
                    * Mathf.Max(0.1f, ringCountMultiplier)
                    + 0.5f));
            wave.InnerRadiusRatio = settings.InnerRadiusRatio;
            wave.LineWidth = settings.LineWidthNormalized;
            wave.Breakup = Mathf.Clamp01(breakup);
            wave.Irregularity = Mathf.Clamp01(irregularity);
            wave.Seed = Random.Range(0.01f, 1000f);
            wave.Color = color;
            wave.Transform.position = worldPosition;
            wave.Transform.rotation = Quaternion.identity;
            wave.Transform.localScale = new Vector3(
                radius * 2f,
                radius * 2f,
                1f);
            wave.Renderer.sortingOrder = sortingOrder;
            wave.GameObject.SetActive(true);
            UpdateWaveVisual(wave, 0f);
        }

        private WaveInstance GetWaveInstance()
        {
            for (int index = 0; index < waves.Count; index++)
            {
                if (!waves[index].Active)
                    return waves[index];
            }

            if (waves.Count < MaximumWaveCount)
                return CreateWaveInstance();

            WaveInstance oldest = waves[0];
            float oldestProgress = oldest.Elapsed
                                   / Mathf.Max(0.01f, oldest.Duration);
            for (int index = 1; index < waves.Count; index++)
            {
                WaveInstance candidate = waves[index];
                float progress = candidate.Elapsed
                                 / Mathf.Max(0.01f, candidate.Duration);
                if (progress > oldestProgress)
                {
                    oldest = candidate;
                    oldestProgress = progress;
                }
            }

            return oldest;
        }

        private WaveInstance CreateWaveInstance()
        {
            var waveObject = new GameObject($"Animal Sound Wave {waves.Count + 1}");
            waveObject.transform.SetParent(transform, false);
            var meshFilter = waveObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = quadMesh;
            var meshRenderer = waveObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = waveMaterial;
            meshRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage =
                UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage =
                UnityEngine.Rendering.ReflectionProbeUsage.Off;

            var wave = new WaveInstance
            {
                GameObject = waveObject,
                Transform = waveObject.transform,
                Renderer = meshRenderer,
                Properties = new MaterialPropertyBlock()
            };
            waves.Add(wave);
            waveObject.SetActive(false);
            return wave;
        }

        private void Update()
        {
            float deltaTime = Mathf.Max(0f, Time.deltaTime);
            for (int index = 0; index < waves.Count; index++)
            {
                WaveInstance wave = waves[index];
                if (!wave.Active)
                    continue;

                wave.Elapsed += deltaTime;
                float normalizedTime = Mathf.Clamp01(
                    wave.Elapsed / Mathf.Max(0.05f, wave.Duration));
                UpdateWaveVisual(wave, normalizedTime);
                if (normalizedTime >= 1f)
                {
                    wave.Active = false;
                    wave.GameObject.SetActive(false);
                }
            }
        }

        private static void UpdateWaveVisual(
            WaveInstance wave,
            float normalizedTime)
        {
            float remaining = 1f - normalizedTime;
            float expansionProgress = 1f - remaining * remaining;
            float fadeIn = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0f, 0.08f, normalizedTime));
            float fadeOut = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.52f, 1f, normalizedTime));
            float opacity = wave.Opacity * fadeIn * fadeOut;

            MaterialPropertyBlock properties = wave.Properties;
            properties.Clear();
            properties.SetColor(ColorProperty, wave.Color);
            properties.SetFloat(ProgressProperty, expansionProgress);
            properties.SetFloat(OpacityProperty, opacity);
            properties.SetFloat(RingCountProperty, wave.RingCount);
            properties.SetFloat(InnerRadiusProperty, wave.InnerRadiusRatio);
            properties.SetFloat(LineWidthProperty, wave.LineWidth);
            properties.SetFloat(BreakupProperty, wave.Breakup);
            properties.SetFloat(IrregularityProperty, wave.Irregularity);
            properties.SetFloat(SeedProperty, wave.Seed);
            wave.Renderer.SetPropertyBlock(properties);
        }

        private static Mesh CreateQuadMesh()
        {
            var mesh = new Mesh
            {
                name = "Runtime Animal Sound Wave Quad",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f),
                    new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f)
                },
                uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f)
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
            if (waveMaterial != null)
                Destroy(waveMaterial);
            if (quadMesh != null)
                Destroy(quadMesh);
        }
    }
}
