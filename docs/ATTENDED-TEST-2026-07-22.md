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

## Diagnostic.2 result

The required initial characteristic read completed successfully and returned 20
bytes. The session connected quickly, produced the vibraphone tone, and forwarded
live MIDI traffic. It remained stable for more than seven minutes and received
864 BLE packets containing 1,344 decoded messages. Closing the application
unsubscribed the CCCD successfully, released GATT in 93.5 ms, closed WinMM, and
removed the temporary Windows MIDI Services endpoint.

The WU-BT10 indicator continued flashing. Because subscription and live MIDI
traffic both succeeded, indicator state is recorded as secondary evidence rather
than the functional acceptance criterion.

## Automated GATT lifecycle A/B test

The same instrument was then exercised by an unattended five-cycle
connect/release harness. Each cycle opened BLE-MIDI, performed the initial read,
subscribed to notifications, released the GATT objects, and verified that WU-BT10
resumed advertising. A physical key event was not required because the piano does
not emit continuous traffic until after an input event.

| Windows GATT policy | Passed | Mean connect | Slowest connect | Mean release | Slowest whole cycle |
| --- | ---: | ---: | ---: | ---: | ---: |
| `GattSession.MaintainConnection = true` | 5/5 | 4,285.7 ms | 8,885.0 ms | 28.6 ms | 23,169.3 ms |
| Finite uncached discovery, no maintained session | 5/5 | 3,479.4 ms | 8,431.3 ms | 29.6 ms | 14,116.4 ms |

Both policies restored advertising after every release and left no application
process or virtual endpoint behind. The finite policy is the production default:
it matched observed reliability, reduced cycle latency, and does not ask Windows
to retain indefinite reconnect ownership. The maintained policy remains
available only through the diagnostic harness for future comparisons.

Evidence files:

- `TestResults/production-baseline-cycles-no-traffic.json`
- `TestResults/production-no-maintain-cycles.json`

## Serialized lifecycle regression test

After adding single-instance ownership, run generations, stale-callback guards,
and an explicit stopping state, the finite policy completed 10/10 additional
hardware cycles. Mean GATT connection time was 4,088.4 ms and the slowest was
7,911.9 ms. Every cycle restored advertising. The trace contained no failed,
timed-out, stale, or error event, and the run left zero WU-BT10 virtual endpoints
and zero application processes behind.

Evidence file: `TestResults/lifecycle-generation-cycles.json`.

## Rejected WMS App Virtual Device experiment

The production branch also tested Microsoft's preferred app-owned virtual UMP
device architecture with Windows MIDI Services App SDK `1.0.17-rc.4.25` and a
.NET 10 diagnostic build. Endpoint creation itself was consistently fast (about
127–301 ms), but teardown was not safe on the installed RC runtime:

- `MidiSession.DisconnectEndpointConnection` blocked indefinitely.
- `MidiSession.Dispose` also blocked indefinitely when explicit disconnect was
  omitted.
- Deferring all managed teardown to process exit still left the MIDI service
  unresponsive to its own ping command.
- Once poisoned, the RC service required an administrator-only forced service
  process reset before it accepted another session.

BLE was explicitly unsubscribed and released before every WMS teardown attempt,
so this failure is isolated to the WMS App virtual-device transport. The
experiment is rejected as a production default for this release. The proven
WMS loopback transport remains in use, protected by the new single-instance
lease and serialized lifecycle. App-owned endpoints should be reconsidered when
a stable WMS SDK/runtime is available and passes endpoint-create, clean-exit,
crash-exit, and service-ping regression tests.

## Bidirectional MIDI and parser regression tests

The retained loopback implementation was extended with an exact-name WinMM
input on the application endpoint, an ordered outbound queue, serialized GATT
writes, BLE-MIDI timestamp encoding, and stateful receive parsing. The parser
now preserves running status across packets and handles interleaved real-time
messages and multi-packet SysEx.

Verification results:

- Three hardware connect/release cycles opened and closed the reverse WinMM
  input 3/3 times and started/stopped its outbound pump 3/3 times, with no
  failed or timed-out event.
- A Windows MIDI Services client sent Note On and Note Off through
  `WU-BT10 MIDI Bridge`. Both messages reached the app and produced successful
  WU-BT10 GATT writes: 2 sent, 0 dropped, 0 failed.
- A safe six-byte MIDI Identity Request traversed the long-message WinMM input
  and was encoded into two BLE-MIDI packets with 0 dropped and 0 failed writes.
  The piano did not return an identity response, which is not required for the
  bridge acceptance test.
- The standalone WinMM loopback probe passed short-message and five-byte SysEx
  delivery in both directions: 2/2 app-to-client and 2/2 client-to-app. Both
  long-message buffers completed, both inputs and outputs closed, and the
  endpoint was removed.

Evidence files:

- `TestResults/bidirectional-loopback-cycles.json`
- `TestResults/reverse-midi-live-test.json`
- `TestResults/reverse-sysex-live-test.json`
- `TestResults/winmm-bidirectional-probe-2.json`

## Production dwell and crash recovery

A representative five-cycle run added a five-second connected dwell and allowed
45 seconds for advertising after Stop. It passed 5/5 with advertising restored
on every cycle. A forced-kill test then verified that the next process safely
reused the stale loopback, connected on its first attempt, removed the endpoint,
and left Windows MIDI Services responsive.

Evidence file: `TestResults/production-realistic-5-cycles.json`.

## Audio root cause and compatibility-mode verification

Windows repeatedly reported WU-BT10 connected while exposing no playback
endpoint. Both the WU-BT10 A2DP MEDIA node and Intel Smart Sound Technology for
Bluetooth Audio were present, started, and `CM_PROB_NONE`. Exact PnP restarts,
device scan, and Audio Endpoint Builder restart did not help.

A guarded reproduction of the historical Intel-bypass plus A2DP-mapping rebuild
created an active standard endpoint and restored Intel, but the endpoint became
phantom after disconnect and two later Windows connections never returned it to
active. This rejected the one-shot mapping rebuild as a production solution.

With Intel Bluetooth Audio offload disabled, normal Windows Connect created
Connected audio and an active WU-BT10 endpoint in about four seconds. The
following passed:

- three normal Audio disconnect/reconnect cycles with targeted playback;
- Audio disconnect → finite MIDI connect/stop → advertising restored → Audio
  reconnect → targeted playback;
- product-driven Intel restoration and compatibility-record removal;
- reproduction of the missing endpoint with Intel restored;
- product-driven compatibility enable, exact disabled-state verification,
  persisted record, supported Windows reconnect, and targeted playback; and
- two diagnoses before/after at the unchanged 54% endpoint volume.

The selected production behavior is a persistent, disclosed, reversible Intel
offload compatibility mode. Production source does not retain the A2DP
service-mapping reset.
