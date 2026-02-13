using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

/// <summary>
/// GPU-driven 2D weather sandbox that advects velocity, humidity, temperature,
/// turbulence, and cloud water fields through a compute-shader pipeline.
/// </summary>
public class Weather2D : MonoBehaviour
{
    [Header("Simulation Size")]
    [FormerlySerializedAs("resolution")]
    [Range(64, 1024)]
    public int simWidth = 256;
    [Range(64, 1024)]
    public int simHeight = 256;
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

    [Header("Background Wind")]
    public Vector2 backgroundWind = Vector2.zero;
    [Range(0f, 10f)]
    public float backgroundWindStrength = 0f;

    [Header("Synoptic Wind")]
    [Range(0f, 3f)]
    public float pressureForceStrength = 0f;
    [Range(-3f, 3f)]
    public float coriolisStrength = 0f;
    [Range(0.9f, 1f)]
    public float synopticPressureDissipation = 0.999f;

    [Header("Synoptic Debug")]
    public bool showSynopticPressureGizmos = false;
    [Range(4, 64)]
    public int synopticGizmoGrid = 16;
    [Range(0.01f, 5f)]
    public float synopticGizmoScale = 0.5f;
    [Range(0.05f, 1f)]
    public float synopticGizmoUpdateInterval = 0.2f;
    public Color synopticGizmoColor = new Color(0.2f, 0.9f, 1f, 0.8f);
    public bool verboseSynopticGizmoReadback = false;

    [Header("Humidity Debug")]
    public bool showHumidityGizmos = false;
    [Range(4, 64)]
    public int humidityGizmoGrid = 16;
    [Range(0.05f, 1f)]
    public float humidityGizmoUpdateInterval = 0.2f;
    [Range(0.01f, 2f)]
    public float humidityGizmoScale = 0.25f;
    public Color humidityLowColor = new Color(0.05f, 0.2f, 0.6f, 0.5f);
    public Color humidityHighColor = new Color(0.9f, 0.9f, 1f, 0.8f);

    [Header("Time Controls")]
    [Range(1f, 20f)]
    public float fastForwardScale = 1f;
    [Range(0f, 10f)]
    public float fastForwardDuration = 0f;

    [Header("Time Stepping")]
    [Range(0.001f, 0.05f)]
    public float fixedSimDt = 0.01f;
    [Range(10, 5000)]
    public int maxStepsPerTick = 600;
    [Range(0.01f, 0.2f)]
    public float maxRealDt = 0.033f;

    [Header("Thermo Profile")]
    public float baseTemperature = 0.4f;
    public float lapseRate = 0.8f;
    [Range(0f, 2f)]
    public float surfaceHumidity = 0.9f;
    [Range(0f, 6f)]
    public float humidityDecay = 2.5f;

    [Header("Surface Moisture Reservoir")]
    public bool enableSurfaceHumidityRelax = false;
    [Range(0f, 30f)]
    public float surfaceHumidityRelaxStrength = 0f;
    [Range(0f, 0.5f)]
    public float surfaceHumidityRelaxHeight = 0.12f;

    [Header("Thermal Buoyancy")]
    [Range(-20f, 20f)]
    public float thermalBuoyancyStrength = 0f;

    [Header("Convergence Forcing")]
    public bool enableConvergence = false;
    [Range(0f, 6f)]
    public float convergenceStrength = 1.5f;
    [Range(0.01f, 0.5f)]
    public float convergenceWidth = 0.18f;
    [Range(0.05f, 1f)]
    public float convergenceHeight = 0.35f;
    [Range(0f, 3f)]
    public float convergenceWindSpeed = 1.2f;
    [Range(0f, 3f)]
    public float convergenceUpdraft = 0f;

    [Header("Display")]
    public RawImage target;
    public FilterMode filter = FilterMode.Bilinear;
    [Range(0.5f, 20f)]
    public float quadSize = 5f;
    public Color lowColor = new Color(0.04f, 0.05f, 0.1f, 1f);
    public Color highColor = new Color(1f, 0.95f, 0.85f, 1f);
    public bool showHumidityInDisplay = false;
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

    [Header("Thermodynamics")]
    [Range(0.9f, 1f)]
    public float temperatureDissipation = 0.9985f;
    [Range(0.9f, 1f)]
    public float turbulenceDissipation = 0.995f;
    [Range(0f, 1f)]
    public float temperatureSaturationFactor = 0.08f;
    [Range(0f, 10f)]
    public float latentHeatTemperatureGain = 1.2f;
    [Range(0f, 10f)]
    public float evaporationCoolingFactor = 0.8f;
    [Range(0f, 10f)]
    public float turbulencePrecipitationFactor = 2.0f;
    [Range(0f, 10f)]
    public float temperatureDecay = 0.6f;
    [Range(0f, 10f)]
    public float turbulenceDecay = 1.5f;

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
    private RenderTexture _temperatureA;
    private RenderTexture _temperatureB;
    private RenderTexture _turbulenceA;
    private RenderTexture _turbulenceB;
    private RenderTexture _pressureA;
    private RenderTexture _pressureB;
    private RenderTexture _divergence;
    private RenderTexture _synopticPressureA;
    private RenderTexture _synopticPressureB;
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
    private int _kBackgroundWind;
    private int _kInjectSynopticPressure;
    private int _kPressureForcing;
    private int _kCoriolis;
    private int _kSeedThermoProfile;
    private int _kApplyConvergence;
    private int _kRelaxHumidityToProfile;
    private int _kApplyThermalBuoyancy;

    private Vector2Int _dispatch;
    private Material _quadMaterial;
    private Transform _quadTransform;
    private float _defaultQuadSize;
    private int _defaultSimWidth;
    private int _defaultSimHeight;
    private float _defaultPressureForceStrength;
    private float _defaultCoriolisStrength;
    private float _defaultSynopticPressureDissipation;
    private float _defaultBaseTemperature;
    private float _defaultLapseRate;
    private float _defaultSurfaceHumidity;
    private float _defaultHumidityDecay;
    private bool _defaultEnableSurfaceHumidityRelax;
    private float _defaultSurfaceHumidityRelaxStrength;
    private float _defaultSurfaceHumidityRelaxHeight;
    private float _defaultThermalBuoyancyStrength;
    private bool _defaultEnableConvergence;
    private float _defaultConvergenceStrength;
    private float _defaultConvergenceWidth;
    private float _defaultConvergenceHeight;
    private float _defaultConvergenceWindSpeed;
    private float _defaultConvergenceUpdraft;
    private float _defaultSaturationThreshold;
    private float _defaultCondensationRate;
    private float _defaultEvaporationRate;
    private float _defaultPrecipitationRate;
    private float _defaultLatentHeatBuoyancy;
    private float _defaultFastForwardScale;
    private float _defaultFastForwardDuration;
    private float[] _synopticPressureData;
    private int _synopticPressureWidth;
    private int _synopticPressureHeight;
    private float _nextSynopticReadbackTime;
    private bool _synopticReadbackPending;
    private bool _synopticReadbackUnsupportedLogged;
    private float[] _humidityData;
    private int _humidityWidth;
    private int _humidityHeight;
    private float _nextHumidityReadbackTime;
    private bool _humidityReadbackPending;
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
    private float _fastForwardTimer;
    private float _simAccumulator;
    private float _effectiveTimeScale = 1f;
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
    public float EffectiveTimeScale => _effectiveTimeScale;

    [System.Serializable]
    public struct Burst
    {
        public Vector2 position;
        public float radius;
        public float density;
        public Vector2 velocity;
        public float heat;
        public float turbulence;
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
        public bool triggerRocketExplosion;
        public Burst rocketExplosion;
        public float rocketExplosionDuration;
        public float quadSize;
        public int simWidth;
        public int simHeight;
        public Vector2[] stormCenters;
        public float stormRadius;
        public float stormStrength;
        public float pressureForceStrength;
        public float coriolisStrength;
        public float synopticPressureDissipation;
        public bool overrideSynopticSettings;
        public bool useThermoProfile;
        public float baseTemperature;
        public float lapseRate;
        public float surfaceHumidity;
        public float humidityDecay;
        public bool enableSurfaceHumidityRelax;
        public float surfaceHumidityRelaxStrength;
        public float surfaceHumidityRelaxHeight;
        public float thermalBuoyancyStrength;
        public bool overrideConvergence;
        public bool enableConvergence;
        public float convergenceStrength;
        public float convergenceWidth;
        public float convergenceHeight;
        public float convergenceWindSpeed;
        public float convergenceUpdraft;
        public bool overrideMicrophysics;
        public float saturationThreshold;
        public float condensationRate;
        public float evaporationRate;
        public float precipitationRate;
        public float latentHeatBuoyancy;
        public bool overrideFastForward;
        public float fastForwardScale;
        public float fastForwardDuration;
    }

    /// <summary>
    /// Cache kernels, allocate buffers, and populate baseline demo data.
    /// </summary>
    private void Awake()
    {
        if (!Initialize())
        {
            enabled = false;
        }
    }

    /// <summary>
    /// Lazy initialization path so tests can spin up Weather2D headlessly.
    /// </summary>
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
        _kBackgroundWind = fluidCompute.FindKernel("ApplyBackgroundWind");
        _kInjectSynopticPressure = fluidCompute.FindKernel("InjectSynopticPressure");
        _kPressureForcing = fluidCompute.FindKernel("ApplyPressureForcing");
        _kCoriolis = fluidCompute.FindKernel("ApplyCoriolis");
        _kSeedThermoProfile = fluidCompute.FindKernel("SeedThermoProfile");
        _kApplyConvergence = fluidCompute.FindKernel("ApplyConvergence");
        _kRelaxHumidityToProfile = fluidCompute.FindKernel("RelaxHumidityToProfile");
        _kApplyThermalBuoyancy = fluidCompute.FindKernel("ApplyThermalBuoyancy");

        _defaultQuadSize = quadSize;
        _defaultSimWidth = simWidth;
        _defaultSimHeight = simHeight;
        _defaultPressureForceStrength = pressureForceStrength;
        _defaultCoriolisStrength = coriolisStrength;
        _defaultSynopticPressureDissipation = synopticPressureDissipation;
        _defaultBaseTemperature = baseTemperature;
        _defaultLapseRate = lapseRate;
        _defaultSurfaceHumidity = surfaceHumidity;
        _defaultHumidityDecay = humidityDecay;
        _defaultEnableSurfaceHumidityRelax = enableSurfaceHumidityRelax;
        _defaultSurfaceHumidityRelaxStrength = surfaceHumidityRelaxStrength;
        _defaultSurfaceHumidityRelaxHeight = surfaceHumidityRelaxHeight;
        _defaultThermalBuoyancyStrength = thermalBuoyancyStrength;
        _defaultEnableConvergence = enableConvergence;
        _defaultConvergenceStrength = convergenceStrength;
        _defaultConvergenceWidth = convergenceWidth;
        _defaultConvergenceHeight = convergenceHeight;
        _defaultConvergenceWindSpeed = convergenceWindSpeed;
        _defaultConvergenceUpdraft = convergenceUpdraft;
        _defaultSaturationThreshold = saturationThreshold;
        _defaultCondensationRate = condensationRate;
        _defaultEvaporationRate = evaporationRate;
        _defaultPrecipitationRate = precipitationRate;
        _defaultLatentHeatBuoyancy = latentHeatBuoyancy;
        _defaultFastForwardScale = fastForwardScale;
        _defaultFastForwardDuration = fastForwardDuration;
        AllocateTextures();
        ConfigureTarget();
        EnsureDemoScenarios();
        _defaultPrecipitationFeedback = precipitationFeedback;
        _initialized = true;
        return true;
    }

    /// <summary>
    /// Apply initial demo and optional sounding when the scene boots.
    /// </summary>
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
        ReleaseTextures();
        _initialized = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ConfigureSurfaceMap();
    }
#endif

    /// <summary>
    /// Allocate all simulation render targets and the stats buffer.
    /// </summary>
    private void AllocateTextures()
    {
        _dispatch = new Vector2Int(
            Mathf.CeilToInt(simWidth / 8f),
            Mathf.CeilToInt(simHeight / 8f));

        _velocityA = CreateVectorRT();
        _velocityB = CreateVectorRT();
        _humidityA = CreateScalarRT();
        _humidityB = CreateScalarRT();
        _cloudA = CreateScalarRT();
        _cloudB = CreateScalarRT();
        _temperatureA = CreateScalarRT();
        _temperatureB = CreateScalarRT();
        _turbulenceA = CreateScalarRT();
        _turbulenceB = CreateScalarRT();
        _pressureA = CreateScalarRT(RenderTextureFormat.RFloat);
        _pressureB = CreateScalarRT(RenderTextureFormat.RFloat);
        _divergence = CreateScalarRT(RenderTextureFormat.RFloat);
        _synopticPressureA = CreateScalarRT(RenderTextureFormat.RFloat);
        _synopticPressureB = CreateScalarRT(RenderTextureFormat.RFloat);
        _display = CreateDisplayRT();
        _precipitation = CreateScalarRT(RenderTextureFormat.RFloat);
        CreateDebugBuffer();
        _statsData = new float[12];

        fluidCompute.SetInts("_SimSize", simWidth, simHeight);
        fluidCompute.SetFloats("_InvSimSize", 1f / simWidth, 1f / simHeight);
        ConfigureSurfaceMap();
    }

    private void ReleaseTextures()
    {
        Release(ref _velocityA);
        Release(ref _velocityB);
        Release(ref _humidityA);
        Release(ref _humidityB);
        Release(ref _cloudA);
        Release(ref _cloudB);
        Release(ref _temperatureA);
        Release(ref _temperatureB);
        Release(ref _turbulenceA);
        Release(ref _turbulenceB);
        Release(ref _pressureA);
        Release(ref _pressureB);
        Release(ref _divergence);
        Release(ref _synopticPressureA);
        Release(ref _synopticPressureB);
        Release(ref _display);
        Release(ref _precipitation);
        ReleaseDebugBuffer();
    }

    private void RebuildSimulation()
    {
        ReleaseTextures();
        AllocateTextures();
        if (target != null)
        {
            target.texture = _display;
        }
    }

    private void MaybeRequestSynopticReadback()
    {
        if (!showSynopticPressureGizmos || _synopticPressureA == null || _synopticReadbackPending)
        {
            return;
        }

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            if (!_synopticReadbackUnsupportedLogged)
            {
                Debug.LogWarning("Synoptic gizmos require AsyncGPUReadback support.", this);
                _synopticReadbackUnsupportedLogged = true;
            }
            return;
        }

        float interval = Mathf.Max(0.01f, synopticGizmoUpdateInterval);
        if (Time.time < _nextSynopticReadbackTime)
        {
            return;
        }

        _synopticReadbackPending = true;
        _nextSynopticReadbackTime = Time.time + interval;
        int width = _synopticPressureA.width;
        int height = _synopticPressureA.height;
        AsyncGPUReadback.Request(_synopticPressureA, 0, request =>
        {
            _synopticReadbackPending = false;
            if (request.hasError)
            {
                if (verboseSynopticGizmoReadback)
                {
                    Debug.LogWarning("Synoptic gizmo readback failed.", this);
                }
                return;
            }
            var data = request.GetData<float>();
            int count = data.Length;
            if (_synopticPressureData == null || _synopticPressureData.Length != count)
            {
                _synopticPressureData = new float[count];
            }
            _synopticPressureWidth = width;
            _synopticPressureHeight = height;
            data.CopyTo(_synopticPressureData);
            if (verboseSynopticGizmoReadback)
            {
                float min = _synopticPressureData[0];
                float max = _synopticPressureData[0];
                for (int i = 1; i < _synopticPressureData.Length; i++)
                {
                    float value = _synopticPressureData[i];
                    if (value < min) min = value;
                    if (value > max) max = value;
                }
                Debug.Log($"Synoptic gizmo readback ok ({width}x{height}). Range: {min:F4} to {max:F4}", this);
            }
        });
    }

    private void MaybeRequestHumidityReadback()
    {
        if (!showHumidityGizmos || _humidityA == null || _humidityReadbackPending)
        {
            return;
        }

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            if (!_synopticReadbackUnsupportedLogged)
            {
                Debug.LogWarning("Humidity gizmos require AsyncGPUReadback support.", this);
                _synopticReadbackUnsupportedLogged = true;
            }
            return;
        }

        float interval = Mathf.Max(0.01f, humidityGizmoUpdateInterval);
        if (Time.time < _nextHumidityReadbackTime)
        {
            return;
        }

        _humidityReadbackPending = true;
        _nextHumidityReadbackTime = Time.time + interval;
        int width = _humidityA.width;
        int height = _humidityA.height;
        AsyncGPUReadback.Request(_humidityA, 0, request =>
        {
            _humidityReadbackPending = false;
            if (request.hasError)
            {
                return;
            }
            var data = request.GetData<float>();
            int count = data.Length;
            if (_humidityData == null || _humidityData.Length != count)
            {
                _humidityData = new float[count];
            }
            _humidityWidth = width;
            _humidityHeight = height;
            data.CopyTo(_humidityData);
        });
    }

    private Vector3 GetSynopticGizmoWorldPosition(float u, float v)
    {
        if (target != null)
        {
            RectTransform rect = target.rectTransform;
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 bottom = Vector3.Lerp(corners[0], corners[3], u);
            Vector3 top = Vector3.Lerp(corners[1], corners[2], u);
            return Vector3.Lerp(bottom, top, v);
        }

        Vector3 local = new Vector3(u - 0.5f, v - 0.5f, 0f);
        return _quadTransform != null ? _quadTransform.TransformPoint(local) : transform.TransformPoint(local);
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
        var rt = new RenderTexture(simWidth, simHeight, 0, format)
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
            if (Application.isPlaying)
            {
                Destroy(rt);
            }
            else
            {
                DestroyImmediate(rt);
            }
            rt = null;
        }
    }

    private void CreateDebugBuffer()
    {
        ReleaseDebugBuffer();
        _debugBuffer = new ComputeBuffer(12, sizeof(float));
    }

    private void EnsureDebugBuffer()
    {
        if (_debugBuffer == null)
        {
            CreateDebugBuffer();
        }
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
            _quadTransform = quad.transform;
            UpdateQuadScale();
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

    private void UpdateQuadScale()
    {
        if (_quadTransform == null)
        {
            return;
        }
        float size = Mathf.Max(0.01f, quadSize);
        float aspect = simHeight > 0 ? (float)simWidth / simHeight : 1f;
        _quadTransform.localScale = new Vector3(size * aspect, size, 1f);
    }

    /// <summary>
    /// Main Unity update loop: advance substeps, visualize, and log stats.
     /// </summary>
    private void Update()
    {
        if (!_initialized && !Initialize())
            return;

        Tick(Time.deltaTime);
        HandleDebugLogging(Time.deltaTime);
        MaybeRequestSynopticReadback();
        MaybeRequestHumidityReadback();

        if (_quadMaterial != null)
        {
            _quadMaterial.mainTexture = _display;
        }
    }

    /// <summary>
    /// Advance the simulation by a slice of real time. Tests and play mode should both call this
    /// to share the same fast-forward schedule and stepping behavior.
    /// </summary>
    public void Tick(float realDt)
    {
        if (!_initialized && !Initialize())
            return;

        ConfigureSurfaceMap();

        float clampedRealDt = Mathf.Clamp(realDt, 0f, Mathf.Max(0.0001f, maxRealDt));
        float timeScale = Mathf.Max(0.1f, GetEffectiveTimeScale(clampedRealDt));
        _effectiveTimeScale = timeScale;

        float stepDt = Mathf.Max(0.0001f, fixedSimDt);
        int macroStepCap = Mathf.Max(1, maxStepsPerTick);
        int innerSubsteps = Mathf.Max(1, substeps);
        float subDt = Mathf.Max(0.0001f, stepDt / innerSubsteps);

        _simAccumulator += clampedRealDt * timeScale;

        int macroSteps = 0;
        while (_simAccumulator >= stepDt && macroSteps < macroStepCap)
        {
            for (int i = 0; i < innerSubsteps; i++)
            {
                RunFluidStep(subDt);
            }
            _simAccumulator -= stepDt;
            macroSteps++;
        }

        // If we hit the cap (e.g., due to a debugger pause), prevent an ever-growing backlog.
        if (macroSteps >= macroStepCap)
        {
            _simAccumulator = Mathf.Min(_simAccumulator, stepDt);
        }

        Visualize();
    }

    private float GetEffectiveTimeScale(float realDt)
    {
        float scale = _scenarioTimeScale;
        if (_fastForwardTimer > 0f && fastForwardDuration > 0f)
        {
            float t = Mathf.Clamp01(_fastForwardTimer / fastForwardDuration);
            float fastScale = Mathf.Max(1f, fastForwardScale);
            scale *= Mathf.Lerp(1f, fastScale, t);
            _fastForwardTimer = Mathf.Max(0f, _fastForwardTimer - Mathf.Max(0f, realDt));
        }
        return scale;
    }

    /// <summary>
    /// Execute one simulation substep (force, advect, project, microphysics).
    /// </summary>
    private void RunFluidStep(float dt)
    {
        EnsureDebugBuffer();
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

        ApplyBackgroundWind();
        ApplySynopticPressureForcing();
        ApplyCoriolisForce();
        ApplyConvergence();
        AdvectVelocity();
        AdvectHumidity();
        RelaxHumidityToProfile();
        AdvectCloud();
        AdvectTemperature();
        AdvectTurbulence();
        AdvectSynopticPressure();
        ProjectVelocity();
        ApplyUpperDamping();
        ApplyThermalBuoyancy();
        RunMicrophysics(dt);
        GatherStatsAndUpdatePrecipitation(dt);
        _stepsCompleted++;
    }

    /// <summary>
    /// Temporarily amplify condensation/precipitation rates to mimic a heat burst.
    /// </summary>
    public void TriggerRocketBoost(float duration, float condensationMultiplier, float precipitationMultiplier)
    {
        _rocketBoostTimer = Mathf.Max(_rocketBoostTimer, Mathf.Max(0f, duration));
        _rocketCondensationMultiplier = Mathf.Max(1f, condensationMultiplier);
        _rocketPrecipitationMultiplier = Mathf.Max(1f, precipitationMultiplier);
    }

    /// <summary>Enable/disable the perpetual ground forcing plume.</summary>
    public void SetBaseSourceActive(bool active)
    {
        _useBaseSource = active;
    }

    /// <summary>Flush any scheduled rocket bursts/explosions.</summary>
    public void ClearScriptedBursts()
    {
        _scheduledRocketBursts.Clear();
        _activeRocketBursts.Clear();
    }

    /// <summary>Enqueue a single scripted burst with no delay.</summary>
    public void TriggerRocketBurst(Burst burst, float duration)
    {
        TriggerRocketBurst(burst, 0f, duration);
    }

    /// <summary>Enqueue a burst with optional delay and duration.</summary>
    public void TriggerRocketBurst(Burst burst, float delay, float duration)
    {
        ScheduleScriptedBurst(burst, delay, duration, false);
    }

    /// <summary>
    /// Enqueue a sequence of bursts spaced out by interval (rocket ascent path).
    /// </summary>
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

    /// <summary>Helper for tests to advance the sim deterministically.</summary>
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

    /// <summary>
    /// Update countdowns for scheduled bursts and apply any active injections.
    /// </summary>
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
                active.modulateSurface && _hasSurfaceMap,
                burst.heat,
                burst.turbulence);
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

    /// <summary>
    /// Clamp/sanitize burst parameters and push them onto the scheduled list.
    /// </summary>
    private void ScheduleScriptedBurst(Burst burst, float delay, float duration, bool modulateSurface)
    {
        Burst adjusted = burst;
        if (adjusted.radius <= 0f)
        {
            adjusted.radius = Mathf.Max(0.01f, sourceRadius * 0.6f);
        }
        if (adjusted.density < 0f)
        {
            adjusted.density = Mathf.Max(0.1f, sourceDensity);
        }
        if (adjusted.velocity == Vector2.zero)
        {
            adjusted.velocity = sourceVelocity;
        }
        if (adjusted.heat < 0f)
        {
            adjusted.heat = 0f;
        }
        if (adjusted.turbulence < 0f)
        {
            adjusted.turbulence = 0f;
        }

        _scheduledRocketBursts.Add(new ScheduledBurst
        {
            burst = adjusted,
            delay = Mathf.Max(0f, delay),
            duration = Mathf.Max(0.0001f, duration),
            modulateSurface = modulateSurface
        });
    }

    /// <summary>
    /// Write density/velocity/temperature/turbulence sources into the compute shader.
    /// </summary>
    private void InjectImpulse(Vector2 center, float radius, float density, Vector2 velocity, float dt, bool modulateSurface = false, float heat = 0f, float turbulence = 0f)
    {
        fluidCompute.SetVector("_SourceCenter", new Vector4(center.x, center.y, 0f, 0f));
        fluidCompute.SetFloat("_SourceRadius", Mathf.Max(0.001f, radius));
        fluidCompute.SetFloat("_SourceDensity", density * dt);
        fluidCompute.SetVector("_SourceVelocity", new Vector4(velocity.x * dt, velocity.y * dt, 0f, 0f));
        fluidCompute.SetFloat("_SourceFeather", Mathf.Max(0.0001f, sourceFeather));
        float blend = modulateSurface && _hasSurfaceMap ? 1f : 0f;
        fluidCompute.SetFloat("_SourceMapBlend", blend);
        fluidCompute.SetFloat("_SourceHeat", heat * dt);
        fluidCompute.SetFloat("_SourceTurbulence", turbulence * dt);
        Texture surfaceTex = surfaceMoistureMap != null ? surfaceMoistureMap : Texture2D.whiteTexture;
        fluidCompute.SetTexture(_kInject, "_SurfaceMoistureTex", surfaceTex);
        fluidCompute.SetBuffer(_kInject, "_DebugBuffer", _debugBuffer);

        fluidCompute.SetTexture(_kInject, "_Velocity", _velocityA);
        fluidCompute.SetTexture(_kInject, "_Humidity", _humidityA);
        fluidCompute.SetTexture(_kInject, "_Temperature", _temperatureA);
        fluidCompute.SetTexture(_kInject, "_Turbulence", _turbulenceA);
        Dispatch(_kInject);
    }

    /// <summary>Add a synoptic pressure gaussian to seed storm centers.</summary>
    private void InjectSynopticPressure(Vector2 center, float radius, float strength)
    {
        fluidCompute.SetVector("_PressureCenter", new Vector4(center.x, center.y, 0f, 0f));
        fluidCompute.SetFloat("_PressureRadius", Mathf.Max(0.0001f, radius));
        fluidCompute.SetFloat("_PressureStrength", strength);
        fluidCompute.SetTexture(_kInjectSynopticPressure, "_SynopticPressureWrite", _synopticPressureA);
        Dispatch(_kInjectSynopticPressure);
    }

    /// <summary>Advect the velocity grid.</summary>
    private void AdvectVelocity()
    {
        fluidCompute.SetTexture(_kAdvectVelocity, "_VectorFieldRead", _velocityA);
        fluidCompute.SetTexture(_kAdvectVelocity, "_VectorFieldWrite", _velocityB);
        fluidCompute.SetBuffer(_kAdvectVelocity, "_DebugBuffer", _debugBuffer);
        Dispatch(_kAdvectVelocity);
        Swap(ref _velocityA, ref _velocityB);
    }

    private void ApplyBackgroundWind()
    {
        if (backgroundWindStrength <= 0.0001f)
        {
            return;
        }
        fluidCompute.SetVector("_BackgroundWind", new Vector4(backgroundWind.x, backgroundWind.y, 0f, 0f));
        fluidCompute.SetFloat("_BackgroundWindStrength", Mathf.Max(0f, backgroundWindStrength));
        fluidCompute.SetTexture(_kBackgroundWind, "_BackgroundWindVelocity", _velocityA);
        Dispatch(_kBackgroundWind);
    }

    private void ApplySynopticPressureForcing()
    {
        if (pressureForceStrength <= 0.0001f)
        {
            return;
        }
        fluidCompute.SetFloat("_PressureForceStrength", Mathf.Max(0f, pressureForceStrength));
        fluidCompute.SetTexture(_kPressureForcing, "_PressureForceTex", _synopticPressureA);
        fluidCompute.SetTexture(_kPressureForcing, "_PressureForceVelocity", _velocityA);
        Dispatch(_kPressureForcing);
    }

    private void ApplyCoriolisForce()
    {
        if (Mathf.Abs(coriolisStrength) <= 0.0001f)
        {
            return;
        }
        fluidCompute.SetFloat("_CoriolisStrength", coriolisStrength);
        fluidCompute.SetTexture(_kCoriolis, "_CoriolisVelocity", _velocityA);
        Dispatch(_kCoriolis);
    }

    private void ApplyConvergence()
    {
        if (!enableConvergence || convergenceStrength <= 0.0001f)
        {
            return;
        }

        fluidCompute.SetFloat("_ConvergenceStrength", Mathf.Max(0f, convergenceStrength));
        fluidCompute.SetFloat("_ConvergenceWidth", Mathf.Max(0.001f, convergenceWidth));
        fluidCompute.SetFloat("_ConvergenceHeight", Mathf.Max(0.001f, convergenceHeight));
        fluidCompute.SetFloat("_ConvergenceWindSpeed", convergenceWindSpeed);
        fluidCompute.SetFloat("_ConvergenceUpdraft", convergenceUpdraft);
        fluidCompute.SetTexture(_kApplyConvergence, "_ConvergenceVelocity", _velocityA);
        Dispatch(_kApplyConvergence);
    }

    private void RelaxHumidityToProfile()
    {
        if (!enableSurfaceHumidityRelax || surfaceHumidityRelaxStrength <= 0.0001f || surfaceHumidityRelaxHeight <= 0.0001f)
        {
            return;
        }

        fluidCompute.SetFloat("_SurfaceHumidity", Mathf.Max(0f, surfaceHumidity));
        fluidCompute.SetFloat("_HumidityDecay", Mathf.Max(0f, humidityDecay));
        fluidCompute.SetFloat("_SurfaceHumidityRelaxStrength", Mathf.Max(0f, surfaceHumidityRelaxStrength));
        fluidCompute.SetFloat("_SurfaceHumidityRelaxHeight", Mathf.Max(0.0001f, surfaceHumidityRelaxHeight));
        fluidCompute.SetTexture(_kRelaxHumidityToProfile, "_RelaxHumidity", _humidityA);
        Dispatch(_kRelaxHumidityToProfile);
    }

    private void ApplyThermalBuoyancy()
    {
        if (Mathf.Abs(thermalBuoyancyStrength) <= 0.0001f)
        {
            return;
        }

        fluidCompute.SetFloat("_BaseTemperature", baseTemperature);
        fluidCompute.SetFloat("_LapseRate", lapseRate);
        fluidCompute.SetFloat("_ThermalBuoyancyStrength", thermalBuoyancyStrength);
        fluidCompute.SetTexture(_kApplyThermalBuoyancy, "_ThermalBuoyancyVelocity", _velocityA);
        fluidCompute.SetTexture(_kApplyThermalBuoyancy, "_ThermalBuoyancyTemperature", _temperatureA);
        Dispatch(_kApplyThermalBuoyancy);
    }

    private void SeedThermoProfile(float baseTemp, float lapse, float surfaceHumid, float decay)
    {
        fluidCompute.SetFloat("_BaseTemperature", baseTemp);
        fluidCompute.SetFloat("_LapseRate", lapse);
        fluidCompute.SetFloat("_SurfaceHumidity", Mathf.Max(0f, surfaceHumid));
        fluidCompute.SetFloat("_HumidityDecay", Mathf.Max(0f, decay));
        fluidCompute.SetTexture(_kSeedThermoProfile, "_SeedTemperatureA", _temperatureA);
        fluidCompute.SetTexture(_kSeedThermoProfile, "_SeedTemperatureB", _temperatureB);
        fluidCompute.SetTexture(_kSeedThermoProfile, "_SeedHumidityA", _humidityA);
        fluidCompute.SetTexture(_kSeedThermoProfile, "_SeedHumidityB", _humidityB);
        Dispatch(_kSeedThermoProfile);
    }

    private void AdvectSynopticPressure()
    {
        fluidCompute.SetFloat("_DensityDissipation", Mathf.Clamp01(synopticPressureDissipation));
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarFieldRead", _synopticPressureA);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarFieldWrite", _synopticPressureB);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarVelocity", _velocityA);
        fluidCompute.SetBuffer(_kAdvectScalar, "_DebugBuffer", _debugBuffer);
        Dispatch(_kAdvectScalar);
        Swap(ref _synopticPressureA, ref _synopticPressureB);
    }

    /// <summary>Advect the humidity scalar.</summary>
    private void AdvectHumidity()
    {
        fluidCompute.SetFloat("_DensityDissipation", densityDissipation);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarFieldRead", _humidityA);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarFieldWrite", _humidityB);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarVelocity", _velocityA);
        fluidCompute.SetBuffer(_kAdvectScalar, "_DebugBuffer", _debugBuffer);
        Dispatch(_kAdvectScalar);
        Swap(ref _humidityA, ref _humidityB);
    }

    /// <summary>Advect cloud water.</summary>
    private void AdvectCloud()
    {
        fluidCompute.SetFloat("_DensityDissipation", densityDissipation);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarFieldRead", _cloudA);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarFieldWrite", _cloudB);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarVelocity", _velocityA);
        fluidCompute.SetBuffer(_kAdvectScalar, "_DebugBuffer", _debugBuffer);
        Dispatch(_kAdvectScalar);
        Swap(ref _cloudA, ref _cloudB);
    }

    /// <summary>Advect the temperature field.</summary>
    private void AdvectTemperature()
    {
        fluidCompute.SetFloat("_DensityDissipation", Mathf.Clamp01(temperatureDissipation));
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarFieldRead", _temperatureA);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarFieldWrite", _temperatureB);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarVelocity", _velocityA);
        fluidCompute.SetBuffer(_kAdvectScalar, "_DebugBuffer", _debugBuffer);
        Dispatch(_kAdvectScalar);
        Swap(ref _temperatureA, ref _temperatureB);
    }

    /// <summary>Advect the turbulence intensity proxy.</summary>
    private void AdvectTurbulence()
    {
        fluidCompute.SetFloat("_DensityDissipation", Mathf.Clamp01(turbulenceDissipation));
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarFieldRead", _turbulenceA);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarFieldWrite", _turbulenceB);
        fluidCompute.SetTexture(_kAdvectScalar, "_ScalarVelocity", _velocityA);
        fluidCompute.SetBuffer(_kAdvectScalar, "_DebugBuffer", _debugBuffer);
        Dispatch(_kAdvectScalar);
        Swap(ref _turbulenceA, ref _turbulenceB);
    }

    /// <summary>Enforce incompressibility via divergence compute and Jacobi solve.</summary>
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

    /// <summary>
    /// Execute condensation/evaporation/precipitation with thermal feedback.
    /// </summary>
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
        fluidCompute.SetFloat("_TemperatureSaturationFactor", temperatureSaturationFactor);
        fluidCompute.SetFloat("_LatentHeatTemperatureGain", latentHeatTemperatureGain);
        fluidCompute.SetFloat("_EvaporationCoolingFactor", evaporationCoolingFactor);
        fluidCompute.SetFloat("_TurbulencePrecipFactor", turbulencePrecipitationFactor);
        fluidCompute.SetFloat("_TemperatureDecay", temperatureDecay);
        fluidCompute.SetFloat("_TurbulenceDecay", turbulenceDecay);
        fluidCompute.SetTexture(_kMicrophysics, "_MicroHumidity", _humidityA);
        fluidCompute.SetTexture(_kMicrophysics, "_MicroCloud", _cloudA);
        fluidCompute.SetTexture(_kMicrophysics, "_MicroVelocity", _velocityA);
        fluidCompute.SetTexture(_kMicrophysics, "_MicroTemperature", _temperatureA);
        fluidCompute.SetTexture(_kMicrophysics, "_MicroTurbulence", _turbulenceA);
        fluidCompute.SetTexture(_kMicrophysics, "_PrecipitationTex", _precipitation);
        Dispatch(_kMicrophysics);
    }

    /// <summary>Fade velocity near the top of the domain to keep clouds visible.</summary>
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

    /// <summary>Read back averages for logging/precipitation feedback.</summary>
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

    /// <summary>Drive the optional particle system and re-injection feedback loop.</summary>
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

    /// <summary>Blit humidity/cloud buffers into the display texture.</summary>
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
        fluidCompute.SetFloat("_ShowHumidity", showHumidityInDisplay ? 1f : 0f);
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
            Camera uiCamera = null;
            Canvas canvas = target.canvas;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                uiCamera = canvas.worldCamera;
            }
            if (uiCamera == null)
            {
                uiCamera = Camera.main;
            }
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPos, uiCamera, out Vector2 local))
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

        if (_quadTransform != null)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return false;
            }
            Ray ray = cam.ScreenPointToRay(screenPos);
            Plane plane = new Plane(_quadTransform.forward, _quadTransform.position);
            if (plane.Raycast(ray, out float enter))
            {
                Vector3 worldPoint = ray.GetPoint(enter);
                Vector3 localPoint = _quadTransform.InverseTransformPoint(worldPoint);
                float u = localPoint.x + 0.5f;
                float v = localPoint.y + 0.5f;
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

    /// <summary>Clear all simulation buffers to a neutral state.</summary>
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
        ClearRenderTexture(_temperatureA, Color.clear);
        ClearRenderTexture(_temperatureB, Color.clear);
        ClearRenderTexture(_turbulenceA, Color.clear);
        ClearRenderTexture(_turbulenceB, Color.clear);
        ClearRenderTexture(_pressureA, Color.clear);
        ClearRenderTexture(_pressureB, Color.clear);
        ClearRenderTexture(_divergence, Color.clear);
        ClearRenderTexture(_synopticPressureA, Color.clear);
        ClearRenderTexture(_synopticPressureB, Color.clear);
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

    /// <summary>Populate default demo presets if none are serialized.</summary>
    private void EnsureDemoScenarios()
    {
        if (demoScenarios != null && demoScenarios.Length > 0)
            return;

        demoScenarios = new[]
        {
            new DemoScenario
            {
                name = "Sandbox",
                densityDissipation = 0.9999f,
                velocityDissipation = 0.995f,
                sourceRadius = 0.12f,
                sourceDensity = 2000f,
                sourceHeight = 0.18f,
                sourceVelocity = new Vector2(0f, 6.0f),
                timeScale = 0.6f,
                disableBaseSource = false,
                quadSize = 7.5f,
                simWidth = 640,
                simHeight = 256,
                loopInterval = 0f,
                precipitationFeedbackOverride = -1f
            },
            new DemoScenario
            {
                name = "Two Storms",
                densityDissipation = 0.999f,
                velocityDissipation = 0.995f,
                sourceRadius = 0.08f,
                sourceDensity = 4f,
                sourceHeight = 0.05f,
                sourceVelocity = new Vector2(0f, 1.5f),
                timeScale = 1f,
                disableBaseSource = true,
                quadSize = 7.5f,
                simWidth = 640,
                simHeight = 256,
                loopInterval = 0f,
                precipitationFeedbackOverride = -1f,
                overrideSynopticSettings = true,
                pressureForceStrength = 1.1f,
                coriolisStrength = 1.4f,
                synopticPressureDissipation = 0.999f,
                stormRadius = 0.12f,
                stormStrength = -1.4f,
                stormCenters = new[]
                {
                    new Vector2(0.35f, 0.45f),
                    new Vector2(0.65f, 0.55f)
                }
            },
            new DemoScenario
            {
                name = "Thunderstorm",
                densityDissipation = 0.9995f,
                velocityDissipation = 0.994f,
                sourceRadius = 0.05f,
                sourceDensity = 2f,
                sourceHeight = 0.05f,
                sourceVelocity = new Vector2(0f, 0.4f),
                timeScale = 0.5f,
                disableBaseSource = true,
                quadSize = 7.5f,
                simWidth = 640,
                simHeight = 256,
                precipitationFeedbackOverride = -1f,
                overrideSynopticSettings = false,
                overrideConvergence = true,
                enableConvergence = true,
                convergenceStrength = 5.2f,
                convergenceWidth = 0.045f,
                convergenceHeight = 0.6f,
                convergenceWindSpeed = 1.45f,
                convergenceUpdraft = 1.4f,
                overrideMicrophysics = true,
                saturationThreshold = 0.014f,
                condensationRate = 2200f,
                evaporationRate = 0f,
                precipitationRate = 0.2f,
                latentHeatBuoyancy = 9.2f,
                overrideFastForward = true,
                fastForwardScale = 3f,
                fastForwardDuration = 1.0f,
                useThermoProfile = true,
                baseTemperature = 0.55f,
                lapseRate = 1.25f,
                surfaceHumidity = 0.06f,
                humidityDecay = 1.6f,
                enableSurfaceHumidityRelax = true,
                surfaceHumidityRelaxStrength = 10.0f,
                surfaceHumidityRelaxHeight = 0.11f,
                thermalBuoyancyStrength = 9.0f,
                initialBursts = new[]
                {
                    new Burst
                    {
                        position = new Vector2(0.5f, 0.18f),
                        radius = 0.17f,
                        density = 0f,
                        velocity = Vector2.zero,
                        heat = 6.0f,
                        turbulence = 1.2f
                    }
                },
                loopBursts = new[]
                {
                    new Burst
                    {
                        position = new Vector2(0.5f, 0.08f),
                        radius = 0.11f,
                        density = 0f,
                        velocity = Vector2.zero,
                        heat = 3.2f,
                        turbulence = 0.5f
                    }
                },
                loopInterval = 0.22f
            },
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
                quadSize = 7.5f,
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
                densityDissipation = 0.9994f,
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
                    },
                    new Burst
                    {
                        position = new Vector2(0.5f, 0.24f),
                        radius = 0.12f,
                        density = 30f,
                        velocity = new Vector2(0f, 2.4f)
                    }
                },
                loopBursts = null,
                loopInterval = 0f,
                rocketDelay = 4f,
                rocketBurstDuration = 0.2f,
                rocketBurstInterval = 0.12f,
                rocketBursts = new[]
                {
                    new Burst { position = new Vector2(0.5f, 0.08f), radius = 0.09f, density = 0f, velocity = Vector2.zero },
                    new Burst { position = new Vector2(0.5f, 0.22f), radius = 0.07f, density = 0f, velocity = Vector2.zero },
                    new Burst { position = new Vector2(0.5f, 0.36f), radius = 0.06f, density = 0f, velocity = Vector2.zero },
                    new Burst { position = new Vector2(0.5f, 0.5f), radius = 0.05f, density = 0f, velocity = Vector2.zero },
                    new Burst { position = new Vector2(0.5f, 0.66f), radius = 0.05f, density = 0f, velocity = Vector2.zero }
                },
                disableBaseSourceAfterRocket = true,
                rocketBoostDuration = 1.2f,
                rocketCondensationMultiplier = 3.5f,
                rocketPrecipitationMultiplier = 5.5f,
                disablePrecipitationFeedback = true,
                precipitationFeedbackOverride = 0f,
                triggerRocketExplosion = true,
                rocketExplosion = new Burst
                {
                    position = new Vector2(0.5f, 0.74f),
                    radius = 0.06f,
                    density = 0f,
                    velocity = new Vector2(0f, 0.8f),
                    heat = 12f,
                    turbulence = 2.5f
                },
                rocketExplosionDuration = 0.2f
            }
        };
    }

    /// <summary>Reset the sim and apply the selected demo configuration.</summary>
    public void ApplyDemo(int index)
    {
        if (demoScenarios == null || demoScenarios.Length == 0)
            return;

        if (!_initialized && !Initialize())
            return;

        int clamped = Mathf.Clamp(index, 0, demoScenarios.Length - 1);
        _activeDemo = clamped;
        DemoScenario scenario = demoScenarios[clamped];

        int nextSimWidth = scenario.simWidth > 0 ? scenario.simWidth : _defaultSimWidth;
        int nextSimHeight = scenario.simHeight > 0 ? scenario.simHeight : _defaultSimHeight;
        bool rebuildSim = nextSimWidth != simWidth || nextSimHeight != simHeight;
        simWidth = nextSimWidth;
        simHeight = nextSimHeight;

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
        quadSize = scenario.quadSize > 0f ? scenario.quadSize : _defaultQuadSize;
        UpdateQuadScale();
        if (scenario.overrideSynopticSettings)
        {
            pressureForceStrength = scenario.pressureForceStrength;
            coriolisStrength = scenario.coriolisStrength;
            synopticPressureDissipation = scenario.synopticPressureDissipation > 0f
                ? scenario.synopticPressureDissipation
                : _defaultSynopticPressureDissipation;
        }
        else
        {
            pressureForceStrength = _defaultPressureForceStrength;
            coriolisStrength = _defaultCoriolisStrength;
            synopticPressureDissipation = _defaultSynopticPressureDissipation;
        }

        if (scenario.overrideConvergence)
        {
            enableConvergence = scenario.enableConvergence;
            convergenceStrength = scenario.convergenceStrength;
            convergenceWidth = scenario.convergenceWidth;
            convergenceHeight = scenario.convergenceHeight;
            convergenceWindSpeed = scenario.convergenceWindSpeed;
            convergenceUpdraft = scenario.convergenceUpdraft;
        }
        else
        {
            enableConvergence = _defaultEnableConvergence;
            convergenceStrength = _defaultConvergenceStrength;
            convergenceWidth = _defaultConvergenceWidth;
            convergenceHeight = _defaultConvergenceHeight;
            convergenceWindSpeed = _defaultConvergenceWindSpeed;
            convergenceUpdraft = _defaultConvergenceUpdraft;
        }

        if (scenario.overrideMicrophysics)
        {
            saturationThreshold = scenario.saturationThreshold;
            condensationRate = scenario.condensationRate;
            evaporationRate = scenario.evaporationRate;
            precipitationRate = scenario.precipitationRate;
            latentHeatBuoyancy = scenario.latentHeatBuoyancy;
        }
        else
        {
            saturationThreshold = _defaultSaturationThreshold;
            condensationRate = _defaultCondensationRate;
            evaporationRate = _defaultEvaporationRate;
            precipitationRate = _defaultPrecipitationRate;
            latentHeatBuoyancy = _defaultLatentHeatBuoyancy;
        }

        if (scenario.overrideFastForward)
        {
            fastForwardScale = scenario.fastForwardScale > 0f ? scenario.fastForwardScale : _defaultFastForwardScale;
            fastForwardDuration = Mathf.Max(0f, scenario.fastForwardDuration);
        }
        else
        {
            fastForwardScale = _defaultFastForwardScale;
            fastForwardDuration = _defaultFastForwardDuration;
        }

        if (scenario.enableSurfaceHumidityRelax)
        {
            enableSurfaceHumidityRelax = true;
            surfaceHumidityRelaxStrength = scenario.surfaceHumidityRelaxStrength > 0f
                ? scenario.surfaceHumidityRelaxStrength
                : _defaultSurfaceHumidityRelaxStrength;
            surfaceHumidityRelaxHeight = scenario.surfaceHumidityRelaxHeight > 0f
                ? scenario.surfaceHumidityRelaxHeight
                : _defaultSurfaceHumidityRelaxHeight;
        }
        else
        {
            enableSurfaceHumidityRelax = _defaultEnableSurfaceHumidityRelax;
            surfaceHumidityRelaxStrength = _defaultSurfaceHumidityRelaxStrength;
            surfaceHumidityRelaxHeight = _defaultSurfaceHumidityRelaxHeight;
        }

        thermalBuoyancyStrength = scenario.thermalBuoyancyStrength != 0f
            ? scenario.thermalBuoyancyStrength
            : _defaultThermalBuoyancyStrength;

        _fastForwardTimer = fastForwardDuration;
        ApplyPrecipitationFeedbackOverride(scenario);

        ClearScriptedBursts();
        if (rebuildSim)
        {
            RebuildSimulation();
        }
        ResetSimulation();
        if (scenario.useThermoProfile)
        {
            float baseTemp = scenario.baseTemperature != 0f ? scenario.baseTemperature : _defaultBaseTemperature;
            float lapse = scenario.lapseRate != 0f ? scenario.lapseRate : _defaultLapseRate;
            float humid = scenario.surfaceHumidity != 0f ? scenario.surfaceHumidity : _defaultSurfaceHumidity;
            float decay = scenario.humidityDecay != 0f ? scenario.humidityDecay : _defaultHumidityDecay;
            baseTemperature = baseTemp;
            lapseRate = lapse;
            surfaceHumidity = humid;
            humidityDecay = decay;
            SeedThermoProfile(baseTemp, lapse, humid, decay);
        }

        if (scenario.stormCenters != null && scenario.stormCenters.Length > 0)
        {
            float radius = Mathf.Max(0.005f, scenario.stormRadius > 0f ? scenario.stormRadius : 0.12f);
            float strength = scenario.stormStrength != 0f ? scenario.stormStrength : -1.25f;
            foreach (var center in scenario.stormCenters)
            {
                InjectSynopticPressure(center, radius, strength);
            }
        }
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

    private void OnDrawGizmos()
    {
        if (!showSynopticPressureGizmos || _synopticPressureData == null || _synopticPressureWidth == 0 || _synopticPressureHeight == 0)
        {
            // Keep going so humidity gizmos can still render.
        }

        if (showSynopticPressureGizmos && _synopticPressureData != null && _synopticPressureWidth > 0 && _synopticPressureHeight > 0)
        {
            int grid = Mathf.Max(2, synopticGizmoGrid);
            float stepX = 1f / (grid - 1);
            float stepY = 1f / (grid - 1);
            float aspect = _synopticPressureHeight > 0 ? (float)_synopticPressureWidth / _synopticPressureHeight : 1f;
            float scale = Mathf.Max(0.001f, synopticGizmoScale);

            Gizmos.color = synopticGizmoColor;

            for (int gy = 0; gy < grid; gy++)
            {
                for (int gx = 0; gx < grid; gx++)
                {
                    float u = gx * stepX;
                    float v = gy * stepY;
                    int x = Mathf.Clamp(Mathf.RoundToInt(u * (_synopticPressureWidth - 1)), 1, _synopticPressureWidth - 2);
                    int y = Mathf.Clamp(Mathf.RoundToInt(v * (_synopticPressureHeight - 1)), 1, _synopticPressureHeight - 2);

                    float left = _synopticPressureData[(y * _synopticPressureWidth) + (x - 1)];
                    float right = _synopticPressureData[(y * _synopticPressureWidth) + (x + 1)];
                    float down = _synopticPressureData[((y - 1) * _synopticPressureWidth) + x];
                    float up = _synopticPressureData[((y + 1) * _synopticPressureWidth) + x];

                    Vector2 grad = new Vector2(right - left, up - down) * 0.5f;
                    grad.x *= aspect;
                    Vector2 dir = -grad;
                    if (dir.sqrMagnitude > 0.000001f)
                    {
                        dir.Normalize();
                    }

                    Vector3 world = GetSynopticGizmoWorldPosition(u, v);
                    Vector3 tip = world + new Vector3(dir.x, dir.y, 0f) * scale;
                    Gizmos.DrawLine(world, tip);
                }
            }
        }

        if (showHumidityGizmos && _humidityData != null && _humidityWidth > 0 && _humidityHeight > 0)
        {
            int grid = Mathf.Max(2, humidityGizmoGrid);
            float stepX = 1f / (grid - 1);
            float stepY = 1f / (grid - 1);
            float scale = Mathf.Max(0.001f, humidityGizmoScale);

            for (int gy = 0; gy < grid; gy++)
            {
                for (int gx = 0; gx < grid; gx++)
                {
                    float u = gx * stepX;
                    float v = gy * stepY;
                    int x = Mathf.Clamp(Mathf.RoundToInt(u * (_humidityWidth - 1)), 0, _humidityWidth - 1);
                    int y = Mathf.Clamp(Mathf.RoundToInt(v * (_humidityHeight - 1)), 0, _humidityHeight - 1);
                    float humidity = _humidityData[(y * _humidityWidth) + x];
                    float t = Mathf.Clamp01(humidity * 0.25f);
                    Gizmos.color = Color.Lerp(humidityLowColor, humidityHighColor, t);
                    Vector3 world = GetSynopticGizmoWorldPosition(u, v);
                    Gizmos.DrawCube(world, Vector3.one * scale);
                }
            }
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
