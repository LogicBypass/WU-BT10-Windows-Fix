# WU-BT10 2.1 hardware acceptance protocol

Record the source commit, build hash, Windows build, Bluetooth driver, audio
driver, Windows MIDI Services version, connection time, final state, and
diagnostic session for every run. Keep full local device IDs out of public
reports.

Casio notification tones:

- rising/falling piano melody: Audio connect/disconnect;
- rising/falling vibraphone melody: BLE-MIDI connect/disconnect.

The indicator is supplementary. This adapter previously exchanged working
Windows MIDI traffic while it continued flashing.

## A. Startup and UI

1. Begin with Audio and MIDI disconnected.
2. Start the app.
3. Confirm no connection notification tone occurs.
4. Confirm no endpoint/transport changes state.
5. Confirm separate Audio and MIDI sections show Connect.
6. Close the app and confirm no temporary MIDI endpoint remains.

## B. Audio section

1. For a first connection, put the piano in Audio pairing standby with
   FUNCTION + C-sharp 1.
2. Press **Connect Audio** once.
3. If Windows shows the mandatory pairing prompt, press **Allow**; confirm the
   app did not open Settings.
4. Confirm rising piano melody.
5. Verify exactly one active playback endpoint, Classic transport connected,
   MIDI stopped, and volume at 20%.
6. Play normal external Audio at a safe source level.
7. Press **Disconnect Audio**.
8. Confirm falling piano melody, endpoint inactive, and transport disconnected.
9. Repeat five times; later connects must reuse pairing without another prompt.

If discovery returns no exact device or multiple exact devices, the app must
fail without service mapping, pairing, or PnP changes. After pairing, an absent
or ambiguous endpoint must also fail safely.

## C. MIDI section

1. Press **Connect MIDI** while Audio is disconnected.
2. Confirm rising vibraphone melody and bridge endpoint creation.
3. Play notes and pedals; verify inbound count and the target app.
4. Send Note On/Off, controller, Program Change, pitch bend, real-time, and a
   safe SysEx message to the piano; verify outbound count.
5. Press **Disconnect MIDI**.
6. Confirm falling vibraphone melody, loopback removal, and advertisement within
   20 seconds.
7. Repeat five times with at least five seconds connected dwell.

## D. Automatic Audio -> MIDI

1. Connect Audio and verify 20%.
2. Press **Connect MIDI** without manually disconnecting Audio.
3. Verify BLE session preparation precedes Audio release.
4. Confirm falling piano then rising vibraphone notification sequence.
5. Verify Audio absent, MIDI connected, and bidirectional traffic.
6. Repeat five times.

## E. Automatic MIDI -> Audio

1. Connect MIDI and exchange traffic.
2. Press **Connect Audio** without manually disconnecting MIDI.
3. Confirm falling vibraphone then rising piano notification sequence.
4. Verify MIDI stopped/removed, advertisement observed before Audio request,
   Audio active, and volume at 20%.
5. Repeat five times.

## F. Failure and recovery

Verify a later normal cycle after each:

1. close the app while MIDI is connected;
2. force process termination, then restart;
3. power-cycle the piano during MIDI automatic recovery;
4. turn the Windows Bluetooth radio off/on;
5. sleep/resume;
6. attempt MIDI while Audio transport is connected but no endpoint exists;
7. attempt Audio while WU-BT10 wireless is off;
8. click both buttons rapidly during a transition.

The app must never accept both modes active, leave a temporary endpoint after a
clean Stop, remove pairing, or leave an Audio/Windows service disabled outside
the explicit recorded compatibility mode.

## Stop conditions

Stop and preserve logs if:

- Audio and MIDI are simultaneously accepted active;
- volume is accepted above 20.5%;
- compatibility mode cannot verify the exact Intel node;
- MIDI does not advertise within the bounded release check;
- Windows MIDI Services stops responding;
- pairing disappears;
- Explorer, the app, or a shell extension crashes;
- a transition continues after the window was closed.
