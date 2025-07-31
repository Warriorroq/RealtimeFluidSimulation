// struct appdata
// {
//     float4 vertex : POSITION;
//     float2 uv : TEXCOORD0;
// };

// struct v2f
// {
//     float2 uv : TEXCOORD0;
//     float4 vertex : SV_POSITION;
// };

// v2f vert(appdata v)
// {
//     v2f o;
//     o.vertex = UnityObjectToClipPos(v.vertex);
//     o.uv = v.uv;
//     return o;
// }

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
float radius;
int useWorldSpaceRadius;
int maxScreenSpaceRadius;
float strength;
float3 smoothMask;

// Returns the gaussian weight for the given offset (in pixels) and sigma.
float gaussianWeight(int pixelOffset, float sigma)
{
    const float denominator = 2 * sigma * sigma;          // Normalisation factor
    return exp(-pixelOffset * pixelOffset / denominator); // 1-D gaussian
}

// Converts a world-space blur radius to its equivalent in screen-space pixels.
float screenSpaceRadius(float worldRadius, float depth, int imageWidth)
{
    // Derivation courtesy of Freya Holmer — see: x.com/FreyaHolmer/status/1820157167682388210
    float widthScale  = UNITY_MATRIX_P._m00;               // Smaller values = larger FOV
    float pxPerMeter  = (imageWidth * widthScale) / (2 * depth);
    return abs(pxPerMeter) * worldRadius;
}

// Reconstruct a view-space position from normalised screen UV coordinates and depth.
float4 reconstructViewPos(float2 uv, float depth)
{
    float3 origin      = 0;
    float3 viewVector  = mul(unity_CameraInvProjection, float4(uv.xy * 2 - 1, 0, -1));
    float3 direction   = normalize(viewVector);
    return float4(origin + direction * depth, depth);
}

// Calculates the effective blur radius (in integer pixels) and the sigma value
// that will be used for the gaussian weights.
void calcRadiusAndSigma(out int radiusPixels, out float sigma, float depth)
{
    radiusPixels = ceil(radius);
    sigma        = max(1e-7, radius / (6 * max(0.01, strength)));

    if (useWorldSpaceRadius == 1)
    {
        float screenRadius = screenSpaceRadius(radius, depth, _MainTex_TexelSize.z);

        // Avoid discontinuities where the radius integer changes.
        if (radiusPixels <= 1 && radius > 0) radius = 2;

        radiusPixels = min(maxScreenSpaceRadius, radius);

        float fractional = max(0, radius - screenRadius);
        sigma            = max(1e-7, (radius - fractional) / (6 * max(0.001, strength)));
    }
}

// Performs the actual 1-D blur along a given axis.
float3 blur1D(float2 uv, float2 axis, int radiusPixels, float sigma)
{
    float3 colourSum = 0;
    float  weightSum = 0;

    const float2 texelStep = _MainTex_TexelSize.xy * axis;

    for (int offset = -radiusPixels; offset <= radiusPixels; offset++)
    {
        const float2 sampleUv = uv + texelStep * offset;
        float4 sampledPixel  = tex2Dlod(_MainTex, float4(sampleUv, 0, 0));

        // Discard sky/background samples
        if (sampledPixel.a < 10000)
        {
            float weight = gaussianWeight(offset, sigma);
            colourSum += sampledPixel.rgb * weight;
            weightSum += weight;
        }
    }

    return colourSum / weightSum;
}

// Main entry point called by the fragment shader.
float4 calculateBlur1D(float2 uv, float2 axis)
{
    const float4 originalSample = tex2D(_MainTex, uv);
    const float  originalDepth  = originalSample.a;

    // Determine blur radius and sigma for this pixel
    int   radiusPixels;
    float sigma;
    calcRadiusAndSigma(radiusPixels, sigma, originalDepth);

    // Accumulate samples
    float3 blurredColour = blur1D(uv, axis, radiusPixels, sigma);

    // Blend between original and blurred colour per channel using smoothMask
    return float4(lerp(originalSample.rgb, blurredColour, smoothMask), originalDepth);
}
