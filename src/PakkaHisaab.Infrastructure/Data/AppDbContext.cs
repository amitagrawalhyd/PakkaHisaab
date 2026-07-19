using Microsoft.EntityFrameworkCore;

namespace PakkaHisaab.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Helper> Helpers => Set<Helper>();
    public DbSet<AttendanceEntry> Attendance => Set<AttendanceEntry>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<SyncBatch> SyncBatches => Set<SyncBatch>();
    public DbSet<RowVersionTicket> RowVersionTickets => Set<RowVersionTicket>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<User>().HasIndex(u => u.Email).IsUnique();

        // Global change-counter sequence backing RowVersion (the sync watermark).
        // SQL Server only — SQLite deployments use the RowVersionTickets fallback table.
        if (Database.IsSqlServer())
            mb.HasSequence<long>("RowVersionSeq", schema: "dbo").StartsAt(1).IncrementsBy(1);

        mb.Entity<RowVersionTicket>().Property(x => x.Id).ValueGeneratedOnAdd();

        mb.Entity<Helper>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.RowVersion });
            e.HasIndex(x => new { x.UserId, x.IsDeleted });
        });

        mb.Entity<AttendanceEntry>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.RowVersion });
            e.HasIndex(x => new { x.HelperId, x.Date }).IsUnique();
        });

        mb.Entity<LedgerEntry>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.RowVersion });
            e.HasIndex(x => new { x.HelperId, x.Period });
        });

        mb.Entity<Settlement>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.RowVersion });
            e.HasIndex(x => new { x.HelperId, x.Period }).IsUnique();
        });

        mb.Entity<SyncBatch>().HasIndex(x => new { x.UserId, x.ProcessedAtUtc });
    }

    /// <summary>Next value of the global row-version counter (provider-aware).</summary>
    public async Task<long> NextRowVersionAsync(CancellationToken ct = default)
    {
        if (Database.IsSqlServer())
        {
            // Single round-trip; works on SQL Server 2016+ / Azure SQL.
            var result = await Database
                .SqlQueryRaw<long>("SELECT NEXT VALUE FOR dbo.RowVersionSeq AS [Value]")
                .ToListAsync(ct);
            return result[0];
        }

        // SQLite (free/self-host mode): auto-increment ticket table.
        // Rows are never deleted so ids stay strictly monotonic (8 bytes per sync write).
        var ticket = new RowVersionTicket();
        RowVersionTickets.Add(ticket);
        await SaveChangesAsync(ct);
        return ticket.Id;
    }
}
