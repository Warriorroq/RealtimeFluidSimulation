using Project.Helpers;
using UnityEngine;

namespace Project.Helpers.Internal
{
	public class SpatialShiftCalc
	{
		private readonly ComputeShader _computeShader = ComputeHelper.LoadComputeShader("OffsetMap");
		private static readonly int NUM_INPUTS = Shader.PropertyToID("numInputs");
		private static readonly int OFFSETS = Shader.PropertyToID("offsets");
		private static readonly int SORTED_KEYS = Shader.PropertyToID("sortedKeys");

		private const int INIT_KERNEL = 0;
		private const int OFFSETS_KERNEL = 1;


		// Executes the offset calculation. If 'shouldInit' is true, the offset buffer is initialised prior to the main dispatch.
		public void Run(bool shouldInit, ComputeBuffer keysBuffer, ComputeBuffer offsetBuffer)
		{
			ValidateInputCounts(keysBuffer, offsetBuffer);
			SetNumInputs(keysBuffer);

			if (shouldInit)
			{
				InitializeOffsetBuffer(offsetBuffer);
			}

			DispatchOffsetCalculation(keysBuffer, offsetBuffer);
		}

		void ValidateInputCounts(ComputeBuffer keysBuffer, ComputeBuffer offsetBuffer)
		{
			if (keysBuffer.count != offsetBuffer.count)
			{
				throw new System.Exception("Input buffer count mismatch");
			}
		}

		void SetNumInputs(ComputeBuffer keysBuffer)
		{
			_computeShader.SetInt(NUM_INPUTS, keysBuffer.count);
		}

		void InitializeOffsetBuffer(ComputeBuffer offsetBuffer)
		{
			_computeShader.SetBuffer(INIT_KERNEL, OFFSETS, offsetBuffer);
			ComputeHelper.Dispatch(_computeShader, offsetBuffer.count, kernelIndex: INIT_KERNEL);
		}

		void DispatchOffsetCalculation(ComputeBuffer keysBuffer, ComputeBuffer offsetBuffer)
		{
			_computeShader.SetBuffer(OFFSETS_KERNEL, OFFSETS, offsetBuffer);
			_computeShader.SetBuffer(OFFSETS_KERNEL, SORTED_KEYS, keysBuffer);
			ComputeHelper.Dispatch(_computeShader, keysBuffer.count, kernelIndex: OFFSETS_KERNEL);
		}
	}
}