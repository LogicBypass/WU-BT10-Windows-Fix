# Verification record for 2.1.0 development build

Date: 2026-07-24

## Completed

- Release build: zero warnings and zero errors with warnings treated as errors.
- Automated tests: 33 passed, zero failed.
- Five new coordinator tests cover both transition orders, abort-on-failed Audio
  release, MIDI-release advertisement verification, and serialization.
- NuGet audit: no known vulnerable or deprecated packages from configured
  sources.
- Static source check: no `BluetoothSetServiceState`, pairing removal, guessed
  BLE address, Windows service restart, WU-BT10 PnP restart, generated Audio
  signal, or administrator application manifest.
- Live UI inspection found separate enabled **Connect Audio** and
  **Connect MIDI** controls plus the separate advanced recovery control.
- Startup trace recorded `startup-read-only-diagnosis`; no Audio or MIDI
  connection operation was initiated.
- MIDI command-line hardware probe connected and released successfully.
- One maintained-session MIDI cycle received one packet/message, sent three
  low-velocity self-test messages, restored advertising, and left the MIDI
  service ping healthy.
- Live UI **Connect MIDI** created the two expected loopback sides; live UI
  **Disconnect MIDI** removed them, returned the button to Connect, and the app
  closed cleanly. Endpoint enumeration then showed only the Windows default
  loopback and no active MIDI sessions.
- The supplied Casio connection guide and MIDI implementation were re-read and
  visually inspected at the relevant Audio/MIDI coexistence and notification
  tone pages.

## Hardware state at the verification pause

Read-only diagnosis reported:

- Intel Audio compatibility mode active;
- no currently visible WU-BT10 AUDIO record;
- MIDI stopped.

This is a valid disconnected startup state, but it does not complete hardware
acceptance.

## Pending hardware acceptance

- Audio Connect/Disconnect from the new Audio button.
- Additional real application traffic through the new MIDI button, including
  pedals, controllers, and SysEx.
- Automatic Audio -> MIDI and MIDI -> Audio transitions.
- Five alternating cycles with 20% verification and restored BLE advertising.
- Close, process-kill, power-cycle, radio-off/on, and sleep/resume recovery.

## Pending packaging acceptance

- Clean self-contained x64 publish without the custom executable icon.
- Explorer enumeration/properties/context-menu testing on a clean second PC.
- Standard-user launch and full functional cycle on that PC.
- Defender/SmartScreen evidence.
- Valid Authenticode signature for a public release.

Until these pending items pass, 2.1.0 is a development build, not a production
release.
