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
    GameObject leftHandObj;
    TrackedPoseDriver rightDriver;
    TrackedPoseDriver leftDriver;

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

        // 1. 카메라에 HMD 헤드 트래킹 드라이버 부착 (공간 고정 보장)
        EnsureHmdDriver();

        // 2. 메타 퀘스트 순정 컨트롤러 하드웨어 오브젝트 생성 및 TrackedPoseDriver 부착
        SetupControllerObjects();

        // 3. 레이저 라인 및 버튼 표면 조준점 생성
        SetupLaserVisuals();

        // 4. 시야 정중앙을 가리는 구형 빨간 점(Pointer) 완전 비활성화
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

    void SetupControllerObjects()
    {
        // 1. 오른손 컨트롤러 생성 및 네이티브 TrackedPoseDriver 부착
        rightHandObj = new GameObject("[VR_Oculus_RightController]");
        rightDriver = rightHandObj.AddComponent<TrackedPoseDriver>();
        rightDriver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRController, TrackedPoseDriver.TrackedPose.RightPose);
        rightDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        rightDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        CreateHandMesh(rightHandObj.transform);

        // 2. 왼손 컨트롤러 생성 및 네이티브 TrackedPoseDriver 부착
        leftHandObj = new GameObject("[VR_Oculus_LeftController]");
        leftDriver = leftHandObj.AddComponent<TrackedPoseDriver>();
        leftDriver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRController, TrackedPoseDriver.TrackedPose.LeftPose);
        leftDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        leftDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        CreateHandMesh(leftHandObj.transform);
    }

    void CreateHandMesh(Transform parent)
    {
        var model = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        model.name = "ControllerGripVisual";
        model.transform.SetParent(parent, false);
        model.transform.localScale = new Vector3(0.04f, 0.08f, 0.04f); // 4cm 굵기, 16cm 길이 그립
        model.transform.localRotation = Quaternion.Euler(90f, 0, 0);

        var col = model.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var ren = model.GetComponent<MeshRenderer>();
        if (ren != null)
        {
            ren.material = new Material(Shader.Find("Sprites/Default"));
            ren.material.color = new Color(0.2f, 0.25f, 0.35f, 0.95f); // 메타 퀘스트 스타일 그레이
        }
    }

    void SetupLaserVisuals()
    {
        // 레이저 라인렌더러
        laserLine = gameObject.AddComponent<LineRenderer>();
        laserLine.material = new Material(Shader.Find("Sprites/Default"));
        laserLine.startColor = new Color(0.15f, 0.9f, 1f, 0.95f); // 손끝에서 선명한 오큘러스 블루
        laserLine.endColor = new Color(0.15f, 0.9f, 1f, 0.25f);
        laserLine.startWidth = laserWidth;
        laserLine.endWidth = laserWidth * 0.4f;
        laserLine.positionCount = 2;
        laserLine.useWorldSpace = true;
        laserLine.enabled = true;

        // 버튼 표면 원형 조준점(Reticle)
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

        // 1. 순정 메타 퀘스트 컨트롤러에서 레이저 시작점 및 조준 방향 취득
        Transform controllerHand = GetActiveController();
        Vector3 rayOrigin = controllerHand.position;
        Vector3 rayDirection = controllerHand.forward;

        // PC 에디터 마우스 조작 폴백 (VR 미착용 시 마우스로 테스트 가능)
        if (Input.mousePresent && (Mathf.Abs(Input.GetAxis("Mouse X")) > 0.05f || Mathf.Abs(Input.GetAxis("Mouse Y")) > 0.05f))
        {
            Ray mouseRay = cam.ScreenPointToRay(Input.mousePosition);
            rayOrigin = mouseRay.origin;
            rayDirection = mouseRay.direction;
        }

        Ray aimRay = new Ray(rayOrigin, rayDirection);

        // 2. 버튼 3D 레이캐스트 충돌 검사
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

        // 3. 레이저 빔 및 조준점 비주얼 업데이트
        laserLine.enabled = true;
        laserLine.SetPosition(0, rayOrigin);

        if (hitButton != null)
        {
            laserLine.SetPosition(1, hitPoint);
            reticleDot.gameObject.SetActive(true);
            reticleDot.position = hitPoint;
            // z=800 거리에서도 선명하게 보이도록 스케일 조정 (약 20유닛)
            float dotScale = Mathf.Clamp(hitDistance * 0.025f, 0.1f, 30f);
            reticleDot.localScale = Vector3.one * dotScale;
        }
        else
        {
            laserLine.SetPosition(1, aimRay.GetPoint(Mathf.Min(800f, maxLaserDistance)));
            reticleDot.gameObject.SetActive(false);
        }

        // 4. 버튼 호버 피드백
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

        // 5. 트리거 / A버튼 클릭 입력 판정
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

    Transform GetActiveController()
    {
        // 1. 오른손 컨트롤러 트래킹 데이터 확인
        if (rightHandObj != null && (rightHandObj.transform.position != Vector3.zero || rightHandObj.transform.rotation != Quaternion.identity))
        {
            return rightHandObj.transform;
        }

        // 2. 왼손 컨트롤러 트래킹 데이터 확인
        if (leftHandObj != null && (leftHandObj.transform.position != Vector3.zero || leftHandObj.transform.rotation != Quaternion.identity))
        {
            return leftHandObj.transform;
        }

        // 3. 컨트롤러가 아직 대기 상태인 경우: 카메라 우측 앞 35cm 자연스러운 손 위치에 배치
        if (rightHandObj != null)
        {
            rightHandObj.transform.position = cam.transform.position + cam.transform.right * 0.22f - Vector3.up * 0.25f + cam.transform.forward * 0.35f;
            return rightHandObj.transform;
        }

        return cam.transform;
    }

    bool CheckTriggerInput()
    {
        bool isPressed = false;

        // 1. 모든 XR 입력 장치 (검지 트리거, Primary A/X 버튼, Grip 버튼)
        var devices = new List<InputDevice>();
        InputDevices.GetDevices(devices);
        foreach (var dev in devices)
        {
            if (dev.TryGetFeatureValue(CommonUsages.triggerButton, out bool tb) && tb) isPressed = true;
            if (dev.TryGetFeatureValue(CommonUsages.trigger, out float tVal) && tVal > 0.15f) isPressed = true;
            if (dev.TryGetFeatureValue(CommonUsages.primaryButton, out bool pb) && pb) isPressed = true;
            if (dev.TryGetFeatureValue(CommonUsages.secondaryButton, out bool sb) && sb) isPressed = true;
            if (dev.TryGetFeatureValue(CommonUsages.gripButton, out bool gb) && gb) isPressed = true;
            if (dev.TryGetFeatureValue(CommonUsages.grip, out float gVal) && gVal > 0.3f) isPressed = true;
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
                if (Mathf.Abs(Input.GetAxisRaw("Axis " + i)) > 0.3f) isPressed = true;
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

        // 버튼 클릭 펀치 피드백
        btn.transform.DOPunchScale(Vector3.one * 0.2f, 0.2f, 10, 1f);

        // 1. Unity UI onClick 이벤트 실행
        btn.onClick.Invoke();

        // 2. EventSystem 포인터 클릭 이벤트 발행
        var pointerEventData = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(btn.gameObject, pointerEventData, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(btn.gameObject, pointerEventData, ExecuteEvents.submitHandler);
    }

    void OnDestroy()
    {
        if (rightHandObj != null) Destroy(rightHandObj);
        if (leftHandObj != null) Destroy(leftHandObj);
        if (reticleDot != null) Destroy(reticleDot.gameObject);
    }
}
