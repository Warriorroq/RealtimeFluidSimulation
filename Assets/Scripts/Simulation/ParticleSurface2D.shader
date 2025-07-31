Shader "Instanced/Particle2D" {
	Properties {
		
	}
	SubShader {

		Tags { "RenderType"="Transparent" "Queue"="Transparent" }
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off

		Pass {

			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.5

			#include "UnityCG.cginc"
			
			StructuredBuffer<float2> positions2D;
			StructuredBuffer<float2> velocities;
			StructuredBuffer<float2> densityData;
			float scale;
			float4 colA;
			Texture2D<float4> colourMap;
			SamplerState linearClampSampler;
			float velocityMax;

			struct V2F
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 colour : TEXCOORD1;
			};

			V2F vert (appdata_full v, uint instanceID : SV_InstanceID)
			{
				float speed = length(velocities[instanceID]);
				float speedT = saturate(speed / velocityMax);
				float colT = speedT;
				
				float3 centreWorld = float3(positions2D[instanceID], 0);
				float3 worldVertPos = centreWorld + mul(unity_ObjectToWorld, v.vertex * scale);
				float3 objectVertPos = mul(unity_WorldToObject, float4(worldVertPos.xyz, 1));

				V2F o;
				o.uv = v.texcoord;
				o.pos = UnityObjectToClipPos(objectVertPos);
				o.colour = colourMap.SampleLevel(linearClampSampler, float2(colT, 0.5), 0);

				return o;
			}


			float4 frag (V2F i) : SV_Target
			{
				float2 centreOffset = (i.uv.xy - 0.5) * 2;
				float sqrDst = dot(centreOffset, centreOffset);
				float delta = fwidth(sqrt(sqrDst));
				float alpha = 1 - smoothstep(1 - delta, 1 + delta, sqrDst);

				float3 colour = i.colour;
				return float4(colour, alpha);
			}

			ENDCG
		}
	}
}