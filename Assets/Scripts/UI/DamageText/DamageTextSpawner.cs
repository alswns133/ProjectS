using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CombatEvents의 데미지 발생 이벤트를 받아 월드 데미지 텍스트를 띄운다.
/// 전투 로직은 숫자와 위치만 발행하고, 연출 생성/풀링은 여기서만 처리한다.
/// </summary>
public class DamageTextSpawner : MonoBehaviour
{
    [SerializeField] private DamageText textPrefab;
    [SerializeField] private int initialPoolSize = 10;

    // 같은 위치에 여러 타격이 들어와도 숫자가 완전히 겹치지 않게 살짝 흩뿌린다.
    [SerializeField] private float spawnJitter = 0.3f;

    private readonly Queue<DamageText> pool = new Queue<DamageText>();

    private void Awake()
    {
        for (int i = 0; i < initialPoolSize; i++)
            pool.Enqueue(CreateText());
    }

    private void OnEnable() => CombatEvents.OnDamageDealt += OnDamageDealt;
    private void OnDisable() => CombatEvents.OnDamageDealt -= OnDamageDealt;

    private void OnDamageDealt(Vector3 worldPos, int amount)
    {
        Vector2 jitter = Random.insideUnitCircle * spawnJitter;
        Vector3 position = worldPos + new Vector3(jitter.x, 0f, jitter.y);

        // 풀에 남은 것이 없을 때만 추가 생성한다. 일반 상황에서는 Instantiate가 반복되지 않는다.
        DamageText text = pool.Count > 0 ? pool.Dequeue() : CreateText();
        text.Show(amount, position, ReturnToPool);
    }

    private DamageText CreateText()
    {
        DamageText text = Instantiate(textPrefab, transform);
        text.gameObject.SetActive(false);
        return text;
    }

    private void ReturnToPool(DamageText text)
    {
        // 수명 종료와 비활성화 타이밍이 겹쳐도 같은 인스턴스가 중복 등록되지 않게 방어한다.
        if (text != null && !pool.Contains(text))
            pool.Enqueue(text);
    }
}
