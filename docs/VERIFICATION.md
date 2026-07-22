# Verification record for 2.0.0 local build

Date: 2026-07-22

## Automated and build verification

- Debug tests: 26 passed, zero failed.
- Debug and Release x64 builds: zero warnings, zero errors.
- Static source scan: no production `BluetoothSetServiceState`, pairing removal,
  device restart, Windows service restart, guessed MIDI address, automatic
  volume change, or application-wide administrator manifest.
- Compatibility-mode decision tests cover exact-one inventory requirements and
  case-insensitive exact PnP identity matching.
- Generated playback-wave tests verify stereo PCM, 48 kHz, 16-bit format and
  checked length arithmetic.
- MIDI parser tests cover channel voice, running status, real-time interleaving,
  system common, and SysEx boundaries.
- Single-instance and diagnostic rotation/retention tests pass.

## BLE-MIDI hardware verification

- Real note traffic was received and the UI counter increased.
- Vibraphone connection/disconnection tones occurred on connect/stop.
- The indicator continued flashing during a working Windows MIDI link; this is
  therefore not, by itself, a failure signal on this adapter.
- Bidirectional WinMM loopback passed for short and long/SysEx messages.
- A six-byte MIDI Identity Request traversed the application output path and was
  written to the BLE characteristic.
- Five representative cycles passed with a five-second connected dwell and a
  45-second release window. Every Stop restored BLE advertising. Evidence:
  `TestResults/production-realistic-5-cycles.json`.
- A deliberately aggressive zero-dwell, 15-second-release stress run stopped on
  cycle two because advertising returned after the short window. Clean CCCD
  unsubscribe completed in 28 ms and no API error occurred. This informed the
  representative dwell/release acceptance test rather than being hidden.
- Forced-process recovery passed: a subsequent process reused the stale
  loopback safely, connected on its first attempt, removed the endpoint, and
  left Windows MIDI Services responsive.

## Audio root-cause evidence

Before compatibility mode:

- Windows Settings and the Classic record reported Connected.
- The WU-BT10 A2DP MEDIA node was present, started, and `CM_PROB_NONE`.
- Intel Smart Sound Technology for Bluetooth Audio was present, started, and
  `CM_PROB_NONE`.
- No present WU-BT10 AudioEndpoint existed. Event 65 showed unplugged/not-present
  states but never active.
- Device scan, exact WU A2DP restart, exact Intel restart, and Audio Endpoint
  Builder restart did not create playback.
- A guarded historical profile-mapping rebuild created one active endpoint, but
  after disconnect two normal reconnects (including a ten-second dwell and
  65-second observation) returned only Connected, not Connected audio. The
  endpoint never became active.

With Intel offload disabled:

- Windows normal Connect changed to Connected audio and created the WU-BT10
  playback endpoint in about four seconds.
- Three normal disconnect/reconnect cycles completed, and a generated test
  signal completed through the exact WU-BT10 render device each time.
- Audio → MIDI → Audio passed: Audio endpoint removed, MIDI connected/stopped,
  BLE advertising returned, Audio reconnected, and targeted playback completed.
- Restoring Intel through the product re-enabled the node, removed the mode
  record, and reproduced the missing endpoint after the next reconnect.
- Enabling compatibility through the product disabled and verified the exact
  Intel node, saved the record, and restored playback after the supported
  Windows reconnect.
- Two consecutive diagnoses reported the same 54% volume before/after; diagnosis
  and playback testing made no volume mutation.
- The GUI Test sound action completed and left both Test sound and explicit 20%
  controls enabled.
- The GUI MIDI action displayed the Audio-active refusal and did not acquire BLE.

## Post-test state

- Audio compatibility mode is intentionally active and recorded.
- Intel Bluetooth Audio reports `CM_PROB_DISABLED` by design; the product shows
  **Restore Intel default mode** and the restoration path has been verified.
- WU-BT10 playback endpoint is active.
- Audio Endpoint Builder, Windows Audio, Bluetooth AVCTP, and Windows MIDI
  Services were confirmed running after elevated tests.
- Local binaries are development artifacts and are not Authenticode signed.
