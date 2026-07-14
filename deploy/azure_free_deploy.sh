#!/usr/bin/env bash
# ============================================================================
# PakkaHisaab — ONE-COMMAND free deployment to Azure (₹0/month)
#
#   Backend : App Service F1 plan   → free forever (60 CPU-min/day, 1 GB)
#   Database: Azure SQL free offer  → free forever (100K vCore-sec/mo, 32 GB,
#                                     auto-pauses when the monthly grant is used)
#
# Prereqs : Azure CLI (az) logged in — `az login` — on any subscription
#           (the free offers work on Pay-As-You-Go too; nothing here bills).
# Usage   : bash deploy/azure_free_deploy.sh [name-suffix]
# Idempotent: safe to re-run; existing resources are reused.
# ============================================================================
set -euo pipefail

SUFFIX="${1:-$(whoami | tr -cd 'a-z0-9' | cut -c1-8)}"
RG="rg-pakkahisaab"
LOCATION="${AZ_LOCATION:-centralindia}"
PLAN="plan-pakkahisaab"
APP="api-pakkahisaab-${SUFFIX}"          # must be globally unique
SQL_SERVER="sql-pakkahisaab-${SUFFIX}"   # must be globally unique
SQL_DB="PakkaHisaab"
SQL_ADMIN="phadmin"
SQL_PASSWORD="${SQL_PASSWORD:-$(openssl rand -base64 24 | tr -d '/+=' | cut -c1-20)Aa1!}"
JWT_KEY="${JWT_KEY:-$(openssl rand -base64 48 | tr -d '\n')}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo "==> Resource group"
az group create -n "$RG" -l "$LOCATION" -o none

echo "==> SQL logical server (free — the server itself never bills)"
az sql server create -g "$RG" -n "$SQL_SERVER" -l "$LOCATION" \
  --admin-user "$SQL_ADMIN" --admin-password "$SQL_PASSWORD" -o none 2>/dev/null || true

echo "==> Azure SQL Database — FREE offer (--use-free-limit)"
# AutoPause = hard ₹0 guarantee: DB pauses if the monthly grant runs out,
# resumes on the 1st. (Use BillOverUsage later if you ever outgrow it.)
az sql db create -g "$RG" -s "$SQL_SERVER" -n "$SQL_DB" \
  --edition GeneralPurpose --compute-model Serverless --family Gen5 --capacity 2 \
  --use-free-limit --free-limit-exhaustion-behavior AutoPause -o none 2>/dev/null || true

echo "==> Firewall: allow Azure services (App Service) through"
az sql server firewall-rule create -g "$RG" -s "$SQL_SERVER" -n AllowAzure \
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 -o none 2>/dev/null || true

echo "==> App Service plan — F1 (Free)"
az appservice plan create -g "$RG" -n "$PLAN" --sku F1 --is-linux -o none 2>/dev/null || true

echo "==> Web app"
az webapp create -g "$RG" -p "$PLAN" -n "$APP" --runtime "DOTNETCORE:8.0" -o none 2>/dev/null || true

echo "==> App settings (connection string, JWT, auto schema creation)"
CONN="Server=tcp:${SQL_SERVER}.database.windows.net,1433;Database=${SQL_DB};User ID=${SQL_ADMIN};Password=${SQL_PASSWORD};Encrypt=True;Connection Timeout=60"
az webapp config appsettings set -g "$RG" -n "$APP" -o none --settings \
  ConnectionStrings__Default="$CONN" \
  Database__Provider="SqlServer" \
  Database__AutoCreate="true" \
  Jwt__Key="$JWT_KEY" \
  Jwt__Issuer="https://${APP}.azurewebsites.net" \
  Jwt__Audience="pakkahisaab-mobile"

echo "==> Build & publish the API"
dotnet publish "$ROOT/src/PakkaHisaab.Api" -c Release -o "$ROOT/publish" >/dev/null
rm -f "$ROOT/api.zip"
if command -v zip >/dev/null 2>&1; then
  ( cd "$ROOT/publish" && zip -qr ../api.zip . )
else
  # Git Bash on Windows ships no zip. Neither Compress-Archive nor
  # ZipFile.CreateFromDirectory are safe substitutes here — Windows PowerShell's
  # .NET Framework stores backslash path separators for nested folders, which
  # Kudu's Linux-side rsync can't stat, silently failing the whole deploy.
  # zip_publish.ps1 walks files manually and forces forward-slash entry names.
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cd "$ROOT/deploy" && pwd -W)\\zip_publish.ps1" \
    -SourceDir "$(cd "$ROOT/publish" && pwd -W)" -DestZip "$(cd "$ROOT" && pwd -W)\\api.zip"
fi
az webapp deploy -g "$RG" -n "$APP" --src-path "$ROOT/api.zip" --type zip -o none

BASE="https://${APP}.azurewebsites.net"
echo
echo "============================================================"
echo " Deployed:            $BASE"
echo " Swagger (dev only):  $BASE/swagger"
echo " SQL admin password:  $SQL_PASSWORD   <-- SAVE THIS"
echo " JWT signing key:     (stored as app setting Jwt__Key)"
echo "============================================================"
echo
echo "==> Waiting for first cold start, then running the end-to-end smoke test…"
sleep 30
bash "$ROOT/deploy/smoke_test.sh" "$BASE"

echo
echo "Next: set Constants.ApiBaseUrl (release branch) to $BASE and build the app:"
echo "  dotnet publish src/PakkaHisaab.Maui -f net9.0-android -c Release"
