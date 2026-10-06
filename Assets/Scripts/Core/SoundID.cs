using UnityEngine;

namespace ProjectS.Core
{
    /// <summary>코드에서 자주 쓰는 사운드 ID 상수. 값은 SoundTable의 Index와 같아야 한다(숫자를 코드에 흩뿌리지 않기 위함).</summary>
    public static class SoundID
    {
       /// <summary>타이틀(로그인) 화면 BGM.</summary>
       public const int BGM_Title = 101;

       /// <summary>캐릭터 선택 화면 BGM.</summary>
       public const int BGM_CharacterSelect = 102;

       /// <summary>튜토리얼 BGM.</summary>
       public const int BGM_Tutorial = 103;

       /// <summary>마을 BGM.</summary>
       public const int BGM_VillageGather = 104;

       /// <summary>던전 1 BGM.</summary>
       public const int BGM_Dungeon1 = 105;

       /// <summary>던전 2 BGM.</summary>
       public const int BGM_Dungeon2 = 106;

       /// <summary>레이드 BGM.</summary>
       public const int BGM_Raid = 107;

       /// <summary>장비 장착/해제, 인벤 슬롯 이동음. 세 경로가 한 클립을 공유한다.</summary>
       public const int SFX_ItemMove = 201;

       /// <summary>상점 구매/판매 성사음. 구매·소모품 판매·장비 판매가 한 클립을 공유한다.</summary>
       public const int SFX_Trade = 202;

       /// <summary>팝업창 열기음(인벤/장비/상점 등). UIManager.ShowPopup에서 공통 재생.</summary>
       public const int SFX_PopupOpen = 203;

       /// <summary>팝업창 닫기음(인벤/장비/상점 등). UIManager.ClosePopup에서 공통 재생.</summary>
       public const int SFX_PopupClose = 204;

       /// <summary>보스 그로기(무력화) 돌입음. EnemyGroggyState.Enter에서 2D로 재생(거리 감쇠 없이 들려야 하는 연출음).</summary>
       public const int SFX_BossGroggy = 401;

    }
}
