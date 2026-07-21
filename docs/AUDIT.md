# Architecture and safety audit (1.4.1)

Date: 2026-07-21

## Findings

### Critical: Audio repair could leave system hardware disabled

The old repair disabled `Intel® Smart Sound Technology for Bluetooth® Audio`,
then performed unrelated A2DP and service resets before trying to enable it
again. A failure between those operations, or an unsuccessful final enable,
left the device in `CM_PROB_DISABLED`. This happened on the audit machine.

Microsoft documents that `BluetoothSetServiceState` installs the mapped device
driver when a service is enabled and removes it when disabled; it is a service
configuration API, not a reliable A2DP connect command:
[BluetoothSetServiceState](https://learn.microsoft.com/en-us/windows/win32/api/bluetoothapis/nf-bluetoothapis-bluetoothsetservicestate).

Resolution: all service-state, disable, device-restart, and audio-service-restart
code and native declarations were removed. Elevated repair is now restricted to
`pnputil /enable-device` for an exact disabled Intel Bluetooth Audio match, with
post-operation verification.

### High: BLE-MIDI raced Windows' lazy unpaired connection

The old implementation created a `BluetoothLEDevice`, set
`GattSession.MaintainConnection`, waited 100 ms, then reused the same device and
session for a handful of rapid GATT queries. It usually abandoned the connection
after roughly two seconds.

Microsoft documents that constructing `BluetoothLEDevice` alone does not
necessarily initiate a connection. `MaintainConnection=true` queues an
indefinite connection with nothing directly awaitable, while uncached GATT
operations have finite but variable timing and cannot currently be canceled:
[FromBluetoothAddressAsync remarks](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothledevice.frombluetoothaddressasync?view=winrt-26100),
[MaintainConnection remarks](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattsession.maintainconnection?view=winrt-26100).

Resolution: connection starts only after a live BLE-MIDI advertisement. Each
failed attempt disposes every app-owned BLE/GATT object, waits for another live
advertisement, and reacquires a fresh device, session, service, characteristic,
and notification subscription. The initial connection window is 60 seconds.

### High: Drops were invisible and unrecoverable

The old bridge never subscribed to `BluetoothLEDevice.ConnectionStatusChanged`.
The UI could therefore claim to be connected while no MIDI flowed.

Microsoft specifies that the event is the notification point for device
connection changes and that handlers should read `ConnectionStatus`:
[ConnectionStatusChanged](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothledevice.connectionstatuschanged?view=winrt-26100).

Resolution: the bridge now marks the link unavailable immediately, preserves
the Windows MIDI port, rediscovers the live peripheral, recreates invalid GATT
handles, and resubscribes automatically until explicitly stopped.

### High: Audio and MIDI operations were uncoordinated

An elevated Audio helper could mutate the shared Bluetooth/audio stack while a
different process retained `MaintainConnection=true` and active GATT objects.

Resolution: all disruptive Audio mutations were removed. If the enable-only
hardware restoration is needed, the UI stops its MIDI bridge first. Opening
Windows Bluetooth settings is non-mutating.

### Medium: Device identity was guessed

The old scanner could return a cached Classic Bluetooth entry or assume that
the BLE-MIDI address was the Audio address plus one. That is not a portable
identity rule and can target a device that is not advertising.

Resolution: cached lookup and address arithmetic were deleted. Only a live
BLE-MIDI advertisement can begin a connection attempt.

## Reference project review

[Maxime-J/BLE-MIDI-Bridge](https://github.com/Maxime-J/BLE-MIDI-Bridge) uses
Chromium Web Bluetooth's service-filtered chooser, an explicit
`device.gatt.connect()`, primary-service and characteristic discovery,
`startNotifications()`, and `ongattserverdisconnected`. Its reliable behavior
supports the same principles: live discovery, a real GATT connect operation,
notification subscription, and explicit disconnect observation.

That project is GPL-3.0. No source code was copied into this MIT project; its
observable connection sequence was compared with Microsoft documentation and
implemented independently with Windows Runtime APIs.

## Windows MIDI Services decision

Windows MIDI Services provides the virtual loopback used by this app, but its
documentation still lists Bluetooth MIDI 1.0 as an after-initial-release/future
transport. Therefore, it cannot yet replace the app's BLE transport:
[Windows MIDI Services overview](https://microsoft.github.io/MIDI/),
[API compatibility matrix](https://microsoft.github.io/MIDI/kb/api-back-compat/).

## Verification completed

- Release build: zero warnings, zero errors.
- Automated tests: eight passed.
- Static safety scan: no legacy device-disable, device-restart, service-restart,
  A2DP service-state, guessed-address, or old Audio-connect action remains.
- Live WU-BT10: BLE advertisement discovery, GATT connection, notification
  subscription, and clean disconnect completed successfully. Raw note packets
  were not observed during the timed captures, so an attended keypress and
  power-cycle recovery test remains required before calling MIDI fully verified.
- Audit machine: Windows still reports Intel Bluetooth Audio disabled until the
  interactive UAC enable-only repair is approved.
