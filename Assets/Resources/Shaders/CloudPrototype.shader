Shader "Hidden/CloudPrototype"
{
  Properties
  {
    _SkyTopColor ("Sky Top", Color) = (0.36, 0.50, 0.74, 1)
    _SkyBottomColor ("Sky Bottom", Color) = (0.20, 0.25, 0.34, 1)
    _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
    _ShadowColor ("Shadow Color", Color) = (0.55, 0.60, 0.68, 1)
    _RainColor ("Rain Color", Color) = (0.68, 0.72, 0.80, 1)

    _CloudBaseHeight ("Cloud Base Height", Range(0, 1)) = 0.18

    _SpawnDelay ("Spawn Delay", Range(0, 2)) = 0.05
    _FormationSeconds ("Formation Seconds", Range(0.25, 20)) = 4.0
    _BodyWidth ("Body Width", Range(0.05, 1)) = 0.32
    _BodyTopHeight ("Body Top Height", Range(0, 1)) = 0.78

    _AnvilHeight ("Anvil Height", Range(0, 1)) = 0.80
    _AnvilWidth ("Anvil Width", Range(0, 2)) = 0.88
    _AnvilStart ("Anvil Start", Range(0, 1)) = 0.65

    _ShearDir ("Shear Dir", Vector) = (1, 0, 0, 0)
    _ShearStrength ("Shear Strength", Range(0, 1)) = 0.12
    _ShearStartHeight ("Shear Start Height", Range(0, 1)) = 0.45

    _EdgeSoftness ("Edge Softness", Range(0.01, 0.25)) = 0.08
    _EdgeNoiseAmp ("Edge Noise Amp", Range(0, 1)) = 0.25
    _EdgeNoiseScale ("Edge Noise Scale", Range(0.5, 12)) = 5
    _InteriorNoiseAmp ("Interior Noise Amp", Range(0, 1)) = 0.06
    _InteriorNoiseScale ("Interior Noise Scale", Range(0.5, 8)) = 2.5
    _NoiseSpeed ("Noise Speed", Range(0, 2)) = 0.2

    _DissolveStrength ("Dissolve Strength", Range(0, 0.5)) = 0.06
    _DissolveScale ("Dissolve Scale", Range(0.5, 12)) = 3
    _DissolveSpeed ("Dissolve Speed", Range(0, 0.5)) = 0.03

    _DensityGain ("Density Gain", Range(0, 10)) = 2.2

    _RainStrength ("Rain Strength", Range(0, 1)) = 0.0
    _RainStartHeight ("Rain Start Height", Range(0, 1)) = 0.62
    _RainWidth ("Rain Width", Range(0.01, 1)) = 0.06
    _RainSpeed ("Rain Speed", Range(0, 8)) = 2.2
    _RainNoiseScale ("Rain Noise Scale", Range(0.5, 20)) = 10

    _LightDir ("Light Dir (xy)", Vector) = (0.3, 0.8, 0, 0)
    _ShadowStrength ("Shadow Strength", Range(0, 3)) = 1.2
    _TimeSeconds ("Time Seconds", Float) = 0
    _DebugMode ("Debug Mode", Range(0, 2)) = 0
  }

  SubShader
  {
    Tags { "RenderType"="Opaque" "Queue"="Geometry" }
    Cull Off ZWrite Off ZTest Always

    Pass
    {
      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #include "UnityCG.cginc"

      struct appdata
      {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
      };

      struct v2f
      {
        float4 vertex : SV_POSITION;
        float2 uv : TEXCOORD0;
      };

      float4 _SkyTopColor;
      float4 _SkyBottomColor;
      float4 _CloudColor;
      float4 _ShadowColor;
      float4 _RainColor;

      float _CloudBaseHeight;

      float _SpawnDelay;
      float _FormationSeconds;
      float _BodyWidth;
      float _BodyTopHeight;

      float _AnvilHeight;
      float _AnvilWidth;
      float _AnvilStart;

      float4 _ShearDir;
      float _ShearStrength;
      float _ShearStartHeight;

      float _EdgeSoftness;
      float _EdgeNoiseAmp;
      float _EdgeNoiseScale;
      float _InteriorNoiseAmp;
      float _InteriorNoiseScale;
      float _NoiseSpeed;

      float _DissolveStrength;
      float _DissolveScale;
      float _DissolveSpeed;

      float _DensityGain;

      float _RainStrength;
      float _RainStartHeight;
      float _RainWidth;
      float _RainSpeed;
      float _RainNoiseScale;

      float4 _LightDir;
      float _ShadowStrength;
      float _TimeSeconds;
      float _DebugMode;

      v2f vert(appdata v)
      {
        v2f o;
        o.vertex = UnityObjectToClipPos(v.vertex);
        o.uv = v.uv;
        return o;
      }

      float hash12(float2 p)
      {
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
      }

      float noise2(float2 p)
      {
        float2 i = floor(p);
        float2 f = frac(p);
        float a = hash12(i);
        float b = hash12(i + float2(1, 0));
        float c = hash12(i + float2(0, 1));
        float d = hash12(i + float2(1, 1));
        float2 u = f * f * (3.0 - 2.0 * f);
        return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
      }

      float fbm(float2 p)
      {
        float v = 0.0;
        float a = 0.5;
        float2 shift = float2(100.0, 100.0);
        for (int i = 0; i < 5; i++)
        {
          v += a * noise2(p);
          p = p * 2.02 + shift;
          a *= 0.5;
        }
        return v;
      }

      float sdBox(float2 p, float2 b)
      {
        float2 d = abs(p) - b;
        return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
      }

      float sdCapsule(float2 p, float2 a, float2 b, float r)
      {
        float2 pa = p - a;
        float2 ba = b - a;
        float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
        return length(pa - ba * h) - r;
      }

      float sdCircle(float2 p, float2 c, float r)
      {
        return length(p - c) - r;
      }

      float2 normalizeSafe(float2 v)
      {
        float lenSq = dot(v, v);
        if (lenSq < 1e-5)
          return float2(1.0, 0.0);
        return v * rsqrt(lenSq);
      }

      float2 shearOffset(float2 uv)
      {
        float2 dir = normalizeSafe(_ShearDir.xy);
        float shearT = smoothstep(_ShearStartHeight, 1.0, uv.y);
        return dir * (shearT * _ShearStrength);
      }

      float saturate01(float v)
      {
        return saturate(v);
      }

      float densityField(float2 uv)
      {
        if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
          return 0.0;
        if (uv.y < _CloudBaseHeight)
          return 0.0;

        float formationDenom = max(_FormationSeconds, 1e-3);
        float progress = saturate01((_TimeSeconds - _SpawnDelay) / formationDenom);
        float progressEase = smoothstep(0.0, 1.0, progress);

        // Keep t=0 clean.
        float visible = smoothstep(0.15, 0.25, progress);
        if (visible <= 0.0)
          return 0.0;

        float noiseRamp = smoothstep(0.25, 0.75, progressEase);

        // Work in a space where x and y units are comparable.
        float2 p = float2((uv.x - 0.5) * 2.0, uv.y);

        // Convective body: a stack of overlapping bubbles that "builds upward".
        float body = -10.0;
        const int kBubbles = 5;
        for (int j = 0; j < kBubbles; j++)
        {
          float j01 = (j + 1.0) / kBubbles;
          float bubbleOn = saturate01((progressEase - (j01 - (1.0 / kBubbles))) * kBubbles);
          // Keep the bubble inactive until its stage is reached.
          if (bubbleOn <= 0.0)
            continue;

          float centerY = lerp(_CloudBaseHeight + 0.06, _BodyTopHeight, j01);

          // Very small, stable x jitter for asymmetry.
          float jitter = (noise2(float2(j * 17.7, 13.1)) - 0.5) * 0.06;

          float radius = _BodyWidth * lerp(0.78, 1.00, bubbleOn) * lerp(1.08, 0.80, j01);
          float2 bubbleP = float2(p.x, (p.y - centerY) * 2.0);
          float2 bubbleC = float2(jitter, 0.0);
          float bubble = -sdCircle(bubbleP, bubbleC, radius);

          // Fade in with stage gating.
          bubble = lerp(-10.0, bubble, bubbleOn);
          body = max(body, bubble);
        }

        // Late anvil: grows in width over time and drifts downwind via shear.
        float anvilT = smoothstep(_AnvilStart, 1.0, progressEase);
        float2 uvAnvil = uv + shearOffset(uv) * anvilT;
        float2 pAnvil = float2((uvAnvil.x - 0.5) * 2.0, uvAnvil.y);

        float anvilWidth = lerp(_BodyWidth * 1.15, _AnvilWidth, anvilT);
        float2 anvilP = pAnvil - float2(0.0, _AnvilHeight);
        float anvilShape = -sdBox(anvilP, float2(anvilWidth, 0.08));
        anvilShape = max(anvilShape, -sdCapsule(pAnvil, float2(-anvilWidth * 0.7, _AnvilHeight), float2(anvilWidth * 0.7, _AnvilHeight), 0.12));
        float anvil = lerp(-10.0, anvilShape, anvilT);

        float core = max(body, anvil);

        // Convert to a closed [0,1] density mask before any noise is applied.
        float baseD = smoothstep(0.0, max(_EdgeSoftness, 1e-4), core);

        // Flatten the cloud base using the density mask (avoid SDF/noise interaction artifacts).
        float flatBottom = smoothstep(0.0, 0.025, uv.y - _CloudBaseHeight);
        baseD *= flatBottom;

        float edgeMask = baseD * (1.0 - baseD);

        float t = _TimeSeconds * _NoiseSpeed;

        float2 edgeUv = uv * _EdgeNoiseScale + float2(t, -t * 0.73);
        float edgeNoise = (fbm(edgeUv) - 0.5) * 2.0;
        baseD = saturate01(baseD + edgeNoise * edgeMask * _EdgeNoiseAmp * noiseRamp);

        float2 interiorUv = uv * _InteriorNoiseScale + float2(-t * 0.31, t * 0.27);
        float interiorNoise = (fbm(interiorUv) - 0.5) * 2.0;
        baseD *= (1.0 + interiorNoise * _InteriorNoiseAmp * noiseRamp);
        baseD = saturate01(baseD);

        // Mature dissolve: slowly erode only at the edge.
        float matureT = max(0.0, _TimeSeconds - (_SpawnDelay + _FormationSeconds));
        if (matureT > 0.0 && _DissolveStrength > 0.0)
        {
          float2 dUv = uv * _DissolveScale + float2(matureT * _DissolveSpeed, -matureT * _DissolveSpeed * 0.7);
          float d = fbm(dUv);
          float erosion = smoothstep(0.55, 0.85, d) * _DissolveStrength * edgeMask;
          baseD = saturate01(baseD - erosion);
        }

        return saturate01(baseD * _DensityGain * visible);
      }

      float rainMask(float2 uv)
      {
        if (_RainStrength <= 0.0)
          return 0.0;

        float rainTop = max(_RainStartHeight, _CloudBaseHeight + 0.05);
        if (uv.y > rainTop)
          return 0.0;

        float sourceY = min(0.99, _AnvilHeight - 0.01);
        float src0 = densityField(float2(0.50, sourceY));
        float src1 = densityField(float2(0.46, sourceY));
        float src2 = densityField(float2(0.54, sourceY));
        float source = smoothstep(0.12, 0.52, max(src0, max(src1, src2)));

        float depth = saturate01((rainTop - uv.y) / max(rainTop - _CloudBaseHeight, 1e-3));
        float width = max(0.01, _RainWidth * lerp(0.6, 1.0, depth));
        float shaft = exp(-pow((uv.x - 0.5) / width, 2.0));

        float t = _TimeSeconds * _RainSpeed;
        float streak = fbm(float2(uv.x * _RainNoiseScale, uv.y * (_RainNoiseScale * 2.0) - t));
        streak = smoothstep(0.35, 0.85, streak);

        return saturate01(source * shaft * streak * depth);
      }

      fixed4 frag(v2f i) : SV_Target
      {
        float2 uv = i.uv;
        float4 sky = lerp(_SkyBottomColor, _SkyTopColor, saturate01(uv.y));

        float d = densityField(uv);
        float rain = rainMask(uv) * _RainStrength;

        if (_DebugMode > 1.5)
          return float4(rain, rain, rain, 1.0);
        if (_DebugMode > 0.5)
          return float4(d, d, d, 1.0);

        if (d <= 0.001 && rain <= 0.001)
          return sky;

        float4 result = sky;
        if (d > 0.001)
        {
          float2 l = normalize(_LightDir.xy);
          float2 stepUv = l * (1.0 / 256.0);

          float occ = 0.0;
          float2 sUv = uv;
          [unroll]
          for (int s = 0; s < 10; s++)
          {
            sUv -= stepUv;
            occ += densityField(sUv) * 0.11;
          }
          float shadow = saturate01(occ * _ShadowStrength);

          float4 cloud = lerp(_CloudColor, _ShadowColor, shadow);
          result = lerp(result, cloud, saturate01(d));
        }

        if (rain > 0.001)
          result = lerp(result, _RainColor, saturate01(rain));

        return result;
      }
      ENDCG
    }
  }
}
