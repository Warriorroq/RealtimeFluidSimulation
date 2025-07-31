static const float PI = 3.1415926;

const float SPIKY_POW2_SCALING_FACTOR;
const float SPIKY_POW3_SCALING_FACTOR;
const float SPIKY_POW2_DERIVATIVE_SCALING_FACTOR;
const float SPIKY_POW3_DERIVATIVE_SCALING_FACTOR;

float linearKernel(float dst, float radius)
{
    if (dst < radius)
    {
        return 1 - dst / radius;
    }
    return 0;
}

float smoothingKernelPoly6(float dst, float radius)
{
    if (dst < radius)
    {
        float scale = 315 / (64 * PI * pow(abs(radius), 9));
        float v = radius * radius - dst * dst;
        return v * v * v * scale;
    }
    return 0;
}

float spikyKernelPow3(float dst, float radius)
{
    if (dst < radius)
    {
        float v = radius - dst;
        return v * v * v * SPIKY_POW3_SCALING_FACTOR;
    }
    return 0;
}

// Integrate[(h-r)^2 r^2 Sin[θ], {r, 0, h}, {θ, 0, π}, {φ, 0, 2*π}]
float spikyKernelPow2(float dst, float radius)
{
    if (dst < radius)
    {
        float v = radius - dst;
        return v * v * SPIKY_POW2_SCALING_FACTOR;
    }
    return 0;
}

float derivativeSpikyPow3(float dst, float radius)
{
    if (dst <= radius)
    {
        float v = radius - dst;
        return -v * v * SPIKY_POW3_DERIVATIVE_SCALING_FACTOR;
    }
    return 0;
}

float derivativeSpikyPow2(float dst, float radius)
{
    if (dst <= radius)
    {
        float v = radius - dst;
        return -v * SPIKY_POW2_DERIVATIVE_SCALING_FACTOR;
    }
    return 0;
}

float densityKernel(float dst, float radius)
{
    //return smoothingKernelPoly6(dst, radius);
    return spikyKernelPow2(dst, radius);
}

float nearDensityKernel(float dst, float radius)
{
    return spikyKernelPow3(dst, radius);
}

float densityDerivative(float dst, float radius)
{
    return derivativeSpikyPow2(dst, radius);
}

float nearDensityDerivative(float dst, float radius)
{
    return derivativeSpikyPow3(dst, radius);
}

