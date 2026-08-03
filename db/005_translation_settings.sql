/* ============================================================================
   PakkaHisaab (ClearKhata) — migration 005: translation settings
   Adds dbo.TranslationSettings, a single-row on/off + provider switch for the
   English-translation feature (migration 004). Off by default — enabling it
   is an Admin-console action (Settings page), not a deploy-time decision, so
   it can be turned on/off without a redeploy or app-setting change.
   Safe to re-run. A fresh 001_schema.sql already includes this table.
   ============================================================================ */
USE PakkaHisaab;
GO

IF OBJECT_ID(N'dbo.TranslationSettings') IS NULL
CREATE TABLE dbo.TranslationSettings
(
    Id          INT             NOT NULL CONSTRAINT PK_TranslationSettings PRIMARY KEY CHECK (Id = 1),
    Enabled     BIT             NOT NULL CONSTRAINT DF_TranslationSettings_Enabled DEFAULT 0,
    Provider    NVARCHAR(20)    NOT NULL CONSTRAINT DF_TranslationSettings_Provider DEFAULT N'GoogleFree'
);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TranslationSettings WHERE Id = 1)
    INSERT INTO dbo.TranslationSettings (Id, Enabled, Provider) VALUES (1, 0, N'GoogleFree');
GO
