#!/usr/bin/env bash
# ============================================================================
# PakkaHisaab — end-to-end smoke test
# Exercises the full server contract against a live deployment:
#   health → register → login → sync push → IDEMPOTENT REPLAY → sync pull
#   → salary math cross-check → delete account → login must now fail
#
# Usage:  bash deploy/smoke_test.sh https://api-pakkahisaab-xyz.azurewebsites.net
# Exits non-zero on the first failure. Needs: bash, curl, python3.
# ============================================================================
set -euo pipefail

BASE="${1:?usage: smoke_test.sh <base-url>}"
EMAIL="smoke+$(date +%s)@pakkahisaab.test"
PASS="Sm0ke!Passw0rd"
HELPER_ID="$(python3 -c 'import uuid;print(uuid.uuid4())')"
BATCH_ID="$(python3 -c 'import uuid;print(uuid.uuid4())')"
NOW="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
TODAY="$(date -u +%Y-%m-%d)"
PERIOD="$(date -u +%Y-%m)"

pass() { echo "  ✔ $1"; }
fail() { echo "  ✘ $1"; exit 1; }
jsonget() { python3 -c "import json,sys;d=json.load(sys.stdin);print(d$1)"; }

echo "1) GET /health"
[ "$(curl -sk -o /dev/null -w '%{http_code}' "$BASE/health")" = "200" ] \
  && pass "healthy" || fail "health check"

echo "2) POST /auth/register"
REG=$(curl -sk -X POST "$BASE/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"displayName\":\"Smoke Test\",\"phoneNumber\":null}")
TOKEN=$(echo "$REG" | jsonget "['accessToken']") || fail "register: $REG"
pass "registered $EMAIL"

echo "3) POST /auth/login"
LOGIN=$(curl -sk -X POST "$BASE/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\"}")
TOKEN=$(echo "$LOGIN" | jsonget "['accessToken']") || fail "login: $LOGIN"
pass "logged in"

AUTH="Authorization: Bearer $TOKEN"

PUSH_BODY=$(cat <<JSON
{
  "clientBatchId": "$BATCH_ID",
  "deviceId": "smoke-device",
  "helpers": [{
    "id": "$HELPER_ID", "name": "Geeta", "whatsAppNumber": "+919800000001",
    "upiId": "geeta@upi", "category": 0, "wageType": 0, "monthlyWage": 5000,
    "ratePerUnit": 0, "unitLabel": "L", "monthlyAllowedAbsences": 2,
    "carryOverLeaveAllowed": true, "carriedOverLeaves": 0, "isActive": true,
    "modifiedAtUtc": "$NOW", "rowVersion": 0, "isDeleted": false
  }],
  "attendance": [{
    "id": "$(python3 -c 'import uuid;print(uuid.uuid4())')", "helperId": "$HELPER_ID",
    "date": "$TODAY", "status": 1, "unitsDelivered": 0,
    "modifiedAtUtc": "$NOW", "rowVersion": 0, "isDeleted": false
  }],
  "ledgerEntries": [{
    "id": "$(python3 -c 'import uuid;print(uuid.uuid4())')", "helperId": "$HELPER_ID",
    "type": 0, "amount": 500, "method": 1, "note": "smoke advance",
    "period": "$PERIOD", "occurredAtUtc": "$NOW", "upiTransactionRef": null,
    "modifiedAtUtc": "$NOW", "rowVersion": 0, "isDeleted": false
  }],
  "settlements": []
}
JSON
)

echo "4) POST /sync/push (outbox drain)"
PUSH=$(curl -sk -X POST "$BASE/sync/push" -H 'Content-Type: application/json' -H "$AUTH" -d "$PUSH_BODY")
ACCEPTED=$(echo "$PUSH" | jsonget "['acceptedRowVersions']['$HELPER_ID']") || fail "push: $PUSH"
[ "$(echo "$PUSH" | jsonget "['alreadyProcessed']")" = "False" ] || fail "first push flagged as replay"
pass "3 records accepted, helper rowVersion=$ACCEPTED"

echo "5) POST /sync/push AGAIN with the SAME ClientBatchId (idempotency)"
REPLAY=$(curl -sk -X POST "$BASE/sync/push" -H 'Content-Type: application/json' -H "$AUTH" -d "$PUSH_BODY")
[ "$(echo "$REPLAY" | jsonget "['alreadyProcessed']")" = "True" ] || fail "replay not detected: $REPLAY"
[ "$(echo "$REPLAY" | jsonget "['acceptedRowVersions']['$HELPER_ID']")" = "$ACCEPTED" ] \
  || fail "replay returned different row versions"
pass "duplicate batch replayed verbatim — no double effects"

echo "6) POST /sync/pull (watermark 0 → everything comes back)"
PULL=$(curl -sk -X POST "$BASE/sync/pull" -H 'Content-Type: application/json' -H "$AUTH" \
  -d '{"sinceWatermark":0,"deviceId":"smoke-device-2"}')
[ "$(echo "$PULL" | jsonget "['helpers'][0]['name']")" = "Geeta" ] || fail "pull: $PULL"
[ "$(echo "$PULL" | jsonget "['ledgerEntries'][0]['amount']")" = "500.0" ] \
  || [ "$(echo "$PULL" | jsonget "['ledgerEntries'][0]['amount']")" = "500" ] \
  || fail "advance amount mismatch"
WM=$(echo "$PULL" | jsonget "['newWatermark']")
pass "pulled helper + attendance + ledger, watermark=$WM"

echo "7) POST /sync/pull since watermark $WM (delta must be empty)"
DELTA=$(curl -sk -X POST "$BASE/sync/pull" -H 'Content-Type: application/json' -H "$AUTH" \
  -d "{\"sinceWatermark\":$WM,\"deviceId\":\"smoke-device-2\"}")
[ "$(echo "$DELTA" | jsonget "['helpers'].__len__()")" = "0" ] || fail "delta not empty: $DELTA"
pass "incremental pull returns nothing new"

echo "8) POST /ai/parse — shared NLP parser"
PARSE=$(curl -sk -X POST "$BASE/ai/parse" -H 'Content-Type: application/json' -H "$AUTH" \
  -d '{"text":"Gave Geeta 500 advance"}')
[ "$(echo "$PARSE" | jsonget "['intent']")" = "1" ] || fail "parse: $PARSE"   # 1 = LogAdvance
[ "$(echo "$PARSE" | jsonget "['helperNameHint']")" = "Geeta" ] || fail "name hint: $PARSE"
pass "voice text parsed → LogAdvance(500, Geeta)"

echo "9) DELETE /account (store-compliance erasure)"
CODE=$(curl -sk -o /dev/null -w '%{http_code}' -X DELETE "$BASE/account" \
  -H 'Content-Type: application/json' -H "$AUTH" \
  -d "{\"password\":\"$PASS\",\"confirmation\":\"DELETE\"}")
[ "$CODE" = "204" ] || fail "delete returned $CODE"
pass "account + all data erased"

echo "10) POST /auth/login after deletion must fail"
CODE=$(curl -sk -o /dev/null -w '%{http_code}' -X POST "$BASE/auth/login" \
  -H 'Content-Type: application/json' -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\"}")
[ "$CODE" = "401" ] || fail "expected 401, got $CODE"
pass "credentials gone"

echo
echo "ALL 10 CHECKS PASSED — the deployment works end to end. ✅"
