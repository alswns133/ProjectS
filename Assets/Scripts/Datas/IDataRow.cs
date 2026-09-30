namespace ProjectS.Data
{
    /// <summary>데이터 테이블의 각 행이 구현해야 하는 인터페이스. JsonManager가 이 계약으로 행을 검증·색인한다.</summary>
    public interface IDataRow
    {
        /// <summary>테이블 안에서 유일한 행 키. JsonManager Dictionary의 키가 된다(중복이면 경고 후 뒤 행이 버려진다).</summary>
        int Index { get; }

        /// <summary>
        /// 각 행이 스스로 유효성을 검사한다. 로딩 시 JsonManager가 호출하며, false면 그 행은 테이블에서 제외된다.
        /// 치명적이지 않은 입력 실수는 여기서 안전한 값으로 보정하고 true를 돌려줘도 된다.
        /// </summary>
        /// <param name="error">탈락·경고 사유(통과 시 null).</param>
        /// <returns>테이블에 넣어도 되는 행이면 true.</returns>
        bool Validate(out string error);
    }
}
