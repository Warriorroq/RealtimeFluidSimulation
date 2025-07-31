using UnityEngine;
using Project.Helpers;

namespace Project.GPUSorting
{
    public class GPUCountSort
    {
        // Shader property IDs
        private static readonly int ID_ITEM_BUFFER = Shader.PropertyToID("itemBuffer");
        private static readonly int ID_KEY_BUFFER = Shader.PropertyToID("keyBuffer");
        private static readonly int ID_SORTED_ITEM_BUFFER = Shader.PropertyToID("sortedItemBuffer");
        private static readonly int ID_SORTED_KEY_BUFFER = Shader.PropertyToID("sortedKeyBuffer");
        private static readonly int ID_PREFIX_SUM = Shader.PropertyToID("prefixSum");
        private static readonly int ID_ELEMENT_COUNT = Shader.PropertyToID("elementCount");

        // Kernel indices (order must match compute shader listing)
        private const int INIT_BUFFERS_KERNEL = 0;
        private const int TALLY_KEYS_KERNEL = 1;
        private const int SCATTER_KERNEL = 2;
        private const int COPY_TO_SOURCE_KERNEL = 3;

        private readonly ScanStride _scan = new();
        private readonly ComputeShader _cs = ComputeHelper.LoadComputeShader("CountArrange");

        private ComputeBuffer _sortedItemBuffer;
        private ComputeBuffer _sortedKeyBuffer;
        private ComputeBuffer _prefixSumBuffer;

        /// <summary>
        /// Sorts an index buffer using a corresponding key buffer.
        /// </summary>
        public void Run(ComputeBuffer itemsBuffer, ComputeBuffer keysBuffer, uint maxKeyValue)
        {
            int count = itemsBuffer.count;

            PrepareBuffers(count, maxKeyValue);
            BindUserBuffers(itemsBuffer, keysBuffer, count);

            Dispatch(count);
        }

        private void PrepareBuffers(int count, uint maxKeyValue)
        {
            if (ComputeHelper.CreateStructuredBuffer<uint>(ref _sortedItemBuffer, count))
            {
                _cs.SetBuffer(SCATTER_KERNEL, ID_SORTED_ITEM_BUFFER, _sortedItemBuffer);
                _cs.SetBuffer(COPY_TO_SOURCE_KERNEL, ID_SORTED_ITEM_BUFFER, _sortedItemBuffer);
            }

            if (ComputeHelper.CreateStructuredBuffer<uint>(ref _sortedKeyBuffer, count))
            {
                _cs.SetBuffer(SCATTER_KERNEL, ID_SORTED_KEY_BUFFER, _sortedKeyBuffer);
                _cs.SetBuffer(COPY_TO_SOURCE_KERNEL, ID_SORTED_KEY_BUFFER, _sortedKeyBuffer);
            }

            if (ComputeHelper.CreateStructuredBuffer<uint>(ref _prefixSumBuffer, (int)maxKeyValue + 1))
            {
                _cs.SetBuffer(INIT_BUFFERS_KERNEL, ID_PREFIX_SUM, _prefixSumBuffer);
                _cs.SetBuffer(TALLY_KEYS_KERNEL, ID_PREFIX_SUM, _prefixSumBuffer);
                _cs.SetBuffer(SCATTER_KERNEL, ID_PREFIX_SUM, _prefixSumBuffer);
            }
        }

        private void BindUserBuffers(ComputeBuffer itemsBuffer, ComputeBuffer keysBuffer, int count)
        {
            _cs.SetBuffer(INIT_BUFFERS_KERNEL, ID_ITEM_BUFFER, itemsBuffer);
            _cs.SetBuffer(SCATTER_KERNEL, ID_ITEM_BUFFER, itemsBuffer);
            _cs.SetBuffer(COPY_TO_SOURCE_KERNEL, ID_ITEM_BUFFER, itemsBuffer);

            _cs.SetBuffer(TALLY_KEYS_KERNEL, ID_KEY_BUFFER, keysBuffer);
            _cs.SetBuffer(SCATTER_KERNEL, ID_KEY_BUFFER, keysBuffer);
            _cs.SetBuffer(COPY_TO_SOURCE_KERNEL, ID_KEY_BUFFER, keysBuffer);

            _cs.SetInt(ID_ELEMENT_COUNT, count);
        }

        private void Dispatch(int count)
        {
            ComputeHelper.Dispatch(_cs, count, kernelIndex: INIT_BUFFERS_KERNEL);
            ComputeHelper.Dispatch(_cs, count, kernelIndex: TALLY_KEYS_KERNEL);

            _scan.Run(_prefixSumBuffer);

            ComputeHelper.Dispatch(_cs, count, kernelIndex: SCATTER_KERNEL);
            ComputeHelper.Dispatch(_cs, count, kernelIndex: COPY_TO_SOURCE_KERNEL);
        }

        public void Release()
        {
            ComputeHelper.Release(_sortedItemBuffer, _sortedKeyBuffer, _prefixSumBuffer);
            _scan.Release();
        }
    }
}