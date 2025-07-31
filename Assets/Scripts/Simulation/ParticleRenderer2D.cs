using Project.Fluid2D.Simulation;
using Project.Helpers;
using UnityEngine;

namespace Project.Fluid2D.Rendering
{
	public class ParticleRenderer2D : MonoBehaviour
	{
		public Simulation2D sim;
		public Mesh mesh;
		public Shader shader;
		public float scale;
		public Gradient colourMap;
		public int gradientResolution;
		public float velocityDisplayMax;

		Material material;
		ComputeBuffer argsBuffer;
		Bounds bounds;
		Texture2D gradientTexture;
		bool needsUpdate;

		void Awake()
		{
			if (sim == null)
			{
				sim = FindObjectOfType<Simulation2D>();
			}
			if (sim == null)
			{
				Debug.LogWarning("ParticleRenderer2D: Simulation2D not found. Disabling component.");
				enabled = false;
			}
		}

		void Start()
		{
			material = new Material(shader);
			needsUpdate = true;
		}

		void LateUpdate()
		{
			if (shader != null)
			{
				UpdateSettings();
				Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, argsBuffer);
			}
		}

		void UpdateSettings()
		{
			if (sim == null) return;
			material.SetBuffer("positions2D", sim.positionBuffer);
			material.SetBuffer("velocities", sim.velocityBuffer);
			material.SetBuffer("densityData", sim.densityBuffer);

			ComputeHelper.CreateArgsBuffer(ref argsBuffer, mesh, sim.positionBuffer.count);
			bounds = new Bounds(Vector3.zero, Vector3.one * 10000);

			if (needsUpdate)
			{
				needsUpdate = false;
				TextureFromGradient(ref gradientTexture, gradientResolution, colourMap);
				material.SetTexture("colourMap", gradientTexture);

				material.SetFloat("scale", scale);
				material.SetFloat("velocityMax", velocityDisplayMax);
			}
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
					new GradientColorKey[] { new GradientColorKey(Color.black, 0), new GradientColorKey(Color.black, 1) },
					new GradientAlphaKey[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }
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

		void OnValidate()
		{
			needsUpdate = true;
		}

		void OnDestroy()
		{
			ComputeHelper.Release(argsBuffer);
		}
	}
}