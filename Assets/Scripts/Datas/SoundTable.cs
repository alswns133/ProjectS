using System;
using ProjectS.Core;

namespace ProjectS.Data
{
    [Serializable]
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
        /// SoundType 문자열을 파싱한 믹서 라우팅 분류. Validate에서 채워진다.
        /// 재생 때마다 문자열을 비교하지 않으려고 로딩 시점에 한 번만 파싱해 둔다.
        /// (private setter라 JSON 역직렬화 대상이 아니다.)
        /// </summary>
        public SoundCategory Category { get; private set; } = SoundCategory.SFX;


        /// <summary>
        /// 재생에 반드시 필요한 값만 검사한다. Scene은 기획 참고용이라 검사에서 제외.
        /// FileName이 비면 로드 자체가 불가능하므로 이 행을 탈락시키고, Volume은 안전 범위로 보정한다.
        /// SoundType은 믹서 그룹 라우팅 기준(2026-09-23 옵션 볼륨 분리)이라 여기서 파싱한다.
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

            // SoundType: 오타 하나로 소리가 통째로 빠지는 것보다 효과음 볼륨을 따라가는 편이 낫다 →
            // 탈락시키지 않고 SFX로 떨어뜨리되, 옵션 슬라이더가 엉뚱하게 먹으므로 경고는 남긴다.
            if (Enum.TryParse(SoundType?.Trim(), true, out SoundCategory category)
                && Enum.IsDefined(typeof(SoundCategory), category))
            {
                Category = category;
            }
            else
            {
                Category = SoundCategory.SFX;
                UnityEngine.Debug.LogWarning(
                    $"[SoundTable] Index {Index}: 알 수 없는 SoundType '{SoundType}' → SFX로 처리 " +
                    $"(허용: {string.Join("/", Enum.GetNames(typeof(SoundCategory)))})");
            }

            error = null;
            return true;   // FileName만 유효하면 데이터는 사용 가능(Volume은 위에서 보정 완료)
        }
    }
}
