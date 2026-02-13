using UnityEngine;

// Play-mode harness for the CloudPrototype shader. This is intentionally separate from Weather2D.
public class CloudPrototypeController : MonoBehaviour
{
  [Header("Target")]
  public Renderer targetRenderer;

  [Header("Shader")]
  public Shader cloudShader;

  [Header("Playback")]
  public bool animateTime = true;
  [Range(0f, 120f)]
  public float timeSeconds = 0f;
  public float timeScale = 1f;

  public bool useAutoFastForward = true;
  public bool matchFastForwardToFormation = true;
  public float fastForwardScale = 8f;
  public float normalScale = 1f;
  [Tooltip("Used only when matchFastForwardToFormation is false.")]
  public float fastForwardUntilSeconds = 4f;

  public bool loop = false;
  public float loopPeriodSeconds = 12f;

  [Header("Sky")]
  public Color skyTopColor = new Color(0.36f, 0.50f, 0.74f, 1f);
  public Color skyBottomColor = new Color(0.20f, 0.25f, 0.34f, 1f);

  [Header("Cloud Shading")]
  public Color cloudColor = Color.white;
  public Color shadowColor = new Color(0.55f, 0.60f, 0.68f, 1f);
  public Color rainColor = new Color(0.68f, 0.72f, 0.80f, 1f);
  public Vector2 lightDir = new Vector2(0.3f, 0.8f);
  [Range(0f, 3f)] public float shadowStrength = 1.2f;

  [Header("Formation")]
  [Range(0f, 1f)] public float cloudBaseHeight = 0.18f;
  [Range(0f, 2f)] public float spawnDelay = 0.05f;
  [Range(0.25f, 20f)] public float formationSeconds = 4.0f;
  [Range(0.05f, 1f)] public float bodyWidth = 0.32f;
  [Range(0f, 1f)] public float bodyTopHeight = 0.78f;
  [Range(0f, 1f)] public float anvilHeight = 0.80f;
  [Range(0f, 2f)] public float anvilWidth = 0.88f;
  [Range(0f, 1f)] public float anvilStart = 0.65f;

  [Header("Shear")]
  public Vector2 shearDir = new Vector2(1f, 0f);
  [Range(0f, 1f)] public float shearStrength = 0.12f;
  [Range(0f, 1f)] public float shearStartHeight = 0.45f;

  [Header("Noise")]
  [Range(0.01f, 0.25f)] public float edgeSoftness = 0.08f;
  [Range(0f, 1f)] public float edgeNoiseAmp = 0.25f;
  [Range(0.5f, 12f)] public float edgeNoiseScale = 5f;
  [Range(0f, 1f)] public float interiorNoiseAmp = 0.06f;
  [Range(0.5f, 8f)] public float interiorNoiseScale = 2.5f;
  [Range(0f, 2f)] public float noiseSpeed = 0.2f;

  [Header("Dissolve")]
  [Range(0f, 0.5f)] public float dissolveStrength = 0.06f;
  [Range(0.5f, 12f)] public float dissolveScale = 3f;
  [Range(0f, 0.5f)] public float dissolveSpeed = 0.03f;

  [Header("Density")]
  [Range(0f, 10f)] public float densityGain = 2.2f;

  [Header("Rain (Off By Default)")]
  [Range(0f, 1f)] public float rainStrength = 0f;
  [Range(0f, 1f)] public float rainStartHeight = 0.62f;
  [Range(0.01f, 1f)] public float rainWidth = 0.06f;
  [Range(0f, 8f)] public float rainSpeed = 2.2f;
  [Range(0.5f, 20f)] public float rainNoiseScale = 10f;

  [Header("Debug")]
  [Range(0, 2)] public int debugMode = 0;

  private Material _material;

  private void OnEnable()
  {
    EnsureMaterial();
    BindTargetRenderer();
    ApplyMaterialProperties();
  }

  private void OnDisable()
  {
    if (_material != null)
    {
      Destroy(_material);
      _material = null;
    }
  }

  private void OnValidate()
  {
    EnsureMaterial();
    BindTargetRenderer();
    ApplyMaterialProperties();
  }

  private void EnsureMaterial()
  {
    if (_material != null)
      return;

    if (cloudShader == null)
    {
      cloudShader = Shader.Find("Hidden/CloudPrototype");
    }
    if (cloudShader == null)
      return;

    _material = new Material(cloudShader);
    BindTargetRenderer();
  }

  private void Update()
  {
    EnsureMaterial();
    if (_material == null)
      return;

    BindTargetRenderer();

    if (animateTime)
    {
      float formationEnd = spawnDelay + formationSeconds;
      float fastUntil = matchFastForwardToFormation ? formationEnd : fastForwardUntilSeconds;

      float scheduleScale = 1f;
      if (useAutoFastForward)
      {
        scheduleScale = timeSeconds < fastUntil ? fastForwardScale : normalScale;
      }

      timeSeconds += Time.deltaTime * timeScale * scheduleScale;

      if (loop && loopPeriodSeconds > 0f && timeSeconds > loopPeriodSeconds)
      {
        timeSeconds = 0f;
      }
    }

    ApplyMaterialProperties();
  }

  private void ApplyMaterialProperties()
  {
    if (_material == null)
      return;

    _material.SetColor("_SkyTopColor", skyTopColor);
    _material.SetColor("_SkyBottomColor", skyBottomColor);
    _material.SetColor("_CloudColor", cloudColor);
    _material.SetColor("_ShadowColor", shadowColor);
    _material.SetColor("_RainColor", rainColor);

    _material.SetFloat("_CloudBaseHeight", cloudBaseHeight);

    _material.SetFloat("_SpawnDelay", spawnDelay);
    _material.SetFloat("_FormationSeconds", formationSeconds);
    _material.SetFloat("_BodyWidth", bodyWidth);
    _material.SetFloat("_BodyTopHeight", bodyTopHeight);

    _material.SetFloat("_AnvilHeight", anvilHeight);
    _material.SetFloat("_AnvilWidth", anvilWidth);
    _material.SetFloat("_AnvilStart", anvilStart);

    _material.SetVector("_ShearDir", new Vector4(shearDir.x, shearDir.y, 0f, 0f));
    _material.SetFloat("_ShearStrength", shearStrength);
    _material.SetFloat("_ShearStartHeight", shearStartHeight);

    _material.SetFloat("_EdgeSoftness", edgeSoftness);
    _material.SetFloat("_EdgeNoiseAmp", edgeNoiseAmp);
    _material.SetFloat("_EdgeNoiseScale", edgeNoiseScale);
    _material.SetFloat("_InteriorNoiseAmp", interiorNoiseAmp);
    _material.SetFloat("_InteriorNoiseScale", interiorNoiseScale);
    _material.SetFloat("_NoiseSpeed", noiseSpeed);

    _material.SetFloat("_DissolveStrength", dissolveStrength);
    _material.SetFloat("_DissolveScale", dissolveScale);
    _material.SetFloat("_DissolveSpeed", dissolveSpeed);

    _material.SetFloat("_DensityGain", densityGain);

    _material.SetFloat("_RainStrength", rainStrength);
    _material.SetFloat("_RainStartHeight", rainStartHeight);
    _material.SetFloat("_RainWidth", rainWidth);
    _material.SetFloat("_RainSpeed", rainSpeed);
    _material.SetFloat("_RainNoiseScale", rainNoiseScale);

    _material.SetVector("_LightDir", new Vector4(lightDir.x, lightDir.y, 0f, 0f));
    _material.SetFloat("_ShadowStrength", shadowStrength);

    _material.SetFloat("_TimeSeconds", timeSeconds);
    _material.SetFloat("_DebugMode", debugMode);
  }

  public void BindTargetRenderer()
  {
    if (_material == null || targetRenderer == null)
      return;
    if (targetRenderer.sharedMaterial != _material)
    {
      targetRenderer.sharedMaterial = _material;
    }
  }
}
