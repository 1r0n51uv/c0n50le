namespace C0n50le.Services;

/// <summary>A small log in the data folder, for the diagnostics report. Never throws.</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string _file = Path.Combine(Path.GetTempPath(), "c0n50le.log");

    public static void UseFile(string file) => _file = file;

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                if (File.Exists(_file) && new FileInfo(_file).Length > 256_000)
                {
                    var keep = File.ReadAllLines(_file).TakeLast(400).ToArray();
                    File.WriteAllLines(_file, keep);
                }
                File.AppendAllText(_file, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static IEnumerable<string> Recent(int lines)
    {
        lock (Gate)
        {
            try { return File.Exists(_file) ? File.ReadAllLines(_file).TakeLast(lines).ToArray() : []; }
            catch (IOException) { return []; }
            catch (UnauthorizedAccessException) { return []; }
        }
    }
}
