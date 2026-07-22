# Changelog

## 2.0.0 - 2026-07-22

- Selected the finite BLE lifecycle after controlled hardware A/B testing and
  verified five representative connect/hold/disconnect cycles with advertising
  restored after every stop.
- Added serialized MIDI lifecycle, process-wide ownership, stale-callback
  rejection, and crash recovery for a leftover Windows MIDI Services loopback.
- Added complete BLE-MIDI framing and bidirectional forwarding, including
  channel voice, system common, real-time, running status, and SysEx.
- Added a persistent, explicit, reversible Intel Bluetooth Audio compatibility
  mode for the verified offload failure. It validates exact device inventory,
  requires UAC only to disable/restore Intel offload, records intentional state,
  and never resets profiles or Windows audio services.
- Added direct WU-BT10 playback testing without changing the Windows default
  output or endpoint volume.
- Made Audio/MIDI switching explicit: Audio stops MIDI first; MIDI refuses to
  start while the Audio endpoint is active.
- Changed diagnosis to be read-only and made the 20% volume action explicit.
- Verified three Audio reconnect/playback cycles, Audio → MIDI → Audio,
  compatibility-mode restore/re-enable, real MIDI notes, SysEx in both
  directions, forced-process recovery, and service/device restoration.

## 1.4.2-diagnostic.2 - 2026-07-22

- Added the BLE-MIDI-required initial characteristic read before enabling
  notifications, matching Android's platform BLE-MIDI connection sequence.
- Preserved the diagnostic.1 maintain-connection, retry, release, parser, and
  WMS behavior so the characteristic read can be evaluated as one A/B variable.
- Recorded the first attended hardware test and its intermittent service-query
  `Unreachable` failure.

## 1.4.2-diagnostic.1 - 2026-07-22

- Added bounded structured diagnostic sessions for BLE advertisement, GATT,
  packet, release, Audio-diagnosis, and UI lifecycle events.
- Added `GattSession.SessionStatusChanged` observation without changing the
  v1.4.1 connection or release policy.
- Added correlation IDs and diagnostic-log paths to command-line MIDI test
  results.
- Added a device-neutral diagnostic probe for verifying packaged logging without
  opening Audio or MIDI connections.
- Added a hardware protocol for distinguishing a retained Windows connection
  from Audio coexistence or an adapter firmware lock.
- Added tests for diagnostic JSON structure, rotation, retention, and disposal.

## 1.4.1 - 2026-07-21

- Kept all normal Audio, volume, diagnosis, and MIDI operations non-elevated.
- Hid the emergency device-enable action unless Windows reports the exact Intel Bluetooth Audio device as disabled.
- Added an explicit technical confirmation with **No** as the default before any UAC request.
- Removed the emergency action as the window's default Enter-key button.

## 1.4.0 - 2026-07-21

- Replaced cached/guessed BLE addresses with live BLE-MIDI advertisement discovery.
- Reacquire the BLE device, GATT session, service, and characteristic after every failed attempt.
- Increased initial connection tolerance to 60 seconds for Windows' variable unpaired-GATT timing.
- Added real link-loss detection and automatic rediscovery/resubscription while preserving the virtual MIDI port.
- Removed A2DP profile toggling, device disable/restart, and Audio Endpoint Builder restart code and APIs.
- Restricted elevated repair to enabling and verifying an already-disabled Intel Bluetooth Audio device.
- Changed Audio connection to the supported Windows Bluetooth Settings flow.
- Simplified Bluetooth enumeration and repair logging.

## 1.1.0 - 2026-07-21

- Added least-privilege elevation: UAC appears only when repair starts.
- Added a polished status-first interface and application icon.
- Added automatic diagnosis and post-repair verification.
- Added safe Intel A2DP offload fallback and guaranteed restoration.
- Added x64 and ARM64 release builds, tests, CodeQL, checksums, and optional
  Azure Artifact Signing automation.
- Added an optional, provider-neutral support link.

## 1.0.0 - 2026-07-21

- Initial proven repair for WU-BT10 AUDIO on Windows with Intel Bluetooth audio
  offload.
