using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Project.Fluid.Simulation
{
    /// <summary>
    /// Runtime diagnostic component for <see cref="Simulation3D"/>. Attach this script to any active
    /// GameObject in your scene (it does not have to be the same object as <see cref="Simulation3D"/>)
    /// and it will periodically check that the compute-kernel mapping used by <see cref="Simulation3D"/>
    /// is still valid and will also sample a small subset of the solver's compute buffers to look for
    /// obvious numerical issues (e.g. NaNs).
    /// <para/>
    /// The intention of this class is to catch integration mistakes early – for example when the
    /// order of kernels inside <c>FluidDynamics.compute</c> has been changed without updating the
    /// indices in <see cref="Simulation3D"/> – as well as surfacing accidental explosions in the
    /// simulation state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Simulation3DMonitor : MonoBehaviour
    {
        private struct MetricsRow
        {
            public float time;
            public float avgDensityError;
            public uint  activeFoam;
            public uint  survivorFoam;
        }

        // The names must exactly match the #pragma kernel declarations inside FluidDynamics.compute
        private static readonly string[] ExpectedKernelNames =
        {
            "ExternalForces",
            "UpdateSpatialHash",
            "Reorder",
            "ReorderCopyBack",
            "CalculateDensities",
            "CalculatePressureForce",
            "CalculateViscosity",
            "UpdatePositions",
            "UpdateDensityTexture",
            "UpdateWhiteParticles",
            "WhiteParticlePrepareNextFrame"
        };
        [Tooltip("How often (in seconds) should the simulation state be validated.")]
        [SerializeField] private float _checkInterval = 5f;

        [Tooltip("Maximum number of elements per buffer to sample when performing value checks.")]
        [SerializeField] private int _sampleCount = 16;

        [Tooltip("If no explicit reference is provided the monitor will try to find one at runtime.")]
        [SerializeField] private Simulation3D _simulation; // Nullable in inspector

        [Tooltip("Absolute or project-relative path for csv log output.")]
        [SerializeField] private string _logFilePath = "metrics_log.csv";

        private float _timer;
        [Tooltip("The maximum allowed deviation from the target density.")]
        [SerializeField]private float _permittedDeviation = 0.05f;

        private void Awake()
        {
            if (_simulation == null)
            {
                _simulation = FindObjectOfType<Simulation3D>();
                if (_simulation == null)
                {
                    Debug.LogError("Simulation3DMonitor: No Simulation3D instance found in scene. Disabling.");
                    enabled = false;
                    return;
                }
            }

            // Ensure metrics file exists.
            EnsureMetricsFile();
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= _checkInterval)
            {
                _timer = 0f;
                PerformChecks();
            }
        }


        private void PerformChecks()
        {
            if (_simulation.compute == null)
            {
                Debug.LogError("Simulation3DMonitor: Simulation compute shader is missing.");
                return;
            }

            CheckKernelMapping();
            CheckSimulationBuffers(out var metrics);
            AppendCsvRow(metrics);
        }

        /// <summary>
        /// Verifies that the compile-time kernel indices defined in <see cref="Simulation3D"/> still
        /// match the index order inside <c>FluidDynamics.compute</c>.
        /// </summary>
        private void CheckKernelMapping()
        {
            var shader = _simulation.compute;
            var sb     = new StringBuilder();
            bool valid = true;

            for (int i = 0; i < ExpectedKernelNames.Length; i++)
            {
                string name        = ExpectedKernelNames[i];
                int    runtimeId   = shader.FindKernel(name);
                if (runtimeId != i)
                {
                    valid = false;
                    sb.AppendLine($"Kernel mismatch → '{name}' is at index {runtimeId}, expected {i}.");
                }
            }

            if (valid)
            {
                Debug.Log("Simulation3DMonitor: Kernel mapping OK.");
            }
            else
            {
                Debug.LogError($"Simulation3DMonitor: Kernel mapping mismatch detected!\n{sb}");
            }
        }

        /// <summary>
        /// Pulls a small sample of values from the key compute buffers and looks for NaNs,
        /// infinities and other obviously invalid data that could indicate that the solver is
        /// diverging. The operation is intentionally kept lightweight so it can be executed in
        /// real-time without noticeable overhead.
        /// </summary>
        private void CheckSimulationBuffers(out MetricsRow metrics)
        {
            metrics = new MetricsRow { time = Time.time };
            void SampleFloat3Buffer(ComputeBuffer buffer, string label, float velocityMagnitudeSoftCap = 250f)
            {
                if (buffer == null || buffer.count == 0) return;

                int sampleCount = Mathf.Min(_sampleCount, buffer.count);
                float3[] data = new float3[sampleCount];
                buffer.GetData(data, 0, 0, sampleCount);

                for (int i = 0; i < sampleCount; i++)
                {
                    float3 v = data[i];
                    if (!math.all(math.isfinite(v)))
                    {
                        Debug.LogWarning($"Simulation3DMonitor: Non-finite value detected in {label} buffer (index {i}). Value = {v}.");
                        return;
                    }

                    if (label == "Velocities")
                    {
                        float mag = math.length(v);
                        if (mag > velocityMagnitudeSoftCap)
                        {
                            Debug.LogWarning($"Simulation3DMonitor: Suspiciously large velocity (|v| = {mag:F1}) detected in index {i}. Potential divergence.");
                            return;
                        }
                    }
                }
            }

            SampleFloat3Buffer(_simulation.positionBuffer, "Positions");
            SampleFloat3Buffer(_simulation.velocityBuffer, "Velocities");
            SampleFloat3Buffer(_simulation.predictedPositionsBuffer, "PredictedPositions");

            if (_simulation.densityBuffer != null && _simulation.densityBuffer.count > 0)
            {
                int sampleCount = Mathf.Min(_sampleCount, _simulation.densityBuffer.count);
                float2[] densities = new float2[sampleCount];
                _simulation.densityBuffer.GetData(densities, 0, 0, sampleCount);

                float target = _simulation.targetDensity;
                float permittedDeviation = target * _permittedDeviation; // 25 % deviation threshold (heuristic)_
                float errorAccum = 0f;
                for (int i = 0; i < sampleCount; i++)
                {
                    float diff = math.abs(densities[i].x - target);
                    errorAccum += diff / math.max(densities[i].x, 0.0001f); // normalize by actual density – avoids div0

                    if (diff > permittedDeviation)
                    {
                        Debug.LogWarning($"Simulation3DMonitor: Density deviation > 25 % of target detected (index {i}). Density = {densities[i].x:F2}, Target = {target:F2}.");
                        // continue checking to accumulate error
                    }
                }
                metrics.avgDensityError = errorAccum / sampleCount;
            }

            if (_simulation.foamCountBuffer != null && _simulation.foamCountBuffer.count >= 2)
            {
                uint[] counters = new uint[2]; // ActiveCount, SurvivorCount
                _simulation.foamCountBuffer.GetData(counters, 0, 0, 2);
                uint activeCount = counters[0];
                uint survivor    = counters[1];

                if (activeCount > _simulation.maxFoamParticleCount)
                {
                    Debug.LogWarning($"Simulation3DMonitor: Active foam-particle count ({activeCount}) exceeds configured maximum ({_simulation.maxFoamParticleCount}).");
                }

                metrics.activeFoam   = activeCount;
                metrics.survivorFoam = survivor;

                Debug.Log($"Simulation3DMonitor: FoamParticles – Active {activeCount}, Surviving {survivor}.");
            }
        }
        private void EnsureMetricsFile()
        {
            string path = ResolvePath(_logFilePath);
            if (!System.IO.File.Exists(path))
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? string.Empty);
                System.IO.File.WriteAllText(path, string.Empty); // no header consistent with existing metrics files
            }
        }

        private void AppendCsvRow(MetricsRow row)
        {
            string path = ResolvePath(_logFilePath);
            string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                        "{0:o},DensityError:{1:F6},ActiveFoam:{2},SurvivorFoam:{3}\n",
                                        System.DateTime.UtcNow,
                                        row.avgDensityError,
                                        row.activeFoam,
                                        row.survivorFoam);
            System.IO.File.AppendAllText(path, line);
        }

        private static string ResolvePath(string path)
        {
            if (System.IO.Path.IsPathRooted(path))
                return path;
            // treat as project-relative
            return System.IO.Path.Combine(Application.dataPath, "..", path);
        }
    }
}