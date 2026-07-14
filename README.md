# PakkaHisaab (ClearKhata)

**Tap. Track. Settle.** — the household khata, digitized. Track attendance, advances and
salaries for maids, drivers, milkmen and every other household helper, then settle in
seconds over UPI and share PDF statements on WhatsApp.

![logo](docs/logo_512.png)

## Solution layout

```
PakkaHisaab.sln
├── src/PakkaHisaab.Shared     DTOs, enums, sync contracts, SalaryCalculator,
│                              VoiceLedgerParser, LeaveForecaster — one engine,
│                              referenced by BOTH the MAUI client and the API (DRY).
├── src/PakkaHisaab.Maui       .NET MAUI (Android + iOS) client. Strict MVVM via
│                              CommunityToolkit.Mvvm; offline-first SQLite; Shiny
│                              background sync; QuestPDF reports; 23-language runtime i18n.
├── src/PakkaHisaab.Api        .NET 8 minimal-API microservice: JWT auth, idempotent
│                              /sync push-pull, account deletion, AI parse/forecast.
├── db/                        SQL Server DDL + maintenance procedures.
├── docs/DEPLOYMENT.md         Azure, keystore/AAB, iOS provisioning/IPA, store checklists.
└── tools/                     get_fonts.sh (font assets), gen_resx.py + translations.json.
```

## Architecture at a glance

- **Offline-first:** every tap writes to local SQLite first (zero-latency UI). Rows carry
  `IsDirty` (outbox), `ModifiedAtUtc` (last-writer-wins) and `RowVersion` (pull watermark).
- **Background sync:** a `Shiny.Jobs` worker drains the outbox to `POST /sync/push` —
  idempotent via `ClientBatchId`, so retries after lost responses never double-apply —
  and pulls server deltas since the stored watermark. Demo mode hard-suspends the job.
- **Salary engine:** `(Monthly Wage) − (Unpaid Absences × daily rate) − (Advances) = Final
  Payable`, plus per-unit (milk) wage models, leave allowances and carry-over — implemented
  once in `PakkaHisaab.Shared.Domain.SalaryCalculator` and used by the app, the API and the
  PDF reports.
- **Demo mode:** the login screen's **Try Demo** button opens an isolated
  `Demo_PakkaHisaab.db` seeded with Geeta (₹5,000/mo house help, 2 absences, ₹500 advance
  → payable ₹4,500 with her 2-day allowance) and Raju (milkman, 1.5 L/day) — no account,
  no network, sync disabled. Built for store reviewers.
- **Notifications:** `Plugin.LocalNotification` — daily 17:00 attendance prompt with an
  inline *Absent* action, salary alerts on the 1st–10th that stop the moment you mark paid.
- **UPI:** NPCI `upi://pay` deep links launched with `Launcher.Default.OpenAsync`; grid of
  known apps, editable amount, cash fallback.
- **i18n:** 23 languages as `.resx` satellites, switched at runtime through a bound
  `LocalizationResourceManager` indexer (instant refresh, RTL flip for Arabic/Urdu).
  English + Hindi ship fully translated; the other 21 ship core strings and fall back to
  English per key — complete them in `tools/translations.json` and re-run `gen_resx.py`.
- **Compliance:** Delete-My-Account (server hard-delete + local wipe), AppCenter
  analytics/crashes, root/jailbreak warning, privacy/TOS WebViews.

## Getting started

```bash
bash tools/get_fonts.sh                 # one-time: Poppins + Material Symbols
dotnet workload install maui
dotnet build src/PakkaHisaab.Api        # backend
dotnet build src/PakkaHisaab.Maui -f net9.0-android   # app
```

Run the API (`dotnet run --project src/PakkaHisaab.Api`), create the DB with
`db/001_schema.sql`, and point `Constants.ApiBaseUrl` at it. Or skip all of that and press
**Try Demo**. Deployment: see `docs/DEPLOYMENT.md`.
