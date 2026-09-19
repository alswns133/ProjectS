using System.Text.Json.Nodes;

namespace ProjectS.DbBackup;

/// <summary>
/// Firebase RTDB 시간별/일별 백업. 윈도우 작업 스케줄러가 <b>매시 정각</b>에 한 번 실행한다(상주하지 않는다).
/// </summary>
/// <remarks>
/// <para>
/// <b>왜 만들었나(2026-09-18).</b> 무료 요금제(Spark)라 Firebase 자동 백업이 없어, 콘솔에서 루트를 잘못 덮어쓴
/// 사고를 되돌릴 방법이 없었다. 그래서 DB를 <b>DB 밖(서버 PC 디스크)</b>에 날짜별로 쌓는다. 백업을 같은 DB 안에 두면
/// 루트 덮어쓰기 한 번에 백업까지 같이 사라진다.
/// </para>
/// <para>
/// <b>읽기 전용.</b> 이 프로그램은 DB에 절대 쓰지 않는다. 복원은 사람이 콘솔에서 파일을 골라 넣는다
/// (캐릭터 하나면 characters/ 조각을 그 노드에, 전체면 daily/·hourly/를 루트에).
/// </para>
/// <para>
/// 종료 코드: 0 = 저장 성공, 1 = 검증 실패 또는 실행 오류(backup.log 확인), 2 = 설정 오류.
/// 작업 스케줄러의 "마지막 실행 결과"에 이 값이 보인다.
/// </para>
/// </remarks>
internal static class Program
{
    private static async Task<int> Main()
    {
        BackupConfig config;
        try
        {
            config = BackupConfig.Load(Path.Combine(AppContext.BaseDirectory, "dbbackup.config.json"));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[DbBackup] 설정 오류: {e.Message}");
            return 2;
        }

        var log = new BackupLog(config.LogPath);
        DateTime now = DateTime.Now;   // 폴더·파일 이름은 서버 PC 현지 시각 기준(정각 = 현지 정각)

        try
        {
            // ① DB 전체 읽기
            JsonNode root = await BackupSteps.FetchDatabaseAsync(config);

            // ② 검증 — 실패면 저장하지 않는다(망가진 DB가 정상 백업을 밀어내지 않게).
            //   실패해도 아래 ④⑤(지난 날짜 확정·정리)는 계속 돈다 — 오늘 DB가 망가졌다고 어제 확정까지 멈출 이유는 없다.
            bool valid = BackupSteps.Validate(config, root, out string reason);
            if (!valid)
            {
                log.Warn($"검증 실패 — 저장 안 함: {reason}");
            }
            else
            {
                if (reason.Length > 0) log.Warn(reason);

                // ③ 시간별 새 파일 저장(덮어쓰지 않음)
                string hourlyPath = BackupSteps.SaveHourly(config, root, now);
                log.Info($"시간별 저장: {hourlyPath}");
            }

            // ④ 지난 날짜인데 아직 확정 안 된 날을 일일본으로 확정 + 캐릭터 조각 + 2차 복사.
            //   자정 실행만 믿으면 그 시각에 PC가 꺼져 있던 날은 영영 확정되지 않아서, 매 실행마다 밀린 날을 찾는다.
            foreach (DateTime day in BackupSteps.PendingDays(config, now))
            {
                string? dailyPath = BackupSteps.FinalizeDay(config, day);
                if (dailyPath == null)
                {
                    log.Warn($"{day:yyyy-MM-dd} 확정할 시간본이 없습니다(hourly 폴더는 남겨 둠).");
                    continue;
                }

                log.Info($"일일본 확정: {dailyPath}");
                int count = BackupSteps.SplitCharacters(config, dailyPath, day);
                log.Info($"캐릭터 조각 {count}개 생성");

                string? copied = BackupSteps.CopySecondary(config, dailyPath);
                if (copied != null) log.Info($"2차 보관 복사: {copied}");
            }

            // ⑤ 보관 기간 정리
            int removed = BackupSteps.PruneOld(config, now);
            if (removed > 0) log.Info($"보관 기간 지난 항목 {removed}개 정리");

            return valid ? 0 : 1;
        }
        catch (Exception e)
        {
            log.Error($"실행 실패: {e}");
            return 1;
        }
    }
}
