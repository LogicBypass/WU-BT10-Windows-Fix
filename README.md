# WU-BT10 Windows Fix

An independent Windows utility for switching a Casio WU-BT10 between Bluetooth
Audio and bidirectional BLE-MIDI.

- Starts disconnected from the application's point of view: opening the app
  performs read-only diagnosis and never initiates Audio or MIDI.
- Presents separate **Audio** and **MIDI** sections with one Connect/Disconnect
  control for each.
- Enforces exclusive ownership. Connecting one mode cleanly releases the other
  mode first and verifies the resulting Windows state.
- Discovers and pairs WU-BT10 Audio inside the Connect Audio workflow, without
  opening Settings, then connects the preserved endpoint and verifies 20%.
- Bridges BLE-MIDI in both directions through a temporary Windows port named
  **WU-BT10 MIDI Bridge**.

> This community project is not affiliated with or endorsed by Casio. WU-BT10
> and Casio are trademarks of their respective owner.

## How to use it

Pair **WU-BT10 AUDIO** with Windows once. Install
[Windows MIDI Services Runtime and Tools](https://microsoft.github.io/MIDI/get-latest/)
if MIDI is required.

When the app opens, it only reports the existing state:

- Press **Connect Audio** to use the piano as a Bluetooth speaker.
- Press **Connect MIDI** to use the keyboard with a MIDI application.
- Press the active mode's **Disconnect** button to release it.

The first Audio connection requires the piano's Audio pairing standby
(FUNCTION + C-sharp 1). Windows shows its mandatory **Allow** confirmation once;
later connections reuse the pairing without Settings or another confirmation.

If Audio is connected when **Connect MIDI** is pressed, the app prepares a BLE
session first, disconnects only the Audio profile, and then completes MIDI
service discovery. If MIDI is connected when **Connect Audio** is pressed, the
app stops the bridge, waits for WU-BT10 to advertise again, and then connects
Audio. This follows Casio's warning that simultaneous Audio and BLE-MIDI can
make MIDI unstable.

Select **WU-BT10 MIDI Bridge** as both input and output in the MIDI application.
Keep this utility open while using MIDI. The utility does not need to remain
open for Audio playback.

## Safety model

Normal diagnosis and switching run as the current standard user. No
administrator access is requested for:

- read-only diagnosis;
- Audio connect/disconnect;
- the 20% Audio endpoint limit; or
- MIDI connect, forwarding, recovery, and disconnect.

Normal switching does not remove pairing, toggle the A2DP service mapping,
restart a Windows service, restart a PnP device, open Settings, or play a
generated test signal.

Audio switching sends the reconnect/disconnect property understood by the
Windows Bluetooth Audio kernel-streaming filter reached through the preserved
MMDevice topology. This property is used narrowly because Windows does not
publish a general Win32 A2DP Connect API for applications. The request is not
treated as proof of success: the app requires exactly one preserved WU-BT10
endpoint and verifies the endpoint and Classic Bluetooth transport after every
operation. Unsupported or ambiguous systems fail without changing pairing or
profile registration.

### Intel Audio compatibility mode

On the audited Intel Smart Sound machine, Intel Bluetooth-audio offload could
report WU-BT10 connected without creating a playback endpoint. The optional,
persistent compatibility mode:

1. requires exactly one healthy Intel Bluetooth Audio offload node and one
   healthy WU-BT10 A2DP node;
2. requires explicit confirmation and UAC;
3. disables only that exact Intel offload node and verifies the result; and
4. records the exact identity so it can be restored later.

This setting affects Bluetooth Audio offload system-wide and persists across
restarts. **Restore Intel default** re-enables and verifies the recorded node.
Normal Audio/MIDI switching remains non-elevated.

## Implementation notes

The MIDI bridge discovers live WU-BT10 BLE advertisements; it never derives a
MIDI address from the Audio address. It performs uncached service and
characteristic discovery, follows the initial-read ordering observed in
Android's BLE-MIDI implementation, subscribes to notifications, and uses
`GattSession.MaintainConnection` only while the user has explicitly enabled the
MIDI bridge. Stop sets `MaintainConnection` to false, unsubscribes, disposes all
GATT objects, closes WinMM, and removes the temporary loopback.

Windows MIDI Services currently provides the loopback endpoint. Microsoft's
native BLE-MIDI 1.0 transport remains listed as a post-initial-release feature,
so this app retains its own BLE transport.

## Requirements

- 64-bit Windows 11 on x64 for the currently tested build.
- Windows MIDI Services Runtime and Tools for MIDI.
- Casio PX-S1100 with WU-BT10.
- No separate .NET installation for the eventual self-contained build.

ARM64 source support remains, but no ARM64 package is released until hardware
execution passes on an ARM64 PC.

## Privacy and local state

- No telemetry, account, updater, service, startup entry, or tray process.
- Network access occurs only when the user opens the Windows MIDI Services
  download link.
- Structured diagnostics:
  `%LOCALAPPDATA%\WU-BT10-Fix\diagnostics` (2 MiB per file, 20 files maximum).
- Repair log: `%LOCALAPPDATA%\WU-BT10-Fix\repair.log`.
- Compatibility record:
  `%LOCALAPPDATA%\WU-BT10-Fix\audio-compatibility-mode.json`.

MIDI payload bytes are not written to diagnostics.

## Build and test

Install the SDK selected by `global.json`, then run:

```powershell
dotnet restore WUBT10Fix.slnx
dotnet build WUBT10Fix.slnx -c Release --no-restore -warnaserror
dotnet test WUBT10Fix.slnx -c Release --no-build
```

The project intentionally has no third-party production NuGet dependency. Test
dependencies are checked for vulnerabilities and deprecation. See
[docs/DEVELOPMENT-REVIEW-2026-07-24.md](docs/DEVELOPMENT-REVIEW-2026-07-24.md)
for the version-by-version review and
[docs/SIGNING.md](docs/SIGNING.md) for release requirements.

## Packaging status

The previous unsigned, custom-icon, 178 MiB raw single-file executable is
rejected because its presence crashed Explorer on another PC. It is not a
release candidate. The custom executable icon resource has been removed from
the build while the in-app PNG remains. A new executable will be handed off
only after Explorer enumeration, launch, signature/reputation behavior, and
functional switching pass on a clean second PC.

## License

[MIT](LICENSE)
