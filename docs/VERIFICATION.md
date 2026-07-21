# Verification record for 1.4.1 local build

Date: 2026-07-21

- `dotnet build -c Release`: passed, zero warnings and zero errors.
- `dotnet test -c Release`: eight tests passed.
- Forbidden-operation source scan: passed. No device-disable, device-restart,
  Windows service restart, A2DP service-state mutation, guessed MIDI address, or
  legacy Audio-connect action remains.
- x64 self-contained executable: version 1.4.1, diagnosis command passed.
- ARM64 self-contained executable: built successfully; not executable on the
  x64 audit machine.
- Live WU-BT10 BLE-MIDI connection: succeeded three times. The first connection
  took about 18 seconds (which the former two-second retry window would have
  lost); subsequent connections also succeeded.
- Live notification subscription and clean teardown: succeeded.
- Timed note capture: zero raw notification packets were observed. Because no
  raw packet arrived, the test cannot distinguish “no key was played during the
  listening phase” from “the instrument emitted no MIDI.” An attended keypress
  test is still required before claiming end-to-end note forwarding verified.
- Automatic reconnect: implemented and build-verified, but an attended piano
  power-cycle test is still required.
- Audit-machine Audio state: Intel Bluetooth Audio remains
  `CM_PROB_DISABLED` until the user approves the interactive enable-only UAC
  repair. No further Audio mutation was attempted.
- Local executables: `NotSigned`. They are development artifacts, not signed
  public releases. See `LOCAL-BUILD-NOTICE.md` and `docs/SIGNING.md`.
