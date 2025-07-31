using Project.Helpers;
using UnityEngine;
using UnityEngine.Rendering;
using Project.Fluid.Simulation;

namespace Project.Fluid.Rendering
{
	public class FoamTrial : MonoBehaviour
	{
		public float scale;
		public float debugParam;
		public bool autoDraw;

		[Header("References")]
		public Shader shaderBillboard;
		public ComputeShader copyCountToArgsCompute;

		Simulation3D sim;
		Material mat;
		Mesh mesh;
		ComputeBuffer argsBuffer;
		Bounds bounds;

		void Awake()
		{
			sim = FindObjectOfType<Simulation3D>();
			if (sim == null) { enabled = false; return; }
			sim.SimulationInitCompleted += Init;
		}

		void Init(Simulation3D sim)
		{
			mat = new Material(shaderBillboard);
			mesh = MeshBuilder.GenerateQuadMesh();
            bounds = new Bounds(Vector3.zero, Vector3.one * 1000);

			ComputeHelper.CreateArgsBuffer(ref argsBuffer, mesh, sim.maxFoamParticleCount);
			copyCountToArgsCompute.SetBuffer(0, "countBuffer", sim.foamCountBuffer);
			copyCountToArgsCompute.SetBuffer(0, "argsBuffer", argsBuffer);
			mat.SetBuffer("Particles", sim.foamBuffer);
		}

		void LateUpdate()
		{
			if (sim.foamActive)
			{
				mat.SetFloat("debugParam", debugParam);
				mat.SetInt("bubbleClassifyMinNeighbours", sim.bubbleClassifyMinNeighbours);
				mat.SetInt("sprayClassifyMaxNeighbours", sim.sprayClassifyMaxNeighbours);
				mat.SetFloat("scale", scale * 0.01f);

				if (autoDraw)
				{
					copyCountToArgsCompute.Dispatch(0, 1, 1, 1);
					Graphics.DrawMeshInstancedIndirect(mesh, 0, mat, bounds, argsBuffer);
				}
			}
		}

		public void RenderWithCmdBuffer(CommandBuffer cmd)
		{
			cmd.DispatchCompute(copyCountToArgsCompute, 0, 1, 1, 1);
			cmd.DrawMeshInstancedIndirect(mesh, 0, mat, 0, argsBuffer);
		}


		private void OnDestroy()
		{
			ComputeHelper.Release(argsBuffer);
		}
	}
}