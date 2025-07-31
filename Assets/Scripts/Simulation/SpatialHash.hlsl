static const int2 OFFSETS2D[9] =
{
	int2(-1, 1),
	int2(0, 1),
	int2(1, 1),
	int2(-1, 0),
	int2(0, 0),
	int2(1, 0),
	int2(-1, -1),
	int2(0, -1),
	int2(1, -1),
};

// Constants used for hashing
static const uint HASH_K1 = 15823;
static const uint HASH_K2 = 9737333;

// Convert floating point position into an integer cell coordinate
int2 getCell2D(float2 position, float radius)
{
	return (int2)floor(position / radius);
}

// Hash cell coordinate to a single unsigned integer
uint hashCell2D(int2 cell)
{
	cell = (uint2)cell;
	uint a = cell.x * HASH_K1;
	uint b = cell.y * HASH_K2;
	return (a + b);
}

uint keyFromHash(uint hash, uint tableSize)
{
	return hash % tableSize;
}
