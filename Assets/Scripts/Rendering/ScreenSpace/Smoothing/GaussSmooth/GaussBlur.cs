using UnityEngine;
using UnityEngine.Rendering;

namespace Project.Fluid.Rendering
{
    public class GaussBlur
    {
        // Reusable material instance used for the blur passes
        Material _material;

        // ID for the temporary render-target used during the two-pass blur
        readonly int _firstPassRT;

        public GaussBlur()
        {
            _firstPassRT = Shader.PropertyToID("GaussSmooth_FirstPassRT_ID");
        }

        /// <summary>
        /// Applies a separable two-pass Gaussian blur to <paramref name="source"/> and stores the
        /// result in <paramref name="target"/>.
        /// </summary>
        /// <remarks>
        /// This overload uses a <see cref="Vector3.one"/> mask to apply the blur equally to all
        /// colour channels.
        /// </remarks>
        public void Smooth(CommandBuffer commandBuffer, RenderTargetIdentifier source, RenderTargetIdentifier target, RenderTextureDescriptor descriptor, GaussianBlurSettings settings)
        {
            Smooth(commandBuffer, source, target, descriptor, settings, Vector3.one);
        }

        /// <summary>
        /// Applies a separable two-pass Gaussian blur to <paramref name="source"/> and stores the
        /// result in <paramref name="target"/>.
        /// </summary>
        public void Smooth(CommandBuffer commandBuffer, RenderTargetIdentifier source, RenderTargetIdentifier target, RenderTextureDescriptor descriptor, GaussianBlurSettings settings, Vector3 smoothMask)
        {
            EnsureMaterial();
            ApplyMaterialSettings(settings, smoothMask);
            ExecuteBlur(commandBuffer, source, target, descriptor, settings.iterations);
        }

        #region Private helpers

        void EnsureMaterial()
        {
            if (_material == null)
            {
                _material = new Material(Shader.Find("Hidden/GaussSmooth"));
            }
        }

        void ApplyMaterialSettings(GaussianBlurSettings settings, Vector3 smoothMask)
        {
            _material.SetFloat("radius", settings.radius);
            _material.SetInt("maxScreenSpaceRadius", settings.maxScreenSpaceRadius);
            _material.SetFloat("strength", settings.strength);
            _material.SetVector("smoothMask", smoothMask);
            _material.SetInt("useWorldSpaceRadius", settings.useWorldSpaceRadius ? 1 : 0);
        }

        void ExecuteBlur(CommandBuffer commandBuffer, RenderTargetIdentifier source, RenderTargetIdentifier target, RenderTextureDescriptor descriptor, int iterationCount)
        {
            // Allocate a temporary texture for the horizontal and vertical passes.
            commandBuffer.GetTemporaryRT(_firstPassRT, descriptor);

            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                ApplyGaussianIteration(commandBuffer, ref source, target);
            }

            commandBuffer.ReleaseTemporaryRT(_firstPassRT);
        }

        void ApplyGaussianIteration(CommandBuffer commandBuffer, ref RenderTargetIdentifier source, RenderTargetIdentifier target)
        {
            // Horizontal pass (kernel direction 1,0)
            commandBuffer.Blit(source, _firstPassRT, _material, 0);
            // Vertical pass (kernel direction 0,1)
            commandBuffer.Blit(_firstPassRT, target, _material, 1);
            // Feed the output back as input for the next iteration
            source = target;
        }

        #endregion

        [System.Serializable]
        public struct GaussianBlurSettings
        {
            public bool useWorldSpaceRadius;
            public float radius;
            public int maxScreenSpaceRadius;
            [Range(0, 1)] public float strength;
            public int iterations;
        }

    }
}