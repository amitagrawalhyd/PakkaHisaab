# Manual QA script — settle previous month(s)

Run in **Try Demo** mode (Geeta/Raju sample data) on a device/emulator. Automated coverage
stops at `PakkaHisaab.Shared` (pure salary math + period enumeration, see
`tests/PakkaHisaab.Shared.Tests`); everything below touches MAUI navigation, SQLite and
notifications, which need a real Android runtime to verify.

## 1. Back-dated settlement pays the viewed month, not today
1. Dashboard → tap a helper card to open **Calendar**.
2. Tap ‹ Previous twice to go back two months. Mark a couple of days Absent.
3. Tap **Settle**. Confirm the Settlement screen's period label shows the *viewed* month
   (not the current one), and a "Back-dated settlement" chip is visible next to it.
4. Complete the payment (Cash is simplest in demo mode).
5. Return to Calendar for that same past month — the day markings and summary should reflect
   the payment; jump forward to the current month and confirm nothing there changed.

## 2. Ledger entries attribute to the viewed month
1. From Calendar, navigate back one month, tap **Ledger**.
2. Confirm the header shows that month's name.
3. Add an Advance of ₹500. Go to **Settlement** for that same month — the advance should
   appear in the breakdown. Navigate to the *current* month's Settlement — the advance must
   **not** appear there.

## 3. Double-settle guard
1. Settle a month (as in step 1).
2. Navigate back to that same month via Calendar → Settle again.
3. Expect: "✓ Already settled on {date}, ₹{amount} paid" banner, Pay-via-UPI/Cash buttons
   hidden — no way to record a second payment for the same month.

## 4. Arrears badge and routing
1. Pick a helper and leave 2–3 consecutive past months unpaid (mark some absences so
   `FinalPayable > 0` for each), leaving the current month untouched.
2. Return to Dashboard — the helper's card should show "⚠ N months pending".
3. Tap **Settle** on that card — it should open **Calendar** (not jump straight to
   Settlement), landing on the current month with ‹ › nav visible to reach the unpaid months.
4. Settle the oldest unpaid month first, return to Dashboard — the arrears count should drop
   by one and the badge text update accordingly. Once all past months are settled, tapping
   **Settle** should go straight to the current month's Settlement screen again (original
   behavior restored).

## 5. Current-month reminder survives an arrears payment
1. Ensure the *current* month still has money owed (don't settle it).
2. Settle an older arrears month per step 4.
3. Confirm (via device notification settings or waiting for the 9 AM window, or by checking
   `LocalNotificationCenter` state if instrumented) that the current month's 1st–10th salary
   reminder is still scheduled — i.e. paying an old month must not silence the still-due
   current month's nag.
4. Separately: fully settle the *current* month, background/reopen the app (triggers
   `OnAppearing` → `LoadAsync`) and confirm the reminder is **not** re-armed.

## 6. Leave carry-forward stays monotonic
1. Use a helper with `CarryOverLeaveAllowed = true` (edit via Helper form if needed).
2. Settle an older month, note the resulting `CarriedOverLeaves` (visible in Helper edit
   screen or Admin portal).
3. Settle a *newer* month (e.g. the current one) — carry-forward updates as expected.
4. Now go back and settle an even older, previously-skipped month. Confirm
   `CarriedOverLeaves` is **not** rolled back to that older month's smaller number — the more
   recent month's value should still stand.

## 8. Deleting a payment un-settles the month (bug fix)
1. Settle a month (UPI or Cash) — confirm the app navigates back to Home/Calendar as expected
   and shows "Payment recorded".
2. Open **Ledger** for that same month, find the "Salary Payment" entry just created, delete it
   (confirm the delete prompt).
3. Go back to **Settlement** for that same month. Expect: the "✓ Already settled" banner is
   gone, Pay-via-UPI/Cash buttons are visible again, and the payable amount reflects the
   payment no longer existing. (Previously this stayed stuck on "Already settled" forever,
   with no way to re-pay the month.)
4. Re-settle the month — should succeed normally, showing "Already settled" again afterward.

## 9. Settle always navigates home (bug fix)
1. Settle a helper with `CarryOverLeaveAllowed = true` for the very first time (no prior
   payment history) via both UPI and Cash on different helpers/months, confirming each one
   navigates back to Home immediately after "Payment recorded" — no case where the app stays
   on the Settlement screen with no feedback.
2. If a settle ever silently fails to navigate again, expect a "Could not record the payment.
   Please try again." toast now instead of no feedback at all — report that toast appearing (or
   the underlying issue) rather than a stuck screen, since it now indicates a real remaining bug
   rather than a swallowed exception.

## Note on translations

New strings for this change were added by hand directly to the neutral
`AppStrings.resx` only (other languages fall back to English automatically for
new keys). **Do not run `tools/gen_resx.py` right now** — `tools/translations.json`
was found to be out of sync with the real resx files (missing keys such as
`App_SplashTagline` and the `Help_*` voice-command strings that exist only in the
hand-maintained resx files), so regenerating from it would silently delete that
content. This drift pre-dates this change and should be reconciled separately
before the generator is used again.

## 7. TestReport bundle spot-checks
- **S-04**: Dashboard total banner shows a muted "across N helpers" subtitle under the total.
- **M-02**: Login screen — Sign-in button is disabled (not just silently rejecting) while
  Email or Password is empty; becomes enabled once both are filled.
- **S-02**: Settings → Log out now shows a confirm dialog before actually logging out.
- **S-03**: Calendar screen already shows a permanent Present/Absent/Half-Day legend — confirm
  it's visible without any extra interaction on first open (no code change was needed here).
