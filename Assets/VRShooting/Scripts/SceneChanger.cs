using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DG.Tweening;

public class SceneChanger : MonoBehaviour
{
    [SerializeField] float fadeDuration = 0.4f;

    static Canvas fadeCanvas;
    static Image fadeImage;
    static bool isTransitioning = false;
    static bool isInitialized = false;

    void Awake()
    {
        EnsureFadeOverlay();
    }

    static void EnsureFadeOverlay()
    {
        if (fadeCanvas != null) return;

        // 동적으로 씬 페이드용 캔버스 생성 (DontDestroyOnLoad로 씬 이동 시 유지)
        var canvasObj = new GameObject("[SceneFadeCanvas]");
        DontDestroyOnLoad(canvasObj);

        fadeCanvas = canvasObj.AddComponent<Canvas>();
        fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        fadeCanvas.sortingOrder = 9999; // 모든 UI 최상단에 렌더링

        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        var imageObj = new GameObject("FadeImage");
        imageObj.transform.SetParent(canvasObj.transform, false);

        fadeImage = imageObj.AddComponent<Image>();
        fadeImage.color = new Color(0f, 0f, 0f, 0f);
        fadeImage.raycastTarget = false;

        var rt = imageObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        if (!isInitialized)
        {
            isInitialized = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        canvasObj.SetActive(false);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 새로운 씬 로드 완료 후 페이드 인 (검은 화면 -> 투명)
        if (fadeImage != null && fadeImage.color.a > 0.01f)
        {
            fadeImage.DOFade(0f, 0.4f)
                     .SetUpdate(true)
                     .OnComplete(() =>
                     {
                         isTransitioning = false;
                         fadeImage.raycastTarget = false;
                         if (fadeCanvas != null) fadeCanvas.gameObject.SetActive(false);
                     });
        }
        else
        {
            isTransitioning = false;
            if (fadeCanvas != null) fadeCanvas.gameObject.SetActive(false);
        }
    }

    public void LoadScene(string sceneName)
    {
        if (isTransitioning) return;
        isTransitioning = true;

        EnsureFadeOverlay();
        fadeCanvas.gameObject.SetActive(true);
        fadeImage.raycastTarget = true; // 전환 도중 추가 클릭 방지

        // 페이드 아웃 (투명 -> 검은 화면)
        fadeImage.DOFade(1f, fadeDuration)
                 .SetUpdate(true)
                 .OnComplete(() =>
                 {
                     SceneManager.LoadScene(sceneName);
                 });
    }

    public void ReloadScene()
    {
        var scene = SceneManager.GetActiveScene();
        LoadScene(scene.name);
    }

    public void QuitGame()
    {
        if (isTransitioning) return;
        isTransitioning = true;

        EnsureFadeOverlay();
        fadeCanvas.gameObject.SetActive(true);
        fadeImage.raycastTarget = true;

        fadeImage.DOFade(1f, fadeDuration)
                 .SetUpdate(true)
                 .OnComplete(() =>
                 {
                     Application.Quit();
                 });
    }
}
