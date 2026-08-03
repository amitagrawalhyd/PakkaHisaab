# Handoff — Admin English-translation feature + v1.0.4 release

You asked me to prepare, not push, this change to your live infrastructure and to not run the
final AAB signing myself. Everything below is built and tested locally; these are the exact
steps for you to actually ship it.

## Cost — why it's off by default

- **Azure AI Translator**: 2,000,000 characters/month free (F0), then $10/million (S1).
- **Google Cloud Translation**: 500,000 characters/month free, then $20/million.

For this app's scale (short names/notes, tens of characters each) you'd realistically never
leave either free tier — but the feature now makes an external network call per new/changed
record whenever it's on, so it stays **disabled by default** and is a runtime toggle, not a
deploy-time decision.

## What changed since the last handoff

- **Settings gate** (new): `dbo.TranslationSettings` — a single admin-editable row
  (`Enabled` bit, default 0; `Provider`, default `GoogleFree`). Admin → **Settings** page has
  a switch + provider dropdown. `TranslationServiceSelector` checks this *before* calling any
  provider — disabled means zero network calls, not just "provider returns null."
- **Free provider** (new, and now the default when enabled): `GoogleFreeTranslateService`
  calls the same public, unofficial endpoint behind translate.google.com — no signup, no key,
  no cost. It is **not** an officially supported API: Google can rate-limit, block, or change
  it without notice, and using it for production traffic is outside Google's Terms of Service
  for the Cloud Translation product. It's a reasonable default for a low-volume app; switch
  the Provider dropdown to "Google Cloud Translation" for a supported SLA once you're
  comfortable with the (still-small) cost.
- **DB**: `dbo.Users.DisplayNameEnglish`, `dbo.Helpers.NameEnglish`, `dbo.LedgerEntries.NoteEnglish`
  (from the previous handoff) + `dbo.TranslationSettings` (new). `db/001_schema.sql` covers
  fresh installs; `db/004_translation_fields.sql` and `db/005_translation_settings.sql` are
  the two migrations for your existing live database.
- **Admin**: Helpers, Ledger, Settlements, Users (index + details) and the Dashboard show the
  English text with the original underneath when they differ, and search matches both —
  unchanged from before. New: **Settings** page (`/Settings`, linked in the sidebar under
  "System") to flip the feature on/off and choose a provider.
- **Version**: `1.0.3` (versionCode 6) → **`1.0.4` (versionCode 7)** in
  `src/PakkaHisaab.Maui/PakkaHisaab.Maui.csproj` (unchanged from the previous handoff).
- **Tests**: 13 in `PakkaHisaab.Shared.Tests` + 13 in `PakkaHisaab.Api.Tests` (up from 5) — all
  passing. New coverage: disabled means zero HTTP calls, enabled dispatches to the right
  provider's actual endpoint, the free endpoint's quirky nested-array response parses
  correctly (including multi-chunk long text), Google Cloud with no API key configured stays a
  safe no-op.

## 1. Apply the DB migrations to your live SQL server

```bash
sqlcmd -S sql-pakkahisaab-amitagrawal.database.windows.net -d PakkaHisaab -U phadmin -P '<PW>' \
  -i db/004_translation_fields.sql -i db/005_translation_settings.sql
```

(Or run both files' contents through Azure Data Studio / SSMS.) Both are idempotent/safe to
re-run, and `005` seeds the settings row as `Enabled = 0` — the feature stays off until you
turn it on from the Admin Settings page.

## 2. (Optional) Google Cloud API key — only if you want the paid provider

Skip this entirely if you're sticking with the free default. If you later switch the Settings
page's Provider to "Google Cloud Translation":

1. console.cloud.google.com → create/select a project → enable **Cloud Translation API**.
2. APIs & Services → Credentials → Create Credentials → API key. Restrict it to the
   Cloud Translation API.
3. `az webapp config appsettings set -g rg-pakkahisaab -n api-pakkahisaab-amitagrawal --settings GoogleTranslate__ApiKey="<key>"`

Without this, selecting "Google Cloud Translation" in Settings just no-ops (same safe fallback
as before) — the free provider needs no key at all.

## 3. Deploy Admin + API

```bash
git add -A
git commit -m "Add on/off translation setting and a free provider option; bump to 1.0.4"
git push origin main
```

Existing CI (`admin-deploy.yml`, `api-deploy.yml`) redeploys both on push to `main`. Verify:
- `https://api-pakkahisaab-amitagrawal.azurewebsites.net/health` → `Healthy`
- Sign in to Admin → **Settings** → confirm "Enable automatic translation" is unchecked and
  Provider shows "Free Google Translate". Flip it on, save, and edit/re-sync a helper name to
  see English text appear on the Helpers page.

## 4. Build the signed AAB yourself

Unchanged from before — your keystore, your passwords, your call:

```bash
dotnet publish src/PakkaHisaab.Maui -f net9.0-android36.0 -c Release \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore="$HOME/keys/pakkahisaab-upload.keystore" \
  -p:AndroidSigningKeyAlias=pakkahisaab \
  -p:AndroidSigningKeyPass=env:KEY_PASS \
  -p:AndroidSigningStorePass=env:STORE_PASS \
  -p:AndroidPackageFormats=aab
```

Output: `src/PakkaHisaab.Maui/bin/Release/net9.0-android36.0/publish/com.clearkhata.pakkahisaab-Signed.aab`

## Notes / boundaries

- Existing rows with no `*English` value aren't retroactively translated in bulk — only on
  their next create/change (and only once you've turned the feature on). A one-time backfill
  would be a separate, deliberately rate-limited script — not something to run silently.
- The free provider is genuinely free but unofficial — if it ever stops working (Google
  blocking the endpoint, format changes), translated fields just go back to showing the
  original text; nothing else breaks, since every provider is designed to fail soft.
- Voice-to-Ledger and the MAUI app itself are untouched — translation is Admin-facing only.
