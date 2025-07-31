using UnityEngine;
using UnityEngine.Rendering;

namespace Project.Fluid.Rendering
{
	public class BilateralSmoother1D
	{
		Material _filterMat;
		readonly int _tempRtId;

		public BilateralSmoother1D()
		{
			_tempRtId = Shader.PropertyToID("Blt1D_TempRT");
		}

		public void Apply(CommandBuffer cmd, RenderTargetIdentifier src, RenderTargetIdentifier dst, RenderTextureDescriptor desc, BilateralSmoother2D.BilateralFilterSettings settings)
		{
			Apply(cmd, src, dst, desc, settings, Vector3.one);
		}

		public void Apply(CommandBuffer cmd, RenderTargetIdentifier src, RenderTargetIdentifier dst, RenderTextureDescriptor desc, BilateralSmoother2D.BilateralFilterSettings settings, Vector3 mask)
		{
			EnsureMaterial();

			_filterMat.SetFloat("_radiusMeters", settings.worldRadius);
			_filterMat.SetInt("_maxPixelRadius", settings.maxScreenSpaceSize);
			_filterMat.SetFloat("_gaussStrength", settings.strength);
			_filterMat.SetFloat("_depthDifferenceScale", settings.diffStrength);
			_filterMat.SetVector("_channelMask", mask);

			cmd.GetTemporaryRT(_tempRtId, desc);

			for (int i = 0; i < settings.iterations; i++)
			{
				// horizontal pass
				cmd.Blit(src, _tempRtId, _filterMat, 0);
				// vertical pass
				cmd.Blit(_tempRtId, dst, _filterMat, 1);
				src = dst;
			}

			cmd.ReleaseTemporaryRT(_tempRtId);
		}

		void EnsureMaterial()
		{
			if (_filterMat == null)
			{
				_filterMat = new Material(Shader.Find("Hidden/BilateralFilter1D"));
			}
		}
	}

	public class Bilateral1D : BilateralSmoother1D {
		public void Smooth(CommandBuffer cmd, RenderTargetIdentifier src, RenderTargetIdentifier dst, RenderTextureDescriptor desc, BilateralSmoother2D.BilateralFilterSettings settings) {
			Apply(cmd, src, dst, desc, settings);
		}
		public void Smooth(CommandBuffer cmd, RenderTargetIdentifier src, RenderTargetIdentifier dst, RenderTextureDescriptor desc, BilateralSmoother2D.BilateralFilterSettings settings, Vector3 mask) {
			Apply(cmd, src, dst, desc, settings, mask);
		}

		// Overloads to accept the 2D wrapper settings struct used elsewhere
		public void Smooth(CommandBuffer cmd, RenderTargetIdentifier src, RenderTargetIdentifier dst, RenderTextureDescriptor desc, BilateralSmooth2D.BilateralFilterSettings settings) {
		    // Map to base struct
		    BilateralSmoother2D.BilateralFilterSettings mapped;
		    mapped.worldRadius = settings.worldRadius != 0 ? settings.worldRadius : settings.WorldRadius;
		    mapped.maxScreenSpaceSize = settings.maxScreenSpaceSize != 0 ? settings.maxScreenSpaceSize : settings.MaxScreenSpaceSize;
		    mapped.strength = settings.strength != 0 ? settings.strength : settings.Strength;
		    mapped.diffStrength = settings.diffStrength != 0 ? settings.diffStrength : settings.DiffStrength;
		    mapped.iterations = settings.iterations != 0 ? settings.iterations : settings.Iterations;
		    Apply(cmd, src, dst, desc, mapped);
		}
		public void Smooth(CommandBuffer cmd, RenderTargetIdentifier src, RenderTargetIdentifier dst, RenderTextureDescriptor desc, BilateralSmooth2D.BilateralFilterSettings settings, Vector3 mask) {
		    BilateralSmoother2D.BilateralFilterSettings mapped;
		    mapped.worldRadius = settings.worldRadius != 0 ? settings.worldRadius : settings.WorldRadius;
		    mapped.maxScreenSpaceSize = settings.maxScreenSpaceSize != 0 ? settings.maxScreenSpaceSize : settings.MaxScreenSpaceSize;
		    mapped.strength = settings.strength != 0 ? settings.strength : settings.Strength;
		    mapped.diffStrength = settings.diffStrength != 0 ? settings.diffStrength : settings.DiffStrength;
		    mapped.iterations = settings.iterations != 0 ? settings.iterations : settings.Iterations;
		    Apply(cmd, src, dst, desc, mapped, mask);
		}
	}
}