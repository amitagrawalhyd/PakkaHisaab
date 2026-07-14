namespace PakkaHisaab.Shared.Sync;

/// <summary>
/// Contract implemented by every replicated record on both client (SQLite) and server (SQL Server).
/// GUID keys are generated client-side so offline inserts never collide; <see cref="ModifiedAtUtc"/>
/// drives last-writer-wins conflict resolution and <see cref="RowVersion"/> is the server-issued
/// monotonic change counter used as the incremental pull watermark.
/// </summary>
public interface ISyncEntity
{
    Guid Id { get; set; }
    DateTime ModifiedAtUtc { get; set; }
    long RowVersion { get; set; }
    bool IsDeleted { get; set; }
}
