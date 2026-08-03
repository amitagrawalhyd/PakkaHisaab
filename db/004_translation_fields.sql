/* ============================================================================
   PakkaHisaab (ClearKhata) — migration 004: English-translation fields
   Adds dbo.Users.DisplayNameEnglish, dbo.Helpers.NameEnglish and
   dbo.LedgerEntries.NoteEnglish — machine-translated copies of the free-text
   fields users can enter in any language, populated server-side by the API
   (see PakkaHisaab.Api.Services.ITranslationService) whenever the source text
   changes. PakkaHisaab.Admin displays these instead of the raw value so every
   admin page reads in English regardless of what language the field was
   entered in, falling back to the original when a translation isn't available
   yet (e.g. GoogleTranslate:ApiKey not configured, or the call failed).
   Safe to re-run. A fresh 001_schema.sql already includes these columns.
   ============================================================================ */
USE PakkaHisaab;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = 'DisplayNameEnglish'
)
    ALTER TABLE dbo.Users ADD DisplayNameEnglish NVARCHAR(128) NULL;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Helpers') AND name = 'NameEnglish'
)
    ALTER TABLE dbo.Helpers ADD NameEnglish NVARCHAR(128) NULL;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.LedgerEntries') AND name = 'NoteEnglish'
)
    ALTER TABLE dbo.LedgerEntries ADD NoteEnglish NVARCHAR(512) NULL;
GO

/* Existing rows are left NULL (Admin falls back to the original text) — they
   backfill lazily the next time each row's Name/Note/DisplayName is synced
   and detected as changed. There is no bulk backfill here on purpose: it
   would call the translation API for every existing row in one burst, which
   costs money and can hit rate limits with no user waiting on the result. */
