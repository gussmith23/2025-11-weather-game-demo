using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

public class Weather2DThunderstormTests
{
    [Test]
    public void ThunderstormDemoBuildsUpdraftAndCloud()
    {
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            Assert.Ignore("AsyncGPUReadback not supported; skipping thunderstorm test.");
        }

        var go = new GameObject("Weather2D Thunderstorm Test");
        var weather = go.AddComponent<Weather2D>();
        weather.enableMouseInput = false;
        weather.precipitationFeedback = 0f;

        weather.ResetSimulation();

        int demoIndex = FindDemoIndex(weather, "Thunderstorm");
        Assert.GreaterOrEqual(demoIndex, 0, "Thunderstorm demo must exist.");
        weather.ApplyDemo(demoIndex);

        var humidityRT = GetPrivateRT(weather, "_humidityA");
        var cloudRT = GetPrivateRT(weather, "_cloudA");
        var temperatureRT = GetPrivateRT(weather, "_temperatureA");
        var velocityRT = GetPrivateRT(weather, "_velocityA");

        float lowTemp = SampleScalar(temperatureRT, new Vector2(0.5f, 0.1f));
        float highTemp = SampleScalar(temperatureRT, new Vector2(0.5f, 0.85f));
        Assert.Greater(lowTemp, highTemp, "Lapse-rate seeding should make low-level air warmer.");

        float lowHumid = SampleScalar(humidityRT, new Vector2(0.5f, 0.1f));
        float highHumid = SampleScalar(humidityRT, new Vector2(0.5f, 0.85f));
        Assert.Greater(lowHumid, highHumid, "Humidity profile should be larger near the surface.");

        weather.StepSimulation(0.01f, 80);

        Vector2 updraft = SampleVector(velocityRT, new Vector2(0.5f, 0.2f));
        Assert.Greater(updraft.y, 0.02f, "Convergence forcing should create an upward draft near the center.");

        float cloudLow = SampleScalar(cloudRT, new Vector2(0.5f, 0.25f));
        float cloudMid = SampleScalar(cloudRT, new Vector2(0.5f, 0.45f));
        float cloudHigh = SampleScalar(cloudRT, new Vector2(0.5f, 0.65f));
        float edgeCloud = SampleScalar(cloudRT, new Vector2(0.15f, 0.45f));
        float cloudPeak = Mathf.Max(cloudLow, Mathf.Max(cloudMid, cloudHigh));
        Assert.Greater(cloudPeak, edgeCloud, "Cloud water should concentrate near the convergent updraft.");
        Assert.Greater(cloudPeak, 0.001f, "Cloud water should form after the initial forcing.");

        weather.StepSimulation(0.01f, 220);

        float precipAvg = weather.LatestAvgPrecip;
        Assert.Greater(precipAvg, 0f, "Thunderstorm should generate precipitation over time.");

        UnityEngine.Object.DestroyImmediate(go);
    }

    private static int FindDemoIndex(Weather2D weather, string demoName)
    {
        var field = typeof(Weather2D).GetField("demoScenarios", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
            return -1;

        var scenarios = field.GetValue(weather) as Array;
        if (scenarios == null)
            return -1;

        for (int i = 0; i < scenarios.Length; i++)
        {
            var scenario = scenarios.GetValue(i);
            var nameField = scenario.GetType().GetField("name");
            if (nameField == null)
                continue;
            var value = nameField.GetValue(scenario) as string;
            if (value == demoName)
                return i;
        }

        return -1;
    }

    private static RenderTexture GetPrivateRT(Weather2D weather, string fieldName)
    {
        var field = typeof(Weather2D).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        return field != null ? field.GetValue(weather) as RenderTexture : null;
    }

    private static float SampleScalar(RenderTexture rt, Vector2 uv)
    {
        if (rt == null)
            return 0f;
        var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RFloat);
        req.WaitForCompletion();
        if (req.hasError)
            return 0f;
        var data = req.GetData<float>();
        int x = Mathf.Clamp(Mathf.RoundToInt(uv.x * (rt.width - 1)), 0, rt.width - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(uv.y * (rt.height - 1)), 0, rt.height - 1);
        return data[(y * rt.width) + x];
    }

    private static Vector2 SampleVector(RenderTexture rt, Vector2 uv)
    {
        if (rt == null)
            return Vector2.zero;
        var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGFloat);
        req.WaitForCompletion();
        if (req.hasError)
            return Vector2.zero;
        var data = req.GetData<Vector2>();
        int x = Mathf.Clamp(Mathf.RoundToInt(uv.x * (rt.width - 1)), 0, rt.width - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(uv.y * (rt.height - 1)), 0, rt.height - 1);
        return data[(y * rt.width) + x];
    }
}
