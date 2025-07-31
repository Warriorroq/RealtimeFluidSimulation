Shader "Fluid/ParticleThickness" {
	SubShader {

		Tags { "Queue"="Transparent" }
		ZWrite Off
		ZTest LEqual
		Cull Off
		Blend One One

		Pass {

			CGPROGRAM

			#pragma vertex vertexShader
			#pragma fragment fragmentShader
			#pragma target 4.5
			#include "UnityCG.cginc"
			
			StructuredBuffer<float3> positions;   // World-space particle centres
			float scale;                          // Half-extent of quad from centre
			static const float CONTRIBUTION = 0.1; // Additive thickness per particle

			struct V2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			V2f vertexShader(appdata_base v, uint instanceID : SV_InstanceID)
			{
				V2f o;
				// Fetch particle centre from the GPU buffer
				float3 worldCentre = positions[instanceID];
				float3 vertOffset = v.vertex * scale * 2;
				float3 camUp = unity_CameraToWorld._m01_m11_m21;
				float3 camRight = unity_CameraToWorld._m00_m10_m20;
				float3 vertPosWorld = worldCentre + camRight * vertOffset.x + camUp * vertOffset.y;
				o.pos = mul(UNITY_MATRIX_VP, float4(vertPosWorld, 1));
				o.uv = v.texcoord;

				return o;
			}

			float4 fragmentShader(V2f i) : SV_Target
			{
				// Compute distance from quad centre in texture space
				float2 centreOffset = (i.uv - 0.5) * 2;
				float sqrDst = dot(centreOffset, centreOffset);
				// Discard fragments outside the unit circle to create a smooth disc
				if (sqrDst >= 1) discard;

				// Write constant thickness contribution (additive blending)
				return CONTRIBUTION;
			}

			ENDCG
		}
	}
}