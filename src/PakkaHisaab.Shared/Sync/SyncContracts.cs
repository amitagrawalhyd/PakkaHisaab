using PakkaHisaab.Shared.Dtos;

namespace PakkaHisaab.Shared.Sync;

/// <summary>
/// Delta-push payload. <see cref="ClientBatchId"/> makes the call idempotent: the server remembers
/// processed batch ids per user and replays the stored result if the same batch is pushed twice
/// (e.g., the response was lost on a flaky network and Shiny retries the job).
/// </summary>
public class SyncPushRequest
{
    public Guid ClientBatchId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public List<HelperDto> Helpers { get; set; } = new();
    public List<AttendanceDto> Attendance { get; set; } = new();
    public List<LedgerEntryDto> LedgerEntries { get; set; } = new();
    public List<SettlementDto> Settlements { get; set; } = new();
}

public class SyncPushResponse
{
    public Guid ClientBatchId { get; set; }
    public bool AlreadyProcessed { get; set; }
    /// <summary>Server-assigned row versions keyed by entity id — client stores these locally.</summary>
    public Dictionary<Guid, long> AcceptedRowVersions { get; set; } = new();
    /// <summary>Records the server rejected because a newer server copy exists (client should pull).</summary>
    public List<Guid> Conflicts { get; set; } = new();
    public long ServerWatermark { get; set; }
}

/// <summary>Incremental pull: "give me everything my user changed after this watermark".</summary>
public class SyncPullRequest
{
    public long SinceWatermark { get; set; }
    public string DeviceId { get; set; } = string.Empty;
}

public class SyncPullResponse
{
    public long NewWatermark { get; set; }
    public List<HelperDto> Helpers { get; set; } = new();
    public List<AttendanceDto> Attendance { get; set; } = new();
    public List<LedgerEntryDto> LedgerEntries { get; set; } = new();
    public List<SettlementDto> Settlements { get; set; } = new();
}
