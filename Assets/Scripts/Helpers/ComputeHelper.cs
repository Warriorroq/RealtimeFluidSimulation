using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Linq;

namespace Project.Helpers
{
	public enum DepthMode
	{
		None = 0,
		Depth16 = 16,
		Depth24 = 24
	}
	
	public static class ComputeHelper
	{
#region Constants
		public const FilterMode defaultFilterMode = FilterMode.Bilinear;
		public const GraphicsFormat defaultGraphicsFormat = GraphicsFormat.R32G32B32A32_SFloat;
		private static readonly uint[] ARGS_BUFFER_ARRAY = new uint[5];
#endregion

#region Thread Groups
		public static void Dispatch(ComputeShader cs, Vector3Int numIterations, int kernelIndex = 0)
		{
			Dispatch(cs, numIterations.x, numIterations.y, numIterations.z, kernelIndex);
		}

		/// Convenience method for dispatching a compute shader.
		/// It calculates the number of thread groups based on the number of iterations needed.
		public static void Dispatch(ComputeShader cs, int numIterationsX, int numIterationsY = 1, int numIterationsZ = 1, int kernelIndex = 0)
		{
			Vector3Int sizes = GetThreadGroupSizes(cs, kernelIndex);
			Vector3Int groups = new Vector3Int(
				GetNumGroups(numIterationsX, sizes.x),
				GetNumGroups(numIterationsY, sizes.y),
				GetNumGroups(numIterationsZ, sizes.z));

			cs.Dispatch(kernelIndex, groups.x, groups.y, groups.z);
		}

		public static int CalculateThreadGroupCount1D(ComputeShader cs, int numIterationsX, int kernelIndex = 0)
		{
			Vector3Int sizes = GetThreadGroupSizes(cs, kernelIndex);
			return GetNumGroups(numIterationsX, sizes.x);
		}
#endregion

#region Buffer Helpers
		public static int GetStride<T>()
		{
			return System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
		}

		public static ComputeBuffer CreateAppendBuffer<T>(int size = 1)
		{
			int stride = GetStride<T>();
			ComputeBuffer buffer = new ComputeBuffer(size, stride, ComputeBufferType.Append);
			buffer.SetCounterValue(0);
			return buffer;
		}


		public static void CreateAppendBuffer<T>(ref ComputeBuffer buffer, int count)
		{
			int stride = GetStride<T>();
			bool createNewBuffer = buffer == null || !buffer.IsValid() || buffer.count != count || buffer.stride != stride;
			if (createNewBuffer)
			{
				Release(buffer);
				buffer = new ComputeBuffer(count, stride, ComputeBufferType.Append);
			}

			buffer.SetCounterValue(0);
		}

		public static bool CreateStructuredBuffer<T>(ref ComputeBuffer buffer, int count)
		{
			int stride = GetStride<T>();
			bool createNewBuffer = buffer == null || !buffer.IsValid() || buffer.count != count || buffer.stride != stride;
			if (createNewBuffer)
			{
				Release(buffer);
				buffer = new ComputeBuffer(count, stride);
				return true;
			}

			return false;
		}


		public static ComputeBuffer CreateStructuredBuffer<T>(T[] data)
		{
			var buffer = new ComputeBuffer(data.Length, GetStride<T>());
			buffer.SetData(data);
			return buffer;
		}

		public static ComputeBuffer CreateStructuredBuffer<T>(List<T> data) where T : struct
		{
			var buffer = new ComputeBuffer(data.Count, GetStride<T>());
			buffer.SetData(data);

			return buffer;
		}

		public static void CreateStructuredBuffer<T>(ref ComputeBuffer buffer, List<T> data) where T : struct
		{
			int stride = GetStride<T>();
			bool createNewBuffer = buffer == null || !buffer.IsValid() || buffer.count != data.Count || buffer.stride != stride;
			if (createNewBuffer)
			{
				Release(buffer);
				buffer = new ComputeBuffer(data.Count, stride);
			}

			buffer.SetData(data);
			// Debug.Log(buffer.IsValid());
		}

		public static ComputeBuffer CreateStructuredBuffer<T>(int count)
		{
			return new ComputeBuffer(count, GetStride<T>());
		}

		public static void CreateStructuredBuffer<T>(ref ComputeBuffer buffer, T[] data)
		{
			CreateStructuredBuffer<T>(ref buffer, data.Length);
			buffer.SetData(data);
		}

		public static void SetBuffer(ComputeShader compute, ComputeBuffer buffer, string id, params int[] kernels)
		{
			for (int i = 0; i < kernels.Length; i++)
				compute.SetBuffer(kernels[i], id, buffer);
		}

		public static void SetBuffers(ComputeShader cs, int kernel, Dictionary<ComputeBuffer, string> nameLookup, params ComputeBuffer[] buffers)
		{
			foreach (ComputeBuffer buffer in buffers)
				cs.SetBuffer(kernel, nameLookup[buffer], buffer);
		}

		public static ComputeBuffer CreateAndSetBuffer<T>(T[] data, ComputeShader cs, string nameID, int kernelIndex = 0)
		{
			ComputeBuffer buffer = null;
			CreateAndSetBuffer<T>(ref buffer, data, cs, nameID, kernelIndex);
			return buffer;
		}

		public static void CreateAndSetBuffer<T>(ref ComputeBuffer buffer, T[] data, ComputeShader cs, string nameID, int kernelIndex = 0)
		{
			CreateStructuredBuffer<T>(ref buffer, data.Length);
			buffer.SetData(data);
			cs.SetBuffer(kernelIndex, nameID, buffer);
		}

		public static ComputeBuffer CreateAndSetBuffer<T>(int length, ComputeShader cs, string nameID, int kernelIndex = 0)
		{
			ComputeBuffer buffer = null;
			CreateAndSetBuffer<T>(ref buffer, length, cs, nameID, kernelIndex);
			return buffer;
		}

		public static void CreateAndSetBuffer<T>(ref ComputeBuffer buffer, int length, ComputeShader cs, string nameID, int kernelIndex = 0)
		{
			CreateStructuredBuffer<T>(ref buffer, length);
			cs.SetBuffer(kernelIndex, nameID, buffer);
		}


		/// Releases supplied buffer/s if not null
		public static void Release(params ComputeBuffer[] buffers)
		{
			foreach (var b in buffers)
			{
				b?.Release();
			}
		}

		/// Releases supplied render textures/s if not null
		public static void Release(params RenderTexture[] textures)
		{
			foreach (var t in textures)
			{
				t?.Release();
			}
		}
#endregion

#region RenderTexture helpers
		public static Vector3Int GetThreadGroupSizes(ComputeShader compute, int kernelIndex = 0)
		{
			uint x, y, z;
			compute.GetKernelThreadGroupSizes(kernelIndex, out x, out y, out z);
			return new Vector3Int((int)x, (int)y, (int)z);
		}


		public static RenderTexture CreateRenderTexture(RenderTexture template)
		{
			RenderTexture renderTexture = null;
			CreateRenderTexture(ref renderTexture, template);
			return renderTexture;
		}

		public static RenderTexture CreateRenderTexture(int width, int height, FilterMode filterMode, GraphicsFormat format, string name = "Unnamed", DepthMode depthMode = DepthMode.None, bool useMipMaps = false)
		{
			RenderTexture texture = new RenderTexture(width, height, (int)depthMode);
			texture.graphicsFormat = format;
			texture.enableRandomWrite = true;
			texture.autoGenerateMips = false;
			texture.useMipMap = useMipMaps;
			texture.Create();

			texture.name = name;
			texture.wrapMode = TextureWrapMode.Clamp;
			texture.filterMode = filterMode;
			return texture;
		}

		public static void CreateRenderTexture(ref RenderTexture texture, RenderTexture template)
		{
			if (texture != null)
			{
				texture.Release();
			}

			texture = new RenderTexture(template.descriptor);
			texture.enableRandomWrite = true;
			texture.Create();
		}

		public static void CreateRenderTexture(ref RenderTexture texture, int width, int height)
		{
			CreateRenderTexture(ref texture, width, height, defaultFilterMode, defaultGraphicsFormat);
		}

		public static RenderTexture CreateRenderTexture(int width, int height)
		{
			return CreateRenderTexture(width, height, defaultFilterMode, defaultGraphicsFormat);
		}


		public static bool CreateRenderTexture(ref RenderTexture texture, int width, int height, FilterMode filterMode, GraphicsFormat format, string name = "Unnamed", DepthMode depthMode = DepthMode.None, bool useMipMaps = false)
		{
			if (texture == null || !texture.IsCreated() || texture.width != width || texture.height != height || texture.graphicsFormat != format || texture.depth != (int)depthMode || texture.useMipMap != useMipMaps)
			{
				if (texture != null)
				{
					texture.Release();
				}

				texture = CreateRenderTexture(width, height, filterMode, format, name, depthMode, useMipMaps);
				return true;
			}
			else
			{
				SetRenderTextureProperties(texture, name, filterMode);
			}

			return false;
		}


		public static void CreateRenderTexture3D(ref RenderTexture texture, RenderTexture template)
		{
			CreateRenderTexture(ref texture, template);
		}

		public static void CreateRenderTexture3D(ref RenderTexture texture, int size, GraphicsFormat format, TextureWrapMode wrapMode = TextureWrapMode.Repeat, string name = "Untitled", bool mipmaps = false)
		{
			CreateRenderTexture3D(ref texture, size, size, size, format, wrapMode, name, mipmaps);
		}

		public static void CreateRenderTexture3D(ref RenderTexture texture, int width, int height, int depth, GraphicsFormat format, TextureWrapMode wrapMode = TextureWrapMode.Repeat, string name = "Untitled", bool mipmaps = false)
		{
			if (texture == null || !texture.IsCreated() || texture.width != width || texture.height != height || texture.volumeDepth != depth || texture.graphicsFormat != format)
			{
				//Debug.Log ("Create tex: update noise: " + updateNoise);
				if (texture != null)
				{
					texture.Release();
				}

				const int numBitsInDepthBuffer = 0;
				texture = new RenderTexture(width, height, numBitsInDepthBuffer);
				texture.graphicsFormat = format;
				texture.volumeDepth = depth;
				texture.enableRandomWrite = true;
				texture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
				texture.useMipMap = mipmaps;
				texture.autoGenerateMips = false;
				texture.Create();
			}

			SetRenderTextureProperties(texture, name, FilterMode.Bilinear, wrapMode);
		}
#endregion

#region Argument Buffers
		// Create args buffer for instanced indirect rendering
		public static ComputeBuffer CreateArgsBuffer(Mesh mesh, int numInstances)
		{
			const int stride = sizeof(uint);
			const int numArgs = 5;

			const int subMeshIndex = 0;
			uint[] args = new uint[numArgs];
			args[0] = (uint)mesh.GetIndexCount(subMeshIndex);
			args[1] = (uint)numInstances;
			args[2] = (uint)mesh.GetIndexStart(subMeshIndex);
			args[3] = (uint)mesh.GetBaseVertex(subMeshIndex);
			args[4] = 0; // offset

			ComputeBuffer argsBuffer = new ComputeBuffer(numArgs, stride, ComputeBufferType.IndirectArguments);
			argsBuffer.SetData(args);
			return argsBuffer;
		}

		public static void CreateArgsBuffer(ref ComputeBuffer buffer, uint[] args)
		{
			const int stride = sizeof(uint);
			const int numArgs = 5;
			if (buffer == null || buffer.stride != stride || buffer.count != numArgs || !buffer.IsValid())
			{
				buffer = new ComputeBuffer(numArgs, stride, ComputeBufferType.IndirectArguments);
			}

			buffer.SetData(args);
		}

		private static readonly uint[] SINGLE_INSTANCE_RENDER_ARGS =
		{
			0, // Index count (to be set)
			1, // instance count
			0, // submesh index
			0, // base vertex
			0, // offset
		};

		public static void CreateArgsBuffer(ref ComputeBuffer buffer, ComputeBuffer appendBuffer)
		{
			const int stride = sizeof(uint);
			if (buffer == null || buffer.stride != stride || buffer.count != SINGLE_INSTANCE_RENDER_ARGS.Length || !buffer.IsValid())
			{
				buffer = new ComputeBuffer(SINGLE_INSTANCE_RENDER_ARGS.Length, stride, ComputeBufferType.IndirectArguments);
			}

			buffer.SetData(SINGLE_INSTANCE_RENDER_ARGS);
			ComputeBuffer.CopyCount(appendBuffer, buffer, dstOffsetBytes: 0);
		}
		
		public static void CreateArgsBuffer(ref ComputeBuffer argsBuffer, Mesh mesh, int numInstances)
		{
			const int stride = sizeof(uint);
			const int numArgs = 5;
			const int subMeshIndex = 0;

			bool createNewBuffer = argsBuffer == null || !argsBuffer.IsValid() || argsBuffer.count != ARGS_BUFFER_ARRAY.Length || argsBuffer.stride != stride;
			if (createNewBuffer)
			{
				Release(argsBuffer);
				argsBuffer = new ComputeBuffer(numArgs, stride, ComputeBufferType.IndirectArguments);
			}

			lock (ARGS_BUFFER_ARRAY)
			{
				ARGS_BUFFER_ARRAY[0] = (uint)mesh.GetIndexCount(subMeshIndex);
				ARGS_BUFFER_ARRAY[1] = (uint)numInstances;
				ARGS_BUFFER_ARRAY[2] = (uint)mesh.GetIndexStart(subMeshIndex);
				ARGS_BUFFER_ARRAY[3] = (uint)mesh.GetBaseVertex(subMeshIndex);
				ARGS_BUFFER_ARRAY[4] = 0; // offset
				
				argsBuffer.SetData(ARGS_BUFFER_ARRAY);
			}
		}

		// Create args buffer for instanced indirect rendering (number of instances comes from size of append buffer)
		public static ComputeBuffer CreateArgsBuffer(Mesh mesh, ComputeBuffer appendBuffer) =>
			CopyCountThenReturn(CreateArgsBuffer(mesh, 0), appendBuffer);

		private static ComputeBuffer CopyCountThenReturn(ComputeBuffer destination, ComputeBuffer source)
		{
			ComputeBuffer.CopyCount(source, destination, sizeof(uint));
			return destination;
		}

		// Read number of elements in append buffer
		public static int ReadAppendBufferLength(ComputeBuffer appendBuffer)
		{
			ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
			ComputeBuffer.CopyCount(appendBuffer, countBuffer, 0);

			int[] data = new int[1];
			countBuffer.GetData(data);
			Release(countBuffer);
			return data[0];
		}

		public static void SetTexture(ComputeShader compute, Texture texture, string name, params int[] kernels)
		{
			for (int i = 0; i < kernels.Length; i++)
			{
				compute.SetTexture(kernels[i], name, texture);
			}
		}

		// Set all values from settings object on the shader. Note, variable names must be an exact match in the shader.
		// Settings object can be any class/struct containing vectors/ints/floats/bools
		public static void SetParams(System.Object settings, ComputeShader shader, string variableNamePrefix = "", string variableNameSuffix = "")
		{
			var fields = settings.GetType().GetFields();
			foreach (var field in fields)
			{
				var fieldType = field.FieldType;
				string shaderVariableName = variableNamePrefix + field.Name + variableNameSuffix;

				if (fieldType == typeof(UnityEngine.Vector4) || fieldType == typeof(Vector3) || fieldType == typeof(Vector2))
				{
					shader.SetVector(shaderVariableName, (Vector4)field.GetValue(settings));
				}
				else if (fieldType == typeof(int))
				{
					shader.SetInt(shaderVariableName, (int)field.GetValue(settings));
				}
				else if (fieldType == typeof(float))
				{
					shader.SetFloat(shaderVariableName, (float)field.GetValue(settings));
				}
				else if (fieldType == typeof(bool))
				{
					shader.SetBool(shaderVariableName, (bool)field.GetValue(settings));
				}
				else
				{
					Debug.Log($"Type {fieldType} not implemented");
				}
			}
		}


		public static float[] PackFloats(params float[] values)
		{
			float[] packed = new float[values.Length * 4];
			for (int i = 0; i < values.Length; i++)
			{
				packed[i * 4] = values[i];
			}

			return packed;
		}

		// Load compute shader by name. Prioritises Resources folder, but includes fallbacks for assets located elsewhere.
		public static void LoadComputeShader(ref ComputeShader shader, string name)
		{
			if (shader == null)
			{
				shader = LoadComputeShader(name);
			}
		}
		
		// Load compute shader by name. Prioritises Resources folder, but includes fallbacks for assets located elsewhere.
		public static ComputeShader LoadComputeShader(string name)
		{
			// Remove any extension that might have been supplied (e.g., ".compute")
			string cleanName = System.IO.Path.GetFileNameWithoutExtension(name);

			// 1) Try standard Resources lookup first (original behaviour)
			ComputeShader shader = Resources.Load<ComputeShader>(cleanName);

#if UNITY_EDITOR
			// 2) If not found, attempt to locate the asset anywhere in the project via AssetDatabase (Editor-only)
			if (shader == null)
			{
				string[] guids = UnityEditor.AssetDatabase.FindAssets($"{cleanName} t:ComputeShader");
				if (guids != null && guids.Length > 0)
				{
					string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
					shader        = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
				}
			}
#endif

			// 3) Fallback: search any already-loaded compute shaders in memory (works in builds)
			if (shader == null)
			{
				shader = Resources.FindObjectsOfTypeAll<ComputeShader>().FirstOrDefault(s => s.name == cleanName);
			}

			if (shader == null)
			{
				Debug.LogError($"Compute shader '{cleanName}' could not be found. Ensure it is placed in a Resources folder or included in the build.");
			}

			return shader;
		}

		// Get data (cpu readback) from buffer
		public static T[] ReadbackData<T>(ComputeBuffer buffer)
		{
			T[] data = new T[buffer.count];
			buffer.GetData(data);
			return data;
		}
#endregion

#region Misc
        // Helper method to calculate thread group count (ceil division)
        private static int GetNumGroups(int iterations, int size) => Mathf.CeilToInt(iterations / (float)size);

        // Helper method to configure basic RenderTexture properties
        private static void SetRenderTextureProperties(RenderTexture tex, string label, FilterMode mode, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            tex.name       = label;
            tex.wrapMode   = wrap;
            tex.filterMode = mode;
        }
#endregion
	}
}