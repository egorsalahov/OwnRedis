using System.Collections.Concurrent;
using System.Diagnostics;

namespace OwnRedis.Core;

public static class AdminLogger
{
    public static ConcurrentQueue<string> Logs = new();
    private static readonly Process CurrentProcess = Process.GetCurrentProcess();

    public static void Log(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        Logs.Enqueue($"[{timestamp}] {message}");
        
        // Держим последние 30 записей
        while (Logs.Count > 30) Logs.TryDequeue(out _);
    }

    public static double GetMemoryUsage() 
    {
        // Получаем объем памяти, занимаемый процессом в МБ
        return Math.Round(CurrentProcess.WorkingSet64 / 1024.0 / 1024.0, 2);
    }
}