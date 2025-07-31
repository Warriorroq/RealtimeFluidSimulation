Shader "Fluid/MarchingCubesDraw"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct McVertex
            {
                float3 position;
                float3 normal;
            };

            struct VertexToFragment
            {
                float4 positionCS : SV_POSITION;
                float3 normal     : TEXCOORD0;
            };

            // The only input provided by DrawProcedural is the vertexID
            struct VertexInput
            {
                uint vertexID : SV_VertexID;
            };


            StructuredBuffer<McVertex> vertexBuffer;
            float4 _Tint;


            VertexToFragment vert (VertexInput input)
            {
                McVertex v = vertexBuffer[input.vertexID];

                VertexToFragment o;
                o.positionCS = UnityObjectToClipPos(float4(v.position, 1.0));
                o.normal     = v.normal;
                return o;
            }

            float4 frag (VertexToFragment i) : SV_Target
            {
                float3 lightDir = _WorldSpaceLightPos0;
                float lighting = saturate(dot(lightDir, normalize(i.normal))) * 0.5 + 0.5;
                return _Tint * lighting;
            }

            ENDCG
        }
    }
}
