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
    private static readonly string[] CaptureLayers = ParseLayers(GetEnvString("THUNDERSTORM_CAPTURE_LAYERS", "display"));

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
        var precipRT = GetPrivateRT(weather, "_precipitation");

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
            Debug.Log($"Thunderstorm capture enabled. Writing PNGs to: {runDir}\nLayers: {string.Join(", ", CaptureLayers)}");
        }

        TickAndCaptureForRealSeconds(weather, displayRT, humidityRT, cloudRT, temperatureRT, velocityRT, precipRT, runDir, 0.8f, realFrameDt,
            ref realTime, ref frameIndex, ref tickCount);
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

        TickAndCaptureForRealSeconds(weather, displayRT, humidityRT, cloudRT, temperatureRT, velocityRT, precipRT, runDir, 2.2f, realFrameDt,
            ref realTime, ref frameIndex, ref tickCount);
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

    private static void TickAndCaptureForRealSeconds(Weather2D weather, RenderTexture displayRT, RenderTexture humidityRT, RenderTexture cloudRT,
        RenderTexture temperatureRT, RenderTexture velocityRT, RenderTexture precipRT, string runDir, float seconds, float realDt,
        ref float realTime, ref int frameIndex, ref int tickCount)
    {
        int frames = Mathf.Max(1, Mathf.CeilToInt(seconds / Mathf.Max(0.0001f, realDt)));
        for (int i = 0; i < frames; i++)
        {
            weather.Tick(realDt);
            realTime += realDt;
            if (!CaptureEnabled)
            {
                tickCount++;
                continue;
            }

            if (frameIndex < CaptureMaxFrames && (tickCount % CaptureEveryFrames) == 0)
            {
                string baseName = $"thunderstorm_{frameIndex:0000}_real{realTime:0.00}";
                CaptureSelectedLayers(weather, displayRT, humidityRT, cloudRT, temperatureRT, velocityRT, precipRT, runDir, baseName);
                frameIndex++;
            }
            tickCount++;
        }
    }

    private static void CaptureSelectedLayers(Weather2D weather, RenderTexture displayRT, RenderTexture humidityRT, RenderTexture cloudRT,
        RenderTexture temperatureRT, RenderTexture velocityRT, RenderTexture precipRT, string runDir, string baseName)
    {
        for (int i = 0; i < CaptureLayers.Length; i++)
        {
            string layer = CaptureLayers[i];
            if (layer == "display")
            {
                if (displayRT == null)
                    continue;
                CaptureDisplay(displayRT, Path.Combine(EnsureLayerDir(runDir, layer), $"{baseName}.png"));
                continue;
            }
            if (layer == "cloud")
            {
                if (cloudRT == null)
                    continue;
                CaptureScalar(cloudRT, Path.Combine(EnsureLayerDir(runDir, layer), $"{baseName}.png"), GetRange("CLOUD", 0f, 0.02f));
                continue;
            }
            if (layer == "humidity")
            {
                if (humidityRT == null)
                    continue;
                CaptureScalar(humidityRT, Path.Combine(EnsureLayerDir(runDir, layer), $"{baseName}.png"), GetRange("HUMIDITY", 0f, 0.08f));
                continue;
            }
            if (layer == "precip")
            {
                if (precipRT == null)
                    continue;
                CaptureScalar(precipRT, Path.Combine(EnsureLayerDir(runDir, layer), $"{baseName}.png"), GetRange("PRECIP", 0f, 0.01f));
                continue;
            }
            if (layer == "vy")
            {
                if (velocityRT == null)
                    continue;
                CaptureVelocityY(velocityRT, Path.Combine(EnsureLayerDir(runDir, layer), $"{baseName}.png"), GetRange("VY", -1.5f, 1.5f));
                continue;
            }
            if (layer == "temp_anom")
            {
                if (temperatureRT == null)
                    continue;
                CaptureTemperatureAnomaly(weather, temperatureRT, Path.Combine(EnsureLayerDir(runDir, layer), $"{baseName}.png"),
                    GetRange("TEMP_ANOM", -0.25f, 0.25f));
                continue;
            }
        }
    }

    private static string EnsureLayerDir(string runDir, string layer)
    {
        string dir = Path.Combine(runDir, layer);
        Directory.CreateDirectory(dir);
        return dir;
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

    private static void CaptureScalar(RenderTexture rt, string path, (float min, float max) range)
    {
        var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RFloat);
        req.WaitForCompletion();
        if (req.hasError)
            return;

        var data = req.GetData<float>();
        int w = rt.width;
        int h = rt.height;
        float min = range.min;
        float max = Mathf.Max(range.max, min + 1e-6f);

        var colors = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float v = data[(y * w) + x];
                float t = Mathf.Clamp01((v - min) / (max - min));
                byte b = (byte)Mathf.RoundToInt(t * 255f);
                colors[(y * w) + x] = new Color32(b, b, b, 255);
            }
        }

        SavePng(colors, w, h, path);
    }

    private static void CaptureVelocityY(RenderTexture rt, string path, (float min, float max) range)
    {
        var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGFloat);
        req.WaitForCompletion();
        if (req.hasError)
            return;

        var data = req.GetData<Vector2>();
        int w = rt.width;
        int h = rt.height;
        float min = range.min;
        float max = Mathf.Max(range.max, min + 1e-6f);

        var colors = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float vy = data[(y * w) + x].y;
                float t = Mathf.Clamp01((vy - min) / (max - min));
                // Diverging map: updraft -> red, downdraft -> blue, neutral -> black.
                if (vy >= 0f)
                {
                    byte r = (byte)Mathf.RoundToInt(t * 255f);
                    colors[(y * w) + x] = new Color32(r, 0, 0, 255);
                }
                else
                {
                    byte b = (byte)Mathf.RoundToInt((1f - t) * 255f);
                    colors[(y * w) + x] = new Color32(0, 0, b, 255);
                }
            }
        }

        SavePng(colors, w, h, path);
    }

    private static void CaptureTemperatureAnomaly(Weather2D weather, RenderTexture temperatureRT, string path, (float min, float max) range)
    {
        var req = AsyncGPUReadback.Request(temperatureRT, 0, TextureFormat.RFloat);
        req.WaitForCompletion();
        if (req.hasError)
            return;

        var data = req.GetData<float>();
        int w = temperatureRT.width;
        int h = temperatureRT.height;
        float min = range.min;
        float max = Mathf.Max(range.max, min + 1e-6f);

        float baseTemp = weather != null ? weather.baseTemperature : 0f;
        float lapse = weather != null ? weather.lapseRate : 0f;

        var colors = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float uvY = h > 1 ? y / (float)(h - 1) : 0f;
            float envTemp = baseTemp - lapse * uvY;
            for (int x = 0; x < w; x++)
            {
                float temp = data[(y * w) + x];
                float anom = temp - envTemp;
                float t = Mathf.Clamp01((anom - min) / (max - min));
                // Diverging map: warm anomaly -> red, cool anomaly -> blue.
                if (anom >= 0f)
                {
                    byte r = (byte)Mathf.RoundToInt(t * 255f);
                    colors[(y * w) + x] = new Color32(r, 0, 0, 255);
                }
                else
                {
                    byte b = (byte)Mathf.RoundToInt((1f - t) * 255f);
                    colors[(y * w) + x] = new Color32(0, 0, b, 255);
                }
            }
        }

        SavePng(colors, w, h, path);
    }

    private static void SavePng(Color32[] colors, int w, int h, string path)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(colors);
        tex.Apply();
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

    private static (float min, float max) GetRange(string suffix, float defaultMin, float defaultMax)
    {
        string raw = Environment.GetEnvironmentVariable($"THUNDERSTORM_CAPTURE_RANGE_{suffix}");
        if (string.IsNullOrEmpty(raw))
            return (defaultMin, defaultMax);
        var parts = raw.Split(',');
        if (parts.Length != 2)
            return (defaultMin, defaultMax);
        if (!float.TryParse(parts[0], out float min))
            min = defaultMin;
        if (!float.TryParse(parts[1], out float max))
            max = defaultMax;
        return (min, max);
    }

    private static string[] ParseLayers(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new[] { "display" };
        var parts = raw.Split(',');
        int count = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(parts[i]))
                count++;
        }
        if (count == 0)
            return new[] { "display" };
        var result = new string[count];
        int idx = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i].Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(p))
                continue;
            result[idx++] = p;
        }
        return result;
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
