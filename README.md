# WU-BT10 Windows Fix

An open-source Windows utility for Casio WU-BT10 Bluetooth Audio and
bidirectional BLE-MIDI.

- Diagnoses the **Headphones (WU-BT10 AUDIO)** endpoint without changing it.
- Plays a short test signal directly to WU-BT10 without changing the default
  Windows output.
- Offers a tested, persistent, and reversible Intel Audio compatibility mode
  when Intel offload connects without creating playback.
- Bridges WU-BT10 BLE-MIDI in both directions through a temporary Windows port
  named **WU-BT10 MIDI Bridge**.
- Detects BLE link loss, reacquires fresh GATT objects, and reconnects while the
  bridge remains enabled.

![WU-BT10 Windows Fix interface](assets/screenshot.png)

> This independent community project is not affiliated with or endorsed by
> Casio. WU-BT10 and Casio are trademarks of their respective owner.

## Safety model

Diagnosis, test sound, volume inspection, Audio/MIDI switching, and the MIDI
bridge run as a standard user. Diagnosis never changes volume; **Set volume
20%** is a separate explicit action.

Windows does not expose a supported public desktop API for reliably connecting
or disconnecting a Classic Bluetooth A2DP client. The app therefore opens
Windows Bluetooth settings for those user-controlled actions. It does not use
`BluetoothSetServiceState` as a connection command, remove a pairing, restart
Windows audio/Bluetooth services, or guess a MIDI address from the Audio MAC.

On the verified Intel Smart Sound failure, Windows reports the Bluetooth and
A2DP devices as healthy and connected but creates no playback endpoint. The app
then offers **Enable compatibility mode**. After confirmation and UAC, it:

1. validates exactly one healthy Intel Bluetooth Audio offload device and one
   healthy WU-BT10 A2DP device;
2. disables only that Intel offload device;
3. verifies `CM_PROB_DISABLED`; and
4. records the exact device ID so later diagnosis treats the state as
   intentional.

Windows then uses its standard A2DP path. This setting affects Bluetooth audio
offload system-wide and persists across restarts, but normal listening and mode
switching require no administrator rights. **Restore Intel default mode** uses
UAC to re-enable and verify the same recorded device. Ambiguous or changed
device inventories are rejected without mutation.

## Audio use

1. Pair **WU-BT10 AUDIO** in Windows Bluetooth settings.
2. Click **Connect Audio in Windows**, then press **Connect** for WU-BT10 AUDIO.
3. Return to the app. A green status means the playback endpoint is active.
4. Press **Test sound** to send a short, quiet 48 kHz stereo signal directly to
   WU-BT10.
5. Use **Set volume 20%** only if you explicitly want that Windows endpoint
   volume.

If Windows says Connected but the app reports that Intel offload created no
playback output, enable the offered compatibility mode once. In Windows
settings, press **Disconnect** and then **Connect**. The mode remains active
until **Restore Intel default mode** is selected.

The app does not need to stay open for Audio playback.

## MIDI use

1. Install [Windows MIDI Services Runtime and Tools](https://microsoft.github.io/MIDI/get-latest/).
2. Disconnect **WU-BT10 AUDIO** in Windows. The app refuses to start MIDI while
   its playback endpoint is active because Casio warns that simultaneous Audio
   and MIDI can be unstable.
3. Ensure no phone, tablet, browser, or other BLE-MIDI utility owns WU-BT10.
4. Click **Connect WU-BT10 MIDI**.
5. Select **WU-BT10 MIDI Bridge** as both input and output in the MIDI app.

Keep WU-BT10 Windows Fix open while using MIDI. It forwards channel voice,
system-common, real-time, and SysEx messages in both directions. Closing the app
or pressing Disconnect unsubscribes notifications, closes WinMM input/output,
removes the temporary loopback endpoint, and releases BLE ownership.

### Switching modes

- **Audio → MIDI:** disconnect WU-BT10 AUDIO in Windows, then press Connect MIDI.
- **MIDI → Audio:** press Connect Audio in Windows. The app first stops and
  releases MIDI, then opens Windows settings so Audio can be connected.

The verified compatibility mode made repeated Audio reconnects and the complete
Audio → MIDI → Audio transition reliable on the Intel test machine.

## Requirements

- Latest supported 64-bit desktop Windows 11 for the integrated MIDI bridge
  (x64 or ARM64), matching
  [Windows MIDI Services requirements](https://microsoft.github.io/MIDI/kb/minimum-requirements/).
- Windows MIDI Services Runtime and Tools.
- A self-contained published build; no separate .NET installation is required.

Windows MIDI Services still lists Bluetooth MIDI 1.0 transport as after its
initial release. This app supplies the BLE transport and uses Windows MIDI
Services for the temporary loopback endpoint.

## Privacy and local state

- No telemetry, account, updater, service, startup entry, or tray process.
- Network access occurs only when the user opens an external download or
  optional support link.
- Rotating structured diagnostics:
  `%LOCALAPPDATA%\WU-BT10-Fix\diagnostics` (2 MiB per file, 20 files maximum).
  MIDI payload bytes are not logged.
- Repair log: `%LOCALAPPDATA%\WU-BT10-Fix\repair.log`.
- Compatibility-mode record:
  `%LOCALAPPDATA%\WU-BT10-Fix\audio-compatibility-mode.json`. It contains only
  the exact Intel PnP instance ID and activation timestamp.

## Build and test

Install the .NET 9 SDK, then run:

```powershell
dotnet restore WUBT10Fix.slnx
dotnet build WUBT10Fix.slnx -c Release --no-restore
dotnet test WUBT10Fix.slnx -c Release --no-build
dotnet publish src/WUBT10Fix/WUBT10Fix.csproj -c Release -r win-x64 --self-contained true
```

Use `win-arm64` for Windows on ARM. See [docs/SIGNING.md](docs/SIGNING.md) for
Authenticode release requirements.

## License

[MIT](LICENSE)
