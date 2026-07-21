# Security policy

## Supported versions

Only the latest release receives security fixes. Versions earlier than 1.4.0
should not be used because their experimental Audio repair could disable system
audio hardware and fail to restore it.

## Reporting a vulnerability

Use GitHub private vulnerability reporting. Do not open a public issue for a
problem that could put users or their systems at risk.

## Privileged operation

The graphical app runs without elevation for diagnosis, Audio settings, volume,
and MIDI. The emergency action is hidden unless Windows reports an exact Intel
Bluetooth Audio device as already disabled. It requires a separate confirmation
with **No** as the default before UAC can be requested. The helper
may run `pnputil /enable-device` for that exact matching device and verifies that
it is no longer disabled.

The project contains no device-disable operation, Bluetooth profile-state
mutation, audio-service restart, driver installer, service, scheduled task,
startup entry, telemetry client, or updater.

## Release signing

Production releases should be Authenticode-signed and RFC 3161 timestamped via
Azure Artifact Signing. Never commit certificate files, private keys, client
secrets, or payment-provider credentials.
