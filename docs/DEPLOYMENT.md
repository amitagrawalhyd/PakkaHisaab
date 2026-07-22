# PakkaHisaab (ClearKhata) — End-to-End Deployment Guide

> **Deploying for free?** See [FREE_DEPLOYMENT.md](FREE_DEPLOYMENT.md) — a ₹0/month strategy (Azure F1 + SQL free offer + GitHub Actions) with a one-command deploy script and automated end-to-end smoke tests.

Covers: Azure backend deployment, SQL Server provisioning, Android keystore signing + AAB
generation for Google Play, and iOS provisioning + IPA for App Store Connect.

---

## 0. Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 8.0.4xx | `dotnet --version` |
| MAUI workloads | latest | `dotnet workload install maui` |
| Visual Studio 2022 / VS Code | 17.10+ | VS for iOS pairing, or a Mac with Xcode 15+ |
| Azure CLI | 2.60+ | `az login` |
| Android SDK | API 34 | installed by the MAUI workload |
| Xcode (Mac) | 15+ | iOS builds require a macOS build host |

One-time repo setup:

```bash
bash tools/get_fonts.sh          # downloads Poppins + Material Symbols into Resources/Fonts
python3 tools/gen_resx.py        # regenerates the 23-language .resx set (already committed)
dotnet build src/PakkaHisaab.Api # sanity-check the backend builds
```

Replace before any release build:

- `Constants.AppCenterAndroidSecret` / `AppCenterIosSecret` (create two apps at appcenter.ms)
- `Constants.ApiBaseUrl` release value → your API host
- `Jwt:Key` in API configuration → a 64+ char random secret (Key Vault, never in git)

---

## 1. Database — Azure SQL

```bash
az group create -n rg-pakkahisaab -l centralindia

az sql server create -g rg-pakkahisaab -n sql-pakkahisaab \
  --admin-user phadmin --admin-password '<STRONG-PASSWORD>'

az sql db create -g rg-pakkahisaab -s sql-pakkahisaab -n PakkaHisaab \
  --service-objective S0 --backup-storage-redundancy Zone

# Allow Azure services (App Service) through the firewall
az sql server firewall-rule create -g rg-pakkahisaab -s sql-pakkahisaab \
  -n AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
```

Apply the schema (SSMS, Azure Data Studio, or sqlcmd):

```bash
sqlcmd -S sql-pakkahisaab.database.windows.net -d PakkaHisaab -U phadmin -P '<PW>' \
  -i db/001_schema.sql -i db/002_maintenance.sql
```

> Alternative: `dotnet ef migrations add Init && dotnet ef database update` from
> `src/PakkaHisaab.Api` — the SQL scripts and the EF model are equivalent; pick one
> strategy and stay with it.

---

## 2. Backend API — Azure App Service

```bash
az appservice plan create -g rg-pakkahisaab -n plan-pakkahisaab --sku B1 --is-linux

az webapp create -g rg-pakkahisaab -p plan-pakkahisaab -n api-pakkahisaab \
  --runtime "DOTNETCORE:8.0"

# Configuration (use Key Vault references in production)
az webapp config appsettings set -g rg-pakkahisaab -n api-pakkahisaab --settings \
  ConnectionStrings__Default="Server=tcp:sql-pakkahisaab.database.windows.net,1433;Database=PakkaHisaab;User ID=phadmin;Password=<PW>;Encrypt=True" \
  Jwt__Key="<64+ char random secret>" \
  Jwt__Issuer="https://api-pakkahisaab.azurewebsites.net" \
  Jwt__Audience="pakkahisaab-mobile"

# Publish
dotnet publish src/PakkaHisaab.Api -c Release -o publish
cd publish && zip -r ../api.zip . && cd ..
az webapp deploy -g rg-pakkahisaab -n api-pakkahisaab --src-path api.zip --type zip
```

Verify: `https://api-pakkahisaab.azurewebsites.net/health` → `Healthy`, and `/swagger`
in a browser (dev slot only).

Production hardening checklist: custom domain + managed TLS, Application Insights,
`PurgeOldSyncBatches` on a daily Elastic Job, Key Vault for `Jwt:Key` and the connection
string, and a staging slot for zero-downtime swaps.

---

## 2.5 Admin console — Azure App Service

The Admin console (`src/PakkaHisaab.Admin`) is a separate Razor Pages web app that reads
and writes the **same** Azure SQL database as the API — no separate database, no separate
plan. It runs as a second Web App on the API's existing App Service plan (a plan can host
several apps).

```bash
az webapp create -g rg-pakkahisaab -p plan-pakkahisaab -n admin-pakkahisaab \
  --runtime "DOTNETCORE:8.0"

# Point it at the SAME database the API uses — copy the value verbatim from the API app.
CONN=$(az webapp config appsettings list -g rg-pakkahisaab -n api-pakkahisaab \
  --query "[?name=='ConnectionStrings__Default'].value" -o tsv)

az webapp config appsettings set -g rg-pakkahisaab -n admin-pakkahisaab --settings \
  ConnectionStrings__Default="$CONN" \
  Database__Provider="SqlServer" \
  ASPNETCORE_ENVIRONMENT="Production"

dotnet publish src/PakkaHisaab.Admin -c Release -o publish
cd publish && zip -qr ../admin.zip . && cd ..
az webapp deploy -g rg-pakkahisaab -n admin-pakkahisaab --src-path admin.zip --type zip
```

> **Zip on Windows:** PowerShell's `Compress-Archive` embeds Windows host-OS metadata that
> Azure's Linux Kudu extraction (`parallel_rsync.sh`) mishandles, producing files with
> literal backslashes in their names and a `400`/rsync failure. Zip from a Linux/macOS
> shell (or `zip -qr`, as GitHub Actions does) — not `Compress-Archive`.

**Admin login** is gated on `dbo.Users.IsAdmin = 1` — there is no separate credential
store. Promote an account (or create a console-only one with no household data) via SQL:

```sql
-- one-time: adds the column to a database provisioned before this feature existed
-- (see db/003_admin_flag.sql; 001_schema.sql already includes it on a fresh DB)
UPDATE dbo.Users SET IsAdmin = 1 WHERE Email = 'you@example.com';
```

CI: `.github/workflows/admin-deploy.yml` redeploys on every push to `main` touching
`src/PakkaHisaab.Admin`, `src/PakkaHisaab.Infrastructure` or `src/PakkaHisaab.Shared`,
reusing the API's OIDC service principal (granted an additional `Website Contributor`
role scoped to the Admin web app resource — no new GitHub secrets needed).

---

## 2.6 Public website — pakkahisaab.app (privacy policy, terms, App Links)

`web/` is a small static site (privacy policy, terms, `.well-known/assetlinks.json`) deployed
free via GitHub Pages by `.github/workflows/pages-deploy.yml` on every push to `main` that
touches `web/**`. `Constants.PrivacyPolicyUrl`/`TermsUrl` point at it, and Play/App Store both
require a live, publicly-reachable privacy policy URL to approve a submission — this is not
optional.

One-time setup:

1. Pages is already enabled on the repo (`gh api -X POST repos/<owner>/<repo>/pages -f
   build_type=workflow`) and the workflow deploys on push. This alone does **not** make
   `pakkahisaab.app` resolve — that needs DNS.
2. At your domain registrar, add these records for the apex domain (`pakkahisaab.app`, no
   `www`) pointing at GitHub Pages:

   | Type | Name | Value |
   |---|---|---|
   | A | @ | 185.199.108.153 |
   | A | @ | 185.199.109.153 |
   | A | @ | 185.199.110.153 |
   | A | @ | 185.199.111.153 |
   | AAAA (optional) | @ | 2606:50c0:8000::153, 2606:50c0:8001::153, 2606:50c0:8002::153, 2606:50c0:8003::153 |

3. DNS propagation can take anywhere from minutes to ~24h. Once it resolves, GitHub
   auto-provisions an HTTPS certificate for the domain (Settings → Pages on the repo shows
   the status) — "Enforce HTTPS" is already on by default for new custom domains.
4. Verify: `https://pakkahisaab.app/privacy` and `/terms` load, and
   `https://pakkahisaab.app/.well-known/assetlinks.json` returns the Digital Asset Links JSON
   (from Play Console → App integrity → App signing) used to verify Android App Links —
   `MainActivity.cs` declares the matching `[IntentFilter(..., AutoVerify = true)]`.

---

## 3. Android — keystore, signing, AAB

### 3.1 Create the upload keystore (once, back it up!)

```bash
keytool -genkeypair -v -keystore pakkahisaab-upload.keystore \
  -alias pakkahisaab -keyalg RSA -keysize 2048 -validity 10000 \
  -dname "CN=PakkaHisaab, O=ClearKhata, C=IN"
```

Store the keystore and passwords in a secret manager. Losing it means losing the
ability to update the app (unless you enrolled in Play App Signing — do enroll).

### 3.2 Build the signed AAB

```bash
dotnet publish src/PakkaHisaab.Maui -f net9.0-android -c Release \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=$HOME/keys/pakkahisaab-upload.keystore \
  -p:AndroidSigningKeyAlias=pakkahisaab \
  -p:AndroidSigningKeyPass=env:KEY_PASS \
  -p:AndroidSigningStorePass=env:STORE_PASS \
  -p:AndroidPackageFormats=aab
```

Output: `src/PakkaHisaab.Maui/bin/Release/net9.0-android/publish/com.clearkhata.pakkahisaab-Signed.aab`

### 3.3 Play Console

1. Create the app (Default language: en-IN; App category: Finance/Productivity).
2. **App content** declarations: Data safety form (declare AppCenter diagnostics +
   account data with deletion available — point to the in-app *Delete My Account & Data*
   button and a web deletion URL), Financial features declaration (the app only deep-links
   into UPI apps; it never handles funds itself).
3. Upload the AAB to *Internal testing* → promote through Closed → Production.
4. Store listing assets: the splash logo works for the 512×512 icon (`docs/logo_512.png`);
   generate screenshots from the Demo mode so reviewers see populated data.

> Play reviewers use the **Try Demo** button — mention it explicitly in
> "App access" notes: "Full functionality available without credentials via the
> 'Try Demo' button on the login screen."

### 3.4 Version bumps

`ApplicationDisplayVersion` (user-visible, e.g. 1.0.1) and `ApplicationVersion`
(monotonic integer) in `PakkaHisaab.Maui.csproj` — bump both per release.

---

## 4. iOS — certificates, provisioning, IPA

### 4.1 Apple Developer setup (once)

1. Enroll in the Apple Developer Program.
2. Certificates: create an **Apple Distribution** certificate (Keychain Access → CSR →
   developer.apple.com → Certificates → Apple Distribution).
3. Identifier: register `com.clearkhata.pakkahisaab` with capabilities:
   *Push Notifications OFF (local only), Background Modes ON (fetch, processing)*.
4. Profile: create an **App Store** provisioning profile bound to the identifier +
   distribution certificate; download and double-click to install (or let VS manage it).

### 4.2 Build the IPA (Mac or Pair-to-Mac from Windows)

```bash
dotnet publish src/PakkaHisaab.Maui -f net9.0-ios -c Release \
  -p:IncludeIos=true \
  -p:ArchiveOnBuild=true \
  -p:RuntimeIdentifier=ios-arm64 \
  -p:CodesignKey="Apple Distribution: <Your Team Name> (<TEAMID>)" \
  -p:CodesignProvision="PakkaHisaab AppStore"
```

Output: `src/PakkaHisaab.Maui/bin/Release/net9.0-ios/ios-arm64/publish/PakkaHisaab.Maui.ipa`

### 4.3 Upload & review

1. Upload via **Transporter** (or `xcrun altool --upload-app`).
2. App Store Connect → TestFlight (internal testers) → App Store submission.
3. **Review notes** (critical for approval): "Tap **Try Demo** on the login screen for
   full access without an account — no server dependency. Account deletion is available
   at Settings → Delete My Account & Data (App Store Guideline 5.1.1(v))."
4. App Privacy questionnaire: Contact Info (email) + Identifiers (device id) linked to
   the user; Diagnostics (AppCenter) not linked. Data deletion URL: your `/privacy` page.

---

## 5. CI/CD sketch (GitHub Actions)

- **api.yml** — on push to `main`: `dotnet test` → `dotnet publish` → `azure/webapps-deploy`.
- **android.yml** — on tag `v*`: restore MAUI workload → decode keystore from
  `secrets.KEYSTORE_B64` → signed AAB → upload artifact / Play Developer API.
- **ios.yml** — `macos-14` runner: import cert + profile from secrets →
  `dotnet publish -f net9.0-ios` → `xcrun altool` upload.

---

## 6. Release smoke checklist

- [ ] Fresh install → onboarding carousel → **Try Demo** → Geeta & Raju visible, totals correct
- [ ] Add helper → mark attendance (3 taps cycle Present→Absent→Half-Day) → advance → settle via UPI intent
- [ ] Airplane mode: every action still instant; back online: Shiny job drains the outbox (verify rows in SQL)
- [ ] 5 PM notification fires with "Absent" action; salary alert stops after "Mark Paid"
- [ ] Language switch (e.g., हिन्दी) updates all visible screens instantly; Arabic/Urdu flip to RTL
- [ ] Both PDFs render with the logo and share to WhatsApp
- [ ] Delete My Account & Data → server rows gone (`dbo.EraseUserData` audit) + local DB wiped
- [ ] Rooted emulator (Magisk) shows the integrity warning, app still usable
