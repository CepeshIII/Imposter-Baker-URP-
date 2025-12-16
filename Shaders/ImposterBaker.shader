Shader "IMP/ImposterBaker"
{
	Properties
	{
	}
	SubShader
	{
		ZTest LEqual
		ZWrite on
		Cull off

		// pass: 0 pixels only pass (used for min max frame computation)
		Pass
		{
			Blend one one
			HLSLPROGRAM
			#pragma target 4.0

			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes
			{
				float3 positionOS 	: POSITION;
			};

			struct Varyings
			{
				float4 positionCS 	: SV_POSITION;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
				output.positionCS = vertexInput.positionCS;

				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				return 1;
			}
			ENDHLSL
		}
		
		//  pass: 1 alpha copy
		Pass
		{
			Blend one one
			HLSLPROGRAM
			#pragma target 4.0

			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_AlphaMap);
			SAMPLER(SamplerState_Point_Clamp);

			float4 _Channels;
			
			struct Attributes
			{
				float3 positionOS 	: POSITION;
				float2 uv 			: TEXCOORD0;
			};

			struct Varyings
			{
				float2 uv 			: TEXCOORD0;
				float4 positionCS 	: SV_POSITION;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
				output.positionCS = vertexInput.positionCS;
				output.uv = input.uv;

				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				float alpha = SAMPLE_TEXTURE2D_LOD(_AlphaMap, SamplerState_Point_Clamp, input.uv, 0).a;
				return alpha * _Channels;
			}
			ENDHLSL
		}

		//  pass: 2 depth copy
		Pass
		{
			Blend one one
			HLSLPROGRAM
			#pragma target 4.0

			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_DepthMap);
			SAMPLER(SamplerState_Point_Clamp);

			float4 _Channels;
			
			struct Attributes
			{
				float3 positionOS 	: POSITION;
				float2 uv 			: TEXCOORD0;
			};

			struct Varyings
			{
				float2 uv 			: TEXCOORD0;
				float4 positionCS 	: SV_POSITION;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
				output.positionCS = vertexInput.positionCS;
				output.uv = input.uv;

				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				float depth = SAMPLE_TEXTURE2D_LOD(_DepthMap, SamplerState_Point_Clamp, input.uv, 0).r;
				return depth * _Channels;
			}
			ENDHLSL
		}

		//  pass: 3 merge normals + depth
		Pass
		{
			Blend one zero
			HLSLPROGRAM
			#pragma target 4.0

			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"

			SAMPLER(SamplerState_Point_Clamp);
			TEXTURE2D(_NormalMap);
			float4 _NormalMap_ST;
			float4 _NormalMap_TexelSize;

			TEXTURE2D(_DepthMap);
			float4 _DepthMap_ST;
			float4 _DepthMap_TexelSize;

			struct Attributes
			{
				float3 positionOS 	: POSITION;
				float2 uv 			: TEXCOORD0;
			};

			struct Varyings
			{
				float2 uv 			: TEXCOORD0;
				float4 positionCS 	: SV_POSITION;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
				output.positionCS = vertexInput.positionCS;
				output.uv = TRANSFORM_TEX(input.uv, _NormalMap);

				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				float3 normalSample = SAMPLE_TEXTURE2D_LOD(_NormalMap, SamplerState_Point_Clamp, input.uv, 0).rgb;
				float depthSample = SAMPLE_TEXTURE2D_LOD(_DepthMap, SamplerState_Point_Clamp, input.uv, 0).r;

				//normalSample = -normalSample;
				float3 unpackedNormal = UnpackNormal(normalSample) * 0.5 + 0.5;
				

				return float4(unpackedNormal, depthSample);
			}
			ENDHLSL
		}

		// pass: 4 dilate pass (super slow - might explode computer)
		Pass
		{
			Blend one one
			HLSLPROGRAM
			#pragma target 4.0

			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			SAMPLER(SamplerState_Point_Clamp);
			TEXTURE2D(_MainTex);
			float4 _MainTex_ST;
			float4 _MainTex_TexelSize;

			TEXTURE2D(_DilateMask);
			float4 _DilateMask_ST;
			float4 _DilateMask_TexelSize;

			float4 _Channels;

			struct Attributes
			{
				float3 positionOS 	: POSITION;
				float2 uv 			: TEXCOORD0;
			};

			struct Varyings
			{
				float2 uv 			: TEXCOORD0;
				float4 positionCS 	: SV_POSITION;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
				output.positionCS = vertexInput.positionCS;
				output.uv = TRANSFORM_TEX(input.uv, _MainTex);

				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				// Pixel colour
				float4 outColor = SAMPLE_TEXTURE2D_LOD(_MainTex, SamplerState_Point_Clamp, input.uv, 0);
				float mask = SAMPLE_TEXTURE2D_LOD(_DilateMask, SamplerState_Point_Clamp, input.uv, 0).r;

				if (mask > 0) return outColor;

				float minDistance = sqrt(_MainTex_TexelSize.z * _MainTex_TexelSize.z + _MainTex_TexelSize.w * _MainTex_TexelSize.w);
				float4 closestColor = outColor;
				float2 uv = input.uv;

				UNITY_LOOP
				for (int i = 0; i < _MainTex_TexelSize.z; ++i) 
				{
					UNITY_LOOP
					for (int j = 0; j < _MainTex_TexelSize.z; ++j) 
					{
						float2 sampleUV = float2(i, j) * _MainTex_TexelSize.xy;

						if (sampleUV.x == uv.x && sampleUV.y == uv.y) continue;

						float texelDistance = distance(sampleUV, input.uv);
						
						float4 sample = SAMPLE_TEXTURE2D_LOD(_MainTex, SamplerState_Point_Clamp, sampleUV, 0);
						float sampleMask = SAMPLE_TEXTURE2D_LOD(_DilateMask, SamplerState_Point_Clamp, sampleUV, 0).r;
						if (sampleMask > 0 && texelDistance < minDistance)
						{
							minDistance = texelDistance;
							closestColor = sample;
						}
					}
				}

				outColor = lerp(outColor, closestColor, _Channels);
				return outColor;
			}
			ENDHLSL
		}
		// pass: 5 

	Pass
		{
			name "MergeColorWithAlpha"
			Blend one one
			HLSLPROGRAM
			#pragma target 4.0

			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			SAMPLER(SamplerState_Point_Clamp);
			TEXTURE2D(_BlitTexture);
			float4 _BlitTexture_ST;
			float4 _BlitTexture_TexelSize;

			TEXTURE2D(_AlphaMap);
			float4 _AlphaMap_ST;
			float4 _AlphaMap_TexelSize;

			float4 _Channels;

			struct Attributes
			{
				float3 positionOS 	: POSITION;
				float2 uv 			: TEXCOORD0;
			};

			struct Varyings
			{
				float2 uv 			: TEXCOORD0;
				float4 positionCS 	: SV_POSITION;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
				output.positionCS = vertexInput.positionCS;
				output.uv = TRANSFORM_TEX(input.uv, _BlitTexture);

				return output;
			}

			//float4 frag(Varyings input) : SV_Target
			//{
			//	float2 uv = input.uv;
			//	float3 color = SAMPLE_TEXTURE2D(_MainTex, SamplerState_Point_Clamp, uv).rgb;
			//	float alpha = SAMPLE_TEXTURE2D(_DilateMask, SamplerState_Point_Clamp, uv).a;
			//	return float4(color, alpha);
			//}

			float4 frag(Varyings input) : SV_Target
			{
				float3 color = SAMPLE_TEXTURE2D_LOD(_BlitTexture, SamplerState_Point_Clamp, input.uv, 0).rgb;
				float alpha = SAMPLE_TEXTURE2D_LOD(_AlphaMap, SamplerState_Point_Clamp, input.uv, 0).a;

				return float4(color, alpha);
			}


			ENDHLSL
		}

		// pass: 6 PartialDilate
		Pass
		{
		    Name "PartialDilate"

			Blend one one
			HLSLPROGRAM
			#pragma target 4.0

			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			SAMPLER(SamplerState_Point_Clamp);
			TEXTURE2D(_MainTex);
			float4 _MainTex_ST;
			float4 _MainTex_TexelSize;

			TEXTURE2D(_DilateMask);
			float4 _DilateMask_ST;
			float4 _DilateMask_TexelSize;

			float4 _Channels;


			struct Attributes
			{
				float3 positionOS 	: POSITION;
				float2 uv 			: TEXCOORD0;
			};

			struct Varyings
			{
				float2 uv 			: TEXCOORD0;
				float4 positionCS 	: SV_POSITION;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
				output.positionCS = vertexInput.positionCS;
				output.uv = TRANSFORM_TEX(input.uv, _MainTex);

				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				// Pixel colour

				float4 outColor = SAMPLE_TEXTURE2D_LOD(_MainTex, SamplerState_Point_Clamp, input.uv, 0);
				float mask = SAMPLE_TEXTURE2D_LOD(_DilateMask, SamplerState_Point_Clamp, input.uv, 0).r;

				if (mask > 0) return outColor;

				float minDistance = sqrt(_MainTex_TexelSize.z * _MainTex_TexelSize.z + _MainTex_TexelSize.w * _MainTex_TexelSize.w);
				float4 closestColor = outColor;
				float2 uv = input.uv;


				//[loop]
				[unroll]
				for (int i = -5; i <= 5; ++i) 
				{
					//[loop]
					[unroll]
					for (int j = -5; j <= 5; ++j) 
					{
						float2 sampleUV = uv + float2(i, j) * _MainTex_TexelSize.xy;

						if (sampleUV.x == uv.x && sampleUV.y == uv.y) continue;

						float texelDistance = distance(sampleUV, input.uv);
						
						float4 sample = SAMPLE_TEXTURE2D_LOD(_MainTex, SamplerState_Point_Clamp, sampleUV, 0);
						//float sampleMask = SAMPLE_TEXTURE2D_LOD(_DilateMask, SamplerState_Point_Clamp, sampleUV, 0).r;
						//if (sampleMask > 0 && texelDistance < minDistance)
						if (sample.a > 1e-5 && texelDistance < minDistance)
						//if (length(sample) > 0 &&  texelDistance < minDistance)
						{
							minDistance = texelDistance;
							closestColor = sample;
						}
					}
				}

				outColor = closestColor;
				return outColor;
			}
			ENDHLSL
		}
		

		// pass: 7 ChannelDifferenceMask
		Pass
		{
		    Name "ChannelDifferenceMask"
		    Blend One One
		
		    HLSLPROGRAM
		    #pragma target 4.0
		    #pragma vertex vert
		    #pragma fragment frag
		
		    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		
		    SAMPLER(SamplerState_Point_Clamp);
		
		    TEXTURE2D(_FirstTex);
		    float4 _FirstTex_ST;
		
		    TEXTURE2D(_SecondTex);
		    float4 _SecondTex_ST;
		
		    // 0 = compare channel
		    // 1 = ignore channel
		    float4 _Channels;
		
		    struct Attributes
		    {
		        float3 positionOS : POSITION;
		        float2 uv         : TEXCOORD0;
		    };
		
		    struct Varyings
		    {
		        float2 uv         : TEXCOORD0;
		        float4 positionCS : SV_POSITION;
		    };
		
		    Varyings vert (Attributes input)
		    {
		        Varyings o = (Varyings)0;
		        VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS);
		        o.positionCS = pos.positionCS;
		        o.uv = TRANSFORM_TEX(input.uv, _FirstTex);
		        return o;
		    }
		
		    float4 frag (Varyings input) : SV_Target
		    {
		        float4 texA = SAMPLE_TEXTURE2D_LOD(
		            _FirstTex, SamplerState_Point_Clamp, input.uv, 0);
		
		        float4 texB = SAMPLE_TEXTURE2D_LOD(
		            _SecondTex, SamplerState_Point_Clamp, input.uv, 0);
		
		        // Step 1: absolute difference
		        float4 diff = abs(texA - texB);
		
		        // Step 2: difference → binary mask
		        float4 diffMask = step(1e-5, diff);
		
		        // Step 3: apply channel selection
		        float4 enabledDiff = diffMask * (1.0 - _Channels);
		
		        // Step 4: collapse channels
		        float anyDiff = max(
		            max(enabledDiff.r, enabledDiff.g),
		            max(enabledDiff.b, enabledDiff.a)
		        );
		
		        // Step 5: output alpha mask
		        return anyDiff;
		    }
		    ENDHLSL
		}
	}
}
