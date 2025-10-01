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
			
			// StructuredBuffer<float2> Positions2D;
			// StructuredBuffer<float2> Velocities;
			struct Particle
			{
				float2 position;
				float2 predictedPositions;
				float2 velocity;
				float2 density;
			};
			StructuredBuffer<Particle> Particles;
			// StructuredBuffer<float2> DensityData;
			
			float scale;
			float4 colA;
			Texture2D<float4> ColourMap;
			SamplerState linear_clamp_sampler;
			float velocityMax;
			uint numParticles;

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 colour : TEXCOORD1;
				float needRender : TEXCOORD2; // 1 if this particle should be rendered, 0 if not.
			};

			v2f vert (appdata_full v, uint instanceID : SV_InstanceID)
			{
				v2f o;

				// Cull out of range particles.
			    if (instanceID >= numParticles)
			    {
					o.needRender = 0;
					return o;
			    }
				o.needRender = 1;
				
				float speed = length(Particles[instanceID].velocity);
				float speedT = saturate(speed / velocityMax);
				float colT = speedT;
				
				float3 centreWorld = float3(Particles[instanceID].position, 0);
				float3 worldVertPos = centreWorld + mul(unity_ObjectToWorld, v.vertex * scale);
				float3 objectVertPos = mul(unity_WorldToObject, float4(worldVertPos.xyz, 1));

				
				o.uv = v.texcoord;
				o.pos = UnityObjectToClipPos(objectVertPos);
				o.colour = ColourMap.SampleLevel(linear_clamp_sampler, float2(colT, 0.5), 0);

				return o;
			}


			float4 frag (v2f i) : SV_Target
			{
				// Cull out of range particles.
			    if (i.needRender < 0.5)
			        return float4(0, 0, 0, 0);
				
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