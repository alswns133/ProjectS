using System;
using UnityEngine;

/// <summary>
/// 풀링되는 피격 이펙트 1개. 재생이 끝나면 스스로 비활성화하고 스포너에 반환을 알린다.
/// DamageText의 수명 종료 콜백과 같은 계약: 생성/풀 관리는 스포너가, 수명 관리는 자신이 담당.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class HitEffect : MonoBehaviour
{
    private ParticleSystem ps;
    private Action<HitEffect> onFinished;

    private void Awake()
    {
        ps = GetComponent<ParticleSystem>();

        // Stop Action이 Callback이어야 OnParticleSystemStopped가 호출된다.
        // 인스펙터 설정에 의존하면 프리팹마다 빠뜨릴 수 있으므로 코드에서 강제한다.
        var main = ps.main;
        main.stopAction = ParticleSystemStopAction.Callback;
    }

    /// <summary>
    /// 지정 위치에서 이펙트를 재생한다. 재생이 끝나면 onFinished로 풀 반환을 알린다.
    /// </summary>
    public void Play(Vector3 position, Action<HitEffect> onFinished)
    {
        this.onFinished = onFinished;
        transform.position = position;
        gameObject.SetActive(true);
        ps.Play();
    }

    // Stop Action = Callback 설정 덕에 파티클이 모두 사라진 프레임에 Unity가 호출한다.
    // 주의: 루트 ParticleSystem 기준이므로, 자식 파티클이 루트보다 오래 살면 잘려 보인다
    // → 프리팹을 만들 때 루트의 Duration/Lifetime이 가장 길게 유지할 것.
    private void OnParticleSystemStopped()
    {
        gameObject.SetActive(false);
        onFinished?.Invoke(this);
    }
}
