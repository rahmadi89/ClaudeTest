using AtmMonitor.Contracts;

namespace AtmMonitor.Domain.Entities;

public class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AtmId { get; set; }
    public Atm? Atm { get; set; }
    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }
    public AlertStatus Status { get; set; } = AlertStatus.Open;

    /// <summary>Identifies "the same problem" so repeated detections update one alert instead of creating many.</summary>
    public required string DedupKey { get; set; }
    public required string Message { get; set; }
    public DateTimeOffset RaisedAt { get; set; }
    public DateTimeOffset LastOccurredAt { get; set; }
    public int OccurrenceCount { get; set; } = 1;
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public string? Note { get; set; }

    public bool IsActive => Status != AlertStatus.Resolved;

    public bool Acknowledge(string user, string? note, DateTimeOffset now)
    {
        if (Status != AlertStatus.Open)
        {
            return false;
        }

        Status = AlertStatus.Acknowledged;
        AcknowledgedAt = now;
        AcknowledgedBy = user;
        Note = note ?? Note;
        return true;
    }

    public bool Resolve(string user, string? note, DateTimeOffset now)
    {
        if (Status == AlertStatus.Resolved)
        {
            return false;
        }

        Status = AlertStatus.Resolved;
        ResolvedAt = now;
        ResolvedBy = user;
        Note = note ?? Note;
        return true;
    }
}
