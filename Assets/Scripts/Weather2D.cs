using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

/// <summary>
/// GPU-driven 2D fluid core based on a staggered-grid style pipeline
/// (pressure solve -> velocity projection -> scalar advection).
/// </summary>
public class Weather2D : MonoBehaviour
{
    [Header("Resolution")]
    [Range(64, 512)]
    public int resolution = 256;
    [Range(1, 8)]
    public int substeps = 4;
    [Range(0.001f, 0.05f)]
    public float timeStep = 0.016f;
    [Range(5, 120)]
    public int jacobiIterations = 40;

    [Header("Dissipation")]
    [Range(0.9f, 1f)]
    public float velocityDissipation = 0.995f;
    [Range(0.9f, 1f)]
    public float densityDissipation = 0.999f;

    [Header("Background Source")]
    [Range(0.01f, 0.4f)]
    public float sourceRadius = 0.08f;
    [Range(0.0f, 0.2f)]
    public float sourceFeather = 0.03f;
    [Range(0f, 1f)]
    public float sourceHeight = 0.05f;
    public float sourceDensity = 4f;
    public Vector2 sourceVelocity = new Vector2(0f, 1.5f);

    [Header("Mouse Injection")]
    public bool enableMouseInput = true;
    public float mouseDensity = 4f;
    public Vector2 mouseVelocity = new Vector2(0f, 2.5f);
    [Range(0.005f, 0.2f)]
    public float mouseRadius = 0.04f;
    [Range(0.01f, 1f)]
    public float loopBurstDuration = 0.2f;

    [Header("Display")]
    public RawImage target;
    public FilterMode filter = FilterMode.Bilinear;
    public Color lowColor = new Color(0.04f, 0.05f, 0.1f, 1f);
    public Color highColor = new Color(1f, 0.95f, 0.85f, 1f);
    [Range(0.1f, 15f)]
    public float displayGain = 6f;
    [Range(0.2f, 2f)]
    public float displayGamma = 0.55f;
    public Color cloudColor = Color.white;
    [Range(0.1f, 20f)]
    public float cloudDisplayGain = 12f;
    [Range(0f, 1f)]
    public float cloudBlend = 0.85f;
    public Color skyTopColor = new Color(0.3f, 0.45f, 0.7f, 1f);
    public Color skyBottomColor = new Color(0.15f, 0.2f, 0.3f, 1f);
    public Color groundColor = new Color(0.08f, 0.06f, 0.04f, 1f);
    public Color cloudShadowColor = new Color(0.4f, 0.45f, 0.52f, 1f);
    [Range(0f, 2f)]
    public float cloudShadowStrength = 0.6f;

    [Header("Microphysics")]
    [Range(0.1f, 1.5f)]
    public float saturationThreshold = 0.55f;
    [Range(0f, 10f)]
    public float condensationRate = 4f;
    [Range(0f, 10f)]
    public float evaporationRate = 2f;
    [Range(0f, 5f)]
    public float precipitationRate = 0.5f;
    [Range(0f, 10f)]
    public float latentHeatBuoyancy = 1.5f;

    [Header("Precipitation Feedback")]
    public ParticleSystem precipitationSystem;
    [Range(0f, 5000f)]
    public float precipitationEmissionGain = 1500f;
    [Range(0f, 100f)]
    public float precipitationFeedback = 20f;
    [Range(0f, 0.01f)]
    public float precipitationThreshold = 0.0005f;

    [Header("Surface Forcing")]
    public bool enableSurfaceForcing = false;
    public Texture2D surfaceMoistureMap;

    [Header("Flow Limits")]
    [Range(0.5f, 0.99f)]
    public float upperDampingStart = 0.82f;
    [Range(0f, 1f)]
    public float upperDampingStrength = 0.45f;

    [Header("Sounding")]
    public bool applySoundingOnStart = false;
    public SoundingProfile initialSounding;

    [SerializeField] private ComputeShader fluidCompute;
    [SerializeField] private DemoScenario[] demoScenarios;

    private RenderTexture _velocityA;
    private RenderTexture _velocityB;
    private RenderTexture _humidityA;
    private RenderTexture _humidityB;
    private RenderTexture _cloudA;
    private RenderTexture _cloudB;
    private RenderTexture _pressureA;
    private RenderTexture _pressureB;
    private RenderTexture _divergence;
    private RenderTexture _display;
    private RenderTexture _precipitation;
    private ComputeBuffer _debugBuffer;
    private float[] _statsData;

    private int _kInject;
    private int _kAdvectVelocity;
    private int _kAdvectScalar;
    private int _kDivergence;
    private int _kJacobi;
    private int _kSubtract;
    private int _kClear;
    private int _kVisualize;
    private int _kStats;
    private int _kMicrophysics;
    private int _kUpperDamping;

    private Vector2Int _dispatch;
    private Material _quadMaterial;
    private int _activeDemo;
    private long _stepsCompleted;
    private float _debugTimer;
    private float _loopTimer;
    private DemoScenario _currentScenario;
    private bool _useBaseSource = true;
    private float _scenarioTimeScale = 1f;
    private bool _hasSurfaceMap = false;
    private bool _initialized = false;
    private float _rocketBoostTimer;
    private float _rocketCondensationMultiplier = 1f;
    private float _rocketPrecipitationMultiplier = 1f;
    private float _defaultPrecipitationFeedback;
    private float _latestAvgPrecip;
    private float _latestAvgCloud;
    private float _latestAvgHumidity;
    private float _latestAvgSpeed;
    private readonly List<ScheduledBurst> _scheduledRocketBursts = new List<ScheduledBurst>();
    private readonly List<ActiveBurst> _activeRocketBursts = new List<ActiveBurst>();

    public event Action<DemoScenario> DemoApplied;

    private struct ScheduledBurst
    {
        public Burst burst;
        public float delay;
        public float duration;
        public bool modulateSurface;
    }

    private struct ActiveBurst
    {
        public Burst burst;
        public float remainingDuration;
        public bool modulateSurface;
    }

    public DemoScenario CurrentScenario => _currentScenario;
    public float LatestAvgPrecip => _latestAvgPrecip;
    public float LatestAvgCloud => _latestAvgCloud;
    public float LatestAvgHumidity => _latestAvgHumidity;
    public float LatestAvgSpeed => _latestAvgSpeed;
    public int PendingRocketBurstCount => _scheduledRocketBursts.Count + _activeRocketBursts.Count;

    [System.Serializable]
    public struct Burst
    {
        public Vector2 position;
        public float radius;
        public float density;
        public Vector2 velocity;
    }

    [System.Serializable]
    public struct DemoScenario
    {
        public string name;
        public float densityDissipation;
        public float velocityDissipation;
        public float sourceRadius;
        public float sourceDensity;
        public float sourceHeight;
        public Vector2 sourceVelocity;
        public Burst[] initialBursts;
        public Burst[] loopBursts;
        public float loopInterval;
        public bool disableBaseSource;
        public float timeScale;
        public float rocketDelay;
        public Burst[] rocketBursts;
        public float rocketBurstDuration;
        public float rocketBurstInterval;
        public bool disableBaseSourceAfterRocket;
        public float rocketBoostDuration;
        public float rocketCondensationMultiplier;
        public float rocketPrecipitationMultiplier;
        public bool disablePrecipitationFeedback;
        public float precipitationFeedbackOverride;
    }

    private void Awake()
    {
        if (!Initialize())
        {
            enabled = false;
        }
    }

    private bool Initialize()
    {
        if (_initialized)
            return true;

        if (fluidCompute == null)
        {
            fluidCompute = Resources.Load<ComputeShader>("WeatherFluid");
        }

        if (fluidCompute == null)
        {
            Debug.LogError("Weather2D needs the WeatherFluid.compute shader in a Resources folder.", this);
            return false;
        }

        _kInject = fluidCompute.FindKernel("InjectSource");
        _kAdvectVelocity = fluidCompute.FindKernel("AdvectVelocity");
        _kAdvectScalar = fluidCompute.FindKernel("AdvectScalar");
        _kDivergence = fluidCompute.FindKernel("ComputeDivergence");
        _kJacobi = fluidCompute.FindKernel("JacobiPressure");
        _kSubtract = fluidCompute.FindKernel("SubtractGradient");
        _kClear = fluidCompute.FindKernel("ClearTexture");
        _kVisualize = fluidCompute.FindKernel("Visualize");
        _kStats = fluidCompute.FindKernel("ComputeStats");
        _kMicrophysics = fluidCompute.FindKernel("MoistureMicrophysics");
        _kUpperDamping = fluidCompute.FindKernel("ApplyUpperDamping");

        AllocateTextures();
        ConfigureTarget();
        EnsureDemoScenarios();
        _defaultPrecipitationFeedback = precipitationFeedback;
        _initialized = true;
        return true;
    }

    private void Start()
    {
        if (!Initialize())
            return;

        if (demoScenarios != null && demoScenarios.Length > 0)
        {
            ApplyDemo(_activeDemo);
        }

        _currentScenario = demoScenarios != null && demoScenarios.Length > 0 ? demoScenarios[Mathf.Clamp(_activeDemo, 0, demoScenarios.Length - 1)] : default;
        _loopTimer = Mathf.Max(0f, _currentScenario.loopInterval);

        if (applySoundingOnStart && initialSounding != null)
        {
            ApplySoundingProfile(initialSounding);
        }
    }

    private void OnDestroy()
    {
        Release(ref _velocityA);
        Release(ref _velocityB);
        Release(ref _humidityA);
        Release(ref _humidityB);
        Release(ref _cloudA);
        Release(ref _cloudB);
        Release(ref _pressureA);
        Release(ref _pressureB);
        Release(ref _divergence);
        Release(ref _display);
        Release(ref _precipitation);
        ReleaseDebugBuffer();
        _initialized = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ConfigureSurfaceMap();
    }
#endif

    private void AllocateTextures()
    {
        _dispatch = new Vector2Int(
            Mathf.CeilToInt(resolution / 8f),
            Mathf.CeilToInt(resolution / 8f));

        _velocityA = CreateVectorRT();
        _velocityB = CreateVectorRT();
        _humidityA = CreateScalarRT();
        _humidityB = CreateScalarRT();
        _cloudA = CreateScalarRT();
        _cloudB = CreateScalarRT();
        _pressureA = CreateScalarRT(RenderTextureFormat.RFloat);
        _pressureB = CreateScalarRT(RenderTextureFormat.RFloat);
        _divergence = CreateScalarRT(RenderTextureFormat.RFloat);
        _display = CreateDisplayRT();
        _precipitation = CreateScalarRT(RenderTextureFormat.RFloat);
        CreateDebugBuffer();
        _statsData = new float[12];

        fluidCompute.SetInts("_SimSize", resolution, resolution);
        fluidCompute.SetFloats("_InvSimSize", 1f / resolution, 1f / resolution);
        ConfigureSurfaceMap();
    }

    private RenderTexture CreateVectorRT()
    {
        return CreateRT(RenderTextureFormat.RGFloat);
    }

    private RenderTexture CreateScalarRT(RenderTextureFormat format = RenderTextureFormat.RFloat)
    {
        return CreateRT(format);
    }

    private RenderTexture CreateDisplayRT()
    {
        return CreateRT(RenderTextureFormat.ARGBHalf);
    }

    private RenderTexture CreateRT(RenderTextureFormat format)
    {
        var rt = new RenderTexture(resolution, resolution, 0, format)
        {
            enableRandomWrite = true,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = filter
        };
        rt.Create();
        return rt;
    }

    private void Release(ref RenderTexture rt)
    {
        if (rt != null)
        {
            rt.Release();
            Destroy(rt);
            rt = null;
        }
    }

    private void CreateDebugBuffer()
    {
        ReleaseDebugBuffer();
        _debugBuffer = new ComputeBuffer(12, sizeof(float));
    }

    private void ConfigureSurfaceMap()
    {
        _hasSurfaceMap = enableSurfaceForcing && surfaceMoistureMap != null;
    }

    private void ReleaseDebugBuffer()
    {
        if (_debugBuffer != null)
        {
            _debugBuffer.Release();
            _debugBuffer.Dispose();
            _debugBuffer = null;
        }
    }

    private void ConfigureTarget()
    {
        if (target == null)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.SetParent(transform, false);
            quad.transform.localScale = Vector3.one * 5f;
            var collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(collider);
                }
                else
                {
                    DestroyImmediate(collider);
                }
            }
            _quadMaterial = new Material(Shader.Find("Unlit/Texture"));
            quad.GetComponent<MeshRenderer>().sharedMaterial = _quadMaterial;
        }
        else
        {
            target.texture = _display;
        }
    }

    private void Update()
    {
        if (!_initialized && !Initialize())
            return;

        ConfigureSurfaceMap();
        float dt = Mathf.Min(Time.deltaTime, 0.033f) * Mathf.Max(0.1f, _scenarioTimeScale);
        int steps = Mathf.Max(1, substeps);
        float subDt = (dt / steps);

        for (int i = 0; i < steps; i++)
        {
            RunFluidStep(Mathf.Max(subDt, 0.0001f));
        }

        Visualize();
        HandleDebugLogging(Time.deltaTime);

        if (_quadMaterial != null)
        {
            _quadMaterial.mainTexture = _display;
        }
    }

    private void RunFluidStep(float dt)
    {
        fluidCompute.SetFloat("_DeltaTime", dt);
        fluidCompute.SetFloat("_VelocityDissipation", velocityDissipation);
        fluidCompute.SetFloat("_DensityDissipation", densityDissipation);

        ProcessRocketBursts(dt);

        // Base thermal source near the ground.
        if (_useBaseSource)
        {
        Vector2 baseCenter = new Vector2(0.5f, Mathf.Clamp01(sourceHeight));
        InjectImpulse(baseCenter, sourceRadius, sourceDensity, sourceVelocity, dt,
            modulateSurface: _hasSurfaceMap && _useBaseSource);
        }

        if (enableMouseInput && IsPointerPressed() && TryGetPointerUV(out Vector2 mouseUV))
        {
            InjectImpulse(mouseUV, mouseRadius, mouseDensity, mouseVelocity, dt);
        }

        if (_currentScenario.loopBursts != null && _currentScenario.loopBursts.Length > 0 && _currentScenario.loopInterval > 0f)
        {
            _loopTimer -= dt;
            if (_loopTimer <= 0f)
            {
                foreach (var burst in _currentScenario.loopBursts)
                {
                    InjectImpulse(burst.position, Mathf.Max(0.005f, burst.radius), Mathf.Max(0f, burst.density),
                        burst.velocity == Vector2.zero ? sourceVelocity : burst.velocity, loopBurstDuration);
                }
                _loopTimer = _currentScenario.loopInterval;
            }
        }

        AdvectVelocity();
        AdvectHumidity();
        AdvectCloud();
        ProjectVelocity();
        ApplyUpperDamping();
        RunMicrophysics(dt);
        GatherStatsAndUpdatePrecipitation(dt);
        _stepsCompleted++;
    }

    public void TriggerRocketBoost(float duration, float condensationMultiplier, float precipitationMultiplier)
    {
        _rocketBoostTimer = Mathf.Max(_rocketBoostTimer, Mathf.Max(0f, duration));
        _rocketCondensationMultiplier = Mathf.Max(1f, condensationMultiplier);
        _rocketPrecipitationMultiplier = Mathf.Max(1f, precipitationMultiplier);
    }

    public void SetBaseSourceActive(bool active)
    {
        _useBaseSource = active;
    }

    public void ClearScriptedBursts()
    {
        _scheduledRocketBursts.Clear();
        _activeRocketBursts.Clear();
    }

    public void TriggerRocketBurst(Burst burst, float duration)
    {
        TriggerRocketBurst(burst, 0f, duration);
    }

    public void TriggerRocketBurst(Burst burst, float delay, float duration)
    {
        ScheduleScriptedBurst(burst, delay, duration, false);
    }

    public void TriggerRocketSequence(Burst[] bursts, float initialDelay, float interval, float duration)
    {
        if (bursts == null || bursts.Length == 0)
            return;

        float delay = Mathf.Max(0f, initialDelay);
        float spacing = Mathf.Max(0.0001f, interval);
        float burstDuration = Mathf.Max(0.0001f, duration);
        for (int i = 0; i < bursts.Length; i++)
        {
            TriggerRocketBurst(bursts[i], delay, burstDuration);
            delay += spacing;
        }
    }

    public void StepSimulation(float dt, int iterations = 1)
    {
        if (!_initialized && !Initialize())
            return;

        int steps = Mathf.Max(1, iterations);
        float clampedDt = Mathf.Max(0.0001f, dt);
        for (int i = 0; i < steps; i++)
        {
            RunFluidStep(clampedDt);
        }
        Visualize();
    }

    private void ProcessRocketBursts(float dt)
    {
        if (_scheduledRocketBursts.Count == 0 && _activeRocketBursts.Count == 0)
            return;

        for (int i = _scheduledRocketBursts.Count - 1; i >= 0; i--)
        {
            var scheduled = _scheduledRocketBursts[i];
            scheduled.delay -= dt;
            if (scheduled.delay <= 0f)
            {
                _activeRocketBursts.Add(new ActiveBurst
                {
                    burst = scheduled.burst,
                    remainingDuration = scheduled.duration,
                    modulateSurface = scheduled.modulateSurface
                });
                _scheduledRocketBursts.RemoveAt(i);
            }
            else
            {
                _scheduledRocketBursts[i] = scheduled;
            }
        }

        for (int i = _activeRocketBursts.Count - 1; i >= 0; i--)
        {
            var active = _activeRocketBursts[i];
            Burst burst = active.burst;
            Vector2 velocity = burst.velocity == Vector2.zero ? sourceVelocity : burst.velocity;
            InjectImpulse(burst.position, Mathf.Max(0.005f, burst.radius <= 0f ? sourceRadius * 0.5f : burst.radius),
                Mathf.Max(0f, burst.density <= 0f ? sourceDensity : burst.density), velocity, dt,
                active.modulateSurface && _hasSurfaceMap);
            active.remainingDuration -= dt;
            if (active.remainingDuration <= 0f)
            {
                _activeRocketBursts.RemoveAt(i);
            }
            else
            {
                _activeRocketBursts[i] = active;
            }
        }
    }

    private void ScheduleScriptedBurst(Burst burst, float delay, float duration, bool modulateSurface)
    {
        Burst adjusted = burst;
        if (adjusted.radius <= 0f)
        {
            adjusted.radius = Mathf.Max(0.01f, sourceRadius * 0.6f);
        }
        if (adjusted.density <= 0f)
        {
            adjusted.density = Mathf.Max(0.1f, sourceDensity);
        }
        if (adjusted.velocity == Vector2.zero)
        {
            adjusted.velocity = sourceVelocity;
        }

        _scheduledRocketBursts.Add(new ScheduledBurst
        {
            burst = adjusted,
            delay = Mathf.Max(0f, delay),
            duration = Mathf.Max(0.0001f, duration),
            modulateSurface = modulateSurface
        });
    }

    private void InjectImpulse(Vector2 center, float radius, float density, Vector2 velocity, float dt, bool modulateSurface = false)
    {
        fluidCompute.SetVector("_SourceCenter", new Vector4(center.x, center.y, 0f, 0f));
        fluidCompute.SetFloat("_SourceRadius", Mathf.Max(0.001f, radius));
        fluidCompute.SetFloat("_SourceDensity", density * dt);
        fluidCompute.SetVector("_SourceVelocity", new Vector4(velocity.x * dt, velocity.y * dt, 0f, 0f));
        fluidCompute.SetFloat("_SourceFeather", Mathf.Max(0.0001f, sourceFeather));
        float blend = modulateSurface && _hasSurfaceMap ? 1f : 0f;
        fluidCompute.SetFloat("_SourceMapBlend", blend);
        Texture surfaceTex = surfaceMoistureMap != null ? surfaceMoistureMap : Texture2D.whiteTexture;
        fluidCompute.SetTexture(_kInject, "_SurfaceMoistureTex", surfaceTex);
        fluidCompute.SetBuffer(_kInject, "_DebugBuffer", _debugBuffer);

        fluidCompute.SetTexture(_kInject, "_Velocity", _velocityA);
        fluidCompute.SetTexture(_kInject, "_Humidity", _humidityA);
        Dispatch(_kInject);
    }

    private void AdvectVelocity()
    {
        fluidCompute.SetTexture(_kAdvectVelocity, "_VectorFieldRead", _velocityA);
        fluidCompute.SetTexture(_kAdvectVelocity, "_VectorFieldWrite", _velocityB);
        fluidCompute.SetBuffer(_kAdvectVelocity, "_DebugBuffer", _debugBuffer);
        Dispatch(_kAdvectVelocity);
        Swap(ref _velocityA, ref _velocityB);
    }

    private void AdvectHumidity()
    {
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarFieldRead", _humidityA);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarFieldWrite", _humidityB);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarVelocity", _velocityA);
        fluidCompute.SetBuffer(_kAdvectScalar, "_DebugBuffer", _debugBuffer);
        Dispatch(_kAdvectScalar);
        Swap(ref _humidityA, ref _humidityB);
    }

    private void AdvectCloud()
    {
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarFieldRead", _cloudA);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarFieldWrite", _cloudB);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarVelocity", _velocityA);
        fluidCompute.SetBuffer(_kAdvectScalar, "_DebugBuffer", _debugBuffer);
        Dispatch(_kAdvectScalar);
        Swap(ref _cloudA, ref _cloudB);
    }

    private void ProjectVelocity()
    {
        fluidCompute.SetTexture(_kDivergence, "_VelocityField", _velocityA);
        fluidCompute.SetTexture(_kDivergence, "_DivergenceWrite", _divergence);
        Dispatch(_kDivergence);

        ClearRT(_pressureA);
        ClearRT(_pressureB);

        RenderTexture read = _pressureA;
        RenderTexture write = _pressureB;
        for (int i = 0; i < jacobiIterations; i++)
        {
            fluidCompute.SetTexture(_kJacobi, "_PressureRead", read);
            fluidCompute.SetTexture(_kJacobi, "_PressureWrite", write);
            fluidCompute.SetTexture(_kJacobi, "_DivergenceTex", _divergence);
            Dispatch(_kJacobi);
            var temp = read;
            read = write;
            write = temp;
        }

        fluidCompute.SetTexture(_kSubtract, "_PressureTex", read);
        fluidCompute.SetTexture(_kSubtract, "_VelocityWriteGradient", _velocityA);
        Dispatch(_kSubtract);
    }

    private void RunMicrophysics(float dt)
    {
        float condensation = condensationRate;
        float evaporation = evaporationRate;
        float precipitation = precipitationRate;
        float latentHeat = latentHeatBuoyancy;

        if (_rocketBoostTimer > 0f)
        {
            _rocketBoostTimer = Mathf.Max(0f, _rocketBoostTimer - dt);
            condensation *= Mathf.Max(1f, _rocketCondensationMultiplier);
            precipitation *= Mathf.Max(1f, _rocketPrecipitationMultiplier);
            latentHeat *= Mathf.Max(1f, _rocketCondensationMultiplier);
        }
        else
        {
            _rocketCondensationMultiplier = 1f;
            _rocketPrecipitationMultiplier = 1f;
        }

        fluidCompute.SetFloat("_SaturationThreshold", saturationThreshold);
        fluidCompute.SetFloat("_CondensationRate", condensation);
        fluidCompute.SetFloat("_EvaporationRate", evaporation);
        fluidCompute.SetFloat("_PrecipitationRate", precipitation);
        fluidCompute.SetFloat("_LatentHeatBuoyancy", latentHeat);
        fluidCompute.SetTexture(_kMicrophysics, "_MicroHumidity", _humidityA);
        fluidCompute.SetTexture(_kMicrophysics, "_MicroCloud", _cloudA);
        fluidCompute.SetTexture(_kMicrophysics, "_MicroVelocity", _velocityA);
        fluidCompute.SetTexture(_kMicrophysics, "_PrecipitationTex", _precipitation);
        Dispatch(_kMicrophysics);
    }

    private void ApplyUpperDamping()
    {
        if (upperDampingStrength <= 0f || _kUpperDamping < 0)
            return;

        float start = Mathf.Clamp01(upperDampingStart);
        float strength = Mathf.Clamp01(upperDampingStrength);
        if (start >= 0.999f || strength <= 0f)
            return;

        fluidCompute.SetFloat("_UpperDampingStart", start);
        fluidCompute.SetFloat("_UpperDampingStrength", strength);
        fluidCompute.SetTexture(_kUpperDamping, "_VelocityDamp", _velocityA);
        Dispatch(_kUpperDamping);
    }

    private void GatherStatsAndUpdatePrecipitation(float dt)
    {
        if (_statsData == null || _statsData.Length < 12)
        {
            _statsData = new float[12];
        }

        fluidCompute.SetTexture(_kStats, "_StatsDensityTex", _humidityA);
        fluidCompute.SetTexture(_kStats, "_StatsVelocityTex", _velocityA);
        fluidCompute.SetTexture(_kStats, "_StatsCloudTex", _cloudA);
        fluidCompute.SetTexture(_kStats, "_StatsPrecipTex", _precipitation);
        fluidCompute.SetBuffer(_kStats, "_DebugBuffer", _debugBuffer);
        fluidCompute.Dispatch(_kStats, 1, 1, 1);

        _debugBuffer.GetData(_statsData);
        _latestAvgHumidity = _statsData[0];
        _latestAvgSpeed = _statsData[2];
        _latestAvgCloud = _statsData[4];
        _latestAvgPrecip = _statsData[8];
        float precipSum = _statsData[10];
        UpdatePrecipitationEffects(_latestAvgPrecip, precipSum, dt);
    }

    private void UpdatePrecipitationEffects(float avgPrecip, float precipSum, float dt)
    {
        if (precipitationSystem != null)
        {
            var emission = precipitationSystem.emission;
            emission.rateOverTime = avgPrecip * precipitationEmissionGain;
        }

        if (precipitationFeedback > 0f && avgPrecip > precipitationThreshold)
        {
            float density = precipitationFeedback * avgPrecip;
            Vector2 center = new Vector2(0.5f, Mathf.Clamp01(sourceHeight * 0.5f));
            InjectImpulse(center, Mathf.Max(0.05f, sourceRadius * 0.8f), density, Vector2.zero, dt,
                modulateSurface: _hasSurfaceMap);
        }
    }

    private void ClearRT(RenderTexture rt)
    {
        fluidCompute.SetTexture(_kClear, "_ClearScalar", rt);
        fluidCompute.SetFloat("_ClearValue", 0f);
        Dispatch(_kClear);
    }

    private void Visualize()
    {
        if (_display == null)
        {
            return;
        }

        fluidCompute.SetTexture(_kVisualize, "_DensityRead", _humidityA);
        fluidCompute.SetTexture(_kVisualize, "_CloudRead", _cloudA);
        fluidCompute.SetTexture(_kVisualize, "_DisplayTexture", _display);
        fluidCompute.SetVector("_ColorLow", lowColor);
        fluidCompute.SetVector("_ColorHigh", highColor);
        fluidCompute.SetVector("_CloudColor", cloudColor);
        fluidCompute.SetFloat("_DisplayGain", displayGain);
        fluidCompute.SetFloat("_DisplayGamma", Mathf.Max(0.1f, displayGamma));
        fluidCompute.SetFloat("_CloudGain", cloudDisplayGain);
        fluidCompute.SetFloat("_CloudBlend", cloudBlend);
        fluidCompute.SetVector("_SkyTopColor", skyTopColor);
        fluidCompute.SetVector("_SkyBottomColor", skyBottomColor);
        fluidCompute.SetVector("_GroundColor", groundColor);
        fluidCompute.SetVector("_CloudShadowColor", cloudShadowColor);
        fluidCompute.SetFloat("_CloudShadowStrength", Mathf.Max(0f, cloudShadowStrength));
        Dispatch(_kVisualize);
    }

    private void Dispatch(int kernel)
    {
        fluidCompute.Dispatch(kernel, _dispatch.x, _dispatch.y, 1);
    }

    private void Swap(ref RenderTexture a, ref RenderTexture b)
    {
        var tmp = a;
        a = b;
        b = tmp;
    }

    private bool TryGetPointerUV(out Vector2 uv)
    {
        uv = Vector2.zero;

        if (!TryGetPointerScreenPosition(out Vector2 screenPos))
        {
            return false;
        }

        if (target != null)
        {
            RectTransform rect = target.rectTransform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPos, null, out Vector2 local))
            {
                Rect r = rect.rect;
                float u = Mathf.InverseLerp(r.xMin, r.xMax, local.x);
                float v = Mathf.InverseLerp(r.yMin, r.yMax, local.y);
                if (u >= 0f && u <= 1f && v >= 0f && v <= 1f)
                {
                    uv = new Vector2(u, v);
                    return true;
                }
            }
            return false;
        }

        uv = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);
        return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
    }

    private bool TryGetPointerScreenPosition(out Vector2 position)
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        if (Mouse.current == null)
        {
            position = Vector2.zero;
            return false;
        }
        position = Mouse.current.position.ReadValue();
        return true;
#else
        position = Input.mousePosition;
        return true;
#endif
    }

    private bool IsPointerPressed()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }

    private void ClearRenderTexture(RenderTexture rt, Color color)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, color);
        RenderTexture.active = previous;
    }

    private void ClearDebugBuffer()
    {
        if (_debugBuffer == null)
            return;
        float[] zeros = {0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f};
        _debugBuffer.SetData(zeros);
    }

    public void ResetSimulation()
    {
        if (!_initialized && !Initialize())
            return;

        ClearRenderTexture(_velocityA, Color.clear);
        ClearRenderTexture(_velocityB, Color.clear);
        ClearRenderTexture(_humidityA, Color.clear);
        ClearRenderTexture(_humidityB, Color.clear);
        ClearRenderTexture(_cloudA, Color.clear);
        ClearRenderTexture(_cloudB, Color.clear);
        ClearRenderTexture(_pressureA, Color.clear);
        ClearRenderTexture(_pressureB, Color.clear);
        ClearRenderTexture(_divergence, Color.clear);
        ClearRenderTexture(_display, lowColor);
        ClearRenderTexture(_precipitation, Color.clear);
        ClearDebugBuffer();
        _stepsCompleted = 0;
        _debugTimer = 0f;
        _latestAvgPrecip = 0f;
        _latestAvgCloud = 0f;
        _latestAvgHumidity = 0f;
        _latestAvgSpeed = 0f;
        _rocketBoostTimer = 0f;
        _rocketCondensationMultiplier = 1f;
        _rocketPrecipitationMultiplier = 1f;
        ClearScriptedBursts();
    }

    private void EnsureDemoScenarios()
    {
        if (demoScenarios != null && demoScenarios.Length > 0)
            return;

        demoScenarios = new[]
        {
            new DemoScenario
            {
                name = "Perpetual Plume",
                densityDissipation = 0.9997f,
                velocityDissipation = 0.996f,
                sourceRadius = 0.2f,
                sourceDensity = 25f,
                sourceHeight = 0.08f,
                sourceVelocity = new Vector2(0.1f, 2.5f),
                initialBursts = new[]
                {
                    new Burst
                    {
                        position = new Vector2(0.5f, 0.1f),
                        radius = 0.22f,
                        density = 60f,
                        velocity = new Vector2(0f, 3f)
                    }
                },
                loopBursts = new[]
                {
                    new Burst
                    {
                        position = new Vector2(0.45f, 0.08f),
                        radius = 0.12f,
                        density = 20f,
                        velocity = new Vector2(-0.4f, 2.2f)
                    },
                    new Burst
                    {
                        position = new Vector2(0.55f, 0.08f),
                        radius = 0.12f,
                        density = 20f,
                        velocity = new Vector2(0.4f, 2.2f)
                    }
                },
                loopInterval = 0.75f,
                precipitationFeedbackOverride = -1f
            },
            new DemoScenario
            {
                name = "Shear Burst",
                densityDissipation = 0.997f,
                velocityDissipation = 0.992f,
                sourceRadius = 0.08f,
                sourceDensity = 4f,
                sourceHeight = 0.08f,
                sourceVelocity = new Vector2(0.8f, 1.6f),
                initialBursts = new[]
                {
                    new Burst
                    {
                        position = new Vector2(0.3f, 0.2f),
                        radius = 0.15f,
                        density = 24f,
                        velocity = new Vector2(1.2f, 2.0f)
                    },
                    new Burst
                    {
                        position = new Vector2(0.7f, 0.2f),
                        radius = 0.15f,
                        density = 24f,
                        velocity = new Vector2(-1.2f, 2.0f)
                    }
                },
                loopBursts = new[]
                {
                    new Burst
                    {
                        position = new Vector2(0.25f, 0.25f),
                        radius = 0.12f,
                        density = 22f,
                        velocity = new Vector2(1.4f, 1.6f)
                    },
                    new Burst
                    {
                        position = new Vector2(0.75f, 0.25f),
                        radius = 0.12f,
                        density = 22f,
                        velocity = new Vector2(-1.4f, 1.6f)
                    }
                },
                loopInterval = 1.25f,
                precipitationFeedbackOverride = -1f
            },
            new DemoScenario
            {
                name = "Cloud Merger",
                densityDissipation = 0.9992f,
                velocityDissipation = 0.9965f,
                sourceRadius = 0.18f,
                sourceDensity = 28f,
                sourceHeight = 0.12f,
                sourceVelocity = new Vector2(0f, 2.2f),
                timeScale = 0.6f,
                disableBaseSource = true,
                initialBursts = new[]
                {
                    new Burst
                    {
                        position = new Vector2(0.35f, 0.18f),
                        radius = 0.16f,
                        density = 35f,
                        velocity = new Vector2(0.8f, 2.8f)
                    },
                    new Burst
                    {
                        position = new Vector2(0.65f, 0.18f),
                        radius = 0.16f,
                        density = 35f,
                        velocity = new Vector2(-0.8f, 2.8f)
                    }
                },
                loopBursts = null,
                loopInterval = 0f,
                precipitationFeedbackOverride = -1f
            },
            new DemoScenario
            {
                name = "Rocket Rain",
                densityDissipation = 0.9998f,
                velocityDissipation = 0.997f,
                sourceRadius = 0.18f,
                sourceDensity = 24f,
                sourceHeight = 0.1f,
                sourceVelocity = new Vector2(0f, 2.2f),
                timeScale = 0.45f,
                disableBaseSource = true,
                initialBursts = new[]
                {
                    new Burst
                    {
                        position = new Vector2(0.5f, 0.08f),
                        radius = 0.28f,
                        density = 42f,
                        velocity = new Vector2(0f, 2.6f)
                    },
                    new Burst
                    {
                        position = new Vector2(0.36f, 0.16f),
                        radius = 0.16f,
                        density = 28f,
                        velocity = new Vector2(0.6f, 2.4f)
                    },
                    new Burst
                    {
                        position = new Vector2(0.64f, 0.16f),
                        radius = 0.16f,
                        density = 28f,
                        velocity = new Vector2(-0.6f, 2.4f)
                    },
                    new Burst
                    {
                        position = new Vector2(0.5f, 0.32f),
                        radius = 0.14f,
                        density = 24f,
                        velocity = new Vector2(0f, 2.2f)
                    }
                },
                loopBursts = null,
                loopInterval = 0f,
                rocketDelay = 3f,
                rocketBurstDuration = 0.32f,
                rocketBurstInterval = 0.16f,
                rocketBursts = new[]
                {
                    new Burst { position = new Vector2(0.5f, 0.08f), radius = 0.11f, density = 48f, velocity = new Vector2(0f, 3.2f) },
                    new Burst { position = new Vector2(0.5f, 0.22f), radius = 0.09f, density = 42f, velocity = new Vector2(0f, 3.4f) },
                    new Burst { position = new Vector2(0.5f, 0.36f), radius = 0.08f, density = 38f, velocity = new Vector2(0f, 3.5f) },
                    new Burst { position = new Vector2(0.5f, 0.5f), radius = 0.07f, density = 30f, velocity = new Vector2(0f, 3.6f) },
                    new Burst { position = new Vector2(0.5f, 0.66f), radius = 0.07f, density = 26f, velocity = new Vector2(0f, 3.7f) }
                },
                disableBaseSourceAfterRocket = true,
                rocketBoostDuration = 2.5f,
                rocketCondensationMultiplier = 2.2f,
                rocketPrecipitationMultiplier = 3f,
                disablePrecipitationFeedback = true,
                precipitationFeedbackOverride = 0f
            }
        };
    }

    public void ApplyDemo(int index)
    {
        if (demoScenarios == null || demoScenarios.Length == 0)
            return;

        if (!_initialized && !Initialize())
            return;

        int clamped = Mathf.Clamp(index, 0, demoScenarios.Length - 1);
        _activeDemo = clamped;
        DemoScenario scenario = demoScenarios[clamped];

        densityDissipation = scenario.densityDissipation;
        velocityDissipation = scenario.velocityDissipation;
        sourceRadius = scenario.sourceRadius;
        sourceDensity = scenario.sourceDensity;
        sourceHeight = scenario.sourceHeight;
        sourceVelocity = scenario.sourceVelocity;
        _currentScenario = scenario;
        _loopTimer = Mathf.Max(0f, scenario.loopInterval);
        _useBaseSource = !scenario.disableBaseSource;
        _scenarioTimeScale = scenario.timeScale > 0f ? scenario.timeScale : 1f;
        ApplyPrecipitationFeedbackOverride(scenario);

        ClearScriptedBursts();
        ResetSimulation();
        if (scenario.initialBursts != null)
        {
            foreach (var burst in scenario.initialBursts)
            {
                float radius = Mathf.Max(0.005f, burst.radius);
                float density = Mathf.Max(0f, burst.density);
                Vector2 velocity = burst.velocity == Vector2.zero ? sourceVelocity : burst.velocity;
                InjectImpulse(burst.position, radius, density, velocity, 1f);
            }
        }

        Visualize();
        DemoApplied?.Invoke(_currentScenario);
    }

    private void ApplyPrecipitationFeedbackOverride(DemoScenario scenario)
    {
        if (!_initialized)
            return;

        if (scenario.disablePrecipitationFeedback)
        {
            precipitationFeedback = 0f;
            return;
        }

        if (scenario.precipitationFeedbackOverride >= 0f)
        {
            precipitationFeedback = scenario.precipitationFeedbackOverride;
        }
        else
        {
            precipitationFeedback = _defaultPrecipitationFeedback;
        }
    }

    private void OnGUI()
    {
        if (demoScenarios == null || demoScenarios.Length == 0)
            return;

        const float width = 220f;
        float height = 40f + demoScenarios.Length * 30f;
        GUILayout.BeginArea(new Rect(16f, 16f, width, height), "Demo Scenarios", GUI.skin.window);
        for (int i = 0; i < demoScenarios.Length; i++)
        {
            bool isActive = i == _activeDemo;
            GUI.enabled = !isActive;
            if (GUILayout.Button(demoScenarios[i].name))
            {
                ApplyDemo(i);
            }
            GUI.enabled = true;
        }
        GUILayout.EndArea();
    }

    [ContextMenu("Apply Initial Sounding")]
    public void ApplyInitialSoundingFromContext()
    {
        ApplySoundingProfile(initialSounding);
    }

    public void ApplySoundingProfile(SoundingProfile profile)
    {
        if (profile == null)
            return;

        saturationThreshold = profile.saturationThreshold;
        condensationRate = profile.condensationRate;
        evaporationRate = profile.evaporationRate;
        precipitationRate = profile.precipitationRate;
        latentHeatBuoyancy = profile.latentHeatBuoyancy;
        sourceDensity = profile.baseSourceDensity;
        sourceRadius = profile.baseSourceRadius;
        sourceHeight = profile.baseSourceHeight;
        sourceVelocity = profile.windShear;
        densityDissipation = profile.densityDissipation;
        velocityDissipation = profile.velocityDissipation;
        if (profile.surfaceMoisture != null)
        {
            surfaceMoistureMap = profile.surfaceMoisture;
            enableSurfaceForcing = true;
            ConfigureSurfaceMap();
        }
        if (profile.timeScale > 0f)
        {
            _scenarioTimeScale = profile.timeScale;
        }
    }

    [Header("Debug Logging")]
    public bool enableDebugLogging = false;
    [Range(0.2f, 5f)]
    public float debugLogInterval = 1f;
    public LogType debugLogType = LogType.Log;

    private void HandleDebugLogging(float deltaTime)
    {
        if (!enableDebugLogging || _statsData == null)
            return;

        _debugTimer += deltaTime;
        if (_debugTimer < Mathf.Max(0.2f, debugLogInterval))
            return;

        _debugTimer = 0f;
        Debug.unityLogger.Log(debugLogType,
            $"Weather2D Stats | steps={_stepsCompleted} | avgHumidity={_latestAvgHumidity:F3} avgCloud={_latestAvgCloud:F3} avgSpeed={_latestAvgSpeed:F3} avgPrecip={_latestAvgPrecip:F4}");
    }
}
