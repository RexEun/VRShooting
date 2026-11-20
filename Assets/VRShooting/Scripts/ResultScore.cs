using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

[RequireComponent(typeof(Text))]
public class ResultScore : MonoBehaviour
{
    [SerializeField] float countDuration = 1.2f; // 점수 카운트업 소요 시간
    Tween scoreTween;

    void Start()
    {
        // 게임 오브젝트를 검색
        var gameObj = GameObject.FindWithTag("Score");
        if (gameObj == null) return;

        // gameObj에 포함되는 Score 컴포넌트를 취득
        var score = gameObj.GetComponent<Score>();
        if (score == null) return;

        // Text 컴포넌트의 취득
        var uiText = GetComponent<Text>();
        int targetScore = score.Points;

        int currentScore = 0;
        uiText.text = "점수 : 000점";

        // 0부터 최종 점수까지 카운트업 연출
        scoreTween = DOTween.To(() => currentScore, x =>
        {
            currentScore = x;
            uiText.text = string.Format("점수 : {0:D3}점", currentScore);
        }, targetScore, countDuration)
        .SetEase(Ease.OutQuad)
        .OnComplete(() =>
        {
            // 목표 점수 도달 시 통통 튀는 바운스 효과
            transform.DOPunchScale(Vector3.one * 0.15f, 0.25f, 5, 0.5f);
        });
    }

    void OnDestroy()
    {
        scoreTween?.Kill();
    }
}
