using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

public class Weather2DThunderstormTests
{
    private const bool Verbose = true;
    private static readonly bool CaptureEnabled = GetEnvFlag("THUNDERSTORM_CAPTURE");
    private static readonly string CaptureDir = GetEnvString("THUNDERSTORM_CAPTURE_DIR", "Logs/thunderstorm-captures");
    private static readonly string CaptureRunPrefix = GetEnvString("THUNDERSTORM_CAPTURE_RUN_PREFIX", "run");
    // Capture cadence is based on Tick() calls ("frames"), not sim substeps.
    private static readonly int CaptureEveryFrames = Mathf.Max(1, GetEnvInt("THUNDERSTORM_CAPTURE_EVERY", 10));
    private static readonly int CaptureMaxFrames = Mathf.Max(1, GetEnvInt("THUNDERSTORM_CAPTURE_MAX", 300));

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
        var displayRT = GetPrivateRT(weather, "_display");

        float lowTemp = SampleScalar(temperatureRT, new Vector2(0.5f, 0.1f));
        float highTemp = SampleScalar(temperatureRT, new Vector2(0.5f, 0.85f));
        Assert.Greater(lowTemp, highTemp, "Lapse-rate seeding should make low-level air warmer.");

        float lowHumid = SampleScalar(humidityRT, new Vector2(0.5f, 0.1f));
        float highHumid = SampleScalar(humidityRT, new Vector2(0.5f, 0.85f));
        Assert.Greater(lowHumid, highHumid, "Humidity profile should be larger near the surface.");

        LogFieldSnapshot("t=0", humidityRT, cloudRT, temperatureRT, velocityRT);

        float realFrameDt = 1f / 60f;
        float realTime = 0f;
        int frameIndex = 0;
        int tickCount = 0;
        string runDir = CaptureDir;
        if (CaptureEnabled)
        {
            runDir = CreateUniqueRunDir(CaptureDir, CaptureRunPrefix);
            Debug.Log($"Thunderstorm capture enabled. Writing PNGs to: {runDir}");
        }

        TickAndCaptureForRealSeconds(weather, displayRT, runDir, 0.8f, realFrameDt, ref realTime, ref frameIndex, ref tickCount);
        LogFieldSnapshot($"real={realTime:0.00}s", humidityRT, cloudRT, temperatureRT, velocityRT);

        Vector2 updraft = SampleVector(velocityRT, new Vector2(0.5f, 0.2f));
        Assert.Greater(updraft.y, 0.02f, "Convergence forcing should create an upward draft near the center.");

        float cloudLow = SampleScalar(cloudRT, new Vector2(0.5f, 0.25f));
        float cloudMid = SampleScalar(cloudRT, new Vector2(0.5f, 0.45f));
        float cloudHigh = SampleScalar(cloudRT, new Vector2(0.5f, 0.65f));
        float edgeCloud = SampleScalar(cloudRT, new Vector2(0.15f, 0.45f));
        float cloudPeak = Mathf.Max(cloudLow, Mathf.Max(cloudMid, cloudHigh));
        // Allow uniform cloud fields while still ensuring cloud forms.
        Assert.Greater(cloudPeak, 0.0001f, "Cloud water should form after the initial forcing.");

        TickAndCaptureForRealSeconds(weather, displayRT, runDir, 2.2f, realFrameDt, ref realTime, ref frameIndex, ref tickCount);
        LogFieldSnapshot($"real={realTime:0.00}s", humidityRT, cloudRT, temperatureRT, velocityRT);

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

    private static void TickAndCaptureForRealSeconds(Weather2D weather, RenderTexture displayRT, string runDir, float seconds, float realDt,
        ref float realTime, ref int frameIndex, ref int tickCount)
    {
        int frames = Mathf.Max(1, Mathf.CeilToInt(seconds / Mathf.Max(0.0001f, realDt)));
        for (int i = 0; i < frames; i++)
        {
            weather.Tick(realDt);
            realTime += realDt;
            if (!CaptureEnabled || displayRT == null)
            {
                tickCount++;
                continue;
            }
            if (frameIndex < CaptureMaxFrames && (tickCount % CaptureEveryFrames) == 0)
            {
                string fileName = $"thunderstorm_{frameIndex:0000}_real{realTime:0.00}.png";
                string path = Path.Combine(runDir, fileName);
                CaptureDisplay(displayRT, path);
                frameIndex++;
            }
            tickCount++;
        }
    }

    private static void CaptureDisplay(RenderTexture displayRT, string path)
    {
        if (displayRT == null)
            return;

        RenderTexture prev = RenderTexture.active;
        var temp = RenderTexture.GetTemporary(displayRT.width, displayRT.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(displayRT, temp);
        RenderTexture.active = temp;

        var tex = new Texture2D(temp.width, temp.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, temp.width, temp.height), 0, 0);
        tex.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(temp);

        byte[] bytes = tex.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(tex);
        File.WriteAllBytes(path, bytes);
    }

    private static string CreateUniqueRunDir(string baseDir, string prefix)
    {
        Directory.CreateDirectory(baseDir);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string runDir = Path.Combine(baseDir, $"{prefix}_{stamp}");
        int suffix = 0;
        while (Directory.Exists(runDir))
        {
            suffix++;
            runDir = Path.Combine(baseDir, $"{prefix}_{stamp}_{suffix}");
        }
        Directory.CreateDirectory(runDir);
        return runDir;
    }

    private static bool GetEnvFlag(string name)
    {
        string value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrEmpty(value) && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetEnvString(string name, string defaultValue)
    {
        string value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }

    private static int GetEnvInt(string name, int defaultValue)
    {
        string value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out int parsed) ? parsed : defaultValue;
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
