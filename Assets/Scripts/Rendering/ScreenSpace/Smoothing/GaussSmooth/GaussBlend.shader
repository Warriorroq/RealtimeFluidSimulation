//  A separable Gaussian blur shader comprising two passes (horizontal & vertical).
//  The heavy-lifting is done in GaussPass.hlsl; this file simply wires the passes
//  into Unity’s render pipeline.

Shader "Hidden/GaussSmooth"
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
            #pragma fragment frag
            #pragma vertex vert

            #include "UnityCG.cginc"
            #include "GaussPass.hlsl"
            
           
            float4 frag (V2F i) : SV_Target
            {
                return calculateBlur1D(i.uv, float2(1, 0));
            }

            ENDCG
        }
         // Vertical blur (kernel direction 0,1)
         Pass
        {
            CGPROGRAM
            #pragma fragment frag
            #pragma vertex vert

            #include "UnityCG.cginc"
            #include "GaussPass.hlsl"
           
            float4 frag (V2F i) : SV_Target
            {
                return calculateBlur1D(i.uv, float2(0, 1));
            }

            ENDCG
        }
      
    }
}
