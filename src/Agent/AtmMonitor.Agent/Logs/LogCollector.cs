using System.Globalization;
using System.Text;
using AtmMonitor.Agent.Configuration;
using AtmMonitor.Agent.Devices;
using AtmMonitor.Agent.Storage;
using AtmMonitor.Contracts;
using Microsoft.Extensions.Options;

namespace AtmMonitor.Agent.Logs;

/// <summary>
/// Gathers log entries from device events and from tailed files (e.g. the ATM application's electronic journal).
/// File offsets are persisted so restarts neither lose nor duplicate lines; rotation (file shrinks) restarts from 0.
/// </summary>
public sealed class LogCollector(IDeviceProvider devices, LocalStore store, IOptions<AgentOptions> options, ILogger<LogCollector> logger)
{
    public const int MaxLinesPerSourcePerCycle = 400;
    public const int MaxLineLength = 4000;

    public IReadOnlyList<LogEntryDto> Collect()
    {
        var entries = devices.DrainEvents()
            .Select(e => new LogEntryDto(e.Timestamp, e.Severity, e.Source, e.Message))
            .ToList();

        foreach (var source in options.Value.LogSources)
        {
            try
            {
                entries.AddRange(Tail(source));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Cannot read log source {Source} at {Path}: {Error}", source.Name, source.Path, ex.Message);
            }
        }

        return entries;
    }

    /// <summary>Returns the last <paramref name="lines"/> lines of a configured source (for the CollectLogs command).</summary>
    public static string ReadTail(string path, int lines)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        const int maxBytes = 256 * 1024;
        fs.Seek(Math.Max(0, fs.Length - maxBytes), SeekOrigin.Begin);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        var all = reader.ReadToEnd().Split('\n');
        return string.Join('\n', all.TakeLast(lines));
    }

    private List<LogEntryDto> Tail(LogSourceOptions source)
    {
        if (!File.Exists(source.Path))
        {
            return [];
        }

        var key = $"offset:{source.Name}";
        var offset = long.TryParse(store.Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var o) ? o : -1;

        using var fs = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (offset < 0)
        {
            // First time we see this file: start at the end, historic content is fetched on demand via CollectLogs.
            store.Set(key, fs.Length.ToString(CultureInfo.InvariantCulture));
            return [];
        }

        if (offset > fs.Length)
        {
            offset = 0; // rotated / truncated
        }

        fs.Seek(offset, SeekOrigin.Begin);
        var result = new List<LogEntryDto>();
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var consumed = offset;
        while (result.Count < MaxLinesPerSourcePerCycle && reader.ReadLine() is { } line)
        {
            consumed += Encoding.UTF8.GetByteCount(line) + 1;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var text = line.Length > MaxLineLength ? line[..MaxLineLength] : line;
            result.Add(new LogEntryDto(DateTimeOffset.UtcNow, Classify(text, source), source.Name, text.TrimEnd('\r')));
        }

        store.Set(key, Math.Min(consumed, fs.Length).ToString(CultureInfo.InvariantCulture));
        return result;
    }

    private static LogSeverity Classify(string line, LogSourceOptions s) =>
        s.ErrorKeywords.Any(k => line.Contains(k, StringComparison.OrdinalIgnoreCase)) ? LogSeverity.Error
        : s.WarningKeywords.Any(k => line.Contains(k, StringComparison.OrdinalIgnoreCase)) ? LogSeverity.Warning
        : LogSeverity.Information;
}
