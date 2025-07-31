Shader "Hidden/BilateralFilter1D"
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
            #include "BilateralPass.hlsl"

            float4 frag (V2F i) : SV_Target
            {
                return calculateBlur1D(i.uv, float2(1, 0));
            }

            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "BilateralPass.hlsl"

            float4 frag (V2F i) : SV_Target
            {
                return calculateBlur1D(i.uv, float2(0, 1));
            }

            ENDCG
        }
    }
}
