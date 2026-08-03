/* ============================================================================
   PakkaHisaab (ClearKhata) — SQL Server schema
   Target: SQL Server 2019+ / Azure SQL Database
   Mirrors the EF Core model (src/PakkaHisaab.Api/Data). Run 001 then 002.
   ============================================================================ */

IF DB_ID(N'PakkaHisaab') IS NULL
    CREATE DATABASE PakkaHisaab;
GO
USE PakkaHisaab;
GO

/* Global monotonic change counter — the sync watermark. */
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'RowVersionSeq')
    CREATE SEQUENCE dbo.RowVersionSeq AS BIGINT START WITH 1 INCREMENT BY 1;
GO

/* ---------------------------------------------------------------- Users -- */
IF OBJECT_ID(N'dbo.Users') IS NULL
CREATE TABLE dbo.Users
(
    Id                          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Users PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    Email                       NVARCHAR(256)    NOT NULL,
    DisplayName                 NVARCHAR(128)    NOT NULL,
    PhoneNumber                 NVARCHAR(32)     NULL,
    PasswordHash                NVARCHAR(MAX)    NOT NULL,
    CreatedAtUtc                DATETIME2(3)     NOT NULL CONSTRAINT DF_Users_Created DEFAULT SYSUTCDATETIME(),
    RefreshToken                NVARCHAR(512)    NULL,
    RefreshTokenExpiresAtUtc    DATETIME2(3)     NULL,
    IsAdmin                     BIT              NOT NULL CONSTRAINT DF_Users_IsAdmin DEFAULT 0
);
GO
CREATE UNIQUE INDEX IX_Users_Email ON dbo.Users (Email);
GO

/* -------------------------------------------------------------- Helpers -- */
IF OBJECT_ID(N'dbo.Helpers') IS NULL
CREATE TABLE dbo.Helpers
(
    Id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Helpers PRIMARY KEY,  -- client-generated GUID
    UserId                  UNIQUEIDENTIFIER NOT NULL,
    Name                    NVARCHAR(128)    NOT NULL,
    WhatsAppNumber          NVARCHAR(32)     NOT NULL CONSTRAINT DF_Helpers_WA DEFAULT N'',
    UpiId                   NVARCHAR(128)    NULL,
    Category                INT              NOT NULL,   -- HelperCategory enum
    WageType                INT              NOT NULL,   -- 0 monthly, 1 per-unit
    MonthlyWage             DECIMAL(12,2)    NOT NULL CONSTRAINT DF_Helpers_Wage DEFAULT 0,
    RatePerUnit             DECIMAL(10,2)    NOT NULL CONSTRAINT DF_Helpers_Rate DEFAULT 0,
    UnitLabel               NVARCHAR(16)     NOT NULL CONSTRAINT DF_Helpers_Unit DEFAULT N'L',
    MonthlyAllowedAbsences  INT              NOT NULL CONSTRAINT DF_Helpers_Allowed DEFAULT 0,
    CarryOverLeaveAllowed   BIT              NOT NULL CONSTRAINT DF_Helpers_Carry DEFAULT 0,
    CarriedOverLeaves       INT              NOT NULL CONSTRAINT DF_Helpers_Carried DEFAULT 0,
    IsActive                BIT              NOT NULL CONSTRAINT DF_Helpers_Active DEFAULT 1,
    ModifiedAtUtc           DATETIME2(3)     NOT NULL,   -- last-writer-wins conflict resolution
    RowVersion              BIGINT           NOT NULL,   -- from dbo.RowVersionSeq; pull watermark
    IsDeleted               BIT              NOT NULL CONSTRAINT DF_Helpers_Deleted DEFAULT 0,  -- tombstone
    CONSTRAINT FK_Helpers_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id)
);
GO
CREATE INDEX IX_Helpers_User_RowVersion ON dbo.Helpers (UserId, RowVersion);
CREATE INDEX IX_Helpers_User_Deleted    ON dbo.Helpers (UserId, IsDeleted);
GO

/* ----------------------------------------------------------- Attendance -- */
IF OBJECT_ID(N'dbo.Attendance') IS NULL
CREATE TABLE dbo.Attendance
(
    Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Attendance PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER NOT NULL,
    HelperId        UNIQUEIDENTIFIER NOT NULL,
    [Date]          CHAR(10)         NOT NULL,   -- yyyy-MM-dd (time-zone independent)
    [Status]        INT              NOT NULL,   -- 0 Present, 1 Absent, 2 HalfDay
    UnitsDelivered  DECIMAL(10,2)    NOT NULL CONSTRAINT DF_Att_Units DEFAULT 0,
    ModifiedAtUtc   DATETIME2(3)     NOT NULL,
    RowVersion      BIGINT           NOT NULL,
    IsDeleted       BIT              NOT NULL CONSTRAINT DF_Att_Deleted DEFAULT 0,
    CONSTRAINT FK_Attendance_Users   FOREIGN KEY (UserId)   REFERENCES dbo.Users (Id),
    CONSTRAINT FK_Attendance_Helpers FOREIGN KEY (HelperId) REFERENCES dbo.Helpers (Id)
);
GO
CREATE INDEX        IX_Attendance_User_RowVersion ON dbo.Attendance (UserId, RowVersion);
CREATE UNIQUE INDEX IX_Attendance_Helper_Date     ON dbo.Attendance (HelperId, [Date]);
GO

/* -------------------------------------------------------- LedgerEntries -- */
IF OBJECT_ID(N'dbo.LedgerEntries') IS NULL
CREATE TABLE dbo.LedgerEntries
(
    Id                  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_LedgerEntries PRIMARY KEY,
    UserId              UNIQUEIDENTIFIER NOT NULL,
    HelperId            UNIQUEIDENTIFIER NOT NULL,
    [Type]              INT              NOT NULL,   -- 0 Advance, 1 SalaryPayment, 2 Bonus, 3 Deduction, 4 DeliveryCharge
    Amount              DECIMAL(12,2)    NOT NULL,
    Method              INT              NOT NULL,   -- 0 UPI, 1 Cash, 2 Bank
    Note                NVARCHAR(512)    NULL,
    Period              CHAR(7)          NOT NULL,   -- yyyy-MM settlement bucket
    OccurredAtUtc       DATETIME2(3)     NOT NULL,
    UpiTransactionRef   NVARCHAR(64)     NULL,
    ModifiedAtUtc       DATETIME2(3)     NOT NULL,
    RowVersion          BIGINT           NOT NULL,
    IsDeleted           BIT              NOT NULL CONSTRAINT DF_Ledger_Deleted DEFAULT 0,
    CONSTRAINT FK_Ledger_Users   FOREIGN KEY (UserId)   REFERENCES dbo.Users (Id),
    CONSTRAINT FK_Ledger_Helpers FOREIGN KEY (HelperId) REFERENCES dbo.Helpers (Id)
);
GO
CREATE INDEX IX_Ledger_User_RowVersion ON dbo.LedgerEntries (UserId, RowVersion);
CREATE INDEX IX_Ledger_Helper_Period   ON dbo.LedgerEntries (HelperId, Period);
GO

/* ---------------------------------------------------------- Settlements -- */
IF OBJECT_ID(N'dbo.Settlements') IS NULL
CREATE TABLE dbo.Settlements
(
    Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Settlements PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER NOT NULL,
    HelperId        UNIQUEIDENTIFIER NOT NULL,
    Period          CHAR(7)          NOT NULL,
    [Status]        INT              NOT NULL,   -- 0 Pending, 1 Paid
    FinalPayable    DECIMAL(12,2)    NOT NULL,
    PaidAtUtc       DATETIME2(3)     NULL,
    ModifiedAtUtc   DATETIME2(3)     NOT NULL,
    RowVersion      BIGINT           NOT NULL,
    IsDeleted       BIT              NOT NULL CONSTRAINT DF_Settle_Deleted DEFAULT 0,
    CONSTRAINT FK_Settle_Users   FOREIGN KEY (UserId)   REFERENCES dbo.Users (Id),
    CONSTRAINT FK_Settle_Helpers FOREIGN KEY (HelperId) REFERENCES dbo.Helpers (Id)
);
GO
CREATE INDEX        IX_Settle_User_RowVersion ON dbo.Settlements (UserId, RowVersion);
CREATE UNIQUE INDEX IX_Settle_Helper_Period   ON dbo.Settlements (HelperId, Period);
GO

/* ---------------------------------------------- SyncBatches (idempotency) */
IF OBJECT_ID(N'dbo.SyncBatches') IS NULL
CREATE TABLE dbo.SyncBatches
(
    ClientBatchId   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_SyncBatches PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER NOT NULL,
    DeviceId        NVARCHAR(64)     NOT NULL,
    ProcessedAtUtc  DATETIME2(3)     NOT NULL CONSTRAINT DF_Batch_Processed DEFAULT SYSUTCDATETIME(),
    ResponseJson    NVARCHAR(MAX)    NOT NULL,
    CONSTRAINT FK_Batch_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id)
);
GO
CREATE INDEX IX_Batch_User_Processed ON dbo.SyncBatches (UserId, ProcessedAtUtc);
GO
