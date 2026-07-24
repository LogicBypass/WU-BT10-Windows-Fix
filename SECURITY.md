# Security policy

## Supported versions

Only the latest release receives security fixes. Versions earlier than 2.0
should not be used because their experimental Audio repair mixed profile,
device, and service mutations.

## Reporting a vulnerability

Use GitHub private vulnerability reporting. Do not open a public issue for a
problem that could put users or their systems at risk.

## Privileged operation

The graphical app runs without elevation for diagnosis, Audio/MIDI switching,
volume enforcement, and MIDI forwarding. The advanced Intel compatibility
transaction requires exact-one healthy Intel and WU-BT10 device preconditions,
a separate confirmation with **No** as the default, and UAC. It may disable only
that exact Intel Bluetooth Audio offload node, verifies the state, and persists
the identity for later restoration. Restore uses UAC to enable and verify that
same recorded node.

Normal switching contains no PnP disable/restart, Bluetooth profile-state
mutation, pairing removal, audio-service restart, driver installer, service,
scheduled task, startup entry, telemetry client, or updater.

## Release signing

Production releases should be Authenticode-signed and RFC 3161 timestamped via
Azure Artifact Signing. Never commit certificate files, private keys, client
secrets, or payment-provider credentials.
