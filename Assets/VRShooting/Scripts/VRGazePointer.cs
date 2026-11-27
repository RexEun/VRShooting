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
    [SerializeField] float maxLaserDistance = 2000f; // z=800 캔버스까지 여유 있게 도달
    [SerializeField] float laserWidth = 0.015f;
    [SerializeField] float gazeAutoClickTime = 0f;    // 0이면 시선 자동클릭 비활성화, 순정 컨트롤러 트리거 클릭 위주

    Camera cam;
    Transform pointerDot;
    Transform controllerVisual;
    LineRenderer laserLine;
    Button currentButton;
    float gazeTimer = 0f;
    Vector3 originalButtonScale = Vector3.one;
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
        CreateControllerVisual();
        CreateLaserLine();
        DisableOldPointer();
    }

    void Start()
    {
        DisableOldPointer();
        EnsureCollidersOnButtons();
    }

    void DisableOldPointer()
    {
        // 카메라 자식으로 붙어있는 구형 정중앙 빨간 점 비활성화
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
        // 손 위치를 눈으로 확인할 수 있는 메타 퀘스트 스타일 손끝 구체
        var handObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        handObj.name = "VR_ControllerHandVisual";
        handObj.transform.localScale = Vector3.one * 0.05f; // 5cm 직경

        var col = handObj.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var renderer = handObj.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.material.color = new Color(0.85f, 0.95f, 1f, 0.9f);
        }

        controllerVisual = handObj.transform;
        controllerVisual.gameObject.SetActive(false);
    }

    void CreateLaserLine()
    {
        laserLine = gameObject.AddComponent<LineRenderer>();
        laserLine.material = new Material(Shader.Find("Sprites/Default"));
        laserLine.startColor = new Color(0.15f, 0.9f, 1f, 0.95f);
        laserLine.endColor = new Color(0.15f, 0.9f, 1f, 0.15f);
        laserLine.startWidth = laserWidth;
        laserLine.endWidth = laserWidth * 0.4f;
        laserLine.positionCount = 2;
        laserLine.useWorldSpace = true;
        laserLine.enabled = true;
    }

    void EnsureCollidersOnButtons()
    {
        // 3D 공간의 Canvas 버튼들에 BoxCollider를 부착하여 정밀한 레이캐스트 보장
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
                box.size = new Vector3(Mathf.Max(20f, rect.rect.width), Mathf.Max(20f, rect.rect.height), 20f);
                box.center = new Vector3(rect.rect.center.x, rect.rect.center.y, 0f);
                box.isTrigger = true;
            }
        }
    }

    void Update()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        DisableOldPointer();

        // 1. 헤드셋 트래킹 동기화 (카메라가 시선 따라 회전, 월드 캔버스는 공간에 단단히 고정)
        UpdateHeadTracking(out Vector3 headLocalPos, out Quaternion headLocalRot);

        // 2. 오큘러스 순정 스타일 컨트롤러 조준 레이(Ray) 계산
        Ray aimRay = GetControllerAimRay(headLocalPos, headLocalRot, out Vector3 beamOrigin);

        // 3. 컨트롤러 손끝 시각화 갱신
        if (controllerVisual != null)
        {
            controllerVisual.position = beamOrigin;
            controllerVisual.gameObject.SetActive(true);
        }

        // 4. 버튼 충돌 검사 (Physics Raycast 1순위, RectTransform 기하 2순위)
        Button hitButton = null;
        Vector3 hitPoint = Vector3.zero;
        float hitDistance = float.MaxValue;

        // A. 3D 물리 레이캐스트
        int mask = ~0;
        RaycastHit[] hits = Physics.RaycastAll(aimRay, maxLaserDistance, mask, QueryTriggerInteraction.Collide);
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

        // 5. 레이저 빔 및 조준점(Reticle) 렌더링
        if (laserLine != null)
        {
            laserLine.enabled = true;
            laserLine.SetPosition(0, beamOrigin);

            if (hitButton != null)
            {
                laserLine.SetPosition(1, hitPoint);
                if (pointerDot != null)
                {
                    pointerDot.gameObject.SetActive(true);
                    pointerDot.position = hitPoint;
                    // 거리에 비례하여 버튼 위에서 선명하게 보이도록 크기 조절 (z=800에서 약 18~20유닛)
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

        // 6. 버튼 호버 피드백
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

        // 7. 순정 메타 퀘스트 컨트롤러 트리거 클릭 입력 처리
        bool isTriggerDown = CheckUniversalTriggerDown();

        if (currentButton != null)
        {
            if (isTriggerDown)
            {
                ExecuteButtonClick(currentButton);
                return;
            }

            // 시선 자동 클릭 옵션 (0보다 클 때만)
            if (gazeAutoClickTime > 0f)
            {
                gazeTimer += Time.deltaTime;
                if (gazeTimer >= gazeAutoClickTime)
                {
                    gazeTimer = 0f;
                    ExecuteButtonClick(currentButton);
                }
            }
        }
        else
        {
            gazeTimer = 0f;
        }
    }

    void UpdateHeadTracking(out Vector3 headPos, out Quaternion headRot)
    {
        headPos = Vector3.zero;
        headRot = Quaternion.identity;
        bool gotRot = false;

        var headDevices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.CenterEye, headDevices);
        if (headDevices.Count == 0)
        {
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, headDevices);
        }

        if (headDevices.Count > 0)
        {
            if (headDevices[0].TryGetFeatureValue(CommonUsages.deviceRotation, out headRot) && headRot != Quaternion.identity)
            {
                gotRot = true;
            }
            headDevices[0].TryGetFeatureValue(CommonUsages.devicePosition, out headPos);
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

    Ray GetControllerAimRay(Vector3 headLocalPos, Quaternion headLocalRot, out Vector3 beamOrigin)
    {
        // 1. 오른손 컨트롤러 우선 트래킹
        if (TryGetHandTransform(XRNode.RightHand, headLocalPos, out Vector3 rPos, out Quaternion rRot))
        {
            beamOrigin = rPos;
            return new Ray(rPos, rRot * Vector3.forward);
        }

        // 2. 왼손 컨트롤러 트래킹
        if (TryGetHandTransform(XRNode.LeftHand, headLocalPos, out Vector3 lPos, out Quaternion lRot))
        {
            beamOrigin = lPos;
            return new Ray(lPos, lRot * Vector3.forward);
        }

        // 3. PC 에디터 마우스 조작 폴백
        if (Input.mousePresent && (Mathf.Abs(Input.GetAxis("Mouse X")) > 0.05f || Mathf.Abs(Input.GetAxis("Mouse Y")) > 0.05f || Input.GetMouseButton(0)))
        {
            beamOrigin = cam.transform.position;
            return cam.ScreenPointToRay(Input.mousePosition);
        }

        // 4. 컨트롤러 미인식 시 가상 오른손 위치에서 카메라 전방으로 발사
        Vector3 virtualHand = cam.transform.position +
                              cam.transform.right * 0.22f -
                              cam.transform.up * 0.22f +
                              cam.transform.forward * 0.35f;
        beamOrigin = virtualHand;
        return new Ray(virtualHand, cam.transform.forward);
    }

    bool TryGetHandTransform(XRNode node, Vector3 headLocalPos, out Vector3 worldPos, out Quaternion worldRot)
    {
        worldPos = Vector3.zero;
        worldRot = Quaternion.identity;

        Vector3 handLocalPos = Vector3.zero;
        Quaternion handLocalRot = Quaternion.identity;
        bool hasRot = false;

        // A. InputDevices 조회
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(node, devices);

        if (devices.Count > 0)
        {
            if (devices[0].TryGetFeatureValue(CommonUsages.deviceRotation, out handLocalRot) && handLocalRot != Quaternion.identity)
            {
                hasRot = true;
            }
            devices[0].TryGetFeatureValue(CommonUsages.devicePosition, out handLocalPos);
        }

        // B. InputTracking 조회
        if (!hasRot)
        {
            handLocalRot = UnityEngine.XR.InputTracking.GetLocalRotation(node);
            handLocalPos = UnityEngine.XR.InputTracking.GetLocalPosition(node);
            if (handLocalRot != Quaternion.identity) hasRot = true;
        }

        if (hasRot)
        {
            // 컨트롤러의 월드 회전은 오큘러스 트래킹 회전값 그 자체입니다 (머리 회전을 이중 곱하지 않음!)
            worldRot = handLocalRot;

            // 컨트롤러의 월드 위치 계산 (헤드셋 위치 기준 상대 오프셋)
            if (handLocalPos != Vector3.zero)
            {
                Vector3 relativeOffset = handLocalPos - headLocalPos;
                worldPos = cam.transform.position + relativeOffset;
            }
            else
            {
                // 3DOF인 경우 몸통 기준 자연스러운 손 위치
                Vector3 naturalOffset = (node == XRNode.RightHand) ? new Vector3(0.22f, -0.3f, 0.4f) : new Vector3(-0.22f, -0.3f, 0.4f);
                worldPos = cam.transform.position + naturalOffset;
            }

            return true;
        }

        return false;
    }

    void ExecuteButtonClick(Button btn)
    {
        if (btn == null) return;

        Debug.Log($"[VRController] Button Clicked: {btn.name}");

        // 버튼 펀치 애니메이션 피드백
        btn.transform.DOPunchScale(Vector3.one * 0.18f, 0.2f, 10, 1f);

        // 1. Button 컴포넌트의 등록된 onClick 이벤트 실행
        btn.onClick.Invoke();

        // 2. Unity EventSystem 포인터 클릭 이벤트 발행
        var pointerEventData = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(btn.gameObject, pointerEventData, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(btn.gameObject, pointerEventData, ExecuteEvents.submitHandler);
    }

    bool CheckUniversalTriggerDown()
    {
        bool isPressed = false;

        // 1. 연결된 모든 XR 입력 장치에서 트리거 및 버튼 상태 탐색
        var allDevices = new List<InputDevice>();
        InputDevices.GetDevices(allDevices);
        foreach (var dev in allDevices)
        {
            // 검지 트리거 (Index Trigger)
            if (dev.TryGetFeatureValue(CommonUsages.triggerButton, out bool tb) && tb) isPressed = true;
            if (dev.TryGetFeatureValue(CommonUsages.trigger, out float tVal) && tVal > 0.25f) isPressed = true;

            // Primary 버튼 (A 버튼 - 오른손 / X 버튼 - 왼손)
            if (dev.TryGetFeatureValue(CommonUsages.primaryButton, out bool pb) && pb) isPressed = true;

            // Secondary 버튼 (B 버튼 / Y 버튼)
            if (dev.TryGetFeatureValue(CommonUsages.secondaryButton, out bool sb) && sb) isPressed = true;

            // 그립 버튼 (Grip Trigger)
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

        // 한 번 눌렀을 때 1회만 클릭 판정 (Down 엣지 디텍션)
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
