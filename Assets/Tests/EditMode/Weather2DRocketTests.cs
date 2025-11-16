using NUnit.Framework;
using UnityEngine;

public class Weather2DRocketTests
{
    [Test]
    public void TriggerRocketSequenceBuildsCloudAndRain()
    {
        var go = new GameObject("Weather2D Test Harness");
        var weather = go.AddComponent<Weather2D>();
        weather.enableMouseInput = false;
        weather.precipitationFeedback = 0f;
        weather.SetBaseSourceActive(false);
        weather.ResetSimulation();
        weather.ClearScriptedBursts();

        var bursts = new[]
        {
            new Weather2D.Burst { position = new Vector2(0.5f, 0.08f), radius = 0.09f, density = 36f, velocity = new Vector2(0f, 3.1f) },
            new Weather2D.Burst { position = new Vector2(0.5f, 0.22f), radius = 0.07f, density = 30f, velocity = new Vector2(0f, 3.3f) },
            new Weather2D.Burst { position = new Vector2(0.5f, 0.36f), radius = 0.06f, density = 26f, velocity = new Vector2(0f, 3.4f) },
            new Weather2D.Burst { position = new Vector2(0.5f, 0.52f), radius = 0.05f, density = 22f, velocity = new Vector2(0f, 3.6f) }
        };

        float baselineHumidity = weather.LatestAvgHumidity;

        weather.TriggerRocketBurst(new Weather2D.Burst
        {
            position = new Vector2(0.5f, 0.05f),
            radius = 0.22f,
            density = 60f,
            velocity = new Vector2(0f, 2.3f)
        }, 0f, 0.6f);

        weather.TriggerRocketBoost(1.5f, 2.5f, 3f);
        weather.TriggerRocketSequence(bursts, 0.35f, 0.12f, 0.22f);
        weather.StepSimulation(0.01f, 700);

        Assert.Greater(weather.LatestAvgHumidity, baselineHumidity + 0.0005f, "Rocket-triggered bursts should moisten the column measurably.");
        Assert.Greater(weather.LatestAvgSpeed, 0.0005f, "Rocket sequence should energize the flow and create an updraft.");
        Assert.AreEqual(0, weather.PendingRocketBurstCount, "All scripted rocket bursts should be consumed after the simulation run.");

        Object.DestroyImmediate(go);
    }
}
