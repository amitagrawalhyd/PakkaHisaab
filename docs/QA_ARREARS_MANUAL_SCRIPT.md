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

## 10. Double-tap Pay no longer creates duplicate payments (bug fix)
1. On Settlement, tap **Pay via UPI** or **Log cash payment** twice in very quick succession
   (or tap-and-hold rapidly). Expect: the button becomes disabled/unresponsive to the second
   tap while the first is processing — only one "Salary Payment" ledger entry should be
   created, not two.
2. Open **Ledger** for that month and confirm there is exactly one "Salary Payment" row for
   this settlement.
3. If you already have test data from before this fix, check every month you've settled for
   *more than one* "Salary Payment" row — a leftover duplicate from the old bug will make a
   month look permanently "already settled" even after deleting one of the two rows, since the
   app correctly refuses to un-settle a month while *any* payment still exists for it (by
   design — it supports legitimate multi-installment payments). Delete every "Salary Payment"
   row for that month to fully clear it.

## 11. Log cash payment no longer crashes the app (bug fix)
1. Root cause: recording a payment (`MarkPaidAsync`) kicks off a background sync to the server
   right before returning control to the Settlement screen. That sync ran on a fire-and-forget
   background thread whose early setup steps (reading the stored auth token, etc.) sat outside
   any try/catch — an exception there (e.g. a transient network blip, or the Android
   SecureStorage/Keystore throwing on some devices) became an unobserved background-thread
   exception, which can take the whole app down instead of just failing that one sync attempt.
2. Fix, three layers deep:
   - `SyncEngine.SynchronizeAsync` now wraps its *entire* body (not just the network calls) in
     one try/catch, so nothing it does can ever throw uncaught — a failure always comes back as
     `false` (row stays dirty, retried on the next scheduled sync) instead of an exception.
   - Push/pull calls now retry up to 3 times with a short backoff before giving up, so a single
     transient blip at the exact moment of payment doesn't immediately surrender.
   - Two platform-level safety nets were added as defense-in-depth (`TaskScheduler.
     UnobservedTaskException` in `App.xaml.cs`, `AndroidEnvironment.UnhandledExceptionRaiser`
     in `MainApplication.cs`) so *any* future stray background-thread exception anywhere in the
     app logs and is swallowed instead of crashing the process.
   - `SettlementViewModel.CompleteAsync`: navigating home after a successful payment no longer
     depends on the confirmation toast succeeding — wrapped in try/finally so a payment is never
     followed by a stuck screen for any reason.
3. **Manual test**: with the device in Airplane Mode (forces the background sync to fail every
   time), tap **Log cash payment** (and separately, **Pay via UPI**) on several helpers/months.
   Expect: "Payment recorded" toast and immediate navigation to Home every single time — no
   crash, no freeze, no stuck screen — even though the sync itself is guaranteed to fail while
   offline. Turn Wi-Fi back on afterward and pull-to-refresh on Dashboard; confirm the payments
   sync up shortly after (rows were left dirty, not lost).
4. Also repeat with normal connectivity a few times back-to-back to confirm nothing regressed
   in the success path (payment recorded, synced, navigates home, as before).

## 12. Log cash payment crash, take two — real root cause was a navigation infinite loop (bug fix)
1. The fix in section 11 above did not actually stop the crash — reproduced live on a physical
   device (SM-S948B) in Demo mode (where sync is disabled entirely, ruling out section 11's
   fix as relevant) by tapping **Log cash payment**. logcat showed a `F/mono-rt` fatal error: an
   unbounded recursive stack of identical frames ending in a StackOverflowException, which
   cannot be caught by any try/catch and kills the process outright.
2. Root cause: `AppShell.OnTabReselected` (`AppShell.xaml.cs`) is subscribed to `Shell.Navigating`
   to work around Shell not popping a tab's pushed pages when you re-tap the already-active tab.
   `SettlementViewModel.GoHomeAsync()` calls `Shell.Current.Navigation.PopToRootAsync()` directly
   after a successful payment — but `PopToRootAsync()` itself synchronously raises a new
   `Navigating` event *before* the navigation stack has actually shrunk. That re-enters
   `OnTabReselected`, which sees the same "still pushed, same target" condition and calls
   `PopToRootAsync()` again — forever.
3. Fix: `OnTabReselected` now guards against this re-entrancy with a simple `_isPoppingToRoot`
   flag, set before calling `PopToRootAsync()` and cleared in a `finally` after it completes.
   The nested `Navigating` event fired from inside `PopToRootAsync()` now sees the flag set and
   returns immediately instead of recursing.
4. **Manual test**: tap **Settle** on a helper, then **Log cash payment** (or **Pay via UPI**).
   Expect: payment recorded, immediate clean navigation back to Dashboard with the updated
   balance, no crash — repeat back-to-back on multiple helpers. Also verify the original
   tab-reselect fix still works: push a page (e.g. **Calendar**) onto a tab, then re-tap that
   same tab in the bottom bar; expect it to pop back to that tab's root screen.

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
