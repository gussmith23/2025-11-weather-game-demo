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
  public float timeScale = 1f;
  [Range(0f, 60f)]
  public float timeSeconds = 0f;

  [Header("Sky")]
  public Color skyTopColor = new Color(0.36f, 0.50f, 0.74f, 1f);
  public Color skyBottomColor = new Color(0.20f, 0.25f, 0.34f, 1f);

  [Header("Cloud Shading")]
  public Color cloudColor = Color.white;
  public Color shadowColor = new Color(0.55f, 0.60f, 0.68f, 1f);
  public Color rainColor = new Color(0.68f, 0.72f, 0.80f, 1f);
  public Vector2 lightDir = new Vector2(0.3f, 0.8f);
  [Range(0f, 3f)] public float shadowStrength = 1.1f;

  [Header("Cloud Shape")]
  [Range(0f, 1f)] public float cloudBaseHeight = 0.18f;
  [Range(0f, 1f)] public float towerTopHeight = 0.74f;
  [Range(0f, 1f)] public float anvilHeight = 0.78f;
  [Range(0f, 2f)] public float anvilWidth = 0.84f;
  [Range(0.01f, 0.4f)] public float anvilThickness = 0.10f;
  [Range(0f, 1f)] public float anvilEdgeFeather = 0.35f;
  [Range(0f, 1f)] public float overshootStrength = 0.30f;

  [Header("Density")]
  [Range(0.01f, 1f)] public float stemWidth = 0.10f;
  [Range(0f, 4f)] public float stemTaper = 1.2f;
  [Range(0.001f, 0.25f)] public float edgeSoftness = 0.08f;
  [Range(0f, 1f)] public float puffiness = 0.35f;
  [Range(0.5f, 12f)] public float noiseScale = 5f;
  [Range(0f, 2f)] public float noiseSpeed = 0.2f;
  [Range(0f, 10f)] public float densityGain = 2.0f;

  [Header("Shear")]
  public Vector2 shearDir = new Vector2(1f, 0f);
  [Range(0f, 1f)] public float shearStrength = 0.12f;
  [Range(0f, 1f)] public float shearStartHeight = 0.45f;

  [Header("Rain")]
  [Range(0f, 1f)] public float rainStrength = 0.28f;
  [Range(0f, 1f)] public float rainStartHeight = 0.62f;
  [Range(0.02f, 1f)] public float rainWidth = 0.06f;
  [Range(0f, 8f)] public float rainSpeed = 2.2f;
  [Range(0.5f, 20f)] public float rainNoiseScale = 11f;

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
      timeSeconds += Time.deltaTime * timeScale;
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
    _material.SetFloat("_TowerTopHeight", towerTopHeight);
    _material.SetFloat("_AnvilHeight", anvilHeight);
    _material.SetFloat("_AnvilWidth", anvilWidth);
    _material.SetFloat("_AnvilThickness", anvilThickness);
    _material.SetFloat("_AnvilEdgeFeather", anvilEdgeFeather);
    _material.SetFloat("_OvershootStrength", overshootStrength);

    _material.SetFloat("_StemWidth", stemWidth);
    _material.SetFloat("_StemTaper", stemTaper);
    _material.SetFloat("_EdgeSoftness", edgeSoftness);
    _material.SetFloat("_Puffiness", puffiness);
    _material.SetFloat("_NoiseScale", noiseScale);
    _material.SetFloat("_NoiseSpeed", noiseSpeed);
    _material.SetFloat("_DensityGain", densityGain);

    _material.SetVector("_ShearDir", new Vector4(shearDir.x, shearDir.y, 0f, 0f));
    _material.SetFloat("_ShearStrength", shearStrength);
    _material.SetFloat("_ShearStartHeight", shearStartHeight);

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
