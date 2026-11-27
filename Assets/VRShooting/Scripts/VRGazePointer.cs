using System.Collections;
#pragma warning disable 0618
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR;
using DG.Tweening;

public class VRGazePointer : MonoBehaviour
{
    [Header("Laser & Pointer Settings")]
    [SerializeField] float maxLaserDistance = 2500f; // z=800 캔버스까지 충분한 거리
    [SerializeField] float laserWidth = 0.012f;

    Camera cam;
    Transform pointerDot;
    Transform controllerVisual;
    LineRenderer laserLine;
    Button currentButton;
    Vector3 originalButtonScale = Vector3.one;
    bool wasTriggerPressed = false;

    InputDevice rightController;
    InputDevice leftController;
    bool hasRightController = false;
    bool hasLeftController = false;

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
        CreateControllerVisual();
        CreateLaserLine();
        DisableOldCameraPointer();
    }

    void Start()
    {
        DisableOldCameraPointer();
        EnsureCollidersOnButtons();
        ScanControllers();
    }

    void DisableOldCameraPointer()
    {
        // 카메라 자식으로 붙어 시선 한가운데를 가리던 구형 빨간 점(Pointer) 완전 비활성화
        var oldPointer = transform.Find("Pointer");
        if (oldPointer != null && oldPointer.gameObject.activeSelf)
        {
            oldPointer.gameObject.SetActive(false);
        }
    }

    void CreatePointerDot()
    {
        var dotObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dotObj.name = "VR_OculusReticleDot";
        dotObj.transform.localScale = Vector3.one * 0.1f;

        var col = dotObj.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var renderer = dotObj.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.material.color = new Color(0.1f, 0.95f, 1f, 0.95f); // 밝은 오큘러스 시안 블루
        }

        pointerDot = dotObj.transform;
        pointerDot.gameObject.SetActive(false);
    }

    void CreateControllerVisual()
    {
        // 내 손 위치를 눈으로 확인할 수 있는 오큘러스 컨트롤러 핸드 모델
        var handObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        handObj.name = "VR_OculusControllerHand";
        handObj.transform.localScale = new Vector3(0.04f, 0.08f, 0.04f); // 4cm 두께, 16cm 길이의 원통형 컨트롤러 그립
        handObj.transform.localRotation = Quaternion.Euler(90f, 0, 0);

        var col = handObj.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var renderer = handObj.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.material.color = new Color(0.2f, 0.25f, 0.35f, 0.95f); // 메타 퀘스트 다크 그레이 그립
        }

        controllerVisual = handObj.transform;
        controllerVisual.gameObject.SetActive(false);
    }

    void CreateLaserLine()
    {
        laserLine = gameObject.AddComponent<LineRenderer>();
        laserLine.material = new Material(Shader.Find("Sprites/Default"));
        laserLine.startColor = new Color(0.15f, 0.9f, 1f, 0.95f); // 손끝에서 선명한 시안 블루
        laserLine.endColor = new Color(0.15f, 0.9f, 1f, 0.2f);   // 끝부분 부드러운 페이드
        laserLine.startWidth = laserWidth;
        laserLine.endWidth = laserWidth * 0.4f;
        laserLine.positionCount = 2;
        laserLine.useWorldSpace = true;
        laserLine.enabled = true;
    }

    void EnsureCollidersOnButtons()
    {
        // 씬 내 모든 UI 버튼에 3D BoxCollider를 부착하여 레이저 광선이 100% 빗나가지 않도록 보장
        var allButtons = FindObjectsOfType<Button>();
        foreach (var btn in allButtons)
        {
            if (btn == null) continue;
            var box = btn.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = btn.gameObject.AddComponent<BoxCollider>();
            }
            var rect = btn.GetComponent<RectTransform>();
            if (rect != null)
            {
                // 두께 50의 넉넉한 3D 박스로 생성
                box.size = new Vector3(Mathf.Max(20f, rect.rect.width * 1.1f), Mathf.Max(20f, rect.rect.height * 1.1f), 50f);
                box.center = new Vector3(rect.rect.center.x, rect.rect.center.y, 0f);
                box.isTrigger = true;
            }
        }
    }

    void ScanControllers()
    {
        // 1. 오른손 컨트롤러 검색
        var rDevs = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, rDevs);
        if (rDevs.Count == 0) InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.HeldInHand, rDevs);
        if (rDevs.Count == 0) InputDevices.GetDevicesAtXRNode(XRNode.RightHand, rDevs);

        if (rDevs.Count > 0 && rDevs[0].isValid)
        {
            rightController = rDevs[0];
            hasRightController = true;
            Debug.Log($"[VR] Right Controller Connected: {rightController.name}");
        }
        else
        {
            hasRightController = false;
        }

        // 2. 왼손 컨트롤러 검색
        var lDevs = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, lDevs);
        if (lDevs.Count == 0) InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.HeldInHand, lDevs);
        if (lDevs.Count == 0) InputDevices.GetDevicesAtXRNode(XRNode.LeftHand, lDevs);

        if (lDevs.Count > 0 && lDevs[0].isValid)
        {
            leftController = lDevs[0];
            hasLeftController = true;
            Debug.Log($"[VR] Left Controller Connected: {leftController.name}");
        }
        else
        {
            hasLeftController = false;
        }
    }

    void Update()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        DisableOldCameraPointer();

        // 1. 헤드셋 머리 회전 실시간 반영 (월드 캔버스가 고개에 붙지 않고 공간에 고정)
        UpdateHeadPose(out Vector3 headPos, out Quaternion headRot);

        // 2. 컨트롤러 연결 상태 주기적 갱신
        if (!hasRightController && !hasLeftController)
        {
            ScanControllers();
        }

        // 3. 순정 메타 퀘스트 손목 각도 및 손 위치 기반 레이(Ray) 계산
        Ray aimRay = CalculateOculusControllerRay(headPos, out Vector3 handOrigin, out Quaternion handRotation, out bool isControllerActive);

        // 4. 가상 컨트롤러 손 비주얼 갱신
        if (controllerVisual != null)
        {
            controllerVisual.position = handOrigin;
            controllerVisual.rotation = handRotation * Quaternion.Euler(90f, 0, 0);
            controllerVisual.gameObject.SetActive(true);
        }

        // 5. 버튼 레이캐스트 검사 (3D Physics 1순위, RectTransform 기하 2순위)
        Button hitButton = null;
        Vector3 hitPoint = Vector3.zero;
        float hitDistance = float.MaxValue;

        // A. 3D 물리 레이캐스트 (버튼의 BoxCollider 감지)
        RaycastHit[] hits = Physics.RaycastAll(aimRay, maxLaserDistance, ~0, QueryTriggerInteraction.Collide);
        foreach (var h in hits)
        {
            var btn = h.collider.GetComponentInParent<Button>();
            if (btn != null && btn.interactable && btn.gameObject.activeInHierarchy)
            {
                if (h.distance < hitDistance)
                {
                    hitDistance = h.distance;
                    hitButton = btn;
                    hitPoint = h.point;
                }
            }
        }

        // B. 기하 평면 폴백
        if (hitButton == null)
        {
            var allButtons = FindObjectsOfType<Button>();
            foreach (var btn in allButtons)
            {
                if (btn == null || !btn.interactable || !btn.gameObject.activeInHierarchy) continue;

                var rect = btn.GetComponent<RectTransform>();
                if (rect == null) continue;

                Vector3[] corners = new Vector3[4];
                rect.GetWorldCorners(corners);

                Plane plane = new Plane(corners[0], corners[1], corners[2]);
                if (plane.Raycast(aimRay, out float enter) && enter > 0f && enter < hitDistance && enter < maxLaserDistance)
                {
                    Vector3 candidate = aimRay.GetPoint(enter);
                    Vector3 vUp = corners[1] - corners[0];
                    Vector3 vRight = corners[3] - corners[0];
                    Vector3 d = candidate - corners[0];

                    float dotUp = Vector3.Dot(d, vUp);
                    float dotRight = Vector3.Dot(d, vRight);

                    if (dotUp >= 0f && dotUp <= vUp.sqrMagnitude && dotRight >= 0f && dotRight <= vRight.sqrMagnitude)
                    {
                        hitDistance = enter;
                        hitButton = btn;
                        hitPoint = candidate;
                    }
                }
            }
        }

        // 6. 레이저 빔 및 조준점 렌더링
        if (laserLine != null)
        {
            laserLine.enabled = true;
            laserLine.SetPosition(0, handOrigin);

            if (hitButton != null)
            {
                laserLine.SetPosition(1, hitPoint);
                if (pointerDot != null)
                {
                    pointerDot.gameObject.SetActive(true);
                    pointerDot.position = hitPoint;
                    // 거리에 비례하여 버튼 위에서 선명하고 적당한 크기(z=800에서 약 18~20유닛)로 유지
                    float dotScale = Mathf.Clamp(hitDistance * 0.025f, 0.05f, 25f);
                    pointerDot.localScale = Vector3.one * dotScale;
                }
            }
            else
            {
                float beamLength = Mathf.Min(800f, maxLaserDistance);
                laserLine.SetPosition(1, aimRay.GetPoint(beamLength));
                if (pointerDot != null) pointerDot.gameObject.SetActive(false);
            }
        }

        // 7. 버튼 호버 피드백 (조준 시 살짝 확대)
        if (hitButton != currentButton)
        {
            if (currentButton != null)
            {
                currentButton.transform.DOScale(originalButtonScale, 0.15f);
            }

            currentButton = hitButton;

            if (currentButton != null)
            {
                originalButtonScale = currentButton.transform.localScale;
                currentButton.transform.DOScale(originalButtonScale * 1.08f, 0.15f);
            }
        }

        // 8. 오큘러스 순정 트리거 & A/X 버튼 클릭 처리
        bool isTriggerDown = CheckUniversalTriggerDown();

        if (isTriggerDown)
        {
            Button targetToClick = hitButton != null ? hitButton : currentButton;
            if (targetToClick != null)
            {
                ExecuteButtonClick(targetToClick);
            }
        }
    }

    void UpdateHeadPose(out Vector3 headPos, out Quaternion headRot)
    {
        headPos = Vector3.zero;
        headRot = Quaternion.identity;
        bool gotRot = false;

        var headDevs = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, headDevs);
        if (headDevs.Count == 0) InputDevices.GetDevicesAtXRNode(XRNode.CenterEye, headDevs);

        if (headDevs.Count > 0)
        {
            if (headDevs[0].TryGetFeatureValue(CommonUsages.deviceRotation, out headRot))
            {
                gotRot = true;
            }
            headDevs[0].TryGetFeatureValue(CommonUsages.devicePosition, out headPos);
        }

        if (!gotRot)
        {
            headRot = UnityEngine.XR.InputTracking.GetLocalRotation(XRNode.CenterEye);
            headPos = UnityEngine.XR.InputTracking.GetLocalPosition(XRNode.CenterEye);
            if (headRot != Quaternion.identity) gotRot = true;
        }

        if (gotRot)
        {
            cam.transform.localRotation = headRot;
        }
    }

    Ray CalculateOculusControllerRay(Vector3 headPos, out Vector3 handOrigin, out Quaternion handRotation, out bool isControllerActive)
    {
        handOrigin = Vector3.zero;
        handRotation = Quaternion.identity;
        isControllerActive = false;

        // 1. 오른손 컨트롤러 회전/위치 취득
        if (hasRightController && rightController.isValid)
        {
            if (rightController.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rRot))
            {
                handRotation = rRot;
                isControllerActive = true;
            }
            rightController.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 rPos);

            if (isControllerActive)
            {
                if (rPos != Vector3.zero)
                {
                    Vector3 relativeOffset = (headPos != Vector3.zero) ? (rPos - headPos) : rPos;
                    handOrigin = cam.transform.position + relativeOffset;
                }
                else
                {
                    // 3DoF 또는 위치 미전송 시 가슴 앞 35cm, 우측 20cm 자연스러운 손 위치
                    handOrigin = cam.transform.position + cam.transform.right * 0.22f - Vector3.up * 0.25f + cam.transform.forward * 0.35f;
                }
                return new Ray(handOrigin, handRotation * Vector3.forward);
            }
        }

        // 2. 왼손 컨트롤러 취득 (오른손이 없을 때)
        if (hasLeftController && leftController.isValid)
        {
            if (leftController.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion lRot))
            {
                handRotation = lRot;
                isControllerActive = true;
            }
            leftController.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 lPos);

            if (isControllerActive)
            {
                if (lPos != Vector3.zero)
                {
                    Vector3 relativeOffset = (headPos != Vector3.zero) ? (lPos - headPos) : lPos;
                    handOrigin = cam.transform.position + relativeOffset;
                }
                else
                {
                    handOrigin = cam.transform.position - cam.transform.right * 0.22f - Vector3.up * 0.25f + cam.transform.forward * 0.35f;
                }
                return new Ray(handOrigin, handRotation * Vector3.forward);
            }
        }

        // 3. InputTracking 네이티브 폴백 (오큘러스 Link)
        Quaternion trackRot = UnityEngine.XR.InputTracking.GetLocalRotation(XRNode.RightHand);
        Vector3 trackPos = UnityEngine.XR.InputTracking.GetLocalPosition(XRNode.RightHand);
        if (trackRot != Quaternion.identity || trackPos != Vector3.zero)
        {
            handRotation = trackRot;
            isControllerActive = true;
            if (trackPos != Vector3.zero)
            {
                Vector3 relativeOffset = (headPos != Vector3.zero) ? (trackPos - headPos) : trackPos;
                handOrigin = cam.transform.position + relativeOffset;
            }
            else
            {
                handOrigin = cam.transform.position + cam.transform.right * 0.22f - Vector3.up * 0.25f + cam.transform.forward * 0.35f;
            }
            return new Ray(handOrigin, handRotation * Vector3.forward);
        }

        // 4. PC 에디터 마우스 조작 폴백
        if (Input.mousePresent && (Mathf.Abs(Input.GetAxis("Mouse X")) > 0.05f || Mathf.Abs(Input.GetAxis("Mouse Y")) > 0.05f || Input.GetMouseButton(0)))
        {
            handOrigin = cam.transform.position;
            handRotation = cam.transform.rotation;
            return cam.ScreenPointToRay(Input.mousePosition);
        }

        // 5. 컨트롤러 완전 미인식 시 가상 손 위치에서 발사
        handOrigin = cam.transform.position + cam.transform.right * 0.22f - cam.transform.up * 0.22f + cam.transform.forward * 0.35f;
        handRotation = cam.transform.rotation;
        return new Ray(handOrigin, cam.transform.forward);
    }

    void ExecuteButtonClick(Button btn)
    {
        if (btn == null) return;

        Debug.Log($"<color=cyan>[VR Controller] Button Clicked:</color> {btn.name}");

        // 버튼 펀치 애니메이션
        btn.transform.DOPunchScale(Vector3.one * 0.18f, 0.2f, 10, 1f);

        // 1. 버튼 onClick 실행
        btn.onClick.Invoke();

        // 2. EventSystem 포인터 클릭 실행
        var pointerEventData = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(btn.gameObject, pointerEventData, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(btn.gameObject, pointerEventData, ExecuteEvents.submitHandler);
    }

    bool CheckUniversalTriggerDown()
    {
        bool isPressed = false;

        // 1. 모든 연결된 XR 컨트롤러에서 검지 트리거 / A 버튼 / X 버튼 상태 검사
        var allDevices = new List<InputDevice>();
        InputDevices.GetDevices(allDevices);
        foreach (var dev in allDevices)
        {
            // 검지 방아쇠 (Index Trigger)
            if (dev.TryGetFeatureValue(CommonUsages.triggerButton, out bool tb) && tb) isPressed = true;
            if (dev.TryGetFeatureValue(CommonUsages.trigger, out float tVal) && tVal > 0.25f) isPressed = true;

            // Primary 버튼 (A 버튼 - 오른손 / X 버튼 - 왼손)
            if (dev.TryGetFeatureValue(CommonUsages.primaryButton, out bool pb) && pb) isPressed = true;

            // Secondary 버튼 (B 버튼 / Y 버튼)
            if (dev.TryGetFeatureValue(CommonUsages.secondaryButton, out bool sb) && sb) isPressed = true;

            // 그립 버튼
            if (dev.TryGetFeatureValue(CommonUsages.gripButton, out bool gb) && gb) isPressed = true;
            if (dev.TryGetFeatureValue(CommonUsages.grip, out float gVal) && gVal > 0.5f) isPressed = true;

            // 조이스틱 클릭
            if (dev.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool stick) && stick) isPressed = true;
        }

        // 2. 조이스틱 버튼 (Oculus Touch KeyCode: 0~19)
        for (int i = 0; i < 20; i++)
        {
            if (Input.GetKey(KeyCode.JoystickButton0 + i)) isPressed = true;
        }

        // 3. 조이스틱 아날로그 축 (Axis 1~28)
        for (int i = 1; i <= 28; i++)
        {
            try
            {
                if (Mathf.Abs(Input.GetAxisRaw("Axis " + i)) > 0.35f) isPressed = true;
            }
            catch {}
        }

        // 4. PC 마우스 좌클릭 / Fire1 / Space / Enter
        if (Input.GetMouseButton(0) || Input.GetButton("Fire1") || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Return))
        {
            isPressed = true;
        }

        // 이번 프레임에 막 눌린 순간(Down 엣지) 판정
        bool triggerDown = isPressed && !wasTriggerPressed;
        wasTriggerPressed = isPressed;

        return triggerDown;
    }

    void OnDestroy()
    {
        if (pointerDot != null) Destroy(pointerDot.gameObject);
        if (controllerVisual != null) Destroy(controllerVisual.gameObject);
    }
}
