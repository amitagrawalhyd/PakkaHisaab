# PakkaHisaab — Store Listing Copy

Every claim below is checked against the current codebase (see **Facts checked against**
at the bottom). Nothing here describes a planned or aspirational feature — if it's
written here, it ships in the app today. Character counts are verified, not estimated.

---

## Google Play Console

**Short description** (limit 80 chars — used: 53)
```
Tap. Track. Settle. — the household khata, digitized.
```

**Full description** (limit 4000 chars — used: 2952)
```
PakkaHisaab — Tap. Track. Settle.

The digital khata for every household that pays a maid, cook, driver, gardener, milkman, or any other domestic help. Stop reconstructing "how much do I owe them this month" from memory or a torn notebook page — PakkaHisaab keeps one running ledger per helper, right on your phone, updated the moment something happens.

WHAT YOU CAN DO

Track attendance in one tap
Mark Present, Absent, or Half-Day for each helper, every day. Helpers paid per delivery (like your milkman) get a simple "units delivered" entry instead.

Automatic salary calculation
Set a monthly wage — or a rate per delivered unit — a monthly allowed-absence quota, and optional leave carry-over. Your payable amount recalculates instantly as you log attendance, advances, deductions, or bonuses, so there's never a separate month-end calculation to do by hand.

Log advances, deductions & bonuses as they happen
An advance given today shows up in the running ledger today, not as a surprise at settlement time.

Settle in seconds over UPI
Tap Settle to see the computed breakdown, then pay with your preferred UPI app via a pre-filled payment link, or simply log a cash payment. Either way, it's recorded in the ledger automatically.

Add entries by voice
Say things like "Gave Geeta 500 advance" or "Raju delivered 1.5 litres" and PakkaHisaab logs it for you — no forms to fill. Works fully offline, in English and Hindi (romanized).

Never forget a day or a payment
A daily reminder at 5 PM asks about today's attendance, with a one-tap "Mark Absent" action right in the notification. Salary-due alerts fire automatically at the start of each month, then cancel themselves the moment you record payment.

See patterns before they surprise you
Once enough attendance history exists, PakkaHisaab shows a helper's typical absence pattern (e.g. "Usually absent on Mondays") right on their dashboard card.

Share printable statements
Generate a PDF ledger for one helper, or a summary across all of them, and share it straight to WhatsApp.

Works without the internet
Every entry saves to your phone instantly. If you're online, it syncs quietly in the background — no connection, no problem, and nothing you log is ever lost.

Try it instantly, risk-free
Tap "Try Demo" on the sign-in screen for a fully working, offline sample setup — no account, no network, and no risk to your real data — so you can explore every feature before deciding to sign up.

Your data, your control
Delete your account and every record tied to it, permanently, any time, from within the app.

Built for households managing domestic help of every kind — house help, cooks, drivers, gardeners, milkmen, newspaper delivery, tutors, cleaners and more — PakkaHisaab replaces the notebook, the memory, and the month-end argument with one clear number both sides can trust.

Available in English and Hindi with full translation, plus partial support for 21 additional languages.
```

**Category:** Finance
**Contact email:** amit.agrawal.hyd@outlook.com
**Website:** https://amitagrawalhyd.github.io/PakkaHisaab/
**Privacy policy:** https://amitagrawalhyd.github.io/PakkaHisaab/privacy/

*(Category, contact, website and privacy URL repeated here from `docs/PLAY_STORE_CHECKLIST.md` — that file remains the source of record for the rest of the Play Console field-by-field setup, including Data Safety, permissions declarations and release notes.)*

---

## Apple App Store

**Subtitle** (limit 30 chars — used: 27)
```
Attendance, Salary, UPI Pay
```

**Promotional text** (limit 170 chars — used: 154; editable anytime without a new review)
```
Track attendance, advances & salary for your maid, driver, cook, milkman & more. Settle instantly via UPI, share PDF statements, and log entries by voice.
```

**Description** (limit 4000 chars)
Same copy as the Google Play full description above — Apple has no separate short/long
split, so the full description doubles as the App Store description.

**Keywords** (limit 100 chars, comma-separated, no spaces — used: 92)
```
salary,attendance,khata,ledger,maid,driver,milkman,cook,UPI,advance,payroll,helper,household
```

**Category:** Finance

---

## Facts checked against

| Claim | Source |
|---|---|
| Tagline "Tap. Track. Settle." | `AppStrings.resx` → `App_Tagline` |
| Attendance: Present/Absent/Half-Day, per-unit delivery entry | `CalendarViewModel.cs` |
| Salary formula, allowed-absence quota, leave carry-over, per-unit wage | `SalaryCalculator.cs` |
| Advance/deduction/bonus ledger | `LedgerViewModel.cs` |
| UPI settlement via pre-filled deep link + cash fallback | `SettlementViewModel.cs`, `UpiService.cs` |
| Voice-to-Ledger, English + romanized Hindi, fully offline | `VoiceLedgerParser.cs`, `VoiceLedgerService.cs` |
| Daily 5 PM reminder with inline "Mark Absent" action | `NotificationService.cs` |
| Salary-due alerts (1st–10th), auto-cancel on payment | `NotificationService.cs`, `DataService.MarkPaidAsync` |
| Absence pattern forecast | `ForecastService.cs`, `LeaveForecaster.cs` |
| PDF ledger + household summary reports, WhatsApp share | `ReportsViewModel.cs` |
| Offline-first SQLite + background sync | `README.md` architecture section |
| "Try Demo" — offline, no account, no network | `AuthService.StartDemoAsync`, `README.md` |
| Account deletion — server hard-delete + local wipe | `AccountEndpoints.cs`, `AuthService.DeleteAccountAsync` |
| Helper categories (house help, cook, driver, gardener, milkman, newspaper, tutor, cleaner) | `AppStrings.resx` → `Category_*` |
| English + Hindi fully translated, 21 languages partial/core-only | `README.md` i18n section |
| Category: Finance; contact email; website; privacy URL | `docs/PLAY_STORE_CHECKLIST.md` |

No mention of vendors, societies, KYC, RBAC, or any capability outside the table above —
consistent with the corrected `docs/PakkaHisaab_Brochure.docx`.
