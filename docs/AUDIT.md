# Architecture and safety audit

Version: 2.1.0
Updated: 2026-07-24

## Outcome

The application uses exclusive Audio/MIDI ownership with explicit controls for
both modes. Opening it performs read-only diagnosis only. User-requested
Connect/Disconnect operations are serialized and each operation verifies the
final Windows state.

Normal switching is non-elevated. UAC is restricted to the optional Intel Audio
compatibility transaction or recovery of an already-disabled exact Intel node.

## Production architecture

### Audio

The app resolves exactly one preserved WU-BT10 MMDevice endpoint, follows its
topology to the Bluetooth Audio kernel-streaming filter, and requests the
driver's reconnect or disconnect property. It never treats request acceptance
as completion. Connect requires:

- an active WU-BT10 playback endpoint;
- the Classic WU-BT10 AUDIO record connected;
- endpoint volume no greater than 20.5%;
- MIDI stopped.

Disconnect requires both the endpoint inactive and Classic record disconnected.

The driver property is narrower and better tested than the historical
service-mapping and PnP reset sequences, but it is not a documented universal
application-level Windows A2DP API. Unsupported topology, zero/multiple
endpoints, or failed post-state checks stop without a broader fallback.

### MIDI

The bridge discovers live advertisements, creates fresh GATT objects for each
attempt, performs uncached discovery, follows the Android-observed initial-read
ordering, enables notifications, and forwards MIDI in both directions.

While the user explicitly leaves MIDI enabled, `GattSession.MaintainConnection`
is true to support Audio handoff and link-loss recovery. Stop sets it false,
unsubscribes, disposes GATT objects, closes WinMM, removes the temporary Windows
MIDI Services loopback, and verifies that WU-BT10 advertises again.

### Mode coordinator

- Audio -> MIDI: prepare retained GATT session, release Audio, verify Audio
  absent, then complete service discovery.
- MIDI -> Audio: stop MIDI, confirm advertisement, connect Audio, verify 20%.
- A failed Audio release aborts and cleans up MIDI.
- Concurrent UI operations are rejected/serialized.
- Closing the window cancels and waits for an active UI operation before MIDI
  disposal.

## Safety invariants

- Startup diagnosis makes no connection or mutation.
- Diagnosis never changes endpoint volume.
- Only explicit Audio Connect enforces 20%.
- Audio and MIDI may not both be accepted as active.
- Pairing is never removed.
- `BluetoothSetServiceState` is not present in production source.
- No Windows Audio/Bluetooth service restart occurs in normal or recovery code.
- No WU-BT10 A2DP or Intel PnP restart occurs in normal switching.
- No BLE address is guessed or derived from the Audio address.
- No generated Audio signal is played.
- The normal manifest is `asInvoker`.
- Compatibility mode validates exact-one inventory, requires UAC, persists the
  exact Intel identity, and verifies disable/restore state.

## Dependency audit

The production project has no third-party NuGet runtime package. It depends on
the .NET Windows Desktop shared framework in source and embeds it only at
self-contained publish time.

Test-only packages:

- Microsoft.NET.Test.Sdk 18.8.1;
- xunit.v3 3.2.2;
- xunit.runner.visualstudio 3.1.5;
- coverlet.collector 10.0.1.

Configured NuGet sources report no known vulnerable or deprecated packages.

## Packaging status

The earlier unsigned raw single-file executable is rejected after an Explorer
crash on another PC. The custom executable ICO assignment was removed. No
package is a release candidate until clean-PC shell and functional validation
passes. See `PACKAGING-INCIDENT-2026-07-22.md`.

## Evidence and open questions

The direct Audio property and coordinated handoff passed repeated cycles on the
development Intel machine. The test suite covers parsers, leases, diagnostics,
compatibility identity checks, and coordinator ordering/serialization.

Still required:

- hardware execution of the new two-button UI;
- additional Windows/Bluetooth/audio-driver families;
- sleep/resume and Bluetooth radio-off recovery;
- ARM64 hardware;
- clean-PC Explorer/package testing;
- Authenticode signing before public release;
- re-evaluation of Windows MIDI Services app-owned virtual endpoints on a
  stable runtime.

The complete version history, rejected alternatives, and current doubts are in
`DEVELOPMENT-REVIEW-2026-07-24.md`.
