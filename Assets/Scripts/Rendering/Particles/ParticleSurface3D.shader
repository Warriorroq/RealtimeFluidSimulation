Shader "Fluid/Particle3DSurf"
{
    Properties
    {
        _MainTex("Albedo (RGB)", 2D) = "white" {}
        _Glossiness("Smoothness", Range(0,1)) = 0.5
        _Metallic("Metallic", Range(0,1)) = 0.0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
        }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard addshadow fullforwardshadows vertex:vert
        #pragma multi_compile_instancing
        #pragma instancing_options procedural:setup

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
            float4 colour;
            float3 worldPos;
        };


        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
			StructuredBuffer<float3> positions;
			StructuredBuffer<float3> velocities;
        #endif


        SamplerState linearClampSampler;
        float velocityMax;

        float scale;

        sampler2D colourMap;

        void vert(inout appdata_full v, out Input o)
        {
                UNITY_INITIALIZE_OUTPUT(Input, o);
            o.uv_MainTex = v.texcoord.xy;

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
				float velocityMagnitude = length(velocities[unity_InstanceID]);
				float normalizedVelocity = saturate(velocityMagnitude / velocityMax);
				float gradientT = normalizedVelocity;
				o.colour = tex2Dlod(colourMap, float4(gradientT, 0.5,0,0));
            #endif
        }

        void setup()
        {
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
				float3 particlePosition = positions[unity_InstanceID];

				unity_ObjectToWorld._11_21_31_41 = float4(scale, 0, 0, 0);
				unity_ObjectToWorld._12_22_32_42 = float4(0, scale, 0, 0);
				unity_ObjectToWorld._13_23_33_43 = float4(0, 0, scale, 0);
				unity_ObjectToWorld._14_24_34_44 = float4(particlePosition, 1);
				unity_WorldToObject = unity_ObjectToWorld;
				unity_WorldToObject._14_24_34 *= -1;
				unity_WorldToObject._11_22_33 = 1.0f / unity_WorldToObject._11_22_33;

            #endif
        }

        half _Glossiness;
        half _Metallic;

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = IN.colour;
            o.Metallic = 0;
            o.Smoothness = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}