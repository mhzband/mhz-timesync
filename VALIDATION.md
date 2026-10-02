# Validation — 2026-09-17

Release: MHZ TimeSync 0.5.0, Windows x64, .NET SDK 10.0.401.

v0.5.0: Release build succeeded with zero warnings/errors and all 17 regression groups passed. The self-contained single-file EXE was launched for UI smoke; no clock correction was approved. The rendered main window measured 1040×700 and showed the complete header, measurement/status area, station controls, decode grid and debug log without crossing the 1366×768 host working area. The 780×620 About dialog was rendered and inspected. Logo assets loaded from embedded resources; title bar, tray and published EXE use the multi-resolution ICO. Published metadata reports File Version 0.5.0.0. Multi-monitor DPI and Windows 10 still require field acceptance testing.

v0.4.0: main-window UI smoke verifies the About dialog title and assembly-derived diagnostic version. The published EXE About dialog was rendered separately and visually inspected for bilingual content, version, developer, contact links, privacy, copyright, runtime information and controls. Link targets are fixed HTTPS URLs; physical browser launch and clipboard interaction remain manual acceptance checks. EXE metadata version is checked after publish.

v0.3.0: 17 regression groups passed, including single-decode manual eligibility/correction sign and stale/replay/WAV/quality/range rejection. Published EXE UI smoke opens the station context menu with a synthetic row, checks that the action is enabled without consensus, inserts a new row and verifies the original target remains captured, then checks TX and changed-context interlocks. No manual confirmation was approved and no clock change was performed. Actual correction/UAC still requires the isolated-machine acceptance test. The new manual override intentionally bypasses the multi-station minimum only for explicit right-click actions.

v0.2.0 verification: 15 regression groups passed (the original 13 plus selected-station filtering/case/deduplication/single-station guards and atomic selection validation/reset). Published EXE UI smoke exercised Use selected, Use all, Hide to tray and restore, then exited with code 0. Inspected the rendered window including clickable HS9XKG credit and selection controls. No clock adjustment was performed. Browser launch on credit click and tray interaction via physical mouse remain manual checks; smoke exercised the same hide/restore handlers programmatically.

Baseline v0.1.0 verification retained below (not a claim that on-air tests were performed):

Completed on the available Windows host:

- Release build and self-contained single-file publish: succeeded; zero compiler warnings/errors.
- Console regression suite: 13 groups passed. Includes 1,000 malformed packets plus every truncated prefix of a valid Decode, schema and numeric rejection, Status frequency/Tx flags, sender identity, single-station guard, MAD outliers, duplicates/replay/playback, expiry, conflicting stations, slot diversity, thresholds/auto gates and four-timestamp NTP validation including 2040 era.
- Read-only live NTP query to time1.nimt.or.th: succeeded with 3 samples; selected RTT approximately 11.6 ms and correction +0.373 s at the time of the test. This is a transient observation, not a calibration result.
- Started the published EXE, rendered the actual WinForms controls, and exited with code 0. Inspected the image and corrected header clipping.
- Sent 30 synthetic Decode datagrams through actual IPv4 UDP to 127.0.0.1:2237 while the published UI was running. Observed 30 samples, 25 valid, 5 MAD rejected, 5 valid stations, 5 slots, median +0.730 s, spread 0.040 s, HIGH. The current source remained NTP because AUTO correctly preferred its valid NTP result. No Status packet was supplied and Correct Clock was disabled.
- Preview image contains **synthetic FT8 data and a real read-only NTP measurement**; it is not an on-air capture.
- No Windows clock changes, UAC elevation of the app, time-service changes or radio commands were performed during validation.

Still requires field/user acceptance testing:

- Real WSJT-X/radio audio chain, measured correction direction and residual bias, actual UAC cancellation/success, and successful SetSystemTime on the target account/policy.
- AUTO fallback across a physically disconnected/reconnected network, all five NTP servers individually, long-running Auto Correct and 5-minute cooldown on an isolated test machine.
- Windows 10 and Windows 11 independently; ARM64 and multiple monitor DPI settings. The x64 build is not a claim that every Windows version/scaling combination was tested.
- Code signing / installer / update service are outside this MVP.

Run the acceptance procedure in README.md before enabling unattended correction.
