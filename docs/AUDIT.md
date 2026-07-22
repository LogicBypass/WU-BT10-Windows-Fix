# Architecture, history, and safety audit (2.0.0)

Date: 2026-07-22

## Outcome

The reliable design is exclusive Audio/MIDI ownership plus a finite BLE-MIDI
lifecycle. On the audited Intel Smart Sound machine, reliable Audio additionally
requires a persistent but reversible compatibility mode that disables only the
Intel Bluetooth-audio offload node so Windows uses its standard A2DP path.

Normal diagnosis, playback testing, switching, and MIDI need no elevation. UAC
is used only to enable or restore compatibility mode, or to recover an
independently disabled Intel device.

## Version evolution and lessons

### 1.0–1.1: a repair that worked once but was too broad

The first successful Audio repair toggled the WU-BT10 A2DP service mapping,
temporarily disabled Intel offload, optionally restarted Audio Endpoint Builder,
then re-enabled Intel. It proved that bypassing Intel allowed Windows to create
a standard WU-BT10 playback endpoint.

The implementation mixed several variables, automatically changed volume, and
treated every nonzero `pnputil` exit as failed restoration. One observed exit 50
said the device was already enabled, so the urgent failure report was false.
Repeated service-mapping toggles also returned errors 87 and 1168.

Microsoft documents that `BluetoothSetServiceState` installs or removes the
mapped device driver. It is not a normal A2DP Connect API:
[BluetoothSetServiceState](https://learn.microsoft.com/en-us/windows/win32/api/bluetoothapis/nf-bluetoothapis-bluetoothsetservicestate).

### 1.3: more recovery steps increased instability

Connect and repair paths repeated profile toggles, device restarts, Intel
disable/enable, and service restarts. A failed sequence could leave Intel
Bluetooth Audio disabled. MIDI used guessed/cached identity, rapid GATT retries,
no robust ownership, and incomplete teardown.

The main lesson was transactional: a recovery action must have narrow
preconditions, one purpose, authoritative post-state checks, and a guaranteed
rollback. Command exit codes alone are not device state.

### 1.4 diagnostic series: isolate variables

Disruptive Audio mutations were removed while BLE-MIDI was instrumented. Live
advertisements replaced address arithmetic. GATT objects were reacquired after
failures, disconnect events became observable, and structured correlation logs
made connection/release timing measurable.

Hardware A/B testing showed that the finite policy without persistent
`MaintainConnection` ownership released predictably. The initial MIDI
characteristic read was not required for sustained traffic. Five representative
cycles with a five-second dwell all re-advertised after Stop.

### 2.0: retain what is repeatable

MIDI now serializes lifecycle changes, rejects stale callbacks, owns only one
bridge process, forwards both directions, removes its temporary loopback, and
recovers from link loss.

Audio analysis separated three Windows layers:

1. the Classic Bluetooth record could say Connected;
2. the WU-BT10 A2DP MEDIA node could be present, started, and `CM_PROB_NONE`;
3. the MMDevice playback endpoint could still be absent.

PnP scan, exact A2DP restart, exact Intel restart, and Audio Endpoint Builder
restart did not create playback. A guarded historical mapping rebuild created
one active endpoint, but it disappeared on disconnect and did not return. That
proved the one-shot repair was not a durable architecture.

With Intel offload left disabled, normal Windows Connect created the standard
playback endpoint in seconds. Three Audio reconnect/playback cycles and the full
Audio → MIDI → Audio transition passed. Restoring Intel reproduced the missing
endpoint; enabling compatibility mode again restored repeatable behavior.

Therefore 2.0 removes the service-mapping reset from production and records the
intentional Intel-disabled state as a reversible compatibility mode.

## Current safety invariants

- Audio diagnosis is read-only.
- Test sound targets WU-BT10 and does not change the default output or endpoint
  volume.
- Volume changes only after the explicit 20% button.
- MIDI cannot start while the WU-BT10 playback endpoint is active.
- Switching to Audio stops MIDI and releases BLE before opening Settings.
- No profile service-state call, pairing removal, Windows service restart,
  device restart, or guessed BLE address exists in production source.
- Compatibility enable requires exactly one healthy Intel Bluetooth Audio node
  and one healthy WU-BT10 A2DP node.
- Compatibility restore validates the recorded `INTELAUDIO\` instance against
  the current device inventory.
- Intentional compatibility state persists in a per-user JSON record and is
  cleared only after verified Intel restoration.
- The normal application manifest is `asInvoker`.

## Windows and Casio constraints

Windows Classic Audio uses A2DP for stereo playback and creates the corresponding
output endpoint; Windows 11 unifies endpoint presentation where HFP is also
supported:
[Bluetooth Classic audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio).

The supported Windows troubleshooting path is Settings-based reconnect,
unpair/re-pair, driver update, or restart—not repeated service mapping mutation:
[Microsoft Bluetooth connected/no sound guidance](https://support.microsoft.com/en-US/Windows/Hardware/Bluetooth/fix-bluetooth-connected-but-no-sound-issue-on-windows).

Casio documents Audio and MIDI as separate connection modes and warns that
simultaneous use may be unstable. The application therefore enforces exclusive
mode transition rather than trying to maintain both links.

## Windows MIDI Services decision

Windows MIDI Services supplies the temporary loopback endpoint and supports
64-bit x64 and ARM64 on the latest supported desktop Windows 11. Its current
matrix still lists Bluetooth MIDI 1.0 transport as after the initial release, so
the application must retain its own BLE transport:
[minimum requirements](https://microsoft.github.io/MIDI/kb/minimum-requirements/),
[compatibility matrix](https://microsoft.github.io/MIDI/kb/api-back-compat/).

## Remaining limitations

- Windows Settings remains necessary for supported Classic Audio
  connect/disconnect.
- Compatibility mode affects Intel Bluetooth audio offload system-wide, not
  only WU-BT10; the UI discloses this before UAC and provides restoration.
- ARM64 artifacts can be built on x64 but require an ARM64 machine for hardware
  execution.
- Public releases should be Authenticode signed; local artifacts remain
  development builds until signing credentials are configured.
