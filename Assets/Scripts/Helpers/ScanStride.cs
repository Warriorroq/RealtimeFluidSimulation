using Project.Helpers;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Project.GPUSorting
{
    public class ScanStride
    {
        private const int scanKernel = 0;        // localPrefixSum
        private const int combineKernel = 1;     // addGroupOffsets

        private static readonly int dataID = Shader.PropertyToID("data");
        private static readonly int workGroupTotalsID = Shader.PropertyToID("workGroupTotals");
        private static readonly int totalCountID = Shader.PropertyToID("totalCount");
        private readonly ComputeShader cs;

        private readonly Dictionary<int, ComputeBuffer> freeBuffers = new();

        public ScanStride()
        {
            cs = ComputeHelper.LoadComputeShader("ScanStrideTest");
        }

        public void Run(ComputeBuffer elements)
        {
            // Calculate number of groups/blocks to run in shader
            cs.GetKernelThreadGroupSizes(scanKernel, out uint threadsPerGroup, out _, out _);
            int numGroups = Mathf.CeilToInt(elements.count / 2f / threadsPerGroup);

            if (!freeBuffers.TryGetValue(numGroups, out ComputeBuffer groupSumBuffer))
            {
                groupSumBuffer = ComputeHelper.CreateStructuredBuffer<uint>(numGroups);
                freeBuffers.Add(numGroups, groupSumBuffer);
            }

            cs.SetBuffer(scanKernel, dataID, elements);
            cs.SetBuffer(scanKernel, workGroupTotalsID, groupSumBuffer);
            cs.SetInt(totalCountID, elements.count);

            // Run scan kernel
            cs.Dispatch(scanKernel, numGroups, 1, 1);

            // If more than one group, then the groups need to be adjusted by adding on all preceding groupSums to each group
            // This can be done efficiently by first calculating the scan of the groupSums
            if (numGroups > 1)
            {
                // Recursively calculate scan groupSums
                Run(groupSumBuffer);

                // Add groupSums
                cs.SetBuffer(combineKernel, dataID, elements);
                cs.SetBuffer(combineKernel, workGroupTotalsID, groupSumBuffer);
                cs.SetInt(totalCountID, elements.count);
                cs.Dispatch(combineKernel, numGroups, 1, 1);
            }
        }

        public void Release()
        {
            foreach (var b in freeBuffers)
            {
                ComputeHelper.Release(b.Value);
            }
        }
    }
}