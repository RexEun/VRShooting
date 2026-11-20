using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using DG.Tweening;

public class Shooter : MonoBehaviour
{
    public static event System.Action OnShoot; // 발사 이벤트 (UI 등 연동)

    [SerializeField] GameObject bulletPrefab; // 총알의 프리팹
    [SerializeField] Transform gunBarrelEnd;  // 총구(총알의 발사 위치)

    [SerializeField] ParticleSystem gunParticle; // 발사 시 연출
    [SerializeField] AudioSource gunAudioSource; // 발사 소리의 음원

    [Header("DOTween Recoil Settings")]
    [SerializeField] float recoilDistance = 0.08f; // 발사 시 뒤로 밀리는 거리
    [SerializeField] float recoilDuration = 0.1f;  // 반동 복귀 시간

    Vector3 initialLocalPosition;
    Tween recoilTween;
    bool wasTriggerPressed; // 이전 프레임 트리거 상태 (GetKeyDown 구현)

    void Awake()
    {
        initialLocalPosition = transform.localPosition;
    }

    // Update is called once per frame
    void Update()
    {
        if (IsFireInput())
        {
            Shoot();
        }
    }

    bool IsFireInput()
    {
        bool isTriggerCurrent = CheckUniversalTrigger();

        // 이번 프레임에 막 당겨진 순간인지 판정 (연타 발사 지원)
        bool triggerDown = isTriggerCurrent && !wasTriggerPressed;
        wasTriggerPressed = isTriggerCurrent;

        return triggerDown;
    }

    bool CheckUniversalTrigger()
    {
        // 1. XR InputDevices (오큘러스 / OpenXR 컨트롤러)
        var controllers = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, controllers);
        foreach (var dev in controllers)
        {
            if (dev.TryGetFeatureValue(CommonUsages.triggerButton, out bool tb) && tb) return true;
            if (dev.TryGetFeatureValue(CommonUsages.trigger, out float tVal) && tVal > 0.35f) return true;
            if (dev.TryGetFeatureValue(CommonUsages.primaryButton, out bool pb) && pb) return true;
        }

        // 2. XRNode 직접 탐색 (RightHand / LeftHand)
        var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (rightHand.isValid)
        {
            if (rightHand.TryGetFeatureValue(CommonUsages.triggerButton, out bool tb) && tb) return true;
            if (rightHand.TryGetFeatureValue(CommonUsages.trigger, out float tVal) && tVal > 0.35f) return true;
            if (rightHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool pb) && pb) return true;
        }
        var leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (leftHand.isValid)
        {
            if (leftHand.TryGetFeatureValue(CommonUsages.triggerButton, out bool tb) && tb) return true;
            if (leftHand.TryGetFeatureValue(CommonUsages.trigger, out float tVal) && tVal > 0.35f) return true;
        }

        // 3. 퀘스트 조이스틱 입력 (오큘러스 Link 네이티브 조이스틱 버튼: 15번 우측트리거, 14번 좌측트리거, 0번 A버튼, 2번 X버튼)
        if (Input.GetKey(KeyCode.JoystickButton15) || Input.GetKey(KeyCode.JoystickButton14) ||
            Input.GetKey(KeyCode.JoystickButton0) || Input.GetKey(KeyCode.JoystickButton2) ||
            Input.GetKey(KeyCode.JoystickButton1) || Input.GetKey(KeyCode.JoystickButton3))
        {
            return true;
        }

        // 4. 모든 조이스틱 버튼 포괄 검사 (0 ~ 19)
        for (int i = 0; i < 20; i++)
        {
            if (Input.GetKey(KeyCode.JoystickButton0 + i)) return true;
        }

        // 5. 오큘러스 트리거 아날로그 축 (Axis 9, Axis 10)
        try
        {
            if (Mathf.Abs(Input.GetAxisRaw("Axis 9")) > 0.35f || Mathf.Abs(Input.GetAxisRaw("Axis 10")) > 0.35f)
            {
                return true;
            }
        }
        catch {}

        // 6. PC 마우스 좌클릭 / Fire1 / 스페이스바
        if (Input.GetButton("Fire1") || Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space))
        {
            return true;
        }

        return false;
    }

    void Shoot()
    {
        // 프리팹을 바탕으로 씬상에 총알을 생성
        Instantiate(bulletPrefab, gunBarrelEnd.position, gunBarrelEnd.rotation);

        // 발사 시 연출을 재생
        gunParticle.Play();

        // 발사 시의 소리를 재생
        gunAudioSource.Play();

        // DOTween 총기 반동 연출 (뒤로 펀치 후 원위치 복귀)
        recoilTween?.Kill(true);
        transform.localPosition = initialLocalPosition;
        recoilTween = transform.DOPunchPosition(new Vector3(0, 0, -recoilDistance), recoilDuration, 10, 0.5f);

        // 발사 이벤트 알림
        OnShoot?.Invoke();
    }

    void OnDestroy()
    {
        recoilTween?.Kill();
    }
}
