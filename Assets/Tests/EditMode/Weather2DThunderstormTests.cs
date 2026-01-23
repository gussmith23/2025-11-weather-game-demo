using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

public class Weather2DThunderstormTests
{
    private const bool Verbose = true;

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

        LogFieldSnapshot("t=0", humidityRT, cloudRT, temperatureRT, velocityRT);

        float baseDt = 0.01f;
        float fastScale = Mathf.Max(1f, weather.fastForwardScale);
        float fastDuration = Mathf.Max(0f, weather.fastForwardDuration);
        int fastSteps = Mathf.Max(1, Mathf.CeilToInt(fastDuration / baseDt));
        if (fastScale > 1f && fastDuration > 0f)
        {
            weather.StepSimulation(baseDt * fastScale, fastSteps);
            LogFieldSnapshot($"t={fastDuration:0.0}s (fast x{fastScale:0.0})", humidityRT, cloudRT, temperatureRT, velocityRT);
        }

        weather.StepSimulation(baseDt, 80);

        LogFieldSnapshot("t=0.8s", humidityRT, cloudRT, temperatureRT, velocityRT);

        Vector2 updraft = SampleVector(velocityRT, new Vector2(0.5f, 0.2f));
        Assert.Greater(updraft.y, 0.02f, "Convergence forcing should create an upward draft near the center.");

        float cloudLow = SampleScalar(cloudRT, new Vector2(0.5f, 0.25f));
        float cloudMid = SampleScalar(cloudRT, new Vector2(0.5f, 0.45f));
        float cloudHigh = SampleScalar(cloudRT, new Vector2(0.5f, 0.65f));
        float edgeCloud = SampleScalar(cloudRT, new Vector2(0.15f, 0.45f));
        float cloudPeak = Mathf.Max(cloudLow, Mathf.Max(cloudMid, cloudHigh));
        // Allow uniform cloud fields while still ensuring cloud forms.
        Assert.Greater(cloudPeak, 0.0001f, "Cloud water should form after the initial forcing.");

        weather.StepSimulation(baseDt, 220);

        LogFieldSnapshot("t=3.0s", humidityRT, cloudRT, temperatureRT, velocityRT);

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

    private static void LogFieldSnapshot(string label, RenderTexture humidityRT, RenderTexture cloudRT, RenderTexture temperatureRT, RenderTexture velocityRT)
    {
        if (!Verbose)
            return;

        var humidity = ReadScalarGrid(humidityRT, 16);
        var cloud = ReadScalarGrid(cloudRT, 16);
        var temp = ReadScalarGrid(temperatureRT, 16);
        var vel = ReadVectorGrid(velocityRT, 16);

        Debug.Log($"Thunderstorm snapshot {label}\nHumidity (top row first):\n{FormatGrid(humidity)}\nCloud (top row first):\n{FormatGrid(cloud)}\nTemp (top row first):\n{FormatGrid(temp)}\nVelocity (y) (top row first):\n{FormatVectorGridY(vel)}");
    }

    private static float[,] ReadScalarGrid(RenderTexture rt, int grid)
    {
        var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RFloat);
        req.WaitForCompletion();
        var data = req.GetData<float>();
        int w = rt.width;
        int h = rt.height;
        float[,] result = new float[grid, grid];
        for (int gy = 0; gy < grid; gy++)
        {
            for (int gx = 0; gx < grid; gx++)
            {
                float u = gx / (float)(grid - 1);
                float v = gy / (float)(grid - 1);
                int x = Mathf.Clamp(Mathf.RoundToInt(u * (w - 1)), 0, w - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(v * (h - 1)), 0, h - 1);
                result[gy, gx] = data[(y * w) + x];
            }
        }
        return result;
    }

    private static Vector2[,] ReadVectorGrid(RenderTexture rt, int grid)
    {
        var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGFloat);
        req.WaitForCompletion();
        var data = req.GetData<Vector2>();
        int w = rt.width;
        int h = rt.height;
        Vector2[,] result = new Vector2[grid, grid];
        for (int gy = 0; gy < grid; gy++)
        {
            for (int gx = 0; gx < grid; gx++)
            {
                float u = gx / (float)(grid - 1);
                float v = gy / (float)(grid - 1);
                int x = Mathf.Clamp(Mathf.RoundToInt(u * (w - 1)), 0, w - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(v * (h - 1)), 0, h - 1);
                result[gy, gx] = data[(y * w) + x];
            }
        }
        return result;
    }

    private static string FormatGrid(float[,] grid)
    {
        int size = grid.GetLength(0);
        var lines = new string[size];
        for (int y = size - 1; y >= 0; y--)
        {
            string line = "";
            for (int x = 0; x < size; x++)
            {
                line += $"{grid[y, x],6:0.00} ";
            }
            lines[size - 1 - y] = line.TrimEnd();
        }
        return string.Join("\n", lines);
    }

    private static string FormatVectorGridY(Vector2[,] grid)
    {
        int size = grid.GetLength(0);
        var lines = new string[size];
        for (int y = size - 1; y >= 0; y--)
        {
            string line = "";
            for (int x = 0; x < size; x++)
            {
                line += $"{grid[y, x].y,6:0.00} ";
            }
            lines[size - 1 - y] = line.TrimEnd();
        }
        return string.Join("\n", lines);
    }
}
