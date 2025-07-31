using System;
using UnityEngine;
using Project.Helpers;
using System.Linq;

namespace Project.Fluid.Rendering
{

    public class MarchingCubesBuilder
    {
        // Private fields
        private readonly ComputeShader _marchingCubesCS;
        private readonly ComputeBuffer _lutBuffer;
        private ComputeBuffer _triangleBuffer;

        public MarchingCubesBuilder()
        {
            // Try load compute shader from Resources first
            _marchingCubesCS = Resources.Load<ComputeShader>("MarchingCubeKernel");
#if UNITY_EDITOR
            // Fallback to AssetDatabase when running in the editor (asset can be anywhere in the project)
            if (_marchingCubesCS == null)
            {
                _marchingCubesCS = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Assets/Scripts/Rendering/Marching Cubes/MarchingCubeKernel.compute");
            }
#endif

            if (_marchingCubesCS == null)
            {
                Debug.LogError("MarchingCubes compute shader not found. Ensure the asset is located in a Resources folder or update the path in MarchingCubes.cs.");
            }

            // Lookup table contains pre-computed triangle indices for each of the 256 cube configurations
            int[] lutVals = LoadLookupTable();
            _lutBuffer = ComputeHelper.CreateStructuredBuffer(lutVals);

        }

        /// <summary>
        /// Loads and parses the marching-cubes lookup table embedded as a text asset.
        /// </summary>
        /// <returns>Array of triangle indices.</returns>
        private static int[] LoadLookupTable()
        {
            TextAsset lutAsset = Resources.Load<TextAsset>("MarchingCubesLUT");
#if UNITY_EDITOR
            if (lutAsset == null)
            {
                lutAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(
                    "Assets/Scripts/Rendering/Marching Cubes/MarchingCubesLUT.txt");
            }
#endif
            if (lutAsset == null)
            {
                Debug.LogError("MarchingCubes LUT asset not found. Ensure the file is located in a Resources folder or update the path in MarchingCubes.cs.");
                return Array.Empty<int>();
            }

            string lutString = lutAsset.text;
            return lutString.Trim().Split(',').Select(int.Parse).ToArray();
        }

        private void ApplyComputeSettings(RenderTexture densityMap, Vector3 scale, float isoLevel, ComputeBuffer triangleBuffer)
        {
            _marchingCubesCS.SetBuffer(0, "triStream", triangleBuffer);
            _marchingCubesCS.SetBuffer(0, "lut", _lutBuffer);

            _marchingCubesCS.SetTexture(0, "DensityMap", densityMap);
            _marchingCubesCS.SetInts("densityMapSize", densityMap.width, densityMap.height, densityMap.volumeDepth);
            _marchingCubesCS.SetFloat("isoLevel", isoLevel);
            _marchingCubesCS.SetVector("scale", scale);
        }

        public ComputeBuffer Run(RenderTexture densityTexture, Vector3 scale, float isoLevel)
        {
            CreateTriangleBuffer(densityTexture.width);
            ApplyComputeSettings(densityTexture, scale, isoLevel, _triangleBuffer);

            int numVoxelsPerX = densityTexture.width - 1;
            int numVoxelsPerY = densityTexture.height - 1;
            int numVoxelsPerZ = densityTexture.volumeDepth - 1;
            ComputeHelper.Dispatch(_marchingCubesCS, numVoxelsPerX, numVoxelsPerY, numVoxelsPerZ, 0);

            return _triangleBuffer;
        }

        private void CreateTriangleBuffer(int resolution, bool warnIfExceedsMaxTheoreticalSize = false)
        {
            int numVoxelsPerAxis = resolution - 1;
            int numVoxels = numVoxelsPerAxis * numVoxelsPerAxis * numVoxelsPerAxis;
            int maxTriangleCount = numVoxels * 5;
            int byteSize = ComputeHelper.GetStride<Triangle>();
            const uint MAX_BYTES = 2147483648;
            uint maxEntries = MAX_BYTES / (uint)byteSize;
            if (maxEntries < maxTriangleCount && warnIfExceedsMaxTheoreticalSize)
            {
                Debug.Log("Triangle count too large for buffer.");
            }

            ComputeHelper.CreateAppendBuffer<Triangle>(ref _triangleBuffer, Math.Min((int)maxEntries, maxTriangleCount));
        }

        public void Release()
        {
            ComputeHelper.Release(_triangleBuffer, _lutBuffer);
        }


        public struct Vertex
        {
            public Vector3 position;
            public Vector3 normal;
        }

        public struct Triangle
        {
            public Vertex vertexA;
            public Vertex vertexB;
            public Vertex vertexC;
        }
    }
}