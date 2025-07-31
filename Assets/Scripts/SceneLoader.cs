using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    private static SceneLoader INSTANCE;

    private void Awake()
    {
        if (INSTANCE != null && INSTANCE != this)
        {
            Destroy(gameObject);
            return;
        }

        INSTANCE = this;
        DontDestroyOnLoad(gameObject);
    }

    public static SceneLoader Instance
    {
        get
        {
            if (INSTANCE == null)
            {
                var loaderObject = new GameObject("SceneLoader");
                INSTANCE = loaderObject.AddComponent<SceneLoader>();
            }
            return INSTANCE;
        }
    }

    public void LoadSceneA(string sceneName)
    {
        LoadScene(sceneName);
    }

    public void LoadScene(string sceneName, LoadSceneMode mode = LoadSceneMode.Single, Action<float> onProgress = null)
    {
        StartCoroutine(LoadSceneRoutine(sceneName, mode, onProgress));
    }

    private IEnumerator LoadSceneRoutine(string sceneName, LoadSceneMode mode, Action<float> onProgress)
    {
        AsyncOperation asyncOp = SceneManager.LoadSceneAsync(sceneName, mode);
        asyncOp.allowSceneActivation = true;

        while (!asyncOp.isDone)
        {
            onProgress?.Invoke(asyncOp.progress);
            yield return null;
        }
        onProgress?.Invoke(1f);
    }

    public void UnloadScene(string sceneName)
    {
        StartCoroutine(UnloadSceneRoutine(sceneName));
    }

    private IEnumerator UnloadSceneRoutine(string sceneName)
    {
        if (!SceneManager.GetSceneByName(sceneName).isLoaded)
        {
            yield break;
        }

        AsyncOperation asyncOp = SceneManager.UnloadSceneAsync(sceneName);
        while (!asyncOp.isDone)
        {
            yield return null;
        }
    }
} 