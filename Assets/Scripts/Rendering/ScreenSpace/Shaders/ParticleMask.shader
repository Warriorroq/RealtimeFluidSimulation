Shader "Fluid/ParticleDepth" {
    SubShader {

        Tags {"Queue"="Geometry"}
        Cull Off
        
        Pass {

            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "UnityCG.cginc"
            
            // Buffers & uniforms
            StructuredBuffer<float3> Positions;   // World‐space particle centres
            float scale;                          // Radius scale of each particle billboard

            // Vertex-to-fragment structure
            struct ParticleDepthV2F {
                float4 pos        : SV_POSITION; // Clip-space position
                float2 uv         : TEXCOORD0;   // Quad UV
                float3 posWorld   : TEXCOORD1;   // World-space position (centre of vertex)
            };

            // Vertex shader: build camera-facing quad for each particle
            ParticleDepthV2F vert(appdata_base v, uint instanceID : SV_InstanceID) {
                ParticleDepthV2F o;
                
                float3 worldCentre = Positions[instanceID];
                float3 vertOffset  = v.vertex * scale * 2.0;

                // Camera orientation vectors (world space)
                float3 camUp    = unity_CameraToWorld._m01_m11_m21;
                float3 camRight = unity_CameraToWorld._m00_m10_m20;

                // Position billboard vertex in world space
                float3 vertPosWorld = worldCentre + camRight * vertOffset.x + camUp * vertOffset.y;

                o.pos      = mul(UNITY_MATRIX_VP, float4(vertPosWorld, 1.0));
                o.posWorld = vertPosWorld;
                o.uv       = v.texcoord;

                return o;
            }

            // Convert linear depth (world units from camera) to Unity's non-linear depth buffer value
            float linearDepthToUnityDepth(float linearDepth) {
                float depth01 = (linearDepth - _ProjectionParams.y) / (_ProjectionParams.z - _ProjectionParams.y);
                return (1.0 - (depth01 * _ZBufferParams.y)) / (depth01 * _ZBufferParams.x);
            }

            // Fragment shader: write per-pixel depth of spherical particle
            float4 frag(ParticleDepthV2F i, out float Depth : SV_Depth) : SV_Target {
                // Discard fragments outside particle circle (in quad UV space)
                float2 centreOffset = (i.uv - 0.5) * 2.0;
                float sqrDst        = dot(centreOffset, centreOffset);
                if (sqrDst > 1.0) discard;

                // Reconstruct sphere depth from circle UV
                float z        = sqrt(1.0 - sqrDst);
                float camSpaceZ = abs(mul(unity_MatrixV, float4(i.posWorld, 1.0)).z);
                float dcam      = length(i.posWorld - _WorldSpaceCameraPos);

                // Push depth towards camera based on sphere surface
                float linearDepth = dcam - z * scale;
                Depth = linearDepthToUnityDepth(linearDepth);
                
                // Also output linear depth to colour for optional debug
                return linearDepth;
            }

            ENDCG
        }
    }
}