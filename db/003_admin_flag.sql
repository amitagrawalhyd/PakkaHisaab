/* ============================================================================
   PakkaHisaab (ClearKhata) — migration 003: admin flag
   Adds dbo.Users.IsAdmin, consumed by PakkaHisaab.Admin's cookie login (only
   users with IsAdmin = 1 may sign into the dashboard). Safe to re-run.
   Apply to any environment provisioned before this column existed; a fresh
   001_schema.sql already includes it.
   ============================================================================ */
USE PakkaHisaab;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = 'IsAdmin'
)
    ALTER TABLE dbo.Users ADD IsAdmin BIT NOT NULL CONSTRAINT DF_Users_IsAdmin DEFAULT 0;
GO

/* Promote the first admin (run once, replace the email):
   UPDATE dbo.Users SET IsAdmin = 1 WHERE Email = 'you@example.com'; */
