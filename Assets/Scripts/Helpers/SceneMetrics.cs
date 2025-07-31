using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Profiling;
using UnityEngine.Rendering;
using System.IO;
using Project.Fluid.Simulation;
using Project.Fluid2D.Simulation;

public class SceneMetrics : MonoBehaviour
{
    // Interval in seconds between metric refreshes
    public float metricInterval = 1f;

    // Toggle on-screen overlay
    public bool showOverlay = true;

    // Toggle saving metrics to CSV file in persistent data path
    public bool saveToFile = true;
    public string fileName = "metrics_log.csv";

    private float _timer;
    private int _frameCounter;
    private string _formattedMetrics = string.Empty;
    private GUIStyle _labelStyle;

#if UNITY_2020_2_OR_NEWER
    private ProfilerRecorder _videoMemoryRecorder;
#endif

    private float _cpuFrameMs;
    private float _gpuFrameMs;

    private string _cpuName;
    private string _gpuName;

    private string _filePath;

    private void Awake()
    {
        _labelStyle = new GUIStyle
        {
            normal = { textColor = Color.white },
            fontSize = 14
        };

        _cpuName = SystemInfo.processorType;
        _gpuName = SystemInfo.graphicsDeviceName;
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
#if UNITY_2020_2_OR_NEWER
        _videoMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Video Memory Used");
#endif
        FrameTimingManager.CaptureFrameTimings();
        // Prepare unique file path with timestamp and scene name
        string datedFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{SceneManager.GetActiveScene().name}{Path.GetExtension(fileName)}";
        string exeDir = Path.GetDirectoryName(Application.dataPath); // Folder containing the executable
        _filePath = Path.Combine(exeDir ?? Application.dataPath, datedFileName);
        // Create an empty file if it does not exist (no header row; each line will contain timestamp and metric:value pairs)
        if (saveToFile && !File.Exists(_filePath))
        {
            File.WriteAllText(_filePath, string.Empty);
        }
    }

    private void OnDisable()
    {
        if (!Application.isPlaying) return;
#if UNITY_2020_2_OR_NEWER
        if (_videoMemoryRecorder.Valid) _videoMemoryRecorder.Dispose();
#endif
    }

    private void Update()
    {
        _timer += Time.unscaledDeltaTime;
        _frameCounter++;

        if (_timer >= metricInterval)
        {
            CollectMetrics();
            _timer = 0f;
            _frameCounter = 0;
        }
    }

    private void OnGUI()
    {
        if (!showOverlay) return;
        GUI.Label(new Rect(10, 10, 400, 80), _formattedMetrics, _labelStyle);
    }

    private void CollectMetrics()
    {
        float fps = _frameCounter / _timer;
        int objectCount = CountSceneObjects();
        int particleCount = CountParticles();
        long particleMemMB = CalculateParticleMemoryMB();
        long memoryMB = GC.GetTotalMemory(false) / (1024 * 1024);

        // Frame timings
        FrameTiming[] timings = new FrameTiming[1];
        if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
        {
            _cpuFrameMs = (float)timings[0].cpuFrameTime;
            _gpuFrameMs = (float)timings[0].gpuFrameTime;
        }

        // VRAM usage
        long vramMB = -1;
#if UNITY_2020_2_OR_NEWER
        if (_videoMemoryRecorder.Valid && _videoMemoryRecorder.LastValue > 0)
        {
            vramMB = (long)(_videoMemoryRecorder.LastValue / (1024f * 1024f));
        }
#endif

        // Scene name
        string sceneName = SceneManager.GetActiveScene().name;

        _formattedMetrics = $"Scene: {sceneName}\nCPU: {_cpuName}\nGPU: {_gpuName}";
        long shaderMemMB = vramMB >= 0 ? Math.Max(vramMB - particleMemMB, 0) : -1;

        _formattedMetrics +=
            $"\nFPS: {fps:F1}\nObjects: {objectCount}\nParticles: {particleCount}\nParticle Mem: {particleMemMB} MB";
        if (shaderMemMB >= 0)
            _formattedMetrics += $"\nShader Mem: {shaderMemMB} MB";
        _formattedMetrics += $"\nMemory: {memoryMB} MB";
        _formattedMetrics += $"\nCPU Frame: {_cpuFrameMs:F2} ms\nGPU Frame: {_gpuFrameMs:F2} ms";
        if (vramMB >= 0)
            _formattedMetrics += $"\nVRAM: {vramMB} MB";

        if (saveToFile)
        {
            string timestamp = DateTime.UtcNow.ToString("o");
            // Save metrics in the format: time, metric:value , metric1:value1 , ...
            string line =
                $"{timestamp}, CPU:\"{_cpuName}\", GPU:\"{_gpuName}\", Scene:{sceneName}, FPS:{fps:F2}, Objects:{objectCount}, " +
                $"Particles:{particleCount}, ParticleMemMB:{particleMemMB}, ShaderMemMB:{shaderMemMB}, MemoryMB:{memoryMB}, " +
                $"CPUms:{_cpuFrameMs:F2}, GPUms:{_gpuFrameMs:F2}, VRAMMB:{vramMB}";
            File.AppendAllText(_filePath, line + "\n");
        }

        Debug.Log($"[SceneMetrics] {_formattedMetrics.Replace("\n", ", ")}");
    }

    private int CountSceneObjects()
    {
        int count = 0;
        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            count += root.GetComponentsInChildren<Transform>(true).Length;
        }
        return count;
    }

    private int CountParticles()
    {
        int total = 0;

        // Legacy/Unity particle systems
        foreach (var ps in FindObjectsOfType<ParticleSystem>())
        {
            total += ps.particleCount;
        }

        // 3D fluid simulations
        foreach (var sim in FindObjectsOfType<Simulation3D>())
        {
            if (sim != null && sim.positionBuffer != null)
            {
                total += sim.positionBuffer.count;
            }
        }

        // 2D fluid simulations
        foreach (var sim2D in FindObjectsOfType<Simulation2D>())
        {
            total += sim2D.numParticles;
        }

        return total;
    }

    private long CalculateParticleMemoryMB()
    {
        long totalBytes = 0;

        void AddBufferSize(ComputeBuffer buffer)
        {
            if (buffer != null)
                totalBytes += (long)buffer.count * buffer.stride;
        }

        // 3D simulations
        foreach (var sim in FindObjectsOfType<Simulation3D>())
        {
            AddBufferSize(sim.positionBuffer);
            AddBufferSize(sim.predictedPositionsBuffer);
            AddBufferSize(sim.velocityBuffer);
            AddBufferSize(sim.densityBuffer);
            AddBufferSize(sim.foamBuffer);
        }

        // 2D simulations
        foreach (var sim2D in FindObjectsOfType<Simulation2D>())
        {
            AddBufferSize(sim2D.positionBuffer);
            AddBufferSize(sim2D.velocityBuffer);
            AddBufferSize(sim2D.densityBuffer);
        }

        return totalBytes / (1024 * 1024);
    }
} 