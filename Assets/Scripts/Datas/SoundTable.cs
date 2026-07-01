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
    /// 재생에 반드시 필요한 값만 검사한다. Scene/SoundType은 기획 참고용이라 검사에서 제외.
    /// FileName이 비면 로드 자체가 불가능하므로 이 행을 탈락시키고, Volume은 안전 범위로 보정한다.
    /// </summary>
    /// <param name="error">탈락 사유(통과 시 null)</param>
    /// <returns>재생 가능한 행이면 true</returns>
    public bool Validate(out string error)
    {
        // FileName: 비어 있으면 로드 실패로 이어지는 치명적 결함 → 이 행을 아예 제외한다.
        if (string.IsNullOrWhiteSpace(FileName))
        {
            error = $"Index {Index}: FileName이 비어있음 (제외됨)";
            return false;
        }

        // Volume: 데이터 입력 실수(음수·1 초과)를 방어하기 위해 0~1로 보정
        Volume = Math.Clamp(Volume, 0f, 1f);

        error = null;
        return true;   // FileName만 유효하면 데이터는 사용 가능(Volume은 위에서 보정 완료)
    }
}
