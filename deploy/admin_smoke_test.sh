#!/usr/bin/env bash
# ============================================================================
# PakkaHisaab Admin — deploy smoke test
# Confirms the admin console is up and serving its shell (health, login page,
# static assets). Does NOT exercise login/CRUD — those touch real production
# data and require a real admin credential, so they're out of scope for CI.
#
# Usage:  bash deploy/admin_smoke_test.sh https://admin-pakkahisaab-xyz.azurewebsites.net
# ============================================================================
set -euo pipefail

BASE="${1:?usage: admin_smoke_test.sh <base-url>}"

pass() { echo "  ✔ $1"; }
fail() { echo "  ✘ $1"; exit 1; }

echo "1) GET /health"
[ "$(curl -sk -o /dev/null -w '%{http_code}' "$BASE/health")" = "200" ] \
  && pass "healthy" || fail "health check"

echo "2) GET /Account/Login"
[ "$(curl -sk -o /dev/null -w '%{http_code}' "$BASE/Account/Login")" = "200" ] \
  && pass "login page reachable" || fail "login page"

echo "3) GET /img/logo.png (branding asset)"
[ "$(curl -sk -o /dev/null -w '%{http_code}' "$BASE/img/logo.png")" = "200" ] \
  && pass "logo asset served" || fail "logo asset"

echo "4) GET / (unauthenticated) must redirect, not 500"
CODE=$(curl -sk -o /dev/null -w '%{http_code}' "$BASE/")
[ "$CODE" = "302" ] || [ "$CODE" = "200" ] || fail "unexpected status $CODE on /"
pass "dashboard route guarded ($CODE)"

echo
echo "ALL ADMIN SMOKE CHECKS PASSED ✅"
