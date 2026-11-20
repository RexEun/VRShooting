using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR;
using DG.Tweening;

public class VRGazePointer : MonoBehaviour
{
    [Header("Pointer Settings")]
    [SerializeField] float gazeClickTime = 1.2f;    // 시선 응시 자동 클릭 시간 (0이면 비활성화)
    [SerializeField] float laserWidth = 0.008f;    // 컨트롤러 레이저 굵기

    Camera cam;
    Transform pointerDot;
    LineRenderer laserLine;
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

        CreatePointerDot();
        CreateLaserLine();

        // 카메라 하위의 구형 Pointer(화면에 붙어있던 빨간 점)는 비활성화하여 시야 방해 제거
        var oldPointer = transform.Find("Pointer");
        if (oldPointer != null)
        {
            oldPointer.gameObject.SetActive(false);
        }
    }

    void CreatePointerDot()
    {
        // 3D 월드 공간의 버튼 표면에 붙는 동적 조준점 생성
        var dotObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dotObj.name = "VR_WorldPointerDot";
        dotObj.transform.localScale = Vector3.one * 0.1f;

        var col = dotObj.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var renderer = dotObj.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.material.color = new Color(0.2f, 0.85f, 1f, 0.95f); // 산뜻한 시안 블루
        }

        pointerDot = dotObj.transform;
        pointerDot.gameObject.SetActive(false);
    }

    void CreateLaserLine()
    {
        // 컨트롤러에서 뻗어나가는 레이저 빔
        laserLine = gameObject.AddComponent<LineRenderer>();
        laserLine.material = new Material(Shader.Find("Sprites/Default"));
        laserLine.startColor = new Color(0.2f, 0.8f, 1f, 0.7f);
        laserLine.endColor = new Color(0.2f, 0.8f, 1f, 0.1f);
        laserLine.startWidth = laserWidth;
        laserLine.endWidth = laserWidth * 0.3f;
        laserLine.positionCount = 2;
        laserLine.enabled = false;
    }

    void Update()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        // 1. 조준 레이(Ray) 결정: 컨트롤러 손 위치 우선 ➔ 컨트롤러 미사용 시 시선(카메라)
        Ray ray = GetAimRay(out bool isControllerActive, out Vector3 origin);

        // 2. 씬 내의 모든 활성화된 버튼과 3D 기하 평면 교차 검사
        Button hitButton = null;
        Vector3 hitPoint = Vector3.zero;
        float minDistance = float.MaxValue;

        var allButtons = FindObjectsOfType<Button>();
        foreach (var btn in allButtons)
        {
            if (btn == null || !btn.interactable || !btn.gameObject.activeInHierarchy) continue;

            var rect = btn.GetComponent<RectTransform>();
            if (rect == null) continue;

            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            // 버튼의 3D 평면
            Plane plane = new Plane(corners[0], corners[1], corners[2]);
            if (plane.Raycast(ray, out float enter) && enter > 0f && enter < minDistance)
            {
                Vector3 candidateHit = ray.GetPoint(enter);

                // 사각형 내부 포함 여부 판정
                Vector3 vUp = corners[1] - corners[0];
                Vector3 vRight = corners[3] - corners[0];
                Vector3 d = candidateHit - corners[0];

                float dotUp = Vector3.Dot(d, vUp);
                float dotRight = Vector3.Dot(d, vRight);

                if (dotUp >= 0f && dotUp <= vUp.sqrMagnitude && dotRight >= 0f && dotRight <= vRight.sqrMagnitude)
                {
                    minDistance = enter;
                    hitButton = btn;
                    hitPoint = candidateHit;
                }
            }
        }

        // 3. 레이저 및 조준점 시각화 업데이트
        if (hitButton != null)
        {
            // 버튼 표면에 조준점을 딱 붙여서 표시 (입체감 일치)
            pointerDot.gameObject.SetActive(true);
            pointerDot.position = hitPoint;
            pointerDot.localScale = Vector3.one * (minDistance * 0.035f); // 거리에 비례하여 크기 유지

            if (isControllerActive && laserLine != null)
            {
                laserLine.enabled = true;
                laserLine.SetPosition(0, origin);
                laserLine.SetPosition(1, hitPoint);
            }
        }
        else
        {
            if (isControllerActive && laserLine != null)
            {
                laserLine.enabled = true;
                laserLine.SetPosition(0, origin);
                laserLine.SetPosition(1, ray.GetPoint(15f));
                pointerDot.gameObject.SetActive(false);
            }
            else
            {
                if (laserLine != null) laserLine.enabled = false;
                pointerDot.gameObject.SetActive(false);
            }
        }

        // 4. 호버(Hover) 상태 전환 및 스케일 애니메이션
        if (hitButton != currentButton)
        {
            if (currentButton != null)
            {
                currentButton.transform.DOScale(originalButtonScale, 0.15f);
            }

            currentButton = hitButton;
            gazeTimer = 0f;

            if (currentButton != null)
            {
                originalButtonScale = currentButton.transform.localScale;
                currentButton.transform.DOScale(originalButtonScale * 1.08f, 0.15f);
            }
        }

        // 5. 클릭 입력 처리 (컨트롤러 트리거, A/X 버튼, 마우스 좌클릭, 스페이스바)
        bool isTriggerDown = CheckTriggerDown();

        if (currentButton != null)
        {
            // 컨트롤러 트리거 또는 마우스 클릭 시 즉각 실행
            if (isTriggerDown)
            {
                TriggerClick(currentButton);
                return;
            }

            // 시선 응시 자동 클릭 (gazeClickTime초 동안 바라보면 클릭)
            if (gazeClickTime > 0f)
            {
                gazeTimer += Time.deltaTime;
                if (gazeTimer >= gazeClickTime)
                {
                    gazeTimer = 0f;
                    TriggerClick(currentButton);
                }
            }
        }
        else
        {
            gazeTimer = 0f;
        }
    }

    Ray GetAimRay(out bool isControllerActive, out Vector3 origin)
    {
        if (!rightController.isValid)
            rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!leftController.isValid)
            leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        // 1. 오른손 컨트롤러 우선 확인
        if (rightController.isValid &&
            rightController.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 rPos) &&
            rightController.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rRot) &&
            rPos != Vector3.zero)
        {
            isControllerActive = true;
            origin = rPos;
            return new Ray(rPos, rRot * Vector3.forward);
        }

        // 2. 왼손 컨트롤러 확인
        if (leftController.isValid &&
            leftController.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 lPos) &&
            leftController.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion lRot) &&
            lPos != Vector3.zero)
        {
            isControllerActive = true;
            origin = lPos;
            return new Ray(lPos, lRot * Vector3.forward);
        }

        // 3. 컨트롤러가 없으면 카메라(시선)에서 발사
        isControllerActive = false;
        origin = cam.transform.position;
        return new Ray(cam.transform.position, cam.transform.forward);
    }

    void TriggerClick(Button btn)
    {
        if (btn == null) return;

        // 클릭 연출 (펀치 스케일)
        btn.transform.DOPunchScale(Vector3.one * 0.12f, 0.2f, 10, 1f);

        // Button의 onClick 이벤트 실행
        btn.onClick.Invoke();

        // EventSystem 이벤트 발송
        var pointerEventData = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(btn.gameObject, pointerEventData, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(btn.gameObject, pointerEventData, ExecuteEvents.submitHandler);
    }

    bool CheckTriggerDown()
    {
        bool isPressed = false;

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

    void OnDestroy()
    {
        if (pointerDot != null)
        {
            Destroy(pointerDot.gameObject);
        }
    }
}