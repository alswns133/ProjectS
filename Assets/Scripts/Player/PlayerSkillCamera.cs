using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace ProjectS.Players
{
    /// <summary>
    /// 스킬 전용 시네머신 카메라를 켜고 끄는 통로.
    /// 클립의 Animation Event가 <see cref="OnSkillCamera"/>/<see cref="OffSkillCamera"/>를 호출하면
    /// 해당 슬롯의 카메라가 활성화되고, 평소 카메라로는 CinemachineBrain이 알아서 블렌딩해 돌아온다.
    ///
    /// ★ 기존 카메라를 코드로 밀고 당기지 않고 카메라를 따로 두는 이유:
    ///   구도(위치·각도·FOV)를 인스펙터에서 눈으로 잡을 수 있고, 평소 조작용 카메라의 로직
    ///   (줌·마우스 회전)을 전혀 건드리지 않아 연출이 끝난 뒤 원래 상태로 돌아오는 것이 보장된다.
    ///
    /// ★ 반드시 Animator와 같은 GameObject(플레이어 루트)에 붙일 것.
    ///   Animation Event는 Animator가 붙은 오브젝트의 컴포넌트에서만 메서드를 찾는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerSkillCamera : MonoBehaviour
    {
        // 키는 클립 Animation Event의 string 인자와 맞춘다(다른 이펙트 슬롯과 같은 규약).
        [Serializable]
        private class SkillCameraSlot
        {
            public string key;

            // 이 슬롯이 켤 스킬 전용 카메라. 씬에 배치하고 평소에는 꺼 둔다
            // (평소 카메라보다 Priority를 높게 잡아야 활성화 시 화면을 가져온다).
            public CinemachineCamera camera;

            [Header("배치")]
            // true면 켜는 순간 캐릭터 기준으로 카메라를 옮긴다.
            // 스킬은 캐릭터가 어디에 있든 발동하므로, 씬에 고정된 자리에 두면 엉뚱한 곳을 비춘다.
            public bool placeAtPlayer = true;

            // 캐릭터 기준 배치 오프셋(+Z 정면, +Y 위, +X 오른쪽).
            // 배치 후에는 캐릭터를 따라가지 않으므로 '시전 위치에서 이만큼 떨어진 자리'에 선다.
            public Vector3 offset = new Vector3(0f, 3f, -6f);

            // true면 배치·회전 중 캐릭터가 있던 지점을 계속 바라본다.
            public bool lookAtPivot = true;

            // 바라볼 높이 보정(발밑이 아니라 가슴 높이를 보게 한다).
            public float lookHeight = 1.2f;

            [Header("회전 (0이면 회전 없음)")]
            // 시전 지점을 축으로 도는 각도. 360이면 한 바퀴.
            public float orbitDegrees;

            // 도는 데 걸리는 시간(초). 각도/시간이 곧 회전 속도다.
            [Min(0.01f)] public float orbitDuration = 4f;
        }

        [SerializeField] private SkillCameraSlot[] cameras;

        // 평소 조작용 카메라의 마우스 회전을 막을 대상. 스킬 카메라가 화면을 가져간 동안에도
        // 이 컴포넌트는 계속 돌고 있어서, 잠그지 않으면 연출 중 쌓인 마우스 입력이
        // 복귀하는 순간 화면을 홱 돌려버린다. 인스펙터 연결이 없으면 씬에서 찾는다.
        [SerializeField] private CameraPivotController cameraPivot;

        // 해제 이벤트를 놓쳤을 때 평소 카메라로 되돌리는 안전장치.
        // 없으면 스킬 카메라에 갇혀 게임을 진행할 수 없게 된다.
        [SerializeField, Min(0.5f)] private float maxActiveTime = 10f;

        // 매 이벤트마다 배열을 뒤지지 않도록 Awake에서 1회 구축하는 조회용 사전.
        private readonly Dictionary<string, SkillCameraSlot> cameraMap = new Dictionary<string, SkillCameraSlot>();

        // 구르기·피격·사망으로 스킬이 끊겼는지 확인할 중앙 컨텍스트.
        private Player player;

        // 지금 켜져 있는 슬롯. null이면 평소 카메라 상태다.
        private SkillCameraSlot active;
        private float activeStartTime;

        // 회전 기준점(켜는 순간의 캐릭터 위치). 캐릭터가 이동해도 갱신하지 않는다 —
        // 따라가지 않는 것이 이 연출의 목적이기 때문이다.
        private Vector3 pivot;
        private float orbitRemaining;
        private float orbitSpeed;

        /// <summary>스킬 전용 카메라가 켜져 있는지 여부.</summary>
        public bool IsActive => active != null;

        private void Awake()
        {
            player = GetComponent<Player>();

            // CameraPivotController는 카메라 릭 오브젝트에 있어 플레이어의 자식이 아닐 수 있다
            // (마우스 회전은 플레이어 트랜스폼과 별개로 도는 구조라서다) → 씬 전체에서 찾는다.
            if (cameraPivot == null) cameraPivot = FindFirstObjectByType<CameraPivotController>();

            if (cameras == null) return;

            foreach (SkillCameraSlot slot in cameras)
            {
                if (slot == null || string.IsNullOrEmpty(slot.key)) continue;

                // 다른 이벤트 슬롯과 같은 방침: 언더바 표기 차이를 흡수하려 정규화한 키로 등록·조회한다.
                string normKey = AnimationEventKey.Normalize(slot.key);

                if (cameraMap.ContainsKey(normKey))
                {
                    Debug.LogWarning($"Duplicate skill camera key '{slot.key}'. Only the first slot is used.", this);
                    continue;
                }

                cameraMap.Add(normKey, slot);

                // 시작 상태를 강제로 맞춘다. 씬에 켠 채로 저장해 두면 게임 시작부터 스킬 화면이 나온다.
                if (slot.camera != null) slot.camera.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (active == null) return;

            // 구르기·피격·사망으로 스킬이 끊기면 해제 이벤트가 담긴 프레임까지 재생되지 않는다.
            if (player != null && player.IsActionInterrupted)
            {
                Deactivate();
                return;
            }

            if (Time.time - activeStartTime >= maxActiveTime)
            {
                Debug.LogWarning($"Skill camera stayed active for {maxActiveTime}s. Switching back by safety timeout — check the OffSkillCamera Animation Event.", this);
                Deactivate();
                return;
            }

            UpdateOrbit();
        }

        private void OnDisable()
        {
            // 비활성화되면 Update가 멈춰 안전장치도 함께 멈춘다.
            // 스킬 카메라가 켜진 채 남지 않도록 여기서 되돌린다(씬 전환 대비).
            Deactivate();
        }

        /// <summary>
        /// 스킬 전용 카메라를 켠다. 인자는 인스펙터에 등록한 슬롯 키.
        /// 켜는 순간 캐릭터 기준으로 배치되고, 그 뒤로는 캐릭터를 따라가지 않는다.
        /// </summary>
        /// <param name="key">슬롯 키. 언더바 표기 차이는 무시된다(예: "Skill_4_1" == "Skill41").</param>
        public void OnSkillCamera(string key)
        {
            // 다른 이펙트 이벤트와 같은 게이트: 구르기·피격·사망으로 끊겼으면
            // 블렌드 아웃 중 뒤늦게 도착한 이벤트로 화면이 넘어가지 않게 막는다.
            if (player != null && player.IsActionInterrupted) return;

            if (!TryGetSlot(key, out SkillCameraSlot slot)) return;

            // 다른 스킬 카메라가 켜져 있으면 먼저 끈다. 둘 다 켜 두면 Priority에 따라
            // 어느 쪽이 잡힐지 예측하기 어려워진다.
            if (active != null && active != slot) Deactivate();

            active = slot;
            activeStartTime = Time.time;

            pivot = transform.position;

            if (slot.placeAtPlayer) Place(slot);

            orbitRemaining = Mathf.Abs(slot.orbitDegrees);
            orbitSpeed = Mathf.Approximately(slot.orbitDegrees, 0f)
                ? 0f
                : slot.orbitDegrees / Mathf.Max(0.01f, slot.orbitDuration);

            if (slot.camera != null) slot.camera.gameObject.SetActive(true);

            // 연출이니 유저 시점 조작을 막는다. 안 막으면 화면엔 안 보여도 평소 카메라가
            // 계속 돌고 있다가, 연출이 끝나 블렌드로 돌아오는 순간 그 회전이 한 번에 반영돼 튄다.
            if (cameraPivot != null) cameraPivot.SetInputLocked(true);
        }

        /// <summary>
        /// 스킬 전용 카메라를 끄고 평소 카메라로 돌아간다.
        /// 되돌아가는 속도·곡선은 CinemachineBrain의 블렌드 설정이 결정한다.
        /// </summary>
        /// <param name="key">슬롯 키. 지금 켜져 있는 카메라를 끄므로 사실상 확인용이다.</param>
        public void OffSkillCamera(string key)
        {
            Deactivate();
        }

        /// <summary>
        /// 켜져 있는 스킬 카메라를 즉시 끈다. 컷신 종료처럼 클립 이벤트 밖에서 끊어야 할 때 쓴다.
        /// </summary>
        public void Deactivate()
        {
            if (active == null) return;

            if (active.camera != null) active.camera.gameObject.SetActive(false);

            active = null;
            orbitRemaining = 0f;

            if (cameraPivot != null) cameraPivot.SetInputLocked(false);
        }

        private void Place(SkillCameraSlot slot)
        {
            if (slot.camera == null) return;

            Transform cam = slot.camera.transform;

            // 오프셋은 캐릭터 기준값이라 캐릭터가 어느 쪽을 보고 시전하든 같은 구도가 나온다.
            cam.position = pivot + transform.rotation * slot.offset;

            if (slot.lookAtPivot) cam.LookAt(pivot + Vector3.up * slot.lookHeight);
        }

        private void UpdateOrbit()
        {
            if (orbitRemaining <= 0f || active.camera == null) return;

            // 남은 각도보다 더 돌지 않게 잘라낸다 → 360도를 주면 정확히 제자리에서 끝난다.
            float step = Mathf.Min(Mathf.Abs(orbitSpeed) * Time.deltaTime, orbitRemaining);
            orbitRemaining -= step;

            Transform cam = active.camera.transform;

            // 시전 지점을 축으로 카메라를 돌린다. 카메라 자체를 회전시키는 게 아니라
            // 기준점 주위를 도는 것이라 피사체가 화면 중앙에 유지된다.
            cam.RotateAround(pivot, Vector3.up, Mathf.Sign(orbitSpeed) * step);

            if (active.lookAtPivot) cam.LookAt(pivot + Vector3.up * active.lookHeight);
        }

        private bool TryGetSlot(string key, out SkillCameraSlot slot)
        {
            // 다른 이펙트 컴포넌트와 같은 방침: 이벤트 인자 실수(오타)는 경고만 남기고 플레이는 계속한다.
            if (string.IsNullOrEmpty(key) || !cameraMap.TryGetValue(AnimationEventKey.Normalize(key), out slot) || slot.camera == null)
            {
                Debug.LogWarning($"Skill camera key not found or empty ('{key}'). Check the Animation Event string.", this);
                slot = null;
                return false;
            }

            return true;
        }
    }
}
