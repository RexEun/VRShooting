using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR;
using DG.Tweening;

public class VRGazePointer : MonoBehaviour
{
    [Header("Gaze Settings")]
    [SerializeField] float gazeClickTime = 1.5f;   // 시선으로 바라보고 있을 때 자동 클릭되는 시간 (0이면 비활성화)
    [SerializeField] bool showPointerDot = true;  // 조준점 표시 여부

    Camera cam;
    Transform pointerDot;
    Button currentButton;
    float gazeTimer = 0f;
    Vector3 originalButtonScale = Vector3.one;

    InputDevice rightController;
    InputDevice leftController;
    bool wasTriggerPressed = false;

    // 모든 씬 로드 시 메인 카메라에 자동 부착
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttachToCamera()
    {
        var mainCam = Camera.main;
        if (mainCam != null && mainCam.GetComponent<VRGazePointer>() == null)
        {
            mainCam.gameObject.AddComponent<VRGazePointer>();
        }
    }

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;

        EnsurePointerDot();
    }

    void EnsurePointerDot()
    {
        // 카메라 하위의 기존 Pointer 오브젝트 탐색
        var existingPointer = transform.Find("Pointer");
        if (existingPointer != null)
        {
            pointerDot = existingPointer;
            pointerDot.gameObject.SetActive(showPointerDot);
        }
        else if (showPointerDot)
        {
            // 기존 조준점이 없으면 가벼운 원형 조준점 동적 생성
            var dotObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dotObj.name = "VRPointerDot";
            dotObj.transform.SetParent(transform, false);
            dotObj.transform.localPosition = new Vector3(0, 0, 1.2f);
            dotObj.transform.localScale = Vector3.one * 0.025f;

            var col = dotObj.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var renderer = dotObj.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material = new Material(Shader.Find("Sprites/Default"));
                renderer.material.color = new Color(1f, 0.2f, 0.2f, 0.9f);
            }

            pointerDot = dotObj.transform;
        }
    }

    void Update()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        // 1. 화면 중심(VR 헤드셋 정중앙) 기준 UI Raycast 수행
        Vector2 screenCenter = GetScreenCenter();
        var pointerEventData = new PointerEventData(EventSystem.current)
        {
            position = screenCenter
        };

        var results = new List<RaycastResult>();
        if (EventSystem.current != null)
        {
            EventSystem.current.RaycastAll(pointerEventData, results);
        }

        // EventSystem Raycast로 안 잡힐 경우를 대비해 씬 내 모든 Canvas 직접 Raycast 보완
        if (results.Count == 0)
        {
            var canvases = FindObjectsOfType<Canvas>();
            foreach (var canvas in canvases)
            {
                var raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null)
                {
                    raycaster.Raycast(pointerEventData, results);
                }
            }
        }

        // 레이캐스트가 닿은 오브젝트 중 상위 Button 컴포넌트 탐색
        Button hitButton = null;
        foreach (var r in results)
        {
            var btn = r.gameObject.GetComponentInParent<Button>();
            if (btn != null && btn.interactable && btn.gameObject.activeInHierarchy)
            {
                hitButton = btn;
                break;
            }
        }

        // 2. 호버(Hover) 상태 전환 처리
        if (hitButton != currentButton)
        {
            if (currentButton != null)
            {
                // 이전 버튼 호버 해제
                ExecuteEvents.Execute(currentButton.gameObject, pointerEventData, ExecuteEvents.pointerExitHandler);
                currentButton.transform.DOScale(originalButtonScale, 0.15f);
            }

            currentButton = hitButton;
            gazeTimer = 0f;

            if (currentButton != null)
            {
                // 새 버튼 호버 진입
                originalButtonScale = currentButton.transform.localScale;
                ExecuteEvents.Execute(currentButton.gameObject, pointerEventData, ExecuteEvents.pointerEnterHandler);
                currentButton.transform.DOScale(originalButtonScale * 1.08f, 0.15f);
            }
        }

        // 3. 클릭 입력 감지 (컨트롤러 트리거, A/X 버튼, 마우스 좌클릭, 스페이스바)
        bool isTriggerDown = CheckTriggerDown();

        if (currentButton != null)
        {
            // 입력 즉시 클릭
            if (isTriggerDown)
            {
                ExecuteClick(currentButton, pointerEventData);
                return;
            }

            // 시선 응시 자동 클릭 (gazeClickTime초 동안 바라보면 클릭)
            if (gazeClickTime > 0f)
            {
                gazeTimer += Time.deltaTime;
                if (gazeTimer >= gazeClickTime)
                {
                    gazeTimer = 0f;
                    ExecuteClick(currentButton, pointerEventData);
                }
            }
        }
        else
        {
            gazeTimer = 0f;
        }
    }

    void ExecuteClick(Button btn, PointerEventData eventData)
    {
        if (btn == null) return;

        // 클릭 연출 (펀치 스케일)
        btn.transform.DOPunchScale(Vector3.one * 0.12f, 0.2f, 10, 1f);

        // 버튼 이벤트 실행
        btn.onClick.Invoke();
        ExecuteEvents.Execute(btn.gameObject, eventData, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(btn.gameObject, eventData, ExecuteEvents.submitHandler);
    }

    bool CheckTriggerDown()
    {
        bool isPressed = false;

        // 메타 퀘스트 컨트롤러 트리거 및 버튼 확인
        if (!rightController.isValid)
            rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!leftController.isValid)
            leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        if (rightController.isValid)
        {
            if (rightController.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed) isPressed = true;
            if (rightController.TryGetFeatureValue(CommonUsages.trigger, out float val) && val > 0.4f) isPressed = true;
            if (rightController.TryGetFeatureValue(CommonUsages.primaryButton, out bool aBtn) && aBtn) isPressed = true;
        }

        if (leftController.isValid)
        {
            if (leftController.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed) isPressed = true;
            if (leftController.TryGetFeatureValue(CommonUsages.trigger, out float val) && val > 0.4f) isPressed = true;
            if (leftController.TryGetFeatureValue(CommonUsages.primaryButton, out bool xBtn) && xBtn) isPressed = true;
        }

        // PC 입력 (마우스 좌클릭, 스페이스바, 엔터, Fire1, Submit)
        if (Input.GetMouseButtonDown(0) || Input.GetButtonDown("Fire1") || Input.GetButtonDown("Submit") ||
            Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
        {
            isPressed = true;
        }

        bool triggerDown = isPressed && !wasTriggerPressed;
        wasTriggerPressed = isPressed;

        return triggerDown;
    }

    Vector2 GetScreenCenter()
    {
        float width = Screen.width;
        float height = Screen.height;

        if (XRSettings.enabled && XRSettings.eyeTextureWidth > 0)
        {
            width = XRSettings.eyeTextureWidth;
            height = XRSettings.eyeTextureHeight;
        }

        return new Vector2(width * 0.5f, height * 0.5f);
    }

    void OnDisable()
    {
        if (currentButton != null)
        {
            currentButton.transform.localScale = originalButtonScale;
            currentButton = null;
        }
    }
}
