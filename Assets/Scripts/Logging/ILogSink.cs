namespace ProjectS.Logging
{
    /// <summary>분류된 로그를 소비하는 저장소의 공통 계약이다.</summary>
    public interface ILogSink
    {
        /// <summary>
        /// 로그 한 줄을 받는다. LogManager가 메인 스레드에서 호출하며, 무거운 I/O·네트워크는 여기서 바로 하지 말고
        /// 모아 두었다가 <see cref="ILogTickableSink.Tick"/>/<see cref="ILogTickableSink.Flush"/>에서 처리한다.
        /// </summary>
        /// <param name="entry">기록할 로그.</param>
        void Write(in LogEntry entry);
    }

    /// <summary>프레임 기반 flush·배칭이 필요한 sink의 선택 계약이다.</summary>
    public interface ILogTickableSink
    {
        /// <summary>매 프레임 호출. 경과 시간을 쌓아 주기·배치 조건이 차면 내보낸다.</summary>
        /// <param name="unscaledDeltaTime">timeScale 영향을 받지 않는 프레임 시간(일시정지 중에도 흐른다).</param>
        void Tick(float unscaledDeltaTime);

        /// <summary>쌓인 로그를 주기를 기다리지 않고 즉시 내보낸다(종료·크래시 직전 등).</summary>
        void Flush();
    }
}
