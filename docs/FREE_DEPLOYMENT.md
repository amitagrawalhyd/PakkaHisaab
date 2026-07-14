# PakkaHisaab — 100% Free Deployment Strategy (₹0 / month)

Every component below runs on a permanent free tier — not a trial, not a credit that
expires. The whole path from `git clone` to an installable app with a live synced
backend costs nothing.

## The stack

| Layer | Service | Free allowance | Cost |
|---|---|---|---|
| API | **Azure App Service — F1 plan** | 60 CPU-min/day, 1 GB storage, 165 MB/day egress | ₹0 forever |
| Database | **Azure SQL Database — free offer** | 100,000 vCore-seconds/mo, 32 GB, serverless w/ auto-pause | ₹0 forever |
| CI/CD | **GitHub Actions** | Unlimited minutes on public repos (2,000/mo private) | ₹0 |
| App distribution (Android) | **Firebase App Distribution** (+ GitHub Releases APK as a fallback) | Unlimited testers | ₹0 |
| Crash reporting | **Sentry free tier** (optional) | 5K errors/mo | ₹0 |
| Monitoring | **App Service logs + /health checks** | Built in | ₹0 |

Why this stack: the Azure SQL free offer means **zero code changes** — the API keeps its
SQL Server provider, sequence-based sync watermark and the `db/*.sql` scripts. Every
other free host (Render, Fly, Koyeb…) would force a database swap, and their free
Postgres tiers now expire after 30 days anyway.

Two escape hatches are built into the code if you ever want off Azure:

- `Database__Provider=Sqlite` — the API runs entirely on a local SQLite file
  (`deploy/Dockerfile` uses this; perfect for a home server or a free Oracle Cloud VM).
- `Database__AutoCreate=true` — the schema creates itself on first boot, so no `sqlcmd`
  step is ever required.

---

## Part 1 — Deploy the backend (one command, ~4 minutes)

Prereqs: a free Azure account ([azure.microsoft.com/free](https://azure.microsoft.com/free) —
no charge; the free offers below also work on any existing subscription) and the Azure CLI.

```bash
az login
bash deploy/azure_free_deploy.sh myname       # any short unique suffix
```

The script provisions everything, deploys the API, then **runs the 10-step end-to-end
smoke test automatically** — health, register, login, sync push, an idempotent-replay
check, incremental pull, the AI parser, and account deletion. If you see
`ALL 10 CHECKS PASSED`, your backend is verifiably working end to end.

It prints your API URL, e.g. `https://api-pakkahisaab-myname.azurewebsites.net`.
Save the SQL password it prints.

Re-verify any time:

```bash
bash deploy/smoke_test.sh https://api-pakkahisaab-myname.azurewebsites.net
```

### Free-tier realities (and why they don't hurt)

- **Cold starts.** F1 has no Always-On; after ~20 idle minutes the first request takes
  10–30 s. The app is offline-first — users never wait on the API; only the silent Shiny
  background sync feels this, and it just retries. Do **not** add a keep-alive pinger:
  it burns the 60 CPU-min/day quota for nothing.
- **DB auto-pause.** The deploy script sets `AutoPause` exhaustion behavior — a hard ₹0
  guarantee. A household-scale workload uses a tiny fraction of 100K vCore-seconds; if
  you ever grow past it, flip to `BillOverUsage` consciously.
- **Quota reset.** CPU minutes reset daily, the DB grant monthly. Sync payloads are a
  few KB, so the 165 MB/day egress cap is ~5 orders of magnitude above real usage.

---

## Part 2 — Point the app at your backend

In `src/PakkaHisaab.Maui/Helpers/Constants.cs`, set the release `ApiBaseUrl` to the URL
the script printed. Debug builds keep using your local machine.

---

## Part 3 — Build & distribute the app for free

### Android (recommended free path: Firebase App Distribution)

1. Push the repo to GitHub (public repo = unlimited free CI minutes).
2. Add the two secrets from `.github/workflows/api-deploy.yml`'s header — now every
   push to `main` redeploys the API **and re-runs the smoke test** automatically.
3. One-time Firebase setup:
   - Firebase Console → **pakkahisaab** project → Project settings → Service accounts
     → *Generate new private key* (downloads a JSON file).
   - GitHub repo → Settings → Secrets → Actions → new secret
     `FIREBASE_SERVICE_ACCOUNT` = the whole JSON file content.
   - Firebase Console → **App Distribution** → Testers & groups → create a group named
     `testers` and add tester email addresses to it (they get an email invite + the
     Firebase App Tester app to install builds from).
4. Tag a release: `git tag v1.0.0 && git push --tags`. The `android-apk` workflow
   builds a **signed APK**, uploads it to Firebase App Distribution (testers are
   notified automatically), and also attaches it to the GitHub Release as a fallback
   sideload link.

For a stable signing identity (in-place updates instead of uninstall/reinstall), create
a keystore once — `keytool -genkeypair -v -keystore pakkahisaab-upload.keystore -alias
pakkahisaab -keyalg RSA -keysize 2048 -validity 10000` — and add it to the repo secrets
as documented in the workflow header. Keep the file safe; it's your app's identity.

Building locally instead of CI works too:

```bash
bash tools/get_fonts.sh
dotnet workload install maui
dotnet publish src/PakkaHisaab.Maui -f net9.0-android -c Release -p:AndroidPackageFormats=apk
adb install src/PakkaHisaab.Maui/bin/Release/net9.0-android/publish/*-Signed.apk
```

Optional upgrade later (not free, listed for honesty):

- **Google Play**: US$25 one-time developer fee — needed only for a public store listing.
  Firebase App Distribution and GitHub Releases both stay free indefinitely for testers.

### iOS (the honest picture)

There is no fully-free public iOS distribution. Free options:

- **Personal sideload**: with a free Apple ID, Xcode (or `dotnet build -f net9.0-ios`
  with a free provisioning profile) installs on your own device; the profile expires
  every 7 days and supports 3 apps.
- **Simulator**: unlimited, free, fine for development and demos.
- Public distribution (TestFlight or App Store) requires the US$99/yr Apple Developer
  Program — when you're ready, `docs/DEPLOYMENT.md` §4 has the full IPA pipeline.

---

## Part 4 — Free observability

- `GET /health` is live on your API — point a free uptime monitor at it
  (UptimeRobot free: 50 monitors @ 5-min interval). Set the interval to ≥15 min so the
  monitor itself doesn't eat the F1 CPU quota keeping the app warm.
- App Service → **Log stream** gives live server logs at no cost.
- **App Center is retired** (Microsoft shut it down in March 2025), so treat the
  AppCenter SDK in the app as a no-op — the code now guards against starting it with
  placeholder secrets. For real crash reporting free of charge, add the `Sentry.Maui`
  NuGet package and call `.UseSentry()` in `MauiProgram` with a free-tier DSN
  (5K errors/mo), or use Firebase Crashlytics.

---

## Part 5 — The seamless end-to-end loop, verified

```
┌────────────┐  git push   ┌─────────────────┐  webapps-deploy  ┌────────────────────┐
│  You code  ├────────────►│  GitHub Actions ├─────────────────►│ Azure F1 Web App    │
└────────────┘             │  (free minutes) │                  │ + SQL free offer    │
      ▲                    │  smoke_test.sh ✔│◄─────────────────┤ (auto-created schema)│
      │   git tag v*       └────────┬────────┘   10 E2E checks  └─────────▲──────────┘
      │                             │ signed APK                          │ idempotent
      │                    ┌────────▼────────┐    sideload      ┌─────────┴──────────┐
      └────────────────────┤ GitHub Release  ├─────────────────►│  Phones (offline-  │
                           └─────────────────┘                  │  first + Shiny sync)│
                                                                └────────────────────┘
```

Every arrow is exercised automatically: the CI pipeline won't go green unless the
deployed API passes all ten smoke checks, and the app's offline-first design means the
free tier's cold starts are invisible to users.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Smoke test step 1 fails right after deploy | F1 cold start — wait 60 s and re-run |
| `az sql db create` rejects `--use-free-limit` | One free DB per subscription — reuse it, or drop the old one |
| 500s mentioning login failed for user | SQL firewall — re-run the script (it re-applies the AllowAzure rule) |
| Sync works on Wi-Fi but the phone build can't connect | Release `ApiBaseUrl` not updated, or the device blocks cleartext — the Azure URL is HTTPS, so use it verbatim |
| APK won't update over an old install | Different signing key — configure the stable keystore secrets |
