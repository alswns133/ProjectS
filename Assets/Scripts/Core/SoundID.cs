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

    }
}
