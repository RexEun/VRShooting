using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class MoveAgent : MonoBehaviour
{
    NavMeshAgent agent; // 내비메시 에이전트

    void Start()
    {
        // 내비메시 에이전트 취득
        agent = GetComponent<NavMeshAgent>();

        // 시작 후 잠시 후 첫 목적지로 이동 (스폰 후 NavMesh 배치 안정화)
        StartCoroutine(StartMoveRoutine());
    }

    IEnumerator StartMoveRoutine()
    {
        yield return null; // 1프레임 대기하여 NavMesh 위 안정적 배치 보장
        GotoNextPoint();
    }

    void Update()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        // 경로 계산 중이 아닐 때, 목적지 부근에 도달했는지 판정
        // (pathPending 중에는 remainingDistance가 0으로 반환되어 무한 재경로 탐색 버그가 발생하는 것을 방지)
        if (!agent.pathPending && agent.remainingDistance < 0.5f)
        {
            GotoNextPoint();
        }
    }

    void GotoNextPoint()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        // 바닥의 이동 지점을 랜덤으로 생성 (-20 ~ 20)
        var randomPoint = new Vector3(Random.Range(-20.0f, 20.0f), 0.0f, Random.Range(-20.0f, 20.0f));

        // 내비메시 상의 유효한 지점인지 샘플링 보정 후 목적지 지정
        if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, 10.0f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
        else
        {
            agent.SetDestination(randomPoint);
        }
    }
}