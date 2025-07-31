Shader "Fluid/ParticleBillboard" {
	Properties {
		
	}
	SubShader {

		Tags {"Queue"="Geometry" }

		Pass {

			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.5

			#include "UnityCG.cginc"
			
			StructuredBuffer<float3> positions;
			StructuredBuffer<float3> velocities;
			Texture2D<float4> colourMap;
			SamplerState linearClampSampler;
			float velocityMax;

			float scale;
			float3 colour;

			float4x4 localToWorld;

			struct V2F
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 colour : TEXCOORD1;
				float3 normal : NORMAL;
			};

			V2F vert (appdata_full v, uint instanceID : SV_InstanceID)
			{
				V2F o;
				o.uv = v.texcoord;
				o.normal = v.normal;
				
				float3 particlePosition = positions[instanceID];
				float3 localVertexScaled = v.vertex * scale * 2;
				float4 viewPos = mul(UNITY_MATRIX_V, float4(particlePosition, 1)) + float4(localVertexScaled, 0);
				o.pos = mul(UNITY_MATRIX_P, viewPos);


				float velocityMagnitude = length(velocities[instanceID]);
				float normalizedVelocity = saturate(velocityMagnitude / velocityMax);
				float gradientT = normalizedVelocity;
				o.colour = colourMap.SampleLevel(linearClampSampler, float2(gradientT, 0.5), 0);

				return o;
			}

			float4 frag (V2F i) : SV_Target
			{
				float shading = saturate(dot(_WorldSpaceLightPos0.xyz, i.normal));
				shading = (shading + 0.6) / 1.4;
				return float4(i.colour, 1);
			}

			ENDCG
		}
	}
}