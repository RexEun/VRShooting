using System.Collections;
#pragma warning disable 0618
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR;
using UnityEngine.SpatialTracking;
using DG.Tweening;

public class VRGazePointer : MonoBehaviour
{
    [Header("Laser Settings")]
    [SerializeField] float maxLaserDistance = 3000f;
    [SerializeField] float laserWidth = 0.012f;

    Camera cam;
    GameObject rightHandObj;
    TrackedPoseDriver rightDriver;

    LineRenderer laserLine;
    Transform reticleDot;
    Button currentTargetButton;

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

        // 1. 카메라에 HMD 헤드 트래킹 드라이버 부착 (공간 고정)
        EnsureHmdDriver();

        // 2. 오른손 컨트롤러 오브젝트 생성
        SetupControllerObject();

        // 3. 레이저 라인 및 조준점 생성
        SetupLaserVisuals();

        // 4. 시야 정중앙을 가리던 옛날 구형 빨간 점 비활성화
        DisableLegacyPointer();
    }

    void Start()
    {
        DisableLegacyPointer();
        EnsureCollidersOnButtons();
    }

    void DisableLegacyPointer()
    {
        var oldPointer = transform.Find("Pointer");
        if (oldPointer != null && oldPointer.gameObject.activeSelf)
        {
            oldPointer.gameObject.SetActive(false);
        }
    }

    void EnsureHmdDriver()
    {
        var driver = cam.GetComponent<TrackedPoseDriver>();
        if (driver == null)
        {
            driver = cam.gameObject.AddComponent<TrackedPoseDriver>();
        }
        driver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
    }

    void SetupControllerObject()
    {
        rightHandObj = new GameObject("[VR_Oculus_RightController]");
        rightDriver = rightHandObj.AddComponent<TrackedPoseDriver>();
        rightDriver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRController, TrackedPoseDriver.TrackedPose.RightPose);
        rightDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        rightDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        // 손 위치 확인용 3D 모델
        var model = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        model.name = "ControllerGripVisual";
        model.transform.SetParent(rightHandObj.transform, false);
        model.transform.localScale = new Vector3(0.04f, 0.08f, 0.04f);
        model.transform.localRotation = Quaternion.Euler(90f, 0, 0);

        var col = model.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var ren = model.GetComponent<MeshRenderer>();
        if (ren != null)
        {
            ren.material = new Material(Shader.Find("Sprites/Default"));
            ren.material.color = new Color(0.2f, 0.25f, 0.35f, 0.95f);
        }
    }

    void SetupLaserVisuals()
    {
        laserLine = gameObject.AddComponent<LineRenderer>();
        laserLine.material = new Material(Shader.Find("Sprites/Default"));
        laserLine.startColor = new Color(0.15f, 0.9f, 1f, 0.95f);
        laserLine.endColor = new Color(0.15f, 0.9f, 1f, 0.25f);
        laserLine.startWidth = laserWidth;
        laserLine.endWidth = laserWidth * 0.4f;
        laserLine.positionCount = 2;
        laserLine.useWorldSpace = true;
        laserLine.enabled = true;

        var dotObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dotObj.name = "VR_OculusReticleDot";
        dotObj.transform.localScale = Vector3.one * 0.1f;

        var dotCol = dotObj.GetComponent<Collider>();
        if (dotCol != null) Destroy(dotCol);

        var dotRen = dotObj.GetComponent<MeshRenderer>();
        if (dotRen != null)
        {
            dotRen.material = new Material(Shader.Find("Sprites/Default"));
            dotRen.material.color = new Color(0.1f, 0.95f, 1f, 0.95f);
        }

        reticleDot = dotObj.transform;
        reticleDot.gameObject.SetActive(false);
    }

    void EnsureCollidersOnButtons()
    {
#pragma warning disable 0618
        var allButtons = FindObjectsOfType<Button>();
#pragma warning restore 0618
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
                box.size = new Vector3(Mathf.Max(30f, rect.rect.width * 1.3f), Mathf.Max(30f, rect.rect.height * 1.3f), 100f);
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

        DisableLegacyPointer();
        EnsureCollidersOnButtons();

        // 1. 컨트롤러 트래킹 데이터 갱신 (TrackedPoseDriver + InputDevices + InputTracking 통합)
        UpdateControllerPose();

        // 2. 조준 레이 계산 (손끝에서 손목 각도대로 발사)
        Vector3 rayOrigin = rightHandObj.transform.position;
        Vector3 rayDirection = rightHandObj.transform.forward;

        // PC 에디터 마우스 조작 폴백
        if (Input.mousePresent && (Mathf.Abs(Input.GetAxis("Mouse X")) > 0.05f || Mathf.Abs(Input.GetAxis("Mouse Y")) > 0.05f))
        {
            Ray mouseRay = cam.ScreenPointToRay(Input.mousePosition);
            rayOrigin = mouseRay.origin;
            rayDirection = mouseRay.direction;
        }

        Ray aimRay = new Ray(rayOrigin, rayDirection);

        // 3. 버튼 3D 레이캐스트 검사
        Button hitButton = null;
        Vector3 hitPoint = Vector3.zero;
        float hitDistance = float.MaxValue;

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

        // 4. 레이저 빔 및 조준점 렌더링
        laserLine.enabled = true;
        laserLine.SetPosition(0, rayOrigin);

        if (hitButton != null)
        {
            laserLine.SetPosition(1, hitPoint);
            reticleDot.gameObject.SetActive(true);
            reticleDot.position = hitPoint;
            float dotScale = Mathf.Clamp(hitDistance * 0.025f, 0.1f, 30f);
            reticleDot.localScale = Vector3.one * dotScale;
        }
        else
        {
            laserLine.SetPosition(1, aimRay.GetPoint(Mathf.Min(800f, maxLaserDistance)));
            reticleDot.gameObject.SetActive(false);
        }

        // 5. 버튼 호버 피드백
        if (hitButton != currentTargetButton)
        {
            if (currentTargetButton != null)
            {
                currentTargetButton.transform.DOScale(Vector3.one, 0.15f);
            }
            currentTargetButton = hitButton;
            if (currentTargetButton != null)
            {
                currentTargetButton.transform.DOScale(Vector3.one * 1.08f, 0.15f);
            }
        }

        // 6. 트리거 클릭 입력 처리
        bool isTriggerDown = CheckTriggerInput();

        if (isTriggerDown)
        {
            Button btnToClick = hitButton != null ? hitButton : currentTargetButton;
            if (btnToClick != null)
            {
                ClickButton(btnToClick);
            }
        }
    }

    void UpdateControllerPose()
    {
        bool hasRotation = false;
        bool hasPosition = false;
        Vector3 pos = Vector3.zero;
        Quaternion rot = Quaternion.identity;

        // 1. InputDevices API (Unity XR 최신 표준)
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, devices);
        if (devices.Count == 0) InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right, devices);
        if (devices.Count == 0) InputDevices.GetDevicesAtXRNode(XRNode.RightHand, devices);
        if (devices.Count == 0) InputDevices.GetDevices(devices);

        foreach (var dev in devices)
        {
            if (!dev.isValid) continue;

            if (dev.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion r) ||
                dev.TryGetFeatureValue(CommonUsages.pointerRotation, out r))
            {
                rot = r;
                hasRotation = true;
            }

            if (dev.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 p) ||
                dev.TryGetFeatureValue(CommonUsages.pointerPosition, out p))
            {
                pos = p;
                hasPosition = true;
            }

            if (hasRotation) break;
        }

        // 2. InputTracking API 폴백
#pragma warning disable 0618
        if (!hasRotation)
        {
            Quaternion r = UnityEngine.XR.InputTracking.GetLocalRotation(XRNode.RightHand);
            if (r != Quaternion.identity)
            {
                rot = r;
                hasRotation = true;
            }
        }
        if (!hasPosition)
        {
            Vector3 p = UnityEngine.XR.InputTracking.GetLocalPosition(XRNode.RightHand);
            if (p != Vector3.zero)
            {
                pos = p;
                hasPosition = true;
            }
        }
#pragma warning restore 0618

        // 3. TrackedPoseDriver 자체 트래킹 확인
        if (!hasRotation && rightHandObj != null)
        {
            if (rightHandObj.transform.localRotation != Quaternion.identity)
            {
                rot = rightHandObj.transform.rotation;
                hasRotation = true;
            }
            if (rightHandObj.transform.localPosition != Vector3.zero)
            {
                pos = rightHandObj.transform.position;
                hasPosition = true;
            }
        }

        // 4. 컨트롤러 오브젝트 위치 및 회전 반영
        if (rightHandObj != null)
        {
            if (hasRotation)
            {
                rightHandObj.transform.rotation = rot;
            }
            else
            {
                rightHandObj.transform.rotation = cam.transform.rotation;
            }

            if (hasPosition && pos != Vector3.zero)
            {
                // 트래킹 좌표계와 카메라 좌표계 동기화
                if (cam.transform.parent != null)
                {
                    rightHandObj.transform.parent = cam.transform.parent;
                    rightHandObj.transform.localPosition = pos;
                }
                else
                {
                    // 카메라 기준 월드 좌표 반영
                    rightHandObj.transform.position = pos;
                }
            }
            else
            {
                // 위치 데이터가 아직 없는 경우 카메라 우측 하단 35cm 자연스러운 손 위치
                rightHandObj.transform.position = cam.transform.position + cam.transform.right * 0.22f - cam.transform.up * 0.22f + cam.transform.forward * 0.35f;
            }
        }
    }

    bool CheckTriggerInput()
    {
        bool isPressed = false;

        // 1. 모든 XR 입력 장치 (검지 트리거, Primary A/X 버튼, Grip)
        var devices = new List<InputDevice>();
        InputDevices.GetDevices(devices);
        foreach (var dev in devices)
        {
            if (dev.TryGetFeatureValue(CommonUsages.triggerButton, out bool tb) && tb) isPressed = true;
            if (dev.TryGetFeatureValue(CommonUsages.trigger, out float tVal) && tVal > 0.1f) isPressed = true;
            if (dev.TryGetFeatureValue(CommonUsages.primaryButton, out bool pb) && pb) isPressed = true;
            if (dev.TryGetFeatureValue(CommonUsages.secondaryButton, out bool sb) && sb) isPressed = true;
            if (dev.TryGetFeatureValue(CommonUsages.gripButton, out bool gb) && gb) isPressed = true;
            if (dev.TryGetFeatureValue(CommonUsages.grip, out float gVal) && gVal > 0.2f) isPressed = true;
        }

        // 2. 오큘러스 Touch 조이스틱 버튼 (0~19)
        for (int i = 0; i < 20; i++)
        {
            if (Input.GetKey(KeyCode.JoystickButton0 + i)) isPressed = true;
        }

        // 3. 오큘러스 아날로그 트리거 축 (1~28)
        for (int i = 1; i <= 28; i++)
        {
            try
            {
                if (Mathf.Abs(Input.GetAxisRaw("Axis " + i)) > 0.2f) isPressed = true;
            }
            catch {}
        }

        // 4. PC 마우스 좌클릭 / Fire1 / Space / Enter
        if (Input.GetMouseButton(0) || Input.GetButton("Fire1") || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Return))
        {
            isPressed = true;
        }

        bool triggerDown = isPressed && !wasTriggerPressed;
        wasTriggerPressed = isPressed;

        return triggerDown;
    }

    void ClickButton(Button btn)
    {
        if (btn == null) return;

        Debug.Log($"<color=green>[VR Controller] Button Clicked:</color> {btn.name}");

        btn.transform.DOPunchScale(Vector3.one * 0.2f, 0.2f, 10, 1f);
        btn.onClick.Invoke();

        var pointerEventData = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(btn.gameObject, pointerEventData, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(btn.gameObject, pointerEventData, ExecuteEvents.submitHandler);
    }

    void OnDestroy()
    {
        if (rightHandObj != null) Destroy(rightHandObj);
        if (reticleDot != null) Destroy(reticleDot.gameObject);
    }
}
