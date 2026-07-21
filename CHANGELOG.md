# Changelog

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
