using AtmMonitor.Contracts;
using AtmMonitor.Domain.Entities;

namespace AtmMonitor.Api.Models;

public static class Mapping
{
    public static AtmDetail ToDetail(this Atm a) => new(
        a.Id, a.TerminalId, a.Name, a.Branch, a.Address, a.City, a.Latitude, a.Longitude, a.Vendor, a.Model, a.SerialNumber,
        a.IsEnabled, a.AgentKeyHash != null, a.IsConnected, a.CreatedAt, a.EnrolledAt,
        a.Status, a.Mode, a.LastSeenAt, a.LastReportAt, a.StatusChangedAt, a.AgentVersion, a.MachineName, a.OsDescription,
        a.Components.OrderBy(c => c.Type).Select(c => new ComponentView(c.Type, c.State, c.ErrorCode, c.Description, c.UpdatedAt, c.StateChangedAt)).ToList(),
        a.Cassettes.OrderBy(c => c.CassetteId).Select(c => c.ToView()).ToList(),
        a.Network, a.System, a.AvailableCash());

    public static CassetteView ToView(this Cassette c) =>
        new(c.CassetteId, c.Type, c.Currency, c.Denomination, c.Count, c.Capacity, c.Status, c.FillPercent, c.Denomination * c.Count);

    public static CommandView ToView(this AtmCommand c, string? terminalId = null) => new(
        c.Id, c.AtmId, terminalId ?? c.Atm?.TerminalId, c.Type, c.Parameters, c.Status, c.RequestedBy, c.Reason,
        c.CreatedAt, c.SentAt, c.CompletedAt, c.ExpiresAt, c.Output, c.Error);

    public static AlertView ToView(this Alert a) => new(
        a.Id, a.AtmId, a.Atm?.TerminalId, a.Atm?.Name, a.Type, a.Severity, a.Status, a.Message, a.RaisedAt, a.LastOccurredAt,
        a.OccurrenceCount, a.AcknowledgedAt, a.AcknowledgedBy, a.ResolvedAt, a.ResolvedBy, a.Note);

    public static UserInfo ToInfo(this User u) => new(u.Id, u.UserName, u.DisplayName, u.Role);

    public static UserView ToView(this User u, DateTimeOffset now) =>
        new(u.Id, u.UserName, u.DisplayName, u.Role, u.IsActive, u.CreatedAt, u.LastLoginAt, u.LockoutEndsAt > now);

    public static EnrollmentTokenView ToView(this EnrollmentToken t) =>
        new(t.Id, t.Description, t.CreatedAt, t.CreatedBy, t.ExpiresAt, t.MaxUses, t.UseCount, t.RevokedAt != null);

    public static bool IsLowOrEmpty(this Cassette c) =>
        c.Type is CassetteType.Dispense or CassetteType.Recycle && c.Status is CassetteStatus.Low or CassetteStatus.Empty;
}
