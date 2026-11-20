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
        // 1. PC 마우스 좌클릭 및 기본 Fire1 입력
        if (Input.GetButtonDown("Fire1"))
        {
            return true;
        }

        // 2. 메타 퀘스트 2 컨트롤러 트리거 감지 (오른손 / 왼손)
        bool isTriggerCurrent = false;

        var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (CheckTrigger(rightHand))
        {
            isTriggerCurrent = true;
        }
        else
        {
            var leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (CheckTrigger(leftHand))
            {
                isTriggerCurrent = true;
            }
        }

        // 이번 프레임에 막 당겨진 순간인지 판정 (연타 발사 지원)
        bool triggerDown = isTriggerCurrent && !wasTriggerPressed;
        wasTriggerPressed = isTriggerCurrent;

        return triggerDown;
    }

    bool CheckTrigger(InputDevice device)
    {
        if (!device.isValid) return false;

        // 검지 트리거 버튼(디지털)
        if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool isPressed) && isPressed)
        {
            return true;
        }

        // 검지 트리거 아날로그 입력 (50% 이상 당겼을 때 발사)
        if (device.TryGetFeatureValue(CommonUsages.trigger, out float triggerVal) && triggerVal > 0.5f)
        {
            return true;
        }

        // A / X 버튼(PrimaryButton)으로도 발사 가능하도록 보조
        if (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool primaryBtn) && primaryBtn)
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
