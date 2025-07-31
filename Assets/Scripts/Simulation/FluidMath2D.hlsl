const float POLY6_SCALING_FACTOR;
const float SPIKY_POW3_SCALING_FACTOR;
const float SPIKY_POW2_SCALING_FACTOR;
const float SPIKY_POW3_DERIVATIVE_SCALING_FACTOR;
const float SPIKY_POW2_DERIVATIVE_SCALING_FACTOR;

float smoothingKernelPoly6(float dst, float radius)
{
	if (dst < radius)
	{
		float v = radius * radius - dst * dst;
		return v * v * v * POLY6_SCALING_FACTOR;
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
