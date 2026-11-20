using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class WeaponHUD : MonoBehaviour
{
    [SerializeField] Text weaponNameText;
    [SerializeField] Text ammoText;

    Tween pulseTween;
    Vector3 initialScale;

    void Awake()
    {
        initialScale = transform.localScale;
    }

    void OnEnable()
    {
        Shooter.OnShoot += TriggerPulse;
    }

    void OnDisable()
    {
        Shooter.OnShoot -= TriggerPulse;
        pulseTween?.Kill();
    }

    void Start()
    {
        if (weaponNameText != null) weaponNameText.text = "AK-47";
        if (ammoText != null) ammoText.text = "RIFLE  |  AMMO : ∞";
    }

    void TriggerPulse()
    {
        // 마우스 클릭이든 퀘스트 컨트롤러 트리거든 발사 시 HUD 펄스 반응
        pulseTween?.Kill(true);
        transform.localScale = initialScale;
        pulseTween = transform.DOPunchScale(new Vector3(0.06f, 0.06f, 0f), 0.1f, 5, 0.5f);
    }
}
