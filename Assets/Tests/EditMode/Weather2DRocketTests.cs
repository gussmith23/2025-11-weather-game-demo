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

        weather.TriggerRocketBurst(new Weather2D.Burst
        {
            position = new Vector2(0.4f, 0.16f),
            radius = 0.15f,
            density = 32f,
            velocity = new Vector2(0.6f, 2.6f)
        }, 0f, 0.5f);

        weather.TriggerRocketBurst(new Weather2D.Burst
        {
            position = new Vector2(0.6f, 0.16f),
            radius = 0.15f,
            density = 32f,
            velocity = new Vector2(-0.6f, 2.6f)
        }, 0f, 0.5f);

        weather.StepSimulation(0.01f, 240);
        float baselinePrecip = weather.LatestAvgPrecip;
        float baselineCloud = weather.LatestAvgCloud;
        float baselineHumidity = weather.LatestAvgHumidity;

        weather.TriggerRocketSequence(bursts, 0.2f, 0.1f, 0.18f);
        weather.TriggerRocketBoost(1.0f, 3.5f, 5f);
        weather.TriggerRocketBurst(new Weather2D.Burst
        {
            position = new Vector2(0.5f, 0.72f),
            radius = 0.05f,
            density = 0f,
            velocity = Vector2.zero,
            heat = 10f,
            turbulence = 2.5f
        }, 0.6f, 0.22f);
        weather.StepSimulation(0.01f, 520);

        Assert.Less(weather.LatestAvgCloud, baselineCloud * 0.85f, "Rocket detonation should reduce cloud water compared to baseline.");
        Assert.Less(weather.LatestAvgHumidity, baselineHumidity * 0.95f, "Rocket detonation should dry out the column relative to baseline.");
        Assert.Greater(weather.LatestAvgSpeed, 0.0005f, "Rocket sequence should energize the flow.");
        Assert.AreEqual(0, weather.PendingRocketBurstCount, "All scripted rocket bursts should be consumed after the simulation run.");

        Object.DestroyImmediate(go);
    }
}
