Shader "Fluid/FluidRender"
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

            V2f vert(AppData input)
            {
                V2f outputData;
                outputData.vertex = UnityObjectToClipPos(input.vertex);
                outputData.uv = input.uv;
                return outputData;
            }

            sampler2D _MainTex;
            sampler2D Normals;
            sampler2D Comp;
            sampler2D ShadowMap;
            
            const float3 extinctionCoefficients;
            const float3 dirToSun;
            const float3 boundsSize;
            const float refractionMultiplier;

            const float4 baseColor;
            const float3 colorVariation;
            const float noiseScale;
            const float secondaryNoiseScale;
            const float secondaryNoiseWeight;
            const float gradientStrength;
            const float4 cornerColorBL;
            const float4 cornerColorBR;
            const float4 cornerColorTL;
            const float4 cornerColorTR;
            const float sunIntensity;
            const float sunInvSize;
            const float4x4 shadowVP;
            const float3 floorPos;
            const float3 floorSize;


            /// === Debug values ===
            float3 testParams;
            int debugDisplayMode;
            float depthDisplayScale;
            float thicknessDisplayScale;
            StructuredBuffer<uint> foamCountBuffer;
            uint foamMax;

            
            struct HitInfo
            {
                bool didHit;
                bool isInside;
                float dst;
                float3 hitPoint;
                float3 normal;
            };

            
            struct LightResponse
            {
                float3 reflectDir;
                float3 refractDir;
                float reflectWeight;
                float refractWeight;
            };


            float3 worldViewDir(float2 uvCoords)
            {
                float3 viewVectorCameraSpace = mul(unity_CameraInvProjection, float4(uvCoords.xy * 2 - 1, 0, -1));
                return normalize(mul(unity_CameraToWorld, viewVectorCameraSpace));
            }

            /// Test intersection of ray with unit box centered at origin
            HitInfo rayUnitBox(float3 position, float3 direction)
            {
                const float3 boxMin = -1;
                const float3 boxMax = 1;
                float3 inverseDirection = 1 / direction;

                float3 tMin = (boxMin - position) * inverseDirection;
                float3 tMax = (boxMax - position) * inverseDirection;
                float3 t1 = min(tMin, tMax);
                float3 t2 = max(tMin, tMax);
                float tNear = max(max(t1.x, t1.y), t1.z);
                float tFar = min(min(t2.x, t2.y), t2.z);

                // Set hit info
                HitInfo hitInfo = (HitInfo)0;
                hitInfo.dst = 1.#INF;
                hitInfo.didHit = tFar >= tNear && tFar > 0;
                hitInfo.isInside = tFar > tNear && tNear <= 0;

                if (hitInfo.didHit)
                {
                    float hitDst = hitInfo.isInside ? tFar : tNear;
                    float3 hitPosition = position + direction * hitDst;

                    hitInfo.dst = hitDst;
                    hitInfo.hitPoint = hitPosition;

                    // Calculate normal
                    float3 relativeDistance = (1 - abs(hitPosition));
                    float3 absNormal = (relativeDistance.x < relativeDistance.y && relativeDistance.x < relativeDistance.z) ? float3(1, 0, 0) : (relativeDistance.y < relativeDistance.z) ? float3(0, 1, 0) : float3(0, 0, 1);
                    hitInfo.normal = absNormal * sign(hitPosition) * (hitInfo.isInside ? -1 : 1);
                }

                return hitInfo;
            }

            HitInfo rayBox(float3 rayPosition, float3 rayDirection, float3 centre, float3 size)
            {
                HitInfo hitInfo = rayUnitBox((rayPosition - centre) / size, rayDirection / size);
                hitInfo.hitPoint = hitInfo.hitPoint * size + centre;
                if (hitInfo.didHit) hitInfo.dst = length(hitInfo.hitPoint - rayPosition);
                return hitInfo;
            }

            float3 calculateClosestFaceNormal(float3 boxSize, float3 pos)
            {
                float3 halfSize = boxSize * 0.5;
                float3 offset = (halfSize - abs(pos));
                return (offset.x < offset.y && offset.x < offset.z) ? float3(sign(pos.x), 0, 0) : (offset.y < offset.z) ? float3(0, sign(pos.y), 0) : float3(0, 0, sign(pos.z));
            }

            float4 smoothEdgeNormals(float3 normal, float3 position, float3 boxSize)
            {
                // Smoothly flatten normals out at boundary edges
                float3 offset = boxSize / 2 - abs(position);
                float faceWeight = max(0, min(offset.x, offset.z));
                float3 faceNormal = calculateClosestFaceNormal(boxSize, position);
                const float smoothDst = 0.01;
                const float smoothPow = 5;
                //faceWeight = (1 - smoothstep(0, smoothDst, faceWeight)) * (1 - pow(saturate(normal.y), smoothPow));
                float cornerWeight = 1 - saturate(abs(offset.x - offset.z) * 6);
                faceWeight = 1 - smoothstep(0, smoothDst, faceWeight);
                faceWeight *= (1 - cornerWeight);

                return float4(normalize(normal * (1 - faceWeight) + faceNormal * (faceWeight)), faceWeight);
            }


            float modulo(float x, float y)
            {
                return (x - y * floor(x / y));
            }
            

            // Converts an RGB color to HSV color space
            float3 rgbToHsv(float3 colorRgb)
            {
                float4 hsvK = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 pVal = colorRgb.g < colorRgb.b ? float4(colorRgb.bg, hsvK.wz) : float4(colorRgb.gb, hsvK.xy);
                float4 qVal = colorRgb.r < pVal.x ? float4(pVal.xyw, colorRgb.r) : float4(colorRgb.r, pVal.yzx);

                float delta = qVal.x - min(qVal.w, qVal.y);
                float epsilon = 1.0e-10;
                return float3(abs(qVal.z + (qVal.w - qVal.y) / (6.0 * delta + epsilon)), delta / (qVal.x + epsilon), qVal.x);
            }

            // Converts HSV color to RGB color space using optimized formula
            float3 hsvToRgb(float3 hsvInput)
            {
                float4 hsvConstants = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 hsvTemp = abs(frac(hsvInput.xxx + hsvConstants.xyz) * 6.0 - hsvConstants.www);
                return hsvInput.z * lerp(hsvConstants.xxx, saturate(hsvTemp - hsvConstants.xxx), hsvInput.y);
            }

            float3 tweakHsv(float3 colRGB, float hueShift, float satShift, float valShift)
            {
                float3 hsv = rgbToHsv(colRGB);
                return saturate(hsvToRgb(hsv + float3(hueShift, satShift, valShift)));
            }

            float3 tweakHsv(float3 colRGB, float3 shift)
            {
                float3 hsv = rgbToHsv(colRGB);
                return saturate(hsvToRgb(hsv + shift));
            }

            uint nextRandomUint(inout uint state)
            {
                state = state * 747796405 + 2891336453;
                uint result = ((state >> ((state >> 28) + 4)) ^ state) * 277803737;
                result = (result >> 22) ^ result;
                return result;
            }

            uint rngSeedUintFromUV(float2 uv)
            {
                return (uint)(uv.x * 5023 + uv.y * 96456);
            }

            uint nextRandom(inout uint state)
            {
                state = state * 747796405 + 2891336453;
                uint result = ((state >> ((state >> 28) + 4)) ^ state) * 277803737;
                result = (result >> 22) ^ result;
                return result;
            }

            float randomUNorm(inout uint state)
            {
                return nextRandom(state) / 4294967295.0; // 2^32 - 1
            }

            float randomSNorm(inout uint state)
            {
                return randomUNorm(state) * 2 - 1;
            }

            float3 randomSNorm3(inout uint state)
            {
                return float3(randomSNorm(state), randomSNorm(state), randomSNorm(state));
            }


            uint hashInt2(int2 v)
            {
                return v.x * 5023 + v.y * 96456;
            }

            // ===== 2D Perlin Noise Functions =====
            float2 fade(float2 t) { return t * t * t * (t * (t * 6 - 15) + 10); }
            float rand2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123); }
            float perlin2D(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                float a = rand2(i);
                float b = rand2(i + float2(1, 0));
                float c = rand2(i + float2(0, 1));
                float d = rand2(i + float2(1, 1));

                float2 u = fade(f);
                return lerp( lerp(a, b, u.x), lerp(c, d, u.x), u.y );
            }

            float3 sampleSky(float3 dir)
            {
                const float3 colGround = float3(0.35, 0.3, 0.35) * 0.53;
                const float3 colSkyHorizon = float3(1, 1, 1);
                const float3 colSkyZenith = float3(0.08, 0.37, 0.73);


                float sun = pow(max(0, dot(dir, dirToSun)), sunInvSize) * sunIntensity;
                float skyGradientT = pow(smoothstep(0, 0.4, dir.y), 0.35);
                float groundToSkyT = smoothstep(-0.01, 0, dir.y);
                float3 skyGradient = lerp(colSkyHorizon, colSkyZenith, skyGradientT);

                return lerp(colGround, skyGradient, groundToSkyT) + sun * (groundToSkyT >= 1);
            }

            float3 sampleEnvironment(float3 pos, float3 dir)
            {
                HitInfo floorInfo = rayBox(pos, dir, floorPos, floorSize);

                if (floorInfo.didHit)
                {
                    float3 tileCol = baseColor.rgb;
                    // Plane is uniform so no quadrant check needed

                    float2 noiseUV = floorInfo.hitPoint.xz * noiseScale;
                    float noiseVal1 = perlin2D(noiseUV);

                    // Second (blurred) noise layer
                    float2 noiseUV2 = floorInfo.hitPoint.xz * secondaryNoiseScale;
                    // Simple blur: average neighbour samples for softer appearance
                    float noiseVal2 = (
                        perlin2D(noiseUV2) +
                        perlin2D(noiseUV2 + float2(1,0)) +
                        perlin2D(noiseUV2 + float2(-1,0)) +
                        perlin2D(noiseUV2 + float2(0,1)) +
                        perlin2D(noiseUV2 + float2(0,-1))
                    ) / 5;

                    // Combine noise layers into hsv variation
                    float3 noiseVariation = (noiseVal1 - 0.5) * 2 * colorVariation +
                                           (noiseVal2 - 0.5) * 2 * colorVariation * secondaryNoiseWeight;

                    // Generate noisy colour by shifting a mid‐gray base so noise is visible regardless of baseColor
                    float3 noisyCol = tweakHsv(float3(0.5, 0.5, 0.5), noiseVariation);

                    // Blend between solid colour and noisy colour based on baseColor alpha (0 = fully noisy)
                    float noiseMask = 1 - baseColor.a;
                    tileCol = lerp(tileCol, noisyCol, noiseMask);

                    // ===== Corner gradient colour =====
                    float2 local = floorInfo.hitPoint.xz / floorSize.xz + 0.5; // uv 0-1 across plane
                    local = saturate(local);
                    float4 gradBottom = lerp(cornerColorBL, cornerColorBR, local.x);
                    float4 gradTop    = lerp(cornerColorTL, cornerColorTR, local.x);
                    float3 gradCol    = lerp(gradBottom, gradTop, local.y).rgb;

                    tileCol = lerp(tileCol, gradCol, gradientStrength);

                    float4 shadowClip = mul(shadowVP, float4(floorInfo.hitPoint, 1));
                    shadowClip /= shadowClip.w;
                    float2 shadowUV = shadowClip.xy * 0.5 + 0.5;
                    float shadowEdgeWeight = shadowUV.x >= 0 && shadowUV.x <= 1 && shadowUV.y >= 0 && shadowUV.y <= 1;
                    float3 shadow = tex2D(ShadowMap, shadowUV).r * shadowEdgeWeight;
                    shadow = exp(-shadow * 1 * extinctionCoefficients);

                    float ambientLight = 0.17;
                    shadow = shadow * (1 - ambientLight) + ambientLight;

                    return tileCol * shadow;
                }

                return sampleSky(dir);
            }

            /// Crude anti-aliasing
            float3 sampleEnvironmentAa(float3 pos, float3 dir)
            {
                float3 right = unity_CameraToWorld._m00_m10_m20;
                float3 up = unity_CameraToWorld._m01_m11_m21;
                float aa = 0.01;

                float3 sum = 0;
                for (int ox = -1; ox <= 1; ox++)
                {
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        float3 jitteredFocusPoint = (pos + dir) + (right * ox + up * oy) * 0.7 / _ScreenParams.x;
                        float3 jDir = normalize(jitteredFocusPoint - pos);
                        sum += sampleEnvironment(pos, jDir);
                    }
                }

                return sum / 9;
            }


            // Calculate the number of pixels covered by a world-space radius at given dst from camera
            float calculateScreenSpaceRadius(float worldRadius, float depth, int imageWidth)
            {
                float widthScale = UNITY_MATRIX_P._m00; // smaller values correspond to higher fov (objects appear smaller)
                float pxPerMeter = (imageWidth * widthScale) / (2 * depth);
                return abs(pxPerMeter) * worldRadius;
            }

            // Calculate the proportion of light that is reflected at the boundary between two media (via the fresnel equations)
            float calculateReflectance(float3 inDir, float3 normal, float iorA, float iorB)
            {
                float refractRatio = iorA / iorB;
                float cosAngleIn = -dot(inDir, normal);
                float sinSqrAngleOfRefraction = refractRatio * refractRatio * (1 - cosAngleIn * cosAngleIn);
                if (sinSqrAngleOfRefraction >= 1) return 1; // Ray is fully reflected, no refraction occurs

                float cosAngleOfRefraction = sqrt(1 - sinSqrAngleOfRefraction);
                // Perpendicular polarization
                float rPerpendicular = (iorA * cosAngleIn - iorB * cosAngleOfRefraction) / (iorA * cosAngleIn + iorB * cosAngleOfRefraction);
                rPerpendicular *= rPerpendicular;
                // Parallel polarization
                float rParallel = (iorB * cosAngleIn - iorA * cosAngleOfRefraction) / (iorB * cosAngleIn + iorA * cosAngleOfRefraction);
                rParallel *= rParallel;

                // Return the average of the perpendicular and parallel polarizations
                return (rPerpendicular + rParallel) / 2;
            }


            float3 refractCustom(float3 inDir, float3 normal, float iorA, float iorB)
            {
                float refractRatio = iorA / iorB;
                float cosAngleIn = -dot(inDir, normal);
                float sinSqrAngleOfRefraction = refractRatio * refractRatio * (1 - cosAngleIn * cosAngleIn);
                if (sinSqrAngleOfRefraction > 1) return 0; // Ray is fully reflected, no refraction occurs

                float3 refractDir = refractRatio * inDir + (refractRatio * cosAngleIn - sqrt(1 - sinSqrAngleOfRefraction)) * normal;
                return refractDir;
            }

            float3 reflectCustom(float3 inDir, float3 normal)
            {
                return inDir - 2 * dot(inDir, normal) * normal;
            }


            LightResponse calculateReflectionAndRefraction(float3 inDir, float3 normal, float iorA, float iorB)
            {
                LightResponse result;

                result.reflectWeight = calculateReflectance(inDir, normal, iorA, iorB);
                result.refractWeight = 1 - result.reflectWeight;

                result.reflectDir = reflectCustom(inDir, normal);
                result.refractDir = refractCustom(inDir, normal, iorA, iorB);

                return result;
            }
            
            float4 debugModeDisplay(float depthSmooth, float depth, float thicknessSmooth, float thickness, float3 normal)
            {
                float3 col = 0;

                switch (debugDisplayMode)
                {
                case 1:
                    col = depth / depthDisplayScale;
                    break;
                case 2:
                    col = depthSmooth / depthDisplayScale;
                    break;
                case 3:
                    if (dot(normal, normal) == 0) col = 0;
                    float3 normalDisplay = normal * 0.5 + 0.5;
                    col = pow(normalDisplay, 2.2), 1;
                    break;
                case 4:
                    col = thickness / thicknessDisplayScale;
                    break;
                case 5:
                    col = thicknessSmooth / thicknessDisplayScale;
                    break;
                default:
                    col = float3(1, 0, 1);
                    break;
                }

                return float4(col, 1);
            }

            float4 frag(V2f i) : SV_Target
            {
                if (i.uv.y < 0.005)
                {
                    return i.uv.x < (foamCountBuffer[0] / (float)foamMax);
                }

                // Read data from texture
                float3 normal = tex2D(Normals, i.uv).xyz;

                float4 packedData = tex2D(Comp, i.uv);
                float depthSmooth = packedData.r;
                float thickness = packedData.g;
                float thickness_hard = packedData.b;
                float depth_hard = packedData.a;

                float4 bg = tex2D(_MainTex, float2(i.uv.x, i.uv.y));
                float foam = bg.r;
                float foamDepth = bg.b;

                //  Get test-environment colour (and early exit if view ray misses fluid)
                float3 viewDirWorld = worldViewDir(i.uv);
                float3 world = sampleEnvironmentAa(_WorldSpaceCameraPos, viewDirWorld);
                if (depthSmooth > 1000) return float4(world, 1) * (1 - foam) + foam;

                // Calculate fluid hit point and smooth out normals along edges of bounding box
                float3 hitPos = _WorldSpaceCameraPos.xyz + viewDirWorld * depthSmooth;
                float3 smoothEdgeNormal = smoothEdgeNormals(normal, hitPos, boundsSize).xyz;
                normal = normalize(normal + smoothEdgeNormal * 6 * max(0, dot(normal, smoothEdgeNormal.xyz)));

                // Debug display mode
                if (debugDisplayMode != 0)
                {
                    return debugModeDisplay(depthSmooth, depth_hard, thickness, thickness_hard, normal);
                }

                // Calculate shading 
                const float ambientLight = 0.3;
                float shading = dot(normal, dirToSun) * 0.5 + 0.5;
                shading = shading * (1 - ambientLight) + ambientLight;

                // Calculate reflection and refraction
                LightResponse lightResponse = calculateReflectionAndRefraction(viewDirWorld, normal, 1, 1.33);
                float3 reflectDir = reflectCustom(viewDirWorld, normal);

                float3 exitPos = hitPos + lightResponse.refractDir * thickness * refractionMultiplier;
                // Clamp to ensure doesn't go below floor
                exitPos += lightResponse.refractDir * max(0, floorPos.y + floorSize.y - exitPos.y) / lightResponse.refractDir.y;
           
                // Colour
                float3 transmission = exp(-thickness * extinctionCoefficients);
                float3 reflectCol = sampleEnvironmentAa(hitPos, lightResponse.reflectDir);
                float3 refractCol = sampleEnvironmentAa(exitPos, viewDirWorld);
                refractCol = refractCol * (1 - foam) + foam;
                refractCol *= transmission;

                // If foam is in front of the fluid, overwrite the reflected col with the foam col
                if (foamDepth < depthSmooth)
                {
                    reflectCol = reflectCol * (1 - foam) + foam;
                }

                // Blend between reflected and refracted col
                float3 col = lerp(reflectCol, refractCol, lightResponse.refractWeight);
                return float4(col, 1);
            }
            ENDCG
        }
    }
}