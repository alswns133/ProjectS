namespace ProjectS.DbBackup;

/// <summary>
/// backup.log에 한 줄씩 남기고 콘솔에도 찍는다. 스케줄러로 창 없이 돌기 때문에, 실패를 나중에 확인할 곳은 이 파일뿐이다.
/// </summary>
/// <remarks>키·토큰 같은 비밀값은 절대 넘기지 않는다(로그 파일은 공유될 수 있다).</remarks>
internal sealed class BackupLog
{
    private readonly string path;

    public BackupLog(string path)
    {
        this.path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        Console.WriteLine(line);
        File.AppendAllText(path, line + Environment.NewLine);
    }
}
