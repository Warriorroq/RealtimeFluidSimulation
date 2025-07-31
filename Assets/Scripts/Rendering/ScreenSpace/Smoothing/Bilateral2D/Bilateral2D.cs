using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Project.Fluid.Rendering
{
	/// <summary>
	/// Performs a separable bilateral smoothing pass on a 2D texture.
	/// </summary>
	public class BilateralSmoother2D
	{
		// Material used for filtering
		Material _filterMat;

		// Unique id for the temporary render target
		readonly int _tempRtId;

		public BilateralSmoother2D()
		{
			_tempRtId = Shader.PropertyToID("BltSmoother_TempRT");
		}

		// Public entry point – smooth with default full-strength mask (Vector3.one)
		public void Apply(CommandBuffer cmd, RenderTargetIdentifier src, RenderTargetIdentifier dst, RenderTextureDescriptor desc, BilateralFilterSettings settings)
		{
			Apply(cmd, src, dst, desc, settings, Vector3.one);
		}

		// Public entry point – smooth using a per-channel mask
		public void Apply(CommandBuffer cmd, RenderTargetIdentifier src, RenderTargetIdentifier dst, RenderTextureDescriptor desc, BilateralFilterSettings settings, Vector3 mask)
		{
			EnsureMaterial();

			PopulateShaderUniforms(mask, settings);

			cmd.GetTemporaryRT(_tempRtId, desc);

			var readTarget = src;
			RenderTargetIdentifier writeTarget = new RenderTargetIdentifier(_tempRtId);

			for (int iteration = 0; iteration < settings.iterations; iteration++)
			{
				ProcessIteration(cmd, ref readTarget, ref writeTarget);
			}

			cmd.Blit(readTarget, dst);
			cmd.ReleaseTemporaryRT(_tempRtId);
		}

		// Run a single bilateral filter iteration
		void ProcessIteration(CommandBuffer cmd, ref RenderTargetIdentifier read, ref RenderTargetIdentifier write)
		{
			cmd.Blit(read, write, _filterMat);
			// swap for next pass
			(read, write) = (write, read);
		}

		// Make sure the material exists and is wired up to the hidden shader
		void EnsureMaterial()
		{
			if (_filterMat == null)
			{
				_filterMat = new Material(Shader.Find("Hidden/BilateralFilter2D"));
			}
		}

		// Push user-defined settings down to the shader as uniform values
		void PopulateShaderUniforms(Vector3 mask, BilateralFilterSettings s)
		{
			_filterMat.SetFloat("_radiusMeters", s.worldRadius);
			_filterMat.SetInt("_maxPixelRadius", s.maxScreenSpaceSize);
			_filterMat.SetFloat("_gaussStrength", s.strength);
			_filterMat.SetFloat("_depthDifferenceScale", s.diffStrength);
			_filterMat.SetVector("_channelMask", mask);
		}

		// Exposed parameters for the filter. All public fields are lowercase to satisfy the user's style guideline.
		[System.Serializable]
		public struct BilateralFilterSettings
		{
			[FormerlySerializedAs("WorldRadius")] public float worldRadius;
			[FormerlySerializedAs("MaxScreenSpaceSize")] public int maxScreenSpaceSize;

			[Range(0, 1)] public float strength;
			public float diffStrength;
			public int iterations;
		}
	}

	public class BilateralSmooth2D : BilateralSmoother2D {
		public new struct BilateralFilterSettings {
			public float worldRadius;
			public int maxScreenSpaceSize;
			[Range(0,1)] public float strength;
			public float diffStrength;
			public int iterations;

			public float WorldRadius;
			public int MaxScreenSpaceSize;
			public float Strength;
			public float DiffStrength;
			public int Iterations;
		}

		public void Smooth(CommandBuffer cmd, RenderTargetIdentifier src, RenderTargetIdentifier dst, RenderTextureDescriptor desc, BilateralFilterSettings settings) {
			// Map legacy struct to base struct
			Project.Fluid.Rendering.BilateralSmoother2D.BilateralFilterSettings mapped;
			mapped.worldRadius = settings.worldRadius != 0 ? settings.worldRadius : settings.WorldRadius;
			mapped.maxScreenSpaceSize = settings.maxScreenSpaceSize != 0 ? settings.maxScreenSpaceSize : settings.MaxScreenSpaceSize;
			mapped.strength = settings.strength != 0 ? settings.strength : settings.Strength;
			mapped.diffStrength = settings.diffStrength != 0 ? settings.diffStrength : settings.DiffStrength;
			mapped.iterations = settings.iterations != 0 ? settings.iterations : settings.Iterations;
			Apply(cmd, src, dst, desc, mapped);
		}

		public void Smooth(CommandBuffer cmd, RenderTargetIdentifier src, RenderTargetIdentifier dst, RenderTextureDescriptor desc, BilateralFilterSettings settings, Vector3 mask) {
			Project.Fluid.Rendering.BilateralSmoother2D.BilateralFilterSettings mapped;
			mapped.worldRadius = settings.worldRadius != 0 ? settings.worldRadius : settings.WorldRadius;
			mapped.maxScreenSpaceSize = settings.maxScreenSpaceSize != 0 ? settings.maxScreenSpaceSize : settings.MaxScreenSpaceSize;
			mapped.strength = settings.strength != 0 ? settings.strength : settings.Strength;
			mapped.diffStrength = settings.diffStrength != 0 ? settings.diffStrength : settings.DiffStrength;
			mapped.iterations = settings.iterations != 0 ? settings.iterations : settings.Iterations;
			Apply(cmd, src, dst, desc, mapped, mask);
		}
	}
}