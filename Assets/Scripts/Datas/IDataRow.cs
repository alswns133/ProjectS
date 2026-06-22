using UnityEngine;

// 데이터 테이블의 각 행이 구현해야 하는 인터페이스
public interface IDataRow
{
    int Index { get; }
    bool Validate(out string error);   // 각 행이 스스로 유효성 검사
}
