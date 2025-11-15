using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Simple 2D weather simulation visualized on a texture.
/// </summary>
public class Weather2D : MonoBehaviour
{
    [Header("Grid")]
    public int width = 256;
    public int height = 256;
    public float cellSize = 1f;

    [Header("Time & Stability")]
    public float dt = 0.016f;
    public int substeps = 2;

    [Header("Coefficients")]
    public float diffusionT = 0.05f;
    public float diffusionH = 0.05f;
    public float viscosity = 0.01f;
    public float advection = 1.0f;
    public float buoyancy = 0.02f;
    public float evapBase = 0.5f;
    public float condenseThreshold = 0.7f;

    [Header("Clouds")]
    public float cloudFormationRate = 0.8f;
    public float cloudEvaporationRate = 0.35f;
    public float cloudDissipation = 0.1f;

    [Header("Ground Forcing")]
    public int groundLayerThickness = 24;
    public float groundHeatRate = 0.6f;
    public float groundMoistureRate = 0.3f;
    public float groundUpdraftStrength = 0.4f;

    [Header("Global Wind")]
    public Vector2 initialWind = new Vector2(0.25f, 0f);
    public float globalWindBlend = 4f;
    public float maxWindSpeed = 1.5f;

    [Header("Simulation Control")]
    public float timeScale = 1f;
    public float maxTimeScale = 4f;

    [Header("Seeding")]
    public float humidityNoiseScale = 0.05f;
    public float humidityNoiseAmplitude = 0.1f;
    public float temperatureNoiseAmplitude = 0.04f;

    [Header("Interaction")]
    public float heatBrush = 1.5f;
    public float moistureBrush = 1.0f;
    public float brushRadius = 6f;
    public KeyCode addHeatKey = KeyCode.Mouse0;
    public KeyCode addMoistureKey = KeyCode.Mouse1;

    [Header("Display")]
    public RawImage uiTarget;
    public FilterMode filter = FilterMode.Bilinear;

    private float[] _temperature;
    private float[] _temperatureNext;
    private float[] _humidity;
    private float[] _humidityNext;
    private float[] _windX;
    private float[] _windXNext;
    private float[] _windY;
    private float[] _windYNext;
    private float[] _cloud;
    private float[] _cloudNext;
    private Texture2D _texture;

    private Camera _camera;
    private Rect _worldRect;
    private Vector2 _globalWind;
    private float _uiWindSpeed;
    private float _uiWindAngle;
    private int _demoSelectionUI;
    private int _activeDemo;
    private GUIStyle _wrapLabelStyle;
    private readonly string[] _demoNames = { "Default Seed", "Cloud Merger" };
    private readonly string[] _demoDescriptions =
    {
        "Baseline convection: terrain heats moist air, spawning drifting cumulus that you can disturb with heat/moisture brushes.",
        "Twin storm cells build on opposite sides, spin slightly, and converge toward the center to collide and merge."
    };

    private void Start()
    {
        Application.targetFrameRate = 120;
        _globalWind = initialWind;
        _uiWindSpeed = _globalWind.magnitude;
        _uiWindAngle = AngleFromVector(_globalWind);
        int size = width * height;

        _temperature = new float[size];
        _temperatureNext = new float[size];
        _humidity = new float[size];
        _humidityNext = new float[size];
        _windX = new float[size];
        _windXNext = new float[size];
        _windY = new float[size];
        _windYNext = new float[size];
        _cloud = new float[size];
        _cloudNext = new float[size];

        _texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = filter,
            wrapMode = TextureWrapMode.Clamp
        };

        if (uiTarget != null)
        {
            uiTarget.texture = _texture;
        }
        else
        {
            CreateQuadRenderer();
        }

        _worldRect = new Rect(-width * 0.005f, -height * 0.005f, width * 0.01f, height * 0.01f);
        ApplyDemoScenario(0);
    }

    private void SeedInitialFields()
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = Index(x, y);
                float normalizedY = (float)y / (height - 1);
                float tempNoise = (Mathf.PerlinNoise((x + 123f) * humidityNoiseScale, (y + 57f) * humidityNoiseScale) - 0.5f) * 2f;
                float humidityNoise = (Mathf.PerlinNoise(x * humidityNoiseScale, y * humidityNoiseScale) - 0.5f) * 2f;

                float baseTemp = Mathf.Lerp(0.25f, 0.85f, 1f - normalizedY);
                _temperature[index] = Mathf.Clamp01(baseTemp + temperatureNoiseAmplitude * tempNoise);

                float baseHumidity = evapBase + humidityNoiseAmplitude * humidityNoise;
                _humidity[index] = Mathf.Clamp01(baseHumidity);

                _windX[index] = _globalWind.x;
                _windY[index] = _globalWind.y;
                _cloud[index] = 0f;
            }
        }
    }

    private void ApplyDemoScenario(int index)
    {
        if (_temperature == null || _humidity == null)
        {
            return;
        }

        int clamped = Mathf.Clamp(index, 0, _demoNames.Length - 1);
        _demoSelectionUI = clamped;
        _activeDemo = clamped;

        Vector2 baseWind = initialWind;
        if (clamped == 1)
        {
            baseWind = Vector2.zero;
        }

        _globalWind = baseWind;
        _uiWindSpeed = _globalWind.magnitude;
        _uiWindAngle = AngleFromVector(_globalWind);
        timeScale = Mathf.Clamp(timeScale, 0.1f, maxTimeScale);

        ClearSimulationBuffers();
        ClearWorkingBuffers();
        SeedInitialFields();

        switch (clamped)
        {
            case 1:
                SetupCloudMergerDemo();
                break;
        }

        ClearWorkingBuffers();

        if (_texture != null)
        {
            UploadToTexture();
        }
    }

    private void ClearSimulationBuffers()
    {
        System.Array.Clear(_temperature, 0, _temperature.Length);
        System.Array.Clear(_humidity, 0, _humidity.Length);
        System.Array.Clear(_windX, 0, _windX.Length);
        System.Array.Clear(_windY, 0, _windY.Length);
        System.Array.Clear(_cloud, 0, _cloud.Length);
    }

    private void ClearWorkingBuffers()
    {
        System.Array.Clear(_temperatureNext, 0, _temperatureNext.Length);
        System.Array.Clear(_humidityNext, 0, _humidityNext.Length);
        System.Array.Clear(_windXNext, 0, _windXNext.Length);
        System.Array.Clear(_windYNext, 0, _windYNext.Length);
        System.Array.Clear(_cloudNext, 0, _cloudNext.Length);
    }

    private void SetupCloudMergerDemo()
    {
        float centerY = height * 0.65f;
        float radius = Mathf.Min(width, height) * 0.18f;

        AddCloudBlob(new Vector2(width * 0.32f, centerY), radius, 0.45f, 0.12f, 0.55f, 0.28f);
        AddCloudBlob(new Vector2(width * 0.68f, centerY * 0.98f), radius, 0.45f, 0.1f, 0.55f, -0.28f);

        for (int i = 0; i < _windX.Length; i++)
        {
            int x = i % width;
            float fx = width > 1 ? (float)x / (width - 1) : 0f;
            float towardCenter = (0.5f - fx) * 0.35f;
            _windX[i] += towardCenter;
        }
    }

    private void AddCloudBlob(Vector2 center, float radius, float humidityBoost, float temperatureBoost, float cloudBoost, float swirlStrength)
    {
        if (radius <= 0f)
        {
            return;
        }

        float radiusSq = radius * radius;
        for (int y = 0; y < height; y++)
        {
            float dy = y - center.y;
            for (int x = 0; x < width; x++)
            {
                float dx = x - center.x;
                float distSq = dx * dx + dy * dy;
                float gaussian = Mathf.Exp(-distSq / (2f * radiusSq));
                if (gaussian < 0.002f)
                {
                    continue;
                }

                int index = Index(x, y);
                _humidity[index] = Mathf.Clamp01(_humidity[index] + humidityBoost * gaussian);
                _temperature[index] = Mathf.Clamp01(_temperature[index] + temperatureBoost * gaussian);
                _cloud[index] = Mathf.Clamp01(_cloud[index] + cloudBoost * gaussian);

                float distance = Mathf.Sqrt(distSq) + 1e-5f;
                float nx = dx / distance;
                float ny = dy / distance;
                float inward = Mathf.Clamp01(1f - (distance / radius));
                float convergence = inward * 0.18f;

                _windX[index] += -nx * convergence;
                _windY[index] += -ny * convergence * 0.6f;

                float swirl = swirlStrength * inward;
                _windX[index] += -ny * swirl;
                _windY[index] += nx * swirl;

                _windY[index] += inward * 0.28f;
            }
        }
    }

    private void Update()
    {
        HandleBrushInput();

        float scaledDt = Mathf.Max(0f, dt * timeScale);
        if (scaledDt <= 0f)
        {
            UploadToTexture();
            return;
        }

        float step = scaledDt / substeps;
        for (int iter = 0; iter < substeps; iter++)
        {
            ApplyBuoyancy(step);
            ApplyGroundForcing(step);
            ApplyGlobalWind(step);

            Diffuse(_windX, _windXNext, viscosity, step);
            Swap(ref _windX, ref _windXNext);
            Diffuse(_windY, _windYNext, viscosity, step);
            Swap(ref _windY, ref _windYNext);

            Diffuse(_temperature, _temperatureNext, diffusionT, step);
            Swap(ref _temperature, ref _temperatureNext);
            Diffuse(_humidity, _humidityNext, diffusionH, step);
            Swap(ref _humidity, ref _humidityNext);
            Diffuse(_cloud, _cloudNext, diffusionH, step);
            Swap(ref _cloud, ref _cloudNext);

            Advect(_temperature, _temperatureNext, _windX, _windY, advection, step);
            Swap(ref _temperature, ref _temperatureNext);
            Advect(_humidity, _humidityNext, _windX, _windY, advection, step);
            Swap(ref _humidity, ref _humidityNext);
            Advect(_windX, _windXNext, _windX, _windY, advection, step);
            Swap(ref _windX, ref _windXNext);
            Advect(_windY, _windYNext, _windX, _windY, advection, step);
            Swap(ref _windY, ref _windYNext);
            Advect(_cloud, _cloudNext, _windX, _windY, advection, step);
            Swap(ref _cloud, ref _cloudNext);

            RelaxHumidity();
            ApplyCloudMicrophysics(step);
            ClampBoundaries(_windX);
            ClampBoundaries(_windY);
            ClampBoundaries(_cloud);
            Clamp01(_temperature);
            Clamp01(_humidity);
            Clamp01(_cloud);
        }

        UploadToTexture();
    }

    private void HandleBrushInput()
    {
        if (_camera == null)
        {
            _camera = Camera.main;
        }

        if (_camera == null)
        {
            return;
        }

        if (!TryGetPointerState(out Vector2 screenPos, out bool heatPressed, out bool moisturePressed))
        {
            return;
        }

        Vector3 world = _camera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 5f));
        if (!_worldRect.Contains(new Vector2(world.x, world.y)))
        {
            return;
        }

        float gridX = Mathf.InverseLerp(_worldRect.xMin, _worldRect.xMax, world.x) * (width - 1);
        float gridY = Mathf.InverseLerp(_worldRect.yMin, _worldRect.yMax, world.y) * (height - 1);
        int centerX = Mathf.RoundToInt(gridX);
        int centerY = Mathf.RoundToInt(gridY);
        int radius = Mathf.CeilToInt(brushRadius);

        if (heatPressed)
        {
            StampCircle(_temperature, centerX, centerY, radius, heatBrush);
        }

        if (moisturePressed)
        {
            StampCircle(_humidity, centerX, centerY, radius, moistureBrush);
        }
    }

    private bool TryGetPointerState(out Vector2 screenPosition, out bool heatPressed, out bool moisturePressed)
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            screenPosition = default;
            heatPressed = false;
            moisturePressed = false;
            return false;
        }

        screenPosition = mouse.position.ReadValue();
        heatPressed = mouse.leftButton.isPressed;
        moisturePressed = mouse.rightButton.isPressed;
        return true;
#elif ENABLE_LEGACY_INPUT_MANAGER
        Vector3 mouse = Input.mousePosition;
        screenPosition = new Vector2(mouse.x, mouse.y);
        heatPressed = Input.GetKey(addHeatKey);
        moisturePressed = Input.GetKey(addMoistureKey);
        return true;
#else
        screenPosition = default;
        heatPressed = false;
        moisturePressed = false;
        return false;
#endif
    }

    private void StampCircle(float[] field, int centerX, int centerY, int radius, float amount)
    {
        int radiusSquared = radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
        {
            int y = centerY + dy;
            if (y < 0 || y >= height)
            {
                continue;
            }

            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = centerX + dx;
                if (x < 0 || x >= width)
                {
                    continue;
                }

                if (dx * dx + dy * dy > radiusSquared)
                {
                    continue;
                }

                int index = Index(x, y);
                float falloff = 1f - Mathf.Sqrt(dx * dx + dy * dy) / radius;
                field[index] = Mathf.Clamp01(field[index] + amount * falloff * Time.deltaTime * timeScale);
            }
        }
    }

    private void ApplyBuoyancy(float step)
    {
        float sumTemperature = 0f;
        for (int i = 0; i < _temperature.Length; i++)
        {
            sumTemperature += _temperature[i];
        }

        float meanTemperature = sumTemperature / _temperature.Length;

        for (int i = 0; i < _windY.Length; i++)
        {
            _windY[i] += buoyancy * (_temperature[i] - meanTemperature) * step;
        }
    }

    private void ApplyGroundForcing(float step)
    {
        int groundCells = Mathf.Clamp(groundLayerThickness, 1, Mathf.Max(1, height - 1));
        for (int y = 0; y < groundCells; y++)
        {
            float weight = 1f - (float)y / groundCells;
            for (int x = 0; x < width; x++)
            {
                int index = Index(x, y);
                _temperature[index] = Mathf.Clamp01(_temperature[index] + groundHeatRate * weight * step);
                _humidity[index] = Mathf.Clamp01(_humidity[index] + groundMoistureRate * weight * step);
                _windY[index] += groundUpdraftStrength * weight * step;
            }
        }
    }

    private void ApplyGlobalWind(float step)
    {
        float blend = 1f - Mathf.Exp(-globalWindBlend * step);
        for (int i = 0; i < _windX.Length; i++)
        {
            int y = i / width;
            float ny = height > 1 ? (float)y / (height - 1) : 0f;
            float shear = (ny - 0.5f) * 0.4f;
            float verticalBias = Mathf.Lerp(groundUpdraftStrength * 0.25f, -0.1f, ny);

            float targetX = _globalWind.x + shear;
            float targetY = _globalWind.y + verticalBias;

            _windX[i] = Mathf.Lerp(_windX[i], targetX, blend);
            _windY[i] = Mathf.Lerp(_windY[i], targetY, blend);
        }
    }

    private void ApplyCloudMicrophysics(float step)
    {
        float threshold = condenseThreshold;
        float formationRate = Mathf.Max(0f, cloudFormationRate);
        float evaporationRate = Mathf.Max(0f, cloudEvaporationRate);
        float dissipationRate = Mathf.Max(0f, cloudDissipation);

        for (int i = 0; i < _humidity.Length; i++)
        {
            float h = _humidity[i];
            float c = _cloud[i];

            float condense = Mathf.Max(0f, h - threshold);
            if (condense > 0f && formationRate > 0f)
            {
                float amount = Mathf.Min(h, condense * formationRate * step);
                h -= amount;
                c = Mathf.Clamp01(c + amount);
            }

            float deficit = Mathf.Max(0f, threshold - h);
            float evapTarget = (deficit * evaporationRate + dissipationRate) * step;
            if (evapTarget > 0f && c > 0f)
            {
                float amount = Mathf.Min(c, evapTarget);
                c -= amount;
                h = Mathf.Clamp01(h + amount * 0.6f);
            }

            _humidity[i] = h;
            _cloud[i] = c;
        }
    }

    private void Diffuse(float[] source, float[] destination, float coefficient, float step)
    {
        float alpha = coefficient * step;
        for (int y = 0; y < height; y++)
        {
            int up = Mathf.Min(y + 1, height - 1);
            int down = Mathf.Max(y - 1, 0);

            for (int x = 0; x < width; x++)
            {
                int right = Mathf.Min(x + 1, width - 1);
                int left = Mathf.Max(x - 1, 0);

                float center = source[Index(x, y)];
                float north = source[Index(x, up)];
                float south = source[Index(x, down)];
                float east = source[Index(right, y)];
                float west = source[Index(left, y)];

                float laplacian = north + south + east + west - 4f * center;
                destination[Index(x, y)] = center + alpha * laplacian;
            }
        }
    }

    private void Advect(float[] source, float[] destination, float[] windX, float[] windY, float strength, float step)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = Index(x, y);
                float prevX = x - strength * windX[index] * step;
                float prevY = y - strength * windY[index] * step;
                destination[index] = SampleBilinear(source, prevX, prevY);
            }
        }
    }

    private float SampleBilinear(float[] field, float x, float y)
    {
        x = Mathf.Clamp(x, 0f, width - 1f);
        y = Mathf.Clamp(y, 0f, height - 1f);

        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, width - 1);
        int y1 = Mathf.Min(y0 + 1, height - 1);

        float tx = x - x0;
        float ty = y - y0;

        float a = field[Index(x0, y0)];
        float b = field[Index(x1, y0)];
        float c = field[Index(x0, y1)];
        float d = field[Index(x1, y1)];

        float ab = Mathf.Lerp(a, b, tx);
        float cd = Mathf.Lerp(c, d, tx);
        return Mathf.Lerp(ab, cd, ty);
    }

    private void RelaxHumidity()
    {
        for (int i = 0; i < _humidity.Length; i++)
        {
            _humidity[i] = Mathf.Lerp(_humidity[i], evapBase, 0.02f);
        }
    }

    private void ClampBoundaries(float[] field)
    {
        for (int x = 0; x < width; x++)
        {
            field[Index(x, 0)] *= 0.5f;
            field[Index(x, height - 1)] *= 0.5f;
        }

        for (int y = 0; y < height; y++)
        {
            field[Index(0, y)] *= 0.5f;
            field[Index(width - 1, y)] *= 0.5f;
        }
    }

    private void Clamp01(float[] field)
    {
        for (int i = 0; i < field.Length; i++)
        {
            field[i] = Mathf.Clamp01(field[i]);
        }
    }

    private void UploadToTexture()
    {
        var pixels = _texture.GetRawTextureData<Color32>();
        int groundCells = Mathf.Clamp(groundLayerThickness, 1, Mathf.Max(1, height - 1));
        Color skyHorizon = new Color(0.55f, 0.73f, 0.98f);
        Color skyZenith = new Color(0.12f, 0.24f, 0.45f);
        Color groundBright = new Color(0.46f, 0.34f, 0.22f);
        Color groundDark = new Color(0.24f, 0.2f, 0.16f);

        for (int i = 0; i < pixels.Length; i++)
        {
            int y = i / width;
            float temperature = Mathf.Clamp01(_temperature[i]);
            float cloud = Mathf.Clamp01(_cloud[i]);
            bool isGround = y < groundCells;

            Color baseColor;
            if (isGround)
            {
                float t = groundCells > 1 ? (float)y / (groundCells - 1) : 0f;
                baseColor = Color.Lerp(groundBright, groundDark, t);
                float warmth = Mathf.InverseLerp(0.4f, 0.9f, temperature);
                baseColor = Color.Lerp(baseColor, new Color(0.6f, 0.42f, 0.24f), warmth * 0.6f);
            }
            else
            {
                float skyT = Mathf.Clamp01((float)(y - groundCells) / Mathf.Max(1, height - groundCells - 1));
                baseColor = Color.Lerp(skyHorizon, skyZenith, skyT);
                float warmth = Mathf.InverseLerp(0.2f, 0.75f, temperature);
                baseColor = Color.Lerp(baseColor, new Color(0.95f, 0.82f, 0.6f), warmth * 0.12f);
            }

            Color finalColor = baseColor;
            if (!isGround)
            {
                float density = Mathf.Pow(cloud, 0.65f);
                finalColor = Color.Lerp(finalColor, Color.white, density * 0.85f);
                finalColor = Color.Lerp(finalColor, new Color(0.68f, 0.7f, 0.76f), density * 0.25f);
            }

            Color32 packed = finalColor;
            packed.a = (byte)Mathf.RoundToInt(isGround ? 255f : Mathf.Lerp(40f, 235f, Mathf.Pow(cloud, 0.7f)));
            pixels[i] = packed;
        }

        _texture.Apply(false);
    }

    private Color TemperatureToColor(float value)
    {
        if (value < 0.5f)
        {
            float t = value / 0.5f;
            return new Color(0f, t, 1f - t, 1f);
        }
        else
        {
            float t = (value - 0.5f) / 0.5f;
            return new Color(t, 1f - t * 0.2f, 0f, 1f);
        }
    }

    private float AngleFromVector(Vector2 vector)
    {
        if (vector == Vector2.zero)
        {
            return 0f;
        }

        float angle = Mathf.Atan2(vector.y, vector.x) * Mathf.Rad2Deg;
        return Mathf.Repeat(angle, 360f);
    }

    private Vector2 VectorFromAngle(float angle, float magnitude)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * magnitude;
    }

    private void Swap(ref float[] a, ref float[] b)
    {
        float[] temp = a;
        a = b;
        b = temp;
    }

    private int Index(int x, int y)
    {
        return y * width + x;
    }

    private void CreateQuadRenderer()
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Weather Screen";
        quad.transform.position = new Vector3(0f, 0f, 5f);
        quad.transform.localScale = new Vector3(width * 0.01f, height * 0.01f, 1f);

        var renderer = quad.GetComponent<MeshRenderer>();
        renderer.material = new Material(Shader.Find("Unlit/Texture"));
        renderer.material.mainTexture = _texture;

        _camera = Camera.main;
        if (_camera == null)
        {
            _camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            _camera.tag = "MainCamera";
        }

        _camera.orthographic = true;
        _camera.orthographicSize = height * 0.005f;
        _camera.transform.position = new Vector3(0f, 0f, -10f);
        _camera.backgroundColor = new Color(0.42f, 0.62f, 0.92f);
        _camera.clearFlags = CameraClearFlags.SolidColor;
    }

    private void OnGUI()
    {
        if (_wrapLabelStyle == null)
        {
            _wrapLabelStyle = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                fontSize = 11
            };
        }

        GUILayout.BeginArea(new Rect(10f, 10f, 260f, 300f), GUI.skin.box);
        GUILayout.Label("Weather Controls");

        GUILayout.Label($"Wind Speed: {_uiWindSpeed:F2}");
        float newSpeed = GUILayout.HorizontalSlider(_uiWindSpeed, 0f, maxWindSpeed);
        if (!Mathf.Approximately(newSpeed, _uiWindSpeed))
        {
            _uiWindSpeed = newSpeed;
            _globalWind = VectorFromAngle(_uiWindAngle, _uiWindSpeed);
        }

        GUILayout.Label($"Wind Direction: {_uiWindAngle:F0}°");
        float newAngle = GUILayout.HorizontalSlider(_uiWindAngle, 0f, 360f);
        if (!Mathf.Approximately(newAngle, _uiWindAngle))
        {
            _uiWindAngle = newAngle;
            _globalWind = VectorFromAngle(_uiWindAngle, _uiWindSpeed);
        }

        GUILayout.Label($"Timescale: {timeScale:F2}");
        float newTimeScale = GUILayout.HorizontalSlider(timeScale, 0.1f, maxTimeScale);
        if (!Mathf.Approximately(newTimeScale, timeScale))
        {
            timeScale = newTimeScale;
        }

        GUILayout.Space(8f);
        GUILayout.Label($"Active Demo: {_demoNames[_activeDemo]}");
        GUILayout.Label("Demo Presets");
        int selection = GUILayout.SelectionGrid(_demoSelectionUI, _demoNames, 1);
        if (selection != _demoSelectionUI)
        {
            _demoSelectionUI = selection;
        }

        if (GUILayout.Button("Run Selected Demo"))
        {
            ApplyDemoScenario(_demoSelectionUI);
        }

        GUILayout.Label(_demoDescriptions[Mathf.Clamp(_activeDemo, 0, _demoDescriptions.Length - 1)], _wrapLabelStyle, GUILayout.Width(240f));

        GUILayout.EndArea();
    }
}
