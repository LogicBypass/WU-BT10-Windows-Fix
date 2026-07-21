# WU-BT10 retained-session hardware test

This protocol is for the `1.4.2-diagnostic.1` build. It observes the existing
1.4.1 connection behavior; it does not contain the proposed connection-lifecycle
fix.

## Evidence to record

Casio uses different sounds for the two Bluetooth functions:

- rising piano melody: Bluetooth Audio connected;
- falling piano melody: Bluetooth Audio disconnected;
- rising vibraphone melody: BLE-MIDI connected; and
- falling vibraphone melody: BLE-MIDI disconnected.

Record the notification sound, Bluetooth light behavior, app status, and whether
Android can find `WU-BT10 MIDI`. The notification sound is stronger evidence than
the light by itself because flashing is also used for Audio pairing standby.

## Safety and recovery

1. Do not run an older 1.3.x build during this test.
2. Do not clear the Audio connection log unless the final recovery step is
   explicitly required; clearing it erases pairing.
3. Recover in this order: app Stop, wait 30 seconds, Windows Bluetooth off/on,
   piano wireless off/on with `FUNCTION + D1`, then restart the piano/adapter.
4. Keep the deliberate simultaneous Audio plus MIDI test to one iteration.

## Test A: clean MIDI release

1. Disconnect `WU-BT10 AUDIO` in Windows Bluetooth settings.
2. Disable Bluetooth temporarily on nearby phones/tablets so they cannot
   reconnect automatically.
3. Do not press the piano's Bluetooth Audio pairing command.
4. Toggle the piano wireless function off and on with `FUNCTION + D1`.
5. Start the diagnostic build and click **Connect WU-BT10 MIDI**.
6. Record whether the rising vibraphone melody occurs.
7. Play at least five notes and confirm the message counter increases.
8. Click **Disconnect WU-BT10 MIDI**.
9. Record whether the falling vibraphone melody occurs.
10. Check the Casio Android app for `WU-BT10 MIDI` after 5, 15, and 30 seconds.
11. Close the Windows app normally.
12. Click **View diagnostic log** before closing if an immediate inspection is
    needed. Otherwise retrieve the latest JSONL file from
    `%LOCALAPPDATA%\WU-BT10-Fix\diagnostics`.

Repeat this test ten times unless Android loses the adapter. Stop immediately on
the first loss and preserve that session log before recovery.

## Test B: shutdown paths

Run one clean connection for each path and test Android visibility afterward:

1. Disconnect button, then close the app.
2. Close the window while MIDI is connected.
3. Turn the piano off while connected, turn it back on, and allow recovery.
4. Put Windows to sleep while connected, resume, and allow recovery.
5. Turn Windows Bluetooth off while connected, turn it back on, and allow
   recovery.

A forced process termination is tested only after the normal paths are recorded,
because it cannot execute the app's cleanup code.

## Test C: exclusive-mode transitions

1. Connect Audio in Windows and confirm the rising piano melody.
2. Disconnect Audio in Windows and confirm the falling piano melody.
3. Connect MIDI and confirm the rising vibraphone melody.
4. Stop MIDI and confirm the falling vibraphone melody.
5. Verify Android visibility, then reconnect Audio.

Run one negative-control iteration with Audio left connected before MIDI. Casio
documents that this combination may make MIDI unstable, so it is not a normal
operating mode.

## Interpretation

- No falling vibraphone plus no Android advertisement points to a retained
  Windows/app GATT connection.
- GATT `Closed` plus a falling vibraphone but no Android advertisement until a
  hardware restart points to an adapter/firmware lock.
- Failures limited to the simultaneous test point to Audio/MIDI coexistence.
- Android sees the adapter but cannot connect points to a connection/cache issue,
  not an advertising failure.
