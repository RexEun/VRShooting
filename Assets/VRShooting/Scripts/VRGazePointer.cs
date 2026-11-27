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
    [Header("Pointer Settings")]
    [SerializeField] float maxLaserDistance = 1500f; // 레이저 빔 최대 거리 (z=800 캔버스까지 도달)
    [SerializeField] float laserWidth = 0.015f;     // 레이저 빔 굵기
    [SerializeField] float gazeClickTime = 1.2f;    // 시선 응시 자동 클릭 (0이면 비활성화)

    Camera cam;
    Transform pointerDot;
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
        CreateLaserLine();
        DisableOldPointer();
    }

    void Start()
    {
        DisableOldPointer();
    }

    void DisableOldPointer()
    {
        // 카메라 하위의 구형 정중앙 빨간 점(Pointer)은 비활성화하여 시야 방해 제거
        var oldPointer = transform.Find("Pointer");
        if (oldPointer != null && oldPointer.gameObject.activeSelf)
        {
            oldPointer.gameObject.SetActive(false);
        }
    }

    void CreatePointerDot()
    {
        var dotObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dotObj.name = "VR_WorldPointerDot";
        dotObj.transform.localScale = Vector3.one * 0.1f;

        var col = dotObj.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var renderer = dotObj.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.material.color = new Color(0.1f, 0.9f, 1f, 0.95f); // 선명한 오큘러스 시안 블루
        }

        pointerDot = dotObj.transform;
        pointerDot.gameObject.SetActive(false);
    }

    void CreateLaserLine()
    {
        laserLine = gameObject.AddComponent<LineRenderer>();
        laserLine.material = new Material(Shader.Find("Sprites/Default"));
        laserLine.startColor = new Color(0.2f, 0.85f, 1f, 0.85f);
        laserLine.endColor = new Color(0.2f, 0.85f, 1f, 0.15f);
        laserLine.startWidth = laserWidth;
        laserLine.endWidth = laserWidth * 0.5f;
        laserLine.positionCount = 2;
        laserLine.enabled = true;
    }

    void Update()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        DisableOldPointer();

        // 1. 유니티 6 VR 헤드셋 트래킹 동기화 (카메라 회전 갱신 -> 월드 캔버스가 시선에 달라붙지 않고 공간에 고정됨)
        UpdateHeadTracking();

        // 2. 오큘러스 순정 스타일 조준 레이(Ray) 계산 (컨트롤러 손 위치 우선)
        Ray ray = GetOculusAimRay(out Vector3 beamOrigin);

        // 3. 씬 내 버튼들과 3D 기하 교차 검사
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

            // 버튼의 3D 평면과의 교차 판정
            Plane plane = new Plane(corners[0], corners[1], corners[2]);
            if (plane.Raycast(ray, out float enter) && enter > 0f && enter < minDistance && enter < maxLaserDistance)
            {
                Vector3 candidateHit = ray.GetPoint(enter);

                // 버튼 사각형 내부인지 정확히 판정
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

        // 4. 레이저 빔 및 조준점 시각화
        if (laserLine != null)
        {
            laserLine.enabled = true;
            laserLine.SetPosition(0, beamOrigin);

            if (hitButton != null)
            {
                // 버튼에 레이저 빔이 도달했을 때
                laserLine.SetPosition(1, hitPoint);
                pointerDot.gameObject.SetActive(true);
                pointerDot.position = hitPoint;
                pointerDot.localScale = Vector3.one * Mathf.Max(0.1f, minDistance * 0.04f); // 원근감에 맞춘 조준점 크기
            }
            else
            {
                // 허공을 가리킬 때도 시원하게 뻗어나가는 빔 연출
                float beamLength = Mathf.Min(800f, maxLaserDistance);
                laserLine.SetPosition(1, ray.GetPoint(beamLength));
                pointerDot.gameObject.SetActive(false);
            }
        }

        // 5. 호버(Hover) 상태 전환 및 확대 피드백
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
                currentButton.transform.DOScale(originalButtonScale * 1.1f, 0.15f);
            }
        }

        // 6. 트리거 입력 처리 (모든 컨트롤러 트리거 & 버튼 & 마우스 지원)
        bool isTriggerDown = CheckUniversalTriggerDown();

        if (currentButton != null)
        {
            if (isTriggerDown)
            {
                ExecuteButtonClick(currentButton);
                return;
            }

            if (gazeClickTime > 0f)
            {
                gazeTimer += Time.deltaTime;
                if (gazeTimer >= gazeClickTime)
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

    void UpdateHeadTracking()
    {
        // VR 헤드셋 트래킹 정보를 읽어 카메라 회전/위치를 직접 갱신 (TrackedPoseDriver 없이도 VR 회전 완벽 보장)
        bool gotRot = false;

        var headDevices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.CenterEye, headDevices);
        if (headDevices.Count == 0)
        {
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, headDevices);
        }

        if (headDevices.Count > 0)
        {
            if (headDevices[0].TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion hRot) && hRot != Quaternion.identity)
            {
                cam.transform.localRotation = hRot;
                gotRot = true;
            }
        }

        if (!gotRot)
        {
            Quaternion hRot = UnityEngine.XR.InputTracking.GetLocalRotation(XRNode.CenterEye);
            if (hRot != Quaternion.identity)
            {
                cam.transform.localRotation = hRot;
            }
        }
    }

    Ray GetOculusAimRay(out Vector3 beamOrigin)
    {
        // 1. 오른손 컨트롤러 트래킹 시도
        if (TryGetControllerTransform(XRNode.RightHand, out Vector3 rPos, out Quaternion rRot))
        {
            beamOrigin = rPos;
            return new Ray(rPos, rRot * Vector3.forward);
        }

        // 2. 왼손 컨트롤러 트래킹 시도
        if (TryGetControllerTransform(XRNode.LeftHand, out Vector3 lPos, out Quaternion lRot))
        {
            beamOrigin = lPos;
            return new Ray(lPos, lRot * Vector3.forward);
        }

        // 3. PC 에디터 마우스 조작 폴백 (VR 기기 미착용 시 마우스로 바로 클릭 가능)
        if (Input.mousePresent && (Mathf.Abs(Input.GetAxis("Mouse X")) > 0.05f || Mathf.Abs(Input.GetAxis("Mouse Y")) > 0.05f || Input.GetMouseButton(0)))
        {
            beamOrigin = cam.transform.position;
            return cam.ScreenPointToRay(Input.mousePosition);
        }

        // 4. 컨트롤러 트래킹이 안 될 경우: 카메라 기준 가상 오른손 위치(가슴 앞 35cm, 우측 22cm)에서 전방으로 빔 발사
        Vector3 virtualHandPos = cam.transform.position +
                                cam.transform.right * 0.22f -
                                cam.transform.up * 0.2f +
                                cam.transform.forward * 0.35f;

        beamOrigin = virtualHandPos;
        return new Ray(virtualHandPos, cam.transform.forward);
    }

    bool TryGetControllerTransform(XRNode node, out Vector3 worldPos, out Quaternion worldRot)
    {
        worldPos = Vector3.zero;
        worldRot = Quaternion.identity;

        Vector3 localPos = Vector3.zero;
        Quaternion localRot = Quaternion.identity;
        bool hasRot = false;

        // A. InputDevices에서 XRNode 기반 검색
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(node, devices);

        if (devices.Count == 0)
        {
            var charMask = (node == XRNode.RightHand) ? InputDeviceCharacteristics.Right : InputDeviceCharacteristics.Left;
            charMask |= InputDeviceCharacteristics.Controller;
            InputDevices.GetDevicesWithCharacteristics(charMask, devices);
        }

        if (devices.Count > 0)
        {
            if (devices[0].TryGetFeatureValue(CommonUsages.deviceRotation, out localRot) && localRot != Quaternion.identity)
            {
                hasRot = true;
            }
            devices[0].TryGetFeatureValue(CommonUsages.devicePosition, out localPos);
        }

        // B. InputTracking 폴백 (Oculus Link)
        if (!hasRot)
        {
            localRot = UnityEngine.XR.InputTracking.GetLocalRotation(node);
            localPos = UnityEngine.XR.InputTracking.GetLocalPosition(node);
            if (localRot != Quaternion.identity) hasRot = true;
        }

        if (hasRot)
        {
            // 컨트롤러 위치가 0이면 카메라 기준 자연스러운 손 위치로 오프셋 부여
            if (localPos == Vector3.zero)
            {
                localPos = (node == XRNode.RightHand) ? new Vector3(0.2f, -0.2f, 0.35f) : new Vector3(-0.2f, -0.2f, 0.35f);
            }

            worldPos = cam.transform.position + cam.transform.rotation * localPos;
            worldRot = cam.transform.rotation * localRot;
            return true;
        }

        return false;
    }

    void ExecuteButtonClick(Button btn)
    {
        if (btn == null) return;

        btn.transform.DOPunchScale(Vector3.one * 0.15f, 0.2f, 10, 1f);
        btn.onClick.Invoke();

        var pointerEventData = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(btn.gameObject, pointerEventData, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(btn.gameObject, pointerEventData, ExecuteEvents.submitHandler);
    }

    bool CheckUniversalTriggerDown()
    {
        bool isPressed = false;

        // 1. XR 컨트롤러 트리거/A/X 버튼
        var controllers = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, controllers);
        foreach (var dev in controllers)
        {
            if (dev.TryGetFeatureValue(CommonUsages.triggerButton, out bool tb) && tb) isPressed = true;
            if (dev.TryGetFeatureValue(CommonUsages.trigger, out float tVal) && tVal > 0.35f) isPressed = true;
            if (dev.TryGetFeatureValue(CommonUsages.primaryButton, out bool pb) && pb) isPressed = true;
        }

        // 2. 조이스틱 버튼 (Oculus Touch 버튼: 15번 우측트리거, 14번 좌측트리거, 0번 A, 2번 X)
        if (Input.GetKey(KeyCode.JoystickButton15) || Input.GetKey(KeyCode.JoystickButton14) ||
            Input.GetKey(KeyCode.JoystickButton0) || Input.GetKey(KeyCode.JoystickButton2))
        {
            isPressed = true;
        }

        // 3. 모든 조이스틱 버튼 0~19
        for (int i = 0; i < 20; i++)
        {
            if (Input.GetKey(KeyCode.JoystickButton0 + i)) isPressed = true;
        }

        // 4. 트리거 아날로그 축
        try
        {
            if (Mathf.Abs(Input.GetAxisRaw("Axis 9")) > 0.35f || Mathf.Abs(Input.GetAxisRaw("Axis 10")) > 0.35f)
            {
                isPressed = true;
            }
        }
        catch {}

        // 5. PC 입력 (마우스 좌클릭, 스페이스바, 엔터, Fire1)
        if (Input.GetButton("Fire1") || Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Return))
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
