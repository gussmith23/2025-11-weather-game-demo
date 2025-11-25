using NUnit.Framework;
using UnityEngine;

public class WeatherFluidTests
{
    private const int Resolution = 128;
    private const bool DumpStepStats = false;
    private ComputeShader shader;
    private RenderTexture velocityA;
    private RenderTexture velocityB;
    private RenderTexture humidityA;
    private RenderTexture humidityB;
    private RenderTexture cloudA;
    private RenderTexture cloudB;
    private RenderTexture temperatureA;
    private RenderTexture temperatureB;
    private RenderTexture turbulenceA;
    private RenderTexture turbulenceB;
    private RenderTexture pressureA;
    private RenderTexture pressureB;
    private RenderTexture divergence;
    private RenderTexture precipitation;
    private ComputeBuffer statsBuffer;
    private Vector2Int dispatch;

    private int kInject;
    private int kAdvectVelocity;
    private int kAdvectScalar;
    private int kDivergence;
    private int kJacobi;
    private int kSubtract;
    private int kClear;
    private int kStats;
    private int kMicrophysics;
    private const float TemperatureDissipation = 0.9985f;
    private const float TurbulenceDissipation = 0.995f;
    private const float TemperatureSaturationFactor = 0.08f;
    private const float LatentHeatTemperatureGain = 1.2f;
    private const float EvaporationCoolingFactor = 0.8f;
    private const float TurbulencePrecipitationFactor = 2.0f;
    private const float TemperatureDecay = 0.6f;
    private const float TurbulenceDecay = 1.5f;

    private struct FluidStats
    {
        public float avgHumidity;
        public float maxHumidity;
        public float avgSpeed;
        public float maxSpeed;
        public float avgCloud;
        public float maxCloud;
        public float totalWater;
        public float cellCount;
        public float avgPrecip;
        public float maxPrecip;
        public float precipSum;
    }

    [SetUp]
    public void SetUp()
    {
        shader = Resources.Load<ComputeShader>("WeatherFluid");
        Assert.IsNotNull(shader, "WeatherFluid compute shader must exist under Resources.");

        kInject = shader.FindKernel("InjectSource");
        kAdvectVelocity = shader.FindKernel("AdvectVelocity");
        kAdvectScalar = shader.FindKernel("AdvectScalar");
        kDivergence = shader.FindKernel("ComputeDivergence");
        kJacobi = shader.FindKernel("JacobiPressure");
        kSubtract = shader.FindKernel("SubtractGradient");
        kClear = shader.FindKernel("ClearTexture");
        kStats = shader.FindKernel("ComputeStats");
        kMicrophysics = shader.FindKernel("MoistureMicrophysics");

        dispatch = new Vector2Int(
            Mathf.CeilToInt(Resolution / 8f),
            Mathf.CeilToInt(Resolution / 8f));

        velocityA = CreateVectorRT();
        velocityB = CreateVectorRT();
        humidityA = CreateScalarRT();
        humidityB = CreateScalarRT();
        cloudA = CreateScalarRT();
        cloudB = CreateScalarRT();
        temperatureA = CreateScalarRT();
        temperatureB = CreateScalarRT();
        turbulenceA = CreateScalarRT();
        turbulenceB = CreateScalarRT();
        pressureA = CreateScalarRT(RenderTextureFormat.RFloat);
        pressureB = CreateScalarRT(RenderTextureFormat.RFloat);
        divergence = CreateScalarRT(RenderTextureFormat.RFloat);
        precipitation = CreateScalarRT();
        ClearRenderTexture(velocityA);
        ClearRenderTexture(velocityB);
        ClearRenderTexture(humidityA);
        ClearRenderTexture(humidityB);
        ClearRenderTexture(cloudA);
        ClearRenderTexture(cloudB);
        ClearRenderTexture(temperatureA);
        ClearRenderTexture(temperatureB);
        ClearRenderTexture(turbulenceA);
        ClearRenderTexture(turbulenceB);
        ClearRenderTexture(pressureA);
        ClearRenderTexture(pressureB);
        ClearRenderTexture(divergence);
        ClearRenderTexture(precipitation);
        statsBuffer = new ComputeBuffer(12, sizeof(float));

        shader.SetInts("_SimSize", Resolution, Resolution);
        shader.SetFloats("_InvSimSize", 1f / Resolution, 1f / Resolution);
        ConfigureMicrophysicsDefaults();
        shader.SetTexture(kInject, "_SurfaceMoistureTex", Texture2D.whiteTexture);
    }

    [TearDown]
    public void TearDown()
    {
        ReleaseRT(ref velocityA);
        ReleaseRT(ref velocityB);
        ReleaseRT(ref humidityA);
        ReleaseRT(ref humidityB);
        ReleaseRT(ref cloudA);
        ReleaseRT(ref cloudB);
        ReleaseRT(ref temperatureA);
        ReleaseRT(ref temperatureB);
        ReleaseRT(ref turbulenceA);
        ReleaseRT(ref turbulenceB);
        ReleaseRT(ref pressureA);
        ReleaseRT(ref pressureB);
        ReleaseRT(ref divergence);
        ReleaseRT(ref precipitation);
        if (statsBuffer != null)
        {
            statsBuffer.Release();
            statsBuffer.Dispose();
            statsBuffer = null;
        }
    }

    [Test]
    public void InjectSourceProducesMeasurableDensityAndVelocity()
    {
        InjectImpulse(new Vector2(0.5f, 0.08f), 0.15f, 10f, new Vector2(0f, 2f), 0.2f);

        FluidStats stats = SampleStats();
        Assert.Greater(stats.avgHumidity, 0.01f, "Average humidity should rise above zero after injection.");
        Assert.Greater(stats.avgSpeed, 0.01f, "Average speed should rise above zero after injection.");
    }

    [Test]
    public void SingleFluidStepKeepsDensityAlive()
    {
        InjectImpulse(new Vector2(0.5f, 0.08f), 0.2f, 25f, new Vector2(0.2f, 2.5f), 0.25f);
        FluidStats statsBefore = SampleStats();
        Assert.Greater(statsBefore.avgHumidity, 0.1f, "Precondition failed: initial average humidity too low.");

        RunFluidStep(0.01f, 20);

        FluidStats statsAfter = SampleStats();
        Assert.Greater(statsAfter.avgHumidity, statsBefore.avgHumidity * 0.2f, "Humidity vanished unexpectedly after one step.");
        Assert.Greater(statsAfter.avgSpeed, 0.01f, "Velocity vanished unexpectedly after one step.");
    }

    [Test]
    public void BaseSourceSustainsEnergyAcrossMultipleSteps()
    {
        for (int i = 0; i < 6; i++)
        {
            RunFluidStepWithSource(0.01f, 30);
        }

        FluidStats stats = SampleStats();
        Assert.Greater(stats.avgHumidity, 0.002f, "Continuous source should accumulate humidity.");
        Assert.Greater(stats.avgSpeed, 0.0005f, "Continuous source should maintain velocity.");
        Assert.Less(stats.avgSpeed, 0.1f, "Projection should not explode velocities.");
    }

    [Test]
    public void CondensationCreatesCloudAndBuoyancy()
    {
        ClearRTCompute(humidityA, 0.9f);
        ClearRTCompute(cloudA, 0f);
        ClearRenderTexture(velocityA);

        shader.SetFloat("_DeltaTime", 0.02f);
        ConfigureMicrophysicsDefaults(saturation: 0.5f, condensation: 8f, evaporation: 0.25f, precipitation: 0f, buoyancy: 2f);
        RunMicrophysics();

        FluidStats stats = SampleStats();
        Assert.Greater(stats.avgCloud, 0.05f, "Condensation should create visible cloud water.");
        Assert.Less(stats.avgHumidity, 0.9f, "Condensation should draw down humidity.");
        Assert.Greater(stats.avgSpeed, 0.0005f, "Latent heating should add upward velocity.");
    }

    [Test]
    public void EvaporationReturnsHumidityWhenDry()
    {
        ClearRTCompute(humidityA, 0.2f);
        ClearRTCompute(cloudA, 0.6f);
        ClearRenderTexture(velocityA);

        shader.SetFloat("_DeltaTime", 0.04f);
        ConfigureMicrophysicsDefaults(saturation: 0.6f, condensation: 0f, evaporation: 6f, precipitation: 0f, buoyancy: 0f);
        RunMicrophysics();

        FluidStats stats = SampleStats();
        Assert.Greater(stats.avgHumidity, 0.25f, "Evaporation should moisten the air when below saturation.");
        Assert.Less(stats.avgCloud, 0.55f, "Cloud water should supply the evaporation sink.");
    }

    [Test]
    public void PrecipitationReducesTotalWaterOverTime()
    {
        ClearRTCompute(humidityA, 0.55f);
        ClearRTCompute(cloudA, 1.0f);
        ClearRenderTexture(velocityA);

        shader.SetFloat("_DeltaTime", 0.03f);
        ConfigureMicrophysicsDefaults(saturation: 0.55f, condensation: 0f, evaporation: 0.2f, precipitation: 3.5f, buoyancy: 0f);
        FluidStats initial = SampleStats();

        for (int i = 0; i < 8; i++)
        {
            RunMicrophysics();
        }

        FluidStats stats = SampleStats();
        Assert.Less(stats.totalWater, initial.totalWater * 0.8f, "Precipitation should bleed water mass out of the column.");
        Assert.Less(stats.avgCloud, initial.avgCloud * 0.5f, "Cloud reservoir should shrink under heavy precipitation.");
    }

    [Test]
    public void RocketStyleColumnGeneratesPrecipitation()
    {
        ClearRTCompute(humidityA, 0.35f);
        ClearRTCompute(cloudA, 0f);
        ClearRenderTexture(velocityA);

        InjectImpulse(new Vector2(0.5f, 0.08f), 0.09f, 42f, new Vector2(0f, 3.2f), 0.18f);
        InjectImpulse(new Vector2(0.5f, 0.24f), 0.07f, 30f, new Vector2(0f, 3.4f), 0.14f);
        InjectImpulse(new Vector2(0.5f, 0.4f), 0.06f, 26f, new Vector2(0f, 3.5f), 0.12f);
        InjectImpulse(new Vector2(0.5f, 0.56f), 0.05f, 22f, new Vector2(0f, 3.6f), 0.1f);

        for (int i = 0; i < 12; i++)
        {
            RunFluidStep(0.012f, 28);
        }

        FluidStats stats = SampleStats();
        Assert.Greater(stats.avgCloud, 0.015f, "Stacked impulses should build a visible cloud column.");
        Assert.Greater(stats.avgPrecip, 0.0002f, "Rocket-style column should create measurable precipitation.");
        Assert.Greater(stats.avgSpeed, 0.002f, "Updraft should stay active throughout the column.");
    }
    private void RunFluidStep(float dt, int jacobiIterations)
    {
        shader.SetFloat("_DeltaTime", dt);
        shader.SetFloat("_VelocityDissipation", 0.995f);
        shader.SetFloat("_DensityDissipation", 0.999f);

        AdvectVelocity();
        AdvectHumidity();
        AdvectCloud();
        AdvectTemperature();
        AdvectTurbulence();
        ProjectVelocity(jacobiIterations);
        RunMicrophysics();
    }

    private void RunFluidStepWithSource(float dt, int jacobiIterations)
    {
        shader.SetFloat("_DeltaTime", dt);
        shader.SetFloat("_VelocityDissipation", 0.995f);
        shader.SetFloat("_DensityDissipation", 0.999f);

        InjectImpulse(new Vector2(0.5f, 0.05f), 0.08f, 4f, new Vector2(0f, 1.5f), dt);
        if (DumpStepStats)
        {
            FluidStats injectStats = SampleStats();
            Debug.Log($"Stats after inject: avgHumidity={injectStats.avgHumidity}, avgCloud={injectStats.avgCloud}, avgSpeed={injectStats.avgSpeed}");
        }
        AdvectVelocity();
        if (DumpStepStats)
        {
            FluidStats afterVel = SampleStats();
            Debug.Log($"Stats after advect velocity: avgHumidity={afterVel.avgHumidity}, avgCloud={afterVel.avgCloud}, avgSpeed={afterVel.avgSpeed}");
        }
        AdvectHumidity();
        AdvectCloud();
        AdvectTemperature();
        AdvectTurbulence();
        if (DumpStepStats)
        {
            FluidStats afterHumidity = SampleStats();
            Debug.Log($"Stats after advect humidity: avgHumidity={afterHumidity.avgHumidity}, avgCloud={afterHumidity.avgCloud}, avgSpeed={afterHumidity.avgSpeed}");
        }
        ProjectVelocity(jacobiIterations);
        RunMicrophysics();
        if (DumpStepStats)
        {
            FluidStats finalStats = SampleStats();
            Debug.Log($"Stats after microphysics: avgHumidity={finalStats.avgHumidity}, avgCloud={finalStats.avgCloud}, avgSpeed={finalStats.avgSpeed}");
        }
    }

    private void ConfigureMicrophysicsDefaults(float saturation = 0.55f, float condensation = 4f, float evaporation = 2f, float precipitation = 0.5f, float buoyancy = 1.5f)
    {
        shader.SetFloat("_SaturationThreshold", saturation);
        shader.SetFloat("_CondensationRate", condensation);
        shader.SetFloat("_EvaporationRate", evaporation);
        shader.SetFloat("_PrecipitationRate", precipitation);
        shader.SetFloat("_LatentHeatBuoyancy", buoyancy);
        shader.SetFloat("_TemperatureSaturationFactor", TemperatureSaturationFactor);
        shader.SetFloat("_LatentHeatTemperatureGain", LatentHeatTemperatureGain);
        shader.SetFloat("_EvaporationCoolingFactor", EvaporationCoolingFactor);
        shader.SetFloat("_TurbulencePrecipFactor", TurbulencePrecipitationFactor);
        shader.SetFloat("_TemperatureDecay", TemperatureDecay);
        shader.SetFloat("_TurbulenceDecay", TurbulenceDecay);
    }

    private void InjectImpulse(Vector2 center, float radius, float density, Vector2 velocity, float dt)
    {
        shader.SetVector("_SourceCenter", new Vector4(center.x, center.y, 0f, 0f));
        shader.SetFloat("_SourceRadius", Mathf.Max(0.001f, radius));
        shader.SetFloat("_SourceDensity", density * dt);
        shader.SetVector("_SourceVelocity", new Vector4(velocity.x * dt, velocity.y * dt, 0f, 0f));
        shader.SetFloat("_SourceFeather", 0.03f);
        shader.SetFloat("_SourceMapBlend", 0f);
        shader.SetFloat("_SourceHeat", 0f);
        shader.SetFloat("_SourceTurbulence", 0f);

        shader.SetTexture(kInject, "_Velocity", velocityA);
        shader.SetTexture(kInject, "_Humidity", humidityA);
        shader.SetTexture(kInject, "_Temperature", temperatureA);
        shader.SetTexture(kInject, "_Turbulence", turbulenceA);
        DispatchSimulation(kInject);
    }

    private void AdvectVelocity()
    {
        shader.SetTexture(kAdvectVelocity, "_VectorFieldRead", velocityA);
        shader.SetTexture(kAdvectVelocity, "_VectorFieldWrite", velocityB);
        DispatchSimulation(kAdvectVelocity);
        Swap(ref velocityA, ref velocityB);
    }

    private void AdvectHumidity()
    {
        shader.SetFloat("_DensityDissipation", 0.999f);
        shader.SetTexture(kAdvectScalar, "_ScalarFieldRead", humidityA);
        shader.SetTexture(kAdvectScalar, "_ScalarFieldWrite", humidityB);
        shader.SetTexture(kAdvectScalar, "_ScalarVelocity", velocityA);
        DispatchSimulation(kAdvectScalar);
        Swap(ref humidityA, ref humidityB);
    }

    private void AdvectCloud()
    {
        shader.SetFloat("_DensityDissipation", 0.999f);
        shader.SetTexture(kAdvectScalar, "_ScalarFieldRead", cloudA);
        shader.SetTexture(kAdvectScalar, "_ScalarFieldWrite", cloudB);
        shader.SetTexture(kAdvectScalar, "_ScalarVelocity", velocityA);
        DispatchSimulation(kAdvectScalar);
        Swap(ref cloudA, ref cloudB);
    }

    private void AdvectTemperature()
    {
        shader.SetFloat("_DensityDissipation", TemperatureDissipation);
        shader.SetTexture(kAdvectScalar, "_ScalarFieldRead", temperatureA);
        shader.SetTexture(kAdvectScalar, "_ScalarFieldWrite", temperatureB);
        shader.SetTexture(kAdvectScalar, "_ScalarVelocity", velocityA);
        DispatchSimulation(kAdvectScalar);
        Swap(ref temperatureA, ref temperatureB);
    }

    private void AdvectTurbulence()
    {
        shader.SetFloat("_DensityDissipation", TurbulenceDissipation);
        shader.SetTexture(kAdvectScalar, "_ScalarFieldRead", turbulenceA);
        shader.SetTexture(kAdvectScalar, "_ScalarFieldWrite", turbulenceB);
        shader.SetTexture(kAdvectScalar, "_ScalarVelocity", velocityA);
        DispatchSimulation(kAdvectScalar);
        Swap(ref turbulenceA, ref turbulenceB);
    }

    private void RunMicrophysics()
    {
        shader.SetTexture(kMicrophysics, "_MicroHumidity", humidityA);
        shader.SetTexture(kMicrophysics, "_MicroCloud", cloudA);
        shader.SetTexture(kMicrophysics, "_MicroVelocity", velocityA);
        shader.SetTexture(kMicrophysics, "_MicroTemperature", temperatureA);
        shader.SetTexture(kMicrophysics, "_MicroTurbulence", turbulenceA);
        shader.SetTexture(kMicrophysics, "_PrecipitationTex", precipitation);
        DispatchSimulation(kMicrophysics);
    }

    private void ProjectVelocity(int jacobiIterations)
    {
        shader.SetTexture(kDivergence, "_VelocityField", velocityA);
        shader.SetTexture(kDivergence, "_DivergenceWrite", divergence);
        DispatchSimulation(kDivergence);

        ClearRTCompute(pressureA);
        ClearRTCompute(pressureB);

        RenderTexture read = pressureA;
        RenderTexture write = pressureB;
        for (int i = 0; i < jacobiIterations; i++)
        {
            shader.SetTexture(kJacobi, "_PressureRead", read);
            shader.SetTexture(kJacobi, "_PressureWrite", write);
            shader.SetTexture(kJacobi, "_DivergenceTex", divergence);
            DispatchSimulation(kJacobi);
            Swap(ref read, ref write);
        }

        shader.SetTexture(kSubtract, "_PressureTex", read);
        shader.SetTexture(kSubtract, "_VelocityWriteGradient", velocityA);
        DispatchSimulation(kSubtract);
    }

    private FluidStats SampleStats()
    {
        shader.SetTexture(kStats, "_StatsDensityTex", humidityA);
        shader.SetTexture(kStats, "_StatsVelocityTex", velocityA);
        shader.SetTexture(kStats, "_StatsCloudTex", cloudA);
        shader.SetTexture(kStats, "_StatsPrecipTex", precipitation);
        shader.SetBuffer(kStats, "_DebugBuffer", statsBuffer);
        shader.Dispatch(kStats, 1, 1, 1);

        float[] data = new float[12];
        statsBuffer.GetData(data);
        return new FluidStats
        {
            avgHumidity = data[0],
            maxHumidity = data[1],
            avgSpeed = data[2],
            maxSpeed = data[3],
            avgCloud = data[4],
            maxCloud = data[5],
            totalWater = data[6],
            cellCount = Mathf.Max(1f, data[7]),
            avgPrecip = data[8],
            maxPrecip = data[9],
            precipSum = data[10]
        };
    }

    private void DispatchSimulation(int kernel)
    {
        shader.Dispatch(kernel, dispatch.x, dispatch.y, 1);
    }

    private void ClearRTCompute(RenderTexture target, float value = 0f)
    {
        shader.SetTexture(kClear, "_ClearScalar", target);
        shader.SetFloat("_ClearValue", value);
        DispatchSimulation(kClear);
    }

    private RenderTexture CreateVectorRT()
    {
        return CreateRT(RenderTextureFormat.RGFloat);
    }

    private RenderTexture CreateScalarRT(RenderTextureFormat format = RenderTextureFormat.RFloat)
    {
        return CreateRT(format);
    }

    private RenderTexture CreateRT(RenderTextureFormat format)
    {
        var rt = new RenderTexture(Resolution, Resolution, 0, format)
        {
            enableRandomWrite = true,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        rt.Create();
        return rt;
    }

    private void ClearRenderTexture(RenderTexture target, Color? color = null)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        Color clear = color ?? Color.clear;
        GL.Clear(true, true, clear);
        RenderTexture.active = previous;
    }

    private void ReleaseRT(ref RenderTexture rt)
    {
        if (rt == null)
            return;
        rt.Release();
        Object.DestroyImmediate(rt);
        rt = null;
    }

    private static void Swap(ref RenderTexture a, ref RenderTexture b)
    {
        var temp = a;
        a = b;
        b = temp;
    }
}
