#include <UnityShaderVariables.cginc>

struct AppData
{
    float4 vertex : POSITION;
    float2 uv : TEXCOORD0;
};

struct V2F
{
    float2 uv : TEXCOORD0;
    float4 vertex : SV_POSITION;
};

V2F vert(AppData v)
{
    V2F o;
    o.vertex = UnityObjectToClipPos(v.vertex);
    o.uv = v.uv;
    return o;
}

sampler2D _MainTex;
float4 _MainTex_TexelSize;

// Updated uniform names (preferred)
float _radiusMeters;
int   _maxPixelRadius;
float _gaussStrength;
float _depthDifferenceScale;
float3 _channelMask;

// Legacy names kept for compatibility (not assigned by script anymore)
float worldRadius;
int   maxScreenSpaceRadius;
float strength;
float diffStrength;
float3 smoothMask;


float calculate1dGaussianKernel(int x, float sigma)
{
    float c = 2 * sigma * sigma;
    return exp(-x * x / c);
}

float calculateScreenSpaceRadius(float3 viewPoint, float worldRadius, int imageWidth)
{
    // Thanks to Freya Holmér
    float clipW = viewPoint.z;
    float proj = UNITY_MATRIX_P._m00;
    float pxPerMeter = (imageWidth * proj) / (2 * clipW);
    return abs(pxPerMeter * worldRadius);
}

// Calculate the number of pixels covered by a world-space radius at given dst from camera
float calculateScreenSpaceRadius(float worldRadius, float depth, int imageWidth)
{
    float widthScale = UNITY_MATRIX_P._m00; // smaller values correspond to higher fov (objects appear smaller)
    float pxPerMeter = (imageWidth * widthScale) / (2 * depth);
    return abs(pxPerMeter) * worldRadius;
}

float4 viewPos(float2 uv, float depth)
{
    float3 origin = 0;
    float3 viewVector = mul(unity_CameraInvProjection, float4(uv.xy * 2 - 1, 0, -1));
    float3 dir = normalize(viewVector);
    return float4(origin + dir * depth, depth);
}

float4 calculateBlur1D(float2 uv, float2 dir)
{
    float4 original = tex2D(_MainTex, uv);
    float depth = original.a;
    
    // Calculate screenspace radius
    float3 viewPosition = viewPos(uv, depth);

    // Prefer new uniforms but fall back to legacy if zero
    float rMeters = _radiusMeters != 0 ? _radiusMeters : worldRadius;
    int maxPx = _maxPixelRadius != 0 ? _maxPixelRadius : maxScreenSpaceRadius;
    float gStrength = _gaussStrength != 0 ? _gaussStrength : strength;
    float dScale = _depthDifferenceScale != 0 ? _depthDifferenceScale : diffStrength;
    float3 mask = (_channelMask.x + _channelMask.y + _channelMask.z) != 0 ? _channelMask : smoothMask;

    float radiusFloat = calculateScreenSpaceRadius(rMeters, depth, _MainTex_TexelSize.z);
    int radius = ceil(radiusFloat);
    if (radius <= 1 && rMeters > 0) radius = 2;
    radius = min(maxPx, radius);
    float fR = max(0, radius - radiusFloat); // use fractional part of radius in sigma calc to avoid harsh boundaries where radius integer changes
    float sigma = max(0.0000001, (radius - fR) / (6 * max(0.001, gStrength)));
    
    float4 sum = 0;
    float wSum = 0;
    float2 texelDelta = _MainTex_TexelSize.xy * dir;

    for (int x = -radius; x <= radius; x++)
    {
        float w = calculate1dGaussianKernel(x, sigma);
        float2 uv2 = uv + texelDelta * x;
        float4 sample = tex2Dlod(_MainTex, float4(uv2, 0, 0));

        float centreDiff = original.a - sample.a;
        float diffWeight = exp(-centreDiff * centreDiff * dScale);

        float sampleWeight = w * diffWeight;
        sum += sample * sampleWeight;
        wSum += sampleWeight;
    }

    if (wSum > 0)
    {
        sum /= wSum;

    }
    return float4(lerp(original.rgb, sum.rgb, mask), depth);
}