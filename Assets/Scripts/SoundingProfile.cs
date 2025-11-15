using UnityEngine;

[CreateAssetMenu(menuName = "Weather/Sounding Profile", fileName = "SoundingProfile")]
public class SoundingProfile : ScriptableObject
{
    [Header("Moisture & Thermodynamics")]
    [Range(0.1f, 1.5f)] public float saturationThreshold = 0.6f;
    [Range(0f, 10f)] public float condensationRate = 4f;
    [Range(0f, 10f)] public float evaporationRate = 2f;
    [Range(0f, 5f)] public float precipitationRate = 0.5f;
    [Range(0f, 10f)] public float latentHeatBuoyancy = 1.5f;

    [Header("Source Parameters")]
    public float baseSourceDensity = 22f;
    public float baseSourceRadius = 0.16f;
    [Range(0f, 1f)] public float baseSourceHeight = 0.08f;
    public Vector2 windShear = new Vector2(0.2f, 1.8f);
    [Range(0.9f, 1f)] public float densityDissipation = 0.999f;
    [Range(0.9f, 1f)] public float velocityDissipation = 0.995f;

    [Header("External Forcing")]
    [Range(0.1f, 2f)] public float timeScale = 1f;
    public Texture2D surfaceMoisture;
}
