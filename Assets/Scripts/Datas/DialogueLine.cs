using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 대사 화자. 이름·초상화를 줄마다 박지 않고 '역할'로 런타임에 자동 치환하기 위한 구분이다
    /// (Npc=상호작용 중인 NPC, Player=플레이어 캐릭터, Other=그 자리에만 나오는 제3자).
    /// 좌/우 배치도 이 값으로 정한다(Npc·Other=왼쪽, Player=오른쪽).
    /// </summary>
    public enum DialogueSpeaker
    {
        Npc,      // 상호작용 중인 NPC (왼쪽)
        Player,   // 플레이어 캐릭터 (오른쪽)
        Other     // 제3자 (왼쪽). 이름·초상화를 줄에서 직접 지정
    }

    /// <summary>
    /// 대화 한 줄. 화자와 대사, 그리고 Other 전용 이름·초상화 주소를 담는다.
    /// Npc/Player의 이름·초상화는 데이터에 두지 않고 재생 시 런타임 값으로 채운다(데이터 재사용·로컬라이징 대비).
    /// </summary>
    [Serializable]
    public class DialogueLine
    {
        /// <summary>이 줄의 화자. Npc/Player면 이름·초상화가 런타임에 자동으로 채워진다.</summary>
        public DialogueSpeaker Speaker;

        /// <summary>Other 화자의 표시 이름. Npc/Player면 비워 둔다.</summary>
        public string Name = string.Empty;

        /// <summary>Other 화자의 초상화 어드레서블 주소. 비어 있거나 로드 실패하면 초상화 없이 이름·대사만 표시한다.</summary>
        public string PortraitAddress = string.Empty;

        /// <summary>대사 내용.</summary>
        public string Text = string.Empty;
    }
}
