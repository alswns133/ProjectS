namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 이동식 창(<see cref="DraggableWindow"/>)의 위치 저장 키(windowId) 단일 소스.
    /// 인스펙터에 매직 스트링을 손으로 치지 않도록 여기서 const로 관리한다 — 창마다 값이 유일해야 하며
    /// (같으면 PlayerPrefs 저장 위치가 섞임), 새 이동식 창을 만들 때 여기에 상수를 추가한다.
    /// </summary>
    public static class WindowIds
    {
        /// <summary>인벤토리 창.</summary>
        public const string Inventory = "inventory";

        /// <summary>장비창.</summary>
        public const string Equipment = "equipment";

        /// <summary>강화창.</summary>
        public const string Enhance = "enhance";

        // 후속: 스킬창 등 이동식 창이 생기면 여기에 추가.
        // public const string Skill = "skill";
    }
}
