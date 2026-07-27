# Development review and next-cycle plan

Date: 2026-07-24
Target version: 2.1.0

## Requested outcome

The application must open without connecting anything and show two independent
sections:

- Audio with Connect/Disconnect;
- MIDI with Connect/Disconnect.

Connecting either mode must detect and cleanly release the other mode before it
continues. Normal use should remain possible without administrator rights.

## Repository and history review

The review covered every tracked source, test, workflow, audit, handover record,
and dependency plus every commit from the v1.4.1 audit baseline through
`50d033e`.

### v1.0-v1.3: broad repair experiments

Early Audio recovery mixed A2DP service-mapping removal/reinstallation, Intel
offload disable/enable, PnP restarts, and Windows service restarts. It produced
a playback endpoint once and established that bypassing Intel offload mattered,
but it changed too many variables at once. It also treated command exit codes
as final device state, which produced false failure reports.

These approaches remain rejected. Microsoft's documentation says
`BluetoothSetServiceState` installs or removes the driver mapped to a Bluetooth
service; it is not a normal Connect/Disconnect command.

### v1.4 diagnostic series: isolate BLE-MIDI

The project replaced guessed/cached BLE identity with live advertisement
discovery, added structured traces, reacquired GATT objects after failures, and
made teardown observable. The Android-inspired initial characteristic read was
tested independently and retained because it improved the observed connection
sequence. It is not described as a BLE-MIDI specification requirement.

Five-cycle A/B tests showed that finite uncached discovery without
`MaintainConnection` released reliably and was faster than an indefinitely
maintained session. Lifecycle serialization, stale-callback rejection, and a
single-instance lease then passed ten additional cycles.

### v2.0 architecture: narrow and reversible

The Audio investigation separated:

1. the Classic Bluetooth record;
2. the WU-BT10 A2DP PnP node;
3. the MMDevice playback endpoint.

The Intel machine could report the first two as connected and healthy while the
third was absent. PnP and service restarts did not repair it. Keeping the exact
Intel Bluetooth Audio offload node disabled allowed Windows' standard A2DP path
to create a repeatable endpoint. That became an explicit, persisted, reversible
compatibility mode.

BLE-MIDI gained bidirectional WinMM forwarding, complete message framing,
ordered writes, crash recovery for a stale loopback, and automatic link-loss
recovery. Microsoft's application-owned virtual MIDI endpoint was tested and
rejected because the installed release-candidate runtime hung during teardown
and could leave the MIDI service unresponsive.

### `50d033e`: direct one-button switching

The last version replaced Settings with a narrow Audio driver request:

- `KSPROPSETID_BtAudio` reconnect on property 0;
- `KSPROPSETID_BtAudio` disconnect on property 1.

It reached the Bluetooth filter through the preserved endpoint topology and
verified endpoint/transport state after the request. Ten coordinated hardware
cycles passed. To preserve BLE availability while Audio was released, the MIDI
path set `GattSession.MaintainConnection = true` before the Audio disconnect.

The interaction was nevertheless wrong for the current requirement: the
visible Audio button became a context-sensitive one-button mode switch, while
an older MIDI tab/card implementation remained in `MainForm.cs` but was never
added to the window.

## Audit findings corrected in 2.1

### 1. Dead and contradictory UI

`BuildMidiPage` and several MIDI controls existed but were unreachable. The
visible control said Connect Audio, Switch to MIDI, or Switch to Audio depending
on state. Documentation still described an even older Settings-based workflow.

Correction: the form was rebuilt around two simultaneously visible Audio and
MIDI cards. Dead MIDI-page code, the unused support control, and the overloaded
primary toggle were removed.

### 2. Incomplete Audio-connected detection

The prior Audio-to-MIDI callback checked only whether the playback endpoint was
active. Windows can report the Classic Audio transport connected while no
endpoint exists. In that condition, MIDI could continue without releasing
Audio, contradicting both the product invariant and Casio's instability warning.

Correction: `IsAudioConnectionPresent` now treats either an active WU-BT10
endpoint or a connected Classic WU-BT10 AUDIO record as Audio ownership. If the
transport is connected but the preserved endpoint cannot be uniquely resolved,
the targeted disconnect fails safely and MIDI startup is aborted.

### 3. UI operations could race

Only the clicked button was disabled. With two controls, simultaneous click
handlers could interleave Stop, Connect, and post-state presentation.

Correction: `ConnectionCoordinator` serializes all mode operations. The form
also serializes user actions, disables both mode buttons during transitions,
passes a close-cancellation token, and waits for the active action before
disposing MIDI.

### 4. A request was treated too close to success

The driver property call itself only reports that the request was accepted.

Correction: every coordinator path now checks the final mode:

- Audio connect requires Audio present and MIDI stopped;
- Audio disconnect requires Audio absent;
- MIDI connect requires MIDI running and Audio absent;
- MIDI disconnect requires MIDI stopped and restored BLE advertising.

The underlying Audio controller already polls endpoint plus Classic transport;
the coordinator adds the cross-mode invariant.

### 5. Startup intent was unclear

Startup performed diagnosis, but the one-button labels and previous
documentation made it appear that the app might initiate Audio.

Correction: startup is explicitly logged as `startup-read-only-diagnosis`.
`Shown` calls only `RepairEngine.Diagnose` and connection-state enumeration.
No Audio or MIDI connect method is called.

### 6. Dependency and packaging hygiene

The production application has no third-party NuGet runtime packages. The test
project used deprecated xUnit v2 and older test/coverage packages.

Correction:

- xUnit migrated to `xunit.v3` 3.2.2;
- Microsoft.NET.Test.Sdk updated to 18.8.1;
- coverlet.collector updated to 10.0.1;
- all packages report no known vulnerability or deprecation from configured
  NuGet sources.

The custom nine-frame executable icon was also removed from the project
resource because the raw unsigned single-file build crashed Explorer on another
PC. The in-app PNG logo remains.

### 7. First-time Audio connection still depended on Settings

The preserved-endpoint controller correctly refused to mutate Bluetooth when no
endpoint existed, but that meant a new PC could not satisfy the one-button
requirement. Windows could show WU-BT10 AUDIO under New devices while the app
reported zero endpoints.

Correction: Connect Audio now resolves the exact Classic Bluetooth address,
uses Microsoft's `DeviceInformation.Pairing.PairAsync`, waits for Windows to
create exactly one endpoint, then follows the existing silent reconnect and 20%
verification path. Windows still shows its mandatory Allow dialog on first
pairing, but the app does not open Settings. Two attended attempts showed an
immediate generic `Failed` result on the first API call and the correct system
dialog on the second. The implementation therefore retries once only when the
generic failure occurs in under two seconds; it never retries authentication
failure, cancellation, or a delayed user-facing failure.

## Decisions retained, with explicit doubt

### Direct Audio driver property

Why retained:

- it completed repeated hardware Audio and coordinated transition cycles;
- it changes neither pairing nor service-to-driver mapping;
- it does not restart PnP devices or Windows services;
- it runs as a standard user;
- it has an exact endpoint precondition and authoritative post-state checks.

Why it is not considered universally proven:

- Microsoft does not document it as a stable public application-level A2DP
  Connect API;
- availability depends on the Windows Bluetooth Audio topology and filter;
- it has been hardware-tested on one Intel/Windows family, not the complete
  Windows driver matrix.

Release consequence: unsupported or ambiguous systems must fail without
fallback mutation. Cross-PC qualification is mandatory.

### `GattSession.MaintainConnection`

Why retained for the interactive bridge:

- Microsoft documents it as the way to ask Windows to maintain a GATT
  connection indefinitely while the peripheral is available;
- setting it before Audio release made the exclusive Audio-to-MIDI handoff
  repeatable;
- it supports automatic recovery while the user explicitly leaves MIDI on;
- Stop always sets it false and disposes the session.

What superseded the earlier finite-policy conclusion:

The finite policy was better for standalone connect/dwell/disconnect tests, but
those tests did not include acquiring BLE before releasing an active A2DP link.
The interactive bridge now has a different requirement: retain ownership only
for the duration of the user's explicit MIDI mode.

Remaining doubt: the maintained and finite policies should be re-compared on
additional Bluetooth chipsets, including sleep/resume and radio-off recovery.

### Windows MIDI Services loopback

Why retained:

- the tested RC application-owned endpoint API could hang during teardown;
- the loopback path passed bidirectional short and SysEx traffic, process-kill
  recovery, endpoint removal, and service health checks.

Remaining doubt: Microsoft's application-owned virtual endpoint should be
retested when a stable SDK/runtime is installed. It would remove the external
`midi.exe` process and give stronger ownership semantics if teardown becomes
reliable.

### 20% Audio endpoint limit

Why retained:

- the user explicitly required speaker protection;
- the app attempts to set the endpoint before accepting it as connected and
  verifies a value no greater than 20.5%.

Important distinction: Casio's rising/falling connection notification tones are
generated by the piano and have their own 0-10 notification volume setting.
Windows endpoint volume cannot make those firmware tones clean, quieter, or
louder.

## Official-document alignment

The supplied Casio connection guide says:

- Audio and BLE-MIDI are selected independently;
- the BLE-MIDI connection is performed each time;
- simultaneous Audio and BLE-MIDI can make MIDI unstable, in which case Audio
  should be disconnected;
- rising/falling piano melodies indicate Audio connect/disconnect;
- rising/falling vibraphone melodies indicate BLE-MIDI connect/disconnect;
- a lit indicator means connected, but observed working Windows MIDI traffic
  with a flashing indicator makes the indicator supplementary evidence on this
  adapter.

The supplied MIDI implementation confirms the channel voice, controller,
program change, pitch bend, real-time, Active Sensing, and SysEx message classes
that the bridge must preserve. Existing parser tests cover framing boundaries;
hardware qualification still needs representative controllers, pedals, Active
Sensing, Program Change, and longer SysEx.

## Verification completed

- Release build with analyzers as errors: zero warnings, zero errors.
- Automated tests: 49 passed in Debug and Release, zero failed.
- New coordinator tests verify:
  - MIDI Stop -> advertisement -> Audio Connect ordering;
  - BLE session preparation -> Audio Disconnect -> MIDI completion ordering;
  - MIDI abort when Audio cannot be released;
  - MIDI Disconnect -> advertising verification;
  - operation serialization.
- NuGet vulnerability scan: none reported.
- NuGet deprecation scan: none reported.
- Live UI inspection:
  - separate Connect Audio and Connect MIDI controls are visible and enabled;
  - advanced recovery remains separate;
  - startup diagnosis showed no Audio or MIDI connection attempt.
- MIDI hardware:
  - short connection/release probe passed;
  - maintained-session self-note cycle received one packet/message and sent
    three messages;
  - advertising returned and the Windows MIDI Services ping remained healthy;
  - live UI Connect created the expected bridge endpoints and live UI
    Disconnect removed them and returned to Connect.
- Audio hardware:
  - exact unpaired device address resolved without Settings (address redacted);
  - Windows pairing completed and created exactly one playback endpoint;
  - real external Audio played through the piano;
  - the endpoint and Classic transport were active at a verified 20%;
  - Audio -> MIDI disconnected only Audio, subscribed BLE-MIDI, and received
    31 live messages;
  - MIDI -> Audio unsubscribed GATT, disabled `MaintainConnection`, removed the
    temporary loopback, observed advertising, and reconnected Audio at 20%.
  - five complete alternating Audio -> MIDI -> Audio cycles passed through the
    release UI. The closing release-session scan contained 389 events with
    no failure/error event; every Audio reconnect completed with one active
    endpoint at 20%, and every MIDI release removed its temporary loopback.

## Remaining development cycle

### Planning gate

Do not publish another user-test executable until both hardware and packaging
gates pass.

### Integration tests on the current piano

1. Start with both modes disconnected.
2. Open the app and verify no connection tone and no new transport.
3. Audio Connect, verify endpoint active and volume at 20%. **Passed once.**
4. Audio Disconnect, verify falling piano tone and Audio absent. **Passed in
   the automatic handoff.**
5. MIDI Connect, exchange notes in both directions. **Connection and inbound
   traffic passed; repeat representative outbound traffic.**
6. MIDI Disconnect, verify falling vibraphone tone, endpoint removal, and
   advertising restored. **Passed in the reverse handoff.**
7. Audio Connect -> MIDI Connect automatic handoff. **Passed in five complete
   alternating cycles.**
8. MIDI Connect -> Audio Connect automatic handoff. **Passed in five complete
   alternating cycles.**
9. Run five complete alternating cycles with a representative dwell. **Passed;
   final state was Audio connected at a verified 20%, with MIDI stopped.**
10. Close and forced-kill recovery, power cycle, Bluetooth radio off/on,
    sleep/resume, and failure rollback.

### Packaging tests

1. Publish a clean, self-contained x64 build without the custom executable icon.
2. Verify PE metadata, hash, Authenticode state, and absence of extra output.
3. Copy it to a clean second PC without elevation.
4. Leave it in Desktop and Downloads while Explorer shows Details, Large icons,
   Properties, and context menu.
5. Confirm Explorer and the shell property/icon handlers do not crash or stall.
6. Launch as a standard user and complete diagnosis plus both mode transitions.
7. Repeat with Defender enabled and capture SmartScreen/reputation behavior.
8. Prefer a signed release; do not label an unsigned local build production.

The icon-free x64 candidate completed steps 1-2 locally on 2026-07-24, plus
standard-user copied-file launch, Shell metadata/icon extraction, read-only
diagnosis, GUI startup/close, and a current Microsoft Defender scan. It remains
explicitly unsigned and marked `TEST`; clean-PC steps 3-7 are now the active
gate.

Direct placement on removable FAT32 drive `E:` then reproduced the access
failure. Defender event 1121 attributed it to ASR rule
`B2B3F03D-6A65-4F7B-A9C7-1C7EF74A9BA4`, which blocks untrusted and unsigned
USB-origin executables. The validated hash and permissions were unchanged and
no Explorer Application Error event was recorded. A ZIP transport round-trip
through `E:` followed by local extraction preserved the EXE hash and launched
successfully. Multi-PC tests must use that ZIP workflow until the executable
has a trusted Authenticode signature.

### Improvement analysis

After the cross-PC soak, classify every failure by layer:

- shell/package;
- Classic Bluetooth record;
- A2DP PnP node;
- MMDevice endpoint/volume;
- BLE advertisement;
- GATT session/service/characteristic;
- Windows MIDI Services loopback;
- UI coordination.

Only one variable should change per next diagnostic cycle. Every accepted
change needs a precondition, a bounded action, a final state assertion, clean
rollback or safe stop behavior, and a regression test.

## Primary references

- Supplied Casio `PXS1100_connectionguide_EN.pdf`, especially EN-1, EN-2, and
  EN-4.
- Supplied Casio `PXS1100_midiimplementation_EN.pdf`.
- [Microsoft BluetoothSetServiceState documentation](https://learn.microsoft.com/en-us/windows/win32/api/bluetoothapis/nf-bluetoothapis-bluetoothsetservicestate)
- [Microsoft Bluetooth Classic Audio architecture](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio)
- [Microsoft Bluetooth GATT client lifecycle](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client)
- [Microsoft device pairing](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/pair-devices)
- [BluetoothDevice.FromBluetoothAddressAsync](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothdevice.frombluetoothaddressasync)
- [GattSession.MaintainConnection](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattsession.maintainconnection)
- [Windows MIDI Services compatibility matrix](https://microsoft.github.io/MIDI/kb/api-back-compat/)
- [.NET single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
