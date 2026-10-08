using System.IO;
using System.Text;

namespace ClaudeTimer.Services;

/// <summary>
/// Simpel fejllog i %LOCALAPPDATA%\ClaudeTimer\log.txt. Skriver aldrig tokens
/// eller e-mails – kun hændelser, kilder og fejltyper. Kaster aldrig selv.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClaudeTimer");

    public static string FilePath { get; } = Path.Combine(Directory, "log.txt");

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN ", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
            .Append(' ').Append(level).Append(' ').Append(message);
        if (exception is not null)
        {
            line.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);
            if (level == "ERROR" && exception.StackTrace is { } stack)
            {
                line.AppendLine().Append(stack);
            }
        }

        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                {
                    File.Move(FilePath, Path.Combine(Directory, "log.1.txt"), overwrite: true);
                }

                File.AppendAllText(FilePath, line.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
        {
            // En log må aldrig vælte appen.
        }
    }
}
