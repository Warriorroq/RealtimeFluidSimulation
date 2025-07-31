Shader "Hidden/BilateralFilter2D" 
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct VertexData
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Interpolators
            {
                float2 uv : TEXCOORD0;
                float4 clipPos : SV_POSITION;
            };

            Interpolators vert (VertexData v)
            {
                Interpolators o;
                o.clipPos = UnityObjectToClipPos(v.position);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;
            float4 _MainTex_TexelSize; // x = texelWidth, y = texelHeight, z = width, w = height
            float _radiusMeters;          // world-space radius (meters)
            int   _maxPixelRadius;         // clamp radius in pixels
            float _gaussStrength;          // gaussian sigma scale
            float _depthDifferenceScale;   // additional depth-based attenuation
            float3 _channelMask;           // rgb mask – controls channel blending

            // 2-dimensional gaussian falloff
            inline float Gaussian2D(int x, int y, float sigma)
            {
                const float denom = 2.0 * sigma * sigma;
                return exp(-(x * x + y * y) / denom);
            }

            // Converts world radius to pixel radius at the given view-space point
            float WorldRadiusToPixels(float3 viewPoint, float meters, int imageWidth)
            {
                float clipW = viewPoint.z;
                float proj = UNITY_MATRIX_P._m00;
                float pxPerMeter = (imageWidth * proj) / (2.0 * clipW);
                return abs(pxPerMeter * meters);
            }

            // Retrieve view position from depth buffer value
            float3 ReconstructViewPos(float2 uv, float depth)
            {
                float3 origin = 0;
                float3 viewVec = mul(unity_CameraInvProjection, float4(uv * 2.0 - 1.0, 0, -1.0));
                float3 dir = normalize(viewVec);
                return origin + dir * depth;
            }

            float4 frag (Interpolators i) : SV_Target
            {
                float4 centre = tex2D(_MainTex, i.uv);

                // dynamic radius in pixels
                float depth = centre.a;
                float3 viewPos = ReconstructViewPos(i.uv, depth);
                int radius = round(WorldRadiusToPixels(viewPos, _radiusMeters, _MainTex_TexelSize.z));
                radius = min(_maxPixelRadius, radius);

                float sigma = max(1e-7, radius * _gaussStrength);

                float4 sum = 0;
                float weightSum = 0;

                // manual unrolled nested loops for clarity
                for (int dx = -radius; dx <= radius; ++dx)
                {
                    for (int dy = -radius; dy <= radius; ++dy)
                    {
                        float2 offsetUv = i.uv + float2(dx, dy) * _MainTex_TexelSize.xy;
                        float4 sample = tex2Dlod(_MainTex, float4(offsetUv, 0, 0));

                        float w = Gaussian2D(dx, dy, sigma);
                        float depthDelta = centre.a - sample.a;
                        float depthWeight = exp(-depthDelta * depthDelta * _depthDifferenceScale);

                        float finalWeight = w * depthWeight;
                        sum += sample * finalWeight;
                        weightSum += finalWeight;
                    }
                }

                if (weightSum > 0)
                {
                    sum /= weightSum;
                }

                float3 blended = lerp(centre.rgb, sum.rgb, _channelMask);
                return float4(blended, depth);
            }

            ENDCG
        }
    }
}
