namespace ProjectS.Logging
{
    /// <summary>분류된 로그를 소비하는 저장소의 공통 계약이다.</summary>
    public interface ILogSink
    {
        void Write(in LogEntry entry);
    }

    /// <summary>프레임 기반 flush·배칭이 필요한 sink의 선택 계약이다.</summary>
    public interface ILogTickableSink
    {
        void Tick(float unscaledDeltaTime);
        void Flush();
    }
}
