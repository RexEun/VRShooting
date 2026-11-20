using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

[RequireComponent(typeof(RectTransform))]
public class SlideInOut : MonoBehaviour
{
    [SerializeField] float startPosX = 1400.0f;     // 화면 오른쪽 시작 위치
    [SerializeField] float centerPosX = 0.0f;       // 화면 중앙 위치
    [SerializeField] float endPosX = -1400.0f;      // 화면 왼쪽 퇴장 위치
    [SerializeField] float slideInDuration = 0.4f;  // 슬라이드인 소요 시간
    [SerializeField] float stayDuration = 0.2f;     // 중앙 머무는 시간
    [SerializeField] float slideOutDuration = 0.4f; // 슬라이드아웃 소요 시간

    RectTransform rectTransform;
    Sequence sequence;
    float initialY;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        initialY = rectTransform.anchoredPosition.y;
    }

    void OnEnable()
    {
        // 이전 트윈 정리
        sequence?.Kill();

        // 1. 화면 오른쪽 바깥에서 시작하도록 위치 초기화
        rectTransform.anchoredPosition = new Vector2(startPosX, initialY);

        // 2. DOTween 시퀀스 작성
        sequence = DOTween.Sequence();

        // 화면 오른쪽에서 중앙으로 슬라이드인
        sequence.Append(rectTransform.DOAnchorPosX(centerPosX, slideInDuration).SetEase(Ease.OutQuad));

        // 중앙에서 잠시 유지
        if (stayDuration > 0f)
        {
            sequence.AppendInterval(stayDuration);
        }

        // 화면 왼쪽으로 슬라이드아웃
        sequence.Append(rectTransform.DOAnchorPosX(endPosX, slideOutDuration).SetEase(Ease.InQuad));
    }

    void OnDisable()
    {
        sequence?.Kill();
    }
}
