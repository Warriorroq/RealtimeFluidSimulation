using UnityEngine;
using Project.Fluid.Simulation;

namespace Project.Fluid.Rendering
{
	[ImageEffectAllowedInSceneView]
	public class RaymarchRenderer : MonoBehaviour
	{
		[Header("Settings")]
		public float densityOffset = 150;
		public int numRefractions = 4;
		public Vector3 extinctionCoefficients;
		public float densityMultiplier = 0.001f;
		[Min(0.01f)] public float stepSize = 0.02f;
		public float lightStepSize = 0.4f;
		[Min(1)] public float indexOfRefraction = 1.33f;
		public Vector3 testParams;
		public EnvironmentSettings environmentSettings;

		[Header("References")]
		public Simulation3D sim;
		public Transform cubeTransform;
		public Shader shader;

		Material _rayMat;

		void Start()
		{
			if (shader == null)
			{
				Debug.LogError("RaymarchRenderer: Shader not assigned.");
				enabled = false;
				return;
			}
			_rayMat = new Material(shader);
			if (sim == null)
			{
				sim = FindObjectOfType<Simulation3D>();
			}
			if (sim == null)
			{
				Debug.LogWarning("RaymarchRenderer: Simulation3D not found yet.");
			}
			Camera.main.depthTextureMode = DepthTextureMode.Depth;
		}

		[ImageEffectOpaque]
		void OnRenderImage(RenderTexture inTex, RenderTexture outTex)
		{
			if (sim != null && sim.DensityMap != null && _rayMat != null && cubeTransform != null)
			{
				ApplyShaderSettings();
				Graphics.Blit(inTex, outTex, _rayMat);
			}
			else
			{
				Graphics.Blit(inTex, outTex);
			}
		}

		void ApplyShaderSettings()
		{
			SetEnvironmentUniforms();
			SetSimulationUniforms();
			SetSceneUniforms();
		}

		void SetEnvironmentUniforms()
		{
			ApplyEnvironmentUniforms(_rayMat, environmentSettings);
		}

		void SetSimulationUniforms()
		{
			if (sim == null) return;
			_rayMat.SetTexture("DensityMap", sim.DensityMap);
			_rayMat.SetVector("boundsSize", sim.Scale);
			_rayMat.SetFloat("volumeValueOffset", densityOffset);
			_rayMat.SetVector("testParams", testParams);
			_rayMat.SetFloat("indexOfRefraction", indexOfRefraction);
			_rayMat.SetFloat("densityMultiplier", densityMultiplier / 1000);
			_rayMat.SetFloat("viewMarchStepSize", stepSize);
			_rayMat.SetFloat("lightStepSize", lightStepSize);
			_rayMat.SetInt("numRefractions", numRefractions);
			_rayMat.SetVector("extinctionCoeff", extinctionCoefficients);
		}

		void SetSceneUniforms()
		{
			_rayMat.SetMatrix("cubeLocalToWorld", Matrix4x4.TRS(cubeTransform.position, cubeTransform.rotation, cubeTransform.localScale / 2));
			_rayMat.SetMatrix("cubeWorldToLocal", Matrix4x4.TRS(cubeTransform.position, cubeTransform.rotation, cubeTransform.localScale / 2).inverse);

			Vector3 flSize = new Vector3(30, 0.05f, 30);
			float flHeight = -sim.Scale.y / 2 + sim.transform.position.y - flSize.y / 2;
			_rayMat.SetVector("floorPos", new Vector3(0, flHeight, 0));
			_rayMat.SetVector("floorSize", flSize);
		}

		public static void ApplyEnvironmentUniforms(Material mat, EnvironmentSettings env)
		{
			if (env.noiseScale <= 0) env.noiseScale = 3;
			if (env.secondaryNoiseScale <= 0) env.secondaryNoiseScale = env.noiseScale * 0.5f;
			if (env.secondaryNoiseWeight == 0) env.secondaryNoiseWeight = 0.5f;
			if (env.gradientStrength == 0) env.gradientStrength = 1;
			if (env.colorVariation == Vector3.zero) env.colorVariation = new Vector3(0.2f, 0.2f, 0.2f);

			mat.SetColor("baseColor", env.baseColor);
			mat.SetVector("colorVariation", env.colorVariation);
			mat.SetFloat("noiseScale", env.noiseScale);
			mat.SetFloat("secondaryNoiseScale", env.secondaryNoiseScale);
			mat.SetFloat("secondaryNoiseWeight", env.secondaryNoiseWeight);
			mat.SetColor("cornerColorBL", env.cornerColorBL);
			mat.SetColor("cornerColorBR", env.cornerColorBR);
			mat.SetColor("cornerColorTL", env.cornerColorTL);
			mat.SetColor("cornerColorTR", env.cornerColorTR);
			mat.SetFloat("gradientStrength", env.gradientStrength);
			mat.SetVector("dirToSun", -env.light.transform.forward);
		}

		[System.Serializable]
		public struct EnvironmentSettings
		{
			public Color baseColor;
			public Vector3 colorVariation;
			public float noiseScale;
			public float secondaryNoiseScale;
			public float secondaryNoiseWeight;
			public Color cornerColorBL;
			public Color cornerColorBR;
			public Color cornerColorTL;
			public Color cornerColorTR;
			public float gradientStrength;
			public Light light;
		}
	}
}