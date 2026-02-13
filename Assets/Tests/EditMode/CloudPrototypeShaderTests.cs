using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

public class CloudPrototypeShaderTests
{
  private readonly struct CaptureLayer
  {
    public CaptureLayer(string key, int debugMode)
    {
      Key = key;
      DebugMode = debugMode;
    }

    public string Key { get; }
    public int DebugMode { get; }
  }

  private static readonly bool CaptureEnabled = GetEnvFlag("CLOUD_CAPTURE");
  private static readonly string CaptureDir = GetEnvString("CLOUD_CAPTURE_DIR", "Logs/cloud-prototype-captures");
  private static readonly string CaptureRunPrefix = GetEnvString("CLOUD_CAPTURE_RUN_PREFIX", "run");
  private static readonly int CaptureEveryFrames = Mathf.Max(1, GetEnvInt("CLOUD_CAPTURE_EVERY", 5));
  private static readonly int CaptureMaxFrames = Mathf.Max(1, GetEnvInt("CLOUD_CAPTURE_MAX", 60));
  private static readonly int Width = Mathf.Max(64, GetEnvInt("CLOUD_CAPTURE_WIDTH", 1024));
  private static readonly int Height = Mathf.Max(64, GetEnvInt("CLOUD_CAPTURE_HEIGHT", 512));

  [Test]
  public void CloudPrototypeShader_RendersAndOptionallyCaptures()
  {
    if (!SystemInfo.supportsAsyncGPUReadback)
      Assert.Ignore("AsyncGPUReadback not supported; skipping shader snapshot test.");

    Shader shader = Shader.Find("Hidden/CloudPrototype");
    Assert.IsNotNull(shader, "CloudPrototype shader not found (Shader.Find failed).");
    var mat = new Material(shader);

    IReadOnlyList<CaptureLayer> layers = ParseCaptureLayers(GetEnvString("CLOUD_CAPTURE_LAYERS", "final"));

    string runDir = CaptureDir;
    var layerDirs = new Dictionary<string, string>();
    if (CaptureEnabled)
    {
      runDir = CreateUniqueRunDir(CaptureDir, CaptureRunPrefix);
      foreach (CaptureLayer layer in layers)
      {
        string dir = Path.Combine(runDir, layer.Key);
        Directory.CreateDirectory(dir);
        layerDirs[layer.Key] = dir;
      }
      Debug.Log($"Cloud prototype capture enabled. Writing PNGs to: {runDir}");
    }

    var rt = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32)
    {
      enableRandomWrite = false,
      wrapMode = TextureWrapMode.Clamp,
      filterMode = FilterMode.Bilinear
    };
    rt.Create();

    float t = 0f;
    float dt = 1f / 30f;
    int frames = Mathf.Min(240, CaptureMaxFrames * CaptureEveryFrames);
    int frameIndex = 0;
    for (int i = 0; i < frames; i++)
    {
      t += dt;

      if (CaptureEnabled && (i % CaptureEveryFrames) == 0 && frameIndex < CaptureMaxFrames)
      {
        foreach (CaptureLayer layer in layers)
        {
          RenderLayer(mat, rt, t, layer.DebugMode);
          string name = $"cloud_{frameIndex:0000}_t{t:0.00}.png";
          string path = Path.Combine(layerDirs[layer.Key], name);
          CaptureRenderTexture(rt, path);
        }
        frameIndex++;
      }
      else
      {
        RenderLayer(mat, rt, t, 0);
      }
    }

    RenderLayer(mat, rt, t, 0);
    float variance = EstimateVariance(rt);
    Assert.Greater(variance, 1e-4f, $"Expected non-trivial render; variance too low: {variance}");

    float rainStrength = mat.GetFloat("_RainStrength");
    if (rainStrength > 0.001f)
    {
      RenderLayer(mat, rt, 1.5f, 2);
      float rainVariance = EstimateVariance(rt);
      Assert.Greater(rainVariance, 1e-5f, $"Expected rain mask variance at t=1.5s; got {rainVariance}");
    }

    UnityEngine.Object.DestroyImmediate(mat);
    rt.Release();
    UnityEngine.Object.DestroyImmediate(rt);
  }

  private static void RenderLayer(Material mat, RenderTexture rt, float timeSeconds, int debugMode)
  {
    mat.SetFloat("_TimeSeconds", timeSeconds);
    mat.SetFloat("_DebugMode", debugMode);
    Graphics.Blit(null, rt, mat);
  }

  private static bool ContainsLayer(IReadOnlyList<CaptureLayer> layers, string key)
  {
    for (int i = 0; i < layers.Count; i++)
    {
      if (string.Equals(layers[i].Key, key, StringComparison.OrdinalIgnoreCase))
        return true;
    }
    return false;
  }

  private static IReadOnlyList<CaptureLayer> ParseCaptureLayers(string raw)
  {
    var layers = new List<CaptureLayer>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    string[] tokens = (raw ?? "final").Split(',');

    for (int i = 0; i < tokens.Length; i++)
    {
      string token = tokens[i].Trim().ToLowerInvariant();
      if (string.IsNullOrEmpty(token))
        continue;

      CaptureLayer layer;
      switch (token)
      {
        case "final":
          layer = new CaptureLayer("final", 0);
          break;
        case "mask_density":
        case "density":
          layer = new CaptureLayer("mask_density", 1);
          break;
        case "mask_rain":
        case "rain":
          layer = new CaptureLayer("mask_rain", 2);
          break;
        default:
          continue;
      }

      if (seen.Add(layer.Key))
      {
        layers.Add(layer);
      }
    }

    if (layers.Count == 0)
    {
      layers.Add(new CaptureLayer("final", 0));
    }

    return layers;
  }

  private static void CaptureRenderTexture(RenderTexture rt, string path)
  {
    var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
    req.WaitForCompletion();
    if (req.hasError)
      return;

    var data = req.GetData<Color32>();
    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
    tex.SetPixels32(data.ToArray());
    tex.Apply();
    byte[] bytes = tex.EncodeToPNG();
    UnityEngine.Object.DestroyImmediate(tex);
    File.WriteAllBytes(path, bytes);
  }

  private static float EstimateVariance(RenderTexture rt)
  {
    var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
    req.WaitForCompletion();
    if (req.hasError)
      return 0f;

    var data = req.GetData<Color32>();
    int count = data.Length;
    int step = Mathf.Max(1, count / 4096);
    double sum = 0.0;
    double sumSq = 0.0;
    int n = 0;
    for (int i = 0; i < count; i += step)
    {
      Color32 c = data[i];
      double v = (0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b) / 255.0;
      sum += v;
      sumSq += v * v;
      n++;
    }
    if (n <= 1)
      return 0f;

    double mean = sum / n;
    return (float)((sumSq / n) - (mean * mean));
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
}
