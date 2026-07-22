# Google Play Console — Field-by-Field Checklist (PakkaHisaab)

Facts below are pulled from this repo (package `com.clearkhata.pakkahisaab`, .NET MAUI,
Firebase/AppCenter telemetry, RECORD_AUDIO for Voice-to-Ledger, login-gated app with a
built-in reviewer demo mode). Work through these Play Console sections in order — each
needs a green check before you can roll out even to Internal testing.

## 1. Create app
| Field | Value |
|---|---|
| App name | `PakkaHisaab` |
| Default language | English (US) — 23 locales exist but only en + hi are fully translated |
| App or game | App |
| Free or paid | Decide now — switching from free to paid later requires a new listing |
| Declarations | Check both (Developer Program Policies, US export laws) |

## 2. App access
- Login/registration is required, **but** the login screen has a **"Try Demo" button**
  seeded with demo data — README explicitly says it was "Built for store reviewers."
  Point reviewers to that instead of (or alongside) test credentials.
- If you still want a real test account, declare "All functionality is restricted" and
  supply a username/password for a seeded account.

## 3. Ads
- Answer **No** — no ad SDK found in the codebase (only AppCenter/Firebase, which are
  analytics/crash, not ads).

## 4. Content ratings
- Fill the IARC questionnaire honestly. For a household ledger app with no
  violence/gambling/UGC, expect all sensitive-content questions to be "No."
- Result should land around **PEGI 3 / Everyone**.

## 5. Target audience and content
- Target age group: your real audience (likely 18+, since it's a financial/salary tool
  for household staff management, not aimed at children).
- "Appeal to children" → No.

## 6. News / Government / COVID-19 apps
- Not applicable — answer No to each.

## 7. Data safety
| Data type | Collected? | Shared? | Purpose |
|---|---|---|---|
| Email address | Yes (registration/login) | No | Account management |
| Financial info (ledger, salary, advances) | Yes | No | App functionality |
| Crash logs | Yes | Yes — Firebase & App Center | Analytics/crash reporting |
| Diagnostics | Yes | Yes — Firebase & App Center | Analytics |
| Device/other IDs | Yes | Yes — Firebase & App Center | Analytics |

- Account deletion: **Yes**, supported — README confirms a "Delete-My-Account (server
  hard-delete + local wipe)" flow already exists.

## 8. Financial features declaration
- Declare as **personal finance / expense & payroll tracking for household help** — not
  lending, not crypto, not payments processing (the app only deep-links out to UPI apps
  via `upi://pay` intents; it never handles payment credentials itself).

## 9. Store listing
| Field | Value |
|---|---|
| App name | `PakkaHisaab` |
| Short description (≤80 chars) | `Tap. Track. Settle. — the household khata, digitized.` |
| Full description (≤4000 chars) | Adapt from `README.md` intro: track attendance, advances & salaries for maids/drivers/milkmen; settle via UPI; share PDF statements on WhatsApp |
| App icon (512×512 PNG, 32-bit w/ alpha) | `docs/logo_512.png` — confirm it has an alpha channel |
| Feature graphic (1024×500) | **Missing** — needs to be created, nothing in repo qualifies |
| Phone screenshots (min 2) | **Missing** — capture from a running emulator/device |
| Category | **Finance** |
| Contact email | `amit.agrawal.hyd@outlook.com` |
| Website | `https://amitagrawalhyd.github.io/PakkaHisaab/` (per `Constants.cs`, `pakkahisaab.app` isn't live/registered yet) |
| Privacy policy URL | `https://amitagrawalhyd.github.io/PakkaHisaab/privacy/` |

## 10. Permissions declaration (sensitive permissions form)
- **RECORD_AUDIO**: justify as core functionality — describe the Voice-to-Ledger feature.
- **SCHEDULE_EXACT_ALARM**: justify as needed for the daily 17:00 attendance prompt and
  salary-due alerts (README: "Notifications" section).

## 11. App integrity (signing)
- Opt into **Play App Signing** — upload `pakkahisaab-upload.keystore` (per
  `docs/DEPLOYMENT.md`) as the upload key; Google holds the app signing key.

## 12. Testing track
- Testing → Internal testing → Create new release → upload the `.aab`.
- Release name: `1.0.0 (1)`.
- Add testers (your email or a Google Group).
- Release notes — see section 13 below.

## 13. Release notes ("What's new")
This is v1.0.0, so write an intro, not a changelog of internal CI/build fixes.

**Internal testing** (testers only, functional tone):
```
Initial internal build 1.0.0 (1) — testing attendance/salary tracking, UPI settlement,
Voice-to-Ledger, PDF statements, and account sync.
```

**Production** (public-facing, ~290 chars, well under the 500-char limit):
```
Welcome to PakkaHisaab — the household khata, digitized.

• Track attendance, advances & salaries for your maid, driver, milkman & more
• Settle instantly over UPI
• Share PDF statements on WhatsApp
• Add entries by voice
• Available in English, Hindi & 21 more languages
```
Add a Hindi version too, since that's your other fully-translated locale.

## 14. Pricing & distribution
- Countries: at minimum India (UPI/rupee-centric use case).
- Confirm **Contains ads: No**, **In-app purchases: No** (no billing SDK found).

---
### Still needed before Production (not required for Internal testing)
- [ ] Feature graphic (1024×500)
- [ ] Real phone screenshots (min 2)
- [ ] Confirm `docs/logo_512.png` has an alpha channel and is exactly 512×512
