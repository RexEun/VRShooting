using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraRotator : MonoBehaviour
{
    [SerializeField] float angularVelocity = 30f; // 회전 속도의 설정

    float horizontalAngle = 0f; // 수평 방향의 회전량을 보존
    float verticalAngle = 0f;   // 수직 방향의 회전량을 보존

#if UNITY_EDITOR
    void Update()
    {
        // VR 헤드셋이 연결되어 활성화된 상태라면 VR 시선 트래킹에 맡기고 키보드 회전은 비활성화
        if (UnityEngine.XR.XRSettings.isDeviceActive || UnityEngine.XR.XRSettings.enabled)
        {
            return;
        }

        // 입력에 따라 회전량을 취득
        var horizontalRotation = Input.GetAxis("Horizontal") * angularVelocity * Time.deltaTime;
        var verticalRotation = -Input.GetAxis("Vertical") * angularVelocity * Time.deltaTime;

        // 회전량을 갱신
        horizontalAngle += horizontalRotation;
        verticalAngle += verticalRotation;

        // 수직 방향은 너무 회전하지 않게 제한
        verticalAngle = Mathf.Clamp(verticalAngle, -80f, 80f);

        // Transform 컴포넌트에 회전량을 적용한다
        transform.rotation = Quaternion.Euler(verticalAngle, horizontalAngle, 0f);
    }
#endif
}
