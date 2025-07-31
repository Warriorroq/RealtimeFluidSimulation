using System;
using UnityEngine;
using Project.GPUSorting;
using Unity.Mathematics;
using System.Collections.Generic;
using Project.Helpers;
using static Project.Helpers.ComputeHelper;

namespace Project.Fluid.Simulation
{
	public class Simulation3D : MonoBehaviour
	{
		public event Action<Simulation3D> SimulationInitCompleted;

		[Header("Time Step")] public float normalTimeScale = 1;
		public float slowTimeScale = 0.1f;
		public float maxTimestepFPS = 60;
		public int iterationsPerFrame = 3;

		[Header("Simulation Settings")] public float gravity = -10;
		public float smoothingRadius = 0.2f;
		public float targetDensity = 630;
		public float pressureMultiplier = 288;
		public float nearPressureMultiplier = 2.15f;
		public float viscosityStrength = 0;
		[Range(0, 1)] public float collisionDamping = 0.95f;

		[Header("Foam Settings")] public bool foamActive;
		public int maxFoamParticleCount = 1000;
		public float trappedAirSpawnRate = 70;
		public float spawnRateFadeInTime = 0.5f;
		public float spawnRateFadeStartTime = 0;
		public Vector2 trappedAirVelocityMinMax = new(5, 25);
		public Vector2 foamKineticEnergyMinMax = new(15, 80);
		public float bubbleBuoyancy = 1.5f;
		public int sprayClassifyMaxNeighbours = 5;
		public int bubbleClassifyMinNeighbours = 15;
		public float bubbleScale = 0.5f;
		public float bubbleChangeScaleSpeed = 7;

		[Header("Volumetric Render Settings")] public bool renderToTex3D;
		public int densityTextureRes;

		[Header("References")] public ComputeShader compute;
		public SpawnParticles3D spawner;

		[HideInInspector] public RenderTexture DensityMap;
		public Vector3 Scale => transform.localScale;

		// Buffers
		public ComputeBuffer foamBuffer { get; private set; }
		public ComputeBuffer foamSortTargetBuffer { get; private set; }
		public ComputeBuffer foamCountBuffer { get; private set; }
		public ComputeBuffer positionBuffer { get; private set; }
		public ComputeBuffer velocityBuffer { get; private set; }
		public ComputeBuffer densityBuffer { get; private set; }
		public ComputeBuffer predictedPositionsBuffer;
		public ComputeBuffer debugBuffer { get; private set; }

		private ComputeBuffer sortTarget_positionBuffer;
		private ComputeBuffer sortTarget_velocityBuffer;
		private ComputeBuffer sortTarget_predictedPositionsBuffer;

		// Kernel IDs
		private const int externalForcesKernel = 0;
		private const int spatialHashKernel = 1;
		private const int reorderKernel = 2;
		private const int reorderCopybackKernel = 3;
		private const int densityKernel = 4;
		private const int pressureKernel = 5;
		private const int viscosityKernel = 6;
		private const int updatePositionsKernel = 7;
		private const int renderKernel = 8;
		private const int foamUpdateKernel = 9;
		private const int foamReorderCopyBackKernel = 10;

        SpatialIndex spatialHash;

		// State
		private bool _isPaused;
		private bool _pauseNextFrame;
		private float _smoothRadiusOld;
		private float _simTimer;
		private bool _inSlowMode;
		private SpawnParticles3D.SpawnData _spawnData;
		private Dictionary<ComputeBuffer, string> _bufferNameLookup;

		private void Start()
		{
			Debug.Log("Controls: Space = Play/Pause, Q = SlowMode, R = Reset");
			_isPaused = false;

			if (spawner == null)
			{
				spawner = FindObjectOfType<SpawnParticles3D>();
				if (spawner == null)
				{
					Debug.LogError("SpawnParticles3D component not found. Simulation3D disabled.");
					enabled = false;
					return;
				}
			}

			Initialize();
		}

		private void Initialize()
		{
			if (compute == null)
			{
				compute = Resources.Load<ComputeShader>("FluidDynamics"); // fallback
				if (compute == null)
				{
					Debug.LogError("ComputeShader for Simulation3D not assigned and not found. Disabling.");
					enabled = false;
					return;
				}
			}

			_spawnData = spawner.GetSpawnData();
			int numParticles = _spawnData.points.Length;

			spatialHash = new SpatialIndex(numParticles);
			
			// Create buffers
			positionBuffer = CreateStructuredBuffer<float3>(numParticles);
			predictedPositionsBuffer = CreateStructuredBuffer<float3>(numParticles);
			velocityBuffer = CreateStructuredBuffer<float3>(numParticles);
			densityBuffer = CreateStructuredBuffer<float2>(numParticles);
			foamBuffer = CreateStructuredBuffer<FoamParticle>(maxFoamParticleCount);
			foamSortTargetBuffer = CreateStructuredBuffer<FoamParticle>(maxFoamParticleCount);
			foamCountBuffer = CreateStructuredBuffer<uint>(4096);
			debugBuffer = CreateStructuredBuffer<float3>(numParticles);

			sortTarget_positionBuffer = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_predictedPositionsBuffer = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_velocityBuffer = CreateStructuredBuffer<float3>(numParticles);

			_bufferNameLookup = new Dictionary<ComputeBuffer, string>
			{
				{ positionBuffer, "Positions" },
				{ predictedPositionsBuffer, "PredictedPositions" },
				{ velocityBuffer, "Velocities" },
				{ densityBuffer, "Densities" },
				{ spatialHash.spatialKeys, "SpatialKeys" },
				{ spatialHash.spatialOffsets, "SpatialOffsets" },
				{ spatialHash.spatialIndices, "SortedIndices" },
				{ sortTarget_positionBuffer, "SortTarget_Positions" },
				{ sortTarget_predictedPositionsBuffer, "SortTarget_PredictedPositions" },
				{ sortTarget_velocityBuffer, "SortTarget_Velocities" },
				{ foamCountBuffer, "WhiteParticleCounters" },
				{ foamBuffer, "WhiteParticles" },
				{ foamSortTargetBuffer, "WhiteParticlesCompacted" },
				{ debugBuffer, "Debug" }
			};

			// Set buffer data
			SetInitialBufferData(_spawnData);

			// External forces kernel
			SetBuffers(compute, externalForcesKernel, _bufferNameLookup, new ComputeBuffer[]
			{
				positionBuffer,
				predictedPositionsBuffer,
				velocityBuffer
			});

			// Spatial hash kernel
			SetBuffers(compute, spatialHashKernel, _bufferNameLookup, new ComputeBuffer[]
			{
				spatialHash.spatialKeys,
				spatialHash.spatialOffsets,
				predictedPositionsBuffer,
				spatialHash.spatialIndices
			});

			// Reorder kernel
			SetBuffers(compute, reorderKernel, _bufferNameLookup, new ComputeBuffer[]
			{
				positionBuffer,
				sortTarget_positionBuffer,
				predictedPositionsBuffer,
				sortTarget_predictedPositionsBuffer,
				velocityBuffer,
				sortTarget_velocityBuffer,
				spatialHash.spatialIndices
			});

			// Reorder copyback kernel
			SetBuffers(compute, reorderCopybackKernel, _bufferNameLookup, new ComputeBuffer[]
			{
				positionBuffer,
				sortTarget_positionBuffer,
				predictedPositionsBuffer,
				sortTarget_predictedPositionsBuffer,
				velocityBuffer,
				sortTarget_velocityBuffer,
				spatialHash.spatialIndices
			});

			// Density kernel
			SetBuffers(compute, densityKernel, _bufferNameLookup, new ComputeBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				spatialHash.spatialKeys,
				spatialHash.spatialOffsets
			});

			// Pressure kernel
			SetBuffers(compute, pressureKernel, _bufferNameLookup, new ComputeBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				velocityBuffer,
				spatialHash.spatialKeys,
				spatialHash.spatialOffsets,
				foamBuffer,
				foamCountBuffer,
				debugBuffer
			});

			// Viscosity kernel
			SetBuffers(compute, viscosityKernel, _bufferNameLookup, new ComputeBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				velocityBuffer,
				spatialHash.spatialKeys,
				spatialHash.spatialOffsets
			});

			// Update positions kernel
			SetBuffers(compute, updatePositionsKernel, _bufferNameLookup, new ComputeBuffer[]
			{
				positionBuffer,
				velocityBuffer
			});

			// Render to 3d tex kernel
			SetBuffers(compute, renderKernel, _bufferNameLookup, new ComputeBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				spatialHash.spatialKeys,
				spatialHash.spatialOffsets,
			});

			// Foam update kernel
			SetBuffers(compute, foamUpdateKernel, _bufferNameLookup, new ComputeBuffer[]
			{
				foamBuffer,
				foamCountBuffer,
				predictedPositionsBuffer,
				densityBuffer,
				velocityBuffer,
				spatialHash.spatialKeys,
				spatialHash.spatialOffsets,
				foamSortTargetBuffer,
				//debugBuffer
			});


			// Foam reorder copyback kernel
			SetBuffers(compute, foamReorderCopyBackKernel, _bufferNameLookup, new ComputeBuffer[]
			{
				foamBuffer,
				foamSortTargetBuffer,
				foamCountBuffer,
			});

			compute.SetInt("numParticles", positionBuffer.count);
			compute.SetInt("MaxWhiteParticleCount", maxFoamParticleCount);

			UpdateSmoothingConstants();

			// Run single frame of sim with deltaTime = 0 to initialize density texture
			// (so that display can work even if paused at start)
			if (renderToTex3D)
			{
				RunSimulationFrame(0);
			}

			SimulationInitCompleted?.Invoke(this);
		}

		private void Update()
		{
			// Run simulation
			if (!_isPaused)
			{
				float maxDeltaTime = maxTimestepFPS > 0 ? 1 / maxTimestepFPS : float.PositiveInfinity; // If framerate dips too low, run the simulation slower than real-time
				float dt = Mathf.Min(Time.deltaTime * ActiveTimeScale, maxDeltaTime);
				RunSimulationFrame(dt);
			}

			if (_pauseNextFrame)
			{
				_isPaused = true;
				_pauseNextFrame = false;
			}

			HandleInput();
		}

		private void RunSimulationFrame(float frameDeltaTime)
		{
			float subStepDeltaTime = frameDeltaTime / iterationsPerFrame;
			UpdateSettings(subStepDeltaTime, frameDeltaTime);

			// Simulation sub-steps
			for (int i = 0; i < iterationsPerFrame; i++)
			{
				_simTimer += subStepDeltaTime;
				RunSimulationStep();
			}

			// Foam and spray particles
			if (foamActive)
			{
				Dispatch(compute, maxFoamParticleCount, kernelIndex: foamUpdateKernel);
				Dispatch(compute, maxFoamParticleCount, kernelIndex: foamReorderCopyBackKernel);
			}

			// 3D density map
			if (renderToTex3D)
			{
				UpdateDensityMap();
			}
		}

		private void UpdateDensityMap()
		{
			float maxAxis = Mathf.Max(transform.localScale.x, transform.localScale.y, transform.localScale.z);
			int w = Mathf.RoundToInt(transform.localScale.x / maxAxis * densityTextureRes);
			int h = Mathf.RoundToInt(transform.localScale.y / maxAxis * densityTextureRes);
			int d = Mathf.RoundToInt(transform.localScale.z / maxAxis * densityTextureRes);
			CreateRenderTexture3D(ref DensityMap, w, h, d, UnityEngine.Experimental.Rendering.GraphicsFormat.R16_SFloat, TextureWrapMode.Clamp);
			//Debug.Log(w + " " + h + "  " + d);
			compute.SetTexture(renderKernel, "DensityMap", DensityMap);
			compute.SetInts("densityMapSize", DensityMap.width, DensityMap.height, DensityMap.volumeDepth);
			Dispatch(compute, DensityMap.width, DensityMap.height, DensityMap.volumeDepth, renderKernel);
		}

		private void RunSimulationStep()
		{
			Dispatch(compute, positionBuffer.count, kernelIndex: externalForcesKernel);

			Dispatch(compute, positionBuffer.count, kernelIndex: spatialHashKernel);
			spatialHash.Run();
			
			Dispatch(compute, positionBuffer.count, kernelIndex: reorderKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: reorderCopybackKernel);

			Dispatch(compute, positionBuffer.count, kernelIndex: densityKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: pressureKernel);
			if (viscosityStrength != 0) Dispatch(compute, positionBuffer.count, kernelIndex: viscosityKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: updatePositionsKernel);
		}

		private void UpdateSmoothingConstants()
		{
			float r = smoothingRadius;
			float spikyPow2 = 15 / (2 * Mathf.PI * Mathf.Pow(r, 5));
			float spikyPow3 = 15 / (Mathf.PI * Mathf.Pow(r, 6));
			float spikyPow2Grad = 15 / (Mathf.PI * Mathf.Pow(r, 5));
			float spikyPow3Grad = 45 / (Mathf.PI * Mathf.Pow(r, 6));

			compute.SetFloat("SPIKY_POW2_SCALING_FACTOR", spikyPow2);
			compute.SetFloat("SPIKY_POW3_SCALING_FACTOR", spikyPow3);
			compute.SetFloat("SPIKY_POW2_DERIVATIVE_SCALING_FACTOR", spikyPow2Grad);
			compute.SetFloat("SPIKY_POW3_DERIVATIVE_SCALING_FACTOR", spikyPow3Grad);
		}

		private void UpdateSettings(float stepDeltaTime, float frameDeltaTime)
		{
			if (smoothingRadius != _smoothRadiusOld)
			{
				_smoothRadiusOld = smoothingRadius;
				UpdateSmoothingConstants();
			}

			Vector3 simBoundsSize = transform.localScale;
			Vector3 simBoundsCentre = transform.position;

			compute.SetFloat("deltaTime", stepDeltaTime);
			compute.SetFloat("whiteParticleDeltaTime", frameDeltaTime);
			compute.SetFloat("simTime", _simTimer);
			compute.SetFloat("gravity", gravity);
			compute.SetFloat("collisionDamping", collisionDamping);
			compute.SetFloat("smoothingRadius", smoothingRadius);
			compute.SetFloat("targetDensity", targetDensity);
			compute.SetFloat("pressureMultiplier", pressureMultiplier);
			compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
			compute.SetFloat("viscosityStrength", viscosityStrength);
			compute.SetVector("boundsSize", simBoundsSize);
			compute.SetVector("centre", simBoundsCentre);

			compute.SetMatrix("localToWorld", transform.localToWorldMatrix);
			compute.SetMatrix("worldToLocal", transform.worldToLocalMatrix);

			// Foam settings
			float fadeInT = (spawnRateFadeInTime <= 0) ? 1 : Mathf.Clamp01((_simTimer - spawnRateFadeStartTime) / spawnRateFadeInTime);
			compute.SetVector("trappedAirParams", new Vector3(trappedAirSpawnRate * fadeInT * fadeInT, trappedAirVelocityMinMax.x, trappedAirVelocityMinMax.y));
			compute.SetVector("kineticEnergyParams", foamKineticEnergyMinMax);
			compute.SetFloat("bubbleBuoyancy", bubbleBuoyancy);
			compute.SetInt("sprayClassifyMaxNeighbours", sprayClassifyMaxNeighbours);
			compute.SetInt("bubbleClassifyMinNeighbours", bubbleClassifyMinNeighbours);
			compute.SetFloat("bubbleScaleChangeSpeed", bubbleChangeScaleSpeed);
			compute.SetFloat("bubbleScale", bubbleScale);
		}

		private void SetInitialBufferData(SpawnParticles3D.SpawnData spawnData)
		{
			positionBuffer.SetData(spawnData.points);
			predictedPositionsBuffer.SetData(spawnData.points);
			velocityBuffer.SetData(spawnData.velocities);

			foamBuffer.SetData(new FoamParticle[foamBuffer.count]);

			debugBuffer.SetData(new float3[debugBuffer.count]);
			foamCountBuffer.SetData(new uint[foamCountBuffer.count]);
			_simTimer = 0;
		}

		private void HandleInput()
		{
			if (Input.GetKeyDown(KeyCode.Space))
			{
				_isPaused = !_isPaused;
			}

			if (Input.GetKeyDown(KeyCode.RightArrow))
			{
				_isPaused = false;
				_pauseNextFrame = true;
			}

			if (Input.GetKeyDown(KeyCode.R))
			{
				_pauseNextFrame = true;
				SetInitialBufferData(_spawnData);
				// Run single frame of sim with deltaTime = 0 to initialize density texture
				// (so that display can work even if paused at start)
				if (renderToTex3D)
				{
					RunSimulationFrame(0);
				}
			}

			if (Input.GetKeyDown(KeyCode.Q))
			{
				_inSlowMode = !_inSlowMode;
			}
		}

		private float ActiveTimeScale => _inSlowMode ? slowTimeScale : normalTimeScale;

		private void OnDestroy()
		{
			foreach (var kvp in _bufferNameLookup)
			{
				Release(kvp.Key);
			}

			if (spatialHash != null)
			{
				spatialHash.Release();
			}
		}


		public struct FoamParticle
		{
			public float3 position;
			public float3 velocity;
			public float lifetime;
			public float scale;
		}

		private void OnDrawGizmos()
		{
			// Draw Bounds
			var m = Gizmos.matrix;
			Gizmos.matrix = transform.localToWorldMatrix;
			Gizmos.color = new Color(0, 1, 0, 0.5f);
			Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
			Gizmos.matrix = m;
		}
	}
}