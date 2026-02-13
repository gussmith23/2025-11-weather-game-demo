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
    _BodyBlendSoftness ("Body Blend Softness", Range(0.005, 0.2)) = 0.055

    _AnvilHeight ("Anvil Height", Range(0, 1)) = 0.80
    _AnvilWidth ("Anvil Width", Range(0, 2)) = 0.88
    _AnvilStart ("Anvil Start", Range(0, 1)) = 0.72
    _AnvilBlendSoftness ("Anvil Blend Softness", Range(0.005, 0.2)) = 0.11

    _ShearDir ("Shear Dir", Vector) = (1, 0, 0, 0)
    _ShearStrength ("Shear Strength", Range(0, 1)) = 0.12
    _ShearStartHeight ("Shear Start Height", Range(0, 1)) = 0.45

    _EdgeSoftness ("Edge Softness", Range(0.01, 0.25)) = 0.08
    _EdgeNoiseAmp ("Edge Noise Amp", Range(0, 1)) = 0.17
    _EdgeNoiseScale ("Edge Noise Scale", Range(0.5, 12)) = 5
    _InteriorNoiseAmp ("Interior Noise Amp", Range(0, 1)) = 0.04
    _InteriorNoiseScale ("Interior Noise Scale", Range(0.5, 8)) = 2.3
    _NoiseSpeed ("Noise Speed", Range(0, 2)) = 0.2

    _BodyWarpScale ("Body Warp Scale", Range(0.5, 8)) = 1.8
    _BodyWarpStrength ("Body Warp Strength", Range(0, 0.2)) = 0.06
    _BillowScale ("Billow Scale", Range(0.5, 8)) = 1.75
    _BillowSpeed ("Billow Speed", Range(0, 1)) = 0.13
    _BillowStrength ("Billow Strength", Range(0, 0.2)) = 0.075

    _BaseFeather ("Base Feather", Range(0.005, 0.08)) = 0.02

    _DissolveStrength ("Dissolve Strength", Range(0, 0.5)) = 0.05
    _DissolveScale ("Dissolve Scale", Range(0.5, 12)) = 3
    _DissolveSpeed ("Dissolve Speed", Range(0, 0.5)) = 0.03

    _DensityGain ("Density Gain", Range(0, 10)) = 2.1

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
      float _BodyBlendSoftness;

      float _AnvilHeight;
      float _AnvilWidth;
      float _AnvilStart;
      float _AnvilBlendSoftness;

      float4 _ShearDir;
      float _ShearStrength;
      float _ShearStartHeight;

      float _EdgeSoftness;
      float _EdgeNoiseAmp;
      float _EdgeNoiseScale;
      float _InteriorNoiseAmp;
      float _InteriorNoiseScale;
      float _NoiseSpeed;

      float _BodyWarpScale;
      float _BodyWarpStrength;
      float _BillowScale;
      float _BillowSpeed;
      float _BillowStrength;

      float _BaseFeather;

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

      float noiseTime(float2 p, float t)
      {
        float slice = floor(t);
        float alpha = frac(t);
        float a = fbm(p + float2(slice * 17.13, slice * 5.71));
        float b = fbm(p + float2((slice + 1.0) * 17.13, (slice + 1.0) * 5.71));
        return lerp(a, b, smoothstep(0.0, 1.0, alpha));
      }

      float sdCircle(float2 p, float2 c, float r)
      {
        return length(p - c) - r;
      }

      float sdCapsule(float2 p, float2 a, float2 b, float r)
      {
        float2 pa = p - a;
        float2 ba = b - a;
        float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
        return length(pa - ba * h) - r;
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

      float smoothMaxField(float a, float b, float k)
      {
        float softness = max(k, 1e-4);
        float h = saturate(0.5 + 0.5 * (a - b) / softness);
        return lerp(b, a, h) + softness * h * (1.0 - h);
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

        float visible = smoothstep(0.15, 0.25, progress);
        if (visible <= 0.0)
          return 0.0;

        float noiseRamp = smoothstep(0.25, 0.75, progressEase);
        float matureT = max(0.0, _TimeSeconds - (_SpawnDelay + _FormationSeconds));

        float2 p = float2((uv.x - 0.5) * 2.0, uv.y);

        // Slow coherent warping keeps the cloud billowing instead of looking like stacked circles.
        float flowT = _TimeSeconds * _BillowSpeed;
        float billowActivity = smoothstep(0.35, 1.0, progressEase) * lerp(0.35, 1.0, saturate01(matureT * 0.25));
        float warpX = (noiseTime(p * _BodyWarpScale + float2(11.3, -3.9), flowT) - 0.5) * 2.0;
        float warpY = (noiseTime(p * _BodyWarpScale + float2(-7.7, 9.4), flowT + 0.37) - 0.5) * 2.0;
        float2 pWarped = p + float2(warpX, warpY) * _BodyWarpStrength * billowActivity;

        // Continuous growth envelope avoids discrete pop-in from per-bubble thresholds.
        float growthTop = lerp(_CloudBaseHeight + 0.08, _BodyTopHeight, smoothstep(0.16, 0.86, progressEase));

        float body = -1.5;
        const int kBubbles = 5;
        for (int j = 0; j < kBubbles; j++)
        {
          float j01 = (j + 1.0) / kBubbles;
          float centerY = lerp(_CloudBaseHeight + 0.10, _BodyTopHeight, j01);
          float activation = smoothstep(-0.16, 0.10, growthTop - centerY);

          float jitter = (noise2(float2(j * 19.7, 13.1)) - 0.5) * 0.05;
          float radius = _BodyWidth * lerp(1.18, 0.78, j01) * lerp(0.84, 1.0, activation);
          float2 bubbleP = float2(pWarped.x - jitter, (pWarped.y - centerY) * 1.85);
          float bubble = -sdCircle(bubbleP, float2(0.0, 0.0), radius);
          bubble = lerp(-1.5, bubble, activation);

          body = smoothMaxField(body, bubble, _BodyBlendSoftness);
        }

        float topLobeOn = smoothstep(0.58, 0.90, progressEase);
        float topLobe = -sdCircle(float2(pWarped.x * 1.04, (pWarped.y - (_BodyTopHeight + 0.025)) * 2.0), float2(0.0, 0.0), _BodyWidth * 0.74);
        body = smoothMaxField(body, lerp(-1.5, topLobe, topLobeOn), _BodyBlendSoftness);

        float anvilT = smoothstep(_AnvilStart, 1.0, progressEase);
        float2 uvAnvil = uv + shearOffset(uv) * anvilT;
        float2 pAnvil = float2((uvAnvil.x - 0.5) * 2.0, uvAnvil.y);

        float anvilWidth = lerp(_BodyWidth * 1.03, _AnvilWidth, anvilT);
        float anvilCap = -sdCapsule(pAnvil, float2(-anvilWidth * 0.64, _AnvilHeight - 0.005), float2(anvilWidth * 0.64, _AnvilHeight - 0.005), 0.085);
        float anvilDome = -sdCircle(float2(pAnvil.x, (pAnvil.y - (_AnvilHeight + 0.045)) * 1.28), float2(0.0, 0.0), 0.12);
        float anvilShape = smoothMaxField(anvilCap, anvilDome, _AnvilBlendSoftness);
        float anvil = lerp(-1.5, anvilShape, anvilT);

        float core = smoothMaxField(body, anvil, 0.06);

        float baseD = smoothstep(0.0, max(_EdgeSoftness, 1e-4), core);

        // Keep a flat-ish base with gentle edge lift so the underside does not read as a hard disc.
        float xNorm = abs(p.x) / max(_BodyWidth * 1.8, 1e-3);
        float baseLift = _CloudBaseHeight + 0.010 * pow(saturate01(xNorm), 1.6);
        float flatBottom = smoothstep(0.0, _BaseFeather, uv.y - baseLift);
        baseD *= flatBottom;

        float edgeMask = baseD * (1.0 - baseD);
        float t = _TimeSeconds * _NoiseSpeed;

        float2 edgeUv = uv * _EdgeNoiseScale + float2(t, -t * 0.73);
        float edgeNoise = (noiseTime(edgeUv, t * 0.35) - 0.5) * 2.0;
        baseD = saturate01(baseD + edgeNoise * edgeMask * _EdgeNoiseAmp * noiseRamp);

        float2 interiorUv = uv * _InteriorNoiseScale + float2(-t * 0.31, t * 0.27);
        float interiorNoise = (noiseTime(interiorUv, t * 0.24 + 0.5) - 0.5) * 2.0;
        baseD *= (1.0 + interiorNoise * _InteriorNoiseAmp * noiseRamp);

        // Billow motion post-formation: edge-limited coherent contour movement.
        float billowEdge = (noiseTime(uv * _BillowScale + float2(3.2, -6.4), flowT + 0.2) - 0.5) * 2.0;
        float postFormation = smoothstep(0.50, 1.0, progressEase);
        baseD = saturate01(baseD + billowEdge * edgeMask * _BillowStrength * postFormation);

        if (matureT > 0.0 && _DissolveStrength > 0.0)
        {
          float2 dUv = uv * _DissolveScale + float2(matureT * _DissolveSpeed, -matureT * _DissolveSpeed * 0.7);
          float d = noiseTime(dUv, matureT * _DissolveSpeed * 0.5 + 1.1);
          float erosion = smoothstep(0.60, 0.90, d) * _DissolveStrength * edgeMask;
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
