using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Project.Fluid.Simulation
{

	public class SpawnParticles3D : MonoBehaviour
	{
		public int particleSpawnDensity = 600;
		public float3 initialVel;
		public float jitterStrength;
		public bool showSpawnBounds;
		public SpawnRegion[] spawnRegions;

		[Header("Randomization Settings")]
		public bool randomizeRegions;
		public Vector3 boundsMin;
		public Vector3 boundsMax;
		public Vector2 sizeMinMax;

		[Header("Debug Info")] public int debug_num_particles;
		public float debug_spawn_volume;


		public SpawnData GetSpawnData()
		{
			if (randomizeRegions)
			{
				RandomizeRegions();
			}
			List<float3> allPoints = new();
			List<float3> allVelocities = new();

			foreach (SpawnRegion region in spawnRegions)
			{
				int particlesPerAxis = region.CalculateParticleCountPerAxis(particleSpawnDensity);
				(float3[] points, float3[] velocities) = SpawnCube(particlesPerAxis, region.centre, Vector3.one * region.size);
				allPoints.AddRange(points);
				allVelocities.AddRange(velocities);
			}

			return new SpawnData() { points = allPoints.ToArray(), velocities = allVelocities.ToArray() };
		}

		void RandomizeRegions()
		{
			if (spawnRegions == null || spawnRegions.Length == 0) return;

			for (int i = 0; i < spawnRegions.Length; i++)
			{
				SpawnRegion region = spawnRegions[i];
				region.centre = new Vector3(
					UnityEngine.Random.Range(boundsMin.x, boundsMax.x),
					UnityEngine.Random.Range(boundsMin.y, boundsMax.y),
					UnityEngine.Random.Range(boundsMin.z, boundsMax.z));

				region.size = UnityEngine.Random.Range(sizeMinMax.x, sizeMinMax.y);
				spawnRegions[i] = region;
			}
		}

		private (float3[] p, float3[] v) SpawnCube(int numPerAxis, Vector3 centre, Vector3 size)
		{
			int totalPoints = numPerAxis * numPerAxis * numPerAxis;
			float3[] points = new float3[totalPoints];
			float3[] velocities = new float3[totalPoints];

			int index = 0;

			for (int x = 0; x < numPerAxis; x++)
			{
				for (int y = 0; y < numPerAxis; y++)
				{
					for (int z = 0; z < numPerAxis; z++)
					{
						float3 jitter = (float3)(UnityEngine.Random.insideUnitSphere * jitterStrength);
						points[index] = CalculatePointPosition(x, y, z, numPerAxis, centre, size) + jitter;
						velocities[index] = initialVel;
						index++;
					}
				}
			}

			return (points, velocities);
		}

		private float3 CalculatePointPosition(int x, int y, int z, int numPerAxis, Vector3 centre, Vector3 size)
		{
			float tX = x / (numPerAxis - 1f);
			float tY = y / (numPerAxis - 1f);
			float tZ = z / (numPerAxis - 1f);

			float pX = (tX - 0.5f) * size.x + centre.x;
			float pY = (tY - 0.5f) * size.y + centre.y;
			float pZ = (tZ - 0.5f) * size.z + centre.z;

			return new float3(pX, pY, pZ);
		}


		private void OnValidate()
		{
			debug_spawn_volume = 0;
			debug_num_particles = 0;

			if (spawnRegions != null)
			{
				foreach (SpawnRegion region in spawnRegions)
				{
					debug_spawn_volume += region.Volume;
					int numPerAxis = region.CalculateParticleCountPerAxis(particleSpawnDensity);
					debug_num_particles += numPerAxis * numPerAxis * numPerAxis;
				}
			}
		}

		private void OnDrawGizmos()
		{
			if (showSpawnBounds && !Application.isPlaying)
			{
				foreach (SpawnRegion region in spawnRegions)
				{
					Gizmos.color = region.debugDisplayCol;
					Gizmos.DrawWireCube(region.centre, Vector3.one * region.size);
				}
			}
		}

		[System.Serializable]
		public struct SpawnRegion
		{
			public Vector3 centre;
			public float size;
			public Color debugDisplayCol;

			public float Volume => size * size * size;

			public int CalculateParticleCountPerAxis(int particleDensity)
			{
				int targetParticleCount = (int)(Volume * particleDensity);
				int particlesPerAxis = (int)Math.Cbrt(targetParticleCount);
				return particlesPerAxis;
			}
		}

		public struct SpawnData
		{
			public float3[] points;
			public float3[] velocities;
		}
	}
}