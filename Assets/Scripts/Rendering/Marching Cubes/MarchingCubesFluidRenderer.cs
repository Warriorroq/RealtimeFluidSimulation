using UnityEngine;
using Project.Helpers;
using Project.Fluid.Simulation;

namespace Project.Fluid.Rendering
{
    public class MarchingCubesFluidRenderer : MonoBehaviour
    {
        // Public inspector fields
        public float isoLevel;
        public Color col;

        [Header("References")]
        public Simulation3D sim;
        public Shader drawShader;
        public ComputeShader renderArgsCompute;

        // Private fields
        private ComputeBuffer _renderArgs; 
        private MarchingCubesBuilder _marchingCubes;
        private ComputeBuffer _triangleBuffer;
        private Material _drawMat;
        private readonly Bounds _bounds = new Bounds(Vector3.zero, Vector3.one * 1000);

        void Awake()
        {
            if (sim == null)
            {
                sim = FindObjectOfType<Simulation3D>();
            }
            if (_marchingCubes == null)
            {
                _marchingCubes = new MarchingCubesBuilder();
            }
        }

        private void LateUpdate()
        {
            if (sim != null && sim.DensityMap != null)
            {
                RenderFluid(sim.DensityMap);
            }
        }


        private void RenderFluid(RenderTexture densityTexture)
        {
            _triangleBuffer = _marchingCubes.Run(densityTexture, sim.Scale, -isoLevel);

            EnsureDrawMaterial();
            EnsureRenderArgsBuffer();
            UpdateRenderArgsBuffer();

            _drawMat.SetBuffer("vertexBuffer", _triangleBuffer);
            _drawMat.SetColor("tint", col);

            // Draw the mesh using ProceduralIndirect to avoid having to read any data back to the CPU
            Graphics.DrawProceduralIndirect(_drawMat, _bounds, MeshTopology.Triangles, _renderArgs);
        }

        /// <summary>
        /// Creates the draw material if it does not already exist.
        /// </summary>
        private void EnsureDrawMaterial()
        {
            if (_drawMat == null)
            {
                _drawMat = new Material(drawShader);
            }
        }

        /// <summary>
        /// Allocates the indirect arguments buffer used by DrawProceduralIndirect.
        /// </summary>
        private void EnsureRenderArgsBuffer()
        {
            if (_renderArgs == null)
            {
                // Triangle index count, instance count, sub-mesh index, base vertex index, byte offset
                _renderArgs = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
                renderArgsCompute.SetBuffer(0, "argsBuffer", _renderArgs);
            }
        }

        /// <summary>
        /// Updates the indirect arguments buffer with the current triangle count.
        /// </summary>
        private void UpdateRenderArgsBuffer()
        {
            // Copy the current number of triangles from buffer into arguments.
            ComputeBuffer.CopyCount(_triangleBuffer, _renderArgs, 0);
            // Multiply by 3 (one vertex per index) inside the compute shader.
            renderArgsCompute.Dispatch(0, 1, 1, 1);
        }

        private void OnDestroy()
        {
            Release();
        }

        private void Release()
        {
            ComputeHelper.Release(_renderArgs);
            _marchingCubes.Release();
        }

    }
}