using Project.Helpers;
using UnityEngine;
using Project.Fluid.Simulation;

namespace Project.Fluid.Rendering
{

	public class ParticleRenderer3D : MonoBehaviour
	{
		public enum DisplayMode
		{
			None,
			Shaded3D,
			Billboard
		}

		[Header("Settings")] public DisplayMode mode;
		public float scale;
		public Gradient colourMap;
		public int gradientResolution;
		public float velocityDisplayMax;
		public int meshResolution;

		[Header("References")] public Simulation3D sim;
		public Shader shaderShaded;
		public Shader shaderBillboard;

		Mesh _mesh;
		Material _material;
		ComputeBuffer _argsBuffer;
		Texture2D _gradientTexture;
		DisplayMode _previousMode;
		bool _requiresUpdate;

		void Awake()
		{
			if (sim == null)
			{
				sim = FindObjectOfType<Simulation3D>();
			}
			if (sim == null)
			{
				Debug.LogWarning("ParticleRenderer3D: Simulation3D not found. Disabling component.");
				enabled = false;
			}
		}

		void LateUpdate()
		{
			RefreshConfiguration();
			RenderInstances();
		}

		void RefreshConfiguration()
		{
			HandleModeChange();
			UpdateMaterialSettings();
		}

		void HandleModeChange()
		{
			if (_previousMode == mode) return;

			_previousMode = mode;
			if (mode == DisplayMode.None) return;

			if (sim == null) return;
			_mesh = mode == DisplayMode.Billboard ? MeshBuilder.GenerateQuadMesh() : MeshBuilder.GenerateSphereMesh(meshResolution);
			ComputeHelper.CreateArgsBuffer(ref _argsBuffer, _mesh, sim.positionBuffer.count);

			_material = mode switch
			{
				DisplayMode.Shaded3D => new Material(shaderShaded),
				DisplayMode.Billboard => new Material(shaderBillboard),
				_ => null
			};

			_material.SetBuffer("positions", sim.positionBuffer);
			_material.SetBuffer("velocities", sim.velocityBuffer);
			_material.SetBuffer("DebugBuffer", sim.debugBuffer);
		}

		void UpdateMaterialSettings()
		{
			if (_material == null) return;

			if (_requiresUpdate)
			{
				_requiresUpdate = false;
				TextureFromGradient(ref _gradientTexture, gradientResolution, colourMap);
				_material.SetTexture("colourMap", _gradientTexture);
			}

			_material.SetFloat("scale", scale * 0.01f);
			_material.SetFloat("velocityMax", velocityDisplayMax);

			// Temporarily reset scale to compute accurate matrix
			Vector3 cachedScale = transform.localScale;
			transform.localScale = Vector3.one;
			Matrix4x4 localToWorld = transform.localToWorldMatrix;
			transform.localScale = cachedScale;

			_material.SetMatrix("localToWorld", localToWorld);
		}

		void RenderInstances()
		{
			if (mode == DisplayMode.None || _mesh == null || _material == null) return;
			Bounds drawBounds = new Bounds(Vector3.zero, Vector3.one * 10000);
			Graphics.DrawMeshInstancedIndirect(_mesh, 0, _material, drawBounds, _argsBuffer);
		}

		public static void TextureFromGradient(ref Texture2D texture, int width, Gradient gradient, FilterMode filterMode = FilterMode.Bilinear)
		{
			if (texture == null)
			{
				texture = new Texture2D(width, 1);
			}
			else if (texture.width != width)
			{
				texture.Reinitialize(width, 1);
			}

			if (gradient == null)
			{
				gradient = new Gradient();
				gradient.SetKeys(
					new GradientColorKey[] { new(Color.black, 0), new(Color.black, 1) },
					new GradientAlphaKey[] { new(1, 0), new(1, 1) }
				);
			}

			texture.wrapMode = TextureWrapMode.Clamp;
			texture.filterMode = filterMode;

			Color[] cols = new Color[width];
			for (int i = 0; i < cols.Length; i++)
			{
				float t = i / (cols.Length - 1f);
				cols[i] = gradient.Evaluate(t);
			}

			texture.SetPixels(cols);
			texture.Apply();
		}

		private void OnValidate()
		{
			_requiresUpdate = true;
		}

		void OnDestroy()
		{
			ComputeHelper.Release(_argsBuffer);
		}
	}
}