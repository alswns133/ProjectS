using System;

[System.Serializable]
public class SoundTable : IDataRow
{
    public string Description;
    public int Index;
    public string Scene;
    public string SoundName;
    public string SoundType;
    public string FileName;
    public float Volume;
    public bool Loop;

    int IDataRow.Index => Index;


    /// <summary>
    /// 기계가 판단 가능한 값만 검증. Scene/SoundType은 기획 영역이라 제외.
    /// </summary>
    /// <param name="error">에러 메세지</param>
    public bool Validate(out string error)
    {
        // FileName: 비면 어차피 로드 실패 → 치명적, 호출부에서 제외 처리
        if (string.IsNullOrWhiteSpace(FileName))
        {
            error = $"Index {Index}: FileName이 비어있음 (제외됨)";
            return false;
        }

        // Volume: 범위 벗어나면 조용히 0~1로 보정
        Volume = Math.Clamp(Volume, 0f, 1f);

        error = null;
        return true;   // FileName만 멀쩡하면 데이터는 유효(Volume은 보정 완료)
    }
}
