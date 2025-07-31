Shader "Fluid/DepthDownsampleCopy"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off
        ZWrite On
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct AppData
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct V2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            V2f vert(AppData v)
            {
                V2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;

            float4 frag(V2f i, out float Depth : SV_Depth) : SV_Target
            {
                Depth = tex2D(_MainTex, i.uv).g;
                return 0;
            }
            ENDCG
        }
    }
}