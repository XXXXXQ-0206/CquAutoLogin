using System.IO;
using System.Text;

namespace CquAutoLogin.Services;

public sealed class FileLogger
{
    private readonly object _writeLock = new();

    public FileLogger(string logDirectoryPath)
    {
        Directory.CreateDirectory(logDirectoryPath);
        LogFilePath = Path.Combine(logDirectoryPath, "monitor.log");
    }

    public string LogFilePath { get; }

    public event EventHandler<string>? LineWritten;

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Error(Exception exception, string message)
    {
        Write("ERROR", $"{message} | {exception.GetType().Name}: {exception.Message}");
    }

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        LineWritten?.Invoke(this, line);

        lock (_writeLock)
        {
            File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
    }
}
