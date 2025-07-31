using Project.GPUSorting;
using UnityEngine;
using Project.Helpers.Internal;

namespace Project.Helpers
{
	public class SpatialIndex
	{
		public ComputeBuffer spatialKeys;
		public ComputeBuffer spatialIndices;
		public ComputeBuffer spatialOffsets;

		private readonly GPUCountSort _gpuSort = new();
		private readonly SpatialShiftCalc _spatialOffsetsCalc = new();

		public SpatialIndex(int size)
		{
			AllocateBuffers(size);
		}

		public void Resize(int newSize)
		{
			AllocateBuffers(newSize);
		}

		// Populates the offset table after sorting the key buffer. The accompanying index buffer can
		// be used to reorder any related data buffer in the same fashion.
		public void Run()
		{
			_gpuSort.Run(spatialIndices, spatialKeys, (uint)(spatialKeys.count - 1));
			_spatialOffsetsCalc.Run(true, spatialKeys, spatialOffsets);
		}

		public void Release()
		{
			_gpuSort.Release();
			ComputeHelper.Release(spatialKeys, spatialIndices, spatialOffsets);
		}

		private void AllocateBuffers(int bufferSize)
		{
			ComputeHelper.CreateStructuredBuffer<uint>(ref spatialKeys, bufferSize);
			ComputeHelper.CreateStructuredBuffer<uint>(ref spatialIndices, bufferSize);
			ComputeHelper.CreateStructuredBuffer<uint>(ref spatialOffsets, bufferSize);
		}
	}
}