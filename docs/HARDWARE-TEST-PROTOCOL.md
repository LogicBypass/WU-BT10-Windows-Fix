# WU-BT10 2.0 production hardware protocol

Use this protocol for a release candidate on every supported Windows
architecture and Bluetooth/audio-driver family available.

## Evidence

Record build hash, Windows build, Bluetooth driver, audio driver, Windows MIDI
Services version, connection time, app state, and the latest correlated JSONL
session. Do not include full device IDs in public reports.

Casio connection sounds are useful physical evidence:

- rising/falling piano melody: Bluetooth Audio connect/disconnect;
- rising/falling vibraphone melody: BLE-MIDI connect/disconnect.

The WU-BT10 light is supplementary evidence. This unit continued flashing
during verified Windows MIDI traffic, although it became steady with Casio's
Android app.

## A. Standard-user Audio

1. Connect WU-BT10 AUDIO in Windows.
2. Confirm the app reports an active endpoint and its current volume.
3. Run Test sound and confirm Windows completes targeted playback.
4. Confirm the default Windows output and endpoint volume did not change.
5. Disconnect and reconnect Audio three times, running Test sound each time.

If Windows reports Connected without playback and the app offers compatibility
mode, continue with B. Do not run compatibility mode on a different or ambiguous
device inventory.

## B. Compatibility-mode transaction

1. Review the confirmation and ensure **No** is the default.
2. Enable compatibility mode and approve UAC.
3. Confirm exactly one recorded Intel Bluetooth Audio node is
   `CM_PROB_DISABLED`.
4. Disconnect/reconnect WU-BT10 AUDIO in Windows and repeat A.
5. Restart the app and Windows; confirm the mode is still recognized.
6. Select Restore Intel default mode and approve UAC.
7. Confirm Intel is `CM_PROB_NONE` and the compatibility record is removed.
8. Re-enable compatibility mode only if the missing-endpoint failure is
   reproduced and the exact-one preconditions still hold.

## C. Bidirectional MIDI

1. Disconnect Audio in Windows.
2. Start MIDI and record the vibraphone connection tone.
3. Play at least five notes; verify input count and the target MIDI application.
4. Send channel voice and one safe SysEx message from the MIDI application;
   verify output count and BLE write completion.
5. Stop MIDI; verify the disconnection tone and BLE advertising within 45
   seconds.
6. Repeat five times with a five-second connected dwell.

## D. Mode transition

1. Connect Audio and complete Test sound.
2. Press Connect MIDI while Audio is active; verify the app refuses and does not
   acquire BLE.
3. Disconnect Audio, connect MIDI, exchange traffic, then stop MIDI.
4. Press Connect Audio in Windows, reconnect, and complete Test sound.
5. Repeat three times.

## E. Recovery paths

Run one connection for each path and verify later BLE advertising and Audio
reconnect:

1. MIDI Disconnect button;
2. close the app while MIDI is connected;
3. piano power cycle with automatic MIDI recovery;
4. Windows Bluetooth off/on;
5. sleep/resume;
6. forced process termination, then next-process stale-loopback recovery.

## Stop conditions

Stop immediately and preserve logs if:

- compatibility mode cannot verify the exact Intel node;
- Intel cannot be restored after a requested rollback;
- WU-BT10 does not advertise within 45 seconds after a representative Stop;
- Windows MIDI Services stops responding;
- pairing disappears without an explicit user removal; or
- a process crash, unhandled UI exception, or stuck temporary endpoint occurs.
