Shader "Fluid/Raymarching"
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

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 viewVector : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                float3 viewVector = mul(unity_CameraInvProjection, float4(v.uv * 2 - 1, 0, -1));
                o.viewVector = mul(unity_CameraToWorld, float4(viewVector, 0));
                return o;
            }

            Texture3D<float4> DensityMap;
            SamplerState linearClampSampler;

            const float indexOfRefraction;
            const int numRefractions;
            const float3 extinctionCoeff;

            const float3 testParams;
            const float3 boundsSize;
            const float volumeValueOffset;
            const float densityMultiplier;
            const float viewMarchStepSize;
            const float lightStepSize;
            static const float EPSILON_ADJ = 0.01;

            // Test-environment settings
            const float3 dirToSun;
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
            const float3 tileColVariation;
            const float tileScale;
            const float tileDarkOffset;

            const float4x4 cubeLocalToWorld;
            const float4x4 cubeWorldToLocal;
            const float3 floorPos;
            const float3 floorSize;
            
            static const float3 CUBE_COL = float3(0.95, 0.3, 0.35);
            static const float IOR_AIR = 1;

            struct HitInfo
            {
                bool didHit;
                bool isInside;
                float dst;
                float3 hitPoint;
                float3 normal;
            };

            float3 convertRgbToHsv(float3 rgb)
            {
                // Handy helper – converts a colour from RGB into HSV so we can nudge its hue/sat/value.
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = rgb.g < rgb.b ? float4(rgb.bg, K.wz) : float4(rgb.gb, K.xy);
                float4 q = rgb.r < p.x ? float4(p.xyw, rgb.r) : float4(rgb.r, p.yzx);

                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 convertHsvToRgb(float3 hsv)
            {
                // Converts HSV back to good old RGB for the final output.
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(hsv.xxx + K.xyz) * 6.0 - K.www);
                return hsv.z * lerp(K.xxx, saturate(p - K.xxx), hsv.y);
            }

            float3 adjustHsv(float3 colRGB, float hueShift, float satShift, float valShift)
            {
                float3 hsv = convertRgbToHsv(colRGB);
                return saturate(convertHsvToRgb(hsv + float3(hueShift, satShift, valShift)));
            }

            float3 adjustHsv(float3 colRGB, float3 shift)
            {
                float3 hsv = convertRgbToHsv(colRGB);
                return saturate(convertHsvToRgb(hsv + shift));
            }


            uint rngSeedUintFromUv(float2 uv)
            {
                return (uint)(uv.x * 5023 + uv.y * 96456);
            }

            // Lightweight PCG random-number generator – quick, compact and perfect for shader work.
            uint nextRandomUint(inout uint state)
            {
                state = state * 747796405 + 2891336453;
                uint result = ((state >> ((state >> 28) + 4)) ^ state) * 277803737;
                result = (result >> 22) ^ result;
                return result;
            }

            // Returns a uniformly‐distributed random float in the range [0, 1].
            float randomUNorm(inout uint state)
            {
                return nextRandomUint(state) / 4294967295.0; // 2^32 - 1
            }

            // Simple wrapper to maintain old API naming – identical to randomUNorm.
            float randomValue(inout uint state)
            {
                return randomUNorm(state);
            }

            // Random value in a normal (Gaussian) distribution with mean = 0 and sd = 1
            float randomValueNormalDistribution(inout uint state)
            {
                const float PI = 3.1415926;
                // Box–Muller transform converts two uniform randoms into a normal distribution.
                float theta = 2 * PI * randomUNorm(state);
                float rho = sqrt(-2 * log(randomUNorm(state)));
                return rho * cos(theta);
            }

            // Returns a signed random float in the range [-1, 1].
            float randomSNorm(inout uint state)
            {
                return randomValue(state) * 2 - 1;
            }

            // Returns a float3 where each component is independently sampled in the
            // range [-1, 1]. Useful for small HSV adjustments or random vectors.
            float3 randomSNorm3(inout uint state)
            {
                return float3(randomSNorm(state), randomSNorm(state), randomSNorm(state));
            }

            // Calculate a random direction
            float3 randomDirection(inout uint state)
            {
                // Spits out a totally random 3-D direction evenly spread over the sphere.
                float x = randomValueNormalDistribution(state);
                float y = randomValueNormalDistribution(state);
                float z = randomValueNormalDistribution(state);
                return normalize(float3(x, y, z));
            }

            float2 randomPointInCircle(inout uint rngState)
            {
                const float PI = 3.1415926;
                float angle = randomUNorm(rngState) * 2 * PI;
                float2 pointOnCircle = float2(cos(angle), sin(angle));
                return pointOnCircle * sqrt(randomUNorm(rngState));
            }


            // Test intersection of ray with unit box centered at origin
            HitInfo rayUnitBox(float3 pos, float3 dir)
            {
                const float3 boxMin = -1;
                const float3 boxMax = 1;
                float3 invDir = 1 / dir;

                // Super-fast ray vs box test – no branches, just maths.
                float3 tMin = (boxMin - pos) * invDir;
                float3 tMax = (boxMax - pos) * invDir;
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
                    float3 hitPos = pos + dir * hitDst;

                    hitInfo.dst = hitDst;
                    hitInfo.hitPoint = hitPos;

                    // Calculate normal
                    float3 o = (1 - abs(hitPos));
                    float3 absNormal = (o.x < o.y && o.x < o.z) ? float3(1, 0, 0) : (o.y < o.z) ? float3(0, 1, 0) : float3(0, 0, 1);
                    hitInfo.normal = absNormal * sign(hitPos) * (hitInfo.isInside ? -1 : 1);
                }

                return hitInfo;
            }


            uint nextRandom(inout uint state)
            {
                return nextRandomUint(state);
            }

            // Returns a float2 where:
            //   x – distance from `rayOrigin` to the first intersection with the box (0 if the origin is already inside)
            //   y – distance the ray travels while inside the box (0 if the ray misses)
            // CASE 1: Ray enters the box from outside (0 <= dstA <= dstB)
            // dstA is the distance to the closest intersection, dstB is the distance to the farthest intersection

            // CASE 2: Ray starts inside the box (dstA < 0 < dstB)
            // dstA is the distance to the intersection behind the origin, dstB is the distance to the intersection ahead

            // CASE 3: Ray does not intersect the box (dstA > dstB)

            float2 rayBoxDst(float3 boundsMin, float3 boundsMax, float3 rayOrigin, float3 rayDir)
            {
                float3 invRayDir = 1 / rayDir;
                float3 t0 = (boundsMin - rayOrigin) * invRayDir;
                float3 t1 = (boundsMax - rayOrigin) * invRayDir;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);

                float dstA = max(max(tmin.x, tmin.y), tmin.z);
                float dstB = min(tmax.x, min(tmax.y, tmax.z));
              
                float dstToBox = max(0, dstA);
                float dstInsideBox = max(0, dstB - dstToBox);
                return float2(dstToBox, dstInsideBox);
            }

            float sampleDensity(float3 pos)
            {
                float3 uvw = (pos + boundsSize * 0.5) / boundsSize;

                const float epsilon = 0.0001;
                bool isEdge = any(uvw >= 1 - epsilon || uvw <= epsilon);
                if (isEdge) return -volumeValueOffset;

                return DensityMap.SampleLevel(linearClampSampler, uvw, 0).r - volumeValueOffset;
            }


            float calculateDensityAlongRay(float3 rayPos, float3 rayDir, float stepSize)
            {
                // Test for non-normalize ray and return 0 in that case.
                // This happens when refract direction is calculated, but ray is totally reflected
                if (dot(rayDir, rayDir) < 0.9) return 0;

                float2 boundsDstInfo = rayBoxDst(-boundsSize * 0.5, boundsSize * 0.5, rayPos, rayDir);
                float dstToBounds = boundsDstInfo[0];
                float dstThroughBounds = boundsDstInfo[1];
                if (dstThroughBounds <= 0) return 0;

                float dstTravelled = 0;
                float opticalDepth = 0;
                float nudge = stepSize * 0.5;
                float3 entryPoint = rayPos + rayDir * (dstToBounds + nudge);
                dstThroughBounds -= (nudge + EPSILON_ADJ);

                while (dstTravelled < dstThroughBounds)
                {
                    rayPos = entryPoint + rayDir * dstTravelled;
                    float density = sampleDensity(rayPos) * densityMultiplier * stepSize;
                    if (density > 0)
                        opticalDepth += density;
                        
                    dstTravelled += stepSize;
                }

                return opticalDepth;
            }

            float calculateDensityAlongRay(float3 rayPos, float3 rayDir)
            {
                return calculateDensityAlongRay(rayPos, rayDir, lightStepSize);
            }

            float3 calculateClosestFaceNormal(float3 boxSize, float3 p)
            {
                float3 halfSize = boxSize * 0.5;
                float3 o = (halfSize - abs(p));
                return (o.x < o.y && o.x < o.z) ? float3(sign(p.x), 0, 0) : (o.y < o.z) ? float3(0, sign(p.y), 0) : float3(0, 0, sign(p.z));
            }

            struct LightResponse
            {
                float3 reflectDir;
                float3 refractDir;
                float reflectWeight;
                float refractWeight;
            };

            // Fresnel reflectance for unpolarised light.
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


            float3 refract(float3 inDir, float3 normal, float iorA, float iorB)
            {
                float refractRatio = iorA / iorB;
                float cosAngleIn = -dot(inDir, normal);
                float sinSqrAngleOfRefraction = refractRatio * refractRatio * (1 - cosAngleIn * cosAngleIn);
                if (sinSqrAngleOfRefraction > 1) return 0; // Ray is fully reflected, no refraction occurs

                float3 refractDir = refractRatio * inDir + (refractRatio * cosAngleIn - sqrt(1 - sinSqrAngleOfRefraction)) * normal;
                return refractDir;
            }

            float3 reflect(float3 inDir, float3 normal)
            {
                return inDir - 2 * dot(inDir, normal) * normal;
            }


            LightResponse calculateReflectionAndRefraction(float3 inDir, float3 normal, float iorA, float iorB)
            {
                LightResponse result;

                result.reflectWeight = calculateReflectance(inDir, normal, iorA, iorB);
                result.refractWeight = 1 - result.reflectWeight;

                result.reflectDir = reflect(inDir, normal);
                result.refractDir = refract(inDir, normal, iorA, iorB);

                return result;
            }

            // Estimates the gradient of the signed-distance field by sampling density
            // on the three primary axes around position `p`. The gradient direction
            // approximates the surface normal.
            float3 estimateVolumeNormal(float3 p, float sampleOffset)
            {
                float3 dx = float3(sampleOffset, 0, 0);
                float3 dy = float3(0, sampleOffset, 0);
                float3 dz = float3(0, 0, sampleOffset);

                float dX = sampleDensity(p - dx) - sampleDensity(p + dx);
                float dY = sampleDensity(p - dy) - sampleDensity(p + dy);
                float dZ = sampleDensity(p - dz) - sampleDensity(p + dz);

                return normalize(float3(dX, dY, dZ));
            }

            // Calculates a smoothed surface normal at `pos`. A gradient-based normal
            // is blended towards the nearest face normal when close to the container
            // walls to avoid noisy highlights and keep silhouettes crisp.
            float3 calculateNormal(float3 pos)
            {
                const float SAMPLE_OFFSET = 0.1;

                // Gradient normal from neighbouring density samples.
                float3 volumeNormal = estimateVolumeNormal(pos, SAMPLE_OFFSET);

                // Weight towards face normal near the bounds to smooth edges.
                float3 o = boundsSize / 2 - abs(pos);
                float faceWeight = min(o.x, min(o.y, o.z));
                float3 faceNormal = calculateClosestFaceNormal(boundsSize, pos);

                const float SMOOTH_DST = 0.3;
                const float SMOOTH_POW = 5;
                faceWeight = (1 - smoothstep(0, SMOOTH_DST, faceWeight)) * (1 - pow(saturate(volumeNormal.y), SMOOTH_POW));

                return normalize(volumeNormal * (1 - faceWeight) + faceNormal * faceWeight);
            }


            struct SurfaceInfo
            {
                float3 pos;
                float3 normal;
                float densityAlongRay;
                bool foundSurface;
            };

            bool isInsideFluid(float3 pos)
            {
                float2 boundsDstInfo = rayBoxDst(-boundsSize * 0.5, boundsSize * 0.5, pos, float3(0, 0, 1));
                return (boundsDstInfo.x <= 0 && boundsDstInfo.y > 0) && sampleDensity(pos) > 0;
            }

            SurfaceInfo findNextSurface(float3 origin, float3 rayDir, bool findNextFluidEntryPoint, uint rngState, float rngWeight, float maxDst)
            {
                SurfaceInfo info = (SurfaceInfo)0;
                if (dot(rayDir, rayDir) < 0.5) return info;

                float2 boundsDstInfo = rayBoxDst(-boundsSize * 0.5, boundsSize * 0.5, origin, rayDir);
                float r = (randomValue(rngState) - 0.5) * viewMarchStepSize * 0.4 * 1;
                bool hasExittedFluid = !isInsideFluid(origin);
                origin = origin + rayDir * (boundsDstInfo.x + r);

                float stepSize = viewMarchStepSize;
                bool hasEnteredFluid = false;
                float3 lastPosInFluid = origin;

                float dstToTest = boundsDstInfo[1] - (EPSILON_ADJ) * 2;

                for (float dst = 0; dst < dstToTest; dst += stepSize)
                {
                    bool isLastStep = dst + stepSize >= dstToTest;
                    float3 samplePos = origin + rayDir * dst;
                    float thickness = sampleDensity(samplePos) * densityMultiplier * stepSize;
                    bool insideFluid = thickness > 0;
                    if (insideFluid)
                    {
                        hasEnteredFluid = true;
                        lastPosInFluid = samplePos;
                        if (dst <= maxDst)
                        {
                            info.densityAlongRay += thickness;
                        }
                    }

                    if (!insideFluid) hasExittedFluid = true;

                    bool found;
                    if (findNextFluidEntryPoint) found = insideFluid && hasExittedFluid;
                    else found = hasEnteredFluid && (!insideFluid || isLastStep);

                    if (found)
                    {
                        info.pos = lastPosInFluid;
                        info.foundSurface = true;
                        break;
                    }
                }

                return info;
            }

            HitInfo rayBox(float3 rayPos, float3 rayDir, float3 centre, float3 size)
            {
                HitInfo hitInfo = rayUnitBox((rayPos - centre) / size, rayDir / size);
                hitInfo.hitPoint = hitInfo.hitPoint * size + centre;
                if (hitInfo.didHit) hitInfo.dst = length(hitInfo.hitPoint - rayPos);
                return hitInfo;
            }

            HitInfo rayBoxWithMatrix(float3 rayPos, float3 rayDir, float4x4 localToWorld, float4x4 worldToLocal)
            {
                float3 posLocal = mul(worldToLocal, float4(rayPos, 1));
                float3 dirLocal = mul(worldToLocal, float4(rayDir, 0));
                HitInfo hitInfo = rayUnitBox(posLocal, dirLocal);
                hitInfo.normal = normalize(mul(localToWorld, float4(hitInfo.normal, 0)));
                hitInfo.hitPoint = mul(localToWorld, float4(hitInfo.hitPoint, 1));
                if (hitInfo.didHit) hitInfo.dst = length(hitInfo.hitPoint - rayPos);
                return hitInfo;
            }

            float fastMod(float x, float y)
            {
                return (x - y * floor(x / y)); // branch-free modulus
            }

            uint hashInt2(int2 v)
            {
                return v.x * 5023 + v.y * 96456;
            }

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

            float3 calcTransmittance(float thickness)
            {
                return exp(-thickness * extinctionCoeff);
            }

            // Simple physically-inspired sky model with a hard-coded sun bloom.
            float3 sampleSky(float3 dir)
            {
                const float3 colGround = float3(0.35, 0.3, 0.35) * 0.53;
                const float3 colSkyHorizon = float3(1, 1, 1);
                const float3 colSkyZenith = float3(0.08, 0.37, 0.73);

                float sun = pow(max(0, dot(dir, dirToSun)), 500) * 1;
                float skyGradientT = pow(smoothstep(0, 0.4, dir.y), 0.35);
                float groundToSkyT = smoothstep(-0.01, 0, dir.y);
                float3 skyGradient = lerp(colSkyHorizon, colSkyZenith, skyGradientT);

                return lerp(colGround, skyGradient, groundToSkyT) + sun * (groundToSkyT >= 1);
            }

            // Returns the colour encountered by a ray `{o,d}` in the test scene.
            // Handles cube, tiled floor and default sky.
            float3 getEnv(float3 o, float3 d)
            {
                HitInfo fHit = rayBox(o, d, floorPos, floorSize);
                HitInfo cHit = rayBoxWithMatrix(o, d, cubeLocalToWorld, cubeWorldToLocal);

                if (cHit.didHit && cHit.dst < fHit.dst)
                {
                    return saturate(dot(cHit.normal, dirToSun) * 0.5 + 0.5) * CUBE_COL;
                }
                else if (fHit.didHit)
                {
                    float3 tileCol = baseColor.rgb;

                    // === Noise layers ===
                    float2 nUV = fHit.hitPoint.xz * noiseScale;
                    float n1 = perlin2D(nUV);

                    float2 nUV2 = fHit.hitPoint.xz * secondaryNoiseScale;
                    float n2 = (
                        perlin2D(nUV2) +
                        perlin2D(nUV2 + float2(1,0)) +
                        perlin2D(nUV2 + float2(-1,0)) +
                        perlin2D(nUV2 + float2(0,1)) +
                        perlin2D(nUV2 + float2(0,-1))
                    ) / 5;

                    float3 noiseVar = (n1 - 0.5) * 2 * colorVariation + (n2 - 0.5) * 2 * colorVariation * secondaryNoiseWeight;
                    float3 noisyCol = adjustHsv(float3(0.5,0.5,0.5), noiseVar);

                    float noiseMask = 1 - baseColor.a;
                    tileCol = lerp(tileCol, noisyCol, noiseMask);

                    // Corner gradient blending
                    float2 local = fHit.hitPoint.xz / floorSize.xz + 0.5;
                    local = saturate(local);
                    float3 gradBot = lerp(cornerColorBL, cornerColorBR, local.x).rgb;
                    float3 gradTop = lerp(cornerColorTL, cornerColorTR, local.x).rgb;
                    float3 gradCol = lerp(gradBot, gradTop, local.y);
                    tileCol = lerp(tileCol, gradCol, gradientStrength);

                    // Shadow map using transmittance
                    float3 sMap = calcTransmittance(calculateDensityAlongRay(fHit.hitPoint, _WorldSpaceLightPos0, lightStepSize * 2) * 2);
                    bool shadowHit = rayBoxWithMatrix(fHit.hitPoint, dirToSun, cubeLocalToWorld, cubeWorldToLocal).didHit;
                    if (shadowHit) sMap *= 0.2;
                    return tileCol * sMap;
                }

                return sampleSky(d);
            }

            // Crude anti-aliasing
            float3 getEnvAA(float3 o, float3 d)
            {
                float3 rVec = unity_CameraToWorld._m00_m10_m20;
                float3 uVec = unity_CameraToWorld._m01_m11_m21;

                float3 accum = 0;
                for (int ox = -1; ox <= 1; ox++)
                {
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        float3 jFocus = (o + d) + (rVec * ox + uVec * oy) * 0.7 / _ScreenParams.x;
                        float3 jDir = normalize(jFocus - o);
                        accum += getEnv(o, jDir);
                    }
                }

                return accum / 9;
            }

            float3 gatherLight(float3 o, float3 d)
            {
                return getEnvAA(o, d);
            }

            // Primary ray-marcher that traces refraction / reflection paths through the fluid volume.
            float3 rayMarchFluid(float2 uv, float stepSize)
            {
                uint rngState = (uint)(uv.x * 1243 + uv.y * 96456);
                float3 localViewVector = mul(unity_CameraInvProjection, float4(uv * 2 - 1, 0, -1));
                float3 rayDir = normalize(mul(unity_CameraToWorld, float4(localViewVector, 0)));
                float3 rayPos = _WorldSpaceCameraPos.xyz;
                bool travellingThroughFluid = isInsideFluid(rayPos);

                float3 transmittance = 1;
                float3 light = 0;

                for (int i = 0; i < numRefractions; i++)
                {
                    float densityStepSize = lightStepSize * (i + 1); // increase step size with each iteration
                    bool searchForNextFluidEntryPoint = !travellingThroughFluid;

                    HitInfo cubeHit = rayBoxWithMatrix(rayPos, rayDir, cubeLocalToWorld, cubeWorldToLocal);
                    SurfaceInfo surfaceInfo = findNextSurface(rayPos, rayDir, searchForNextFluidEntryPoint, rngState, i == 0 ? 1 : 0, cubeHit.dst);
                    bool useCubeHit = cubeHit.didHit && cubeHit.dst < length(surfaceInfo.pos - rayPos);
                    if (!surfaceInfo.foundSurface) break;

                    transmittance *= calcTransmittance(surfaceInfo.densityAlongRay);

                    // Hit test cube
                    if (useCubeHit)
                    {
                        if (travellingThroughFluid)
                        {
                            transmittance *= calcTransmittance(calculateDensityAlongRay(cubeHit.hitPoint, cubeHit.normal, densityStepSize));
                        }
                        light += gatherLight(rayPos, rayDir) * transmittance;
                        transmittance = 0;
                        break;
                    }

                    // If light hits the floor it will be scattered in all directions (in hemisphere)
                    // Not sure how to handle this in real-time, so just break out of loop here
                    if (surfaceInfo.pos.y < -boundsSize.y / 2 + 0.05)
                    {
                        break;
                    }

                    float3 normal = calculateNormal(surfaceInfo.pos);
                    if (dot(normal, rayDir) > 0) normal = -normal;

                    // Indicies of refraction
                    float iorA = travellingThroughFluid ? indexOfRefraction : IOR_AIR;
                    float iorB = travellingThroughFluid ? IOR_AIR : indexOfRefraction;

                    // Calculate reflection and refraction, and choose which path to follow
                    LightResponse lightResponse = calculateReflectionAndRefraction(rayDir, normal, iorA, iorB);
                    float densityAlongRefractRay = calculateDensityAlongRay(surfaceInfo.pos, lightResponse.refractDir, densityStepSize);
                    float densityAlongReflectRay = calculateDensityAlongRay(surfaceInfo.pos, lightResponse.reflectDir, densityStepSize);
                    bool traceRefractedRay = densityAlongRefractRay * lightResponse.refractWeight > densityAlongReflectRay * lightResponse.reflectWeight;
                    travellingThroughFluid = traceRefractedRay != travellingThroughFluid;

                    // Approximate less interesting path
                    if (traceRefractedRay) light += gatherLight(surfaceInfo.pos, lightResponse.reflectDir) * transmittance * calcTransmittance(densityAlongReflectRay) * lightResponse.reflectWeight;
                    else light += gatherLight(surfaceInfo.pos, lightResponse.refractDir) * transmittance * calcTransmittance(densityAlongRefractRay) * lightResponse.refractWeight;

                    // Set up ray for more interesting path
                    rayPos = surfaceInfo.pos;
                    rayDir = traceRefractedRay ? lightResponse.refractDir : lightResponse.reflectDir;
                    transmittance *= (traceRefractedRay ? lightResponse.refractWeight : lightResponse.reflectWeight);
                }

                // Approximate remaining path
                float densityRemainder = calculateDensityAlongRay(rayPos, rayDir, lightStepSize);
                light += gatherLight(rayPos, rayDir) * transmittance * calcTransmittance(densityRemainder);

                return light;
            }


            float4 frag(v2f i) : SV_Target
            {
                return float4(rayMarchFluid(i.uv, viewMarchStepSize), 1);
            }
            
            ENDCG
        }
    }
}