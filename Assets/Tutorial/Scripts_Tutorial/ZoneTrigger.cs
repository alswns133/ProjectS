using UnityEngine;

public class ZoneTrigger : MonoBehaviour
{
    [Header("조작할 오브젝트 설정")]
    // 활성화할 오브젝트
    public GameObject objectToActivate;
    // 비활성화할 오브젝트
    public GameObject objectToDeactivate;

    [Header("태그 설정")]
    // 충돌을 감지할 캐릭터의 태그 (기본값은 Player)
    public string targetTag = "Player";

    // 트리거 구역에 무언가 들어왔을 때 실행되는 함수
    private void OnTriggerEnter(Collider other)
    {
        // 들어온 오브젝트의 태그가 지정한 캐릭터 태그와 일치하는지 확인
        if (other.CompareTag(targetTag))
        {
            // 오브젝트 활성화
            if (objectToActivate != null)
            {
                objectToActivate.SetActive(true);
            }

            // 오브젝트 비활성화
            if (objectToDeactivate != null)
            {
                objectToDeactivate.SetActive(false);
            }

            // (선택사항) 한 번만 실행되게 하고 싶다면 이 스크립트를 스스로 비활성화
            // this.enabled = false;
        }
    }
}
