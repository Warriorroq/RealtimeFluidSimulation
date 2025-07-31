Shader "Fluid/SmoothThickPrepare"
{
    // Texture placeholder kept for consistency (not used directly)
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        // Screen-space utility pass: culls nothing, writes no depth, always passes depth test
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vertexShader
            #pragma fragment fragmentShader

            #include "UnityCG.cginc"

            // Vertex data supplied by the full-screen quad
            struct AppData
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            // Data passed from the vertex shader to the fragment shader
            struct V2F
            {
                float2 uv     : TEXCOORD0;
                float4 pos    : SV_POSITION;
            };

            // Vertex shader : transforms position to clip space, forwards UVs
            V2F vertexShader(AppData v)
            {
                V2F o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            sampler2D Depth;      // Raw/filtered scene depth (R channel)
            sampler2D Thick;      // Smoothed particle thickness (R channel)

            // Fragment shader : packs depth + thickness into a single RGBA output
            //  R = depth, G = thickness, B = thickness (duplicate for later use), A = depth
            float4 fragmentShader(V2F i) : SV_Target
            {
                float  depth     = tex2D(Depth,  i.uv).r;
                float  thickness = tex2D(Thick,  i.uv).r;

                return float4(depth, thickness, thickness, depth);
            }
            ENDCG
        }
    }
}
