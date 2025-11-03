Shader "Custom/RayMarchShader"
{
    Properties
    {
        _DensityTex ("Density Texture", 3D) = "" {}
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _DarkColor ("Dark Cloud Color", Color) = (0.5, 0.5, 0.5, 1)
        _Absorption ("Absorption", Range(0, 2)) = 0.5
        _Steps ("Ray March Steps", Int) = 64
        _DebugBounds ("Debug Bounds", Int) = 0
        _DensitySharpness ("Density Sharpness", Range(1, 10)) = 4.0
    }
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Name "VolumetricRayMarch"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Texture and Sampler
            TEXTURE3D(_DensityTex);
            SAMPLER(sampler_DensityTex);

            // Properties
            CBUFFER_START(UnityPerMaterial)
                float3 _GridSize;
                float3 _BoundsMin;
                float3 _BoundsSize;
                float4 _CloudColor;
                float4 _DarkColor;
                float  _Absorption;
                int    _Steps;
                int    _DebugBounds;
                float  _DensitySharpness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                
                return output;
            }

            bool RayBox(float3 ro, float3 rd, float3 bmin, float3 bmax, out float tmin, out float tmax)
            {
                float3 inv = 1.0 / (rd + 1e-6); // Add epsilon to avoid division by zero
                float3 t0 = (bmin - ro) * inv;
                float3 t1 = (bmax - ro) * inv;
                float3 tsmaller = min(t0, t1);
                float3 tbigger  = max(t0, t1);
                tmin = max(tsmaller.x, max(tsmaller.y, tsmaller.z));
                tmax = min(tbigger.x,  min(tbigger.y,  tbigger.z));
                return tmax > max(tmin, 0.0);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 ro = _WorldSpaceCameraPos;
                float3 rd = normalize(input.positionWS - ro);

                float t0, t1;
                if (!RayBox(ro, rd, _BoundsMin, _BoundsMin + _BoundsSize, t0, t1))
                {
                    discard;
                }

                t0 = max(t0, 0.0);
                float dist = t1 - t0;

                int steps = max(4, _Steps);
                float stepSize = dist / (float)steps;
                float3 startPos = ro + rd * t0;

                float3 accum = 0.0;
                float transmittance = 1.0;
                float densityAccum = 0.0;

                // Ray marching loop simples
                for (int s = 0; s < steps; s++)
                {
                    float3 p = startPos + rd * ((float)s * stepSize + stepSize * 0.5);
                    float3 uvw = (p - _BoundsMin) / _BoundsSize;
                    uvw = saturate(uvw);

                    // Sample raw density from the 3D texture
                    float rawDensity = SAMPLE_TEXTURE3D_LOD(_DensityTex, sampler_DensityTex, uvw, 0).r;

                    // Remap density to give crisper cloud edges
                    const float densityThreshold = 0.05;
                    float density = pow(saturate((rawDensity - densityThreshold) / (1.0 - densityThreshold)), _DensitySharpness);

                    if (density > 0.01)
                    {
                        densityAccum += density * stepSize;
                        // Absorção simples
                        float absorb = exp(-density * _Absorption * stepSize);
                        // Cor baseada na densidade acumulada
                        float lightFactor = pow(1.0 - saturate(densityAccum * 0.15), 2.0);
                        lightFactor = lerp(0.9, 1.0, lightFactor); // Mínimo de 70% de luz
                        float3 cloudColor = lerp(_DarkColor.rgb, _CloudColor.rgb, lightFactor);
                        
                        // Acumular cor
                        accum += transmittance * (1.0 - absorb) * cloudColor;
                        transmittance *= absorb;
                        // Early termination
                        if (transmittance < 0.01)
                            break;
                    }
                }

                float alpha = 1.0 - transmittance;

                // Optional debug: show bounding box edges
                if (_DebugBounds == 1)
                {
                    float3 rel = (input.positionWS - (_BoundsMin + 0.5 * _BoundsSize)) / (_BoundsSize * 0.5);
                    float edge = step(0.95, max(abs(rel.x), max(abs(rel.y), abs(rel.z))));
                    accum = lerp(accum, float3(1, 0, 0), edge);
                    alpha = max(alpha, edge * 0.3);
                }

                // Apply fog
                accum = MixFog(accum, input.fogFactor);
                
                // Fade alpha para evitar bordas duras
                alpha = saturate(alpha);

                if (alpha < 0.01)
                    discard;

                return half4(accum, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
