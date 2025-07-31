using Project.Helpers;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Project.Fluid.Simulation;

namespace Project.Fluid.Rendering
{
	public class FluidRender : MonoBehaviour
	{
		[Header("Main Settings")] public bool useFullSizeThicknessTex;
		public Vector3 extinctionCoefficients;
		public float extinctionMultiplier;
		public float depthParticleSize;
		public float thicknessParticleScale;
		public float refractionMultiplier;
		public Vector3 testParams;

		[Header("Smoothing Settings")] public BlurType smoothType;
		public BilateralSmooth2D.BilateralFilterSettings bilateralSettings;
		public GaussBlur.GaussianBlurSettings gaussSmoothSettings;

		[Header("Environment")] public GaussBlur.GaussianBlurSettings shadowSmoothSettings;
		public EnvironmentSettings environmentSettings;

		[Header("Debug Settings")] public DisplayMode displayMode;
		public float depthDisplayScale;
		public float thicknessDisplayScale;

		[Header("References")] public Shader renderA;
		public Shader depthDownsampleCopyShader;
		public Shader depthShader;
		public Shader normalShader;
		public Shader thicknessShader;
		public Shader smoothThickPrepareShader;
		public Simulation3D sim;
		public Camera shadowCam;
		public Light sun;
		public FoamTrial foamTest;

		private DisplayMode _displayModeOld;
		private Mesh _quadMesh;
		private Material _matDepth;
		private Material _matThickness;
		private Material _matNormal;
		private Material _matComposite;
		private Material _smoothPrepareMat;
		private Material _depthDownsampleCopyMat;
		private ComputeBuffer _argsBuffer;

		private RenderTexture _compRt;
		private RenderTexture _depthRt;
		private RenderTexture _normalRt;
		private RenderTexture _shadowRt;
		private RenderTexture _foamRt;
		private RenderTexture _thicknessRt;

		private CommandBuffer _cmd;
		private CommandBuffer _shadowCmd;

		private Bilateral1D _bilateral1D = new();
		private BilateralSmooth2D _bilateral2D = new();
		private GaussBlur _gaussSmooth = new();

		private void Update()
		{
			Init();
			RenderCamSetup();
			ShadowCamSetup();
			BuildCommands();
			UpdateSettings();

			HandleDebugDisplayInput();
		}

		private void BuildCommands()
		{
			BuildShadowCommands();
			BuildRenderCommands();
		}

		private void BuildShadowCommands()
		{
			_shadowCmd.Clear();
			_shadowCmd.SetRenderTarget(_shadowRt);
			_shadowCmd.ClearRenderTarget(true, true, Color.black);
			_shadowCmd.DrawMeshInstancedIndirect(_quadMesh, 0, _matThickness, 0, _argsBuffer);
			_gaussSmooth.Smooth(_shadowCmd, _shadowRt, _shadowRt, _shadowRt.descriptor, shadowSmoothSettings, Vector3.one);
		}

		private void BuildRenderCommands()
		{
			_cmd.Clear();
			RenderFoamAndSpray();
			RenderParticleDepth();
			RenderParticleThickness();
			PackThicknessAndDepth();
			ApplySmoothing();
			ReconstructNormals();
			CompositeToScreen();
		}

		private void RenderFoamAndSpray()
		{
			if (foamTest == null) return;
			_cmd.SetRenderTarget(_foamRt);
			float depthClearVal = SystemInfo.usesReversedZBuffer ? 0 : 1;
			_cmd.ClearRenderTarget(true, true, new Color(0, depthClearVal, 0, 0));
			foamTest.RenderWithCmdBuffer(_cmd);
		}

		private void RenderParticleDepth()
		{
			_cmd.SetRenderTarget(_depthRt);
			_cmd.ClearRenderTarget(true, true, Color.white * 10000000, 1);
			_cmd.DrawMeshInstancedIndirect(_quadMesh, 0, _matDepth, 0, _argsBuffer);
		}

		private void RenderParticleThickness()
		{
			_cmd.SetRenderTarget(_thicknessRt);
			_cmd.Blit(_foamRt, _thicknessRt, _depthDownsampleCopyMat);
			_cmd.DrawMeshInstancedIndirect(_quadMesh, 0, _matThickness, 0, _argsBuffer);
		}

		private void PackThicknessAndDepth()
		{
			_cmd.Blit(null, _compRt, _smoothPrepareMat);
		}

		private void ApplySmoothing()
		{
			ApplyActiveSmoothingType(_cmd, _compRt, _compRt, _compRt.descriptor, new Vector3(1, 1, 0));
		}

		private void ReconstructNormals()
		{
			_cmd.Blit(_compRt, _normalRt, _matNormal);
		}

		private void CompositeToScreen()
		{
			_cmd.Blit(_foamRt, BuiltinRenderTextureType.CameraTarget, _matComposite);
		}

		private void Init()
		{
			if (sim == null)
			{
				sim = FindObjectOfType<Simulation3D>();
				if (sim == null) return; // simulation not ready yet
			}
			if (foamTest == null)
			{
				foamTest = FindObjectOfType<FoamTrial>();
			}
			if (_cmd == null) { _cmd = new CommandBuffer { name = "Fluid Render Commands" }; }
			if (_shadowCmd == null) { _shadowCmd = new CommandBuffer { name = "Fluid Shadow Commands" }; }
			if (!_quadMesh) 
				_quadMesh = MeshBuilder.GenerateQuadMesh();
			
			ComputeHelper.CreateArgsBuffer(ref _argsBuffer, _quadMesh, sim.positionBuffer.count);
			InitTextures();
			InitMaterials();
		}

		private void InitMaterials()
		{
			if (!_depthDownsampleCopyMat) _depthDownsampleCopyMat = new Material(depthDownsampleCopyShader);
			if (!_matDepth) _matDepth = new Material(depthShader);
			if (!_matNormal) _matNormal = new Material(normalShader);
			if (!_matThickness) _matThickness = new Material(thicknessShader);
			if (!_smoothPrepareMat) _smoothPrepareMat = new Material(smoothThickPrepareShader);
			if (!_matComposite) _matComposite = new Material(renderA);
		}

		private void InitTextures()
		{
			int width = Screen.width;
			int height = Screen.height;
			float aspect = height / (float)width;
			int thicknessTexMaxWidth = Mathf.Min(1280, width);
			int thicknessTexMaxHeight = Mathf.Min((int)(1280 * aspect), height);
			int thicknessTexWidth = Mathf.Max(thicknessTexMaxWidth, width / 2);
			int thicknessTexHeight = Mathf.Max(thicknessTexMaxHeight, height / 2);

			if (useFullSizeThicknessTex)
			{
				thicknessTexWidth = width;
				thicknessTexHeight = height;
			}

			// Shadow texture size
			const int shadowTexSizeReduction = 4;
			int shadowTexWidth = width / shadowTexSizeReduction;
			int shadowTexHeight = height / shadowTexSizeReduction;
			GraphicsFormat fmtRGBA = GraphicsFormat.R32G32B32A32_SFloat;
			GraphicsFormat fmtR = GraphicsFormat.R32_SFloat;
			ComputeHelper.CreateRenderTexture(ref _depthRt, width, height, FilterMode.Bilinear, fmtR, depthMode: DepthMode.Depth16);
			ComputeHelper.CreateRenderTexture(ref _thicknessRt, thicknessTexWidth, thicknessTexHeight, FilterMode.Bilinear, fmtR, depthMode: DepthMode.Depth16);
			ComputeHelper.CreateRenderTexture(ref _normalRt, width, height, FilterMode.Bilinear, fmtRGBA, depthMode: DepthMode.None);
			ComputeHelper.CreateRenderTexture(ref _compRt, width, height, FilterMode.Bilinear, fmtRGBA, depthMode: DepthMode.None);
			ComputeHelper.CreateRenderTexture(ref _shadowRt, shadowTexWidth, shadowTexHeight, FilterMode.Bilinear, fmtR, depthMode: DepthMode.None);
			ComputeHelper.CreateRenderTexture(ref _foamRt, width, height, FilterMode.Bilinear, fmtRGBA, depthMode: DepthMode.Depth16);

			}

		private void ApplyActiveSmoothingType(CommandBuffer buffer, RenderTargetIdentifier src, RenderTargetIdentifier target, RenderTextureDescriptor desc, Vector3 smoothMask)
		{
			if (smoothType == BlurType.Bilateral1D)
				_bilateral1D.Smooth(buffer, src, target, desc, bilateralSettings, smoothMask);
			else if (smoothType == BlurType.Bilateral2D)
				_bilateral2D.Smooth(buffer, src, target, desc, bilateralSettings, smoothMask);
			else if (smoothType == BlurType.Gaussian)
				_gaussSmooth.Smooth(buffer, src, target, desc, gaussSmoothSettings, smoothMask);
		}

		private float FrameBoundsOrtho(Vector3 boundsSize, Matrix4x4 worldToView)
		{
			Vector3 halfSize = boundsSize * 0.5f;
			float maxX = 0;
			float maxY = 0;

			for (int i = 0; i < 8; i++)
			{
				Vector3 corner = new Vector3(
					(i & 1) == 0 ? -halfSize.x : halfSize.x,
					(i & 2) == 0 ? -halfSize.y : halfSize.y,
					(i & 4) == 0 ? -halfSize.z : halfSize.z
				);

				Vector3 viewCorner = worldToView.MultiplyPoint(corner);
				maxX = Mathf.Max(maxX, Mathf.Abs(viewCorner.x));
				maxY = Mathf.Max(maxY, Mathf.Abs(viewCorner.y));
			}

			float aspect = Screen.height / (float)Screen.width;
			float targetOrtho = Mathf.Max(maxY, maxX * aspect);
			return targetOrtho;
		}

		private void RenderCamSetup()
		{
			if (_cmd == null)
			{
				_cmd = new();
				_cmd.name = "Fluid Render Commands";
			}

			Camera.main.RemoveAllCommandBuffers();
			Camera.main.AddCommandBuffer(CameraEvent.AfterEverything, _cmd);
			Camera.main.depthTextureMode = DepthTextureMode.Depth;
		}

		private void ShadowCamSetup()
		{
			if (shadowCam == null || sun == null) return;

			if (_shadowCmd == null)
			{
				_shadowCmd = new();
				_shadowCmd.name = "Fluid Shadow Render Commands";
			}

			shadowCam.RemoveAllCommandBuffers();
			shadowCam.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, _shadowCmd);

			Vector3 dirToSun = -sun.transform.forward;
			shadowCam.transform.position = dirToSun * 50;
			shadowCam.transform.rotation = sun.transform.rotation;
			shadowCam.orthographicSize = FrameBoundsOrtho(sim.Scale, shadowCam.worldToCameraMatrix) + 0.5f;
		}


		private void UpdateSettings()
		{
			_smoothPrepareMat.SetTexture("Depth", _depthRt);
			_smoothPrepareMat.SetTexture("Thick", _thicknessRt);
			_matThickness.SetBuffer("positions", sim.positionBuffer);
			_matThickness.SetFloat("scale", thicknessParticleScale);
			_matDepth.SetBuffer("Positions", sim.positionBuffer);
			_matDepth.SetFloat("scale", depthParticleSize);
			_matNormal.SetInt("useSmoothedDepth", Input.GetKey(KeyCode.LeftControl) ? 0 : 1);

			_matComposite.SetInt("debugDisplayMode", (int)displayMode);
			_matComposite.SetTexture("Comp", _compRt);
			_matComposite.SetTexture("Normals", _normalRt);
			_matComposite.SetTexture("ShadowMap", _shadowRt);
			
			_matComposite.SetVector("testParams", testParams);
			_matComposite.SetVector("extinctionCoefficients", extinctionCoefficients * extinctionMultiplier);
			_matComposite.SetVector("boundsSize", sim.Scale);
			_matComposite.SetFloat("refractionMultiplier", refractionMultiplier);

			_matComposite.SetMatrix("shadowVP", GL.GetGPUProjectionMatrix(shadowCam.projectionMatrix, false) * shadowCam.worldToCameraMatrix);
			_matComposite.SetVector("dirToSun", -sun.transform.forward);
			_matComposite.SetFloat("depthDisplayScale", depthDisplayScale);
			_matComposite.SetFloat("thicknessDisplayScale", thicknessDisplayScale);
			_matComposite.SetBuffer("foamCountBuffer", sim.foamCountBuffer);
			_matComposite.SetInt("foamMax", sim.foamBuffer.count);
			
			Vector3 floorSize = new Vector3(30, 0.05f, 30);
			float floorHeight = -sim.Scale.y / 2 + sim.transform.position.y - floorSize.y / 2;
			_matComposite.SetVector("floorPos", new Vector3(0, floorHeight, 0));
			_matComposite.SetVector("floorSize", floorSize);
			EnvironmentSettings env = environmentSettings;
			if (env.noiseScale <= 0) env.noiseScale = 3;
			if (env.secondaryNoiseScale <= 0) env.secondaryNoiseScale = env.noiseScale * 0.5f;
			if (env.secondaryNoiseWeight == 0) env.secondaryNoiseWeight = 0.5f;
			if (env.gradientStrength == 0) env.gradientStrength = 1;
			if (env.colorVariation == Vector3.zero) env.colorVariation = new Vector3(0.2f, 0.2f, 0.2f);
			if (env.baseColor.a == 0) env.baseColor = Color.gray;
			
			_matComposite.SetColor("baseColor", env.baseColor);
			_matComposite.SetVector("colorVariation", env.colorVariation);
			_matComposite.SetFloat("noiseScale", env.noiseScale);
			_matComposite.SetFloat("secondaryNoiseScale", env.secondaryNoiseScale);
			_matComposite.SetFloat("secondaryNoiseWeight", env.secondaryNoiseWeight);
			_matComposite.SetColor("cornerColorBL", env.cornerColorBL);
			_matComposite.SetColor("cornerColorBR", env.cornerColorBR);
			_matComposite.SetColor("cornerColorTL", env.cornerColorTL);
			_matComposite.SetColor("cornerColorTR", env.cornerColorTR);
			_matComposite.SetFloat("gradientStrength", env.gradientStrength);
			_matComposite.SetFloat("sunIntensity", environmentSettings.sunIntensity);
			_matComposite.SetFloat("sunInvSize", environmentSettings.sunInvSize);
		}

		private void HandleDebugDisplayInput()
		{
			for (int i = 0; i <= 9; i++)
			{
				if (Input.GetKeyDown(KeyCode.Alpha0 + i))
				{
					displayMode = (DisplayMode)i;
					Debug.Log("Set display mode: " + displayMode);
				}
			}
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
			public float sunIntensity;
			public float sunInvSize;
		}

		public enum DisplayMode
		{
			Composite,
			Depth,
			SmoothDepth,
			Normal,
			Thickness,
			SmoothThickness
		}

		public enum BlurType
		{
			Gaussian,
			Bilateral2D,
			Bilateral1D
		}


		private void OnDestroy()
		{
			ComputeHelper.Release(_argsBuffer);
			ComputeHelper.Release(_depthRt, _thicknessRt, _normalRt, _compRt, _shadowRt, _foamRt);
		}
	}
}