using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.UI;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 보스 없이 보스 HP 바를 검증하는 테스트 하네스. (2026-09-07 TH)
    ///
    /// 진짜 보스는 <c>Boss.Start()</c>가 <c>BossEvents.FireBossAppeared</c>를 쏠 때만 바가 뜬다. HUD 전용 씬에는
    /// 보스도 NavMesh도 없어서 세그먼트 트랙·잔상·피격 플래시·줄 넘김 글리치를 눈으로 확인할 방법이 없다.
    /// 그래서 이벤트와 프레젠터를 건너뛰고 <see cref="BossHpView"/>의 공개 메서드
    /// (<c>Show</c>/<c>SetHp</c>/<c>SetGroggy</c>/<c>Hide</c>)를 직접 호출한다.
    /// 가짜 <c>Boss</c>·<c>EnemyStats</c>를 만들지 않는 이유도 같다 — 그쪽은 NavMeshAgent·Animator까지 딸려 온다.
    ///
    /// <b>진짜 보스가 있는 씬에서는 이 컴포넌트를 꺼 둔다.</b> 프레젠터와 이 테스터가 같은 뷰에 서로 다른 HP를
    /// 써 넣어 바가 튄다. 보스가 없는 씬에서는 프레젠터가 아무 이벤트도 못 받으므로 충돌하지 않는다.
    ///
    /// 키는 F2~F9. 기존 디버그 스크립트가 쓰는 키(F1·0·9·H·L·M·R·U·V·,·.·/)를 피해서 골랐다.
    /// 키보드 포커스 없이도 되도록 인스펙터 컨텍스트 메뉴(⋮)에 같은 동작을 걸어 뒀다.
    /// </summary>
    public class BossHpTester : MonoBehaviour
    {
        [Header("대상 (비우면 씬에서 찾는다)")]
        [SerializeField] private BossHpView view;

        [Header("가짜 보스")]
        [SerializeField] private string bossName = "SENTINEL // 감시자";
        [SerializeField, Min(1)] private int maxHp = 100000;
        // 풀 HP일 때 표시할 줄 수(로아식 다단 바). 0이면 줄 없이 단일 바로 그린다.
        [SerializeField, Min(0)] private int segmentCount = 3;

        [Header("피해량")]
        [SerializeField, Min(1)] private int normalHit = 1500;
        [SerializeField, Min(1)] private int bigHit = 9000;

        [Header("자동 전투")]
        [SerializeField] private bool autoBattle;
        [SerializeField, Min(0.05f)] private float autoInterval = 1.25f;
        [SerializeField, Min(1)] private int autoMinHit = 900;
        [SerializeField, Min(1)] private int autoMaxHit = 4100;

        [Header("그로기")]
        // F7 한 번에 깎을 그로기 비율.
        [SerializeField, Range(0.01f, 1f)] private float groggyStep = 0.2f;

        // 지금 바가 떠 있는지. Spawn/Despawn으로만 바뀐다.
        private bool spawned;
        private int currentHp;
        private float groggyRatio = 1f;
        private bool groggyLocked;
        private float autoTimer;

        private void Awake()
        {
            // 비활성 오브젝트까지 찾는다 — 보스 바는 barRoot가 꺼진 채로 씬에 놓여 있는 게 정상이다.
            if (view == null) view = FindAnyObjectByType<BossHpView>(FindObjectsInactive.Include);
        }

        private void Start()
        {
            if (view == null)
            {
                Debug.LogWarning("[BossHpTester] 씬에서 BossHpView를 찾지 못했다. 대상을 직접 지정하라.", this);
                enabled = false;
                return;
            }

            Debug.Log("[BossHpTester] F2 등장/리셋 · F3 피격 · F4 대형피격 · F5 줄 강제소진 · " +
                      "F6 자동전투 · F7 그로기 감소 · F8 그로기 잠금 · F9 퇴장", this);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.f2Key.wasPressedThisFrame) Spawn();
            if (keyboard.f3Key.wasPressedThisFrame) Hit(normalHit);
            if (keyboard.f4Key.wasPressedThisFrame) Hit(bigHit);
            if (keyboard.f5Key.wasPressedThisFrame) BreakLine();
            if (keyboard.f6Key.wasPressedThisFrame) ToggleAutoBattle();
            if (keyboard.f7Key.wasPressedThisFrame) DrainGroggy();
            if (keyboard.f8Key.wasPressedThisFrame) ToggleGroggyLock();
            if (keyboard.f9Key.wasPressedThisFrame) Despawn();

            if (!autoBattle || !spawned) return;

            autoTimer += Time.deltaTime;
            if (autoTimer < autoInterval) return;

            autoTimer = 0f;
            Hit(Random.Range(Mathf.Min(autoMinHit, autoMaxHit), Mathf.Max(autoMinHit, autoMaxHit) + 1));
        }

        /// <summary>가짜 보스를 등장시킨다(이미 떠 있으면 풀 HP로 리셋). F2.</summary>
        [ContextMenu("보스 등장 / 리셋")]
        public void Spawn()
        {
            if (view == null) return;

            spawned = true;
            currentHp = maxHp;
            groggyRatio = 1f;
            groggyLocked = false;
            autoTimer = 0f;

            // Show가 snapNext를 세워 첫 SetHp를 드레인 없이 즉시 맞춘다(등장부터 깎이는 연출 방지).
            view.Show(bossName);
            view.SetHp(currentHp, maxHp, segmentCount);
            view.SetGroggy(groggyRatio, groggyLocked);
        }

        /// <summary>바를 내린다. F9.</summary>
        [ContextMenu("보스 퇴장")]
        public void Despawn()
        {
            if (view == null) return;

            spawned = false;
            autoBattle = false;
            view.Hide();
        }

        /// <summary>일반 피격. F3.</summary>
        [ContextMenu("피격 (일반)")]
        public void HitNormal() => Hit(normalHit);

        /// <summary>대형 피격. F4.</summary>
        [ContextMenu("피격 (대형)")]
        public void HitBig() => Hit(bigHit);

        /// <summary>지정 피해를 넣는다. HP가 0이 되면 자동 전투를 멈춘다.</summary>
        /// <param name="amount">깎을 HP.</param>
        public void Hit(int amount)
        {
            if (view == null || !spawned || currentHp <= 0) return;

            currentHp = Mathf.Max(0, currentHp - amount);
            view.SetHp(currentHp, maxHp, segmentCount);

            if (currentHp > 0) return;

            autoBattle = false;
            Debug.Log("[BossHpTester] 처치. F2로 다시 등장.", this);
        }

        /// <summary>
        /// 현재 줄을 즉시 비워 줄 넘김을 강제한다(글리치·색 순환·잔상 리셋 확인용). F5.
        /// 줄이 없거나(segmentCount 0) 마지막 줄이면 남은 HP를 1만 남기고 깎는다.
        /// </summary>
        [ContextMenu("현재 줄 강제 소진")]
        public void BreakLine()
        {
            if (view == null || !spawned || currentHp <= 0) return;

            if (segmentCount <= 0)
            {
                Hit(currentHp - 1);
                return;
            }

            // 남은 줄 수와 그 줄의 하한을 BossHpView와 같은 식으로 계산해, 하한 바로 아래까지 깎는다.
            float hpPerSegment = (float)maxHp / segmentCount;
            int segments = Mathf.CeilToInt(currentHp / hpPerSegment);
            int lower = Mathf.CeilToInt((segments - 1) * hpPerSegment);

            // 마지막 줄이면 하한이 0이라 그대로 죽는다 — 1 남겨 저체력 min-pixel clamp를 볼 수 있게 한다.
            int target = segments <= 1 ? 1 : lower - 1;
            Hit(Mathf.Max(0, currentHp - target));
        }

        /// <summary>자동 전투를 켜고 끈다. F6.</summary>
        [ContextMenu("자동 전투 토글")]
        public void ToggleAutoBattle()
        {
            autoBattle = !autoBattle;
            autoTimer = 0f;
            Debug.Log($"[BossHpTester] 자동 전투 {(autoBattle ? "ON" : "OFF")}", this);
        }

        /// <summary>그로기 게이지를 groggyStep만큼 깎는다(0에 닿으면 다시 가득 찬다). F7.</summary>
        [ContextMenu("그로기 감소")]
        public void DrainGroggy()
        {
            if (view == null || !spawned) return;

            groggyRatio -= groggyStep;
            if (groggyRatio <= 0f) groggyRatio = 1f;   // 그로기 발동 후 재충전을 흉내
            view.SetGroggy(groggyRatio, groggyLocked);
        }

        /// <summary>그로기 잠금(자물쇠 표시)을 토글한다. F8.</summary>
        [ContextMenu("그로기 잠금 토글")]
        public void ToggleGroggyLock()
        {
            if (view == null || !spawned) return;

            groggyLocked = !groggyLocked;
            view.SetGroggy(groggyRatio, groggyLocked);
        }
    }
}
