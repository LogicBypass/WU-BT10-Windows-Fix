# Attended WU-BT10 test — 2026-07-22

## Environment

- Diagnostic build: `1.4.2-diagnostic.1`
- Source commit: `ff8b30699b8fb7accb6adb8bc671f5f294da4ee0`
- Windows MIDI Services console: `1.0.17-rc.4.25`
- WU-BT10 MIDI address observed by Windows: `[redacted device address]`
- WU-BT10 Audio was disconnected during the Windows MIDI tests.

The correlated JSONL trace is retained in the local diagnostic directory. It is
not committed because it includes machine-specific device identifiers and local
paths.

## Successful Windows behavior

- Three Windows sessions completed GATT service and characteristic discovery,
  subscribed to notifications, and received MIDI packets.
- Connection times were approximately 1.3 to 2.8 seconds after the GATT attempt
  started.
- The piano produced the BLE-MIDI vibraphone connection and disconnection tones.
- During Windows MIDI connections the Bluetooth indicator continued flashing,
  even though MIDI packets and messages were received.
- Every completed session explicitly disabled notifications, set
  `MaintainConnection` to false, disposed its GATT objects, closed WinMM, and
  removed the WMS loopback endpoint.
- The explicit GATT release portions took approximately 78 to 126 milliseconds.
- Android could discover WU-BT10 MIDI after each completed release; no adapter
  restart was required.

## Intermittent Windows failure

One attempted cycle did not complete:

1. Windows received the WU-BT10 advertisement at RSSI -49 dBm.
2. `GattSession` became Active with no reported session error.
3. Thirty seconds later the session changed back to Closed while the uncached
   service query was still pending.
4. The service query completed after 40.4 seconds with
   `GattCommunicationStatus.Unreachable` and no MIDI service.
5. No new advertisement was seen during the remaining retry window, so the
   60-second operation timed out.
6. The adapter later recovered and subsequent attempts connected normally
   without a hardware restart.

This is a connection-establishment defect, not a failed cleanup after a completed
MIDI session.

## Android comparison

After the PC was restarted, the Casio Android app connected BLE-MIDI and made the
Bluetooth indicator steady while WU-BT10 Audio remained disconnected. Therefore
the steady indicator can represent a complete BLE-MIDI connection and is not
explained only by an automatic Audio connection.

Android Open Source Project's BLE-MIDI implementation performs an initial read of
the MIDI characteristic before enabling notifications. It also requests a high
connection priority and a larger MTU. The current Windows bridge skips the
required initial characteristic read and immediately writes the notification
CCCD.

Primary references:

- [Android platform BLE-MIDI implementation](https://android.googlesource.com/platform/frameworks/base/+/master/media/packages/BluetoothMidiService/src/com/android/bluetoothmidiservice/BluetoothMidiDevice.java)
- [Android `MidiManager.openBluetoothDevice`](https://developer.android.com/reference/android/media/midi/MidiManager#openBluetoothDevice(android.bluetooth.BluetoothDevice,%20android.media.midi.MidiManager.OnDeviceOpenedListener,%20android.os.Handler))
- [Microsoft Bluetooth GATT client lifecycle](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client)

## Next controlled experiment

Build `1.4.2-diagnostic.2` with one connection-sequence change: perform and log an
uncached MIDI-characteristic read before installing the notification handler and
enabling the CCCD. Keep `MaintainConnection`, discovery, retry, release, parser,
and WMS behavior unchanged so the LED and reliability result can be attributed
to the initial read.
