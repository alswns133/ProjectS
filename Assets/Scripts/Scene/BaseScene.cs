using UnityEngine;

public abstract class BaseScene : MonoBehaviour
{
    /// <summary>
    /// 씬 오브젝트가 생성된 직후 1회 호출되는 초기화 훅(GameSceneManager.RegisterScene에서 호출).
    /// Enter보다 먼저, 씬이 화면에 뜨기 전에 필요한 준비를 여기서 한다.
    /// </summary>
    public abstract void Initialize();
    /// <summary>
    /// 신 파일이 로드가 완료되는 시점에 변경되는 신의 Enter 메서드가 호출됩니다.
    /// </summary>
    public abstract void Enter();
    /// <summary>
    /// 신 파일이 로드가 완료되는 시점에 이전 신의 Exit 메서드가 호출됩니다.
    /// </summary>
    public abstract void Exit();

    /// <summary>
    /// 로딩 중 매 프레임 호출. 씬별 로딩 화면 연출용(영상 재생, 진행도에 따른 일러스트 전환 등).
    /// 진행도 바 자체는 공통 UIManager가 그리므로, 여기서 바를 그리지 말 것.
    /// </summary>
    /// <param name="progress">progress는 "연출이 진행도에 반응해야 할 때"를 위해 받아둔다.</param>
    public abstract void Progress(float progress);

}
