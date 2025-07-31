Shader "Fluid/NormalsFromDepth"
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

            // Vertex data coming from the mesh
            struct AppData
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            // Data that will be sent from the vertex shader to the fragment shader
            struct V2F
            {
                float2 uv  : TEXCOORD0;
                float4 pos : SV_POSITION;
            };

            // Vertex shader : transforms vertex position and passes the UV unchanged
            V2F vert(AppData v)
            {
                V2F o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            sampler2D _MainTex;                   // Packed depth texture
            float4    _MainTex_TexelSize;         // (1/width, 1/height, width, height)
            float4x4  _CameraInvViewMatrix;       // Inverse view matrix (unused but kept for compatibility)
            int       useSmoothedDepth;           // 0 = raw depth (alpha), 1 = smoothed depth (red)

            float4 viewPos(float2 uv)
            {
                // Sample depth; choose between smoothed (R) or raw (A) channel
                float4 depthInfo = tex2D(_MainTex, uv);
                float  depth     = useSmoothedDepth ? depthInfo.r : depthInfo.a;

                // Camera origin in view space
                float3 origin = 0;

                // Convert UV (0-1) to clip space (-1..1) and unproject
                float3 viewVector = mul(unity_CameraInvProjection,
                                        float4(uv * 2 - 1, 0, -1));
                float3 dir = normalize(viewVector);

                // Return XYZ position and store depth in W
                return float4(origin + dir * depth, depth);
            }

            // Fragment shader : outputs world-space normal encoded in RGB, alpha = 1
            float4 frag(V2F i) : SV_Target
            {
                float4 posCentre = viewPos(i.uv);

                // Discard far-plane samples (sentinel value written by depth pass)
                if (posCentre.a > 10000)
                {
                    return 0;
                }

                float2 texel = _MainTex_TexelSize.xy;

                // Finite differences in X 
                float3 deltaX     = viewPos(i.uv + float2(texel.x, 0)) - posCentre;
                float3 deltaXAlt  = posCentre - viewPos(i.uv - float2(texel.x, 0));
                if (abs(deltaXAlt.z) < abs(deltaX.z))
                {
                    deltaX = deltaXAlt;
                }

                // Finite differences in Y 
                float3 deltaY     = viewPos(i.uv + float2(0, texel.y)) - posCentre;
                float3 deltaYAlt  = posCentre - viewPos(i.uv - float2(0, texel.y));
                if (abs(deltaYAlt.z) < abs(deltaY.z))
                {
                    deltaY = deltaYAlt;
                }

                // View-space normal via cross product
                float3 viewNormal  = normalize(cross(deltaY, deltaX));

                // Convert to world space
                float3 worldNormal = mul(unity_CameraToWorld, float4(viewNormal, 0));

                return float4(worldNormal, 1);
            }
            ENDCG
        }
    }
}
