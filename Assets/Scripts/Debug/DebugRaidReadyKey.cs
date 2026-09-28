#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Core;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.UI;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 에디터 전용: T 키로 레이드 보스를 클리어할 수 있는 상태를 즉시 만든다 —
    /// 레벨 <see cref="TargetLevel"/>로 올리고, 캐릭터에 맞는 30레벨 무기(검사 131030 / 거너 132030)를 지급해 바로 착용한다.
    /// 레이드 흐름(등장연출·페이즈·제한 시간)을 반복 테스트할 때 매번 육성을 밟지 않기 위한 임시 도구다.
    /// <para>
    /// 씬 배치 불필요 — 플레이 시작 시 자기 오브젝트를 만들어 붙는다(AutoCreate). 파일 전체가 #if UNITY_EDITOR라
    /// 빌드에는 포함되지 않는다. 레벨·장착은 기존 경로(<see cref="PlayerStats.SetLevel"/>,
    /// <see cref="InventoryManager.AddItem"/>/<see cref="InventoryManager.Equip"/>)를 그대로 타므로 HUD·장비창·저장이 함께 따라온다.
    /// </para>
    /// <para>
    /// ★ <b>마을에서 누르고 입장하세요.</b> 원격 클라의 보스 데미지는 서버가 <b>접속 시 읽은 세이브</b>로 계산한다
    /// (<c>NetworkDamageRelay.BuildServerDamage</c>). 그래서 원격 클라는 T 후 저장이 끝난 뒤 <b>재접속</b>해야 보스 데미지에 반영된다.
    /// 호스트(에디터 Host)·오프라인은 클라 계산값을 쓰므로 즉시 반영된다.
    /// </para>
    /// </summary>
    public class DebugRaidReadyKey : MonoBehaviour, ISurvivesReboot
    {
        private const int TargetLevel = 30;
        private const int SwordWeaponId = 131030;   // 유물 검 Lv30
        private const int GunWeaponId = 132030;     // 유물 총 Lv30

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            GameObject go = new GameObject("[DebugRaidReadyKey]");
            go.AddComponent<DebugRaidReadyKey>();
            DontDestroyOnLoad(go);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            // 채팅 등 텍스트 입력 중에는 무시한다 — t가 들어간 문장을 치면 발동하기 때문(raw 키 읽기라 입력 억제 무관).
            if (UiTypingGuard.IsTypingInInputField()) return;

            if (keyboard.tKey.wasPressedThisFrame) MakeRaidReady();
        }

        private void MakeRaidReady()
        {
            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady)
            {
                DevLog.Log("[Debug] JsonManager 준비 전이라 레이드 준비 생략");
                return;
            }

            Player controlled = LocalPlayer.Current;
            if (controlled == null) controlled = FindObjectOfType<Player>();
            if (controlled == null || controlled.Stats == null)
            {
                DevLog.Log("[Debug] 플레이어를 못 찾아 레이드 준비 생략");
                return;
            }

            // 레벨은 조작 중인 캐릭터(멀티 레이드면 아바타)와 저장 원천(PlayerManager.Player)에 둘 다 올린다.
            // 착용 레벨 판정·장비 스탯은 LocalPlayer.Current를, 세이브는 PlayerManager.Player를 보기 때문에
            // 한쪽만 올리면 "착용 불가" 또는 "저장 안 됨"이 된다.
            RaiseLevel(controlled);
            Player saved = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
            if (saved != null && saved != controlled) RaiseLevel(saved);

            GiveAndEquipWeapon(controlled.Stats.CharacterId);

            // 레벨만 바뀌고 무기가 이미 착용돼 있던 경우엔 Equip의 SaveNow가 안 돌므로 여기서 커밋한다.
            PlayerSaveService.SaveNow();
        }

        private static void RaiseLevel(Player player)
        {
            if (player.Stats.Level >= TargetLevel) return;
            player.Stats.SetLevel(TargetLevel);
            DevLog.Log($"[Debug] 레벨 {TargetLevel} 설정: {player.name}");
        }

        // 캐릭터 무기를 지급하고 착용한다. 이미 같은 무기를 차고 있으면 중복 지급하지 않는다(T 연타 대비).
        private static void GiveAndEquipWeapon(int characterId)
        {
            InventoryManager inventory = InventoryManager.Instance;
            if (inventory == null)
            {
                DevLog.Log("[Debug] InventoryManager가 없어 무기 지급 생략");
                return;
            }

            int weaponId = characterId == 2 ? GunWeaponId : SwordWeaponId;

            EquipmentInstance current = inventory.GetEquipped(EquipSlot.Weapon);
            if (current?.Item != null && current.Item.Index == weaponId)
            {
                DevLog.Log($"[Debug] 이미 {weaponId} 착용 중 — 무기 지급 생략");
                return;
            }

            inventory.AddItem(weaponId);

            EquipmentInstance granted = FindInBag(inventory, weaponId);
            if (granted == null)
            {
                DevLog.Log($"[Debug] 무기 {weaponId} 지급 실패(가방이 가득 찼거나 정의 없음)");
                return;
            }

            bool equipped = inventory.Equip(granted);
            DevLog.Log($"[Debug] 무기 {weaponId} 지급" + (equipped ? " + 착용" : " (착용 실패 — 로그/토스트 확인)"));
        }

        private static EquipmentInstance FindInBag(InventoryManager inventory, int itemId)
        {
            foreach (EquipmentInstance instance in inventory.OwnedEquipment)
                if (instance?.Item != null && instance.Item.Index == itemId) return instance;
            return null;
        }
    }
}
#endif
