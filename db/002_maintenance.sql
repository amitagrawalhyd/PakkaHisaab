/* ============================================================================
   PakkaHisaab — maintenance objects
   ============================================================================ */
USE PakkaHisaab;
GO

/* Purge idempotency records older than 30 days (safe: retries never arrive that late).
   Schedule via SQL Agent or an Azure Elastic Job — e.g. daily at 03:00 UTC. */
CREATE OR ALTER PROCEDURE dbo.PurgeOldSyncBatches
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.SyncBatches
    WHERE ProcessedAtUtc < DATEADD(DAY, -30, SYSUTCDATETIME());
END
GO

/* Hard-erase one user and every piece of their data (backs the DELETE /account endpoint;
   also usable directly by support staff for GDPR/DPDP erasure requests). */
CREATE OR ALTER PROCEDURE dbo.EraseUserData
    @UserId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
        DELETE FROM dbo.SyncBatches    WHERE UserId = @UserId;
        DELETE FROM dbo.Settlements    WHERE UserId = @UserId;
        DELETE FROM dbo.LedgerEntries  WHERE UserId = @UserId;
        DELETE FROM dbo.Attendance     WHERE UserId = @UserId;
        DELETE FROM dbo.Helpers        WHERE UserId = @UserId;
        DELETE FROM dbo.Users          WHERE Id     = @UserId;
    COMMIT TRANSACTION;
END
GO

/* Monthly settlement view — handy for support queries and BI dashboards. */
CREATE OR ALTER VIEW dbo.vw_MonthlyLedger
AS
SELECT
    h.UserId,
    h.Id            AS HelperId,
    h.Name          AS HelperName,
    l.Period,
    SUM(CASE WHEN l.[Type] = 0 THEN l.Amount ELSE 0 END) AS Advances,
    SUM(CASE WHEN l.[Type] = 1 THEN l.Amount ELSE 0 END) AS SalaryPaid,
    SUM(CASE WHEN l.[Type] = 2 THEN l.Amount ELSE 0 END) AS Bonuses,
    SUM(CASE WHEN l.[Type] = 3 THEN l.Amount ELSE 0 END) AS Deductions
FROM dbo.Helpers h
JOIN dbo.LedgerEntries l
    ON l.HelperId = h.Id AND l.IsDeleted = 0
WHERE h.IsDeleted = 0
GROUP BY h.UserId, h.Id, h.Name, l.Period;
GO
